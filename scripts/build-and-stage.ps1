[CmdletBinding()]
param(
    [string]$GameDir = $env:STS2_GAME_DIR,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipStage,
    [switch]$SkipArchive
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$projectPath = Join-Path $repoRoot 'Sts2DpsMeter.csproj'
$manifestPath = Join-Path $repoRoot 'manifest.json'
$workshopRoot = Join-Path $repoRoot 'workshop'
$workshopManifestPath = Join-Path $workshopRoot 'workshop.json'
$workshopReadmePath = Join-Path $workshopRoot 'README.md'
$coverPath = Join-Path $workshopRoot 'image.png'
$contentDir = Join-Path $workshopRoot 'content'
$artifactDir = Join-Path $repoRoot 'artifacts\build'
$workshopArtifactDir = Join-Path $repoRoot 'artifacts\workshop'

function Get-SteamLibraryRoots {
    $roots = [System.Collections.Generic.List[string]]::new()
    $registryKeys = @(
        'HKCU:\Software\Valve\Steam',
        'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam',
        'HKLM:\SOFTWARE\Valve\Steam'
    )

    foreach ($key in $registryKeys) {
        try {
            $steamPath = (Get-ItemProperty -LiteralPath $key -Name SteamPath -ErrorAction Stop).SteamPath
            if (-not [string]::IsNullOrWhiteSpace($steamPath) -and (Test-Path -LiteralPath $steamPath)) {
                $roots.Add([System.IO.Path]::GetFullPath($steamPath))
            }
        }
        catch {
            # 当前注册表位置不存在时继续查找其他位置。
        }
    }

    foreach ($commonRoot in @('C:\Program Files (x86)\Steam', 'C:\Program Files\Steam')) {
        if ((Test-Path -LiteralPath $commonRoot -PathType Container) -and -not $roots.Contains($commonRoot)) {
            $roots.Add([System.IO.Path]::GetFullPath($commonRoot))
        }
    }

    $candidateRoots = $roots.ToArray()
    foreach ($root in $candidateRoots) {
        $libraryFile = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (-not (Test-Path -LiteralPath $libraryFile -PathType Leaf)) {
            continue
        }

        $content = Get-Content -LiteralPath $libraryFile -Raw
        foreach ($match in [regex]::Matches($content, '"path"\s+"(?<path>.+?)"')) {
            $libraryPath = $match.Groups['path'].Value -replace '\\\\', '\'
            if ((Test-Path -LiteralPath $libraryPath) -and -not $roots.Contains($libraryPath)) {
                $roots.Add([System.IO.Path]::GetFullPath($libraryPath))
            }
        }
    }

    return $roots
}

function Resolve-GameDirectory {
    param([string]$ConfiguredDirectory)

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredDirectory)) {
        return [System.IO.Path]::GetFullPath($ConfiguredDirectory)
    }

    foreach ($libraryRoot in Get-SteamLibraryRoots) {
        $candidate = Join-Path $libraryRoot 'steamapps\common\Slay the Spire 2'
        if (Test-Path -LiteralPath $candidate -PathType Container) {
            return [System.IO.Path]::GetFullPath($candidate)
        }
    }

    throw '未找到《杀戮尖塔2》目录。请设置 $env:STS2_GAME_DIR，或使用 -GameDir 参数指定。'
}

function Remove-ArtifactDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    $artifactRoot = [System.IO.Path]::GetFullPath($workshopArtifactDir)
    $target = [System.IO.Path]::GetFullPath($Path)
    $prefix = $artifactRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $target.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝删除 artifacts/workshop 之外的目录：$target"
    }

    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}

function New-WorkshopArchive {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$BuiltDll
    )

    if (-not (Test-Path -LiteralPath $workshopManifestPath -PathType Leaf)) {
        throw "缺少创意工坊配置：$workshopManifestPath"
    }
    if (-not (Test-Path -LiteralPath $coverPath -PathType Leaf)) {
        throw "缺少创意工坊封面：$coverPath"
    }

    New-Item -ItemType Directory -Force -Path $workshopArtifactDir | Out-Null
    $packageRoot = Join-Path $workshopArtifactDir ".staging-$Version"
    Remove-ArtifactDirectory -Path $packageRoot
    New-Item -ItemType Directory -Force -Path (Join-Path $packageRoot 'content') | Out-Null

    Copy-Item -LiteralPath $BuiltDll -Destination (Join-Path $packageRoot 'content\Sts2DpsMeter.dll') -Force
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $packageRoot 'content\manifest.json') -Force
    Copy-Item -LiteralPath $workshopManifestPath -Destination (Join-Path $packageRoot 'workshop.json') -Force
    Copy-Item -LiteralPath $coverPath -Destination (Join-Path $packageRoot 'image.png') -Force
    if (Test-Path -LiteralPath $workshopReadmePath -PathType Leaf) {
        Copy-Item -LiteralPath $workshopReadmePath -Destination (Join-Path $packageRoot 'README.md') -Force
    }

    $archivePath = Join-Path $workshopArtifactDir "Sts2DpsMeter-Ironclad-Test-$Version.zip"
    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }

    Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $archivePath -CompressionLevel Optimal
    Remove-ArtifactDirectory -Path $packageRoot

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName -replace '\\', '/' })
        $requiredEntries = @(
            'content/Sts2DpsMeter.dll',
            'content/manifest.json',
            'workshop.json',
            'image.png'
        )
        foreach ($entry in $requiredEntries) {
            if ($entries -notcontains $entry) {
                throw "创意工坊 ZIP 缺少条目：$entry"
            }
        }

        $forbiddenEntries = @('sts2.dll', 'GodotSharp.dll', '0Harmony.dll', 'mod-uploader.log')
        foreach ($entry in $entries) {
            $leaf = [System.IO.Path]::GetFileName($entry)
            if ($forbiddenEntries -contains $leaf) {
                throw "创意工坊 ZIP 包含禁止文件：$entry"
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    return $archivePath
}

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "缺少项目文件：$projectPath"
}
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "缺少 Mod 清单：$manifestPath"
}

$modManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$version = [string]$modManifest.version
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "manifest.json 的 version 不是三段式版本：$version"
}

$resolvedGameDir = Resolve-GameDirectory -ConfiguredDirectory $GameDir
$runningOnWindows = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT
$dataDirectoryName = if ($runningOnWindows) { 'data_sts2_windows_x86_64' } else { 'data_sts2_linuxbsd_x86_64' }
$dataDirectory = Join-Path $resolvedGameDir $dataDirectoryName

$requiredAssemblies = @('sts2.dll', 'GodotSharp.dll', '0Harmony.dll')
foreach ($assemblyName in $requiredAssemblies) {
    $assemblyPath = Join-Path $dataDirectory $assemblyName
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
        throw "游戏程序集不存在：$assemblyPath"
    }
}

New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
Write-Host "使用游戏目录：$resolvedGameDir"
Write-Host "开始构建：$Configuration，版本：$version"

& dotnet build $projectPath --configuration $Configuration --output $artifactDir -p:STS2GameDir="$resolvedGameDir"
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build 失败，退出码：$LASTEXITCODE"
}

$builtDll = Join-Path $artifactDir 'Sts2DpsMeter.dll'
if (-not (Test-Path -LiteralPath $builtDll -PathType Leaf)) {
    throw "构建产物不存在：$builtDll"
}

if (-not $SkipStage) {
    New-Item -ItemType Directory -Force -Path $contentDir | Out-Null
    Copy-Item -LiteralPath $builtDll -Destination (Join-Path $contentDir 'Sts2DpsMeter.dll') -Force
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $contentDir 'manifest.json') -Force
    Write-Host "已暂存 Steam 内容：$contentDir"
}

if (-not (Test-Path -LiteralPath $coverPath -PathType Leaf)) {
    Write-Warning '缺少 workshop/image.png；Steam 上传前必须替换为小于 1 MB 的封面。'
}
elseif ((Get-Item -LiteralPath $coverPath).Length -ge 1MB) {
    throw 'workshop/image.png 必须小于 1 MB。'
}

if (-not $SkipArchive) {
    $archivePath = New-WorkshopArchive -Version $version -BuiltDll $builtDll
    Write-Host "已生成可上传 Steam 创意工坊工作区：$archivePath" -ForegroundColor Green
}

Write-Host '构建完成。下一步请在 Steam 已登录的 Windows 机器上运行官方 ModUploader。' -ForegroundColor Green
