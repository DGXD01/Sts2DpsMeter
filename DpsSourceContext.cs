using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Sts2DpsMeter;

public static class DpsSourceContext
{
    private const string HarmonyId = "DGXD01.Sts2DpsMeter";
    private static readonly object SyncRoot = new();
    private static readonly ConditionalWeakTable<PlayerChoiceContext, Stack<AbstractModel>> ModelStacks = new();
    private static readonly Stopwatch RecentTimer = Stopwatch.StartNew();
    private static AbstractModel? _recentModel;
    private static long _recentModelTimestamp;
    private static OrbModel? _recentOrb;
    private static long _recentOrbTimestamp;
    private static bool _initialized;
    private static bool _initializationFailed;

    public static bool Initialize()
    {
        if (_initialized)
        {
            return true;
        }

        if (_initializationFailed)
        {
            return false;
        }

        try
        {
            var harmony = new Harmony(HarmonyId);
            var pushModel = AccessTools.Method(
                typeof(PlayerChoiceContext),
                nameof(PlayerChoiceContext.PushModel));
            var popModel = AccessTools.Method(
                typeof(PlayerChoiceContext),
                nameof(PlayerChoiceContext.PopModel));
            var pushPostfix = AccessTools.Method(typeof(DpsSourceContext), nameof(PushModelPostfix));
            var popPostfix = AccessTools.Method(typeof(DpsSourceContext), nameof(PopModelPostfix));
            var powerFlashPostfix = AccessTools.Method(typeof(DpsSourceContext), nameof(PowerFlashPostfix));
            var relicFlashPostfix = AccessTools.Method(typeof(DpsSourceContext), nameof(RelicFlashPostfix));
            var orbTriggerPostfix = AccessTools.Method(typeof(DpsSourceContext), nameof(OrbTriggerPostfix));
            var doomKillPrefix = AccessTools.Method(typeof(DpsSourceContext), nameof(DoomKillPrefix));
            var energyGainPostfix = AccessTools.Method(typeof(DpsSourceContext), nameof(EnergyGainPostfix));
            var healPrefix = AccessTools.Method(typeof(DpsSourceContext), nameof(HealPrefix));
            var healPostfix = AccessTools.Method(typeof(DpsSourceContext), nameof(HealPostfix));
            var doomKillMethod = AccessTools.Method(
                typeof(DoomPower),
                nameof(DoomPower.DoomKill),
                new[] { typeof(IReadOnlyList<Creature>) });

            PatchRequired(harmony, pushModel, pushPostfix, "PlayerChoiceContext.PushModel");
            PatchRequired(harmony, popModel, popPostfix, "PlayerChoiceContext.PopModel");
            PatchOptional(
                harmony,
                AccessTools.Method(typeof(PowerModel), "Flash", Type.EmptyTypes),
                powerFlashPostfix,
                "PowerModel.Flash");
            PatchOptional(
                harmony,
                AccessTools.Method(
                    typeof(RelicModel),
                    "Flash",
                    new[] { typeof(IEnumerable<Creature>) }),
                relicFlashPostfix,
                "RelicModel.Flash(IEnumerable<Creature>)");
            PatchOptional(
                harmony,
                AccessTools.Method(typeof(OrbModel), nameof(OrbModel.Trigger), Type.EmptyTypes),
                orbTriggerPostfix,
                "OrbModel.Trigger");
            PatchOptional(
                harmony,
                AccessTools.Method(
                    typeof(PlayerCombatState),
                    nameof(PlayerCombatState.GainEnergy),
                    new[] { typeof(decimal) }),
                energyGainPostfix,
                "PlayerCombatState.GainEnergy");
            PatchOptional(
                harmony,
                AccessTools.Method(
                    typeof(Creature),
                    nameof(Creature.HealInternal),
                    new[] { typeof(decimal) }),
                healPrefix,
                healPostfix,
                "Creature.HealInternal");
            PatchRequiredPrefix(
                harmony,
                doomKillMethod,
                doomKillPrefix,
                "DoomPower.DoomKill");

            _initialized = true;
            GD.Print($"[{ModEntry.ModId}] 已安装伤害来源上下文补丁");
            return true;
        }
        catch (Exception exception)
        {
            _initializationFailed = true;
            GD.PushError($"[{ModEntry.ModId}] 安装伤害来源上下文补丁失败，已停止 Mod 初始化：{exception}");
            return false;
        }
    }

    public static IReadOnlyList<AbstractModel> GetModelChain(PlayerChoiceContext context)
    {
        if (context is null)
        {
            return Array.Empty<AbstractModel>();
        }

        lock (SyncRoot)
        {
            if (ModelStacks.TryGetValue(context, out var stack) && stack.Count > 0)
            {
                return stack
                    .Where(model => model is not DpsHook)
                    .ToArray();
            }
        }

        try
        {
            var lastModel = context.LastInvolvedModel;
            if (lastModel is not null && lastModel is not DpsHook)
            {
                return new[] { lastModel };
            }
        }
        catch
        {
            // 某些临时上下文可能没有可读取的模型栈，不影响伤害统计。
        }

        return Array.Empty<AbstractModel>();
    }

    public static Player? ResolveSourcePlayer(
        PlayerChoiceContext? context,
        Creature? target,
        AbstractModel? explicitModel = null)
    {
        try
        {
            var explicitSource = ResolveModelSource(explicitModel);
            if (explicitSource is not null)
            {
                return explicitSource;
            }

            if (context is not null)
            {
                foreach (var model in GetModelChain(context))
                {
                    var source = ResolveModelSource(model);
                    if (source is not null)
                    {
                        return source;
                    }
                }
            }

            var recentSource = ResolveModelSource(GetRecentModel<AbstractModel>());
            if (recentSource is not null)
            {
                return recentSource;
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"[{ModEntry.ModId}] 解析队友增益来源失败：{exception}");
        }

        return null;
    }

    public static void RecordRecentModel(AbstractModel model)
    {
        lock (SyncRoot)
        {
            _recentModel = model;
            _recentModelTimestamp = RecentTimer.ElapsedTicks;
        }
    }

    public static T? GetRecentModel<T>() where T : AbstractModel
    {
        lock (SyncRoot)
        {
            if (_recentModel is not T typed)
            {
                return null;
            }

            var age = TimeSpan.FromTicks(RecentTimer.ElapsedTicks - _recentModelTimestamp);
            return age <= TimeSpan.FromMilliseconds(500) ? typed : null;
        }
    }

    public static void RecordRecentOrb(OrbModel orb)
    {
        lock (SyncRoot)
        {
            _recentOrb = orb;
            _recentOrbTimestamp = RecentTimer.ElapsedTicks;
        }
    }

    public static OrbModel? GetRecentOrb()
    {
        lock (SyncRoot)
        {
            if (_recentOrb is null)
            {
                return null;
            }

            var age = TimeSpan.FromTicks(RecentTimer.ElapsedTicks - _recentOrbTimestamp);
            return age <= TimeSpan.FromMilliseconds(500) ? _recentOrb : null;
        }
    }

    private static void PushModelPostfix(PlayerChoiceContext __instance, AbstractModel model)
    {
        Push(__instance, model);
    }

    private static void PopModelPostfix(PlayerChoiceContext __instance, AbstractModel model)
    {
        Pop(__instance, model);
    }

    private static void PowerFlashPostfix(PowerModel __instance)
    {
        RecordRecentModel(__instance);
    }

    private static void RelicFlashPostfix(RelicModel __instance)
    {
        RecordRecentModel(__instance);
    }

    private static void OrbTriggerPostfix(OrbModel __instance)
    {
        RecordRecentOrb(__instance);
    }

    private static void EnergyGainPostfix(PlayerCombatState __instance, decimal amount)
    {
        try
        {
            var target = GetPlayerForCombatState(__instance);
            if (target is null || amount <= 0)
            {
                return;
            }

            var source = ResolveSourcePlayer(null, target.Creature);
            if (DpsStats.RecordAllySupport(
                    source,
                    target,
                    AllySupportKind.EnergyGiven,
                    amount))
            {
                DpsMeterUi.RequestImmediateRefresh();
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"[{ModEntry.ModId}] 记录队友能量失败：{exception}");
        }
    }

    private static void HealPrefix(Creature __instance, out int __state)
    {
        __state = __instance.CurrentHp;
    }

    private static void HealPostfix(Creature __instance, int __state)
    {
        try
        {
            var healed = __instance.CurrentHp - __state;
            if (healed <= 0 || !__instance.IsPlayer || __instance.Player is null)
            {
                return;
            }

            var source = ResolveSourcePlayer(null, __instance);
            if (DpsStats.RecordAllySupport(
                    source,
                    __instance.Player,
                    AllySupportKind.HealingGiven,
                    healed))
            {
                DpsMeterUi.RequestImmediateRefresh();
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"[{ModEntry.ModId}] 记录队友回血失败：{exception}");
        }
    }

    private static void DoomKillPrefix(IReadOnlyList<Creature> creatures)
    {
        try
        {
            DpsStats.CaptureDoomKills(creatures);
        }
        catch (Exception exception)
        {
            GD.PushError($"[{ModEntry.ModId}] 捕获灾厄击杀失败：{exception}");
        }
    }

    private static void PatchRequired(
        Harmony harmony,
        MethodBase? original,
        MethodInfo? postfix,
        string description)
    {
        if (original is null || postfix is null)
        {
            throw new InvalidOperationException($"找不到 {description} 补丁目标或回调。");
        }

        harmony.Patch(original, postfix: new HarmonyMethod(postfix));
    }

    private static void PatchRequiredPrefix(
        Harmony harmony,
        MethodBase? original,
        MethodInfo? prefix,
        string description)
    {
        if (original is null || prefix is null)
        {
            throw new InvalidOperationException($"找不到 {description} 补丁目标或回调。");
        }

        harmony.Patch(original, prefix: new HarmonyMethod(prefix));
    }

    private static void PatchOptional(
        Harmony harmony,
        MethodBase? original,
        MethodInfo? postfix,
        string description)
    {
        if (original is null || postfix is null)
        {
            GD.PushWarning($"[{ModEntry.ModId}] 跳过可选补丁：{description} 不存在。");
            return;
        }

        try
        {
            harmony.Patch(original, postfix: new HarmonyMethod(postfix));
        }
        catch (Exception exception)
        {
            GD.PushWarning($"[{ModEntry.ModId}] 安装可选补丁失败：{description}，{exception.Message}");
        }
    }

    private static void PatchOptional(
        Harmony harmony,
        MethodBase? original,
        MethodInfo? prefix,
        MethodInfo? postfix,
        string description)
    {
        if (original is null || prefix is null || postfix is null)
        {
            GD.PushWarning($"[{ModEntry.ModId}] 跳过可选补丁：{description} 不存在。");
            return;
        }

        try
        {
            harmony.Patch(
                original,
                prefix: new HarmonyMethod(prefix),
                postfix: new HarmonyMethod(postfix));
        }
        catch (Exception exception)
        {
            GD.PushWarning($"[{ModEntry.ModId}] 安装可选补丁失败：{description}，{exception.Message}");
        }
    }

    private static Player? ResolveModelSource(AbstractModel? model)
    {
        if (model is null)
        {
            return null;
        }

        try
        {
            return model switch
            {
                CardModel card => card.Owner,
                PotionModel potion => potion.Owner,
                RelicModel relic => relic.Owner,
                OrbModel orb => orb.Owner,
                PowerModel power => GetPlayerForCreature(power.Applier),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static Player? GetPlayerForCombatState(PlayerCombatState state)
    {
        try
        {
            return AccessTools.Field(typeof(PlayerCombatState), "_player")?.GetValue(state) as Player;
        }
        catch
        {
            return null;
        }
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

    private static void Push(PlayerChoiceContext context, AbstractModel model)
    {
        lock (SyncRoot)
        {
            var stack = ModelStacks.GetValue(context, _ => new Stack<AbstractModel>());
            stack.Push(model);
        }

        RecordRecentModel(model);
    }

    private static void Pop(PlayerChoiceContext context, AbstractModel model)
    {
        lock (SyncRoot)
        {
            if (!ModelStacks.TryGetValue(context, out var stack) || stack.Count == 0)
            {
                return;
            }

            if (ReferenceEquals(stack.Peek(), model))
            {
                stack.Pop();
                return;
            }

            var remaining = new List<AbstractModel>(stack.Count);
            while (stack.Count > 0)
            {
                var candidate = stack.Pop();
                if (!ReferenceEquals(candidate, model))
                {
                    remaining.Add(candidate);
                }
            }

            for (var index = remaining.Count - 1; index >= 0; index--)
            {
                stack.Push(remaining[index]);
            }
        }
    }
}
