using System;
using System.Collections.Generic;
using System.Linq;

namespace Sts2DpsMeter;

public enum HudSortMode { Damage, Percentage, Hits, MaxHit, Name, Recent }

internal sealed record DamageRowData(
    string Label,
    long Damage,
    string? Details = null,
    string? NameColorHex = null,
    bool IsLocalPlayer = false,
    HudSourceInfo? Info = null,
    string? Key = null);

internal static class DpsHudSorting
{
    internal static DamageRowData[] VisibleRows(IEnumerable<DamageRowData> rows, long total,
        bool keepZeroRows, HudSortMode mode, int maxRows)
    {
        var candidates = rows.Where(row => keepZeroRows || row.Damage > 0);
        var sorted = mode switch
        {
            // 每个模块的分母相同；零分母时所有份额都是零，使用稳定的名称/键顺序。
            HudSortMode.Percentage => candidates.OrderByDescending(row => total > 0
                ? Math.Clamp(row.Damage / (double)total, 0, 1) : 0),
            HudSortMode.Hits => candidates.OrderByDescending(row => row.Info?.Hits),
            HudSortMode.MaxHit => candidates.OrderByDescending(row => row.Info?.MaxHit),
            HudSortMode.Name => candidates.OrderBy(row => row.Label, StringComparer.Ordinal),
            HudSortMode.Recent => candidates.OrderByDescending(row => row.Info?.LastTriggerSequence),
            _ => candidates.OrderByDescending(row => row.Damage)
        };
        return sorted.ThenBy(row => row.Label, StringComparer.Ordinal)
            .ThenBy(row => row.Key ?? row.Info?.InternalId ?? string.Empty, StringComparer.Ordinal)
            .Take(Math.Clamp(maxRows, 1, 12)).ToArray();
    }
}
