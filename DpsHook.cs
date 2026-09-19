using System;
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
            DpsAttribution.RecordPowerOrigin(power, cardSource);
        }
        catch (Exception exception)
        {
            GD.PushError($"[{ModEntry.ModId}] 记录状态来源失败：{exception}");
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
