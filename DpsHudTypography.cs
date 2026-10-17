using System;
using System.Collections.Generic;
using Godot;

namespace Sts2DpsMeter;

internal enum HudFontRole { Body, CompactBody, Header, Detail, SettingsTitle, Note, Navigation, ModuleHeading }

internal static class DpsHudTypography
{
    internal const int BodySize = 16;
    internal const int CompactBodySize = 14;
    internal const int HeaderSize = 16;
    internal const int DetailSize = 13;
    internal const int OutlineSize = 1;
    internal const int RowVerticalPadding = 4 + 2 * OutlineSize;
    internal const int SettingsTitleSize = 18;
    internal const int SettingsNoteSize = 14;

    private static readonly float[] ScaleValues = { 0.8f, 0.9f, 1f, 1.1f, 1.25f, 1.5f };
    internal static IReadOnlyList<float> Scales { get; } = Array.AsReadOnly(ScaleValues);

    internal static float NormalizeScale(float value)
    {
        if (!float.IsFinite(value)) return 1f;
        value = Math.Clamp(value, ScaleValues[0], ScaleValues[^1]);
        var nearest = ScaleValues[0];
        foreach (var candidate in ScaleValues)
            if (MathF.Abs(candidate - value) < MathF.Abs(nearest - value)) nearest = candidate;
        return nearest;
    }

    internal static int ScaleIndex(float value) => Array.IndexOf(ScaleValues, NormalizeScale(value));

    internal static int Size(HudFontRole role, float fontScale)
    {
        var basis = role switch
        {
            HudFontRole.CompactBody or HudFontRole.ModuleHeading => CompactBodySize,
            HudFontRole.Header => HeaderSize,
            HudFontRole.Detail => DetailSize,
            HudFontRole.SettingsTitle => SettingsTitleSize,
            HudFontRole.Note => SettingsNoteSize,
            HudFontRole.Navigation => 20,
            _ => BodySize
        };
        return Math.Max(1, (int)MathF.Round(basis * NormalizeScale(fontScale), MidpointRounding.AwayFromZero));
    }

    internal static float ContentWidth(float basis, float fontScale) => basis * MathF.Max(1f, NormalizeScale(fontScale));
    internal static float RowHeight(bool compact, float textHeight) => MathF.Ceiling(MathF.Max(compact ? 26 : 30, textHeight + RowVerticalPadding));

    private static Theme? _theme;
    private static bool _initialized;

    internal static Theme CopyTheme(float fontScale)
    {
        EnsureInitialized();
        var theme = _theme?.Duplicate() as Theme ?? new Theme();
        var body = Size(HudFontRole.Body, fontScale);
        theme.DefaultFontSize = body;
        foreach (var type in new[] { "Label", "Button", "OptionButton", "CheckBox" }) theme.SetFontSize("font_size", type, body);
        foreach (var key in new[] { "normal_font_size", "bold_font_size", "italics_font_size", "bold_italics_font_size" })
            theme.SetFontSize(key, "RichTextLabel", body);
        theme.SetFontSize("font_size", "PopupMenu", Size(HudFontRole.CompactBody, fontScale));
        theme.SetFontSize("font_size", "TooltipLabel", Size(HudFontRole.CompactBody, fontScale));
        return theme;
    }

    private static void EnsureInitialized()
    {
        if (!_initialized)
        {
            _initialized = true;
            try
            {
                // 独立字体实例，不修改游戏共享字体；让系统为中文及符号补齐字形。
                var regular = CreateFont(400);
                var bold = CreateFont(700);
                var theme = new Theme { DefaultFont = regular, DefaultFontSize = BodySize };
                theme.SetFont("normal_font", "RichTextLabel", regular);
                theme.SetFont("bold_font", "RichTextLabel", bold);
                theme.SetFont("italics_font", "RichTextLabel", regular);
                theme.SetFont("bold_italics_font", "RichTextLabel", bold);
                foreach (var type in new[] { "Label", "RichTextLabel", "Button", "OptionButton", "PopupMenu", "TooltipLabel" })
                {
                    theme.SetConstant("outline_size", type, OutlineSize);
                }
                theme.SetFontSize("font_size", "PopupMenu", CompactBodySize);
                theme.SetFontSize("font_size", "TooltipLabel", CompactBodySize);
                _theme = theme;
            }
            catch (Exception exception)
            {
                // 字体不可用时仍创建面板，继续使用引擎默认主题。
                GD.PushWarning($"[{ModEntry.ModId}] 界面字体初始化失败，使用默认字体：{exception.Message}");
            }
        }
    }

    private static SystemFont CreateFont(int weight) => new()
    {
        FontNames = new[] { "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "Segoe UI", "Arial" },
        FontWeight = weight,
        AllowSystemFallback = true,
        Oversampling = 2f,
        Hinting = TextServer.Hinting.Normal,
        SubpixelPositioning = TextServer.SubpixelPositioning.Auto
    };
}
