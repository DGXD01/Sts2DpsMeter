using System;
using Godot;

namespace Sts2DpsMeter;

[Flags]
public enum HudSnapAnchor
{
    None = 0,
    Left = 1,
    Right = 2,
    Top = 4,
    Bottom = 8
}

public enum HudLowInterferenceMode
{
    Off,
    Fade,
    AutoCollapse
}

internal readonly record struct HudSnapResult(Vector2 Position, HudSnapAnchor Anchor);

// 输入尺寸均为视口坐标；调用方传入已经乘以 UiScale 的面板尺寸。
internal static class DpsHudPlacement
{
    internal static HudSnapAnchor NormalizeAnchor(HudSnapAnchor anchor)
    {
        if (((int)anchor & ~15) != 0 ||
            (anchor.HasFlag(HudSnapAnchor.Left) && anchor.HasFlag(HudSnapAnchor.Right)) ||
            (anchor.HasFlag(HudSnapAnchor.Top) && anchor.HasFlag(HudSnapAnchor.Bottom))) return HudSnapAnchor.None;
        return anchor;
    }

    private static (float Min, float Max) Limits(float viewport, float size, float margin)
    {
        var available = MathF.Max(0, viewport - size);
        var padding = MathF.Min(MathF.Max(0, margin), available / 2);
        var min = MathF.Ceiling(padding);
        var max = MathF.Floor(available - padding);
        if (max < min) min = max = MathF.Floor(available / 2);
        return (min, max);
    }

    internal static Vector2 Restore(Vector2 position, Vector2 size, Vector2 viewport, float margin, HudSnapAnchor anchor)
    {
        var x = Limits(viewport.X, size.X, margin);
        var y = Limits(viewport.Y, size.Y, margin);
        anchor = NormalizeAnchor(anchor);
        return new Vector2(
            anchor.HasFlag(HudSnapAnchor.Left) ? x.Min : anchor.HasFlag(HudSnapAnchor.Right) ? x.Max : Math.Clamp(MathF.Round(position.X), x.Min, x.Max),
            anchor.HasFlag(HudSnapAnchor.Top) ? y.Min : anchor.HasFlag(HudSnapAnchor.Bottom) ? y.Max : Math.Clamp(MathF.Round(position.Y), y.Min, y.Max));
    }

    internal static HudSnapResult Drop(Vector2 position, Vector2 size, Vector2 viewport, bool enabled, float distance, float margin)
    {
        var bounded = Restore(position, size, viewport, enabled ? margin : 0, HudSnapAnchor.None);
        if (!enabled) return new HudSnapResult(bounded, HudSnapAnchor.None);
        var x = Limits(viewport.X, size.X, margin);
        var y = Limits(viewport.Y, size.Y, margin);
        var anchor = HudSnapAnchor.None;
        if (MathF.Min(MathF.Abs(bounded.X - x.Min), MathF.Abs(bounded.X - x.Max)) <= distance)
            anchor |= MathF.Abs(bounded.X - x.Min) <= MathF.Abs(bounded.X - x.Max) ? HudSnapAnchor.Left : HudSnapAnchor.Right;
        if (MathF.Min(MathF.Abs(bounded.Y - y.Min), MathF.Abs(bounded.Y - y.Max)) <= distance)
            anchor |= MathF.Abs(bounded.Y - y.Min) <= MathF.Abs(bounded.Y - y.Max) ? HudSnapAnchor.Top : HudSnapAnchor.Bottom;
        return new HudSnapResult(Restore(bounded, size, viewport, margin, anchor), anchor);
    }
}

internal readonly record struct HudInterferencePresentation(float Alpha, bool Collapsed);

// 每帧推进，不受统计面板的 0.25 秒轮询影响；反向时从当前值开始。
internal sealed class HudOpacityTransition
{
    internal const double DurationSeconds = 0.18;
    private float _start = 1;
    private float _target = 1;
    private double _elapsed = DurationSeconds;
    internal float Current { get; private set; } = 1;

    internal float Update(float target, double delta)
    {
        target = float.IsFinite(target) ? Math.Clamp(target, 0, 1) : 1;
        if (target != _target)
        {
            _start = Current;
            _target = target;
            _elapsed = 0;
        }
        if (double.IsFinite(delta) && delta > 0) _elapsed = Math.Min(DurationSeconds, _elapsed + delta);
        var t = _elapsed / DurationSeconds;
        var eased = (float)(t * t * (3 - 2 * t));
        Current = t >= 1 ? _target : _start + (_target - _start) * eased;
        return Current;
    }

    internal void Reset()
    {
        Current = _start = _target = 1;
        _elapsed = DurationSeconds;
    }
}

internal sealed class HudLowInterferenceState
{
    private double _awaySeconds;
    internal bool Active { get; private set; }

    internal void Update(double delta, bool eligible)
    {
        if (!eligible)
        {
            Reset();
            return;
        }
        if (double.IsFinite(delta) && delta > 0) _awaySeconds = Math.Min(1, _awaySeconds + Math.Min(delta, 0.1));
        Active = _awaySeconds + 0.000001 >= 1;
    }

    internal HudInterferencePresentation Presentation(HudLowInterferenceMode mode, bool manualCollapsed, float opacity) =>
        new(Active && mode == HudLowInterferenceMode.Fade ? opacity : 1,
            manualCollapsed || (Active && mode == HudLowInterferenceMode.AutoCollapse));

    internal void Reset()
    {
        _awaySeconds = 0;
        Active = false;
    }
}
