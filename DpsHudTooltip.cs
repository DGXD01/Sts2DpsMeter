using System;
using Godot;

namespace Sts2DpsMeter;

// 与 Godot 原生提示服务无关的停留计时；不可用、移出或按下鼠标即重新计时。
internal sealed class HudHoverDelay
{
    private const double DelaySeconds = 0.4;
    private double _elapsed;

    internal bool Update(bool eligible, double delta)
    {
        if (!eligible)
        {
            Reset();
            return false;
        }
        if (double.IsFinite(delta) && delta > 0) _elapsed += Math.Min(delta, 0.1);
        return _elapsed + 0.000001 >= DelaySeconds;
    }

    internal void Reset() => _elapsed = 0;
}

internal static class DpsHudTooltip
{
    internal static Vector2 Position(Rect2 anchor, Vector2 size, Rect2 viewport)
    {
        const float margin = 8;
        var minimum = viewport.Position + Vector2.One * margin;
        var maximum = (viewport.End - size - Vector2.One * margin).Max(minimum);
        var y = anchor.End.Y + margin;
        if (y + size.Y > viewport.End.Y - margin) y = anchor.Position.Y - size.Y - margin;
        return new Vector2(Math.Clamp(anchor.Position.X, minimum.X, maximum.X),
            Math.Clamp(y, minimum.Y, maximum.Y));
    }
}
