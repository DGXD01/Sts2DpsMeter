[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [Parameter(Mandatory = $true)][string]$ModDll,
    [Parameter(Mandatory = $true)][string]$WorkDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$gameData = Join-Path ([IO.Path]::GetFullPath($GameDir)) 'data_sts2_windows_x86_64'
$modPath = (Resolve-Path -LiteralPath $ModDll).Path
$godotPath = (Resolve-Path -LiteralPath (Join-Path $gameData 'GodotSharp.dll')).Path
$workPath = [IO.Path]::GetFullPath($WorkDir)
$sourcePath = Join-Path $PSScriptRoot 'hud-refresh-checks\Program.cs.txt'
$stageThreeSourcePath = Join-Path $PSScriptRoot 'hud-refresh-checks\StageThreeChecks.cs.txt'
$stageFourSourcePath = Join-Path $PSScriptRoot 'hud-refresh-checks\StageFourChecks.cs.txt'
New-Item -ItemType Directory -Path $workPath -Force | Out-Null

function Escape-Xml([string]$Value) {
    return [Security.SecurityElement]::Escape($Value)
}

# 测试项目和构建产物全部写入调用方指定的“辅助生成文件”子目录。
$project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <UseAppHost>false</UseAppHost>
    <RollForward>Major</RollForward>
    <Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$(Escape-Xml $sourcePath)" />
    <Compile Include="$(Escape-Xml $stageThreeSourcePath)" />
    <Compile Include="$(Escape-Xml $stageFourSourcePath)" />
    <Reference Include="Sts2DpsMeter"><HintPath>$(Escape-Xml $modPath)</HintPath></Reference>
    <Reference Include="GodotSharp"><HintPath>$(Escape-Xml $godotPath)</HintPath></Reference>
    <Reference Include="sts2"><HintPath>$(Escape-Xml (Join-Path $gameData 'sts2.dll'))</HintPath></Reference>
  </ItemGroup>
</Project>
"@
$projectPath = Join-Path $workPath 'HudRefreshChecks.csproj'
[IO.File]::WriteAllText($projectPath, $project, [Text.UTF8Encoding]::new($false))

& dotnet build $projectPath --configuration Release --disable-build-servers -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) {
    throw 'HUD 刷新检查项目构建失败。'
}

& dotnet (Join-Path $workPath 'bin\Release\net9.0\HudRefreshChecks.dll') (Join-Path $workPath '图标预览')
if ($LASTEXITCODE -ne 0) {
    throw 'HUD 刷新检查失败。'
}
