using System;
using System.Globalization;

namespace Sts2DpsMeter;

[Flags]
internal enum HudRowField
{
    None = 0,
    Name = 1,
    Damage = 2,
    Percentage = 4,
    Hits = 8,
    MaxHit = 16,
    Average = 32
}

internal static class DpsHudRowFields
{
    internal static readonly HudRowField[] NumericFields =
    {
        HudRowField.Damage, HudRowField.Percentage, HudRowField.Hits, HudRowField.MaxHit, HudRowField.Average
    };

    internal static HudRowField GetVisible(DpsMeterSettingsData settings)
    {
        var fields = HudRowField.None;
        if (settings.ShowFieldName) fields |= HudRowField.Name;
        if (settings.ShowFieldDamage) fields |= HudRowField.Damage;
        if (settings.ShowFieldPercentage) fields |= HudRowField.Percentage;
        if (settings.ShowFieldHits) fields |= HudRowField.Hits;
        if (settings.ShowFieldMaxHit) fields |= HudRowField.MaxHit;
        if (settings.ShowFieldAverage) fields |= HudRowField.Average;
        return fields == HudRowField.None ? HudRowField.Name : fields;
    }

    internal static string Text(HudRowField field, long damage, double percentage, HudSourceInfo? info, bool chinese)
    {
        return field switch
        {
            HudRowField.Damage => damage.ToString(CultureInfo.InvariantCulture),
            HudRowField.Percentage => $"{Math.Round(percentage):0}%",
            HudRowField.Hits when info?.Hits is { } hits => $"{hits}{(chinese ? "次" : "h")}",
            HudRowField.MaxHit when info?.MaxHit is { } maxHit => $"{(chinese ? "峰" : "↑")}{maxHit}",
            HudRowField.Average when info?.Hits is > 0 =>
                $"{(chinese ? "均" : "~")}{(damage / (double)info.Hits.Value).ToString("0.#", CultureInfo.InvariantCulture)}",
            _ => string.Empty
        };
    }

    internal static void LimitWidths(float[] widths, float available, HudRowField fields)
    {
        var count = 0;
        foreach (var field in NumericFields) if (fields.HasFlag(field)) count++;
        if (count == 0) { Array.Clear(widths); return; }
        var fixedSpace = 14 + 16 + (fields.HasFlag(HudRowField.Name) ? 48 : 20) + 6 * (count + 1);
        var cap = Math.Max(1, (available - fixedSpace) / count);
        for (var index = 0; index < widths.Length; index++)
        {
            widths[index] = fields.HasFlag(NumericFields[index]) ? Math.Clamp(widths[index], Math.Min(42, cap), cap) : 0;
        }
    }
}

// 一个 HUD 同时只展开一个稳定来源键，切换范围、玩家或战斗后清除。
internal sealed class HudRowSelection
{
    internal string? SelectedKey { get; private set; }
    internal void Toggle(string key) => SelectedKey = SelectedKey == key ? null : key;
    internal void Clear() => SelectedKey = null;
}
