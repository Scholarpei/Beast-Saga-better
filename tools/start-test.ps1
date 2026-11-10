param([switch]$Graphical)
$ErrorActionPreference = 'Stop'
$testRoot = (Resolve-Path "$PSScriptRoot/../sandbox/Game").Path
$testExecutable = [IO.Path]::GetFullPath("$testRoot/BeastSaga.exe")
if (-not (Test-Path -LiteralPath "$testRoot/SAFE_DIALOGUE_TEST_COPY")) { throw 'Not an isolated test copy' }
$testProcesses = @(Get-Process -Name BeastSaga -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $testExecutable })
if ($testProcesses.Count -gt 1) { throw 'Multiple isolated processes found; inspect them before restarting' }
if ($testProcesses.Count -eq 1) {
    & "$PSScriptRoot/send-test.ps1" -Command 'quit'
    $testProcesses | Wait-Process -Timeout 10
}
Copy-Item -LiteralPath "$PSScriptRoot/../Plugin/bin/Release/net6.0/SafeDialogueTools.dll" -Destination "$testRoot/BepInEx/plugins/SafeDialogueTools/SafeDialogueTools.dll"
$arguments = if ($Graphical) { @('-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720') } else { @('-batchmode', '-nographics') }
$arguments += @('-logFile', "`"$testRoot/Unity-test.log`"")
$process = Start-Process -FilePath $testExecutable -WorkingDirectory $testRoot -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output "TEST_PID=$($process.Id)"
