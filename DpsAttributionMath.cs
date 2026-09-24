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

internal readonly record struct DamageComponents(
    long BlockedDamage,
    long UnblockedDamage,
    long OverkillDamage)
{
    public long Damage => BlockedDamage + UnblockedDamage + OverkillDamage;
}
