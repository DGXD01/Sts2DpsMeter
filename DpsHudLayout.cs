using System;
using Godot;

namespace Sts2DpsMeter;

// 只计算逻辑尺寸，不访问场景树，便于覆盖不同分辨率与缩放组合。
internal static class DpsHudLayout
{
    internal const float PreferredWidth = 360f;
    private const float ScreenPadding = 24f;

    internal static HudPanelBounds Calculate(
        Vector2 viewport,
        Vector2 position,
        float uiScale,
        Vector2 chromeMinimum,
        float contentHeight,
        bool collapsed)
    {
        var logicalViewport = viewport / uiScale;
        var width = Math.Min(logicalViewport.X, Math.Max(PreferredWidth, chromeMinimum.X));
        var heightLimit = Math.Max(chromeMinimum.Y, (viewport.Y - ScreenPadding) / uiScale);
        var bodyHeight = collapsed ? 0f : Math.Clamp(contentHeight, 0f, heightLimit - chromeMinimum.Y);
        var size = new Vector2(width, chromeMinimum.Y + bodyHeight);
        var boundedPosition = new Vector2(
            Math.Clamp(position.X, 0f, Math.Max(0f, viewport.X - size.X * uiScale)),
            Math.Clamp(position.Y, 0f, Math.Max(0f, viewport.Y - size.Y * uiScale)));
        return new HudPanelBounds(size, boundedPosition, bodyHeight);
    }
}

internal readonly record struct HudPanelBounds(Vector2 Size, Vector2 Position, float BodyHeight);
