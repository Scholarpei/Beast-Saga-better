param([Parameter(Mandatory = $true)][string]$Command, [int]$TimeoutSeconds = 20)
$ErrorActionPreference = 'Stop'
$testRoot = (Resolve-Path "$PSScriptRoot/../sandbox/Game").Path
if (-not (Test-Path -LiteralPath "$testRoot/SAFE_DIALOGUE_TEST_COPY")) { throw 'Not an isolated test copy' }
if (Test-Path -LiteralPath "$testRoot/test-command.txt") { throw 'Previous test command is still pending' }
if (Test-Path -LiteralPath "$testRoot/test-result.txt") { Remove-Item -LiteralPath "$testRoot/test-result.txt" }
[IO.File]::WriteAllText("$testRoot/test-command.txt", $Command)
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
while (-not (Test-Path -LiteralPath "$testRoot/test-result.txt")) {
    if ([DateTime]::UtcNow -gt $deadline) { throw "Test command timed out: $Command" }
    Start-Sleep -Milliseconds 200
}
Get-Content -LiteralPath "$testRoot/test-result.txt"
if ($Command -eq 'state') { Get-Content -LiteralPath "$testRoot/test-state.json" }
