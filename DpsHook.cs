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
            DpsStats.Record(choiceContext, dealer, target, result, props, cardSource);
        }
        catch (Exception exception)
        {
            GD.PushError($"[{ModEntry.ModId}] 记录伤害事件失败：{exception}");
        }

        return Task.CompletedTask;
    }
}
