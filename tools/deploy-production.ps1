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

$verificationDir = Join-Path $projectRoot 'verification'
New-Item -ItemType Directory -Path $verificationDir -Force | Out-Null
$probe = Join-Path $verificationDir ('.deployment-probe-' + [guid]::NewGuid().ToString('N'))
try {
    Set-Content -LiteralPath $probe -Value 'deployment record preflight' -Encoding utf8
} finally {
    if (Test-Path -LiteralPath $probe) { Remove-Item -LiteralPath $probe -Force }
}

$stamp = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$backupDir = Join-Path $projectRoot "backups/$stamp-deploy"
New-Item -ItemType Directory -Path $backupDir | Out-Null
$recordPath = Join-Path $verificationDir "production-deployment-$stamp.json"

$legacyBackup = $null
if ($plan.RequiresMigration) {
    $legacyBackup = Join-Path $backupDir 'legacy-plugin'
    Backup-DirectoryTree -SourceDirectory $plan.LegacyPluginDir -BackupDirectory $legacyBackup | Out-Null
}

$previousHash = $null
# Keep immutable rollback evidence outside the publication directories until the record succeeds.
$fileStates = @(
    foreach ($path in @($targetDll, $distDll, (Join-Path $distDirectory 'SafeDialogueTools.dll'), (Join-Path $distDirectory 'SafeDialogueTools.pdb'))) {
        $copy = $null
        if (Test-Path -LiteralPath $path) {
            $snapshotDirectory = if ($path -eq $targetDll) { $backupDir } else { Join-Path $backupDir 'dist' }
            New-Item -ItemType Directory -Path $snapshotDirectory -Force | Out-Null
            $copy = New-VerifiedTemporaryCopy -SourceFile $path -DestinationDirectory $snapshotDirectory
            $snapshotPath = Join-Path $snapshotDirectory ([IO.Path]::GetFileName($path))
            Publish-VerifiedFile -TemporaryFile $copy.Path -DestinationFile $snapshotPath -ExpectedHash $copy.SHA256
            $copy = [pscustomobject]@{ Path = $snapshotPath; SHA256 = $copy.SHA256 }
        }
        [pscustomobject]@{ Path = $path; Backup = $copy }
    }
)
if ($null -ne $fileStates[0].Backup) { $previousHash = $fileStates[0].Backup.SHA256 }
$pluginDirectoryExisted = Test-Path -LiteralPath $pluginDir
$distDirectoryExisted = Test-Path -LiteralPath $distDirectory
$migrationStarted = $false
$temporaryCopies = [Collections.Generic.List[object]]::new()
try {
    New-Item -ItemType Directory -Path $distDirectory, $pluginDir -Force | Out-Null
    $distCopy = New-VerifiedTemporaryCopy -SourceFile $sourceDll -DestinationDirectory $distDirectory
    $temporaryCopies.Add($distCopy)
    $deployCopy = New-VerifiedTemporaryCopy -SourceFile $sourceDll -DestinationDirectory $pluginDir
    $temporaryCopies.Add($deployCopy)
    $sourceHash = (Get-FileHash -LiteralPath $sourceDll -Algorithm SHA256).Hash
    $removedDistFiles = @(Remove-LegacyDistArtifacts -DistDirectory $distDirectory)
    Publish-VerifiedFile -TemporaryFile $distCopy.Path -DestinationFile $distDll -ExpectedHash $sourceHash
    Publish-VerifiedFile -TemporaryFile $deployCopy.Path -DestinationFile $targetDll -ExpectedHash $sourceHash
    $deployedHash = (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash
    $distHash = (Get-FileHash -LiteralPath $distDll -Algorithm SHA256).Hash
    if ($deployedHash -ne $sourceHash -or $distHash -ne $sourceHash) { throw '部署后哈希不一致。' }

    if ($plan.RequiresMigration) {
        $migrationStarted = $true
        Disable-DirectoryTree -Directory $plan.LegacyPluginDir
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
    $record | ConvertTo-Json | Set-Content -LiteralPath $recordPath -Encoding utf8
} catch {
    $deploymentError = $_
    $rollbackErrors = [Collections.Generic.List[string]]::new()
    # First remove/restore the new active assembly, then restore the old plugin.
    foreach ($state in $fileStates) {
        try {
            if ($null -ne $state.Backup) {
                $restore = New-VerifiedTemporaryCopy -SourceFile $state.Backup.Path -DestinationDirectory (Split-Path -Parent $state.Path)
                $temporaryCopies.Add($restore)
                Publish-VerifiedFile -TemporaryFile $restore.Path -DestinationFile $state.Path -ExpectedHash $state.Backup.SHA256
            } elseif (Test-Path -LiteralPath $state.Path) {
                Remove-Item -LiteralPath $state.Path -Force
            }
        } catch { $rollbackErrors.Add($_.Exception.Message) }
    }
    if ($migrationStarted) {
        try {
            Restore-DeploymentDirectoryBackup -BackupDirectory $legacyBackup -Directory $plan.LegacyPluginDir -AllowedRoot (Join-Path $resolvedGameDir 'BepInEx/plugins')
        } catch { $rollbackErrors.Add($_.Exception.Message) }
    }
    try {
        if (Test-Path -LiteralPath $recordPath) { Remove-Item -LiteralPath $recordPath -Force }
    } catch { $rollbackErrors.Add($_.Exception.Message) }
    if ($rollbackErrors.Count -gt 0) {
        throw "部署失败：$($deploymentError.Exception.Message)。回滚未完成：$($rollbackErrors -join '；')。保留恢复备份：$backupDir"
    }
    throw $deploymentError
} finally {
    foreach ($temporary in $temporaryCopies) {
        if (Test-Path -LiteralPath $temporary.Path) { Remove-Item -LiteralPath $temporary.Path -Force }
    }
    foreach ($directory in @(
        [pscustomobject]@{ Path = $pluginDir; Existed = $pluginDirectoryExisted },
        [pscustomobject]@{ Path = $distDirectory; Existed = $distDirectoryExisted }
    )) {
        if (-not $directory.Existed -and (Test-Path -LiteralPath $directory.Path) -and
            @(Get-ChildItem -LiteralPath $directory.Path -Force).Count -eq 0) {
            Remove-Item -LiteralPath $directory.Path
        }
    }
}

[pscustomobject]@{
    Deployed = $targetDll
    SHA256 = $deployedHash
    Previous = $previousHash
    Backup = $backupDir
    LegacyPluginBackup = $legacyBackup
    RemovedDistFiles = $removedDistFiles
    Record = $recordPath
} | Format-List
