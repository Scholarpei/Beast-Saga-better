param([switch]$UseReadOnlySeed)
$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path "$PSScriptRoot/../..").Path
$projectRoot = (Resolve-Path "$PSScriptRoot/..").Path
$sandboxRoot = [IO.Path]::GetFullPath("$projectRoot/sandbox/Game")
if (-not $sandboxRoot.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Sandbox is outside project root'
}
if ((Get-Process -Name BeastSaga -ErrorAction SilentlyContinue) -and -not $UseReadOnlySeed) { throw 'Exit BeastSaga before preparing a consistent backup' }
if (Test-Path -LiteralPath $sandboxRoot) { throw 'Sandbox already exists; inspect it instead of overwriting' }
if (-not $UseReadOnlySeed) {
$backupRoot = "$projectRoot/backups/" + (Get-Date -Format 'yyyyMMdd-HHmmss')
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
Copy-Item -LiteralPath "$gameRoot/SaveData" -Destination "$backupRoot/SaveData" -Recurse
$manifest = foreach ($file in Get-ChildItem -LiteralPath "$gameRoot/SaveData" -Recurse -File) {
    $relative = [IO.Path]::GetRelativePath("$gameRoot/SaveData", $file.FullName)
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    $backupHash = (Get-FileHash -LiteralPath "$backupRoot/SaveData/$relative" -Algorithm SHA256).Hash
    if ($hash -ne $backupHash) { throw "Backup mismatch: $relative" }
    [pscustomobject]@{ Path = $relative; Length = $file.Length; SHA256 = $hash }
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath "$backupRoot/manifest.json" -Encoding utf8
}
New-Item -ItemType Directory -Path $sandboxRoot -Force | Out-Null
$sources = @('BeastSaga_Data', 'BepInEx', 'dotnet', 'SaveData', 'BeastSaga.exe', 'GameAssembly.dll', 'UnityPlayer.dll', 'baselib.dll', 'UnityCrashHandler64.exe', 'doorstop_config.ini', 'winhttp.dll')
foreach ($source in $sources) {
    if ($UseReadOnlySeed -and $source -eq 'SaveData') { continue }
    $sourcePath = Join-Path $gameRoot $source
    $files = if (Test-Path -LiteralPath $sourcePath -PathType Container) { Get-ChildItem -LiteralPath $sourcePath -Recurse -File } else { Get-Item -LiteralPath $sourcePath }
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($gameRoot, $file.FullName)
        if ($relative -match '^BepInEx[\\/](cache|Logs)[\\/]' -or $file.Name -match '\.log$') { continue }
        $target = Join-Path $sandboxRoot $relative
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
        if ($source -in @('BeastSaga_Data', 'dotnet') -or ($source -eq 'BepInEx' -and $relative -match '^BepInEx[\\/](core|interop|unity-libs)[\\/]') -or $source -match '\.(exe|dll)$') {
            New-Item -ItemType HardLink -Path $target -Target $file.FullName | Out-Null
        } else {
            Copy-Item -LiteralPath $file.FullName -Destination $target
        }
    }
}
if ($UseReadOnlySeed) {
    New-Item -ItemType Directory -Path "$sandboxRoot/SaveData" -Force | Out-Null
    $seedPath = "$gameRoot/SaveData/quick1.es3"
    $inputStream = [IO.FileStream]::new($seedPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $outputStream = [IO.FileStream]::new("$sandboxRoot/SaveData/seed.es3", [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $inputStream.CopyTo($outputStream); $outputStream.Flush($true) } finally { $outputStream.Dispose() }
    } finally { $inputStream.Dispose() }
    Write-Output 'Seed copied under a read lock; this is not a full save backup.'
}
Set-Content -LiteralPath "$sandboxRoot/SAFE_DIALOGUE_TEST_COPY" -Value 'Isolated test saves; suppress Unity PlayerPrefs writes' -Encoding ascii
New-Item -ItemType Directory -Path "$sandboxRoot/BepInEx/plugins/SafeDialogueTools" -Force | Out-Null
Copy-Item -LiteralPath "$projectRoot/Plugin/bin/Release/net6.0/SafeDialogueTools.dll" -Destination "$sandboxRoot/BepInEx/plugins/SafeDialogueTools/SafeDialogueTools.dll"
Write-Output "BACKUP=$backupRoot"
Write-Output "SANDBOX=$sandboxRoot"
