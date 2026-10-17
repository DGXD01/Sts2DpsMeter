using Godot;

namespace Sts2DpsMeter;

public enum HudTheme
{
    DefaultDark,
    GameBlend,
    HighContrast
}

// 只保存视觉角色；无统计、布局、透明度策略或玩家身份信息。
internal sealed record ThemeDefinition(
    HudTheme Id, Color PanelBackground, Color Border, Color Title, Color Accent,
    Color Primary, Color Muted, Color BarTrack, Color BarFill, Color ControlBackground,
    Color Hover, Color Pressed, Color SettingsBackground, Color OverlayBackground,
    Color Outline, Color LocalPlayer, Color Divider, Color SnapFill);

internal static class DpsHudThemes
{
    private static Color C(uint rgba) => new((rgba >> 24) / 255f, ((rgba >> 16) & 255) / 255f,
        ((rgba >> 8) & 255) / 255f, (rgba & 255) / 255f);

    private static readonly ThemeDefinition Dark = new(HudTheme.DefaultDark,
        C(0x000000EB), C(0xEBF2FFE0), C(0x7ABDFFFF), C(0x7AB8FFFF),
        C(0xFFFFFFFF), C(0xD1D9E6FF), C(0x1C1F25F5), C(0xD1141AFF), C(0x0F1114FA),
        C(0x14387E80), C(0x0D2E6BBF), C(0x090B12F7), C(0x090B12FA),
        C(0x000000FF), C(0x7FD3FFFF), C(0xEBF2FF2E), C(0x1A66CC14));
    private static readonly ThemeDefinition Blend = new(HudTheme.GameBlend,
        C(0x211C16EB), C(0xBDA376FF), C(0xF6D79FFF), C(0xEACB8FFF),
        C(0xFFF3DAFF), C(0xE0D0B5FF), C(0x30281EFF), C(0x713E2DFF), C(0x2D251CFF),
        C(0x51412EFF), C(0x3E3122FF), C(0x211C16FA), C(0x211C16FA),
        C(0x000000FF), C(0xFFE0A5FF), C(0xBDA37660), C(0xEACB8F18));
    private static readonly ThemeDefinition Contrast = new(HudTheme.HighContrast,
        C(0x000000FF), C(0xFFFFFFFF), C(0xFFEA00FF), C(0x65E8FFFF),
        C(0xFFFFFFFF), C(0xF2F2F2FF), C(0x242424FF), C(0x005368FF), C(0x090909FF),
        C(0x143B46FF), C(0x10414FFF), C(0x000000FF), C(0x000000FF),
        C(0x000000FF), C(0xFFEA00FF), C(0xFFFFFFB3), C(0x65E8FF20));

    internal static HudTheme Normalize(HudTheme theme) => theme is >= HudTheme.DefaultDark and <= HudTheme.HighContrast
        ? theme : HudTheme.DefaultDark;

    internal static ThemeDefinition Get(HudTheme theme) => Normalize(theme) switch
    {
        HudTheme.GameBlend => Blend,
        HudTheme.HighContrast => Contrast,
        _ => Dark
    };

    internal static string Label(HudTheme theme, bool chinese) => Normalize(theme) switch
    {
        HudTheme.GameBlend => chinese ? "游戏融合" : "Game Blend",
        HudTheme.HighContrast => chinese ? "高对比" : "High Contrast",
        _ => chinese ? "默认深色" : "Default Dark"
    };
}
