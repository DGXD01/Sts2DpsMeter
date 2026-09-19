using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Godot;

namespace Sts2DpsMeter;

public static class DpsMeterUi
{
    private const float MinimumPanelWidth = 360f;
    private const float MinimumPanelHeight = 1f;
    private const int ScopeCurrent = 0;
    private const int ScopeTurn = 1;
    private const int ScopeTaken = 2;
    private const int ScopeTotal = 3;
    private const int ScopeBoth = 4;

    private static readonly Color PanelBackground = new(0f, 0f, 0f, 0.92f);
    private static readonly Color PanelBorder = new(0.92f, 0.95f, 1f, 0.88f);
    private static readonly Color TitleBlue = new(0.2f, 0.6f, 1f);
    private static readonly Color ArrowBlue = new(0.18f, 0.54f, 1f);
    private static readonly Color BarBackground = new(0.11f, 0.12f, 0.145f, 0.96f);
    private static readonly Color BarFill = new(0.82f, 0.08f, 0.1f, 1f);
    private static readonly Color DropdownBackground = new(0.06f, 0.065f, 0.08f, 0.98f);
    private static readonly Color PrimaryText = new(0.98f, 0.99f, 1f);
    private static readonly Color MutedText = new(0.68f, 0.72f, 0.78f);

    private static CanvasLayer? _canvas;
    private static PanelContainer? _panel;
    private static StyleBoxFlat? _panelStyle;
    private static GridContainer? _moduleGrid;
    private static Label? _header;
    private static Button? _previousPlayerButton;
    private static Button? _nextPlayerButton;
    private static OptionButton? _scopeOption;
    private static Button? _collapseButton;
    private static Control? _contentDivider;
    private static DpsMeterHost? _host;
    private static bool _visible = true;
    private static bool _visibilityInitialized;
    private static bool _dragging;
    private static bool _contentCollapsed;
    private static int _selectedPlayerIndex;
    private static int _scopeIndex = ScopeCurrent;
    private static int _immediateRefreshRequested;
    private static int _deferredRefreshQueued;
    private static int _deferredPanelFitQueued;

    public static bool IsVisible => _visible;

    public static void EnsureAttached()
    {
        if (IsAlive(_canvas) && IsAlive(_host))
        {
            DpsMeterSettingsUi.EnsureAttached(_canvas!);
            _canvas!.Visible = true;
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
                BuildPanel(_canvas);
            }

            DpsMeterSettingsUi.EnsureAttached(_canvas!);
            if (!_visibilityInitialized)
            {
                _visible = DpsMeterSettings.Current.StartVisible;
                _visibilityInitialized = true;
            }

            _canvas!.Visible = true;
            Refresh();
        }
        catch (Exception exception)
        {
            GD.PushError($"[{ModEntry.ModId}] 创建统计面板失败：{exception}");
        }
    }

    public static void Refresh()
    {
        if (!IsAlive(_panel) ||
            !IsAlive(_moduleGrid) ||
            !IsAlive(_header) ||
            !IsAlive(_scopeOption) ||
            !IsAlive(_collapseButton) ||
            !IsAlive(_contentDivider))
        {
            return;
        }

        var settings = DpsMeterSettings.Current;
        var snapshot = DpsStats.GetSnapshot();

        _panel!.Visible = _visible;
        if (!_dragging)
        {
            _panel.Position = new Vector2(settings.PositionX, settings.PositionY);
        }

        _panel.Scale = new Vector2(settings.UiScale, settings.UiScale);
        _panel.PivotOffset = Vector2.Zero;
        ApplyPanelStyle(settings);
        NormalizeScopeSelection(settings);
        _contentDivider!.Visible = !_contentCollapsed;
        _moduleGrid!.Visible = !_contentCollapsed;
        UpdateCollapseButton();

        var view = BuildDamageView(settings, snapshot);
        _header!.Text = view.Title;
        UpdateNavigationState(snapshot.Players.Count);

        _moduleGrid!.Columns = 1;
        if (_contentCollapsed)
        {
            EnsurePanelSize(settings);
            QueueDeferredPanelFit();
            return;
        }

        ClearChildren(_moduleGrid);

        if (view.Sections.Count == 0)
        {
            AddDamageRow(settings, "暂无记录", 0, 0d);
        }
        else
        {
            foreach (var section in view.Sections)
            {
                if (!string.IsNullOrWhiteSpace(section.Heading))
                {
                    AddDamageSectionHeader(section.Heading);
                }

                var rows = section.Rows
                    .Where(row => row.Damage > 0)
                    .OrderByDescending(row => row.Damage)
                    .ThenBy(row => row.Label, StringComparer.Ordinal)
                    .Take(settings.MaxRows)
                    .ToArray();

                if (rows.Length == 0)
                {
                    AddDamageRow(settings, "暂无记录", 0, 0d);
                    continue;
                }

                foreach (var row in rows)
                {
                    var percentage = section.Total > 0
                        ? Math.Clamp(row.Damage / (double)section.Total * 100d, 0d, 100d)
                        : 0d;
                    AddDamageRow(settings, row.Label, row.Damage, percentage);
                }
            }
        }

        EnsurePanelSize(settings);
        QueueDeferredPanelFit();
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

    internal static void FlushDeferredRefresh()
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

    internal static void FlushDeferredPanelFit()
    {
        Interlocked.Exchange(ref _deferredPanelFitQueued, 0);
        FitPanelToContent(DpsMeterSettings.Current);
    }

    public static void ToggleVisibility()
    {
        if (!IsAlive(_canvas))
        {
            return;
        }

        _visible = !_visible;
        _panel!.Visible = _visible;
        GD.Print($"[{ModEntry.ModId}] 统计面板：{(_visible ? "显示" : "隐藏")}");
    }

    public static void ShowSettings()
    {
        DpsMeterSettingsUi.ToggleVisibility();
    }

    private static DamageViewData BuildDamageView(
        DpsMeterSettingsData settings,
        CombatStatsSnapshot snapshot)
    {
        if (snapshot.Players.Count == 0)
        {
            return new DamageViewData("伤害：0", Array.Empty<DamageSectionData>());
        }

        _selectedPlayerIndex = Math.Clamp(_selectedPlayerIndex, 0, snapshot.Players.Count - 1);
        var player = snapshot.Players[_selectedPlayerIndex];

        if (_scopeIndex == ScopeBoth)
        {
            var dealtRows = BuildCurrentRows(player);
            var takenRows = BuildTakenRows(player);
            var dealtTotal = player.TotalDamage;
            var takenTotal = player.IncomingDamage + player.SelfLossDamage;
            var sections = new[]
            {
                new DamageSectionData("造成伤害", dealtTotal, dealtRows),
                new DamageSectionData("受到伤害", takenTotal, takenRows)
            };
            return new DamageViewData($"造成：{dealtTotal} / 承伤：{takenTotal}", sections);
        }

        var rows = _scopeIndex switch
        {
            ScopeTurn => BuildTurnRows(player),
            ScopeTaken => BuildTakenRows(player),
            ScopeTotal => BuildTotalRows(snapshot),
            _ => BuildCurrentRows(player)
        };

        var total = _scopeIndex switch
        {
            ScopeTurn => rows.Sum(row => row.Damage),
            ScopeTaken => player.IncomingDamage + player.SelfLossDamage,
            ScopeTotal => snapshot.Players.Sum(item => item.TotalDamage),
            _ => player.TotalDamage
        };

        var title = _scopeIndex == ScopeTaken ? $"受到：{total}" : $"伤害：{total}";
        return new DamageViewData(
            title,
            new[] { new DamageSectionData(null, total, rows) });
    }

    private static IReadOnlyList<DamageRowData> BuildCurrentRows(PlayerStatsSnapshot player)
    {
        return player.Sources
            .Select(source => new DamageRowData(source.Source, source.Damage))
            .ToArray();
    }

    private static IReadOnlyList<DamageRowData> BuildTurnRows(PlayerStatsSnapshot player)
    {
        var currentTurn = player.Turns
            .OrderByDescending(turn => turn.Turn)
            .FirstOrDefault();

        if (currentTurn is null)
        {
            return Array.Empty<DamageRowData>();
        }

        return currentTurn.Cards
            .GroupBy(card => card.DisplayName, StringComparer.Ordinal)
            .Select(group => new DamageRowData(group.Key, group.Sum(card => card.Damage)))
            .ToArray();
    }

    private static IReadOnlyList<DamageRowData> BuildTakenRows(PlayerStatsSnapshot player)
    {
        return player.IncomingSources
            .Concat(player.SelfLossSources)
            .GroupBy(source => source.Source, StringComparer.Ordinal)
            .Select(group => new DamageRowData(group.Key, group.Sum(source => source.Damage)))
            .ToArray();
    }

    private static IReadOnlyList<DamageRowData> BuildTotalRows(CombatStatsSnapshot snapshot)
    {
        return snapshot.Players
            .SelectMany(player => player.Sources)
            .GroupBy(source => source.Source, StringComparer.Ordinal)
            .Select(group => new DamageRowData(group.Key, group.Sum(source => source.Damage)))
            .ToArray();
    }

    private static void AddDamageRow(
        DpsMeterSettingsData settings,
        string labelText,
        long damage,
        double percentage)
    {
        var rowHeight = settings.CompactMode ? 24f : 28f;
        var row = new Control
        {
            CustomMinimumSize = new Vector2(0, rowHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };

        var progress = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = percentage,
            ShowPercentage = false,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        progress.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        progress.AddThemeStyleboxOverride("background", CreateBarStyle(BarBackground, 5));
        progress.AddThemeStyleboxOverride("fill", CreateBarStyle(BarFill, 5));
        row.AddChild(progress);

        var overlay = new MarginContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        overlay.AddThemeConstantOverride("margin_left", 7);
        overlay.AddThemeConstantOverride("margin_right", 7);
        overlay.AddThemeConstantOverride("margin_top", 2);
        overlay.AddThemeConstantOverride("margin_bottom", 2);
        row.AddChild(overlay);

        var textRow = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        textRow.AddThemeConstantOverride("separation", 8);
        overlay.AddChild(textRow);

        var nameLabel = new Label
        {
            Text = labelText,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        nameLabel.AddThemeColorOverride("font_color", PrimaryText);
        nameLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        nameLabel.AddThemeConstantOverride("outline_size", 2);
        nameLabel.AddThemeFontSizeOverride("font_size", settings.CompactMode ? 12 : 13);
        textRow.AddChild(nameLabel);

        var damageLabel = new Label
        {
            Text = damage.ToString(),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(42, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        damageLabel.AddThemeColorOverride("font_color", PrimaryText);
        damageLabel.AddThemeFontSizeOverride("font_size", settings.CompactMode ? 12 : 13);
        textRow.AddChild(damageLabel);

        var percentageLabel = new Label
        {
            Text = $"{Math.Round(percentage):0}%",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(42, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        percentageLabel.AddThemeColorOverride("font_color", PrimaryText);
        percentageLabel.AddThemeFontSizeOverride("font_size", settings.CompactMode ? 12 : 13);
        textRow.AddChild(percentageLabel);

        _moduleGrid!.AddChild(row);
    }

    private static void AddDamageSectionHeader(string heading)
    {
        var label = new Label
        {
            Text = heading,
            CustomMinimumSize = new Vector2(0, 20f),
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride("font_color", TitleBlue);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 2);
        label.AddThemeFontSizeOverride("font_size", 14);
        _moduleGrid!.AddChild(label);
    }

    private static StyleBoxFlat CreateBarStyle(Color color, int radius)
    {
        return new StyleBoxFlat
        {
            BgColor = color,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius
        };
    }

    private static void NormalizeScopeSelection(DpsMeterSettingsData settings)
    {
        if (_scopeIndex == ScopeBoth &&
            (!settings.ShowDealtBreakdown || !settings.ShowTakenBreakdown))
        {
            _scopeIndex = settings.ShowDealtBreakdown
                ? ScopeCurrent
                : settings.ShowTakenBreakdown ? ScopeTaken : ScopeCurrent;
        }

        if (_scopeIndex == ScopeTaken && !settings.ShowTakenBreakdown)
        {
            _scopeIndex = ScopeCurrent;
        }

        if (_scopeIndex != ScopeTaken &&
            _scopeIndex != ScopeBoth &&
            !settings.ShowDealtBreakdown)
        {
            _scopeIndex = settings.ShowTakenBreakdown ? ScopeTaken : ScopeCurrent;
        }

        _scopeOption!.Selected = _scopeIndex;
        _scopeOption.SetItemDisabled(ScopeCurrent, !settings.ShowDealtBreakdown);
        _scopeOption.SetItemDisabled(ScopeTurn, !settings.ShowDealtBreakdown);
        _scopeOption.SetItemDisabled(ScopeTaken, !settings.ShowTakenBreakdown);
        _scopeOption.SetItemDisabled(ScopeTotal, !settings.ShowDealtBreakdown);
        _scopeOption.SetItemDisabled(
            ScopeBoth,
            !settings.ShowDealtBreakdown || !settings.ShowTakenBreakdown);
    }

    private static void UpdateNavigationState(int playerCount)
    {
        var canSwitchPlayer = _scopeIndex != ScopeTotal && playerCount > 1;
        _previousPlayerButton!.Disabled = !canSwitchPlayer;
        _nextPlayerButton!.Disabled = !canSwitchPlayer;
        _previousPlayerButton.TooltipText = canSwitchPlayer ? "上一个玩家" : "当前仅有一个统计对象";
        _nextPlayerButton.TooltipText = _previousPlayerButton.TooltipText;
    }

    private static void OnPreviousPlayerPressed()
    {
        MovePlayerSelection(-1);
    }

    private static void OnNextPlayerPressed()
    {
        MovePlayerSelection(1);
    }

    private static void MovePlayerSelection(int delta)
    {
        var players = DpsStats.GetSnapshot().Players;
        if (players.Count <= 1)
        {
            return;
        }

        _selectedPlayerIndex = (_selectedPlayerIndex + delta + players.Count) % players.Count;
        Refresh();
    }

    private static void OnScopeSelected(long index)
    {
        _scopeIndex = Math.Clamp((int)index, ScopeCurrent, ScopeBoth);
        _selectedPlayerIndex = 0;
        Refresh();
    }

    private static void OnCollapsePressed()
    {
        _contentCollapsed = !_contentCollapsed;
        Refresh();
    }

    private static void UpdateCollapseButton()
    {
        if (!IsAlive(_collapseButton))
        {
            return;
        }

        _collapseButton!.Text = _contentCollapsed ? "+" : "−";
        _collapseButton.TooltipText = _contentCollapsed ? "展开伤害明细" : "收起伤害明细";
    }

    private static void EnsurePanelSize(DpsMeterSettingsData settings)
    {
        FitPanelToContent(settings);
    }

    private static void FitPanelToContent(DpsMeterSettingsData settings)
    {
        if (!IsAlive(_panel))
        {
            return;
        }

        _panel!.CustomMinimumSize = new Vector2(MinimumPanelWidth, 0f);
        var minimum = _panel.GetCombinedMinimumSize();
        var viewportSize = GetViewportSize();
        var maxWidth = Mathf.Max(MinimumPanelWidth, viewportSize.X - 24f);
        var maxHeight = Mathf.Max(MinimumPanelHeight, viewportSize.Y - 24f);
        var width = Mathf.Clamp(minimum.X, MinimumPanelWidth, maxWidth);
        var height = Mathf.Clamp(minimum.Y, MinimumPanelHeight, maxHeight);
        _panel.Size = new Vector2(width, height);

        var scaledSize = _panel.Size * settings.UiScale;
        _panel.Position = new Vector2(
            Mathf.Clamp(_panel.Position.X, 0f, Mathf.Max(0f, viewportSize.X - scaledSize.X)),
            Mathf.Clamp(_panel.Position.Y, 0f, Mathf.Max(0f, viewportSize.Y - scaledSize.Y)));
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

    private static void QueueDeferredPanelFit()
    {
        if (!IsAlive(_host) || Interlocked.Exchange(ref _deferredPanelFitQueued, 1) == 1)
        {
            return;
        }

        try
        {
            Callable.From(FlushDeferredPanelFit).CallDeferred();
        }
        catch
        {
            Interlocked.Exchange(ref _deferredPanelFitQueued, 0);
        }
    }

    private static Vector2 GetViewportSize()
    {
        var viewport = _panel?.GetViewport();
        return viewport?.GetVisibleRect().Size ?? new Vector2(1280f, 720f);
    }

    private static void ApplyPanelStyle(DpsMeterSettingsData settings)
    {
        if (_panelStyle is null)
        {
            return;
        }

        _panelStyle.BgColor = new Color(PanelBackground.R, PanelBackground.G, PanelBackground.B, settings.PanelOpacity);
        _panelStyle.BorderColor = PanelBorder;
    }

    private static void BuildPanel(CanvasLayer canvas)
    {
        _panel = new PanelContainer
        {
            Name = "Sts2DpsMeterPanel",
            Position = new Vector2(18, 18),
            Size = new Vector2(MinimumPanelWidth, MinimumPanelHeight),
            CustomMinimumSize = new Vector2(MinimumPanelWidth, 0f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };

        _panelStyle = new StyleBoxFlat
        {
            BgColor = PanelBackground,
            BorderColor = PanelBorder,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 11,
            CornerRadiusTopRight = 11,
            CornerRadiusBottomLeft = 11,
            CornerRadiusBottomRight = 11,
            ContentMarginLeft = 11,
            ContentMarginTop = 10,
            ContentMarginRight = 11,
            ContentMarginBottom = 10
        };
        _panel.AddThemeStyleboxOverride("panel", _panelStyle);
        _panel.GuiInput += OnPanelGuiInput;
        canvas.AddChild(_panel);

        var layout = new VBoxContainer
        {
            Name = "Sts2DpsMeterLayout",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        layout.AddThemeConstantOverride("separation", 8);
        _panel.AddChild(layout);

        var headerRow = new HBoxContainer
        {
            Name = "Sts2DpsMeterHeader",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        headerRow.AddThemeConstantOverride("separation", 6);
        layout.AddChild(headerRow);

        _previousPlayerButton = CreateArrowButton("◀", OnPreviousPlayerPressed);
        headerRow.AddChild(_previousPlayerButton);

        _header = new Label
        {
            Text = "伤害：0",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _header.AddThemeColorOverride("font_color", TitleBlue);
        _header.AddThemeColorOverride("font_outline_color", Colors.Black);
        _header.AddThemeConstantOverride("outline_size", 3);
        _header.AddThemeFontSizeOverride("font_size", 19);
        headerRow.AddChild(_header);

        _nextPlayerButton = CreateArrowButton("▶", OnNextPlayerPressed);
        headerRow.AddChild(_nextPlayerButton);

        _scopeOption = new OptionButton
        {
            CustomMinimumSize = new Vector2(88, 30),
            MouseFilter = Control.MouseFilterEnum.Stop,
            TooltipText = "选择伤害统计范围"
        };
        _scopeOption.AddItem("当前");
        _scopeOption.AddItem("本回合");
        _scopeOption.AddItem("受到");
        _scopeOption.AddItem("总计");
        _scopeOption.AddItem("同时");
        _scopeOption.Selected = ScopeCurrent;
        _scopeOption.ItemSelected += OnScopeSelected;
        _scopeOption.AddThemeColorOverride("font_color", PrimaryText);
        _scopeOption.AddThemeFontSizeOverride("font_size", 12);
        ApplyDropdownStyle(_scopeOption);
        headerRow.AddChild(_scopeOption);

        _collapseButton = CreateArrowButton(_contentCollapsed ? "+" : "−", OnCollapsePressed);
        _collapseButton.TooltipText = _contentCollapsed ? "展开伤害明细" : "收起伤害明细";
        headerRow.AddChild(_collapseButton);

        _contentDivider = new ColorRect
        {
            Color = new Color(PanelBorder.R, PanelBorder.G, PanelBorder.B, 0.18f),
            CustomMinimumSize = new Vector2(0, 1),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        layout.AddChild(_contentDivider);

        _moduleGrid = new GridContainer
        {
            Name = "Sts2DpsMeterRows",
            Columns = 1,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _moduleGrid.AddThemeConstantOverride("h_separation", 6);
        _moduleGrid.AddThemeConstantOverride("v_separation", 6);
        layout.AddChild(_moduleGrid);
    }

    private static Button CreateArrowButton(string text, Action pressed)
    {
        var button = new Button
        {
            Text = text,
            Flat = true,
            CustomMinimumSize = new Vector2(30, 30),
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        button.AddThemeColorOverride("font_color", ArrowBlue);
        button.AddThemeColorOverride("font_hover_color", new Color(0.45f, 0.78f, 1f));
        button.AddThemeColorOverride("font_pressed_color", new Color(0.1f, 0.4f, 0.9f));
        button.AddThemeColorOverride("font_disabled_color", new Color(ArrowBlue.R, ArrowBlue.G, ArrowBlue.B, 0.28f));
        button.AddThemeFontSizeOverride("font_size", 20);
        button.AddThemeStyleboxOverride("normal", CreateFlatButtonStyle(new Color(0f, 0f, 0f, 0f)));
        button.AddThemeStyleboxOverride("hover", CreateFlatButtonStyle(new Color(0.08f, 0.28f, 0.62f, 0.5f)));
        button.AddThemeStyleboxOverride("pressed", CreateFlatButtonStyle(new Color(0.05f, 0.18f, 0.42f, 0.75f)));
        button.AddThemeStyleboxOverride("focus", CreateFlatButtonStyle(new Color(0f, 0f, 0f, 0f)));
        button.Pressed += pressed;
        return button;
    }

    private static StyleBoxFlat CreateFlatButtonStyle(Color background)
    {
        return new StyleBoxFlat
        {
            BgColor = background,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5
        };
    }

    private static void ApplyDropdownStyle(OptionButton option)
    {
        var normal = new StyleBoxFlat
        {
            BgColor = DropdownBackground,
            BorderColor = new Color(PanelBorder.R, PanelBorder.G, PanelBorder.B, 0.72f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
            ContentMarginLeft = 7,
            ContentMarginRight = 7
        };
        var hover = normal.Duplicate() as StyleBoxFlat ?? normal;
        hover.BorderColor = new Color(0.32f, 0.72f, 1f, 0.95f);
        var pressed = normal.Duplicate() as StyleBoxFlat ?? normal;
        pressed.BgColor = new Color(0.08f, 0.1f, 0.14f, 1f);
        option.AddThemeStyleboxOverride("normal", normal);
        option.AddThemeStyleboxOverride("hover", hover);
        option.AddThemeStyleboxOverride("pressed", pressed);
        option.AddThemeStyleboxOverride("focus", hover);
    }

    private static void OnPanelGuiInput(InputEvent @event)
    {
        var settings = DpsMeterSettings.Current;
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.Pressed &&
                mouseButton.CtrlPressed &&
                mouseButton.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                var step = mouseButton.ButtonIndex == MouseButton.WheelUp
                    ? DpsMeterSettings.UiScaleStep
                    : -DpsMeterSettings.UiScaleStep;
                settings.UiScale = DpsMeterSettings.ClampUiScale(settings.UiScale + step);
                DpsMeterSettings.Save();
                Refresh();
                DpsMeterSettingsUi.Refresh();
                _panel!.AcceptEvent();
                return;
            }

            if (settings.LockPosition)
            {
                return;
            }

            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                if (mouseButton.Pressed)
                {
                    _dragging = true;
                }
                else if (_dragging)
                {
                    _dragging = false;
                    settings.PositionX = _panel!.Position.X;
                    settings.PositionY = _panel.Position.Y;
                    DpsMeterSettings.Save();
                    DpsMeterSettingsUi.Refresh();
                }
            }
        }
        else if (@event is InputEventMouseMotion mouseMotion && _dragging && !settings.LockPosition)
        {
            var viewport = _panel!.GetViewport();
            var viewportSize = viewport?.GetVisibleRect().Size ?? new Vector2(1280f, 720f);
            var proposed = _panel.Position + mouseMotion.Relative;
            var scaledSize = _panel.Size * DpsMeterSettings.Current.UiScale;
            _panel.Position = new Vector2(
                Mathf.Clamp(proposed.X, 0f, Mathf.Max(0f, viewportSize.X - scaledSize.X)),
                Mathf.Clamp(proposed.Y, 0f, Mathf.Max(0f, viewportSize.Y - scaledSize.Y)));
        }
    }

    private static void ClearChildren(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static bool IsAlive(GodotObject? value)
    {
        return value is not null && GodotObject.IsInstanceValid(value);
    }

    private sealed record DamageRowData(string Label, long Damage);

    private sealed record DamageSectionData(
        string? Heading,
        long Total,
        IReadOnlyList<DamageRowData> Rows);

    private sealed record DamageViewData(
        string Title,
        IReadOnlyList<DamageSectionData> Sections);
}

internal sealed class DpsMeterHost : Node
{
    private double _refreshTimer;

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo)
        {
            return;
        }

        if (IsKey(key, Key.F8))
        {
            DpsMeterUi.ToggleVisibility();
        }
        else if (IsKey(key, Key.F7))
        {
            DpsMeterUi.ShowSettings();
        }
    }

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

    private static bool IsKey(InputEventKey key, Key expected)
    {
        return key.PhysicalKeycode == expected || key.Keycode == expected;
    }
}
