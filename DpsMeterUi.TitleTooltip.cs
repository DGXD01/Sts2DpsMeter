using Godot;

namespace Sts2DpsMeter;

public static partial class DpsMeterUi
{
    private static readonly HudHoverDelay HeaderHover = new();
    private static PanelContainer? _headerTooltipPanel;
    private static Label? _headerTooltipLabel;
    private static string? _headerTooltipText;

    private static void BuildHeaderTooltip(CanvasLayer canvas)
    {
        _headerTooltipText = null;
        _headerTooltipPanel = new PanelContainer
        {
            Name = "Sts2DpsMeterTitleTooltip",
            Visible = false,
            ZIndex = 100,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        DpsHudTypography.Apply(_headerTooltipPanel);
        _headerTooltipPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.035f, 0.045f, 0.07f, 0.98f),
            BorderColor = PanelBorder,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 8,
            ContentMarginBottom = 8
        });
        canvas.AddChild(_headerTooltipPanel);
        _headerTooltipLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _headerTooltipLabel.AddThemeColorOverride("font_color", PrimaryText);
        _headerTooltipLabel.AddThemeFontSizeOverride("font_size", DpsHudTypography.BodySize);
        _headerTooltipPanel.AddChild(_headerTooltipLabel);
    }

    private static void ProcessHeaderTooltip(double delta)
    {
        if (!IsAlive(_header) || !IsAlive(_panel) || !IsAlive(_headerTooltipPanel) ||
            !IsAlive(_headerTooltipLabel) || !_visible || !_panel!.IsVisibleInTree() ||
            _dragging || Input.GetMouseButtonMask() != 0 || !_panel.GetWindow().HasFocus() ||
            (IsAlive(_scopeOption) && _scopeOption!.GetPopup().Visible))
        {
            HideHeaderTooltip();
            return;
        }

        // 本地坐标自然包含 HUD 缩放；不依赖原生悬停目标或进入信号。
        // 标题外的按钮不在检测区域内，设置面板盖住标题时也不触发。
        var inside = new Rect2(Vector2.Zero, _header!.Size).HasPoint(_header.GetLocalMousePosition());
        if (!inside || DpsMeterSettingsUi.CoversMouse())
        {
            HideHeaderTooltip();
            return;
        }

        var text = HeaderTooltip.Enter();
        if (!HeaderHover.Update(true, delta) || string.IsNullOrEmpty(text)) return;

        // 一项一行，不再在提示中重复标题栏的省略；悬停期间沿用同一份完整摘要快照。
        if (_headerTooltipText != text)
        {
            _headerTooltipText = text;
            _headerTooltipLabel!.Text = text.Replace(" · ", "\n");
        }
        var scale = _panel.Scale.X;
        var bounds = _panel.GetCanvasTransform().AffineInverse() * _panel.GetViewport().GetVisibleRect();
        var width = Mathf.Min(400, Mathf.Max(1, (bounds.Size.X - 16) / scale));
        _headerTooltipPanel!.Scale = Vector2.One * scale;
        _headerTooltipLabel!.Size = new Vector2(Mathf.Max(1, width - 24), 0);
        _headerTooltipPanel.Size = new Vector2(width, 0);
        var anchor = _header.GetGlobalTransform() * new Rect2(Vector2.Zero, _header.Size);
        _headerTooltipPanel.Position = DpsHudTooltip.Position(anchor, _headerTooltipPanel.Size * scale, bounds);
        _headerTooltipPanel.Visible = true;
    }

    private static void HideHeaderTooltip()
    {
        HeaderHover.Reset();
        HeaderTooltip.Exit();
        if (IsAlive(_headerTooltipPanel)) _headerTooltipPanel!.Visible = false;
    }
}
