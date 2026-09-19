using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

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

    public static void Record(Creature? dealer, Creature receiver, DamageResult result, CardModel? cardSource)
    {
        if (dealer is null || !receiver.IsEnemy)
        {
            return;
        }

        if (!dealer.IsPlayer && !dealer.IsPet)
        {
            return;
        }

        var player = dealer.IsPlayer ? dealer.Player : dealer.PetOwner;
        if (player is null)
        {
            return;
        }

        var damage = result.BlockedDamage + result.UnblockedDamage + result.OverkillDamage;
        if (damage <= 0)
        {
            return;
        }

        var source = ResolveSource(dealer, cardSource);

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
            stats.TotalDamage += damage;
            stats.HitCount++;

            if (damage > stats.MaxHit)
            {
                stats.MaxHit = damage;
                stats.MaxHitSource = source;
            }

            stats.DamageBySource[source] = stats.DamageBySource.GetValueOrDefault(source) + damage;
            stats.DamageByTurn[_currentTurn] =
                stats.DamageByTurn.GetValueOrDefault(_currentTurn) + damage;
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
                    player.DamageBySource
                        .OrderByDescending(pair => pair.Value)
                        .Select(pair => new SourceStatsSnapshot(pair.Key, pair.Value))
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
                $"[{ModEntry.ModId}] {player.DisplayName}：总伤害 {player.TotalDamage}，" +
                $"命中 {player.HitCount}，最高单次 {player.MaxHit}（{player.MaxHitSource}），" +
                $"DPS {player.Dps:F2}，DPT {player.Dpt:F2}");

            foreach (var source in player.Sources.Take(5))
            {
                GD.Print($"[{ModEntry.ModId}]   来源 {source.Source}：{source.Damage}");
            }
        }
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

    private static string ResolveSource(Creature dealer, CardModel? cardSource)
    {
        if (dealer.IsPet)
        {
            var petName = dealer.Name;
            return string.IsNullOrWhiteSpace(petName) ? "宠物" : $"宠物:{petName}";
        }

        try
        {
            var cardTitle = cardSource?.Title;
            if (!string.IsNullOrWhiteSpace(cardTitle))
            {
                return cardTitle;
            }
        }
        catch
        {
            // 某些临时卡牌可能没有可读取的本地化标题，归入其他来源。
        }

        return UnknownSource;
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

        public int HitCount { get; set; }

        public int MaxHit { get; set; }

        public string MaxHitSource { get; set; } = UnknownSource;

        public Dictionary<string, long> DamageBySource { get; } = new();

        public Dictionary<int, long> DamageByTurn { get; } = new();
    }
}
