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

        _initialized = true;

        DpsSourceContext.Initialize();

        ModHelper.SubscribeForCombatStateHooks(
            ModId,
            _ => new[] { DpsHook.GetCanonical() });

        RunManager.Instance.RunStarted += OnRunStarted;
        CombatManager.Instance.CombatSetUp += OnCombatSetUp;
        CombatManager.Instance.CombatEnded += OnCombatEnded;

        GD.Print($"[{ModId}] 已初始化，点击 HUD 的“设”按钮可打开设置面板");
    }

    private static void OnRunStarted(RunState state)
    {
        DpsStats.ResetRun();
        DpsMeterUi.Refresh();
        GD.Print($"[{ModId}] 新对局开始");
    }

    private static void OnCombatSetUp(CombatState state)
    {
        DpsStats.BeginCombat();
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

        DpsStats.EndCombat();
        DpsMeterUi.Refresh();
        DpsStats.PrintSummary();
        GD.Print($"[{ModId}] 战斗结束");
    }
}
