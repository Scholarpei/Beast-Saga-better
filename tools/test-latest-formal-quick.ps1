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

$latestQuick = Get-ChildItem -LiteralPath $formalSaveRoot -File -Filter 'quick*.es3' |
    Sort-Object LastWriteTime, Name -Descending |
    Select-Object -First 1
if ($null -eq $latestQuick) { throw 'No production quick save found.' }
if ($latestQuick.Name -notmatch '^quick(?<number>[123])\.es3$') { throw "Unexpected quick save name: $($latestQuick.Name)" }
$quickNumber = [int]$Matches['number']
$saveInfoPath = Join-Path $formalSaveRoot "SaveInfo$quickNumber.es3"
if (-not (Test-Path -LiteralPath $saveInfoPath)) { throw "Missing save index: $saveInfoPath" }
$saveInfoText = Get-Content -LiteralPath $saveInfoPath -Raw
if ($saveInfoText -notmatch [regex]::Escape($latestQuick.Name)) { throw "Save index does not reference $($latestQuick.Name)." }

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$archiveRoot = Assert-InsideProject("$projectRoot/backups/$timestamp-latest-formal-quick-reset")
New-Item -ItemType Directory -Path (Join-Path $archiveRoot 'SaveData') -Force | Out-Null
$saveDataRoot = Assert-InsideProject("$testRoot/SaveData")
foreach ($file in @(Get-ChildItem -LiteralPath $saveDataRoot -File | Where-Object { $_.Name -ne 'seed.es3' })) {
    Move-Item -LiteralPath $file.FullName -Destination (Join-Path $archiveRoot 'SaveData' | Join-Path -ChildPath $file.Name)
}

if (-not (Test-Path -LiteralPath (Join-Path $saveDataRoot 'seed.es3'))) {
    Copy-Item -LiteralPath $latestQuick.FullName -Destination (Join-Path $saveDataRoot 'seed.es3')
}
Copy-Item -LiteralPath $latestQuick.FullName -Destination (Join-Path $saveDataRoot $latestQuick.Name)
$quickHash = (Get-FileHash -LiteralPath $latestQuick.FullName -Algorithm SHA256).Hash
$copiedQuickHash = (Get-FileHash -LiteralPath (Join-Path $saveDataRoot $latestQuick.Name) -Algorithm SHA256).Hash
if ($quickHash -ne $copiedQuickHash) { throw 'Copied quick save hash mismatch.' }
Copy-Item -LiteralPath $saveInfoPath -Destination (Join-Path $saveDataRoot "SaveInfo$quickNumber.es3")
foreach ($name in @('saveinfo.dat', 'SaveCGInfoBy.dat', 'SaveTuJianBy.dat')) {
    $path = Join-Path $formalSaveRoot $name
    if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination (Join-Path $saveDataRoot $name) }
}

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
$sentContinue = $false
for ($attempt = 0; $attempt -lt 120; $attempt++) {
    try {
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        if ($state.faulted) { break }
        $expectedFile = Join-Path $saveDataRoot $latestQuick.Name
        if ($state.isLogin -and $state.loadOver -and $state.canGame -and -not $state.loading -and -not $state.faulted -and
            ([string]$state.file).EndsWith($latestQuick.Name, [StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        if (-not $sentContinue -and $attempt -ge 10) {
            $sentContinue = [bool](TryTestCommand 'menu-continue')
        }
        elseif ($sentContinue -and $attempt % 10 -eq 0) {
            TryTestCommand 'menu-continue' | Out-Null
        }
    } catch {
        $state = $null
    }
    Start-Sleep -Seconds 2
}
$expectedFile = Join-Path $saveDataRoot $latestQuick.Name
if ($null -eq $state -or -not ($state.isLogin -and $state.loadOver -and $state.canGame -and -not $state.loading -and -not $state.faulted -and
    ([string]$state.file).EndsWith($latestQuick.Name, [StringComparison]::OrdinalIgnoreCase))) {
    throw 'The isolated game did not finish loading the latest production quick save.'
}

[pscustomobject]@{
    SourceQuick = $latestQuick.FullName
    SourceWriteTime = $latestQuick.LastWriteTime
    SandboxQuick = Join-Path $saveDataRoot $latestQuick.Name
    SHA256 = $quickHash
    Archive = $archiveRoot
    ProcessId = ($startOutput | Select-String -Pattern 'TEST_PID=(\d+)').Matches[0].Groups[1].Value
    LoadedFile = $state.file
    Position = $state.position
    SelectedCheckpoint = $state.selected
} | Format-List
