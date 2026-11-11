param([int]$Port = 5080)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/..").Path
$dotnet = Join-Path $root 'tools/sdk/dotnet.exe'
$checkpointRoot = [IO.Path]::GetFullPath((Join-Path $root 'sandbox/Game/BeastSagaBetterData/checkpoints'))
$env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
& $dotnet run --project (Join-Path $root 'tools/PanelPreview/PanelPreview.csproj') -c Release --no-launch-profile -- $checkpointRoot
