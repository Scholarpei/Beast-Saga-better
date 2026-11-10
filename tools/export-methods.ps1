param([string]$GameDir = (Resolve-Path "$PSScriptRoot/../..").Path)
$ErrorActionPreference = 'Stop'
Add-Type -Path "$GameDir/BepInEx/core/Mono.Cecil.dll"
$result = @{}
foreach ($name in @('Model.Runtime', 'Dialogue', 'LJGFramework.RunTime', 'UnityEngine.CoreModule', 'UnityEngine.InputLegacyModule', 'FairyGUI', 'Il2Cppmscorlib')) {
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$GameDir/BepInEx/interop/$name.dll")
    $methods = @{}
    foreach ($type in $assembly.MainModule.GetTypes()) {
        foreach ($method in $type.Methods) {
            $methods[[string]$method.MetadataToken.ToInt32()] = $method.FullName
        }
    }
    $result[$name] = $methods
    $assembly.Dispose()
}
$result | ConvertTo-Json -Depth 4 -Compress | Set-Content -LiteralPath "$PSScriptRoot/methods.json" -Encoding utf8
