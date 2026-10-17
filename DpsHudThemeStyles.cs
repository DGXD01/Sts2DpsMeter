using System.Collections.Generic;
using Godot;

namespace Sts2DpsMeter;

internal enum HudPanelKind { Main, Settings, Tooltip, Summary, Snap }

// Mod 独享、按主题缓存；不修改游戏主题或字体实例。所有主题使用相同边距和控件尺寸。
internal static class DpsHudThemeStyles
{
    private static readonly Dictionary<HudTheme, Theme> Cache = new();

    internal static void Apply(Control control, HudTheme id)
    {
        id = DpsHudThemes.Normalize(id);
        if (!Cache.TryGetValue(id, out var theme) || !GodotObject.IsInstanceValid(theme))
        {
            theme = Build(DpsHudThemes.Get(id));
            Cache[id] = theme;
        }
        control.Theme = theme;
    }

    internal static StyleBoxFlat Flat(Color background, Color border, int radius = 5, int width = 0) => new()
    {
        BgColor = background,
        BorderColor = border,
        BorderWidthLeft = width,
        BorderWidthTop = width,
        BorderWidthRight = width,
        BorderWidthBottom = width,
        CornerRadiusTopLeft = radius,
        CornerRadiusTopRight = radius,
        CornerRadiusBottomLeft = radius,
        CornerRadiusBottomRight = radius
    };

    internal static StyleBoxFlat Panel(HudPanelKind kind, ThemeDefinition p)
    {
        var style = Flat(kind switch
        {
            HudPanelKind.Main => p.PanelBackground,
            HudPanelKind.Settings => p.SettingsBackground,
            HudPanelKind.Snap => p.SnapFill,
            _ => p.OverlayBackground
        }, kind == HudPanelKind.Snap ? p.Accent : p.Border,
            kind == HudPanelKind.Settings ? 7 : kind is HudPanelKind.Main or HudPanelKind.Snap ? 6 : 5,
            kind == HudPanelKind.Snap ? 2 : 1);
        if (kind == HudPanelKind.Snap) return style;
        style.ContentMarginLeft = style.ContentMarginRight = kind == HudPanelKind.Main ? 8 : 12;
        style.ContentMarginTop = style.ContentMarginBottom = kind switch
        {
            HudPanelKind.Main => 6,
            HudPanelKind.Tooltip => 8,
            _ => 10
        };
        return style;
    }

    internal static void Arrow(Button button, ThemeDefinition p)
    {
        button.AddThemeColorOverride("font_color", p.Accent);
        button.AddThemeColorOverride("font_hover_color", p.Title);
        button.AddThemeColorOverride("font_pressed_color", p.Primary);
        button.AddThemeColorOverride("font_disabled_color", new Color(p.Accent.R, p.Accent.G, p.Accent.B, 0.28f));
        button.AddThemeStyleboxOverride("normal", Flat(Colors.Transparent, p.Border));
        button.AddThemeStyleboxOverride("hover", Flat(p.Hover, p.Border));
        button.AddThemeStyleboxOverride("pressed", Flat(p.Pressed, p.Border));
        button.AddThemeStyleboxOverride("focus", Flat(Colors.Transparent, p.Border));
    }

    private static Theme Build(ThemeDefinition p)
    {
        var theme = DpsHudTypography.CopyTheme();
        foreach (var type in new[] { "Label", "Button", "OptionButton", "CheckBox", "PopupMenu", "TooltipLabel" })
        {
            theme.SetColor("font_color", type, p.Primary);
            theme.SetColor("font_outline_color", type, p.Outline);
            foreach (var state in new[] { "hover", "pressed", "hover_pressed", "focus" })
                theme.SetColor("font_" + state + "_color", type, p.Primary);
            theme.SetColor("font_disabled_color", type, new Color(p.Muted.R, p.Muted.G, p.Muted.B, 0.55f));
        }
        theme.SetColor("default_color", "RichTextLabel", p.Primary);
        theme.SetColor("font_outline_color", "RichTextLabel", p.Outline);
        foreach (var type in new[] { "Button", "OptionButton" })
        {
            var normal = Flat(p.ControlBackground, p.Border, width: 1);
            normal.ContentMarginLeft = normal.ContentMarginRight = 7;
            var hover = (StyleBoxFlat)normal.Duplicate(); hover.BgColor = p.Hover; hover.BorderColor = p.Accent;
            var pressed = (StyleBoxFlat)normal.Duplicate(); pressed.BgColor = p.Pressed;
            theme.SetStylebox("normal", type, normal);
            theme.SetStylebox("hover", type, hover);
            theme.SetStylebox("pressed", type, pressed);
            theme.SetStylebox("hover_pressed", type, pressed);
            theme.SetStylebox("focus", type, hover);
            theme.SetStylebox("disabled", type, normal);
        }
        var popup = Flat(p.OverlayBackground, p.Border, width: 1);
        popup.ContentMarginLeft = popup.ContentMarginRight = 6;
        popup.ContentMarginTop = popup.ContentMarginBottom = 4;
        theme.SetStylebox("panel", "PopupMenu", popup);
        theme.SetStylebox("hover", "PopupMenu", Flat(p.Hover, p.Accent));
        theme.SetStylebox("panel", "TooltipPanel", Panel(HudPanelKind.Tooltip, p));
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
        {
            var style = Flat(state == "hover" ? p.Hover : Colors.Transparent, p.Border);
            style.ContentMarginLeft = style.ContentMarginRight = 4;
            style.ContentMarginTop = style.ContentMarginBottom = 4;
            theme.SetStylebox(state, "CheckBox", style);
        }
        theme.SetIcon("checked", "CheckBox", Icon(p, true));
        theme.SetIcon("unchecked", "CheckBox", Icon(p, false));
        theme.SetIcon("checked_disabled", "CheckBox", Icon(p, true));
        theme.SetIcon("unchecked_disabled", "CheckBox", Icon(p, false));
        var rail = Flat(p.BarTrack, p.Border, 2, 1); rail.ContentMarginTop = rail.ContentMarginBottom = 2;
        theme.SetStylebox("slider", "HSlider", rail);
        theme.SetStylebox("grabber_area", "HSlider", Flat(p.Accent, p.Accent, 2));
        theme.SetStylebox("grabber_area_highlight", "HSlider", Flat(p.Title, p.Title, 2));
        theme.SetIcon("grabber", "HSlider", Icon(p, false, true));
        theme.SetIcon("grabber_highlight", "HSlider", Icon(p, true, true));
        foreach (var type in new[] { "VScrollBar", "HScrollBar" })
        {
            var track = Flat(p.BarTrack, p.Border, 3);
            track.ContentMarginLeft = track.ContentMarginRight = 3;
            track.ContentMarginTop = track.ContentMarginBottom = 3;
            theme.SetStylebox("scroll", type, track);
            theme.SetStylebox("grabber", type, Flat(p.Muted, p.Border, 3));
            theme.SetStylebox("grabber_highlight", type, Flat(p.Accent, p.Border, 3));
            theme.SetStylebox("grabber_pressed", type, Flat(p.Title, p.Border, 3));
        }
        return theme;
    }

    private static ImageTexture Icon(ThemeDefinition p, bool active, bool circle = false)
    {
        var stroke = p.Primary.ToHtml(false);
        var body = circle
            ? $"<circle cx='8' cy='8' r='6' fill='#{(active ? p.Title : p.Accent).ToHtml(false)}' stroke='#{stroke}'/>"
            : $"<rect x='2' y='2' width='12' height='12' rx='1' fill='#{p.ControlBackground.ToHtml(false)}' stroke='#{stroke}' stroke-width='1.5'/>" +
                (active ? $"<path d='M4 8 L7 11 L12 5' fill='none' stroke='#{stroke}' stroke-width='2'/>" : "");
        using var image = new Image();
        if (image.LoadSvgFromString($"<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16'>{body}</svg>") != Error.Ok)
        {
            // 简单图元解码失败时回退纯色像素，设置项仍可见、可操作。
            image.SetData(16, 16, false, Image.Format.Rgba8, new byte[16 * 16 * 4]);
            image.Fill(active ? p.Primary : p.Muted);
        }
        return ImageTexture.CreateFromImage(image);
    }
}
