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
        DpsHudThemeStyles.Apply(_combatSummaryPanel, DpsMeterSettings.Current.HudTheme, DpsMeterSettings.Current.FontScale);
        _combatSummaryPanel.AddThemeStyleboxOverride("panel", DpsHudThemeStyles.Panel(HudPanelKind.Summary, Palette));
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
                // Godot 4.5 的自动换行 + 省略会返回 1px 最小高度，VBox 因而把整行压空。
                // 保留最多两行，以自然文本高度参与排版；长来源仍由文本层限制长度。
                TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            label.AddThemeColorOverride("font_color", index == 0 ? TitleColor : PrimaryText);
            label.AddThemeFontSizeOverride("font_size", FontSize(HudFontRole.Body));
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
        var width = Mathf.Min(DpsHudTypography.ContentWidth(420, settings.FontScale), Mathf.Max(1, (bounds.Size.X - 16) / scale));
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
