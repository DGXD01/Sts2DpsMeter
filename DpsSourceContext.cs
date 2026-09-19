using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Sts2DpsMeter;

public static class DpsSourceContext
{
    private const string HarmonyId = "DGXD01.Sts2DpsMeter";
    private static readonly object SyncRoot = new();
    private static readonly ConditionalWeakTable<PlayerChoiceContext, Stack<AbstractModel>> ModelStacks = new();
    private static readonly Stopwatch RecentTimer = Stopwatch.StartNew();
    private static AbstractModel? _recentModel;
    private static long _recentModelTimestamp;
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

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

            GD.Print($"[{ModEntry.ModId}] 已安装伤害来源上下文补丁");
        }
        catch (Exception exception)
        {
            GD.PushError($"[{ModEntry.ModId}] 安装伤害来源上下文补丁失败：{exception}");
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
