param(
    [switch]$SkipTests,
    [switch]$SkipBuild,
    [string]$GameDir,
    [string]$PluginDir
)
$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path "$PSScriptRoot/..").Path
$dotnet = Join-Path $projectRoot 'tools/sdk/dotnet.exe'
$sourceDll = Join-Path $projectRoot 'Plugin/bin/Release/net6.0/BeastSagaBetter.dll'
$distDirectory = Join-Path $projectRoot 'dist'
$distDll = Join-Path $distDirectory 'BeastSagaBetter.dll'
. (Join-Path $PSScriptRoot 'Deployment.ps1')

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    $resolvedGameDir = (Resolve-Path "$projectRoot/..").Path
} else {
    $resolvedGameDir = [IO.Path]::GetFullPath($GameDir)
}

$plan = New-DeploymentPlan -ProjectRoot $projectRoot -GameDir $resolvedGameDir -PluginDir $PluginDir
$pluginDir = $plan.PluginDir
$targetDll = Join-Path $pluginDir 'BeastSagaBetter.dll'

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

$freshness = Test-DeploymentInputsFresh -ProjectRoot $projectRoot -ArtifactPath $sourceDll
if (-not $freshness.Fresh) { throw $freshness.Reason }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupDir = Join-Path $projectRoot "backups/$stamp-deploy"
New-Item -ItemType Directory -Path $backupDir | Out-Null

$legacyBackup = $null
if ($plan.RequiresMigration) {
    $legacyBackup = Join-Path $backupDir 'legacy-plugin'
    Backup-DirectoryTree -SourceDirectory $plan.LegacyPluginDir -BackupDirectory $legacyBackup | Out-Null
}

$previousHash = $null
if (Test-Path -LiteralPath $targetDll) {
    $previousHash = (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash
    $targetBackup = Join-Path $backupDir 'BeastSagaBetter.dll'
    $targetBackupCopy = New-VerifiedTemporaryCopy -SourceFile $targetDll -DestinationDirectory $backupDir
    Publish-VerifiedFile -TemporaryFile $targetBackupCopy.Path -DestinationFile $targetBackup -ExpectedHash $targetBackupCopy.SHA256
}

New-Item -ItemType Directory -Path (Split-Path -Parent $distDll) -Force | Out-Null
$legacyDisabled = $false
$removedDistFiles = @()
try {
    $removedDistFiles = Remove-LegacyDistArtifacts -DistDirectory $distDirectory
} catch {
    if ($legacyDisabled) { Restore-DisabledDirectoryTree -Directory $plan.LegacyPluginDir }
    throw
}
$distCopy = New-VerifiedTemporaryCopy -SourceFile $sourceDll -DestinationDirectory $distDirectory
$distRollback = $null
if (Test-Path -LiteralPath $distDll) {
    $distRollback = New-VerifiedTemporaryCopy -SourceFile $distDll -DestinationDirectory $distDirectory
}

New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
$deployCopy = New-VerifiedTemporaryCopy -SourceFile $sourceDll -DestinationDirectory $pluginDir
$deployRollback = $null
if (Test-Path -LiteralPath $targetDll) {
    $deployRollback = New-VerifiedTemporaryCopy -SourceFile $targetDll -DestinationDirectory $pluginDir
}

$sourceHash = (Get-FileHash -LiteralPath $sourceDll -Algorithm SHA256).Hash
try {
    Publish-VerifiedFile -TemporaryFile $distCopy.Path -DestinationFile $distDll -ExpectedHash $sourceHash
    Publish-VerifiedFile -TemporaryFile $deployCopy.Path -DestinationFile $targetDll -ExpectedHash $sourceHash
    $deployedHash = (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash
    $distHash = (Get-FileHash -LiteralPath $distDll -Algorithm SHA256).Hash
    if ($deployedHash -ne $sourceHash -or $distHash -ne $sourceHash) { throw '部署后哈希不一致。' }
} catch {
    if ($null -ne $deployRollback -and (Test-Path -LiteralPath $deployRollback.Path)) {
        Publish-VerifiedFile -TemporaryFile $deployRollback.Path -DestinationFile $targetDll -ExpectedHash $deployRollback.SHA256
    }
    if ($null -ne $distRollback -and (Test-Path -LiteralPath $distRollback.Path)) {
        Publish-VerifiedFile -TemporaryFile $distRollback.Path -DestinationFile $distDll -ExpectedHash $distRollback.SHA256
    }
    if ($legacyDisabled) { Restore-DisabledDirectoryTree -Directory $plan.LegacyPluginDir }
    throw
} finally {
    foreach ($temporary in @($distCopy, $deployCopy, $deployRollback, $distRollback)) {
        if ($null -ne $temporary -and (Test-Path -LiteralPath $temporary.Path)) {
            Remove-Item -LiteralPath $temporary
        }
    }
}

if ($plan.RequiresMigration) {
    try {
        Disable-DirectoryTree -Directory $plan.LegacyPluginDir
        $legacyDisabled = $true
    } catch {
        if (Test-Path -LiteralPath (Join-Path $plan.LegacyPluginDir '.disabled')) {
            Restore-DisabledDirectoryTree -Directory $plan.LegacyPluginDir
        }
        throw
    }
}

$record = [pscustomobject]@{
    deployedAt = (Get-Date).ToString('o')
    dll = $targetDll
    sha256 = $deployedHash
    previousDllSha256 = $previousHash
    backup = $backupDir
    legacyPluginBackup = $legacyBackup
    legacyPluginDir = $plan.LegacyPluginDir
    legacyPluginDisabled = $plan.RequiresMigration
    removedDistFiles = $removedDistFiles
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
    LegacyPluginBackup = $legacyBackup
    RemovedDistFiles = $removedDistFiles
    Record = $recordPath
} | Format-List
