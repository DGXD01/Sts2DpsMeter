using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Sts2DpsMeter;

public enum DamageSourceType
{
    Card,
    Power,
    Relic,
    Potion,
    Orb,
    Pet,
    Enemy,
    Event,
    Self,
    Unknown
}

public enum DamageCategory
{
    Output,
    Incoming,
    SelfLoss,
    Ignored
}

public enum AttributionConfidence
{
    Exact,
    Inferred,
    Unknown
}

public enum DamageMechanismKind
{
    Unknown,
    DirectAttack,
    PowerEffect,
    Poison,
    Thorns,
    Reflect,
    HpLoss
}

public enum SourceTrackingStatus
{
    NotApplicable,
    Deferred,
    Tracked,
    Unknown
}

public sealed record PoisonSourceInfo(
    string? SourceInternalId,
    string? SourceDisplayName,
    int? AppliedStacks,
    SourceTrackingStatus TrackingStatus)
{
    public static PoisonSourceInfo Deferred { get; } = new(
        null,
        null,
        null,
        SourceTrackingStatus.Deferred);
}

public sealed record PlayerDamageShare(Player Player, decimal Fraction);

public sealed record DamageAttribution(
    string DisplayName,
    string InternalId,
    DamageSourceType SourceType,
    DamageCategory Category,
    AttributionConfidence Confidence,
    DamageMechanismKind Mechanism,
    SourceTrackingStatus TrackingStatus,
    string? TriggerCard,
    string? TriggerCardId,
    string? SemanticTag,
    PoisonSourceInfo? PoisonSource,
    Player? AccountablePlayer,
    IReadOnlyList<PlayerDamageShare>? PlayerShares = null);

public static class DpsAttribution
{
    private enum DirectSourceProvenance
    {
        None,
        GameModelStack,
        ExplicitEffectStack,
        RecentWindowFallback,
        DealerFallback
    }

    private const string UnknownSource = "未知来源";
    private static readonly object PowerOriginSyncRoot = new();
    private static readonly ConditionalWeakTable<PowerModel, PowerOriginRecord> PowerOrigins = new();
    private static readonly ConditionalWeakTable<PowerModel, PowerOwnershipRecord> PowerOwnerships = new();
    private static readonly Dictionary<Creature, PoisonTickState> PoisonTicks = new();

    private static readonly HashSet<string> DamagePowerNames = new(StringComparer.Ordinal)
    {
        "BlackHolePower",
        "ConstrictPower",
        "FlameBarrierPower",
        "GravityPower",
        "HailstormPower",
        "HauntPower",
        "InfernoPower",
        "JuggernautPower",
        "NecroMasteryPower",
        "OutbreakPower",
        "PanachePower",
        "PaperCutsPower",
        "PoisonPower",
        "ReaperFormPower",
        "ReflectPower",
        "RollingBoulderPower",
        "SerpentFormPower",
        "SicEmPower",
        "SleightOfFleshPower",
        "SmokestackPower",
        "SpeedsterPower",
        "StranglePower",
        "TheBombPower",
        "ThornsPower",
        "ThunderPower"
    };

    // 反伤能力只会在拥有者受击后伤害攻击者，不能作为拥有者自身受伤的来源。
    private static readonly HashSet<string> SelfReactiveDamagePowerNames = new(StringComparer.Ordinal)
    {
        "FlameBarrierPower",
        "ReflectPower",
        "ThornsPower"
    };

    private static readonly Dictionary<string, string> SemanticTags = new(StringComparer.Ordinal)
    {
        ["PoisonPower"] = "中毒/持续伤害",
        ["InfernoPower"] = "火焰/持续伤害",
        ["ThunderPower"] = "闪电",
        ["FlameBarrierPower"] = "火焰/反伤",
        ["BlackHolePower"] = "星能/能力伤害",
        ["ReflectPower"] = "反伤",
        ["ThornsPower"] = "反伤",
        ["ConstrictPower"] = "束缚/持续伤害"
    };

    // 这些能力可能由多个不同卡牌、遗物或药水施加，不能静态绑定到单一卡牌。
    private static readonly HashSet<string> AmbiguousPowerNames = new(StringComparer.Ordinal)
    {
        "ThornsPower"
    };

    // 只映射能够稳定确定唯一来源卡牌的能力。共享能力必须依赖运行时来源记录。
    private static readonly Dictionary<Type, Type> PowerOriginCardTypes = new()
    {
        [typeof(BlackHolePower)] = typeof(BlackHole),
        [typeof(FlameBarrierPower)] = typeof(FlameBarrier),
        [typeof(HailstormPower)] = typeof(Hailstorm),
        [typeof(HauntPower)] = typeof(Haunt),
        [typeof(InfernoPower)] = typeof(Inferno),
        [typeof(JuggernautPower)] = typeof(Juggernaut),
        [typeof(CrimsonMantlePower)] = typeof(CrimsonMantle),
        [typeof(NecroMasteryPower)] = typeof(NecroMastery),
        [typeof(OutbreakPower)] = typeof(Outbreak),
        [typeof(SerpentFormPower)] = typeof(SerpentForm),
        [typeof(SleightOfFleshPower)] = typeof(SleightOfFlesh),
        [typeof(SmokestackPower)] = typeof(Smokestack),
        [typeof(SpeedsterPower)] = typeof(Speedster),
        [typeof(ReflectPower)] = typeof(Reflect),
        [typeof(StranglePower)] = typeof(Strangle),
        [typeof(ThunderPower)] = typeof(Thunder)
    };

    public static DamageAttribution Resolve(
        PlayerChoiceContext choiceContext,
        Creature? dealer,
        Creature target,
        DamageResult result,
        ValueProp props,
        CardModel? cardSource)
    {
        var chain = DpsSourceContext.GetModelChain(choiceContext);
        AbstractModel? directModel = cardSource;
        PoisonTickState? poisonTick = null;
        var sourceProvenance = cardSource is null
            ? DirectSourceProvenance.None
            : DirectSourceProvenance.GameModelStack;

        // 中毒 tick 的 dealer/cardSource 都是 null，按目标保存本次回合开始的来源状态。
        if (cardSource is null
            && TryGetPoisonTick(target, out var activePoisonTick)
            && activePoisonTick.Power.Amount > 0)
        {
            poisonTick = activePoisonTick;
            directModel = poisonTick.Power;
            sourceProvenance = DirectSourceProvenance.ExplicitEffectStack;
        }

        // 遗物和无 PushModel 的异步效果没有真实模型栈，使用补丁作用域内的显式来源。
        if (directModel is null)
        {
            directModel = DpsSourceContext.GetCurrentEffect(dealer)
                ?? DpsSourceContext.GetCurrentEffect(target);
            if (directModel is not null)
            {
                sourceProvenance = DirectSourceProvenance.ExplicitEffectStack;
            }
        }

        if (directModel is null)
        {
            directModel = SelectDirectModel(chain, target);
            if (directModel is not null)
            {
                sourceProvenance = DirectSourceProvenance.GameModelStack;
            }
        }

        // 回合结束球体被动不经 OrbCmd，使用 Trigger 后保存的独立近时来源。
        if (directModel is null && props.HasFlag(ValueProp.Unpowered))
        {
            directModel = DpsSourceContext.GetRecentOrb();
            if (directModel is not null)
            {
                sourceProvenance = DirectSourceProvenance.RecentWindowFallback;
            }
        }

        if (directModel is null)
        {
            var recentPower = DpsSourceContext.GetRecentModel<PowerModel>();
            if (recentPower is not null && IsDamagePower(recentPower)
                && IsPlausibleIncomingDamagePower(recentPower, target))
            {
                directModel = recentPower;
                sourceProvenance = DirectSourceProvenance.RecentWindowFallback;
            }
        }

        if (directModel is null)
        {
            directModel = DpsSourceContext.GetRecentModel<RelicModel>();
            if (directModel is not null)
            {
                sourceProvenance = DirectSourceProvenance.RecentWindowFallback;
            }
        }

        if (directModel is null)
        {
            directModel = DpsSourceContext.GetRecentModel<PotionModel>();
            if (directModel is not null)
            {
                sourceProvenance = DirectSourceProvenance.RecentWindowFallback;
            }
        }

        if (directModel is null)
        {
            directModel = FindFallbackPower(dealer, target, result);
            if (directModel is not null)
            {
                sourceProvenance = DirectSourceProvenance.DealerFallback;
            }
        }

        var card = cardSource ?? FindFirst<CardModel>(chain);
        var triggerCard = ResolveTriggerCardInfo(chain, card, directModel)
            ?? ResolvePowerOriginCardInfo(directModel);
        var accountablePlayer = ResolveAccountablePlayer(directModel, dealer, target, card);
        var selfLoss = IsSelfDamage(dealer, target, directModel, card);
        var category = ResolveCategory(target, directModel, dealer, accountablePlayer, selfLoss);
        var mechanism = ResolveDamageMechanism(directModel, props, selfLoss);
        var poisonSource = ResolvePoisonSource(mechanism, directModel);
        var playerShares = category == DamageCategory.Output
            ? ResolvePlayerShares(directModel, poisonTick)
            : null;

        if (category == DamageCategory.Ignored)
        {
            return new DamageAttribution(
                UnknownSource,
                string.Empty,
                DamageSourceType.Unknown,
                DamageCategory.Ignored,
                AttributionConfidence.Unknown,
                DamageMechanismKind.Unknown,
                SourceTrackingStatus.Unknown,
                null,
                null,
                null,
                null,
                null);
        }

        if (directModel is null)
        {
            var creatureConfidence = ResolveConfidence(sourceProvenance);
            var trackingStatus = ResolveTrackingStatus(null, triggerCard, creatureConfidence);

            if (dealer is not null)
            {
                var creatureKind = dealer.IsPet
                    ? DamageSourceType.Pet
                    : dealer.IsPlayer
                        ? card is not null
                            ? DamageSourceType.Card
                            : DamageSourceType.Unknown
                        : DamageSourceType.Enemy;
                var displayName = dealer.IsPet && card is not null
                    ? $"宠物 + {GetModelTitle(card)}"
                    : SafeCreatureName(dealer);

                return new DamageAttribution(
                    displayName,
                    GetCreatureInternalId(dealer),
                    creatureKind,
                    category,
                    creatureConfidence,
                    mechanism,
                    trackingStatus,
                    triggerCard?.DisplayName,
                    triggerCard?.InternalId,
                    null,
                    poisonSource,
                    accountablePlayer,
                    playerShares);
            }

            return new DamageAttribution(
                UnknownSource,
                string.Empty,
                DamageSourceType.Unknown,
                category,
                AttributionConfidence.Unknown,
                mechanism,
                trackingStatus,
                triggerCard?.DisplayName,
                triggerCard?.InternalId,
                null,
                poisonSource,
                accountablePlayer,
                playerShares);
        }

        var sourceType = GetSourceType(directModel);
        var displayTitle = GetModelTitle(directModel);
        var internalId = GetModelInternalId(directModel);
        var semanticTag = ResolveSemanticTag(directModel, chain, result, props);
        var confidence = ResolveConfidence(sourceProvenance);

        if (directModel is OrbModel && string.IsNullOrWhiteSpace(semanticTag))
        {
            semanticTag = CountSameModel(chain, directModel) > 1 ? "激发" : "被动/触发";
        }

        if (sourceType == DamageSourceType.Unknown)
        {
            confidence = AttributionConfidence.Unknown;
        }

        var finalTrackingStatus = ResolveTrackingStatus(directModel, triggerCard, confidence);

        return new DamageAttribution(
            displayTitle,
            internalId,
            sourceType,
            category,
            confidence,
            mechanism,
            finalTrackingStatus,
            triggerCard?.DisplayName,
            triggerCard?.InternalId,
            semanticTag,
            poisonSource,
            accountablePlayer,
            playerShares);
    }
    private static AttributionConfidence ResolveConfidence(DirectSourceProvenance provenance)
    {
        return provenance == DirectSourceProvenance.GameModelStack
            ? AttributionConfidence.Exact
            : AttributionConfidence.Inferred;
    }

    public static DamageAttribution? ResolveDoomKill(DoomPower power, Creature target)
    {
        if (!target.IsEnemy)
        {
            return null;
        }

        var hasShares = TryGetPowerShares(power, out var playerShares);
        var accountablePlayer = GetPlayerForCreature(power.Applier)
            ?? (hasShares ? playerShares[0].Player : null);
        if (accountablePlayer is null)
        {
            return null;
        }

        var triggerCard = ResolvePowerOriginCardInfo(power);
        return new DamageAttribution(
            GetModelTitle(power),
            GetModelInternalId(power),
            DamageSourceType.Power,
            DamageCategory.Output,
            AttributionConfidence.Exact,
            DamageMechanismKind.PowerEffect,
            SourceTrackingStatus.Tracked,
            triggerCard?.DisplayName,
            triggerCard?.InternalId,
            "灾厄击杀",
            null,
            accountablePlayer,
            hasShares ? playerShares : null);
    }
    private static AbstractModel? SelectDirectModel(IReadOnlyList<AbstractModel> chain, Creature target)
    {
        foreach (var model in chain)
        {
            if (model is DpsHook)
            {
                continue;
            }

            if (model is CardModel or RelicModel or PotionModel or OrbModel or EventModel)
            {
                return model;
            }

            if (model is PowerModel power && IsDamagePower(power)
                && IsPlausibleIncomingDamagePower(power, target))
            {
                return power;
            }
        }

        return null;
    }

    private static bool IsDamagePower(PowerModel power)
    {
        return DamagePowerNames.Contains(power.GetType().Name);
    }

    private static bool IsPlausibleIncomingDamagePower(PowerModel power, Creature target)
    {
        return !SelfReactiveDamagePowerNames.Contains(power.GetType().Name)
            || !ReferenceEquals(power.Owner, target);
    }

    private static T? FindFirst<T>(IReadOnlyList<AbstractModel> chain) where T : AbstractModel
    {
        foreach (var model in chain)
        {
            if (model is T typed)
            {
                return typed;
            }
        }

        return null;
    }

    private static PowerModel? FindFallbackPower(Creature? dealer, Creature target, DamageResult result)
    {
        var damage = result.BlockedDamage + result.UnblockedDamage + result.OverkillDamage;
        var candidates = new List<PowerModel>();

        AddPowers(candidates, target.Powers, target, target, damage);
        if (dealer is not null)
        {
            AddPowers(candidates, dealer.Powers, dealer, target, damage);
        }

        return candidates
            .OrderByDescending(power => GetFallbackPriority(power.GetType().Name))
            .ThenByDescending(power => power.Amount == damage)
            .FirstOrDefault();
    }

    private static void AddPowers(
        ICollection<PowerModel> candidates,
        IEnumerable<PowerModel> powers,
        Creature expectedOwner,
        Creature damageTarget,
        int damage)
    {
        foreach (var power in powers)
        {
            var powerName = power.GetType().Name;
            if (!DamagePowerNames.Contains(powerName))
            {
                continue;
            }

            if (SelfReactiveDamagePowerNames.Contains(powerName)
                && ReferenceEquals(power.Owner, damageTarget))
            {
                continue;
            }

            if (power.Owner != expectedOwner && powerName is not "PoisonPower" and not "ConstrictPower")
            {
                continue;
            }

            candidates.Add(power);
        }
    }

    private static int GetFallbackPriority(string powerName)
    {
        return powerName switch
        {
            "PoisonPower" => 100,
            "ConstrictPower" => 95,
            "JuggernautPower" => 90,
            "BlackHolePower" => 85,
            "ThornsPower" => 80,
            "ReflectPower" => 75,
            "FlameBarrierPower" => 75,
            _ => 50
        };
    }

    private static DamageCategory ResolveCategory(
        Creature target,
        AbstractModel? directModel,
        Creature? dealer,
        Player? accountablePlayer,
        bool selfLoss)
    {
        if (target.IsEnemy)
        {
            if (selfLoss)
            {
                return DamageCategory.Ignored;
            }

            if (accountablePlayer is not null)
            {
                return DamageCategory.Output;
            }

            if (dealer is not null && (dealer.IsPlayer || dealer.IsPet))
            {
                return DamageCategory.Output;
            }

            if (directModel is PowerModel power && IsPlayerSide(power))
            {
                return DamageCategory.Output;
            }

            return DamageCategory.Ignored;
        }

        if (target.IsPlayer || target.IsPet)
        {
            return selfLoss ? DamageCategory.SelfLoss : DamageCategory.Incoming;
        }

        return DamageCategory.Ignored;
    }

    private static bool IsSelfDamage(
        Creature? dealer,
        Creature target,
        AbstractModel? directModel,
        CardModel? card)
    {
        if (!target.IsPlayer && !target.IsPet)
        {
            return false;
        }

        var targetPlayer = GetPlayerForCreature(target);
        if (targetPlayer is null)
        {
            return false;
        }

        if (directModel is PowerModel power && power.Applier is not null)
        {
            return GetPlayerForCreature(power.Applier) == targetPlayer;
        }

        if (dealer is not null && GetPlayerForCreature(dealer) == targetPlayer)
        {
            return true;
        }

        if (card is not null)
        {
            return card.Owner == targetPlayer
                && (dealer is null || GetPlayerForCreature(dealer) == targetPlayer);
        }

        return false;
    }

    private static Player? ResolveAccountablePlayer(
        AbstractModel? directModel,
        Creature? dealer,
        Creature target,
        CardModel? card)
    {
        if (directModel is PowerModel power && power.Applier is not null)
        {
            var applier = GetPlayerForCreature(power.Applier);
            if (applier is not null)
            {
                return applier;
            }
        }

        var modelPlayer = GetModelPlayer(directModel);
        if (modelPlayer is not null)
        {
            return modelPlayer;
        }

        if (card is not null)
        {
            return card.Owner;
        }

        return GetPlayerForCreature(dealer) ?? GetPlayerForCreature(target);
    }

    private static Player? GetModelPlayer(AbstractModel? model)
    {
        return model switch
        {
            CardModel card => card.Owner,
            PotionModel potion => potion.Owner,
            RelicModel relic => relic.Owner,
            OrbModel orb => orb.Owner,
            PowerModel power => GetPlayerForCreature(power.Applier) ?? GetPlayerForCreature(power.Owner),
            _ => null
        };
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

    private static bool IsPlayerSide(PowerModel power)
    {
        return power.Owner.IsPlayer || power.Owner.IsPet || power.Applier?.IsPlayer == true || power.Applier?.IsPet == true;
    }

    private static TriggerCardInfo? ResolveTriggerCardInfo(
        IReadOnlyList<AbstractModel> chain,
        CardModel? card,
        AbstractModel? directModel)
    {
        if (card is not null && !ReferenceEquals(card, directModel))
        {
            return CreateTriggerCardInfo(card);
        }

        foreach (var model in chain)
        {
            if (model is CardModel candidate && !ReferenceEquals(candidate, directModel))
            {
                return CreateTriggerCardInfo(candidate);
            }
        }

        return null;
    }

    private static TriggerCardInfo? ResolvePowerOriginCardInfo(AbstractModel? directModel)
    {
        if (directModel is not PowerModel power)
        {
            return null;
        }

        if (TryGetTrackedPowerOrigin(power, out var trackedOrigin))
        {
            return trackedOrigin.SourceType == DamageSourceType.Card
                ? new TriggerCardInfo(trackedOrigin.DisplayName, trackedOrigin.InternalId)
                : null;
        }

        if (!PowerOriginCardTypes.TryGetValue(power.GetType(), out var cardType))
        {
            return null;
        }

        try
        {
            var originCard = ModelDb.GetById<CardModel>(ModelDb.GetId(cardType));
            return new TriggerCardInfo(
                GetModelTitle(originCard),
                GetModelInternalId(originCard));
        }
        catch
        {
            // 无法解析稳定卡牌 ID 时保留能力来源，不能影响伤害记录。
            return null;
        }
    }

    public static void RecordPowerOrigin(
        PowerModel power,
        decimal amount,
        Creature? applier,
        AbstractModel? sourceModel)
    {
        if (amount <= 0)
        {
            return;
        }

        RecordPowerOwnership(power, amount, applier);

        var powerName = power.GetType().Name;
        var isDoomPower = power is DoomPower;
        var isPoisonPower = powerName == "PoisonPower";
        var tracksPowerOrigin = isDoomPower
            || isPoisonPower
            || DamagePowerNames.Contains(powerName)
            && (sourceModel is not null || AmbiguousPowerNames.Contains(powerName));
        if (!tracksPowerOrigin)
        {
            return;
        }

        var snapshot = sourceModel is null
            ? null
            : new PowerOriginSnapshot(
                GetModelTitle(sourceModel),
                GetModelInternalId(sourceModel),
                GetSourceType(sourceModel),
                ToAppliedStacks(amount));
        lock (PowerOriginSyncRoot)
        {
            var record = PowerOrigins.GetValue(power, _ => new PowerOriginRecord());
            if (snapshot is null)
            {
                record.RecordUnknownSource();
            }
            else
            {
                record.Record(snapshot);
            }
        }
    }

    public static bool BeginPoisonTick(PoisonPower power, IReadOnlyList<Creature> participants)
    {
        if (!participants.Contains(power.Owner))
        {
            return false;
        }

        var accelerantAmount = power.Owner.CombatState
            .GetOpponentsOf(power.Owner)
            .Where(creature => creature.IsAlive)
            .Sum(creature => creature.GetPowerAmount<AccelerantPower>());
        var triggerCount = Math.Min(power.Amount, 1 + accelerantAmount);
        if (triggerCount <= 0)
        {
            return false;
        }

        var poisonShares = GetPowerSharesWithFallback(power);
        var accelerantShares = BuildAccelerantShares(power);
        lock (PowerOriginSyncRoot)
        {
            PoisonTicks[power.Owner] = new PoisonTickState(
                power,
                poisonShares,
                accelerantShares,
                triggerCount);
        }

        return true;
    }

    public static void EndPoisonTick(Creature owner)
    {
        lock (PowerOriginSyncRoot)
        {
            PoisonTicks.Remove(owner);
        }
    }

    public static void ResetSourceTracking()
    {
        lock (PowerOriginSyncRoot)
        {
            PoisonTicks.Clear();
        }
    }

    private static bool TryGetPoisonTick(Creature target, out PoisonTickState state)
    {
        lock (PowerOriginSyncRoot)
        {
            return PoisonTicks.TryGetValue(target, out state!);
        }
    }

    private static IReadOnlyList<PlayerDamageShare>? ResolvePlayerShares(
        AbstractModel? directModel,
        PoisonTickState? poisonTick)
    {
        if (poisonTick is not null)
        {
            lock (PowerOriginSyncRoot)
            {
                poisonTick.Consume(out var tickShares);
                return tickShares.Count == 0 ? null : tickShares;
            }
        }

        return directModel is PowerModel power && TryGetPowerShares(power, out var shares)
            ? shares
            : null;
    }

    private static void RecordPowerOwnership(
        PowerModel power,
        decimal amount,
        Creature? applier)
    {
        var player = ResolvePowerPlayer(power, applier);
        if (player is null || amount <= 0)
        {
            return;
        }

        lock (PowerOriginSyncRoot)
        {
            PowerOwnerships
                .GetValue(power, _ => new PowerOwnershipRecord())
                .Record(player, amount);
        }
    }

    public static bool TryGetPowerShares(
        PowerModel power,
        out IReadOnlyList<PlayerDamageShare> shares)
    {
        lock (PowerOriginSyncRoot)
        {
            if (PowerOwnerships.TryGetValue(power, out var record)
                && record.TryGetShares(out shares))
            {
                return true;
            }
        }

        shares = Array.Empty<PlayerDamageShare>();
        return false;
    }

    private static IReadOnlyList<PlayerDamageShare> GetPowerSharesWithFallback(PowerModel power)
    {
        if (TryGetPowerShares(power, out var shares))
        {
            return shares;
        }

        var player = ResolvePowerPlayer(power, null);
        return player is null
            ? Array.Empty<PlayerDamageShare>()
            : new[] { new PlayerDamageShare(player, 1m) };
    }

    private static IReadOnlyList<PlayerDamageShare> BuildAccelerantShares(PowerModel poisonPower)
    {
        var weightedShares = new List<PlayerDamageShare>();
        foreach (var opponent in poisonPower.Owner.CombatState
                     .GetOpponentsOf(poisonPower.Owner)
                     .Where(creature => creature.IsAlive))
        {
            var accelerant = opponent.GetPower<AccelerantPower>();
            if (accelerant is null || accelerant.Amount <= 0)
            {
                continue;
            }

            var ownershipShares = TryGetPowerShares(accelerant, out var recordedShares)
                ? recordedShares
                : GetPowerSharesWithFallback(accelerant);
            foreach (var ownershipShare in ownershipShares)
            {
                weightedShares.Add(new PlayerDamageShare(
                    ownershipShare.Player,
                    accelerant.Amount * ownershipShare.Fraction));
            }
        }

        return NormalizeShares(weightedShares);
    }

    private static IReadOnlyList<PlayerDamageShare> NormalizeShares(
        IEnumerable<PlayerDamageShare> shares)
    {
        var grouped = new Dictionary<ulong, PlayerDamageShare>();
        foreach (var share in shares)
        {
            if (share.Player is null || share.Fraction <= 0)
            {
                continue;
            }

            if (grouped.TryGetValue(share.Player.NetId, out var existing))
            {
                grouped[share.Player.NetId] = existing with
                {
                    Fraction = existing.Fraction + share.Fraction
                };
            }
            else
            {
                grouped.Add(share.Player.NetId, share);
            }
        }

        var total = grouped.Values.Sum(share => share.Fraction);
        if (total <= 0)
        {
            return Array.Empty<PlayerDamageShare>();
        }

        return grouped.Values
            .Select(share => share with { Fraction = share.Fraction / total })
            .OrderByDescending(share => share.Fraction)
            .ThenBy(share => share.Player.NetId)
            .ToArray();
    }

    private static Player? ResolvePowerPlayer(PowerModel power, Creature? applier)
    {
        return GetPlayerForCreature(applier)
            ?? GetPlayerForCreature(power.Applier)
            ?? GetPlayerForCreature(power.Owner);
    }

    private static int ToAppliedStacks(decimal amount)
    {
        return amount >= int.MaxValue ? int.MaxValue : decimal.ToInt32(amount);
    }

    private static bool TryGetTrackedPowerOrigin(PowerModel power, out PowerOriginSnapshot snapshot)
    {
        lock (PowerOriginSyncRoot)
        {
            if (!PowerOrigins.TryGetValue(power, out var record))
            {
                snapshot = null!;
                return false;
            }

            return record.TryGetUnambiguousSnapshot(out snapshot);
        }
    }

    private static TriggerCardInfo CreateTriggerCardInfo(CardModel card)
    {
        return new TriggerCardInfo(GetModelTitle(card), GetModelInternalId(card));
    }

    private static DamageMechanismKind ResolveDamageMechanism(
        AbstractModel? directModel,
        ValueProp props,
        bool selfLoss)
    {
        var typeName = directModel?.GetType().Name;
        switch (typeName)
        {
            case "PoisonPower":
                return DamageMechanismKind.Poison;
            case "ThornsPower":
                return DamageMechanismKind.Thorns;
            case "FlameBarrierPower":
            case "ReflectPower":
                return DamageMechanismKind.Reflect;
        }

        if (selfLoss && props.HasFlag(ValueProp.Unblockable) && props.HasFlag(ValueProp.Unpowered))
        {
            return DamageMechanismKind.HpLoss;
        }

        if (props.HasFlag(ValueProp.Move))
        {
            return DamageMechanismKind.DirectAttack;
        }

        if (directModel is CardModel)
        {
            return DamageMechanismKind.DirectAttack;
        }

        if (directModel is PowerModel)
        {
            return DamageMechanismKind.PowerEffect;
        }

        return DamageMechanismKind.Unknown;
    }

    private static SourceTrackingStatus ResolveTrackingStatus(
        AbstractModel? directModel,
        TriggerCardInfo? triggerCard,
        AttributionConfidence confidence)
    {
        if (directModel is PowerModel poisonPower
            && poisonPower.GetType().Name == "PoisonPower")
        {
            return TryGetTrackedPowerOrigin(poisonPower, out _)
                ? SourceTrackingStatus.Tracked
                : SourceTrackingStatus.Deferred;
        }

        if (confidence == AttributionConfidence.Unknown || directModel is null && triggerCard is null)
        {
            return SourceTrackingStatus.Unknown;
        }

        return SourceTrackingStatus.Tracked;
    }

    private static PoisonSourceInfo? ResolvePoisonSource(
        DamageMechanismKind mechanism,
        AbstractModel? directModel)
    {
        if (mechanism != DamageMechanismKind.Poison)
        {
            return null;
        }

        if (directModel is PowerModel power
            && TryGetTrackedPowerOrigin(power, out var origin))
        {
            return new PoisonSourceInfo(
                origin.InternalId,
                origin.DisplayName,
                origin.AppliedStacks,
                SourceTrackingStatus.Tracked);
        }

        return PoisonSourceInfo.Deferred;
    }

    private sealed record TriggerCardInfo(string DisplayName, string InternalId);
    private sealed record PowerOriginSnapshot(
        string DisplayName,
        string InternalId,
        DamageSourceType SourceType,
        int AppliedStacks);

    private sealed class PowerOwnershipRecord
    {
        private readonly Dictionary<ulong, PowerOwnershipEntry> _entries = new();

        public void Record(Player player, decimal amount)
        {
            if (_entries.TryGetValue(player.NetId, out var entry))
            {
                entry.Player = player;
                entry.AppliedAmount += amount;
                return;
            }

            _entries.Add(player.NetId, new PowerOwnershipEntry(player, amount));
        }

        public bool TryGetShares(out IReadOnlyList<PlayerDamageShare> shares)
        {
            var total = _entries.Values.Sum(entry => entry.AppliedAmount);
            if (total <= 0)
            {
                shares = Array.Empty<PlayerDamageShare>();
                return false;
            }

            shares = _entries.Values
                .Where(entry => entry.AppliedAmount > 0)
                .Select(entry => new PlayerDamageShare(
                    entry.Player,
                    entry.AppliedAmount / total))
                .OrderByDescending(share => share.Fraction)
                .ThenBy(share => share.Player.NetId)
                .ToArray();
            return shares.Count > 0;
        }
    }

    private sealed class PowerOwnershipEntry
    {
        public PowerOwnershipEntry(Player player, decimal appliedAmount)
        {
            Player = player;
            AppliedAmount = appliedAmount;
        }

        public Player Player { get; set; }

        public decimal AppliedAmount { get; set; }
    }

    private sealed class PoisonTickState
    {
        public PoisonTickState(
            PoisonPower power,
            IReadOnlyList<PlayerDamageShare> poisonShares,
            IReadOnlyList<PlayerDamageShare> accelerantShares,
            int remaining)
        {
            Power = power;
            PoisonShares = poisonShares;
            AccelerantShares = accelerantShares;
            Remaining = remaining;
        }

        public PoisonPower Power { get; }

        public IReadOnlyList<PlayerDamageShare> PoisonShares { get; }

        public IReadOnlyList<PlayerDamageShare> AccelerantShares { get; }

        public int TickIndex { get; private set; }

        public int Remaining { get; private set; }

        public void Consume(out IReadOnlyList<PlayerDamageShare> shares)
        {
            if (Remaining <= 0)
            {
                shares = Array.Empty<PlayerDamageShare>();
                return;
            }

            shares = TickIndex == 0 || AccelerantShares.Count == 0
                ? PoisonShares
                : AccelerantShares;
            TickIndex++;
            Remaining--;
        }
    }

    private sealed class PowerOriginRecord
    {
        public PowerOriginSnapshot? Snapshot { get; private set; }

        public bool HasMixedSources { get; private set; }

        public bool HasUnknownSource { get; private set; }

        public void RecordUnknownSource()
        {
            HasUnknownSource = true;
        }

        public void Record(PowerOriginSnapshot snapshot)
        {
            if (Snapshot is null)
            {
                Snapshot = snapshot;
                return;
            }

            if (Snapshot.SourceType != snapshot.SourceType
                || !string.Equals(Snapshot.InternalId, snapshot.InternalId, StringComparison.Ordinal))
            {
                HasMixedSources = true;
                return;
            }

            Snapshot = Snapshot with
            {
                AppliedStacks = AddAppliedStacks(Snapshot.AppliedStacks, snapshot.AppliedStacks)
            };
        }

        private static int AddAppliedStacks(int current, int additional)
        {
            return current > int.MaxValue - additional
                ? int.MaxValue
                : current + additional;
        }

        public bool TryGetUnambiguousSnapshot(out PowerOriginSnapshot snapshot)
        {
            if (!HasMixedSources && !HasUnknownSource && Snapshot is not null)
            {
                snapshot = Snapshot;
                return true;
            }

            snapshot = null!;
            return false;
        }
    }

    private static string? ResolveSemanticTag(
        AbstractModel directModel,
        IReadOnlyList<AbstractModel> chain,
        DamageResult result,
        ValueProp props)
    {
        var typeName = directModel.GetType().Name;
        if (SemanticTags.TryGetValue(typeName, out var semanticTag))
        {
            return semanticTag;
        }

        if (directModel is PowerModel power && DamagePowerNames.Contains(typeName))
        {
            return "能力伤害";
        }

        if (directModel is OrbModel)
        {
            return "充能球伤害";
        }

        if (directModel is RelicModel)
        {
            return "遗物伤害";
        }

        if (directModel is PotionModel)
        {
            return "药水伤害";
        }

        if (directModel is CardModel && result.OverkillDamage > 0)
        {
            return "过量伤害";
        }

        if (props.HasFlag(ValueProp.Unblockable))
        {
            return "不可格挡";
        }

        return null;
    }

    private static DamageSourceType GetSourceType(AbstractModel model)
    {
        return model switch
        {
            CardModel => DamageSourceType.Card,
            PowerModel => DamageSourceType.Power,
            RelicModel => DamageSourceType.Relic,
            PotionModel => DamageSourceType.Potion,
            OrbModel => DamageSourceType.Orb,
            EventModel => DamageSourceType.Event,
            _ => DamageSourceType.Unknown
        };
    }

    private static string GetModelTitle(AbstractModel model)
    {
        try
        {
            return model switch
            {
                CardModel card => card.Title,
                PowerModel power => GetLocString(power.Title),
                RelicModel relic => GetLocString(relic.Title),
                PotionModel potion => GetLocString(potion.Title),
                OrbModel orb => GetLocString(orb.Title),
                _ => model.Id.Entry
            };
        }
        catch
        {
            return model.Id.Entry;
        }
    }

    private static string GetLocString(MegaCrit.Sts2.Core.Localization.LocString value)
    {
        try
        {
            var text = value.GetFormattedText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }
        catch
        {
            // 某些临时模型可能没有可用的本地化文本，退回内部 ID。
        }

        return value.ToString();
    }

    private static string GetModelInternalId(AbstractModel model)
    {
        try
        {
            return model.Id.ToString();
        }
        catch
        {
            return model.GetType().Name;
        }
    }

    private static string GetCreatureInternalId(Creature creature)
    {
        try
        {
            return creature.ModelId.ToString();
        }
        catch
        {
            return creature.GetType().Name;
        }
    }

    private static string SafeCreatureName(Creature creature)
    {
        try
        {
            var name = creature.Name;
            return string.IsNullOrWhiteSpace(name) ? UnknownSource : name;
        }
        catch
        {
            return UnknownSource;
        }
    }

    private static int CountSameModel(IReadOnlyList<AbstractModel> chain, AbstractModel model)
    {
        var count = 0;
        foreach (var candidate in chain)
        {
            if (ReferenceEquals(candidate, model))
            {
                count++;
            }
        }

        return count;
    }
}
