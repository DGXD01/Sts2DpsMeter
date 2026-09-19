[CmdletBinding()]
param(
    [string]$GameDir = $env:STS2_GAME_DIR,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipStage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$projectPath = Join-Path $repoRoot 'Sts2DpsMeter.csproj'
$manifestPath = Join-Path $repoRoot 'manifest.json'
$artifactDir = Join-Path $repoRoot 'artifacts\build'
$contentDir = Join-Path $repoRoot 'workshop\content'

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

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "缺少项目文件：$projectPath"
}
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "缺少 Mod 清单：$manifestPath"
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
Write-Host "开始构建：$Configuration"

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

$coverPath = Join-Path $repoRoot 'workshop\image.png'
if (-not (Test-Path -LiteralPath $coverPath -PathType Leaf)) {
    Write-Warning '缺少 workshop/image.png；Steam 上传前必须替换为小于 1 MB 的封面。'
}
elseif ((Get-Item -LiteralPath $coverPath).Length -ge 1MB) {
    throw 'workshop/image.png 必须小于 1 MB。'
}

Write-Host '构建完成。下一步请在 Steam 已登录的 Windows 机器上运行官方 ModUploader。' -ForegroundColor Green