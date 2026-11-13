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

function Remove-TestDirectory([string[]] $Path) {
    foreach ($directory in $Path) {
        $absolute = [IO.Path]::GetFullPath($directory)
        $testRoot = [IO.Path]::GetFullPath($temporaryRoot)
        Require ([IO.Path]::GetFileName($testRoot) -like 'BeastSagaBetter-deployment-tests-*') 'Unexpected test root'
        $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
        Require ($testRoot.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) 'Test root escaped TEMP'
        Require ($absolute -eq $testRoot -or $absolute.StartsWith($testRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) 'Cleanup escaped test root'
        Remove-Item -LiteralPath $absolute -Recurse -Force
    }
}

# Execute the real entry point in an isolated, disposable fake installation.
function New-DeploymentFixture([string] $name, [switch] $ExistingTarget, [switch] $Legacy) {
    $root = Join-Path $temporaryRoot $name
    $project = Join-Path $root 'project'
    $game = Join-Path $root 'game'
    $target = Join-Path $game 'BepInEx/plugins/BeastSagaBetter/BeastSagaBetter.dll'
    $legacyDir = Join-Path $game 'BepInEx/plugins/SafeDialogueTools'
    $dist = Join-Path $project 'dist'
    New-Item -ItemType Directory -Path (Join-Path $project 'tools'), (Join-Path $project 'Core'),
        (Join-Path $project 'Plugin/bin/Release/net6.0'), $dist | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Deployment.ps1'), (Join-Path $PSScriptRoot 'deploy-production.ps1') -Destination (Join-Path $project 'tools')
    Set-Content -LiteralPath (Join-Path $project 'Core/Input.cs') -Value 'input'
    Set-Content -LiteralPath (Join-Path $project 'Plugin/bin/Release/net6.0/BeastSagaBetter.dll') -Value 'new-plugin'
    Set-Content -LiteralPath (Join-Path $dist 'BeastSagaBetter.dll') -Value 'old-dist'
    Set-Content -LiteralPath (Join-Path $dist 'SafeDialogueTools.dll') -Value 'legacy-dist'
    Set-Content -LiteralPath (Join-Path $dist 'SafeDialogueTools.pdb') -Value 'legacy-symbols'
    if ($ExistingTarget) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Set-Content -LiteralPath $target -Value 'old-target'
    }
    if ($Legacy) {
        New-Item -ItemType Directory -Path $legacyDir -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $legacyDir 'SafeDialogueTools.dll') -Value 'old-legacy'
    }
    # Process discovery is the only external boundary; never inspect/control a real game.
    Add-Content -LiteralPath (Join-Path $project 'tools/Deployment.ps1') -Value 'function Get-Process { param($Name, $ErrorAction) }'
    [pscustomobject]@{ Project = $project; Game = $game; Target = $target; Legacy = $legacyDir; Dist = $dist }
}
function Invoke-FixtureDeployment($fixture) {
    & (Join-Path $fixture.Project 'tools/deploy-production.ps1') -SkipTests -SkipBuild -GameDir $fixture.Game | Out-Null
}
function Require-FileText([string] $path, [string] $text) {
    Require (Test-Path -LiteralPath $path -PathType Leaf) "Missing file: $path"
    Require ((Get-Content -LiteralPath $path -Raw).TrimEnd() -eq $text) "Unexpected content: $path"
}
function Require-OldDist($fixture) {
    Require-FileText (Join-Path $fixture.Dist 'BeastSagaBetter.dll') 'old-dist'
    Require-FileText (Join-Path $fixture.Dist 'SafeDialogueTools.dll') 'legacy-dist'
    Require-FileText (Join-Path $fixture.Dist 'SafeDialogueTools.pdb') 'legacy-symbols'
}

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
        Remove-TestDirectory $legacy
    }
    Then 'coexisting new and legacy plugin directories are rejected' {
        New-Item -ItemType Directory -Path (Join-Path $pluginRoot 'BeastSagaBetter'), (Join-Path $pluginRoot 'SafeDialogueTools') | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $pluginRoot 'SafeDialogueTools/nested') | Out-Null
        $hiddenLegacyDll = Join-Path $pluginRoot 'SafeDialogueTools/nested/HiddenDependency.dll'
        Set-Content -LiteralPath $hiddenLegacyDll -Value 'hidden-legacy-plugin'
        attrib.exe +h $hiddenLegacyDll
        Assert-Throws '不能同时存在' { New-DeploymentPlan -ProjectRoot $projectRoot -GameDir $gameRoot }
        Assert-Throws '不能同时存在' { New-DeploymentPlan -ProjectRoot $projectRoot -GameDir $gameRoot -PluginDir (Join-Path $temporaryRoot 'explicit-with-coexistence') }
        Remove-TestDirectory @((Join-Path $pluginRoot 'BeastSagaBetter'), (Join-Path $pluginRoot 'SafeDialogueTools'))
    }
    Then 'an already disabled legacy directory does not require another migration' {
        $legacy = Join-Path $pluginRoot 'SafeDialogueTools'
        $newPlugin = Join-Path $pluginRoot 'BeastSagaBetter'
        New-Item -ItemType Directory -Path (Join-Path $legacy '.disabled'), $newPlugin | Out-Null
        Set-Content -LiteralPath (Join-Path $legacy '.disabled/SafeDialogueTools.dll.disabled') -Value 'legacy-plugin'
        $plan = New-DeploymentPlan -ProjectRoot $projectRoot -GameDir $gameRoot
        Require ($plan.PluginDir -eq $newPlugin) $plan.PluginDir
        Require ($null -eq $plan.LegacyPluginDir) 'Inactive legacy directory was exposed as active'
        Require (-not $plan.RequiresMigration) 'Inactive legacy directory requested migration'
        Remove-TestDirectory @($legacy, $newPlugin)
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
    Then 'a legacy directory backup handles a single-file plugin' {
        $source = Join-Path $temporaryRoot 'legacy-single-file-source'
        $backup = Join-Path $temporaryRoot 'legacy-single-file-backup'
        New-Item -ItemType Directory -Path $source | Out-Null
        Set-Content -LiteralPath (Join-Path $source 'SafeDialogueTools.dll') -Value 'old-plugin'
        $result = Backup-DirectoryTree -SourceDirectory $source -BackupDirectory $backup
        Require ($result.FileCount -eq 1) "file count $($result.FileCount)"
        Require ($result.Hashes.Count -eq 1) "hash count $($result.Hashes.Count)"
    }
    Then 'backup includes hidden DLLs and files inside nested hidden directories' {
        $source = Join-Path $temporaryRoot 'hidden-source'
        $backup = Join-Path $temporaryRoot 'hidden-backup'
        New-Item -ItemType Directory -Path (Join-Path $source 'hidden/nested') | Out-Null
        Set-Content -LiteralPath (Join-Path $source 'Hidden.dll') -Value 'hidden-root'
        Set-Content -LiteralPath (Join-Path $source 'hidden/nested/Dependency.dll') -Value 'hidden-nested'
        attrib.exe +h (Join-Path $source 'Hidden.dll')
        attrib.exe +h (Join-Path $source 'hidden')
        $result = Backup-DirectoryTree -SourceDirectory $source -BackupDirectory $backup
        Require ($result.FileCount -eq 2) "file count $($result.FileCount)"
        Require ($result.Hashes.Count -eq 2) "hash count $($result.Hashes.Count)"
        Require-FileText (Join-Path $backup 'Hidden.dll') 'hidden-root'
        Require-FileText (Join-Path $backup 'hidden/nested/Dependency.dll') 'hidden-nested'
    }
    Then 'migration refusal leaves only the original active assembly and original dist' {
        $fixture = New-DeploymentFixture 'migration-refusal' -Legacy
        New-Item -ItemType Directory -Path (Join-Path $fixture.Legacy '.disabled') | Out-Null
        Set-Content -LiteralPath (Join-Path $fixture.Legacy '.disabled/evidence.txt') -Value 'retain-evidence'
        Assert-Throws '禁用目录已存在' { Invoke-FixtureDeployment $fixture }
        Require (-not (Test-Path -LiteralPath $fixture.Target)) 'new target remained active after refusal'
        Require-FileText (Join-Path $fixture.Legacy 'SafeDialogueTools.dll') 'old-legacy'
        Require-FileText (Join-Path $fixture.Legacy '.disabled/evidence.txt') 'retain-evidence'
        Require-OldDist $fixture
    }
    Then 'partial migration failure restores original names and original active assembly' {
        $fixture = New-DeploymentFixture 'migration-partial' -Legacy
        New-Item -ItemType Directory -Path (Join-Path $fixture.Legacy 'nested') | Out-Null
        Set-Content -LiteralPath (Join-Path $fixture.Legacy 'nested/a.dll') -Value 'first-renamed'
        Set-Content -LiteralPath (Join-Path $fixture.Legacy 'nested/dependency.dll') -Value 'dependency'
        Set-Content -LiteralPath (Join-Path $fixture.Legacy 'nested/dependency.dll.disabled') -Value 'preexisting-disabled'
        Assert-Throws '禁用目标已存在' { Invoke-FixtureDeployment $fixture }
        Require (-not (Test-Path -LiteralPath $fixture.Target)) 'new target remained after partial migration'
        Require-FileText (Join-Path $fixture.Legacy 'SafeDialogueTools.dll') 'old-legacy'
        Require-FileText (Join-Path $fixture.Legacy 'nested/a.dll') 'first-renamed'
        Require-FileText (Join-Path $fixture.Legacy 'nested/dependency.dll') 'dependency'
        Require-FileText (Join-Path $fixture.Legacy 'nested/dependency.dll.disabled') 'preexisting-disabled'
        Require (-not (Test-Path -LiteralPath (Join-Path $fixture.Legacy '.disabled'))) 'partial migration directory remained'
        Require-OldDist $fixture
    }
    Then 'publication verification failure removes a newly created target and restores dist' {
        $fixture = New-DeploymentFixture 'publication-failure'
        Add-Content -LiteralPath (Join-Path $fixture.Project 'tools/Deployment.ps1') -Value @'
$script:originalPublish = ${function:Publish-VerifiedFile}
$script:publicationFaultRaised = $false
function Publish-VerifiedFile {
    param($TemporaryFile, $DestinationFile, $ExpectedHash)
    & $script:originalPublish @PSBoundParameters
    if ($DestinationFile -like '*game*BeastSagaBetter.dll' -and -not $script:publicationFaultRaised) {
        $script:publicationFaultRaised = $true
        throw 'injected post-publication verification failure'
    }
}
'@
        Assert-Throws 'injected post-publication verification failure' { Invoke-FixtureDeployment $fixture }
        Require (-not (Test-Path -LiteralPath $fixture.Target)) 'new target survived publication failure'
        Require-OldDist $fixture
    }
    Then 'record failure after migration restores the original legacy directory' {
        $fixture = New-DeploymentFixture 'migration-record-failure' -Legacy
        Add-Content -LiteralPath (Join-Path $fixture.Project 'tools/Deployment.ps1') -Value @'
function Set-Content {
    param($LiteralPath, $Value, $Encoding)
    if ($LiteralPath -like '*production-deployment-*.json') { throw 'injected record write failure' }
    Microsoft.PowerShell.Management\Set-Content @PSBoundParameters
}
'@
        Assert-Throws 'injected record write failure' { Invoke-FixtureDeployment $fixture }
        Require (-not (Test-Path -LiteralPath $fixture.Target)) 'new target survived record failure'
        Require-FileText (Join-Path $fixture.Legacy 'SafeDialogueTools.dll') 'old-legacy'
        Require (-not (Test-Path -LiteralPath (Join-Path $fixture.Legacy '.disabled'))) 'legacy remained disabled'
        Require-OldDist $fixture
    }
    foreach ($existing in @($true, $false)) {
        Then "record failure restores original publication state (existing target: $existing)" {
            $fixture = New-DeploymentFixture "record-failure-$existing" -ExistingTarget:$existing
            Add-Content -LiteralPath (Join-Path $fixture.Project 'tools/Deployment.ps1') -Value @'
function Set-Content {
    param($LiteralPath, $Value, $Encoding)
    if ($LiteralPath -like '*production-deployment-*.json') { throw 'injected record write failure' }
    Microsoft.PowerShell.Management\Set-Content @PSBoundParameters
}
'@
            Assert-Throws 'injected record write failure' { Invoke-FixtureDeployment $fixture }
            if ($existing) { Require-FileText $fixture.Target 'old-target' }
            else { Require (-not (Test-Path -LiteralPath $fixture.Target)) 'new target survived record failure' }
            Require-OldDist $fixture
        }
    }
    Then 'a clean clone creates its verification directory and records a complete migration' {
        $fixture = New-DeploymentFixture 'clean-clone' -Legacy
        Invoke-FixtureDeployment $fixture
        Require-FileText $fixture.Target 'new-plugin'
        Require-FileText (Join-Path $fixture.Legacy '.disabled/SafeDialogueTools.dll.disabled') 'old-legacy'
        $records = @(Get-ChildItem -LiteralPath (Join-Path $fixture.Project 'verification') -File -Filter 'production-deployment-*.json')
        Require ($records.Count -eq 1) "record count $($records.Count)"
        $record = Get-Content -LiteralPath $records[0].FullName -Raw | ConvertFrom-Json
        Require $record.legacyPluginDisabled 'migration record is missing'
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
        Set-Content -LiteralPath (Join-Path $legacy 'nested/dependency.dll') -Value 'dependency'
        Set-Content -LiteralPath (Join-Path $legacy 'nested/Dependency.DLL') -Value 'uppercase-dependency'
        $hiddenDependency = Join-Path $legacy 'nested/HiddenDependency.dll'
        Set-Content -LiteralPath $hiddenDependency -Value 'hidden-dependency'
        attrib.exe +h $hiddenDependency
        Set-Content -LiteralPath (Join-Path $legacy 'nested/metadata.txt') -Value 'metadata'
        Disable-DirectoryTree -Directory $legacy
        Require (Test-Path -LiteralPath (Join-Path $legacy '.disabled/SafeDialogueTools.dll.disabled')) 'legacy DLL was not disabled'
        Require (Test-Path -LiteralPath (Join-Path $legacy '.disabled/nested/dependency.dll.disabled')) 'nested DLL was not disabled'
        Require (Test-Path -LiteralPath (Join-Path $legacy '.disabled/nested/Dependency.DLL.disabled')) 'uppercase nested DLL was not disabled'
        Require (Test-Path -LiteralPath (Join-Path $legacy '.disabled/nested/HiddenDependency.dll.disabled')) 'hidden nested DLL was not disabled'
        Require (Test-Path -LiteralPath (Join-Path $legacy '.disabled/nested/metadata.txt')) 'nested content was not disabled'
        Require (-not (Test-Path -LiteralPath (Join-Path $legacy '.disabled/SafeDialogueTools.dll'))) 'legacy DLL remained loadable'
        Require (-not (Test-Path -LiteralPath (Join-Path $legacy '.disabled/nested/dependency.dll'))) 'nested DLL remained loadable'
        Require (-not (Test-Path -LiteralPath (Join-Path $legacy '.disabled/nested/Dependency.DLL'))) 'uppercase nested DLL remained loadable'
        Require (-not (Test-Path -LiteralPath (Join-Path $legacy '.disabled/nested/HiddenDependency.dll'))) 'hidden nested DLL remained loadable'
        $topLevelNames = @(Get-ChildItem -LiteralPath $legacy -Force | ForEach-Object { $_.Name })
        Require ($topLevelNames.Count -eq 1 -and $topLevelNames[0] -eq '.disabled') "top-level names: $($topLevelNames -join ', ')"
    }
    Then 'a disabled legacy plugin directory can be restored after deployment failure' {
        $legacy = Join-Path $temporaryRoot 'restore-legacy'
        New-Item -ItemType Directory -Path $legacy | Out-Null
        Set-Content -LiteralPath (Join-Path $legacy 'SafeDialogueTools.dll') -Value 'legacy-plugin'
        New-Item -ItemType Directory -Path (Join-Path $legacy 'nested') | Out-Null
        Set-Content -LiteralPath (Join-Path $legacy 'nested/Dependency.DLL') -Value 'uppercase-dependency'
        $hiddenDependency = Join-Path $legacy 'nested/HiddenDependency.dll'
        Set-Content -LiteralPath $hiddenDependency -Value 'hidden-dependency'
        attrib.exe +h $hiddenDependency
        Disable-DirectoryTree -Directory $legacy
        Restore-DisabledDirectoryTree -Directory $legacy
        Require (Test-Path -LiteralPath (Join-Path $legacy 'SafeDialogueTools.dll')) 'legacy DLL was not restored'
        Require (Test-Path -LiteralPath (Join-Path $legacy 'nested/Dependency.DLL')) 'uppercase nested DLL was not restored'
        Require (Test-Path -LiteralPath (Join-Path $legacy 'nested/HiddenDependency.dll')) 'hidden nested DLL was not restored'
        Require (-not (Test-Path -LiteralPath (Join-Path $legacy 'nested/Dependency.DLL.disabled'))) 'uppercase nested DLL disabled suffix was not stripped'
        Require (-not (Test-Path -LiteralPath (Join-Path $legacy 'nested/HiddenDependency.dll.disabled'))) 'hidden nested DLL disabled suffix was not stripped'
        Require (-not (Test-Path -LiteralPath (Join-Path $legacy 'SafeDialogueTools.dll.disabled'))) 'disabled suffix was not stripped'
        Require (-not (Test-Path -LiteralPath (Join-Path $legacy '.disabled'))) 'disabled directory was not removed'
    }
} finally {
    Remove-TestDirectory $temporaryRoot
}

$passed = $script:checks - $script:failures
Write-Output "RESULT $passed/$script:checks passed"
if ($script:failures -ne 0) { exit 1 }
exit 0
