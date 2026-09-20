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

public static class DpsStats
{
    private const string UnknownSource = "其他";
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<ulong, PlayerAccumulator> Players = new();
    private static readonly Dictionary<Creature, DoomKillCapture> PendingDoomKills = new();

    private static bool _active;
    private static double _elapsedSeconds;
    private static int _currentTurn = 1;

    public static void ResetRun()
    {
        lock (SyncRoot)
        {
            Players.Clear();
            PendingDoomKills.Clear();
            _active = false;
            _elapsedSeconds = 0;
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
            Players.Clear();
            PendingDoomKills.Clear();
            _active = true;
            _elapsedSeconds = 0;
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
            if (player is null)
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

            if (RecordDamage(player, attribution, result, capture.HealthBeforeKill))
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
        if (player is null)
        {
            return false;
        }

        return RecordDamage(player, attribution, result, damage);
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
        Player player,
        DamageAttribution attribution,
        DamageResult result,
        int damage)
    {
        lock (SyncRoot)
        {
            if (!_active)
            {
                return false;
            }

            if (!Players.TryGetValue(player.NetId, out var stats))
            {
                stats = CreatePlayerAccumulator(player);
                Players.Add(player.NetId, stats);
            }

            ApplyPlayerMetadata(stats, player);

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
                    AddOutputBreakdown(stats, attribution, damage, result, _currentTurn);
                    break;
                case DamageCategory.Incoming:
                    stats.IncomingDamage += damage;
                    stats.IncomingHits++;
                    stats.IncomingDamageByTurn[_currentTurn] =
                        stats.IncomingDamageByTurn.GetValueOrDefault(_currentTurn) + damage;
                    break;
                case DamageCategory.SelfLoss:
                    stats.SelfLossDamage += damage;
                    stats.SelfLossHits++;
                    stats.SelfLossDamageByTurn[_currentTurn] =
                        stats.SelfLossDamageByTurn.GetValueOrDefault(_currentTurn) + damage;
                    break;
            }
        }

        return true;
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
                $"获得格挡 {player.BlockGained}，消耗能量 {player.EnergySpent}，" +
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

    private static IReadOnlyList<TurnStatsSnapshot> BuildTurnSnapshots(PlayerAccumulator player)
    {
        return player.DamageByTurn.Keys
            .Concat(player.BlockGainedByTurn.Keys)
            .Concat(player.EnergySpentByTurn.Keys)
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
                    : Array.Empty<MechanismDamageSnapshot>()))
            .ToArray();
    }

    private static void AddOutputBreakdown(
        PlayerAccumulator player,
        DamageAttribution attribution,
        long damage,
        DamageResult result,
        int turn)
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

        card.Add(damage, result);

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

        mechanism.Add(damage, result);
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

        public Dictionary<int, long> IncomingDamageByTurn { get; } = new();

        public Dictionary<int, long> SelfLossDamageByTurn { get; } = new();

        public Dictionary<int, Dictionary<string, CardDamageAccumulator>> CardsByTurn { get; } = new();

        public Dictionary<int, Dictionary<DamageMechanismKind, MechanismAccumulator>> DamageByMechanismByTurn { get; } = new();
    }

    private sealed record DoomKillCapture(
        Creature Target,
        int HealthBeforeKill,
        DoomPower Power);

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

        public void Add(long damage, DamageResult result)
        {
            Damage += damage;
            BlockedDamage += result.BlockedDamage;
            UnblockedDamage += result.UnblockedDamage;
            OverkillDamage += result.OverkillDamage;
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
                OverkillDamage);
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

        public void Add(long damage, DamageResult result)
        {
            Damage += damage;
            BlockedDamage += result.BlockedDamage;
            UnblockedDamage += result.UnblockedDamage;
            OverkillDamage += result.OverkillDamage;
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
                OverkillDamage);
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
                _attribution.Mechanism,
                _attribution.TrackingStatus,
                _attribution.TriggerCard,
                _attribution.TriggerCardId,
                _attribution.SemanticTag,
                _attribution.PoisonSource,
                Hits,
                BlockedDamage,
                UnblockedDamage,
                OverkillDamage);
        }
    }
}
