[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GodotExe,
    [Parameter(Mandatory = $true)][string]$MetricsJson,
    [Parameter(Mandatory = $true)][string]$WorkDir
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$enginePath = (Resolve-Path -LiteralPath $GodotExe).Path
if ([IO.Path]::GetFileName($enginePath) -notmatch '^Godot_v4\.5\.1-stable_win64(_console)?\.exe$') {
    throw '只允许独立官方Godot4.5.1，不能使用游戏程序。'
}
$workPath = [IO.Path]::GetFullPath($WorkDir)
New-Item -ItemType Directory -Path $workPath -Force | Out-Null
$typography = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../DpsHudTypography.cs') -Raw
$familyBlock = [regex]::Match($typography, 'FontNames\s*=\s*new\[\]\s*\{([^}]+)\}').Groups[1].Value
$families = @([regex]::Matches($familyBlock, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../DpsMeterUi.CombatSummary.cs') -Raw
$overrun = [regex]::Match($source, 'TextOverrunBehavior\s*=\s*TextServer\.OverrunBehavior\.(\w+)').Groups[1].Value
$maxLines = [regex]::Match($source, 'MaxLinesVisible\s*=\s*(\d+)').Groups[1].Value
if ($families.Count -lt 1 -or $overrun -ne 'NoTrimming' -or $maxLines -ne '2') { throw '产品字体或总结布局已改变，请更新检查。' }
$metrics = Get-Content -LiteralPath $MetricsJson -Raw | ConvertFrom-Json
$config = @{ metrics = @($metrics); families = $families; overrun = $overrun; maxLines = [int]$maxLines } | ConvertTo-Json -Depth 30
$project = @'
config_version=5
[application]
config/name="HUD font layout checks"
[rendering]
renderer/rendering_method="gl_compatibility"
[debug]
file_logging/enable_file_logging=false
'@
[IO.File]::WriteAllText((Join-Path $workPath 'project.godot'), $project, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $workPath 'production.json'), $config, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'hud-refresh-checks/FontNativeLayout.gd') -Destination (Join-Path $workPath 'check.gd') -Force
& $enginePath --headless --path $workPath --log-file (Join-Path $workPath 'engine.log') --script res://check.gd
if ($LASTEXITCODE -ne 0) { throw '独立字体原生布局检查失败。' }
