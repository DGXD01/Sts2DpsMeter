using System;
using System.Collections.Generic;
using System.Linq;

namespace Sts2DpsMeter;

internal static class DpsAttributionMath
{
    public static bool ShouldResolveDebuffTransfer(
        float factor,
        bool hasDealer,
        bool targetIsEnemy,
        bool isPoweredAttack,
        long totalDamage)
    {
        return !float.IsNaN(factor)
            && factor > 0f
            && hasDealer
            && targetIsEnemy
            && isPoweredAttack
            && totalDamage > 0;
    }

    public static double CalculateExtraDamage(long totalDamage, double multiplierProduct)
    {
        if (totalDamage <= 0 || !double.IsFinite(multiplierProduct) || multiplierProduct <= 1d)
        {
            return 0d;
        }

        var extraDamage = totalDamage - totalDamage / multiplierProduct;
        return extraDamage > 0d ? extraDamage : 0d;
    }

    public static VulnerableDamageBreakdown CalculateVulnerableBreakdown(
        long baseDamage,
        double vulnerableMultiplier,
        double paperPhrogExtra,
        double crueltyExtra,
        bool hasDebilitate)
    {
        if (baseDamage <= 0
            || !double.IsFinite(vulnerableMultiplier)
            || vulnerableMultiplier <= 1d
            || !double.IsFinite(paperPhrogExtra)
            || !double.IsFinite(crueltyExtra))
        {
            return default;
        }

        var vulnerableExtra = vulnerableMultiplier - 1d;
        var attackerExtra = Math.Max(paperPhrogExtra, 0d) + Math.Max(crueltyExtra, 0d);
        var debilitateFactor = hasDebilitate ? 2d : 1d;
        var finalMultiplier = 1d + debilitateFactor * (vulnerableExtra + attackerExtra);
        if (!double.IsFinite(finalMultiplier) || finalMultiplier <= 1d)
        {
            return default;
        }

        var vulnerableDamage = baseDamage * vulnerableExtra;
        var debilitateDamage = hasDebilitate ? vulnerableDamage : 0d;
        var attackerDamage = baseDamage * debilitateFactor * attackerExtra;
        return new VulnerableDamageBreakdown(
            finalMultiplier,
            vulnerableDamage,
            debilitateDamage,
            attackerDamage);
    }
    public static double NormalizeFactor(float factor)
    {
        if (float.IsNaN(factor))
        {
            return 0d;
        }

        return Math.Clamp((double)factor, 0d, 1d);
    }

    public static long ClampTransferTotal(long totalDamage, double rawTransferTotal)
    {
        if (totalDamage <= 0 || double.IsNaN(rawTransferTotal) || rawTransferTotal <= 0d)
        {
            return 0;
        }

        if (!double.IsFinite(rawTransferTotal) || rawTransferTotal >= totalDamage)
        {
            return totalDamage;
        }

        return RoundPositive(rawTransferTotal);
    }

    public static DamageComponents AllocateComponents(
        long total,
        long blockedDamage,
        long unblockedDamage,
        long overkillDamage)
    {
        if (total <= 0)
        {
            return default;
        }

        var weights = new[]
        {
            (double)blockedDamage,
            (double)unblockedDamage,
            (double)overkillDamage
        };
        var allocated = AllocateByWeights(total, weights);
        return new DamageComponents(allocated[0], allocated[1], allocated[2]);
    }

    public static long[] AllocateByWeights(long total, IReadOnlyList<double> weights)
    {
        var result = new long[weights.Count];
        if (total <= 0 || weights.Count == 0)
        {
            return result;
        }

        var weightTotal = weights.Sum(weight => weight > 0d ? weight : 0d);
        if (weightTotal <= 0d)
        {
            return result;
        }

        var remainders = new List<(int Index, double Remainder)>(weights.Count);
        long assigned = 0;
        for (var index = 0; index < weights.Count; index++)
        {
            var weight = weights[index] > 0d ? weights[index] : 0d;
            var raw = total * weight / weightTotal;
            var floor = (long)Math.Floor(raw);
            result[index] = floor;
            assigned += floor;
            remainders.Add((index, raw - floor));
        }

        var residual = total - assigned;
        foreach (var item in remainders
                     .OrderByDescending(item => item.Remainder)
                     .ThenBy(item => item.Index))
        {
            if (residual <= 0)
            {
                break;
            }

            result[item.Index]++;
            residual--;
        }

        return result;
    }

    public static bool TrySubtractComponents(
        DamageComponents allocation,
        IReadOnlyList<DamageComponents> transfers,
        out DamageComponents remaining)
    {
        if (allocation.BlockedDamage < 0
            || allocation.UnblockedDamage < 0
            || allocation.OverkillDamage < 0)
        {
            remaining = allocation;
            return false;
        }

        var blockedDamage = allocation.BlockedDamage;
        var unblockedDamage = allocation.UnblockedDamage;
        var overkillDamage = allocation.OverkillDamage;

        foreach (var transfer in transfers)
        {
            if (transfer.BlockedDamage < 0
                || transfer.UnblockedDamage < 0
                || transfer.OverkillDamage < 0
                || transfer.BlockedDamage > blockedDamage
                || transfer.UnblockedDamage > unblockedDamage
                || transfer.OverkillDamage > overkillDamage)
            {
                remaining = allocation;
                return false;
            }

            blockedDamage -= transfer.BlockedDamage;
            unblockedDamage -= transfer.UnblockedDamage;
            overkillDamage -= transfer.OverkillDamage;
        }

        remaining = new DamageComponents(blockedDamage, unblockedDamage, overkillDamage);
        return true;
    }

    public static IReadOnlyList<DebuffFactor> ResolveDebuffFactors(
        DpsDebuffResolutionInput input)
    {
        var factors = new List<DebuffFactor>();
        var vulnerable = input.Powers.FirstOrDefault(
            power => power.Kind == DpsDebuffPowerKind.Vulnerable);
        var debilitate = input.Powers.FirstOrDefault(
            power => power.Kind == DpsDebuffPowerKind.Debilitate);
        if (vulnerable is not null)
        {
            var breakdown = CalculateVulnerableBreakdown(
                1,
                vulnerable.Amount,
                input.PaperPhrogExtra,
                input.CrueltyExtra,
                debilitate is not null);
            if (breakdown.TotalExtraDamage > 0d)
            {
                var components = new List<DebuffFactorComponent>
                {
                    new(
                        "ASSIST_VULNERABLE_POWER",
                        "助攻：易伤",
                        vulnerable.Shares,
                        breakdown.VulnerableExtraDamage)
                };
                if (debilitate is not null && breakdown.DebilitateExtraDamage > 0d)
                {
                    components.Add(new DebuffFactorComponent(
                        "ASSIST_DEBILITATE_VULNERABLE",
                        "助攻：易伤强化",
                        debilitate.Shares,
                        breakdown.DebilitateExtraDamage));
                }

                if (double.IsFinite(breakdown.FinalMultiplier)
                    && breakdown.FinalMultiplier <= (double)decimal.MaxValue)
                {
                    factors.Add(new DebuffFactor(
                        (decimal)breakdown.FinalMultiplier,
                        breakdown.TransferableExtraDamage / breakdown.TotalExtraDamage,
                        components));
                }
            }
        }

        foreach (var power in input.Powers)
        {
            switch (power.Kind)
            {
                case DpsDebuffPowerKind.Flanking:
                    AddSingleFactor(
                        factors,
                        power,
                        power.Amount,
                        "ASSIST_FLANKING_POWER",
                        "助攻：侧击");
                    break;
                case DpsDebuffPowerKind.Knockdown:
                    AddSingleFactor(
                        factors,
                        power,
                        power.Amount,
                        "ASSIST_KNOCKDOWN_POWER",
                        "助攻：击倒");
                    break;
                case DpsDebuffPowerKind.Slow:
                    AddSingleFactor(
                        factors,
                        power,
                        1d + 0.1d * power.Amount,
                        "ASSIST_SLOW_POWER",
                        "助攻：减速");
                    break;
                case DpsDebuffPowerKind.Weak:
                case DpsDebuffPowerKind.Vulnerable:
                case DpsDebuffPowerKind.Debilitate:
                    break;
            }
        }

        return factors;
    }

    private static void AddSingleFactor(
        List<DebuffFactor> factors,
        DpsDebuffPowerInput power,
        double multiplier,
        string internalId,
        string displayName)
    {
        if (power.IsDealerApplied
            || !double.IsFinite(multiplier)
            || multiplier <= 1d
            || multiplier > (double)decimal.MaxValue)
        {
            return;
        }

        factors.Add(new DebuffFactor(
            (decimal)multiplier,
            1d,
            new[]
            {
                new DebuffFactorComponent(internalId, displayName, power.Shares, 1d)
            }));
    }

    public static IReadOnlyList<DpsDebuffTransferAllocation> CalculateDebuffTransfers(
        long blockedDamage,
        long unblockedDamage,
        long overkillDamage,
        ulong dealerPlayerId,
        float factor,
        IReadOnlyList<DebuffFactor> factors)
    {
        var totalDamage = blockedDamage + unblockedDamage + overkillDamage;
        if (totalDamage <= 0 || factors.Count == 0)
        {
            return Array.Empty<DpsDebuffTransferAllocation>();
        }

        var factorWeight = NormalizeFactor(factor);
        if (factorWeight <= 0d)
        {
            return Array.Empty<DpsDebuffTransferAllocation>();
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
            return Array.Empty<DpsDebuffTransferAllocation>();
        }

        var extraDamage = CalculateExtraDamage(totalDamage, multiplierProduct);
        if (extraDamage <= 0d)
        {
            return Array.Empty<DpsDebuffTransferAllocation>();
        }

        var logProduct = Math.Log(multiplierProduct);
        if (logProduct <= 0d)
        {
            return Array.Empty<DpsDebuffTransferAllocation>();
        }

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

                foreach (var share in component.Shares)
                {
                    if (share.PlayerId is not ulong playerId
                        || share.Fraction <= 0m
                        || playerId == dealerPlayerId)
                    {
                        continue;
                    }

                    var amount = componentContribution * (double)share.Fraction;
                    if (amount <= 0d)
                    {
                        continue;
                    }

                    rawTransfers.Add(new RawDebuffTransfer(
                        playerId,
                        component.InternalId,
                        component.DisplayName,
                        amount));
                }
            }
        }

        if (rawTransfers.Count == 0)
        {
            return Array.Empty<DpsDebuffTransferAllocation>();
        }

        var roundedTotal = ClampTransferTotal(
            totalDamage,
            rawTransfers.Sum(item => item.Amount));
        if (roundedTotal <= 0)
        {
            return Array.Empty<DpsDebuffTransferAllocation>();
        }

        var rawWeights = rawTransfers.Select(item => item.Amount).ToArray();
        var componentTotals = AllocateComponents(
            roundedTotal,
            blockedDamage,
            unblockedDamage,
            overkillDamage);
        var blockedAmounts = AllocateByWeights(componentTotals.BlockedDamage, rawWeights);
        var unblockedAmounts = AllocateByWeights(componentTotals.UnblockedDamage, rawWeights);
        var overkillAmounts = AllocateByWeights(componentTotals.OverkillDamage, rawWeights);

        var transfers = new List<DpsDebuffTransferAllocation>();
        for (var index = 0; index < rawTransfers.Count; index++)
        {
            var components = new DamageComponents(
                blockedAmounts[index],
                unblockedAmounts[index],
                overkillAmounts[index]);
            if (components.Damage <= 0)
            {
                continue;
            }

            var rawTransfer = rawTransfers[index];
            transfers.Add(new DpsDebuffTransferAllocation(
                rawTransfer.PlayerId,
                rawTransfer.InternalId,
                rawTransfer.DisplayName,
                components));
        }

        return transfers;
    }
    private sealed record RawDebuffTransfer(
        ulong PlayerId,
        string InternalId,
        string DisplayName,
        double Amount);
    private static long RoundPositive(double value)
    {
        if (value <= 0d)
        {
            return 0;
        }

        if (value >= long.MaxValue)
        {
            return long.MaxValue;
        }

        return (long)Math.Floor(value + 0.5d);
    }
}

internal readonly record struct VulnerableDamageBreakdown(
    double FinalMultiplier,
    double VulnerableExtraDamage,
    double DebilitateExtraDamage,
    double AttackerConditionalExtraDamage)
{
    public double TransferableExtraDamage => VulnerableExtraDamage + DebilitateExtraDamage;

    public double TotalExtraDamage => TransferableExtraDamage + AttackerConditionalExtraDamage;
}

internal readonly record struct DamageComponents(
    long BlockedDamage,
    long UnblockedDamage,
    long OverkillDamage)
{
    public long Damage => BlockedDamage + UnblockedDamage + OverkillDamage;
}

internal enum DpsDebuffPowerKind
{
    Vulnerable,
    Debilitate,
    Flanking,
    Knockdown,
    Slow,
    Weak
}

internal sealed record DpsPlayerShareInput(
    ulong? PlayerId,
    decimal Fraction);

internal sealed record DpsDebuffPowerInput(
    DpsDebuffPowerKind Kind,
    double Amount,
    bool IsDealerApplied,
    IReadOnlyList<DpsPlayerShareInput> Shares);

internal sealed record DpsDebuffResolutionInput(
    double PaperPhrogExtra,
    double CrueltyExtra,
    IReadOnlyList<DpsDebuffPowerInput> Powers);

internal sealed record DebuffFactor(
    decimal Multiplier,
    double TransferableFraction,
    IReadOnlyList<DebuffFactorComponent> Components);

internal sealed record DebuffFactorComponent(
    string InternalId,
    string DisplayName,
    IReadOnlyList<DpsPlayerShareInput> Shares,
    double Weight);

internal sealed record DpsDebuffTransferAllocation(
    ulong PlayerId,
    string InternalId,
    string DisplayName,
    DamageComponents Components);
