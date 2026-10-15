using System;
using Godot;

namespace Sts2DpsMeter;

internal static class DpsHudTypography
{
    internal const int BodySize = 16;
    internal const int CompactBodySize = 14;
    internal const int HeaderSize = 16;
    internal const int DetailSize = 13;
    internal const int OutlineSize = 1;
    internal const int SettingsTitleSize = 18;
    internal const int SettingsNoteSize = 14;

    private static Theme? _theme;
    private static bool _initialized;

    internal static void Apply(Control panel)
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
        if (_theme is not null && GodotObject.IsInstanceValid(_theme)) panel.Theme = _theme;
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
