param([switch]$Batch, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path "$PSScriptRoot/..").Path
$testRoot = [IO.Path]::GetFullPath("$projectRoot/sandbox/Game")
$formalRoot = [IO.Path]::GetFullPath("$projectRoot/../")
$formalSaveRoot = [IO.Path]::GetFullPath("$formalRoot/SaveData")
$dotnet = [IO.Path]::GetFullPath("$projectRoot/tools/sdk/dotnet.exe")

function Assert-InsideProject([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the project: $fullPath"
    }
    return $fullPath
}

function Invoke-TestCommand([string]$Command) {
    $output = & "$PSScriptRoot/send-test.ps1" -Command $Command 2>&1
    $resultPath = Assert-InsideProject("$testRoot/test-result.txt")
    $result = Get-Content -LiteralPath $resultPath -Raw -ErrorAction SilentlyContinue
    if ($result -ne "OK $Command") {
        throw "Test command failed: $Command`n$output"
    }
}

function TryTestCommand([string]$Command) {
    try {
        Invoke-TestCommand $Command
        return $true
    } catch {
        return $false
    }
}

$formalExecutable = [IO.Path]::GetFullPath("$formalRoot/BeastSaga.exe")
$formalProcesses = @(Get-Process -Name BeastSaga -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $formalExecutable })
if ($formalProcesses.Count -gt 0) {
    throw 'Close the production game before syncing its latest quick save.'
}

if (-not $SkipBuild) {
    & $dotnet run --project "$projectRoot/Tests/SafeDialogueTools.Tests.csproj" -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed with exit code $LASTEXITCODE" }
    & $dotnet build "$projectRoot/Plugin/SafeDialogueTools.csproj" -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Plugin build failed with exit code $LASTEXITCODE" }
}

$testExecutable = [IO.Path]::GetFullPath("$testRoot/BeastSaga.exe")
$testProcesses = @(Get-Process -Name BeastSaga -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $testExecutable })
if ($testProcesses.Count -gt 1) { throw 'Multiple isolated test processes found; close them before resetting saves.' }
if ($testProcesses.Count -eq 1) {
    & "$PSScriptRoot/send-test.ps1" -Command 'quit'
    $testProcesses | Wait-Process -Timeout 15
}

$latestQuick = Get-ChildItem -LiteralPath $formalSaveRoot -File -Filter 'quick*.es3' |
    Sort-Object LastWriteTime, Name -Descending |
    Select-Object -First 1
if ($null -eq $latestQuick) { throw 'No production quick save found.' }
if ($latestQuick.Name -notmatch '^quick(?<number>[123])\.es3$') { throw "Unexpected quick save name: $($latestQuick.Name)" }
$nativeQuickNumber = [int]$Matches['number']
$checkpointRoot = Join-Path $formalRoot 'SafeDialogueToolsData/checkpoints'
$latestManual = $null
if (Test-Path -LiteralPath $checkpointRoot) {
    foreach ($directory in @(Get-ChildItem -LiteralPath $checkpointRoot -Directory)) {
        $manifestPath = Join-Path $directory.FullName 'manifest.json'
        if (-not (Test-Path -LiteralPath $manifestPath)) { continue }
        try { $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json } catch { continue }
        if ($manifest.Version -ne 2 -or $manifest.Metadata.Kind -ne 0 -or $null -eq $manifest.Metadata.CapturedAt) { continue }
        $savePath = Join-Path $directory.FullName 'save.es3'
        $utilPath = Join-Path $directory.FullName 'map-util.es3'
        if (-not (Test-Path -LiteralPath $savePath)) { continue }
        $captured = [DateTimeOffset]::Parse([string]$manifest.Metadata.CapturedAt, [Globalization.CultureInfo]::InvariantCulture)
        if ($null -eq $latestManual -or $captured -gt $latestManual.CapturedAt) {
            $latestManual = [pscustomobject]@{
                Directory = $directory
                CapturedAt = $captured
                SavePath = $savePath
                UtilPath = $utilPath
                ThumbnailPath = Join-Path $directory.FullName 'thumbnail.png'
                Manifest = $manifest
            }
        }
    }
}

$useCheckpoint = $null -ne $latestManual -and
    $latestManual.CapturedAt -gt [DateTimeOffset]::new($latestQuick.LastWriteTime)
$quickNumber = if ($useCheckpoint) { 3 } else { $nativeQuickNumber }
$expectedSlotName = "quick$quickNumber.es3"
$sourceKind = if ($useCheckpoint) { 'F5ManualCheckpoint' } else { 'NativeQuick' }
$sourcePath = if ($useCheckpoint) { $latestManual.SavePath } else { $latestQuick.FullName }
$sourceTime = if ($useCheckpoint) { $latestManual.CapturedAt } else { $latestQuick.LastWriteTime }
$sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
$expectedLevel = $null
if ($useCheckpoint) {
    $saveHash = [string]$latestManual.Manifest.Files.PSObject.Properties['save.es3'].Value
    $utilHash = [string]$latestManual.Manifest.Files.PSObject.Properties['map-util.es3'].Value
    if ($saveHash -ne $sourceHash) { throw 'Latest F5 checkpoint save hash does not match its manifest.' }
    if (-not (Test-Path -LiteralPath $latestManual.UtilPath)) { throw "Latest F5 checkpoint is missing map-util.es3: $($latestManual.Directory.Name)" }
    if ((Get-FileHash -LiteralPath $latestManual.UtilPath -Algorithm SHA256).Hash -ne $utilHash) {
        throw 'Latest F5 checkpoint map-util hash does not match its manifest.'
    }
    if ($null -ne $latestManual.Manifest.Metadata.PlayerLevel) {
        $expectedLevel = [int]$latestManual.Manifest.Metadata.PlayerLevel
    }
}
$formalFiles = @(Get-ChildItem -LiteralPath $formalSaveRoot -File)
if (-not ($formalFiles.Name -contains $latestQuick.Name)) { throw "Production quick save disappeared: $($latestQuick.Name)" }

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$archiveRoot = Assert-InsideProject("$projectRoot/backups/$timestamp-latest-formal-quick-reset")
New-Item -ItemType Directory -Path (Join-Path $archiveRoot 'SaveData') -Force | Out-Null
$saveDataRoot = Assert-InsideProject("$testRoot/SaveData")
foreach ($file in @(Get-ChildItem -LiteralPath $saveDataRoot -File)) {
    Move-Item -LiteralPath $file.FullName -Destination (Join-Path $archiveRoot 'SaveData' | Join-Path -ChildPath $file.Name)
}

foreach ($file in $formalFiles) {
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $saveDataRoot $file.Name) -Force
}
Copy-Item -LiteralPath $latestQuick.FullName -Destination (Join-Path $saveDataRoot 'seed.es3') -Force
$quickHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
$bootstrapHash = (Get-FileHash -LiteralPath $latestQuick.FullName -Algorithm SHA256).Hash
$seedHash = (Get-FileHash -LiteralPath (Join-Path $saveDataRoot 'seed.es3') -Algorithm SHA256).Hash
if ($bootstrapHash -ne $seedHash) { throw 'Seed copy hash mismatch.' }
Set-Content -LiteralPath (Assert-InsideProject("$testRoot/test-expected-quick.txt")) -Value $quickNumber -Encoding ascii
$saveIndexes = @(Get-ChildItem -LiteralPath $saveDataRoot -File -Filter 'SaveInfo*.es3')
if ($saveIndexes.Count -eq 0) { throw 'The isolated copy has no bootstrap save index.' }

$dataRoot = Assert-InsideProject("$testRoot/SafeDialogueToolsData")
foreach ($name in @('checkpoints', 'load-work')) {
    $path = Join-Path $dataRoot $name
    if (Test-Path -LiteralPath $path) { Move-Item -LiteralPath $path -Destination (Join-Path $archiveRoot $name) }
}

$startOutput = & "$PSScriptRoot/start-test.ps1" -Graphical:(-not $Batch)
$startOutput
$ready = $false
for ($attempt = 0; $attempt -lt 15; $attempt++) {
    if (TryTestCommand 'state') {
        $ready = $true
        break
    }
    Start-Sleep -Seconds 2
}
if (-not $ready) { throw 'The isolated game did not respond before loading.' }

$statePath = Assert-InsideProject("$testRoot/test-state.json")
$state = $null
$prepared = $false
$continueAttempts = 0
$lastContinueAt = [DateTime]::MinValue
$targetSaveType = $null
$targetSaveId = $null
$targetSavePath = $null
$targetSaveTime = $null
$deadline = [DateTime]::UtcNow.AddSeconds(180)
for ($attempt = 0; [DateTime]::UtcNow -lt $deadline; $attempt++) {
    $state = $null
    if (-not (TryTestCommand 'state')) {
        Start-Sleep -Milliseconds 500
        continue
    }
    try {
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    } catch {
        # Retry with a fresh snapshot on the next iteration.
    }
    if ($null -ne $state) {
        if ($state.faulted) { break }
        if ($state.isLogin -and $state.loadOver -and $state.canGame -and -not $state.loading -and -not $state.faulted -and
            ([string]$state.file).EndsWith($expectedSlotName, [StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        if (-not $prepared -and $state.loginOpen -and $null -ne $state.nativePath) {
            $keepSlot = $expectedSlotName
            foreach ($file in @(Get-ChildItem -LiteralPath $saveDataRoot -File)) {
                $isSlot = $file.Name -match '^(Auto|quick|note|custom)\d+\.es3$'
                $isUtil = $file.Name -match '^quickUtil\d+\.es3$'
                $isIndex = $file.Name -match '^SaveInfo\d+\.es3$'
                $isScreenshot = $file.Name -match '^Screenshot_(Auto|Quick|Note|Custom)\d+\.png$'
                if ($isSlot) {
                    Remove-Item -LiteralPath $file.FullName -Force
                } elseif ($isUtil) {
                    Remove-Item -LiteralPath $file.FullName -Force
                } elseif ($isIndex) {
                    Remove-Item -LiteralPath $file.FullName -Force
                } elseif ($isScreenshot -and $file.Name -ne "Screenshot_Quick$quickNumber.png") {
                    Remove-Item -LiteralPath $file.FullName -Force
                }
            }
            Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $saveDataRoot "quick$quickNumber.es3") -Force
            Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $saveDataRoot 'seed.es3') -Force
            if ($useCheckpoint) {
                Copy-Item -LiteralPath $latestManual.UtilPath -Destination (Join-Path $saveDataRoot "quickUtil$quickNumber.es3") -Force
                if (Test-Path -LiteralPath $latestManual.ThumbnailPath) {
                    Copy-Item -LiteralPath $latestManual.ThumbnailPath -Destination (Join-Path $saveDataRoot "Screenshot_Quick$quickNumber.png") -Force
                }
            }
            $copiedSourceHash = (Get-FileHash -LiteralPath (Join-Path $saveDataRoot "quick$quickNumber.es3") -Algorithm SHA256).Hash
            if ($quickHash -ne $copiedSourceHash) { throw 'Copied quick save hash mismatch.' }
            $remainingSlots = @(Get-ChildItem -LiteralPath $saveDataRoot -File | Where-Object {
                $_.Name -match '^(Auto|quick|note|custom)\d+\.es3$'
            })
            if ($remainingSlots.Count -ne 1 -or $remainingSlots[0].Name -ne $keepSlot) {
                throw "Isolated save cleanup failed: $($remainingSlots.Name -join ', ')"
            }
            if ($useCheckpoint -and -not (Test-Path -LiteralPath (Join-Path $saveDataRoot "quickUtil$quickNumber.es3"))) {
                throw 'Isolated quick util file disappeared during cleanup.'
            }
            Invoke-TestCommand "rebuild-save-info:$quickNumber"
            $prepared = $true
        }
        if (-not $state.continueRequested -and -not $state.nativeLoadStarted -and
            ([DateTime]::UtcNow - $lastContinueAt).TotalSeconds -ge 1 -and
            $prepared -and $state.continueReady -and $state.loginOpen -and $null -ne $state.firstSaveType) {
            if ($state.firstSaveType -ne 1 -or [int]$state.firstSaveId -ne $quickNumber) {
                throw "继续游戏目标错误：firstSaveType=$($state.firstSaveType) firstSaveId=$($state.firstSaveId) firstSavePath=$($state.firstSavePath)。应为 quick$quickNumber.es3。"
            }
            if ($continueAttempts -eq 0) {
                $targetSaveType = $state.firstSaveType
                $targetSaveId = $state.firstSaveId
                $targetSavePath = $state.firstSavePath
                $targetSaveTime = $state.firstSaveTime
            }
            Invoke-TestCommand "menu-continue-quick:$quickNumber"
            $continueAttempts++
            $lastContinueAt = [DateTime]::UtcNow
        }
    }
    Start-Sleep -Milliseconds 500
}
if ($null -eq $state -or -not ($state.isLogin -and $state.loadOver -and $state.canGame -and -not $state.loading -and -not $state.faulted -and
    ([string]$state.file).EndsWith($expectedSlotName, [StringComparison]::OrdinalIgnoreCase))) {
    if ($null -ne $state) {
        Write-Output ("LAST_STATE=" + ($state | ConvertTo-Json -Compress))
    }
    throw 'The isolated game did not finish loading the latest production quick save.'
}
if ($null -ne $expectedLevel -and [int]$state.level -ne $expectedLevel) {
    throw "Loaded player level $($state.level) does not match the latest F5 checkpoint level $expectedLevel."
}

[pscustomobject]@{
    SourceKind = $sourceKind
    SourceSave = $sourcePath
    SourceTime = $sourceTime
    SourceCheckpoint = if ($useCheckpoint) { $latestManual.Directory.Name } else { $null }
    SandboxQuick = Join-Path $saveDataRoot "quick$quickNumber.es3"
    SHA256 = $quickHash
    Archive = $archiveRoot
    ProcessId = ($startOutput | Select-String -Pattern 'TEST_PID=(\d+)').Matches[0].Groups[1].Value
    LoadedFile = $state.file
    LoadedLevel = $state.level
    ContinueAttempts = $continueAttempts
    TargetSaveType = $targetSaveType
    TargetSaveId = $targetSaveId
    TargetSavePath = $targetSavePath
    TargetSaveTime = $targetSaveTime
    Position = $state.position
    SelectedCheckpoint = $state.selected
} | Format-List
