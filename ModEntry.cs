using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2DpsMeter;

[ModInitializer(nameof(Init))]
public static class ModEntry
{
    public const string ModId = "Sts2DpsMeter";

    private static bool _initialized;

    public static void Init()
    {
        if (_initialized)
        {
            return;
        }

        if (!DpsSourceContext.Initialize())
        {
            GD.PushError($"[{ModId}] 初始化中止：伤害来源上下文不可用。");
            return;
        }

        _initialized = true;

        ModHelper.SubscribeForCombatStateHooks(
            ModId,
            _ => new[] { DpsHook.GetCanonical() });

        DpsLocalization.EnsureSubscribed();

        RunManager.Instance.RunStarted += OnRunStarted;
        CombatManager.Instance.CombatSetUp += OnCombatSetUp;
        CombatManager.Instance.CombatEnded += OnCombatEnded;

        GD.Print($"[{ModId}] 已初始化，点击 HUD 的齿轮（⚙）按钮可打开设置面板");
    }

    private static void OnRunStarted(RunState state)
    {
        DpsSourceContext.ClearEffectSources();
        DpsStats.ResetRun();
        DpsMeterUi.Refresh();
        GD.Print($"[{ModId}] 新对局开始");
    }

    private static void OnCombatSetUp(CombatState state)
    {
        DpsSourceContext.ClearEffectSources();
        DpsStats.BeginCombat(state.Players);
        DpsMeterUi.EnsureAttached();

        CombatManager.Instance.TurnStarted -= OnTurnStarted;
        CombatManager.Instance.TurnEnded -= OnTurnEnded;
        CombatManager.Instance.TurnStarted += OnTurnStarted;
        CombatManager.Instance.TurnEnded += OnTurnEnded;

        DpsMeterUi.Refresh();
        GD.Print($"[{ModId}] 战斗开始（第 {state.RoundNumber} 回合）");
    }

    private static void OnTurnStarted(CombatState state)
    {
        DpsStats.SetTurn(state.RoundNumber);
        DpsMeterUi.Refresh();
    }

    private static void OnTurnEnded(CombatState state)
    {
        DpsMeterUi.Refresh();
    }

    private static void OnCombatEnded(CombatRoom room)
    {
        CombatManager.Instance.TurnStarted -= OnTurnStarted;
        CombatManager.Instance.TurnEnded -= OnTurnEnded;

        DpsSourceContext.ClearEffectSources();
        DpsStats.EndCombat();
        DpsMeterUi.Refresh();
        DpsStats.PrintSummary();
        GD.Print($"[{ModId}] 战斗结束");
    }
}
