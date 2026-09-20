using System;
using System.Threading;
using Godot;

namespace Sts2DpsMeter;

public static class DpsMeterUi
{
    private static CanvasLayer? _canvas;
    private static DpsMeterHost? _host;
    private static int _immediateRefreshRequested;
    private static int _deferredRefreshQueued;

    public static void EnsureAttached()
    {
        if (IsAlive(_canvas) && IsAlive(_host))
        {
            DpsMeterModuleUi.EnsureAttached(_canvas!);
            DpsMeterSettingsUi.EnsureAttached(_canvas!);
            Refresh();
            return;
        }

        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            var root = tree?.Root;
            if (root is null)
            {
                return;
            }

            if (!IsAlive(_host))
            {
                _host = new DpsMeterHost
                {
                    Name = "Sts2DpsMeterHost",
                    ProcessMode = Node.ProcessModeEnum.Always
                };
                root.AddChild(_host);
                _host.SetProcess(true);
            }

            if (!IsAlive(_canvas))
            {
                _canvas = new CanvasLayer
                {
                    Name = "Sts2DpsMeterCanvas",
                    Layer = 120
                };
                root.AddChild(_canvas);
            }

            DpsMeterModuleUi.EnsureAttached(_canvas!);
            DpsMeterSettingsUi.EnsureAttached(_canvas!);
            Refresh();
        }
        catch (Exception exception)
        {
            GD.PushError($"[{ModEntry.ModId}] 创建统计面板失败：{exception}");
        }
    }

    public static void Refresh()
    {
        if (IsAlive(_canvas))
        {
            DpsMeterModuleUi.Refresh(_canvas!);
        }
    }

    public static void RequestImmediateRefresh()
    {
        // 伤害 Hook 由游戏主线程同步调用时，直接刷新，避免等待下一帧或回合结束。
        if (IsMainThread())
        {
            Interlocked.Exchange(ref _immediateRefreshRequested, 0);
            Refresh();
            return;
        }

        // 后台线程不能直接操作 Godot 控件，统一投递到主线程。
        Interlocked.Exchange(ref _immediateRefreshRequested, 1);
        QueueDeferredRefresh();
    }

    internal static bool ConsumeImmediateRefreshRequest()
    {
        return Interlocked.Exchange(ref _immediateRefreshRequested, 0) == 1;
    }

    private static void FlushDeferredRefresh()
    {
        Interlocked.Exchange(ref _deferredRefreshQueued, 0);
        if (ConsumeImmediateRefreshRequest())
        {
            Refresh();
        }
    }

    private static bool IsMainThread()
    {
        try
        {
            return OS.GetThreadCallerId() == OS.GetMainThreadId();
        }
        catch
        {
            // 无法判断线程时保守地走延迟队列，避免在后台线程直接访问控件。
            return false;
        }
    }

    private static void QueueDeferredRefresh()
    {
        if (!IsAlive(_host) || Interlocked.Exchange(ref _deferredRefreshQueued, 1) == 1)
        {
            return;
        }

        try
        {
            Callable.From(FlushDeferredRefresh).CallDeferred();
        }
        catch
        {
            Interlocked.Exchange(ref _deferredRefreshQueued, 0);
        }
    }

    private static bool IsAlive(GodotObject? value)
    {
        return value is not null && GodotObject.IsInstanceValid(value);
    }
}

internal sealed class DpsMeterHost : Node
{
    private double _refreshTimer;

    public override void _Process(double delta)
    {
        DpsStats.Tick(delta);

        var immediateRefresh = DpsMeterUi.ConsumeImmediateRefreshRequest();
        _refreshTimer -= delta;
        if (immediateRefresh || _refreshTimer <= 0)
        {
            _refreshTimer = 0.25;
            DpsMeterUi.Refresh();
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (DpsMeterModuleUi.HandleGlobalInput(@event))
        {
            GetViewport().SetInputAsHandled();
        }
    }
}
