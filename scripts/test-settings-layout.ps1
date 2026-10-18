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
    throw '仅允许独立官方Godot4.5.1，不使用游戏程序。'
}
$workPath = [IO.Path]::GetFullPath($WorkDir)
New-Item -ItemType Directory -Path $workPath -Force | Out-Null
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../DpsMeterSettingsUi.Sections.cs') -Raw
$common = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../DpsMeterSettingsUi.cs') -Raw
$typography = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../DpsHudTypography.cs') -Raw
$familyBlock = [regex]::Match($typography, 'FontNames\s*=\s*new\[\]\s*\{([^}]+)\}').Groups[1].Value
$families = @([regex]::Matches($familyBlock, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
if ($families.Count -lt 1 -or $common -notmatch 'HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled') { throw '字体或产品滚动结构改变，请更新检查。' }
$metrics = Get-Content -LiteralPath $MetricsJson -Raw | ConvertFrom-Json
$pattern = '(?:Add(?<type>SummaryToggle|Toggle|OptionRow|SliderRow|Text)\(\s*|\bText\s*=\s*)DpsLocalization.Get\(\s*"(?<cn>(?:[^"\\]|\\.)*)"\s*,\s*"(?<en>(?:[^"\\]|\\.)*)"'
$groups = @{}
foreach ($section in @($metrics.sections) + @(@{ id = 'Footer' })) {
    $method = if ($section.id -eq 'Footer') { 'AddFooter' } else { 'Add' + $section.id + 'Settings' }
    $body = [regex]::Match($source, '(?s)private static void ' + $method + '\([^)]*\)\s*\{(.*?)(?=\n    private static|\n\})').Groups[1].Value
    if ([string]::IsNullOrWhiteSpace($body)) { throw "未找到真实产品设置组：$method" }
    $items = @([regex]::Matches($body, $pattern) | ForEach-Object {
        $type = $_.Groups['type'].Value
        if ($type -eq '') { $type = 'Button' }
        if ($type -eq 'SummaryToggle') { $type = 'Toggle' }
        @{ type = $type; cn = $_.Groups['cn'].Value; en = $_.Groups['en'].Value }
    })
    if ($items.Count -lt 1) { throw "设置组没有解析出任何控件：$method" }
    $groups[$section.id] = $items
}
$introduction = [regex]::Match($common, 'Text = DpsLocalization.Get\("点击 HUD 齿轮.*?", "([^"]+)"\)').Groups[1].Value
$config = @{ metrics = $metrics; groups = $groups; families = $families; introduction = $introduction } | ConvertTo-Json -Depth 30
$project = @'
config_version=5
[application]
config/name="HUD settings layout checks"
[rendering]
renderer/rendering_method="gl_compatibility"
[debug]
file_logging/enable_file_logging=false
'@
[IO.File]::WriteAllText((Join-Path $workPath 'project.godot'), $project, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $workPath 'production.json'), $config, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'hud-refresh-checks/SettingsNativeLayout.gd') -Destination (Join-Path $workPath 'check.gd') -Force
& $enginePath --headless --path $workPath --log-file (Join-Path $workPath 'engine.log') --script res://check.gd
if ($LASTEXITCODE -ne 0) { throw '独立设置原生布局检查失败。' }
