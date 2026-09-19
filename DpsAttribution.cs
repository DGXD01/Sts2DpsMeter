using System;
using System.Collections.Generic;
using System.Linq;
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
    Player? AccountablePlayer);

public static class DpsAttribution
{
    private const string UnknownSource = "未知来源";

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
        "TheBombPower",
        "ThornsPower",
        "ThunderPower"
    };

    private static readonly Dictionary<string, string> SemanticTags = new(StringComparer.Ordinal)
    {
        ["PoisonPower"] = "中毒/持续伤害",
        ["InfernoPower"] = "火焰/持续伤害",
        ["ThunderPower"] = "闪电",
        ["FlameBarrierPower"] = "火焰/反伤",
        ["ReflectPower"] = "反伤",
        ["ThornsPower"] = "反伤",
        ["ConstrictPower"] = "束缚/持续伤害"
    };

    private static readonly Dictionary<Type, Type> PowerOriginCardTypes = new()
    {
        [typeof(FlameBarrierPower)] = typeof(FlameBarrier),
        [typeof(InfernoPower)] = typeof(Inferno),
        [typeof(JuggernautPower)] = typeof(Juggernaut),
        [typeof(CrimsonMantlePower)] = typeof(CrimsonMantle)
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
        var directModel = SelectDirectModel(chain);
        var directFromGameStack = directModel is not null;

        if (directModel is null && cardSource is not null)
        {
            directModel = cardSource;
            directFromGameStack = true;
        }

        if (directModel is null)
        {
            directModel = DpsSourceContext.GetRecentModel<PowerModel>();
        }

        if (directModel is null)
        {
            directModel = DpsSourceContext.GetRecentModel<RelicModel>();
        }

        if (directModel is null)
        {
            directModel = DpsSourceContext.GetRecentModel<PotionModel>();
        }

        if (directModel is null)
        {
            directModel = DpsSourceContext.GetRecentModel<OrbModel>();
        }

        if (directModel is null)
        {
            directModel = FindFallbackPower(dealer, target, result);
        }

        var card = cardSource ?? FindFirst<CardModel>(chain);
        var triggerCard = ResolveTriggerCardInfo(chain, card, directModel)
            ?? ResolvePowerOriginCardInfo(directModel);
        var accountablePlayer = ResolveAccountablePlayer(directModel, dealer, target, card);
        var selfLoss = IsSelfDamage(dealer, target, directModel, card);
        var category = ResolveCategory(target, directModel, dealer, accountablePlayer, selfLoss);
        var mechanism = ResolveDamageMechanism(directModel, props, selfLoss);
        var poisonSource = ResolvePoisonSource(mechanism);

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
            var creatureConfidence = directFromGameStack
                ? AttributionConfidence.Exact
                : AttributionConfidence.Inferred;
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
                    accountablePlayer);
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
                accountablePlayer);
        }

        var sourceType = GetSourceType(directModel);
        var displayTitle = GetModelTitle(directModel);
        var internalId = GetModelInternalId(directModel);
        var semanticTag = ResolveSemanticTag(directModel, chain, result, props);
        var confidence = directFromGameStack
            ? AttributionConfidence.Exact
            : AttributionConfidence.Inferred;

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
            accountablePlayer);
    }
    private static AbstractModel? SelectDirectModel(IReadOnlyList<AbstractModel> chain)
    {
        foreach (var model in chain)
        {
            if (model is DpsHook)
            {
                continue;
            }

            if (model is CardModel or PowerModel or RelicModel or PotionModel or OrbModel or EventModel)
            {
                return model;
            }
        }

        return null;
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

        AddPowers(candidates, target.Powers, target, damage);
        if (dealer is not null)
        {
            AddPowers(candidates, dealer.Powers, dealer, damage);
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
        int damage)
    {
        foreach (var power in powers)
        {
            var powerName = power.GetType().Name;
            if (!DamagePowerNames.Contains(powerName))
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
        if (directModel is null || !PowerOriginCardTypes.TryGetValue(directModel.GetType(), out var cardType))
        {
            return null;
        }

        try
        {
            return new TriggerCardInfo(
                GetModelTitle(directModel),
                ModelDb.GetId(cardType).ToString());
        }
        catch
        {
            // 无法解析稳定卡牌 ID 时保留能力来源，不能影响伤害记录。
            return null;
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
        if (directModel?.GetType().Name == "PoisonPower")
        {
            return SourceTrackingStatus.Deferred;
        }

        if (confidence == AttributionConfidence.Unknown || directModel is null && triggerCard is null)
        {
            return SourceTrackingStatus.Unknown;
        }

        return SourceTrackingStatus.Tracked;
    }

    private static PoisonSourceInfo? ResolvePoisonSource(DamageMechanismKind mechanism)
    {
        return mechanism == DamageMechanismKind.Poison
            ? PoisonSourceInfo.Deferred
            : null;
    }

    private sealed record TriggerCardInfo(string DisplayName, string InternalId);
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
