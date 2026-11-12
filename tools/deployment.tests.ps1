$ErrorActionPreference = 'Stop'

$script:checks = 0
$script:failures = 0
function Then([string] $name, [scriptblock] $action) {
    $script:checks++
    try {
        & $action
        Write-Output "PASS $name"
    } catch {
        $script:failures++
        Write-Output "FAIL ${name}: $($_.Exception.Message)"
        Write-Output $_.InvocationInfo.PositionMessage
    }
}
function Require([bool] $condition, [string] $message = 'Assertion failed') {
    if (-not $condition) { throw $message }
}
function Test-NameIn([object[]] $items, [string] $name) {
    foreach ($item in $items) {
        if ([string]::Equals($item, $name, [StringComparison]::Ordinal)) { return $true }
    }
    return $false
}
function Assert-Throws([string] $expected, [scriptblock] $action) {
    try {
        & $action
    } catch {
        if ($_.Exception.Message -notlike "*$expected*") {
            throw "Expected error containing '$expected', got '$($_.Exception.Message)'"
        }
        return
    }
    throw 'Expected an error, but the operation succeeded'
}

. (Join-Path $PSScriptRoot 'Deployment.ps1')

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("BeastSagaBetter-deployment-tests-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    $projectRoot = Join-Path $temporaryRoot 'project'
    $gameRoot = Join-Path $temporaryRoot 'game'
    $pluginRoot = Join-Path $gameRoot 'BepInEx/plugins'
    New-Item -ItemType Directory -Path (Join-Path $projectRoot 'Plugin/bin'), (Join-Path $projectRoot 'Core'), $pluginRoot | Out-Null
    Set-Content -LiteralPath (Join-Path $projectRoot 'Plugin/Plugin.cs') -Value 'plugin'
    Set-Content -LiteralPath (Join-Path $projectRoot 'Plugin/BeastSagaBetter.csproj') -Value 'project'
    Set-Content -LiteralPath (Join-Path $projectRoot 'Core/Core.cs') -Value 'core'
    Set-Content -LiteralPath (Join-Path $projectRoot 'Core/BeastSagaBetter.Core.csproj') -Value 'core-project'
    Set-Content -LiteralPath (Join-Path $projectRoot 'Plugin/bin/generated.cs') -Value 'generated'
    $artifact = Join-Path $projectRoot 'Plugin/bin/Release/net6.0/BeastSagaBetter.dll'
    New-Item -ItemType Directory -Path (Split-Path -Parent $artifact) | Out-Null
    Set-Content -LiteralPath $artifact -Value 'artifact'

    Then 'deployment freshness includes Plugin and Core source and project files' {
        $names = @(Get-DeploymentInputFiles -ProjectRoot $projectRoot | ForEach-Object { $_.Name })
        $hasRequiredInputs = ($names -contains 'Plugin.cs' -and $names -contains 'BeastSagaBetter.csproj' -and
            $names -contains 'Core.cs' -and $names -contains 'BeastSagaBetter.Core.csproj')
        Require $hasRequiredInputs "inputs: $($names -join ', ')"
        Require (-not ($names -contains 'generated.cs')) 'bin inputs must be excluded'
    }
    Then 'freshness accepts an artifact newer than every build input' {
        $result = Test-DeploymentInputsFresh -ProjectRoot $projectRoot -ArtifactPath $artifact
        Require $result.Fresh $result.Reason
    }
    Then 'freshness rejects a project file newer than the artifact' {
        $newerProject = Join-Path $projectRoot 'Core/BeastSagaBetter.Core.csproj'
        (Get-Item -LiteralPath $newerProject).LastWriteTime = (Get-Item -LiteralPath $artifact).LastWriteTime.AddSeconds(2)
        $result = Test-DeploymentInputsFresh -ProjectRoot $projectRoot -ArtifactPath $artifact
        Require (-not $result.Fresh) 'A stale artifact was accepted'
        Require ($result.Reason -like '*BeastSagaBetter.Core.csproj*') $result.Reason
    }

    Then 'a unique legacy plugin directory is planned for migration' {
        $legacy = Join-Path $pluginRoot 'SafeDialogueTools'
        New-Item -ItemType Directory -Path $legacy | Out-Null
        Set-Content -LiteralPath (Join-Path $legacy 'SafeDialogueTools.dll') -Value 'legacy-plugin'
        $plan = New-DeploymentPlan -ProjectRoot $projectRoot -GameDir $gameRoot
        Require ($plan.PluginDir -eq (Join-Path $pluginRoot 'BeastSagaBetter')) $plan.PluginDir
        Require ($plan.LegacyPluginDir -eq $legacy) $plan.LegacyPluginDir
        Require $plan.RequiresMigration 'Migration was not requested'
        Remove-Item -LiteralPath $legacy -Recurse -Force
    }
    Then 'coexisting new and legacy plugin directories are rejected' {
        New-Item -ItemType Directory -Path (Join-Path $pluginRoot 'BeastSagaBetter'), (Join-Path $pluginRoot 'SafeDialogueTools') | Out-Null
        Assert-Throws '不能同时存在' { New-DeploymentPlan -ProjectRoot $projectRoot -GameDir $gameRoot }
        Remove-Item -LiteralPath (Join-Path $pluginRoot 'BeastSagaBetter'), (Join-Path $pluginRoot 'SafeDialogueTools') -Recurse -Force
    }
    Then 'an explicit plugin directory is honored without auto-discovery' {
        $explicit = Join-Path $temporaryRoot 'explicit-plugin'
        $plan = New-DeploymentPlan -ProjectRoot $projectRoot -GameDir $gameRoot -PluginDir $explicit
        Require ($plan.PluginDir -eq $explicit) $plan.PluginDir
        Require (-not $plan.RequiresMigration) 'Explicit target should not require default-directory migration'
        Require $plan.AllowCreate 'An explicit nonexistent plugin directory must be created'
    }

    Then 'a legacy directory backup preserves nested content and hashes' {
        $source = Join-Path $temporaryRoot 'legacy-backup-source'
        $backup = Join-Path $temporaryRoot 'legacy-backup'
        New-Item -ItemType Directory -Path (Join-Path $source 'nested') | Out-Null
        Set-Content -LiteralPath (Join-Path $source 'SafeDialogueTools.dll') -Value 'old-plugin'
        Set-Content -LiteralPath (Join-Path $source 'nested/metadata.txt') -Value 'metadata'
        $result = Backup-DirectoryTree -SourceDirectory $source -BackupDirectory $backup
        Require ($result.FileCount -eq 2) "file count $($result.FileCount)"
        Require (Test-Path -LiteralPath (Join-Path $backup 'nested/metadata.txt')) 'nested backup missing'
        Require ($result.Hashes.Count -eq 2) 'hash count mismatch'
    }
    Then 'verified temporary copies stay in the destination directory and match the source' {
        $destination = Join-Path $temporaryRoot 'destination'
        New-Item -ItemType Directory -Path $destination | Out-Null
        $source = Join-Path $temporaryRoot 'source.dll'
        Set-Content -LiteralPath $source -Value 'source-bytes'
        $copy = New-VerifiedTemporaryCopy -SourceFile $source -DestinationDirectory $destination
        try {
            Require ((Split-Path -Parent $copy.Path) -eq $destination) $copy.Path
            Require ($copy.SHA256 -eq (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash) 'hash mismatch'
        } finally {
            Remove-Item -LiteralPath $copy.Path -Force
        }
    }
    Then 'dist cleanup removes only the legacy plugin artifacts' {
        $dist = Join-Path $temporaryRoot 'dist'
        New-Item -ItemType Directory -Path $dist | Out-Null
        Set-Content -LiteralPath (Join-Path $dist 'SafeDialogueTools.dll') -Value 'legacy-dist'
        Set-Content -LiteralPath (Join-Path $dist 'SafeDialogueTools.pdb') -Value 'legacy-symbols'
        Set-Content -LiteralPath (Join-Path $dist 'unrelated.txt') -Value 'retain'
        $removed = Remove-LegacyDistArtifacts -DistDirectory $dist
        $legacyFilesRemoved = (-not (Test-Path -LiteralPath (Join-Path $dist 'SafeDialogueTools.dll')) -and
            -not (Test-Path -LiteralPath (Join-Path $dist 'SafeDialogueTools.pdb')))
        Require $legacyFilesRemoved 'legacy dist artifacts remain'
        Require (Test-Path -LiteralPath (Join-Path $dist 'unrelated.txt')) 'unrelated dist file was removed'
        Require ($removed.Count -eq 2) "removed $($removed.Count)"
    }
    Then 'verified publication atomically replaces the destination with the temporary copy' {
        $publicationRoot = Join-Path $temporaryRoot 'publication'
        New-Item -ItemType Directory -Path $publicationRoot | Out-Null
        $target = Join-Path $publicationRoot 'target.dll'
        $source = Join-Path $publicationRoot 'source.dll'
        Set-Content -LiteralPath $target -Value 'old-target'
        Set-Content -LiteralPath $source -Value 'new-target'
        $copy = New-VerifiedTemporaryCopy -SourceFile $source -DestinationDirectory $publicationRoot
        try {
            Publish-VerifiedFile -TemporaryFile $copy.Path -DestinationFile $target -ExpectedHash $copy.SHA256
            $publishedContent = (Get-Content -LiteralPath $target -Raw).TrimEnd()
            Require ($publishedContent -eq 'new-target') "target was not replaced: $publishedContent"
            Require (-not (Test-Path -LiteralPath $copy.Path)) 'temporary file was not consumed'
        } finally {
            if (Test-Path -LiteralPath $copy.Path) { Remove-Item -LiteralPath $copy.Path -Force }
        }
    }
    Then 'disabling a legacy plugin directory moves all old contents into a hidden subdirectory' {
        $legacy = Join-Path $temporaryRoot 'disable-legacy'
        New-Item -ItemType Directory -Path (Join-Path $legacy 'nested') | Out-Null
        Set-Content -LiteralPath (Join-Path $legacy 'SafeDialogueTools.dll') -Value 'legacy-plugin'
        Set-Content -LiteralPath (Join-Path $legacy 'nested/metadata.txt') -Value 'metadata'
        Disable-DirectoryTree -Directory $legacy
        Require (Test-Path -LiteralPath (Join-Path $legacy '.disabled/SafeDialogueTools.dll')) 'legacy DLL was not disabled'
        Require (Test-Path -LiteralPath (Join-Path $legacy '.disabled/nested/metadata.txt')) 'nested content was not disabled'
        $topLevelNames = @(Get-ChildItem -LiteralPath $legacy -Force | ForEach-Object { $_.Name })
        Require ($topLevelNames.Count -eq 1 -and $topLevelNames[0] -eq '.disabled') "top-level names: $($topLevelNames -join ', ')"
    }
    Then 'a disabled legacy plugin directory can be restored after deployment failure' {
        $legacy = Join-Path $temporaryRoot 'restore-legacy'
        New-Item -ItemType Directory -Path $legacy | Out-Null
        Set-Content -LiteralPath (Join-Path $legacy 'SafeDialogueTools.dll') -Value 'legacy-plugin'
        Disable-DirectoryTree -Directory $legacy
        Restore-DisabledDirectoryTree -Directory $legacy
        Require (Test-Path -LiteralPath (Join-Path $legacy 'SafeDialogueTools.dll')) 'legacy DLL was not restored'
        Require (-not (Test-Path -LiteralPath (Join-Path $legacy '.disabled'))) 'disabled directory was not removed'
    }
} finally {
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
}

$passed = $script:checks - $script:failures
Write-Output "RESULT $passed/$script:checks passed"
if ($script:failures -ne 0) { exit 1 }
exit 0
