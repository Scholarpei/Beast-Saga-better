$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path "$PSScriptRoot/../..").Path
if (Get-Process -Name BeastSaga -ErrorAction SilentlyContinue) { throw 'Game is running; refusing to back up inconsistent saves' }
$backupRoot = Join-Path "$PSScriptRoot/../backups" (Get-Date -Format 'yyyyMMdd-HHmmss')
New-Item -ItemType Directory -Path $backupRoot | Out-Null
$before = @(Get-ChildItem -LiteralPath "$gameRoot/SaveData" -Recurse -File | ForEach-Object {
    [pscustomobject]@{
        Path = [IO.Path]::GetRelativePath("$gameRoot/SaveData", $_.FullName)
        Length = $_.Length
        SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
})
Copy-Item -LiteralPath "$gameRoot/SaveData" -Destination "$backupRoot/SaveData" -Recurse
Copy-Item -LiteralPath "$gameRoot/BepInEx/config" -Destination "$backupRoot/config" -Recurse
foreach ($entry in $before) {
    foreach ($base in @("$gameRoot/SaveData", "$backupRoot/SaveData")) {
        $path = Join-Path $base $entry.Path
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.SHA256) { throw "Save changed or backup invalid: $path" }
    }
}
if ((Get-ChildItem -LiteralPath "$gameRoot/SaveData" -Recurse -File).Count -ne $before.Count) { throw 'Save file list changed while backing up' }
if (Get-Process -Name BeastSaga -ErrorAction SilentlyContinue) { throw 'Game started during backup; backup is not certified' }
$before | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$backupRoot/manifest.json" -Encoding utf8
Set-Content -LiteralPath "$backupRoot/VERIFIED" -Value "All $($before.Count) files verified against source SHA256; game exited" -Encoding utf8
$resolvedBackup = (Resolve-Path $backupRoot).Path
[IO.File]::WriteAllText("$PSScriptRoot/../verification/latest-backup.txt", $resolvedBackup)
Write-Output "BACKUP=$resolvedBackup"
Write-Output "VERIFIED_FILES=$($before.Count)"
