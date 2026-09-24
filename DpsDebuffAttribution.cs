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

        var resolution = ResolveFactors(dealer!, target);
        if (resolution.Factors.Count == 0)
        {
            return Array.Empty<DebuffDamageTransfer>();
        }

        var allocations = DpsAttributionMath.CalculateDebuffTransfers(
            blockedDamage,
            unblockedDamage,
            overkillDamage,
            dealerPlayer.NetId,
            factor,
            resolution.Factors);
        var transfers = new List<DebuffDamageTransfer>();
        foreach (var allocation in allocations)
        {
            if (!resolution.PlayersByNetId.TryGetValue(allocation.PlayerId, out var player))
            {
                continue;
            }

            var components = allocation.Components;
            if (components.Damage <= 0)
            {
                continue;
            }

            transfers.Add(new DebuffDamageTransfer(
                player,
                CreateAttribution(allocation.InternalId, allocation.DisplayName, player),
                components.BlockedDamage,
                components.UnblockedDamage,
                components.OverkillDamage));
        }

        return transfers;
    }
    private static DebuffFactorResolution ResolveFactors(
        Creature dealer,
        Creature target)
    {
        var players = new Dictionary<ulong, Player>();
        var powers = new List<DpsDebuffPowerInput>();

        var vulnerable = target.GetPower<VulnerablePower>();
        if (vulnerable is not null)
        {
            powers.Add(CreatePowerInput(
                DpsDebuffPowerKind.Vulnerable,
                (double)vulnerable.DynamicVars["DamageIncrease"].BaseValue,
                false,
                vulnerable,
                players));
        }

        var debilitate = target.GetPower<DebilitatePower>();
        if (debilitate is not null)
        {
            powers.Add(CreatePowerInput(
                DpsDebuffPowerKind.Debilitate,
                1d,
                false,
                debilitate,
                players));
        }

        foreach (var flanking in target.GetPowerInstances<FlankingPower>())
        {
            powers.Add(CreatePowerInput(
                DpsDebuffPowerKind.Flanking,
                (double)flanking.Amount,
                ReferenceEquals(dealer, flanking.Applier),
                flanking,
                players));
        }

        foreach (var knockdown in target.GetPowerInstances<KnockdownPower>())
        {
            powers.Add(CreatePowerInput(
                DpsDebuffPowerKind.Knockdown,
                (double)knockdown.Amount,
                ReferenceEquals(dealer, knockdown.Applier),
                knockdown,
                players));
        }

        var slow = target.GetPower<SlowPower>();
        if (slow is not null)
        {
            powers.Add(CreatePowerInput(
                DpsDebuffPowerKind.Slow,
                (double)slow.DynamicVars["SlowAmount"].BaseValue,
                false,
                slow,
                players));
        }

        var weak = target.GetPower<WeakPower>();
        if (weak is not null)
        {
            // 虚弱不会提高敌方受到的伤害，显式进入解析输入但不生成助攻因子。
            powers.Add(CreatePowerInput(
                DpsDebuffPowerKind.Weak,
                (double)weak.Amount,
                false,
                weak,
                players));
        }

        var paperPhrogExtra = vulnerable is not null
            && dealer.Player?.GetRelic<PaperPhrog>() is not null
            ? 0.25d
            : 0d;
        CrueltyPower? cruelty = vulnerable is null
            ? null
            : dealer.GetPower<CrueltyPower>();
        var crueltyExtra = cruelty is null ? 0d : (double)cruelty.Amount / 100d;
        var factors = DpsAttributionMath.ResolveDebuffFactors(
            new DpsDebuffResolutionInput(
                paperPhrogExtra,
                crueltyExtra,
                powers));
        return new DebuffFactorResolution(factors, players);
    }

    private static DpsDebuffPowerInput CreatePowerInput(
        DpsDebuffPowerKind kind,
        double amount,
        bool isDealerApplied,
        PowerModel power,
        IDictionary<ulong, Player> players)
    {
        var shares = new List<DpsPlayerShareInput>();
        foreach (var share in DpsAttribution.GetPowerSharesWithFallback(power))
        {
            if (share.Player is not null)
            {
                players[share.Player.NetId] = share.Player;
            }

            shares.Add(new DpsPlayerShareInput(share.Player?.NetId, share.Fraction));
        }

        return new DpsDebuffPowerInput(kind, amount, isDealerApplied, shares);
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

    private sealed record DebuffFactorResolution(
        IReadOnlyList<DebuffFactor> Factors,
        IReadOnlyDictionary<ulong, Player> PlayersByNetId);

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
