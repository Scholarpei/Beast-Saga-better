param([switch]$SkipTests, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path "$PSScriptRoot/..").Path
$gameRoot = (Resolve-Path "$projectRoot/..").Path
$dotnet = Join-Path $projectRoot 'tools/sdk/dotnet.exe'
$sourceDll = Join-Path $projectRoot 'Plugin/bin/Release/net6.0/BeastSagaBetter.dll'
$pluginDir = Join-Path $gameRoot 'BepInEx/plugins/BeastSagaBetter'
$targetDll = Join-Path $pluginDir 'BeastSagaBetter.dll'
$distDll = Join-Path $projectRoot 'dist/BeastSagaBetter.dll'

if (Get-Process -Name BeastSaga -ErrorAction SilentlyContinue) {
    throw '请先退出游戏，再部署插件。'
}

if (-not $SkipTests) {
    & $dotnet run --project "$projectRoot/Tests/BeastSagaBetter.Tests.csproj" -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "测试失败，退出码 $LASTEXITCODE" }
}

if (-not $SkipBuild) {
    & $dotnet build "$projectRoot/Plugin/BeastSagaBetter.csproj" -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "构建失败，退出码 $LASTEXITCODE" }
}

if (-not (Test-Path -LiteralPath $sourceDll)) { throw "找不到构建产物：$sourceDll" }
if (-not (Test-Path -LiteralPath $pluginDir)) { throw "找不到正式插件目录：$pluginDir" }

$newestSource = Get-ChildItem -LiteralPath "$projectRoot/Plugin" -Recurse -File -Include *.cs |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if ($null -ne $newestSource -and $newestSource.LastWriteTime -gt (Get-Item -LiteralPath $sourceDll).LastWriteTime) {
    throw "构建产物早于源码 $($newestSource.Name)，请去掉 -SkipBuild 重新构建。"
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupDir = Join-Path $projectRoot "backups/$stamp-deploy"
New-Item -ItemType Directory -Path $backupDir | Out-Null

$previousHash = $null
if (Test-Path -LiteralPath $targetDll) {
    $previousHash = (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash
    Copy-Item -LiteralPath $targetDll -Destination (Join-Path $backupDir 'BeastSagaBetter.dll')
    $backupHash = (Get-FileHash -LiteralPath (Join-Path $backupDir 'BeastSagaBetter.dll') -Algorithm SHA256).Hash
    if ($backupHash -ne $previousHash) { throw '旧 DLL 备份校验失败。' }
}

Copy-Item -LiteralPath $sourceDll -Destination $targetDll -Force
New-Item -ItemType Directory -Path (Split-Path -Parent $distDll) -Force | Out-Null
Copy-Item -LiteralPath $sourceDll -Destination $distDll -Force

$sourceHash = (Get-FileHash -LiteralPath $sourceDll -Algorithm SHA256).Hash
$deployedHash = (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash
$distHash = (Get-FileHash -LiteralPath $distDll -Algorithm SHA256).Hash
if ($deployedHash -ne $sourceHash -or $distHash -ne $sourceHash) { throw '部署后哈希不一致。' }

$record = [pscustomobject]@{
    deployedAt = (Get-Date).ToString('o')
    dll = $targetDll
    sha256 = $deployedHash
    previousDllSha256 = $previousHash
    backup = $backupDir
    tests = -not $SkipTests
    build = -not $SkipBuild
}
$recordPath = Join-Path $projectRoot "verification/production-deployment-$stamp.json"
$record | ConvertTo-Json | Set-Content -LiteralPath $recordPath -Encoding utf8

[pscustomobject]@{
    Deployed = $targetDll
    SHA256 = $deployedHash
    Previous = $previousHash
    Backup = $backupDir
    Record = $recordPath
} | Format-List
