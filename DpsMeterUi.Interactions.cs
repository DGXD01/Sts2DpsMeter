using Godot;

namespace Sts2DpsMeter;

public static partial class DpsMeterUi
{
    private static readonly HudLowInterferenceState LowInterference = new();
    private static readonly HudOpacityTransition OpacityTransition = new();
    private static bool _combatActive;
    private static Panel? _snapPreview;
    private static Vector2 _dragMouseOrigin;
    private static Vector2 _dragPanelOrigin;
    private static bool _dragMoved;

    private static HudInterferencePresentation InteractionPresentation => LowInterference.Presentation(
        DpsMeterSettings.Current.LowInterferenceMode, _contentCollapsed, DpsMeterSettings.Current.LowInterferenceOpacity);

    private static void BuildSnapPreview(CanvasLayer canvas)
    {
        LowInterference.Reset();
        OpacityTransition.Reset();
        _dragging = false;
        _snapPreview = new Panel
        {
            Name = "Sts2DpsMeterSnapPreview",
            Visible = false,
            ZIndex = 5,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _snapPreview.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.1f, 0.4f, 0.8f, 0.08f),
            BorderColor = TitleBlue,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6
        });
        canvas.AddChild(_snapPreview);
    }

    private static void ProcessHudInteractions(double delta)
    {
        if (!IsAlive(_panel)) return;
        var settings = DpsMeterSettings.Current;
        var focused = _panel!.GetWindow().HasFocus();
        if (_dragging)
        {
            if (!_visible || !focused || settings.LockPosition) FinishHudDrag(cancel: true);
            else if (!Input.IsMouseButtonPressed(MouseButton.Left)) FinishHudDrag(cancel: false);
            else MoveHudDrag();
        }

        var wasActive = LowInterference.Active;
        var overHud = new Rect2(Vector2.Zero, _panel.Size).HasPoint(_panel.GetLocalMousePosition());
        var scopeOpen = IsAlive(_scopeOption) && _scopeOption!.GetPopup().Visible;
        LowInterference.Update(delta, _combatActive && _visible && focused && !_dragging &&
            !overHud && !DpsMeterSettingsUi.IsVisible && !scopeOpen && settings.LowInterferenceMode != HudLowInterferenceMode.Off);
        if (wasActive != LowInterference.Active) RequestImmediateRefresh();
    }

    private static void UpdateHudOpacity(double delta)
    {
        if (!IsAlive(_panel)) return;
        if (!_visible || !_panel!.IsVisibleInTree())
        {
            OpacityTransition.Reset();
            return;
        }
        var alpha = OpacityTransition.Update(InteractionPresentation.Alpha, delta);
        if (_panel.Modulate.A != alpha) _panel.Modulate = new Color(1, 1, 1, alpha);
    }

    private static void BeginHudDrag()
    {
        if (DpsMeterSettings.Current.LockPosition || !IsAlive(_headerRow) ||
            !new Rect2(Vector2.Zero, _headerRow!.Size).HasPoint(_headerRow.GetLocalMousePosition())) return;
        _dragging = true;
        _dragMoved = false;
        _dragMouseOrigin = _panel!.GetGlobalMousePosition();
        _dragPanelOrigin = _panel.Position;
        LowInterference.Reset();
        HideHeaderTooltip();
        Refresh();
    }

    private static void MoveHudDrag()
    {
        var delta = _panel!.GetGlobalMousePosition() - _dragMouseOrigin;
        _dragMoved |= delta.LengthSquared() >= 9;
        if (!_dragMoved) return;
        // 采用同一 CanvasLayer 的全局坐标差，避免 UiScale 改变拖动距离。
        _panel.Position = DpsHudPlacement.Restore(_dragPanelOrigin + delta, _panel.Size * _panel.Scale,
            GetViewportSize(), 0, HudSnapAnchor.None);
        if (!IsAlive(_snapPreview)) return;
        var target = GetDropTarget();
        _snapPreview!.Visible = target.Anchor != HudSnapAnchor.None;
        _snapPreview.Position = target.Position;
        _snapPreview.Size = _panel.Size;
        _snapPreview.Scale = _panel.Scale;
    }

    private static HudSnapResult GetDropTarget()
    {
        var settings = DpsMeterSettings.Current;
        return DpsHudPlacement.Drop(_panel!.Position, _panel.Size * _panel.Scale, GetViewportSize(),
            settings.SnapEnabled, settings.SnapDistance, settings.ScreenMargin);
    }

    private static void FinishHudDrag(bool cancel)
    {
        if (!_dragging) return;
        if (IsAlive(_snapPreview)) _snapPreview!.Visible = false;
        if (!cancel) MoveHudDrag();
        if (IsAlive(_snapPreview)) _snapPreview!.Visible = false;
        _dragging = false;
        LowInterference.Reset();
        if (!cancel && _dragMoved)
        {
            var target = GetDropTarget();
            _panel!.Position = target.Position;
            var settings = DpsMeterSettings.Current;
            settings.PositionX = target.Position.X;
            settings.PositionY = target.Position.Y;
            settings.SnapAnchor = target.Anchor;
            DpsMeterSettings.Save();
            DpsMeterSettingsUi.Refresh();
        }
        else _panel!.Position = _dragPanelOrigin;
        Refresh();
    }
}
