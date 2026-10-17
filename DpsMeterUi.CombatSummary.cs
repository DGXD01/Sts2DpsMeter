using Godot;

namespace Sts2DpsMeter;

public static partial class DpsMeterUi
{
    private static readonly HudCombatSummaryState CombatSummary = new();
    private static PanelContainer? _combatSummaryPanel;
    private static readonly Label[] CombatSummaryLabels = new Label[5];
    private static CombatStatsSnapshot? _displayedSummary;
    private static string? _summaryLanguage;

    private static void BuildCombatSummary(CanvasLayer canvas)
    {
        CombatSummary.Dismiss();
        _displayedSummary = null;
        _combatSummaryPanel = new PanelContainer
        {
            Name = "Sts2DpsMeterCombatSummary",
            Visible = false,
            ZIndex = 10,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        DpsHudTypography.Apply(_combatSummaryPanel);
        _combatSummaryPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.035f, 0.045f, 0.07f, 0.97f),
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
            ContentMarginTop = 10,
            ContentMarginBottom = 10
        });
        canvas.AddChild(_combatSummaryPanel);
        var layout = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        layout.AddThemeConstantOverride("separation", 5);
        _combatSummaryPanel.AddChild(layout);
        for (var index = 0; index < CombatSummaryLabels.Length; index++)
        {
            var label = new Label
            {
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MaxLinesVisible = 2,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            label.AddThemeColorOverride("font_color", index == 0 ? TitleBlue : PrimaryText);
            label.AddThemeFontSizeOverride("font_size", DpsHudTypography.BodySize);
            layout.AddChild(label);
            CombatSummaryLabels[index] = label;
        }
    }

    private static void ProcessCombatSummary(double delta)
    {
        if (!IsAlive(_combatSummaryPanel) || !IsAlive(_panel)) return;
        var settings = DpsMeterSettings.Current;
        var status = DpsStats.GetCombatSummaryStatus();
        CombatSummary.Update(status.CombatId, status.IsActive, status.Completed,
            settings.ShowCombatSummary && _visible, settings.CombatSummaryDuration, delta);
        var snapshot = CombatSummary.Snapshot;
        if (snapshot is null || DpsMeterSettingsUi.IsVisible || !_panel!.IsVisibleInTree())
        {
            _combatSummaryPanel!.Visible = false;
            return;
        }
        if (!ReferenceEquals(snapshot, _displayedSummary) || _summaryLanguage != DpsLocalization.LanguageCode)
        {
            _displayedSummary = snapshot;
            _summaryLanguage = DpsLocalization.LanguageCode;
            var lines = DpsCombatSummary.Lines(DpsCombatSummary.Build(snapshot), DpsLocalization.IsChinese, LocalizeKnownValue);
            for (var index = 0; index < lines.Length; index++) CombatSummaryLabels[index].Text = lines[index];
        }
        // 独立于 HUD 的折叠和临时淡化。全层级忽略鼠标，不抢焦点或拦截奖励操作。
        var scale = _panel!.Scale.X;
        var bounds = _panel.GetCanvasTransform().AffineInverse() * _panel.GetViewport().GetVisibleRect();
        var width = Mathf.Min(420, Mathf.Max(1, (bounds.Size.X - 16) / scale));
        _combatSummaryPanel!.Scale = Vector2.One * scale;
        _combatSummaryPanel.CustomMinimumSize = new Vector2(width, 0);
        _combatSummaryPanel.Size = new Vector2(width, 0);
        var anchor = _panel.GetGlobalTransform() * new Rect2(Vector2.Zero, _panel.Size);
        _combatSummaryPanel.Position = DpsHudTooltip.Position(anchor, _combatSummaryPanel.Size * scale, bounds);
        _combatSummaryPanel.Visible = true;
    }

    private static void HideCombatSummary()
    {
        CombatSummary.Dismiss();
        _displayedSummary = null;
        if (IsAlive(_combatSummaryPanel)) _combatSummaryPanel!.Visible = false;
    }
}
