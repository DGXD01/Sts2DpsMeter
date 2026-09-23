using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Sts2DpsMeter;

public sealed class DpsHook : AbstractModel
{
    public override bool ShouldReceiveCombatHooks => true;

    public static DpsHook GetCanonical()
    {
        return ModelDb.GetById<DpsHook>(ModelDb.GetId<DpsHook>());
    }

    public override Task AfterPowerAmountChanged(
        PlayerChoiceContext choiceContext,
        PowerModel power,
        decimal amount,
        Creature? applier,
        CardModel? cardSource)
    {
        try
        {
            AbstractModel? sourceModel = cardSource;
            sourceModel ??= DpsSourceContext.GetCurrentEffect(applier);
            DpsAttribution.RecordPowerOrigin(power, amount, applier, sourceModel);
        }
        catch (Exception exception)
        {
            GD.PushError($"[{ModEntry.ModId}] 记录状态来源失败：{exception}");
        }

        return Task.CompletedTask;
    }

    public override Task AfterBlockGained(
        Creature creature,
        decimal amount,
        ValueProp props,
        CardModel? cardSource)
    {
        try
        {
            var player = creature.IsPlayer
                ? creature.Player
                : creature.IsPet ? creature.PetOwner : null;
            if (player is not null && DpsStats.RecordBlockGained(player, amount))
            {
                DpsMeterUi.RequestImmediateRefresh();
            }

            if (player is not null)
            {
                var source = DpsSourceContext.ResolveSourcePlayer(null, creature, cardSource);
                if (DpsStats.RecordAllySupport(
                        source,
                        player,
                        AllySupportKind.BlockGiven,
                        amount))
                {
                    DpsMeterUi.RequestImmediateRefresh();
                }
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"[{ModEntry.ModId}] 记录格挡获得失败：{exception}");
        }

        return Task.CompletedTask;
    }

    public override Task AfterCardDrawn(
        PlayerChoiceContext choiceContext,
        CardModel card,
        bool fromHandDraw)
    {
        try
        {
            if (fromHandDraw || card?.Owner is null)
            {
                return Task.CompletedTask;
            }

            var target = card.Owner;
            var source = DpsSourceContext.ResolveSourcePlayer(choiceContext, target.Creature);
            if (DpsStats.RecordAllySupport(
                    source,
                    target,
                    AllySupportKind.CardDrawn,
                    1m))
            {
                DpsMeterUi.RequestImmediateRefresh();
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"[{ModEntry.ModId}] 记录队友抽牌失败：{exception}");
        }

        return Task.CompletedTask;
    }

    public override Task AfterEnergySpent(CardModel card, int amount)
    {
        try
        {
            if (card?.Owner is not null && DpsStats.RecordEnergySpent(card.Owner, amount))
            {
                DpsMeterUi.RequestImmediateRefresh();
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"[{ModEntry.ModId}] 记录能量消耗失败：{exception}");
        }

        return Task.CompletedTask;
    }

    public override Task AfterDiedToDoom(
        PlayerChoiceContext choiceContext,
        IReadOnlyList<Creature> creatures)
    {
        try
        {
            if (DpsStats.RecordDoomKills(creatures))
            {
                DpsMeterUi.RequestImmediateRefresh();
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"[{ModEntry.ModId}] 记录灾厄击杀失败：{exception}");
        }

        return Task.CompletedTask;
    }
    public override Task AfterDamageGiven(
        PlayerChoiceContext choiceContext,
        Creature? dealer,
        DamageResult result,
        ValueProp props,
        Creature target,
        CardModel? cardSource)
    {
        try
        {
            if (DpsStats.Record(choiceContext, dealer, target, result, props, cardSource))
            {
                DpsMeterUi.RequestImmediateRefresh();
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"[{ModEntry.ModId}] 记录伤害事件失败：{exception}");
        }

        return Task.CompletedTask;
    }
}
