using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;

namespace Sts2DpsMeter;

internal static class DpsDebuffAttribution
{
    private const decimal VulnerablePowerMinimumMultiplier = 1m;

    public static IReadOnlyList<DebuffDamageTransfer> Resolve(
        Player dealerPlayer,
        Creature? dealer,
        Creature target,
        long blockedDamage,
        long unblockedDamage,
        long overkillDamage,
        ValueProp props,
        float factor)
    {
        var totalDamage = blockedDamage + unblockedDamage + overkillDamage;
        if (!DpsAttributionMath.ShouldResolveDebuffTransfer(
                factor,
                dealer is not null,
                target.IsEnemy,
                props.IsPoweredAttack(),
                totalDamage))
        {
            return Array.Empty<DebuffDamageTransfer>();
        }

        var factors = ResolveFactors(dealer!, target, props);
        if (factors.Count == 0)
        {
            return Array.Empty<DebuffDamageTransfer>();
        }

        var multiplierProduct = 1d;
        foreach (var debuffFactor in factors)
        {
            var multiplier = (double)debuffFactor.Multiplier;
            if (multiplier > 1d)
            {
                multiplierProduct *= multiplier;
            }
        }

        if (!double.IsFinite(multiplierProduct) || multiplierProduct <= 1d)
        {
            return Array.Empty<DebuffDamageTransfer>();
        }

        var extraDamage = DpsAttributionMath.CalculateExtraDamage(totalDamage, multiplierProduct);
        if (extraDamage <= 0d)
        {
            return Array.Empty<DebuffDamageTransfer>();
        }

        var logProduct = Math.Log(multiplierProduct);
        if (logProduct <= 0d)
        {
            return Array.Empty<DebuffDamageTransfer>();
        }

        var factorWeight = DpsAttributionMath.NormalizeFactor(factor);
        var rawTransfers = new List<RawDebuffTransfer>();
        foreach (var debuffFactor in factors)
        {
            var multiplier = (double)debuffFactor.Multiplier;
            if (multiplier <= 1d)
            {
                continue;
            }

            var logContribution = Math.Log(multiplier) / logProduct;
            if (logContribution <= 0d)
            {
                continue;
            }

            var factorContribution = extraDamage
                * logContribution
                * factorWeight
                * debuffFactor.TransferableFraction;
            if (factorContribution <= 0d)
            {
                continue;
            }

            var componentWeightTotal = debuffFactor.Components.Sum(
                component => Math.Max(component.Weight, 0d));
            if (componentWeightTotal <= 0d)
            {
                continue;
            }

            foreach (var component in debuffFactor.Components)
            {
                var componentContribution = factorContribution
                    * Math.Max(component.Weight, 0d)
                    / componentWeightTotal;
                if (componentContribution <= 0d)
                {
                    continue;
                }

                foreach (var share in DpsAttribution.GetPowerSharesWithFallback(component.Power))
                {
                    if (share.Player is null
                        || share.Fraction <= 0m
                        || share.Player.NetId == dealerPlayer.NetId)
                    {
                        continue;
                    }

                    var amount = componentContribution * (double)share.Fraction;
                    if (amount <= 0d)
                    {
                        continue;
                    }

                    rawTransfers.Add(new RawDebuffTransfer(
                        share.Player,
                        component.InternalId,
                        component.DisplayName,
                        amount));
                }
            }
        }

        if (rawTransfers.Count == 0)
        {
            return Array.Empty<DebuffDamageTransfer>();
        }

        var roundedTotal = DpsAttributionMath.ClampTransferTotal(
            totalDamage,
            rawTransfers.Sum(item => item.Amount));
        if (roundedTotal <= 0)
        {
            return Array.Empty<DebuffDamageTransfer>();
        }

        var rawWeights = rawTransfers.Select(item => item.Amount).ToArray();
        var componentTotals = DpsAttributionMath.AllocateComponents(
            roundedTotal,
            blockedDamage,
            unblockedDamage,
            overkillDamage);
        var blockedAmounts = DpsAttributionMath.AllocateByWeights(componentTotals.BlockedDamage, rawWeights);
        var unblockedAmounts = DpsAttributionMath.AllocateByWeights(componentTotals.UnblockedDamage, rawWeights);
        var overkillAmounts = DpsAttributionMath.AllocateByWeights(componentTotals.OverkillDamage, rawWeights);

        var transfers = new List<DebuffDamageTransfer>();
        for (var index = 0; index < rawTransfers.Count; index++)
        {
            var components = new DamageComponents(
                blockedAmounts[index],
                unblockedAmounts[index],
                overkillAmounts[index]);
            if (components.BlockedDamage + components.UnblockedDamage + components.OverkillDamage <= 0)
            {
                continue;
            }

            var rawTransfer = rawTransfers[index];
            transfers.Add(new DebuffDamageTransfer(
                rawTransfer.Player,
                CreateAttribution(rawTransfer.InternalId, rawTransfer.DisplayName, rawTransfer.Player),
                components.BlockedDamage,
                components.UnblockedDamage,
                components.OverkillDamage));
        }

        return transfers;
    }

    private static IReadOnlyList<DebuffFactor> ResolveFactors(
        Creature dealer,
        Creature target,
        ValueProp props)
    {
        var factors = new List<DebuffFactor>();
        var vulnerable = target.GetPower<VulnerablePower>();
        if (vulnerable is not null)
        {
            var vulnerableMultiplier = vulnerable.DynamicVars["DamageIncrease"].BaseValue;
            if (vulnerableMultiplier > VulnerablePowerMinimumMultiplier)
            {
                var paperPhrogExtra = dealer.Player?.GetRelic<PaperPhrog>() is null ? 0d : 0.25d;
                var cruelty = dealer.GetPower<CrueltyPower>();
                var crueltyExtra = cruelty is null ? 0d : (double)cruelty.Amount / 100d;
                var debilitate = target.GetPower<DebilitatePower>();
                // 此处只需要拆分比例，单位伤害即可；实际金额会在 Resolve 中按本次伤害分配。
                var breakdown = DpsAttributionMath.CalculateVulnerableBreakdown(
                    1,
                    (double)vulnerableMultiplier,
                    paperPhrogExtra,
                    crueltyExtra,
                    debilitate is not null);
                if (breakdown.TotalExtraDamage > 0d)
                {
                    var components = new List<DebuffFactorComponent>
                    {
                        new(
                            "ASSIST_VULNERABLE_POWER",
                            "助攻：易伤",
                            vulnerable,
                            breakdown.VulnerableExtraDamage)
                    };
                    if (debilitate is not null && breakdown.DebilitateExtraDamage > 0d)
                    {
                        components.Add(new DebuffFactorComponent(
                            "ASSIST_DEBILITATE_VULNERABLE",
                            "助攻：易伤强化",
                            debilitate,
                            breakdown.DebilitateExtraDamage));
                    }

                    factors.Add(new DebuffFactor(
                        (decimal)breakdown.FinalMultiplier,
                        breakdown.TransferableExtraDamage / breakdown.TotalExtraDamage,
                        components));
                }
            }
        }

        foreach (var flanking in target.GetPowerInstances<FlankingPower>())
        {
            var multiplier = (decimal)flanking.Amount;
            if (multiplier > 1m && !ReferenceEquals(dealer, flanking.Applier))
            {
                factors.Add(CreateSingleFactor(
                    "ASSIST_FLANKING_POWER",
                    "助攻：侧击",
                    multiplier,
                    flanking));
            }
        }

        foreach (var knockdown in target.GetPowerInstances<KnockdownPower>())
        {
            var multiplier = (decimal)knockdown.Amount;
            if (multiplier > 1m && !ReferenceEquals(dealer, knockdown.Applier))
            {
                factors.Add(CreateSingleFactor(
                    "ASSIST_KNOCKDOWN_POWER",
                    "助攻：击倒",
                    multiplier,
                    knockdown));
            }
        }

        var slow = target.GetPower<SlowPower>();
        if (slow is not null)
        {
            var slowMultiplier = 1m + 0.1m * slow.DynamicVars["SlowAmount"].BaseValue;
            if (slowMultiplier > 1m)
            {
                factors.Add(CreateSingleFactor(
                    "ASSIST_SLOW_POWER",
                    "助攻：减速",
                    slowMultiplier,
                    slow));
            }
        }

        return factors;
    }

    private static DebuffFactor CreateSingleFactor(
        string internalId,
        string displayName,
        decimal multiplier,
        PowerModel power)
    {
        return new DebuffFactor(
            multiplier,
            1d,
            new[]
            {
                new DebuffFactorComponent(internalId, displayName, power, 1d)
            });
    }

    private static DamageAttribution CreateAttribution(
        string internalId,
        string displayName,
        Player player)
    {
        return new DamageAttribution(
            displayName,
            internalId,
            DamageSourceType.Power,
            DamageCategory.Output,
            AttributionConfidence.Inferred,
            DamageMechanismKind.PowerEffect,
            SourceTrackingStatus.Tracked,
            null,
            null,
            null,
            null,
            player);
    }

    private sealed record DebuffFactor(
        decimal Multiplier,
        double TransferableFraction,
        IReadOnlyList<DebuffFactorComponent> Components);

    private sealed record DebuffFactorComponent(
        string InternalId,
        string DisplayName,
        PowerModel Power,
        double Weight);

    private sealed record RawDebuffTransfer(
        Player Player,
        string InternalId,
        string DisplayName,
        double Amount);

}

internal sealed record DebuffDamageTransfer(
    Player Player,
    DamageAttribution Attribution,
    long BlockedDamage,
    long UnblockedDamage,
    long OverkillDamage)
{
    public long Damage => BlockedDamage + UnblockedDamage + OverkillDamage;
}
