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
$bepInExConfig = Join-Path $testRoot 'BepInEx/config/BepInEx.cfg'
if (Test-Path -LiteralPath $bepInExConfig) {
    $lines = @(Get-Content -LiteralPath $bepInExConfig)
    $inConsole = $false
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match '^\[Logging\.Console\]') { $inConsole = $true; continue }
        if ($lines[$index] -match '^\[') { $inConsole = $false; continue }
        if ($inConsole -and $lines[$index] -match '^(\s*Enabled\s*=\s*)true\s*$') {
            $lines[$index] = $Matches[1] + 'false'
        }
    }
    Set-Content -LiteralPath $bepInExConfig -Value $lines -Encoding UTF8
}
Copy-Item -LiteralPath "$PSScriptRoot/../Plugin/bin/Release/net6.0/BeastSagaBetter.dll" -Destination "$testRoot/BepInEx/plugins/BeastSagaBetter/BeastSagaBetter.dll"
$arguments = if ($Graphical) { @('-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720') } else { @('-batchmode', '-nographics') }
$arguments += @('-logFile', "`"$testRoot/Unity-test.log`"")
$windowStyle = if ($Graphical) { 'Normal' } else { 'Hidden' }
$process = Start-Process -FilePath $testExecutable -WorkingDirectory $testRoot -ArgumentList $arguments -WindowStyle $windowStyle -PassThru
if ($Graphical) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class TestGameFocus {
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@
    for ($attempt = 0; $attempt -lt 40 -and $process.MainWindowHandle -eq 0; $attempt++) {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
    }
    if ($process.MainWindowHandle -ne 0) {
        [void][TestGameFocus]::ShowWindow($process.MainWindowHandle, 9)
        [void][TestGameFocus]::SetForegroundWindow($process.MainWindowHandle)
    }
}
Write-Output "TEST_PID=$($process.Id)"
