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
function Remove-TestDirectory([string] $path) {
    $absolutePath = [IO.Path]::GetFullPath($path)
    $absoluteRoot = [IO.Path]::GetFullPath($temporaryRoot)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    Require ([IO.Path]::GetFileName($absoluteRoot) -like 'BeastSagaBetter-backup-tests-*') 'Unexpected test root'
    Require ($absoluteRoot.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) 'Test root escaped TEMP'
    Require ($absolutePath -eq $absoluteRoot -or $absolutePath.StartsWith($absoluteRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) 'Cleanup escaped test root'
    Remove-Item -LiteralPath $absolutePath -Recurse -Force
}
function Get-TestTreeState([string] $root) {
    Get-ChildItem -LiteralPath $root -Recurse -Force | Sort-Object FullName | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($root, $_.FullName)
        if ($_.PSIsContainer) { "directory|$relative" }
        else { "file|$relative|$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)" }
    }
}
function Invoke-BackupScript([string] $gameRoot, [switch] $GameRunning) {
    $scriptCopy = Join-Path $gameRoot 'project/tools/backup-saves.ps1'
    $driver = Join-Path $gameRoot 'project/tools/backup-test-driver.ps1'
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $driver, '-ScriptPath', $scriptCopy)
    if ($GameRunning) { $arguments += '-GameRunning' }
    $output = @(& (Join-Path $PSHOME 'pwsh.exe') @arguments 2>&1 | ForEach-Object { $_.ToString() })
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
# Fixed driver source and parameter binding keep paths out of generated shell commands.
# The override lives only in this fixture's child process; it never queries the host.
Set-Content -LiteralPath (Join-Path $gameRoot 'project/tools/backup-test-driver.ps1') -Value @'
param([Parameter(Mandatory = $true)][string] $ScriptPath, [switch] $GameRunning)
$ErrorActionPreference = 'Stop'
$simulatedGameRunning = $GameRunning.IsPresent
$processQuery = {
    [CmdletBinding()]
    param([string] $Name)
    if ($Name -ne 'BeastSaga') { throw "Unexpected process query in backup fixture: $Name" }
    if ($simulatedGameRunning) { [pscustomobject]@{ ProcessName = 'BeastSaga'; Id = -1 } }
}.GetNewClosure()
Set-Item -LiteralPath Function:Get-Process -Value $processQuery
try {
    & $ScriptPath
    exit 0
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
'@

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
    Then 'a running game is refused before any backup or verification write' {
        $before = @(Get-TestTreeState $gameRoot) -join "`n"
        $refused = Invoke-BackupScript $gameRoot -GameRunning
        Require ($refused.ExitCode -ne 0) 'A running game was accepted'
        Require (($refused.Output -join "`n") -like '*Game is running; refusing to back up inconsistent saves*') ($refused.Output -join "`n")
        Require ([string]::IsNullOrWhiteSpace($refused.Backup)) 'Refused backup reported a BACKUP artifact'
        Require (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'project/backups'))) 'Refused backup created backup artifacts'
        Require (@(Get-ChildItem -LiteralPath (Join-Path $gameRoot 'project/verification') -Force).Count -eq 0) 'Refused backup wrote verification artifacts'
        Require (@(Get-ChildItem -LiteralPath $gameRoot -Recurse -Force -File -Filter 'VERIFIED').Count -eq 0) 'Refused backup created VERIFIED'
        Require ((@(Get-TestTreeState $gameRoot) -join "`n") -eq $before) 'Refused backup changed the fixture tree'
    }
    $result = Invoke-BackupScript $temporaryRoot
    Then 'backup completes' {
        $diagnostics = (($result.Output | Select-Object -First 8) -join [Environment]::NewLine)
        Require ($result.ExitCode -eq 0) $diagnostics
        Require (-not [string]::IsNullOrWhiteSpace($result.Backup)) "BACKUP output missing: $diagnostics"
        Require (Test-Path -LiteralPath $result.Backup -PathType Container) "BACKUP path missing: $($result.Backup)"
    }

    $manifest = @()
    if (-not [string]::IsNullOrWhiteSpace($result.Backup) -and (Test-Path -LiteralPath (Join-Path $result.Backup 'manifest.json'))) {
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

    Remove-TestDirectory (Join-Path $gameRoot 'SafeDialogueToolsData')
    $missingRootResult = Invoke-BackupScript $temporaryRoot
    $missingRootManifest = @()
    if (-not [string]::IsNullOrWhiteSpace($missingRootResult.Backup) -and (Test-Path -LiteralPath (Join-Path $missingRootResult.Backup 'manifest.json'))) {
        $missingRootManifest = @((Get-Content -LiteralPath (Join-Path $missingRootResult.Backup 'manifest.json') -Raw | ConvertFrom-Json))
    }
    Then 'a missing historical data root is skipped' {
        Require ($missingRootResult.ExitCode -eq 0) (($missingRootResult.Output | Select-Object -First 5) -join [Environment]::NewLine)
        $expectedWithoutHistoricalRoot = @($expectedFiles | Where-Object { $_.Root -ne 'SafeDialogueToolsData' })
        Require ($missingRootManifest.Count -eq $expectedWithoutHistoricalRoot.Count) "manifest has $($missingRootManifest.Count) entries; expected $($expectedWithoutHistoricalRoot.Count)"
        Require (-not (Test-Path -LiteralPath (Join-Path $missingRootResult.Backup 'SafeDialogueToolsData'))) 'missing root was created in backup'
    }
} finally {
    Remove-TestDirectory $temporaryRoot
}

$passed = $script:checks - $script:failures
Write-Output "RESULT $passed/$script:checks passed"
if ($script:failures -ne 0) { exit 1 }
exit 0
