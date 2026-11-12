Set-StrictMode -Version 3.0
$script:DeploymentPluginName = 'BeastSagaBetter'
$script:DeploymentLegacyPluginName = 'SafeDialogueTools'

function Get-DeploymentInputFiles {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ProjectRoot
    )

    $pluginRoot = Join-Path $ProjectRoot 'Plugin'
    $coreRoot = Join-Path $ProjectRoot 'Core'
    $roots = @()
    if (Test-Path -LiteralPath $pluginRoot) { $roots += $pluginRoot }
    if (Test-Path -LiteralPath $coreRoot) { $roots += $coreRoot }
    if ($roots.Count -eq 0) { throw "找不到 Plugin/Core 源目录：$ProjectRoot" }

    foreach ($root in $roots) {
        Get-ChildItem -LiteralPath $root -Recurse -File -Include *.cs, *.csproj |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
    }
}

function Test-DeploymentInputsFresh {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ProjectRoot,
        [Parameter(Mandatory = $true)]
        [string] $ArtifactPath
    )

    if (-not (Test-Path -LiteralPath $ArtifactPath)) {
        return [pscustomobject]@{ Fresh = $false; Reason = "找不到构建产物：$ArtifactPath" }
    }
    $artifactTime = (Get-Item -LiteralPath $ArtifactPath).LastWriteTimeUtc
    $newest = Get-DeploymentInputFiles -ProjectRoot $ProjectRoot |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $newest) {
        return [pscustomobject]@{ Fresh = $true; Reason = $null }
    }
    if ($newest.LastWriteTimeUtc -gt $artifactTime) {
        return [pscustomobject]@{
            Fresh = $false
            Reason = "构建产物早于源码或项目文件 $($newest.Name)，请去掉 -SkipBuild 重新构建。"
        }
    }
    return [pscustomobject]@{ Fresh = $true; Reason = $null }
}

function New-DeploymentPlan {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ProjectRoot,
        [Parameter(Mandatory = $true)]
        [string] $GameDir,
        [string] $PluginDir
    )

    $pluginRoot = Join-Path $GameDir 'BepInEx/plugins'
    $defaultPluginDir = Join-Path $pluginRoot $script:DeploymentPluginName
    $legacyPluginDir = Join-Path $pluginRoot $script:DeploymentLegacyPluginName
    $targetPluginDir = if ([string]::IsNullOrWhiteSpace($PluginDir)) { $defaultPluginDir } else { [IO.Path]::GetFullPath($PluginDir) }

    $newExists = Test-Path -LiteralPath $defaultPluginDir
    $legacyExists = Test-Path -LiteralPath $legacyPluginDir
    if ($newExists -and $legacyExists) {
        throw "新旧插件目录不能同时存在：$defaultPluginDir / $legacyPluginDir。请确认后只保留一个，或用 -PluginDir 显式指定目标。"
    }

    $requiresMigration = $false
    $allowCreate = $false
    if (-not [string]::IsNullOrWhiteSpace($PluginDir)) {
        $allowCreate = -not (Test-Path -LiteralPath $targetPluginDir)
    } elseif ($legacyExists) {
        $requiresMigration = $true
    }

    [pscustomobject]@{
        GameDir = [IO.Path]::GetFullPath($GameDir)
        PluginDir = $targetPluginDir
        LegacyPluginDir = if ($legacyExists) { $legacyPluginDir } else { $null }
        RequiresMigration = $requiresMigration
        AllowCreate = $allowCreate
    }
}

function Backup-DirectoryTree {
    param(
        [Parameter(Mandatory = $true)]
        [string] $SourceDirectory,
        [Parameter(Mandatory = $true)]
        [string] $BackupDirectory
    )

    if (-not (Test-Path -LiteralPath $SourceDirectory -PathType Container)) {
        throw "找不到要备份的目录：$SourceDirectory"
    }
    if (Test-Path -LiteralPath $BackupDirectory) {
        throw "备份目录已存在：$BackupDirectory"
    }
    New-Item -ItemType Directory -Path $BackupDirectory | Out-Null

    $hashes = [ordered]@{}
    $files = Get-ChildItem -LiteralPath $SourceDirectory -Recurse -File
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($SourceDirectory.Length).TrimStart('\', '/')
        $destination = Join-Path $BackupDirectory $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        Copy-Item -LiteralPath $file.FullName -Destination $destination
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $hash) {
            throw "备份哈希不一致：$($file.FullName)"
        }
        $hashes[$relative] = $hash
    }

    [pscustomobject]@{ FileCount = $files.Count; Hashes = $hashes }
}

function New-VerifiedTemporaryCopy {
    param(
        [Parameter(Mandatory = $true)]
        [string] $SourceFile,
        [Parameter(Mandatory = $true)]
        [string] $DestinationDirectory
    )

    if (-not (Test-Path -LiteralPath $SourceFile -PathType Leaf)) {
        throw "找不到源文件：$SourceFile"
    }
    if (-not (Test-Path -LiteralPath $DestinationDirectory -PathType Container)) {
        throw "找不到目标目录：$DestinationDirectory"
    }

    $temporaryFile = Join-Path $DestinationDirectory ('.' + [IO.Path]::GetFileName($SourceFile) + '.' + [guid]::NewGuid().ToString('N') + '.tmp')
    Copy-Item -LiteralPath $SourceFile -Destination $temporaryFile
    try {
        $sourceHash = (Get-FileHash -LiteralPath $SourceFile -Algorithm SHA256).Hash
        $temporaryHash = (Get-FileHash -LiteralPath $temporaryFile -Algorithm SHA256).Hash
        if ($temporaryHash -ne $sourceHash) { throw "临时文件哈希不一致：$temporaryFile" }
        return [pscustomobject]@{ Path = $temporaryFile; SHA256 = $temporaryHash }
    } catch {
        if (Test-Path -LiteralPath $temporaryFile) { Remove-Item -LiteralPath $temporaryFile }
        throw
    }
}

function Publish-VerifiedFile {
    param(
        [Parameter(Mandatory = $true)]
        [string] $TemporaryFile,
        [Parameter(Mandatory = $true)]
        [string] $DestinationFile,
        [Parameter(Mandatory = $true)]
        [string] $ExpectedHash
    )

    if ((Get-FileHash -LiteralPath $TemporaryFile -Algorithm SHA256).Hash -ne $ExpectedHash) {
        throw "临时文件发布前哈希不一致：$TemporaryFile"
    }
    Move-Item -LiteralPath $TemporaryFile -Destination $DestinationFile -Force
    if ((Get-FileHash -LiteralPath $DestinationFile -Algorithm SHA256).Hash -ne $ExpectedHash) {
        throw "目标文件发布后哈希不一致：$DestinationFile"
    }
}

function Remove-LegacyDistArtifacts {
    param(
        [Parameter(Mandatory = $true)]
        [string] $DistDirectory
    )

    if (-not (Test-Path -LiteralPath $DistDirectory)) { return @() }
    $legacyNames = @('SafeDialogueTools.dll', 'SafeDialogueTools.pdb')
    $removed = @()
    foreach ($name in $legacyNames) {
        $path = Join-Path $DistDirectory $name
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            Remove-Item -LiteralPath $path
            $removed += $path
        }
    }
    return $removed
}

function Disable-DirectoryTree {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Directory
    )

    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
        throw "找不到要禁用的目录：$Directory"
    }
    $disabled = Join-Path $Directory '.disabled'
    if (Test-Path -LiteralPath $disabled) {
        throw "禁用目录已存在：$disabled"
    }
    New-Item -ItemType Directory -Path $disabled | Out-Null

    Get-ChildItem -LiteralPath $Directory -Force |
        Where-Object { $_.Name -ne '.disabled' } |
        ForEach-Object {
            $destination = Join-Path $disabled $_.Name
            if (Test-Path -LiteralPath $destination) {
                throw "禁用目标已存在：$destination"
            }
            Move-Item -LiteralPath $_.FullName -Destination $destination
        }
}

function Restore-DisabledDirectoryTree {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Directory
    )

    $disabled = Join-Path $Directory '.disabled'
    if (-not (Test-Path -LiteralPath $disabled -PathType Container)) {
        throw "找不到要恢复的禁用目录：$disabled"
    }

    Get-ChildItem -LiteralPath $disabled -Force |
        ForEach-Object {
            $destination = Join-Path $Directory $_.Name
            if (Test-Path -LiteralPath $destination) {
                throw "恢复目标已存在：$destination"
            }
            Move-Item -LiteralPath $_.FullName -Destination $destination
        }
    Remove-Item -LiteralPath $disabled
}
