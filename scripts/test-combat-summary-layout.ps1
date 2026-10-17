[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GodotExe,
    [Parameter(Mandatory = $true)][string]$WorkDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$enginePath = (Resolve-Path -LiteralPath $GodotExe).Path
if ([IO.Path]::GetFileName($enginePath) -notmatch '^Godot_v4\.5\.1-stable_win64(_console)?\.exe$') {
    throw '本检查只接受独立官方 Godot 4.5.1，引擎不能使用游戏程序。'
}
$workPath = [IO.Path]::GetFullPath($WorkDir)
New-Item -ItemType Directory -Path $workPath -Force | Out-Null
$sourcePath = Join-Path $PSScriptRoot '../DpsMeterUi.CombatSummary.cs'
$source = Get-Content -LiteralPath $sourcePath -Raw
$overrun = [regex]::Match($source, 'TextOverrunBehavior\s*=\s*TextServer\.OverrunBehavior\.(\w+)').Groups[1].Value
$wrap = [regex]::Match($source, 'AutowrapMode\s*=\s*TextServer\.AutowrapMode\.(\w+)').Groups[1].Value
$maxLines = [regex]::Match($source, 'MaxLinesVisible\s*=\s*(\d+)').Groups[1].Value
if (-not $overrun -or -not $wrap -or -not $maxLines) { throw '无法读取实际产品 Label 布局设置。' }
$config = @{ overrun = $overrun; wrap = $wrap; max_lines = [int]$maxLines } | ConvertTo-Json
$project = @'
config_version=5
[application]
config/name="Combat summary native layout checks"
[rendering]
renderer/rendering_method="gl_compatibility"
[debug]
file_logging/enable_file_logging=false
'@
[IO.File]::WriteAllText((Join-Path $workPath 'project.godot'), $project, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $workPath 'production-label.json'), $config, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'hud-refresh-checks/CombatSummaryNativeLayout.gd') -Destination (Join-Path $workPath 'check.gd') -Force
# 日志明确落在辅助目录，避免默认 user:// 路径需要访问用户配置目录。
& $enginePath --headless --path $workPath --log-file (Join-Path $workPath 'engine.log') --script res://check.gd
if ($LASTEXITCODE -ne 0) { throw '总结原生布局检查失败。' }
