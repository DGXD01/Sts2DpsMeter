using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Sts2DpsMeter;

// 只读展示元数据。缺失值保留 null，不把格挡/辅助总量伪装成伤害命中。
internal sealed record HudSourceInfo(
    DamageSourceType? SourceType = null,
    int? Hits = null,
    long? MaxHit = null,
    long? BlockedDamage = null,
    long? UnblockedDamage = null,
    long? OverkillDamage = null,
    AttributionConfidence? Confidence = null,
    DamageMechanismKind? Mechanism = null,
    SourceTrackingStatus? TrackingStatus = null,
    string? InternalId = null,
    string? MetricName = null,
    long? LastTriggerSequence = null);

internal static class DpsHudSourceInfo
{
    internal static string SourceKey(SourceStatsSnapshot source) => string.Join("|",
        source.SourceType, source.InternalId, source.TriggerCardId, source.Category,
        source.Mechanism, source.Confidence, string.IsNullOrEmpty(source.InternalId) ? source.Source : null);

    internal static HudSourceInfo FromSources(IEnumerable<SourceStatsSnapshot> sources)
    {
        var items = sources.ToArray();
        if (items.Length == 0)
        {
            return new HudSourceInfo();
        }
        return new HudSourceInfo(
            Common(items.Select(item => (DamageSourceType?)item.SourceType)) ?? DamageSourceType.Unknown,
            items.Sum(item => item.Hits), items.Max(item => item.MaxHit),
            items.Sum(item => item.BlockedDamage), items.Sum(item => item.UnblockedDamage),
            items.Sum(item => item.OverkillDamage),
            Common(items.Select(item => (AttributionConfidence?)item.Confidence)),
            Common(items.Select(item => (DamageMechanismKind?)item.Mechanism)),
            Common(items.Select(item => (SourceTrackingStatus?)item.TrackingStatus)),
            CommonId(items.Select(item => item.InternalId)),
            LastTriggerSequence: Latest(items.Select(item => item.LastTriggerSequence)));
    }

    internal static HudSourceInfo FromCards(IEnumerable<CardDamageSnapshot> cards)
    {
        var items = cards.ToArray();
        if (items.Length == 0)
        {
            return new HudSourceInfo();
        }
        return new HudSourceInfo(
            Common(items.Select(item => (DamageSourceType?)item.SourceType)) ?? DamageSourceType.Unknown,
            items.Sum(item => item.Hits), items.Max(item => item.MaxHit),
            items.Sum(item => item.BlockedDamage), items.Sum(item => item.UnblockedDamage),
            items.Sum(item => item.OverkillDamage),
            TrackingStatus: Common(items.Select(item => (SourceTrackingStatus?)item.TrackingStatus)),
            InternalId: CommonId(items.Select(item => item.CardId ?? item.GroupId)),
            LastTriggerSequence: Latest(items.Select(item => item.LastTriggerSequence)));
    }

    internal static long? Latest(IEnumerable<long> sequences)
    {
        var latest = sequences.DefaultIfEmpty(0).Max();
        return latest > 0 ? latest : null;
    }

    private static T? Common<T>(IEnumerable<T?> values) where T : struct
    {
        var distinct = values.Distinct().Take(2).ToArray();
        return distinct.Length == 1 ? distinct[0] : null;
    }

    private static string? CommonId(IEnumerable<string> values)
    {
        var distinct = values.Distinct(StringComparer.Ordinal).Take(2).ToArray();
        return distinct.Length == 1 ? distinct[0] : null;
    }

    internal static string TypeName(DamageSourceType type, bool chinese) => type switch
    {
        DamageSourceType.Card => chinese ? "卡牌" : "Card",
        DamageSourceType.Power => chinese ? "状态/能力" : "Power",
        DamageSourceType.Relic => chinese ? "遗物" : "Relic",
        DamageSourceType.Potion => chinese ? "药水" : "Potion",
        DamageSourceType.Orb => chinese ? "充能球" : "Orb",
        DamageSourceType.Pet => chinese ? "宠物" : "Pet",
        DamageSourceType.Enemy => chinese ? "敌人" : "Enemy",
        DamageSourceType.Event => chinese ? "事件" : "Event",
        DamageSourceType.Self => chinese ? "自损" : "Self",
        _ => chinese ? "未知/混合来源" : "Unknown/Mixed"
    };

    internal static string Tooltip(string label, long damage, double percentage,
        HudSourceInfo? info, HudTooltipDetail detail, bool chinese)
    {
        string L(string zh, string en) => chinese ? zh : en;
        var lines = new List<string> { Wrap(label) };
        if (info?.SourceType is { } sourceType)
        {
            lines.Add($"{L("来源类型", "Source type")}: {TypeName(sourceType, chinese)}");
        }
        var metric = info?.MetricName ?? L("总伤害", "Damage");
        lines.Add($"{metric}: {damage} · {L("占比", "Share")}: {percentage.ToString("0.#", CultureInfo.InvariantCulture)}%");
        if (detail == HudTooltipDetail.Simple || info is null)
        {
            return string.Join('\n', lines);
        }
        if (info.Hits is > 0)
        {
            lines.Add($"{L("命中次数", "Hits")}: {info.Hits}");
            if (info.MaxHit is { } maxHit)
            {
                lines.Add($"{L("最大伤害", "Max hit")}: {maxHit}");
            }
            lines.Add($"{L("平均伤害", "Average")}: {(damage / (double)info.Hits.Value).ToString("0.#", CultureInfo.InvariantCulture)}");
        }
        if (info.BlockedDamage is { } blocked)
        {
            lines.Add($"{L("格挡伤害", "Blocked")}: {blocked}");
        }
        if (info.UnblockedDamage is { } unblocked)
        {
            lines.Add($"{L("生命伤害", "HP damage")}: {unblocked}");
        }
        if (info.OverkillDamage is { } overkill)
        {
            lines.Add($"{L("过量伤害", "Overkill")}: {overkill}");
        }
        if (info.Mechanism is { } mechanism && mechanism != DamageMechanismKind.Unknown)
        {
            lines.Add($"{L("机制", "Mechanism")}: {MechanismName(mechanism, chinese)}");
        }
        if (detail == HudTooltipDetail.Developer)
        {
            if (info.Confidence is { } confidence)
            {
                lines.Add($"{L("归因置信度", "Attribution")}: {confidence}");
            }
            if (info.TrackingStatus is { } tracking)
            {
                lines.Add($"{L("追溯状态", "Tracking")}: {tracking}");
            }
            if (!string.IsNullOrWhiteSpace(info.InternalId))
            {
                lines.Add($"ID: {Wrap(info.InternalId)}");
            }
        }
        return string.Join('\n', lines);
    }

    private static string MechanismName(DamageMechanismKind kind, bool chinese) => kind switch
    {
        DamageMechanismKind.DirectAttack => chinese ? "直接攻击" : "Direct attack",
        DamageMechanismKind.PowerEffect => chinese ? "状态/能力效果" : "Power effect",
        DamageMechanismKind.Poison => chinese ? "中毒" : "Poison",
        DamageMechanismKind.Thorns => chinese ? "荆棘" : "Thorns",
        DamageMechanismKind.Reflect => chinese ? "反伤" : "Reflect",
        DamageMechanismKind.HpLoss => chinese ? "生命流失" : "HP loss",
        _ => chinese ? "未知" : "Unknown"
    };

    // 原生 Tooltip 无需自定义脚本绑定；按文本元素换行，保留完整名称并避免拆开表情/组合字符。
    private static string Wrap(string value)
    {
        value = value.Replace('\n', ' ').Replace('\r', ' ');
        var elements = StringInfo.ParseCombiningCharacters(value);
        if (elements.Length <= 36) return value;
        var lines = new List<string>();
        for (var index = 0; index < elements.Length; index += 36)
        {
            var start = elements[index];
            var end = index + 36 < elements.Length ? elements[index + 36] : value.Length;
            lines.Add(value[start..end]);
        }
        return string.Join('\n', lines);
    }
}
