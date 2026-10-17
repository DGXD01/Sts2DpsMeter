using System;
using System.Linq;

namespace Sts2DpsMeter;

// 只消费结束时的只读快照。显示计时不触及战斗计时、归因或累计器。
internal sealed class HudCombatSummaryState
{
    private long _seenCombatId = long.MinValue;
    private double _remaining;
    internal CombatStatsSnapshot? Snapshot { get; private set; }

    internal void Update(long combatId, bool active, CombatStatsSnapshot? completed,
        bool enabled, double duration, double delta)
    {
        if (active || completed is null || completed.CombatId != combatId)
        {
            Dismiss();
            return;
        }
        if (_seenCombatId != completed.CombatId)
        {
            _seenCombatId = completed.CombatId;
            Snapshot = enabled && completed.Players.Count > 0 ? completed : null;
            _remaining = DpsCombatSummary.NormalizeDuration(duration);
            return;
        }
        if (!enabled) Dismiss();
        if (Snapshot is null) return;
        if (double.IsFinite(delta) && delta > 0) _remaining -= delta;
        if (_remaining <= 0.000001) Dismiss();
    }

    internal void Dismiss()
    {
        Snapshot = null;
        _remaining = 0;
        // 保留已消费 ID，关闭设置/隐藏/重建之后不重播同一场总结。
    }
}

internal sealed record CombatSummaryData(bool IsTeam, long Damage, double Dps,
    string? TopSource, long TopSourceDamage, int MaxHit, string? MaxHitSource, int Turns, double Seconds);

internal static class DpsCombatSummary
{
    internal const float DefaultDuration = 5;
    internal static float NormalizeDuration(double value) => double.IsFinite(value)
        ? (float)Math.Clamp(value, 3, 15) : DefaultDuration;

    internal static CombatSummaryData Build(CombatStatsSnapshot snapshot)
    {
        var damage = snapshot.Players.Sum(player => player.TotalDamage);
        var source = snapshot.Players.SelectMany(player => player.Sources)
            .Where(item => item.Damage > 0)
            .GroupBy(DpsHudSourceInfo.SourceKey, StringComparer.Ordinal)
            .Select(group => new { Key = group.Key, Name = group.First().Source, Damage = group.Sum(item => item.Damage) })
            .OrderByDescending(item => item.Damage).ThenBy(item => item.Key, StringComparer.Ordinal).FirstOrDefault();
        var maximum = snapshot.Players.OrderByDescending(player => player.MaxHit)
            .ThenBy(player => player.PlayerNetId).FirstOrDefault();
        var seconds = double.IsFinite(snapshot.ElapsedSeconds) ? Math.Max(0, snapshot.ElapsedSeconds) : 0;
        return new CombatSummaryData(snapshot.Players.Count > 1, damage, seconds > 0 ? damage / seconds : 0,
            source?.Name, source?.Damage ?? 0, maximum?.MaxHit ?? 0, maximum?.MaxHitSource,
            Math.Max(1, snapshot.CurrentTurn), seconds);
    }

    internal static string[] Lines(CombatSummaryData data, bool chinese, Func<string, string> localize)
    {
        string Name(string? name) => string.IsNullOrWhiteSpace(name) ? "—" : Clean(localize(name));
        return new[]
        {
            chinese ? $"战斗总结 · {(data.IsTeam ? "全队" : "本场战斗")}" : $"Combat summary · {(data.IsTeam ? "Team" : "This combat")}",
            chinese ? $"总伤害 {data.Damage:N0} · DPS {data.Dps:F1}" : $"Damage {data.Damage:N0} · DPS {data.Dps:F1}",
            chinese ? $"最高来源：{Name(data.TopSource)}（{data.TopSourceDamage:N0}）" : $"Top source: {Name(data.TopSource)} ({data.TopSourceDamage:N0})",
            chinese ? $"最大单次：{data.MaxHit:N0}（{Name(data.MaxHitSource)}）" : $"Max hit: {data.MaxHit:N0} ({Name(data.MaxHitSource)})",
            chinese ? $"回合数 {data.Turns} · 耗时 {data.Seconds:F1} 秒" : $"Turns {data.Turns} · Time {data.Seconds:F1}s"
        };
    }

    private static string Clean(string value)
    {
        var text = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        return text.Length <= 100 ? text : text[..100] + "…";
    }
}
