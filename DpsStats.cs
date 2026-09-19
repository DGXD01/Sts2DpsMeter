using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Sts2DpsMeter;

public static class DpsStats
{
    private const string UnknownSource = "其他";
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<ulong, PlayerAccumulator> Players = new();

    private static bool _active;
    private static double _elapsedSeconds;
    private static int _currentTurn = 1;

    public static void ResetRun()
    {
        lock (SyncRoot)
        {
            Players.Clear();
            _active = false;
            _elapsedSeconds = 0;
            _currentTurn = 1;
        }
    }

    public static void BeginCombat()
    {
        lock (SyncRoot)
        {
            Players.Clear();
            _active = true;
            _elapsedSeconds = 0;
            _currentTurn = 1;
        }
    }

    public static void EndCombat()
    {
        lock (SyncRoot)
        {
            _active = false;
        }
    }

    public static void SetTurn(int roundNumber)
    {
        lock (SyncRoot)
        {
            _currentTurn = Math.Max(1, roundNumber);
        }
    }

    public static void Tick(double delta)
    {
        if (delta <= 0 || double.IsNaN(delta) || double.IsInfinity(delta))
        {
            return;
        }

        if (!IsCombatClockRunning())
        {
            return;
        }

        lock (SyncRoot)
        {
            if (_active)
            {
                _elapsedSeconds += delta;
            }
        }
    }

    public static void Record(
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
            return;
        }

        var attribution = DpsAttribution.Resolve(choiceContext, dealer, target, result, props, cardSource);
        if (attribution.Category == DamageCategory.Ignored)
        {
            return;
        }

        var player = ResolveRecordedPlayer(attribution, dealer, target);
        if (player is null)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (!_active)
            {
                return;
            }

            if (!Players.TryGetValue(player.NetId, out var stats))
            {
                stats = new PlayerAccumulator(player.NetId, ResolvePlayerName(player));
                Players.Add(player.NetId, stats);
            }

            stats.DisplayName = ResolvePlayerName(player);

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

            source.Add(damage, result);

            switch (attribution.Category)
            {
                case DamageCategory.Output:
                    stats.TotalDamage += damage;
                    stats.EffectiveHpDamage += result.UnblockedDamage;
                    stats.BlockedDamage += result.BlockedDamage;
                    stats.UnblockedDamage += result.UnblockedDamage;
                    stats.OverkillDamage += result.OverkillDamage;
                    stats.HitCount++;
                    if (damage > stats.MaxHit)
                    {
                        stats.MaxHit = damage;
                        stats.MaxHitSource = attribution.DisplayName;
                    }

                    stats.DamageByTurn[_currentTurn] =
                        stats.DamageByTurn.GetValueOrDefault(_currentTurn) + damage;
                    break;
                case DamageCategory.Incoming:
                    stats.IncomingDamage += damage;
                    stats.IncomingHits++;
                    break;
                case DamageCategory.SelfLoss:
                    stats.SelfLossDamage += damage;
                    stats.SelfLossHits++;
                    break;
            }
        }
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
                    player.TotalDamage,
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
                    player.DamageByTurn
                        .OrderBy(pair => pair.Key)
                        .Select(pair => new TurnStatsSnapshot(pair.Key, pair.Value))
                        .ToArray()))
                .ToArray();

            return new CombatStatsSnapshot(_active, _elapsedSeconds, _currentTurn, players);
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
                $"格挡 {player.BlockedDamage}，掉血 {player.EffectiveHpDamage}，过量 {player.OverkillDamage}，" +
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
            return attribution.AccountablePlayer;
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

    private static string BuildSourceKey(DamageAttribution attribution)
    {
        return string.Join(
            "|",
            attribution.SourceType,
            attribution.InternalId,
            attribution.DisplayName,
            attribution.TriggerCard ?? string.Empty,
            attribution.SemanticTag ?? string.Empty);
    }

    private static string FormatSource(SourceStatsSnapshot source)
    {
        var parts = new List<string> { source.Source };
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

    private static bool IsCombatClockRunning()
    {
        try
        {
            var manager = CombatManager.Instance;
            return manager is not null && manager.IsInProgress && !manager.IsPaused;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolvePlayerName(Player player)
    {
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

    private sealed class PlayerAccumulator
    {
        public PlayerAccumulator(ulong playerNetId, string displayName)
        {
            PlayerNetId = playerNetId;
            DisplayName = displayName;
        }

        public ulong PlayerNetId { get; }

        public string DisplayName { get; set; }

        public long TotalDamage { get; set; }

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

        public int Hits { get; private set; }

        public void Add(long damage, DamageResult result)
        {
            Damage += damage;
            BlockedDamage += result.BlockedDamage;
            UnblockedDamage += result.UnblockedDamage;
            OverkillDamage += result.OverkillDamage;
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
                _attribution.TriggerCard,
                _attribution.SemanticTag,
                Hits,
                BlockedDamage,
                UnblockedDamage,
                OverkillDamage);
        }
    }
}
