using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.ValueProps;

namespace Sts2DpsMeter;

public enum AllySupportKind
{
    EnergyGiven,
    BlockGiven,
    HealingGiven,
    CardDrawn
}

public static class DpsStats
{
    private const string UnknownSource = "其他";
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<ulong, PlayerAccumulator> Players = new();
    private static readonly Dictionary<Creature, DoomKillCapture> PendingDoomKills = new();

    private static bool _active;
    private static double _elapsedSeconds;
    private static double _currentTurnElapsedSeconds;
    private static long _damageEventSequence;
    private static long _combatId;
    private static int _currentTurn = 1;

    public static void ResetRun()
    {
        lock (SyncRoot)
        {
            Players.Clear();
            PendingDoomKills.Clear();
            _active = false;
            _elapsedSeconds = 0;
            _currentTurnElapsedSeconds = 0;
            _damageEventSequence = 0;
            _currentTurn = 1;
        }
    }

    public static void BeginCombat()
    {
        BeginCombat(Array.Empty<Player>());
    }

    public static void BeginCombat(IReadOnlyList<Player> players)
    {
        lock (SyncRoot)
        {
            _combatId++;
            Players.Clear();
            PendingDoomKills.Clear();
            _active = true;
            _elapsedSeconds = 0;
            _currentTurnElapsedSeconds = 0;
            _damageEventSequence = 0;
            _currentTurn = 1;

            if (players is null)
            {
                return;
            }

            foreach (var player in players)
            {
                if (player is null)
                {
                    continue;
                }

                Players[player.NetId] = CreatePlayerAccumulator(player);
            }
        }
    }

    public static void EndCombat()
    {
        lock (SyncRoot)
        {
            PendingDoomKills.Clear();
            _active = false;
        }
    }

    public static void SetTurn(int roundNumber)
    {
        lock (SyncRoot)
        {
            var turn = Math.Max(1, roundNumber);
            if (_currentTurn != turn) _currentTurnElapsedSeconds = 0;
            _currentTurn = turn;
        }
    }

    public static void Tick(double delta)
    {
        if (delta <= 0 || double.IsNaN(delta) || double.IsInfinity(delta))
        {
            return;
        }

        lock (SyncRoot)
        {
            if (_active)
            {
                _elapsedSeconds += delta;
                _currentTurnElapsedSeconds += delta;
            }
        }
    }

    public static void CaptureDoomKills(IReadOnlyList<Creature> creatures)
    {
        lock (SyncRoot)
        {
            foreach (var creature in creatures)
            {
                if (!creature.IsEnemy || creature.IsDead || creature.CurrentHp <= 0)
                {
                    continue;
                }

                var power = creature.GetPower<DoomPower>();
                if (power is null || !power.IsOwnerDoomed())
                {
                    continue;
                }

                PendingDoomKills[creature] = new DoomKillCapture(
                    creature,
                    creature.CurrentHp,
                    power);
            }
        }
    }

    public static bool RecordDoomKills(IReadOnlyList<Creature> creatures)
    {
        var captures = new List<DoomKillCapture>();
        lock (SyncRoot)
        {
            foreach (var creature in creatures)
            {
                if (PendingDoomKills.Remove(creature, out var capture))
                {
                    captures.Add(capture);
                }
            }
        }

        var recorded = false;
        foreach (var capture in captures)
        {
            if (!capture.Target.IsDead || capture.HealthBeforeKill <= 0)
            {
                continue;
            }

            var attribution = DpsAttribution.ResolveDoomKill(capture.Power, capture.Target);
            if (attribution is null)
            {
                continue;
            }

            var player = ResolveRecordedPlayer(attribution, capture.Power.Applier, capture.Target);
            if (player is null && !HasPlayerShares(attribution))
            {
                continue;
            }

            var result = new DamageResult(
                capture.Target,
                ValueProp.Unblockable | ValueProp.Unpowered)
            {
                BlockedDamage = 0,
                UnblockedDamage = capture.HealthBeforeKill,
                OverkillDamage = 0,
                WasTargetKilled = true
            };

            if (RecordDamage(player, attribution, result))
            {
                recorded = true;
            }
        }

        return recorded;
    }

    public static bool Record(
        PlayerChoiceContext choiceContext,
        Creature? dealer,
        Creature target,
        DamageResult result,
        ValueProp props,
        CardModel? cardSource)
    {
        var damage = result.BlockedDamage + result.UnblockedDamage + result.OverkillDamage;
        if (damage <= 0)
        {
            return false;
        }

        var attribution = DpsAttribution.Resolve(choiceContext, dealer, target, result, props, cardSource);
        if (attribution.Category == DamageCategory.Ignored)
        {
            return false;
        }

        var player = ResolveRecordedPlayer(attribution, dealer, target);
        if (player is null && !HasPlayerShares(attribution))
        {
            return false;
        }

        return RecordDamage(player, attribution, result);
    }

    public static bool RecordBlockGained(Player player, decimal amount)
    {
        if (amount <= 0)
        {
            return false;
        }

        var blockGained = (long)decimal.Ceiling(amount);
        lock (SyncRoot)
        {
            if (!_active)
            {
                return false;
            }

            var stats = GetOrCreatePlayer(player);
            stats.BlockGained += blockGained;
            stats.BlockGainedByTurn[_currentTurn] =
                stats.BlockGainedByTurn.GetValueOrDefault(_currentTurn) + blockGained;
        }

        return true;
    }

    public static bool RecordEnergySpent(Player player, int amount)
    {
        if (amount <= 0)
        {
            return false;
        }

        lock (SyncRoot)
        {
            if (!_active)
            {
                return false;
            }

            var stats = GetOrCreatePlayer(player);
            stats.EnergySpent += amount;
            stats.EnergySpentByTurn[_currentTurn] =
                stats.EnergySpentByTurn.GetValueOrDefault(_currentTurn) + amount;
        }

        return true;
    }

    public static bool RecordAllySupport(
        Player? source,
        Player? target,
        AllySupportKind kind,
        decimal amount)
    {
        if (source is null || target is null || source.NetId == target.NetId || amount <= 0)
        {
            return false;
        }

        var value = (long)decimal.Ceiling(amount);
        lock (SyncRoot)
        {
            if (!_active)
            {
                return false;
            }

            var stats = GetOrCreatePlayer(source);
            switch (kind)
            {
                case AllySupportKind.EnergyGiven:
                    stats.EnergyGivenToAllies += value;
                    stats.EnergyGivenToAlliesByTurn[_currentTurn] =
                        stats.EnergyGivenToAlliesByTurn.GetValueOrDefault(_currentTurn) + value;
                    break;
                case AllySupportKind.BlockGiven:
                    stats.BlockGivenToAllies += value;
                    stats.BlockGivenToAlliesByTurn[_currentTurn] =
                        stats.BlockGivenToAlliesByTurn.GetValueOrDefault(_currentTurn) + value;
                    break;
                case AllySupportKind.HealingGiven:
                    stats.HealingGivenToAllies += value;
                    stats.HealingGivenToAlliesByTurn[_currentTurn] =
                        stats.HealingGivenToAlliesByTurn.GetValueOrDefault(_currentTurn) + value;
                    break;
                case AllySupportKind.CardDrawn:
                    stats.CardsDrawnForAllies += value;
                    stats.CardsDrawnForAlliesByTurn[_currentTurn] =
                        stats.CardsDrawnForAlliesByTurn.GetValueOrDefault(_currentTurn) + value;
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static PlayerAccumulator GetOrCreatePlayer(Player player)
    {
        if (!Players.TryGetValue(player.NetId, out var stats))
        {
            stats = CreatePlayerAccumulator(player);
            Players.Add(player.NetId, stats);
        }

        ApplyPlayerMetadata(stats, player);
        return stats;
    }

    private static bool RecordDamage(
        Player? fallbackPlayer,
        DamageAttribution attribution,
        DamageResult result)
    {
        var allocations = ResolveDamageAllocations(fallbackPlayer, attribution, result);
        if (allocations.Count == 0)
        {
            return false;
        }

        lock (SyncRoot)
        {
            if (!_active)
            {
                return false;
            }

            // 同一伤害事件的多人份额使用同一序号，避免分配遍历顺序变成触发先后。
            var sequence = ++_damageEventSequence;
            foreach (var allocation in allocations)
            {
                RecordDamageAllocation(allocation, attribution, sequence);
            }
        }

        return true;
    }

    private static void RecordDamageAllocation(
        DamageAllocation allocation,
        DamageAttribution attribution,
        long sequence)
    {
        var stats = GetOrCreatePlayer(allocation.Player);
        var damage = allocation.Damage;
        var sourceStats = attribution.Category switch
        {
            DamageCategory.Output => stats.DamageBySource,
            DamageCategory.Incoming => stats.IncomingBySource,
            DamageCategory.SelfLoss => stats.SelfLossBySource,
            _ => stats.DamageBySource
        };
        var sourceKey = BuildSourceKey(attribution);
        if (!sourceStats.TryGetValue(sourceKey, out var source))
        {
            source = new SourceAccumulator(attribution);
            sourceStats.Add(sourceKey, source);
        }

        source.Add(
            damage,
            allocation.BlockedDamage,
            allocation.UnblockedDamage,
            allocation.OverkillDamage,
            sequence);

        switch (attribution.Category)
        {
            case DamageCategory.Output:
                stats.TotalDamage += damage;
                stats.EffectiveHpDamage += allocation.UnblockedDamage;
                stats.BlockedDamage += allocation.BlockedDamage;
                stats.UnblockedDamage += allocation.UnblockedDamage;
                stats.OverkillDamage += allocation.OverkillDamage;
                stats.HitCount++;
                if (damage > stats.MaxHit)
                {
                    stats.MaxHit = (int)damage;
                    stats.MaxHitSource = attribution.DisplayName;
                }

                stats.DamageByTurn[_currentTurn] =
                    stats.DamageByTurn.GetValueOrDefault(_currentTurn) + damage;
                AddOutputBreakdown(stats, attribution, allocation, _currentTurn, sequence);
                break;
            case DamageCategory.Incoming:
                stats.IncomingLastSequenceByTurn[_currentTurn] = sequence;
                stats.IncomingDamage += damage;
                stats.IncomingHits++;
                stats.IncomingDamageByTurn[_currentTurn] =
                    stats.IncomingDamageByTurn.GetValueOrDefault(_currentTurn) + damage;
                break;
            case DamageCategory.SelfLoss:
                stats.SelfLossLastSequenceByTurn[_currentTurn] = sequence;
                stats.SelfLossDamage += damage;
                stats.SelfLossHits++;
                stats.SelfLossDamageByTurn[_currentTurn] =
                    stats.SelfLossDamageByTurn.GetValueOrDefault(_currentTurn) + damage;
                break;
        }
    }

    private static IReadOnlyList<DamageAllocation> ResolveDamageAllocations(
        Player? fallbackPlayer,
        DamageAttribution attribution,
        DamageResult result)
    {
        if (attribution.Category != DamageCategory.Output
            || attribution.PlayerShares is not { Count: > 0 } shares)
        {
            return fallbackPlayer is null
                ? Array.Empty<DamageAllocation>()
                : new[] { CreateDamageAllocation(fallbackPlayer, result) };
        }

        var positiveShares = shares
            .Where(share => share.Fraction > 0)
            .GroupBy(share => share.Player.NetId)
            .Select(group => group.First() with
            {
                Fraction = group.Sum(share => share.Fraction)
            })
            .ToArray();

        if (positiveShares.Length == 0)
        {
            return fallbackPlayer is null
                ? Array.Empty<DamageAllocation>()
                : new[] { CreateDamageAllocation(fallbackPlayer, result) };
        }

        var blockedDamage = AllocateDamageComponent(result.BlockedDamage, positiveShares);
        var unblockedDamage = AllocateDamageComponent(result.UnblockedDamage, positiveShares);
        var overkillDamage = AllocateDamageComponent(result.OverkillDamage, positiveShares);

        return positiveShares
            .Select(share =>
            {
                var netId = share.Player.NetId;
                return new DamageAllocation(
                    share.Player,
                    blockedDamage[netId],
                    unblockedDamage[netId],
                    overkillDamage[netId]);
            })
            .Where(allocation => allocation.Damage > 0)
            .ToArray();
    }

    private static DamageAllocation CreateDamageAllocation(Player player, DamageResult result)
    {
        return new DamageAllocation(
            player,
            result.BlockedDamage,
            result.UnblockedDamage,
            result.OverkillDamage);
    }

    private static Dictionary<ulong, long> AllocateDamageComponent(
        int total,
        IReadOnlyList<PlayerDamageShare> shares)
    {
        var allocations = shares.ToDictionary(share => share.Player.NetId, _ => 0L);
        if (total <= 0 || shares.Count == 0)
        {
            return allocations;
        }

        long assigned = 0;
        var remainders = new List<(ulong NetId, decimal Remainder)>(shares.Count);
        foreach (var share in shares)
        {
            var raw = total * share.Fraction;
            var floor = decimal.ToInt64(decimal.Floor(raw));
            allocations[share.Player.NetId] = floor;
            assigned += floor;
            remainders.Add((share.Player.NetId, raw - floor));
        }

        var residual = total - assigned;
        if (residual <= 0)
        {
            return allocations;
        }

        var order = remainders
            .OrderByDescending(item => item.Remainder)
            .ThenBy(item => item.NetId)
            .Select(item => item.NetId)
            .ToArray();
        for (long index = 0; index < residual; index++)
        {
            var netId = order[(int)(index % order.Length)];
            allocations[netId]++;
        }

        return allocations;
    }

    private static bool HasPlayerShares(DamageAttribution attribution)
    {
        return attribution.Category == DamageCategory.Output
            && attribution.PlayerShares is { Count: > 0 };
    }
    public static CombatStatsSnapshot GetSnapshot()
    {
        lock (SyncRoot)
        {
            var players = Players.Values
                .OrderByDescending(player => player.TotalDamage)
                .Select(player => new PlayerStatsSnapshot(
                    player.PlayerNetId,
                    player.DisplayName,
                    player.CharacterId,
                    player.CharacterName,
                    player.NameColorHex,
                    player.IsLocalPlayer,
                    player.TotalDamage,
                    player.BlockGained,
                    player.EnergySpent,
                    player.EnergyGivenToAllies,
                    player.BlockGivenToAllies,
                    player.HealingGivenToAllies,
                    player.CardsDrawnForAllies,
                    player.HitCount,
                    player.MaxHit,
                    player.MaxHitSource,
                    _elapsedSeconds > 0 ? player.TotalDamage / _elapsedSeconds : 0,
                    player.TotalDamage / (double)_currentTurn,
                    player.EffectiveHpDamage,
                    player.BlockedDamage,
                    player.UnblockedDamage,
                    player.OverkillDamage,
                    player.IncomingDamage,
                    player.SelfLossDamage,
                    player.IncomingHits,
                    player.SelfLossHits,
                    player.DamageBySource
                        .Values
                        .OrderByDescending(source => source.Damage)
                        .Select(source => source.ToSnapshot())
                        .ToArray(),
                    player.IncomingBySource
                        .Values
                        .OrderByDescending(source => source.Damage)
                        .Select(source => source.ToSnapshot())
                        .ToArray(),
                    player.SelfLossBySource
                        .Values
                        .OrderByDescending(source => source.Damage)
                        .Select(source => source.ToSnapshot())
                        .ToArray(),
                    BuildTurnSnapshots(player)))
                .ToArray();

            return new CombatStatsSnapshot(_active, _elapsedSeconds, _currentTurn, players, _currentTurnElapsedSeconds, _combatId);
        }
    }

    public static void PrintSummary()
    {
        var snapshot = GetSnapshot();
        GD.Print($"[{ModEntry.ModId}] 战斗耗时：{snapshot.ElapsedSeconds:F2} 秒，回合数：{snapshot.CurrentTurn}");

        foreach (var player in snapshot.Players)
        {
            GD.Print(
                $"[{ModEntry.ModId}] {player.DisplayName}：输出 {player.TotalDamage}，" +
                $"获得格挡 {player.BlockGained}，消耗能量 {player.EnergySpent}，" +
                $"给队友能量 {player.EnergyGivenToAllies}，给队友护盾 {player.BlockGivenToAllies}，" +
                $"给队友回血 {player.HealingGivenToAllies}，给队友抽牌 {player.CardsDrawnForAllies}，" +
                $"敌方吸收 {player.BlockedDamage}，掉血 {player.EffectiveHpDamage}，过量 {player.OverkillDamage}，" +
                $"命中 {player.HitCount}，最高单次 {player.MaxHit}（{player.MaxHitSource}），" +
                $"DPS {player.Dps:F2}，DPT {player.Dpt:F2}，承伤 {player.IncomingDamage}，自损 {player.SelfLossDamage}");

            foreach (var source in player.Sources.Take(8))
            {
                GD.Print($"[{ModEntry.ModId}]   输出来源 {FormatSource(source)}：{source.Damage}");
            }

            foreach (var source in player.IncomingSources.Take(5))
            {
                GD.Print($"[{ModEntry.ModId}]   承伤来源 {FormatSource(source)}：{source.Damage}");
            }

            foreach (var source in player.SelfLossSources.Take(5))
            {
                GD.Print($"[{ModEntry.ModId}]   自损来源 {FormatSource(source)}：{source.Damage}");
            }
        }
    }

    private static Player? ResolveRecordedPlayer(DamageAttribution attribution, Creature? dealer, Creature target)
    {
        if (attribution.Category == DamageCategory.Output)
        {
            var sharedPlayer = attribution.PlayerShares?
                .FirstOrDefault(share => share.Fraction > 0)
                ?.Player;
            return sharedPlayer
                ?? attribution.AccountablePlayer
                ?? GetPlayerForCreature(dealer)
                ?? GetPlayerForCreature(target);
        }

        return GetPlayerForCreature(target)
            ?? attribution.AccountablePlayer
            ?? GetPlayerForCreature(dealer);
    }

    private static Player? GetPlayerForCreature(Creature? creature)
    {
        if (creature is null)
        {
            return null;
        }

        if (creature.IsPlayer)
        {
            return creature.Player;
        }

        return creature.IsPet ? creature.PetOwner : null;
    }

    private static IReadOnlyList<TurnStatsSnapshot> BuildTurnSnapshots(PlayerAccumulator player)
    {
        return player.DamageByTurn.Keys
            .Concat(player.BlockGainedByTurn.Keys)
            .Concat(player.EnergySpentByTurn.Keys)
            .Concat(player.EnergyGivenToAlliesByTurn.Keys)
            .Concat(player.BlockGivenToAlliesByTurn.Keys)
            .Concat(player.HealingGivenToAlliesByTurn.Keys)
            .Concat(player.CardsDrawnForAlliesByTurn.Keys)
            .Concat(player.IncomingDamageByTurn.Keys)
            .Concat(player.SelfLossDamageByTurn.Keys)
            .Concat(player.CardsByTurn.Keys)
            .Concat(player.DamageByMechanismByTurn.Keys)
            .Distinct()
            .OrderBy(turn => turn)
            .Select(turn => new TurnStatsSnapshot(
                turn,
                player.DamageByTurn.GetValueOrDefault(turn),
                player.BlockGainedByTurn.GetValueOrDefault(turn),
                player.EnergySpentByTurn.GetValueOrDefault(turn),
                player.EnergyGivenToAlliesByTurn.GetValueOrDefault(turn),
                player.BlockGivenToAlliesByTurn.GetValueOrDefault(turn),
                player.HealingGivenToAlliesByTurn.GetValueOrDefault(turn),
                player.CardsDrawnForAlliesByTurn.GetValueOrDefault(turn),
                player.IncomingDamageByTurn.GetValueOrDefault(turn),
                player.SelfLossDamageByTurn.GetValueOrDefault(turn),
                player.CardsByTurn.TryGetValue(turn, out var cards)
                    ? cards.Values
                        .OrderByDescending(card => card.Damage)
                        .Select(card => card.ToSnapshot())
                        .ToArray()
                    : Array.Empty<CardDamageSnapshot>(),
                player.DamageByMechanismByTurn.TryGetValue(turn, out var mechanisms)
                    ? mechanisms.Values
                        .OrderByDescending(mechanism => mechanism.Damage)
                        .Select(mechanism => mechanism.ToSnapshot())
                        .ToArray()
                    : Array.Empty<MechanismDamageSnapshot>(),
                player.IncomingLastSequenceByTurn.GetValueOrDefault(turn),
                player.SelfLossLastSequenceByTurn.GetValueOrDefault(turn)))
            .ToArray();
    }

    private static void AddOutputBreakdown(
        PlayerAccumulator player,
        DamageAttribution attribution,
        DamageAllocation allocation,
        int turn,
        long sequence)
    {
        var identity = ResolveOutputGroupIdentity(attribution);

        if (!player.CardsByTurn.TryGetValue(turn, out var cards))
        {
            cards = new Dictionary<string, CardDamageAccumulator>(StringComparer.Ordinal);
            player.CardsByTurn.Add(turn, cards);
        }

        if (!cards.TryGetValue(identity.GroupId, out var card))
        {
            card = new CardDamageAccumulator(identity, attribution);
            cards.Add(identity.GroupId, card);
        }

        card.Add(
            allocation.Damage,
            allocation.BlockedDamage,
            allocation.UnblockedDamage,
            allocation.OverkillDamage,
            sequence);

        if (!player.DamageByMechanismByTurn.TryGetValue(turn, out var mechanisms))
        {
            mechanisms = new Dictionary<DamageMechanismKind, MechanismAccumulator>();
            player.DamageByMechanismByTurn.Add(turn, mechanisms);
        }

        if (!mechanisms.TryGetValue(attribution.Mechanism, out var mechanism))
        {
            mechanism = new MechanismAccumulator(attribution.Mechanism);
            mechanisms.Add(attribution.Mechanism, mechanism);
        }

        mechanism.Add(
            allocation.Damage,
            allocation.BlockedDamage,
            allocation.UnblockedDamage,
            allocation.OverkillDamage,
            sequence);
    }

    private static OutputGroupIdentity ResolveOutputGroupIdentity(DamageAttribution attribution)
    {
        if (attribution.SourceType == DamageSourceType.Card
            && !string.IsNullOrWhiteSpace(attribution.InternalId))
        {
            return new OutputGroupIdentity(
                attribution.InternalId,
                attribution.DisplayName,
                attribution.InternalId);
        }

        if (!string.IsNullOrWhiteSpace(attribution.TriggerCardId))
        {
            return new OutputGroupIdentity(
                attribution.TriggerCardId,
                attribution.TriggerCard ?? attribution.DisplayName,
                attribution.TriggerCardId);
        }

        var groupId = string.Join(
            "|",
            attribution.SourceType,
            attribution.InternalId,
            attribution.Mechanism);
        return new OutputGroupIdentity(groupId, attribution.DisplayName, null);
    }
    private static string BuildSourceKey(DamageAttribution attribution)
    {
        var identity = ResolveOutputGroupIdentity(attribution);
        var stableGroupId = !string.IsNullOrWhiteSpace(identity.GroupId)
            ? identity.GroupId
            : string.Join(
                "|",
                attribution.SourceType,
                attribution.DisplayName,
                attribution.Mechanism);

        return string.Join("|", attribution.SourceType, stableGroupId);
    }

    private static string FormatSource(SourceStatsSnapshot source)
    {
        var parts = new List<string>
        {
            source.Source,
            $"归因置信度={source.Confidence}"
        };
        if (!string.IsNullOrWhiteSpace(source.SemanticTag))
        {
            parts.Add(source.SemanticTag);
        }

        if (!string.IsNullOrWhiteSpace(source.TriggerCard))
        {
            parts.Add($"触发卡牌：{source.TriggerCard}");
        }

        return string.Join(" / ", parts);
    }


    private static PlayerAccumulator CreatePlayerAccumulator(Player player)
    {
        var stats = new PlayerAccumulator(player.NetId, ResolvePlayerName(player));
        ApplyPlayerMetadata(stats, player);
        return stats;
    }

    private static void ApplyPlayerMetadata(PlayerAccumulator stats, Player player)
    {
        stats.DisplayName = ResolvePlayerName(player);
        stats.CharacterId = ResolveCharacterId(player);
        stats.CharacterName = ResolveCharacterName(player);
        stats.NameColorHex = ResolveNameColorHex(player);
        stats.IsLocalPlayer = ResolveIsLocalPlayer(player);
    }

    private static string ResolvePlayerName(Player player)
    {
        try
        {
            var platformName = PlatformUtil.GetPlayerNameRaw(PlatformUtil.PrimaryPlatform, player.NetId);
            if (!string.IsNullOrWhiteSpace(platformName))
            {
                return platformName.Trim();
            }
        }
        catch
        {
            // Steam/LAN 平台名不可用时继续使用战斗内显示名。
        }

        try
        {
            var creatureName = player.Creature?.Name;
            if (!string.IsNullOrWhiteSpace(creatureName))
            {
                return creatureName;
            }
        }
        catch
        {
            // 读取显示名失败时退回 NetId，不能影响伤害事件处理。
        }

        try
        {
            var characterTitle = player.Character?.Title?.ToString();
            if (!string.IsNullOrWhiteSpace(characterTitle))
            {
                return characterTitle;
            }
        }
        catch
        {
            // 角色标题不是统计数据的必要字段。
        }

        return $"玩家 {player.NetId}";
    }

    private static string ResolveCharacterId(Player player)
    {
        try
        {
            return player.Character?.Id.Entry ?? "UNKNOWN";
        }
        catch
        {
            return "UNKNOWN";
        }
    }

    private static string ResolveCharacterName(Player player)
    {
        try
        {
            var title = player.Character?.Title?.ToString();
            if (!string.IsNullOrWhiteSpace(title))
            {
                return title;
            }
        }
        catch
        {
            // 职业名称仅用于富文本补充信息。
        }

        return "未知职业";
    }

    private static string ResolveNameColorHex(Player player)
    {
        try
        {
            return player.Character?.NameColor.ToHtml(false) ?? "FFFFFF";
        }
        catch
        {
            return "FFFFFF";
        }
    }

    private static bool ResolveIsLocalPlayer(Player player)
    {
        try
        {
            return LocalContext.IsMe(player);
        }
        catch
        {
            return false;
        }
    }

    private sealed class PlayerAccumulator
    {
        public PlayerAccumulator(ulong playerNetId, string displayName)
        {
            PlayerNetId = playerNetId;
            DisplayName = displayName;
        }

        public ulong PlayerNetId { get; }

        public string DisplayName { get; set; }

        public string CharacterId { get; set; } = "UNKNOWN";

        public string CharacterName { get; set; } = "未知职业";

        public string NameColorHex { get; set; } = "FFFFFF";

        public bool IsLocalPlayer { get; set; }

        public long TotalDamage { get; set; }

        public long BlockGained { get; set; }

        public long EnergySpent { get; set; }

        public long EnergyGivenToAllies { get; set; }

        public long BlockGivenToAllies { get; set; }

        public long HealingGivenToAllies { get; set; }

        public long CardsDrawnForAllies { get; set; }

        public long EffectiveHpDamage { get; set; }

        public long BlockedDamage { get; set; }

        public long UnblockedDamage { get; set; }

        public long OverkillDamage { get; set; }

        public long IncomingDamage { get; set; }

        public long SelfLossDamage { get; set; }

        public int HitCount { get; set; }

        public int IncomingHits { get; set; }

        public int SelfLossHits { get; set; }

        public int MaxHit { get; set; }

        public string MaxHitSource { get; set; } = UnknownSource;

        public Dictionary<string, SourceAccumulator> DamageBySource { get; } = new();

        public Dictionary<string, SourceAccumulator> IncomingBySource { get; } = new();

        public Dictionary<string, SourceAccumulator> SelfLossBySource { get; } = new();

        public Dictionary<int, long> DamageByTurn { get; } = new();

        public Dictionary<int, long> BlockGainedByTurn { get; } = new();

        public Dictionary<int, long> EnergySpentByTurn { get; } = new();

        public Dictionary<int, long> EnergyGivenToAlliesByTurn { get; } = new();

        public Dictionary<int, long> BlockGivenToAlliesByTurn { get; } = new();

        public Dictionary<int, long> HealingGivenToAlliesByTurn { get; } = new();

        public Dictionary<int, long> CardsDrawnForAlliesByTurn { get; } = new();

        public Dictionary<int, long> IncomingDamageByTurn { get; } = new();

        public Dictionary<int, long> SelfLossDamageByTurn { get; } = new();

        public Dictionary<int, long> IncomingLastSequenceByTurn { get; } = new();

        public Dictionary<int, long> SelfLossLastSequenceByTurn { get; } = new();

        public Dictionary<int, Dictionary<string, CardDamageAccumulator>> CardsByTurn { get; } = new();

        public Dictionary<int, Dictionary<DamageMechanismKind, MechanismAccumulator>> DamageByMechanismByTurn { get; } = new();
    }

    private sealed record DoomKillCapture(
        Creature Target,
        int HealthBeforeKill,
        DoomPower Power);

    private readonly record struct DamageAllocation(
        Player Player,
        long BlockedDamage,
        long UnblockedDamage,
        long OverkillDamage)
    {
        public long Damage => BlockedDamage + UnblockedDamage + OverkillDamage;
    }

    private sealed record OutputGroupIdentity(
        string GroupId,
        string DisplayName,
        string? CardId);

    private sealed class CardDamageAccumulator
    {
        public CardDamageAccumulator(OutputGroupIdentity identity, DamageAttribution attribution)
        {
            GroupId = identity.GroupId;
            DisplayName = identity.DisplayName;
            CardId = identity.CardId;
            SourceType = attribution.SourceType;
            TrackingStatus = attribution.TrackingStatus;
            TriggerCard = attribution.TriggerCard;
            TriggerCardId = attribution.TriggerCardId;
            SemanticTag = attribution.SemanticTag;
            PoisonSource = attribution.PoisonSource;
        }

        public string GroupId { get; }

        public string DisplayName { get; }

        public string? CardId { get; }

        public DamageSourceType SourceType { get; }

        public SourceTrackingStatus TrackingStatus { get; }

        public string? TriggerCard { get; }

        public string? TriggerCardId { get; }

        public string? SemanticTag { get; }

        public PoisonSourceInfo? PoisonSource { get; }

        public int Hits { get; private set; }

        public long Damage { get; private set; }

        public long BlockedDamage { get; private set; }

        public long UnblockedDamage { get; private set; }

        public long OverkillDamage { get; private set; }

        public long MaxHit { get; private set; }

        public long LastTriggerSequence { get; private set; }

        public void Add(long damage, DamageResult result)
        {
            Add(damage, result.BlockedDamage, result.UnblockedDamage, result.OverkillDamage);
        }

        public void Add(long damage, long blockedDamage, long unblockedDamage, long overkillDamage)
        {
            Add(damage, blockedDamage, unblockedDamage, overkillDamage, 0);
        }

        public void Add(long damage, long blockedDamage, long unblockedDamage, long overkillDamage, long sequence)
        {
            LastTriggerSequence = Math.Max(LastTriggerSequence, sequence);
            MaxHit = Math.Max(MaxHit, damage);
            Damage += damage;
            BlockedDamage += blockedDamage;
            UnblockedDamage += unblockedDamage;
            OverkillDamage += overkillDamage;
            Hits++;
        }

        public CardDamageSnapshot ToSnapshot()
        {
            return new CardDamageSnapshot(
                GroupId,
                DisplayName,
                CardId,
                SourceType,
                TrackingStatus,
                TriggerCard,
                TriggerCardId,
                SemanticTag,
                PoisonSource,
                Hits,
                Damage,
                BlockedDamage,
                UnblockedDamage,
                OverkillDamage,
                MaxHit,
                LastTriggerSequence);
        }
    }

    private sealed class MechanismAccumulator
    {
        public MechanismAccumulator(DamageMechanismKind mechanism)
        {
            Mechanism = mechanism;
        }

        public DamageMechanismKind Mechanism { get; }

        public int Hits { get; private set; }

        public long Damage { get; private set; }

        public long BlockedDamage { get; private set; }

        public long UnblockedDamage { get; private set; }

        public long OverkillDamage { get; private set; }

        public long MaxHit { get; private set; }

        public long LastTriggerSequence { get; private set; }

        public void Add(long damage, DamageResult result)
        {
            Add(damage, result.BlockedDamage, result.UnblockedDamage, result.OverkillDamage);
        }

        public void Add(long damage, long blockedDamage, long unblockedDamage, long overkillDamage)
        {
            Add(damage, blockedDamage, unblockedDamage, overkillDamage, 0);
        }

        public void Add(long damage, long blockedDamage, long unblockedDamage, long overkillDamage, long sequence)
        {
            LastTriggerSequence = Math.Max(LastTriggerSequence, sequence);
            MaxHit = Math.Max(MaxHit, damage);
            Damage += damage;
            BlockedDamage += blockedDamage;
            UnblockedDamage += unblockedDamage;
            OverkillDamage += overkillDamage;
            Hits++;
        }

        public MechanismDamageSnapshot ToSnapshot()
        {
            return new MechanismDamageSnapshot(
                Mechanism,
                Hits,
                Damage,
                BlockedDamage,
                UnblockedDamage,
                OverkillDamage,
                MaxHit,
                LastTriggerSequence);
        }
    }

    private sealed class SourceAccumulator
    {
        private readonly DamageAttribution _attribution;

        public SourceAccumulator(DamageAttribution attribution)
        {
            _attribution = attribution;
        }

        public long Damage { get; private set; }

        public long BlockedDamage { get; private set; }

        public long UnblockedDamage { get; private set; }

        public long OverkillDamage { get; private set; }

        public long MaxHit { get; private set; }

        public long LastTriggerSequence { get; private set; }

        public int Hits { get; private set; }

        public void Add(long damage, DamageResult result)
        {
            Add(damage, result.BlockedDamage, result.UnblockedDamage, result.OverkillDamage);
        }

        public void Add(long damage, long blockedDamage, long unblockedDamage, long overkillDamage)
        {
            Add(damage, blockedDamage, unblockedDamage, overkillDamage, 0);
        }

        public void Add(long damage, long blockedDamage, long unblockedDamage, long overkillDamage, long sequence)
        {
            LastTriggerSequence = Math.Max(LastTriggerSequence, sequence);
            MaxHit = Math.Max(MaxHit, damage);
            Damage += damage;
            BlockedDamage += blockedDamage;
            UnblockedDamage += unblockedDamage;
            OverkillDamage += overkillDamage;
            Hits++;
        }

        public SourceStatsSnapshot ToSnapshot()
        {
            return new SourceStatsSnapshot(
                _attribution.DisplayName,
                Damage,
                _attribution.InternalId,
                _attribution.SourceType,
                _attribution.Category,
                _attribution.Confidence,
                _attribution.Mechanism,
                _attribution.TrackingStatus,
                _attribution.TriggerCard,
                _attribution.TriggerCardId,
                _attribution.SemanticTag,
                _attribution.PoisonSource,
                Hits,
                BlockedDamage,
                UnblockedDamage,
                OverkillDamage,
                MaxHit,
                LastTriggerSequence);
        }
    }
}
