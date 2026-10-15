using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Sts2DpsMeter;

internal enum HudScope { Combat, Turn, Team }

internal static class DpsHudScope
{
    internal static HudScope Normalize(HudScope scope, int playerCount) =>
        !Enum.IsDefined(scope) || (scope == HudScope.Team && playerCount < 2) ? HudScope.Combat : scope;

    internal static int Count(int playerCount) => playerCount > 1 ? 3 : 2;

    internal static string Label(HudScope scope, bool chinese) => scope switch
    {
        HudScope.Turn => chinese ? "本回合" : "This Turn",
        HudScope.Team => chinese ? "全队" : "Team",
        _ => chinese ? "本场战斗" : "This Combat"
    };
}

internal readonly record struct HudSummaryValues(long Damage, long Taken, long Hits,
    int Turn, double Dps, double Dpt);

internal static class DpsHudSummary
{
    internal const int MaxFields = 3;

    internal static int SelectedCount(DpsMeterSettingsData settings) => Enabled(settings).Count(value => value);

    private static bool[] Enabled(DpsMeterSettingsData settings) =>
        [settings.SummaryDamage, settings.SummaryDps, settings.SummaryDpt,
        settings.SummaryTaken, settings.SummaryTurn, settings.SummaryHits];

    internal static void Normalize(DpsMeterSettingsData settings)
    {
        var remaining = MaxFields;
        bool Keep(bool selected) => selected && remaining-- > 0;
        settings.SummaryDamage = Keep(settings.SummaryDamage);
        settings.SummaryDps = Keep(settings.SummaryDps);
        settings.SummaryDpt = Keep(settings.SummaryDpt);
        settings.SummaryTaken = Keep(settings.SummaryTaken);
        settings.SummaryTurn = Keep(settings.SummaryTurn);
        settings.SummaryHits = Keep(settings.SummaryHits);
    }

    internal static HudSummaryValues Values(CombatStatsSnapshot snapshot, PlayerStatsSnapshot? player, HudScope scope)
    {
        scope = DpsHudScope.Normalize(scope, snapshot.Players.Count);
        var turnNumber = Math.Max(1, snapshot.CurrentTurn);
        long damage, taken, hits;
        var seconds = snapshot.ElapsedSeconds;
        if (scope == HudScope.Team)
        {
            damage = snapshot.Players.Sum(item => item.TotalDamage);
            taken = snapshot.Players.Sum(item => item.IncomingDamage + item.SelfLossDamage);
            hits = snapshot.Players.Sum(item => (long)item.HitCount);
        }
        else if (scope == HudScope.Turn)
        {
            var turn = player?.Turns.FirstOrDefault(item => item.Turn == snapshot.CurrentTurn);
            damage = turn?.Damage ?? 0;
            taken = (turn?.IncomingDamage ?? 0) + (turn?.SelfLossDamage ?? 0);
            hits = turn?.Cards.Sum(card => (long)card.Hits) ?? 0;
            seconds = snapshot.CurrentTurnElapsedSeconds;
        }
        else
        {
            damage = player?.TotalDamage ?? 0;
            taken = (player?.IncomingDamage ?? 0) + (player?.SelfLossDamage ?? 0);
            hits = player?.HitCount ?? 0;
        }
        var dps = seconds > 0 && double.IsFinite(seconds) ? damage / seconds : 0;
        return new HudSummaryValues(damage, taken, hits, turnNumber, double.IsFinite(dps) ? dps : 0,
            damage / (double)(scope == HudScope.Turn ? 1 : turnNumber));
    }

    internal static string Title(DpsMeterSettingsData settings, HudSummaryValues values,
        HudScope scope, bool multiplayer, string? playerName, bool chinese)
    {
        string L(string zh, string en) => chinese ? zh : en;
        var parts = new List<string>();
        if (settings.SummaryDamage) parts.Add($"{L("伤害", "Damage")} {values.Damage}");
        if (settings.SummaryDps) parts.Add($"{values.Dps.ToString("0.0", CultureInfo.InvariantCulture)} DPS");
        if (settings.SummaryDpt) parts.Add($"{values.Dpt.ToString("0.0", CultureInfo.InvariantCulture)} DPT");
        if (settings.SummaryTaken) parts.Add($"{L("承伤", "Taken")} {values.Taken}");
        if (settings.SummaryTurn) parts.Add($"{L("回合", "Turn")} {values.Turn}");
        if (settings.SummaryHits) parts.Add($"{L("命中", "Hits")} {values.Hits}");
        var identity = multiplayer && scope != HudScope.Team && !string.IsNullOrWhiteSpace(playerName)
            ? playerName + " · " : string.Empty;
        var title = identity + DpsHudScope.Label(scope, chinese);
        return parts.Count == 0 ? title : title + " · " + string.Join(" · ", parts.Take(MaxFields));
    }
}
