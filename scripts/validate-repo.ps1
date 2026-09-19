[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$errors = [System.Collections.Generic.List[string]]::new()

function Add-ValidationError {
    param([Parameter(Mandatory = $true)][string]$Message)
    $errors.Add($Message)
}

function Read-JsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Add-ValidationError "缺少文件：$Path"
        return $null
    }

    try {
        return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    }
    catch {
        Add-ValidationError "JSON 格式错误：$Path（$($_.Exception.Message)）"
        return $null
    }
}

$modManifestPath = Join-Path $repoRoot 'manifest.json'
$workshopManifestPath = Join-Path $repoRoot 'workshop\workshop.json'
$projectPath = Join-Path $repoRoot 'Sts2DpsMeter.csproj'
$imagePath = Join-Path $repoRoot 'workshop\image.png'

$mod = Read-JsonFile -Path $modManifestPath
$workshop = Read-JsonFile -Path $workshopManifestPath

if ($null -ne $mod) {
    if ($mod.id -ne 'Sts2DpsMeter') {
        Add-ValidationError "Mod id 必须为 Sts2DpsMeter，实际为：$($mod.id)"
    }
    if ($mod.has_dll -ne $true) {
        Add-ValidationError 'manifest.json 必须设置 has_dll = true。'
    }
    if ($mod.has_pck -ne $false) {
        Add-ValidationError '当前项目没有 PCK，manifest.json 必须设置 has_pck = false。'
    }
    if ([string]::IsNullOrWhiteSpace($mod.version) -or $mod.version -notmatch '^\d+\.\d+\.\d+$') {
        Add-ValidationError "Mod version 必须为三段式语义化版本，实际为：$($mod.version)"
    }
    if ([string]::IsNullOrWhiteSpace($mod.min_game_version) -or $mod.min_game_version -notmatch '^\d+\.\d+\.\d+$') {
        Add-ValidationError "min_game_version 必须为三段式语义化版本，实际为：$($mod.min_game_version)"
    }
}

if ($null -ne $workshop) {
    if ([string]::IsNullOrWhiteSpace($workshop.title)) {
        Add-ValidationError 'workshop/workshop.json 缺少 title。'
    }
    if ([string]::IsNullOrWhiteSpace($workshop.visibility)) {
        Add-ValidationError 'workshop/workshop.json 缺少 visibility。'
    }
    if ($null -eq $workshop.tags) {
        Add-ValidationError 'workshop/workshop.json 缺少 tags。'
    }
}

$projectVersion = $null
if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    Add-ValidationError "缺少文件：$projectPath"
}
else {
    $projectText = Get-Content -LiteralPath $projectPath -Raw
    $versionMatch = [regex]::Match($projectText, '<Version>\s*(?<version>[^<]+?)\s*</Version>')
    if (-not $versionMatch.Success) {
        Add-ValidationError 'Sts2DpsMeter.csproj 缺少 <Version> 字段。'
    }
    else {
        $projectVersion = $versionMatch.Groups['version'].Value
        if ($null -ne $mod -and $projectVersion -ne $mod.version) {
            Add-ValidationError "项目版本 $projectVersion 与 Mod 清单版本 $($mod.version) 不一致。"
        }
    }
}

$requiredPaths = @(
    (Join-Path $repoRoot 'ModEntry.cs'),
    (Join-Path $repoRoot 'DpsHook.cs'),
    (Join-Path $repoRoot 'DpsStats.cs'),
    (Join-Path $repoRoot 'DpsModels.cs'),
    (Join-Path $repoRoot 'DpsMeterUi.cs'),
    (Join-Path $repoRoot 'manifest.json'),
    (Join-Path $repoRoot 'docs\开发与更新流程.md')
)
foreach ($requiredPath in $requiredPaths) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        Add-ValidationError "缺少必要路径：$requiredPath"
    }
}

if (-not (Test-Path -LiteralPath $imagePath -PathType Leaf)) {
    Add-ValidationError '缺少 workshop/image.png。上传前必须放入小于 1 MB 的封面。'
}
elseif ((Get-Item -LiteralPath $imagePath).Length -ge 1MB) {
    Add-ValidationError 'workshop/image.png 不能大于或等于 1 MB。'
}

$scriptFiles = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -Recurse | Where-Object { -not $_.PSIsContainer }
foreach ($scriptFile in $scriptFiles) {
    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($scriptFile.FullName, [ref]$tokens, [ref]$parseErrors) | Out-Null
    if (@($parseErrors).Count -gt 0) {
        foreach ($parseError in @($parseErrors)) {
            Add-ValidationError "PowerShell 语法错误：$($scriptFile.FullName): $($parseError.Message)"
        }
    }
}

$forbiddenNames = @('sts2.dll', 'GodotSharp.dll', '0Harmony.dll', 'mod-uploader.log')
$forbiddenFiles = Get-ChildItem -LiteralPath $repoRoot -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $forbiddenNames -contains $_.Name }
foreach ($forbiddenFile in $forbiddenFiles) {
    Add-ValidationError "禁止提交游戏程序集或上传器日志：$($forbiddenFile.FullName)"
}

if ($errors.Count -gt 0) {
    Write-Host '仓库校验失败：' -ForegroundColor Red
    foreach ($errorItem in $errors) {
        Write-Host " - $errorItem" -ForegroundColor Red
    }
    exit 1
}

Write-Host '仓库校验通过。' -ForegroundColor Green
Write-Host "Mod 版本：$($mod.version)"
Write-Host '下一步：设置 STS2_GAME_DIR 后运行 scripts/build-and-stage.ps1。'