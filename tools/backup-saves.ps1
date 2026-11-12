$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path -LiteralPath "$PSScriptRoot/../..").Path
$projectRoot = Split-Path -Parent $PSScriptRoot
$backupParent = Join-Path $projectRoot 'backups'
$verificationDirectory = Join-Path $projectRoot 'verification'
$roots = @(
    @{ Source = 'SaveData'; Backup = 'SaveData'; Required = $true }
    @{ Source = 'BepInEx/config'; Backup = 'config'; Required = $true }
    @{ Source = 'BeastSagaBetterData'; Backup = 'BeastSagaBetterData'; Required = $false }
    @{ Source = 'SafeDialogueToolsData'; Backup = 'SafeDialogueToolsData'; Required = $false }
)

function Assert-PathIsUnderGameRoot([string] $path) {
    $fullPath = [IO.Path]::GetFullPath($path)
    $comparison = [StringComparison]::OrdinalIgnoreCase
    if (-not $fullPath.StartsWith($gameRoot + [IO.Path]::DirectorySeparatorChar, $comparison)) {
        throw "Refusing path outside game root: $fullPath"
    }
}

if (Get-Process -Name BeastSaga -ErrorAction SilentlyContinue) { throw 'Game is running; refusing to back up inconsistent saves' }
$backupRoot = Join-Path $backupParent (Get-Date -Format 'yyyyMMdd-HHmmss')
New-Item -ItemType Directory -Path $backupRoot | Out-Null
$before = @()
$sourceFilesByRoot = @{}
foreach ($root in $roots) {
    $sourceRoot = Join-Path $gameRoot $root.Source
    if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) {
        if ($root.Required) { throw "Required backup root is missing: $sourceRoot" }
        continue
    }
    Assert-PathIsUnderGameRoot $sourceRoot
    $files = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | ForEach-Object {
        [pscustomobject]@{
            Root = $root.Source
            Path = [IO.Path]::GetRelativePath($sourceRoot, $_.FullName)
            Length = $_.Length
            SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    })
    $sourceFilesByRoot[$root.Source] = $files
    $before += $files
}

foreach ($root in $roots) {
    if (-not $sourceFilesByRoot.ContainsKey($root.Source)) { continue }
    $sourceRoot = Join-Path $gameRoot $root.Source
    $backupRootForTree = Join-Path $backupRoot $root.Backup
    Assert-PathIsUnderGameRoot $backupRootForTree
    Copy-Item -LiteralPath $sourceRoot -Destination $backupRootForTree -Recurse
}

foreach ($root in $roots) {
    if (-not $sourceFilesByRoot.ContainsKey($root.Source)) { continue }
    $sourceRoot = Join-Path $gameRoot $root.Source
    $backupRootForTree = Join-Path $backupRoot $root.Backup
    foreach ($entry in $sourceFilesByRoot[$root.Source]) {
        foreach ($base in @($sourceRoot, $backupRootForTree)) {
            $path = Join-Path $base $entry.Path
            Assert-PathIsUnderGameRoot $path
            if ((Get-Item -LiteralPath $path).Length -ne $entry.Length) { throw "File length changed or backup invalid: $path" }
            if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.SHA256) { throw "File changed or backup invalid: $path" }
        }
    }
    if ((Get-ChildItem -LiteralPath $sourceRoot -Recurse -File).Count -ne $sourceFilesByRoot[$root.Source].Count) {
        throw "File list changed while backing up: $($root.Source)"
    }
}

if (Get-Process -Name BeastSaga -ErrorAction SilentlyContinue) { throw 'Game started during backup; backup is not certified' }
$before | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backupRoot 'manifest.json') -Encoding utf8
Set-Content -LiteralPath (Join-Path $backupRoot 'VERIFIED') -Value "All $($before.Count) files verified against source SHA256; game exited" -Encoding utf8
$resolvedBackup = (Resolve-Path -LiteralPath $backupRoot).Path
[IO.File]::WriteAllText((Join-Path $verificationDirectory 'latest-backup.txt'), $resolvedBackup)
Write-Output "BACKUP=$resolvedBackup"
Write-Output "VERIFIED_FILES=$($before.Count)"
