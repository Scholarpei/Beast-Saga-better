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
function New-TestFile([string] $path, [string] $content) {
    $parent = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent | Out-Null }
    Set-Content -LiteralPath $path -Value $content -NoNewline
}
function Invoke-BackupScript([string] $gameRoot) {
    $scriptCopy = Join-Path $gameRoot 'project/tools/backup-saves.ps1'
    $output = & pwsh.exe -NoProfile -ExecutionPolicy Bypass -File $scriptCopy
    return @{
        Output = $output
        ExitCode = $LASTEXITCODE
        Backup = [string]($output | Where-Object { $_ -like 'BACKUP=*' } | ForEach-Object { $_.Substring(7) })
    }
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("BeastSagaBetter-backup-tests-" + [guid]::NewGuid().ToString('N'))
$gameRoot = $temporaryRoot
New-Item -ItemType Directory -Path (Join-Path $gameRoot 'project/tools'), (Join-Path $gameRoot 'project/verification') | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'backup-saves.ps1') -Destination (Join-Path $gameRoot 'project/tools/backup-saves.ps1')

$expectedFiles = @(
    @{ Root = 'SaveData'; Path = 'save.dat'; Content = 'save-data' }
    @{ Root = 'BepInEx/config'; Path = 'plugin.cfg'; Content = 'plugin-config' }
    @{ Root = 'BeastSagaBetterData'; Path = 'checkpoints/checkpoint.dat'; Content = 'checkpoint' }
    @{ Root = 'BeastSagaBetterData'; Path = 'load-work/item.dat'; Content = 'load-work' }
    @{ Root = 'BeastSagaBetterData'; Path = 'selected/item.dat'; Content = 'selected' }
    @{ Root = 'BeastSagaBetterData'; Path = 'notes/note.txt'; Content = 'notes' }
    @{ Root = 'BeastSagaBetterData'; Path = 'thumbnails/item.png'; Content = 'thumbnail' }
    @{ Root = 'SafeDialogueToolsData'; Path = 'checkpoints/checkpoint.dat'; Content = 'legacy-checkpoint' }
    @{ Root = 'SafeDialogueToolsData'; Path = 'load-work/item.dat'; Content = 'legacy-load-work' }
    @{ Root = 'SafeDialogueToolsData'; Path = 'selected/item.dat'; Content = 'legacy-selected' }
    @{ Root = 'SafeDialogueToolsData'; Path = 'notes/note.txt'; Content = 'legacy-notes' }
    @{ Root = 'SafeDialogueToolsData'; Path = 'thumbnails/item.png'; Content = 'legacy-thumbnail' }
)

foreach ($expected in $expectedFiles) {
    New-TestFile (Join-Path $gameRoot (Join-Path $expected.Root $expected.Path)) $expected.Content
}

try {
    $result = Invoke-BackupScript $temporaryRoot
    Then 'backup completes' {
        $diagnostics = (($result.Output | Select-Object -First 8) -join [Environment]::NewLine)
        Require ($result.ExitCode -eq 0) $diagnostics
        Require (-not [string]::IsNullOrWhiteSpace($result.Backup)) "BACKUP output missing: $diagnostics"
        Require (Test-Path -LiteralPath $result.Backup -PathType Container) "BACKUP path missing: $($result.Backup)"
    }

    $manifest = @()
    if (Test-Path -LiteralPath (Join-Path $result.Backup 'manifest.json')) {
        $manifest = @((Get-Content -LiteralPath (Join-Path $result.Backup 'manifest.json') -Raw | ConvertFrom-Json))
    }
    Then 'one manifest strictly covers every required root' {
        Require ($manifest.Count -eq $expectedFiles.Count) "manifest has $($manifest.Count) entries; expected $($expectedFiles.Count)"
        foreach ($expected in $expectedFiles) {
            $entry = $manifest | Where-Object { $_.Root -eq $expected.Root -and $_.Path.Replace('\', '/') -eq $expected.Path }
            Require (@($entry).Count -eq 1) "manifest missing $($expected.Root)/$($expected.Path)"
            $sourcePath = Join-Path $gameRoot (Join-Path $expected.Root $expected.Path)
            Require ($entry.Length -eq (Get-Item -LiteralPath $sourcePath).Length) "length mismatch: $($expected.Root)/$($expected.Path)"
            Require ($entry.SHA256 -eq (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash) "hash mismatch: $($expected.Root)/$($expected.Path)"
        }
    }

    Then 'all required nested files are copied with unchanged bytes' {
        foreach ($expected in $expectedFiles) {
            $backupRelative = if ($expected.Root -eq 'BepInEx/config') { 'config' } else { $expected.Root }
            $backupPath = Join-Path $result.Backup (Join-Path $backupRelative $expected.Path)
            Require (Test-Path -LiteralPath $backupPath) "backup missing $($expected.Root)/$($expected.Path)"
            Require ((Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath (Join-Path $gameRoot (Join-Path $expected.Root $expected.Path)) -Algorithm SHA256).Hash) "backup hash mismatch: $($expected.Root)/$($expected.Path)"
        }
    }

    Then 'verification marker reports every verified file' {
        $marker = Join-Path $result.Backup 'VERIFIED'
        Require (Test-Path -LiteralPath $marker) 'VERIFIED marker missing'
        Require ((Get-Content -LiteralPath $marker -Raw) -like "*$($expectedFiles.Count) files*") (Get-Content -LiteralPath $marker -Raw)
    }

    Remove-Item -LiteralPath (Join-Path $gameRoot 'SafeDialogueToolsData') -Recurse -Force
    $missingRootResult = Invoke-BackupScript $temporaryRoot
    $missingRootManifest = @()
    if (Test-Path -LiteralPath (Join-Path $missingRootResult.Backup 'manifest.json')) {
        $missingRootManifest = @((Get-Content -LiteralPath (Join-Path $missingRootResult.Backup 'manifest.json') -Raw | ConvertFrom-Json))
    }
    Then 'a missing historical data root is skipped' {
        Require ($missingRootResult.ExitCode -eq 0) (($missingRootResult.Output | Select-Object -First 5) -join [Environment]::NewLine)
        $expectedWithoutHistoricalRoot = @($expectedFiles | Where-Object { $_.Root -ne 'SafeDialogueToolsData' })
        Require ($missingRootManifest.Count -eq $expectedWithoutHistoricalRoot.Count) "manifest has $($missingRootManifest.Count) entries; expected $($expectedWithoutHistoricalRoot.Count)"
        Require (-not (Test-Path -LiteralPath (Join-Path $missingRootResult.Backup 'SafeDialogueToolsData'))) 'missing root was created in backup'
    }
} finally {
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
}

$passed = $script:checks - $script:failures
Write-Output "RESULT $passed/$script:checks passed"
if ($script:failures -ne 0) { exit 1 }
exit 0
