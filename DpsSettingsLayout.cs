using System;
using System.Collections.Generic;
using Godot;

namespace Sts2DpsMeter;

internal enum HudSettingsSection { Hud, Position, Statistics, Rows, Multiplayer, CombatEnd, Presets }

internal static class DpsSettingsLayout
{
    public static IReadOnlyList<HudSettingsSection> Sections { get; } = Array.AsReadOnly(new[]
    {
        HudSettingsSection.Hud, HudSettingsSection.Position, HudSettingsSection.Statistics,
        HudSettingsSection.Rows, HudSettingsSection.Multiplayer, HudSettingsSection.CombatEnd, HudSettingsSection.Presets
    });

    public static string Label(HudSettingsSection section, bool chinese) => (section, chinese) switch
    {
        (HudSettingsSection.Hud, _) => "HUD",
        (HudSettingsSection.Position, true) => "位置",
        (HudSettingsSection.Position, false) => "Position",
        (HudSettingsSection.Statistics, true) => "统计内容",
        (HudSettingsSection.Statistics, false) => "Statistics",
        (HudSettingsSection.Rows, true) => "数据行",
        (HudSettingsSection.Rows, false) => "Data Rows",
        (HudSettingsSection.Multiplayer, true) => "多人",
        (HudSettingsSection.Multiplayer, false) => "Multiplayer",
        (HudSettingsSection.CombatEnd, true) => "战斗结束",
        (HudSettingsSection.CombatEnd, false) => "Combat End",
        (HudSettingsSection.Presets, true) => "预设",
        (HudSettingsSection.Presets, false) => "Presets",
        _ => "HUD"
    };

    public static Rect2 Calculate(Vector2 viewport)
    {
        var width = float.IsFinite(viewport.X) && viewport.X > 0 ? viewport.X : 1920;
        var height = float.IsFinite(viewport.Y) && viewport.Y > 0 ? viewport.Y : 1080;
        var marginX = Math.Min(16, width / 4);
        var marginY = Math.Min(16, height / 4);
        var size = new Vector2(Math.Min(485, width - 2 * marginX), Math.Min(650, height - 2 * marginY));
        return new Rect2(new Vector2(Math.Clamp(400, marginX, width - size.X - marginX),
            Math.Clamp(45, marginY, height - size.Y - marginY)), size);
    }

    public static float ScrollFraction(double value, double maximum, double page)
    {
        var limit = maximum - page;
        return double.IsFinite(value) && double.IsFinite(limit) && limit > 0
            ? (float)Math.Clamp(value / limit, 0, 1) : 0;
    }

    public static int RestoreScroll(float fraction, double maximum, double page)
    {
        var limit = maximum - page;
        if (!float.IsFinite(fraction) || !double.IsFinite(limit) || limit <= 0) return 0;
        return (int)Math.Min(int.MaxValue, Math.Round(Math.Clamp(fraction, 0, 1) * limit));
    }
}
