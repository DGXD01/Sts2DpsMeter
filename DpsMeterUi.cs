using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Godot;
using MegaCrit.Sts2.Core.Helpers;

namespace Sts2DpsMeter;

public static class DpsMeterUi
{
    private const float MinimumPanelWidth = 360f;
    private const float MinimumPanelHeight = 1f;
    private const int ScopeCombat = 0;
    private const int ScopeTurn = 1;
    private const int ScopeTotal = 2;

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
    private static Button? _settingsButton;
    private static Button? _collapseButton;
    private static Control? _contentDivider;
    private static DpsMeterHost? _host;
    private static bool _visible = true;
    private static bool _visibilityInitialized;
    private static bool _dragging;
    private static bool _contentCollapsed;
    private static int _selectedPlayerIndex;
    private static int _scopeIndex = ScopeCombat;
    private static int _immediateRefreshRequested;
    private static int _deferredRefreshQueued;
    private static int _deferredPanelFitQueued;

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
            !IsAlive(_settingsButton) ||
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
        NormalizeScopeSelection();
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
                    .Where(row => section.KeepZeroRows || row.Damage > 0)
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
                    AddDamageRow(settings, row, percentage);
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

    private static DamageViewData BuildDamageView(
        DpsMeterSettingsData settings,
        CombatStatsSnapshot snapshot)
    {
        if (!HasEnabledModules(settings))
        {
            return new DamageViewData("未启用统计模块", Array.Empty<DamageSectionData>());
        }

        if (snapshot.Players.Count == 0)
        {
            return new DamageViewData("暂无记录", Array.Empty<DamageSectionData>());
        }

        _selectedPlayerIndex = Math.Clamp(_selectedPlayerIndex, 0, snapshot.Players.Count - 1);
        var player = snapshot.Players[_selectedPlayerIndex];
        var isMultiplayer = snapshot.Players.Count > 1;

        if (_scopeIndex == ScopeTotal)
        {
            return BuildTotalView(settings, snapshot, isMultiplayer);
        }

        return _scopeIndex == ScopeTurn
            ? BuildTurnView(settings, player, snapshot.CurrentTurn, isMultiplayer)
            : BuildCombatView(settings, player, isMultiplayer);
    }

    private static DamageViewData BuildCombatView(
        DpsMeterSettingsData settings,
        PlayerStatsSnapshot player,
        bool isMultiplayer,
        string scopeLabel = "本场战斗")
    {
        var sections = new List<DamageSectionData>();
        var titleParts = new List<string>();
        var takenDamage = player.IncomingDamage + player.SelfLossDamage;

        if (settings.ShowDealtBreakdown)
        {
            sections.Add(new DamageSectionData("造成伤害", player.TotalDamage, BuildCurrentRows(player)));
            titleParts.Add($"伤害 {player.TotalDamage}");
        }

        if (settings.ShowTakenBreakdown)
        {
            sections.Add(new DamageSectionData("受到伤害", takenDamage, BuildTakenRows(player)));
            titleParts.Add($"承伤 {takenDamage}");
        }

        if (settings.ShowBlockGained)
        {
            sections.Add(new DamageSectionData(
                "格挡防御",
                player.BlockGained,
                BuildMetricRows("格挡获得", player.BlockGained),
                KeepZeroRows: true));
            titleParts.Add($"格挡 {player.BlockGained}");
        }

        if (settings.ShowEnergySpent)
        {
            sections.Add(new DamageSectionData(
                "能量使用",
                player.EnergySpent,
                BuildMetricRows("消耗能量", player.EnergySpent),
                KeepZeroRows: true));
            titleParts.Add($"能量 {player.EnergySpent}");
        }

        return new DamageViewData(
            BuildTitle(scopeLabel, isMultiplayer, player.DisplayName, titleParts),
            sections);
    }

    private static DamageViewData BuildTurnView(
        DpsMeterSettingsData settings,
        PlayerStatsSnapshot player,
        int currentTurn,
        bool isMultiplayer)
    {
        var turn = player.Turns.FirstOrDefault(item => item.Turn == currentTurn);
        var sections = new List<DamageSectionData>();
        var titleParts = new List<string>();
        var damage = turn?.Damage ?? 0;
        var blockGained = turn?.BlockGained ?? 0;
        var energySpent = turn?.EnergySpent ?? 0;
        var takenDamage = (turn?.IncomingDamage ?? 0) + (turn?.SelfLossDamage ?? 0);

        if (settings.ShowDealtBreakdown)
        {
            sections.Add(new DamageSectionData(
                "造成伤害",
                damage,
                BuildTurnRows(turn)));
            titleParts.Add($"伤害 {damage}");
        }

        if (settings.ShowTakenBreakdown)
        {
            sections.Add(new DamageSectionData(
                "受到伤害",
                takenDamage,
                BuildTurnTakenRows(turn),
                KeepZeroRows: true));
            titleParts.Add($"承伤 {takenDamage}");
        }

        if (settings.ShowBlockGained)
        {
            sections.Add(new DamageSectionData(
                "格挡防御",
                blockGained,
                BuildMetricRows("格挡获得", blockGained),
                KeepZeroRows: true));
            titleParts.Add($"格挡 {blockGained}");
        }

        if (settings.ShowEnergySpent)
        {
            sections.Add(new DamageSectionData(
                "能量使用",
                energySpent,
                BuildMetricRows("消耗能量", energySpent),
                KeepZeroRows: true));
            titleParts.Add($"能量 {energySpent}");
        }

        return new DamageViewData(
            BuildTitle("本回合", isMultiplayer, player.DisplayName, titleParts),
            sections);
    }

    private static DamageViewData BuildTotalView(
        DpsMeterSettingsData settings,
        CombatStatsSnapshot snapshot,
        bool isMultiplayer)
    {
        if (!isMultiplayer)
        {
            return BuildCombatView(settings, snapshot.Players[0], isMultiplayer: false, scopeLabel: "总计");
        }

        var dealtTotal = snapshot.Players.Sum(player => player.TotalDamage);
        var takenTotal = snapshot.Players.Sum(player => player.IncomingDamage + player.SelfLossDamage);
        var blockTotal = snapshot.Players.Sum(player => player.BlockGained);
        var energyTotal = snapshot.Players.Sum(player => player.EnergySpent);
        var sections = new List<DamageSectionData>();
        var titleParts = new List<string>();

        if (settings.ShowDealtBreakdown)
        {
            sections.Add(new DamageSectionData(
                "队伍输出排行",
                dealtTotal,
                BuildMultiplayerOutputRows(snapshot),
                KeepZeroRows: true));
            titleParts.Add($"伤害 {dealtTotal}");
        }

        if (settings.ShowTakenBreakdown)
        {
            sections.Add(new DamageSectionData(
                "队伍承伤排行",
                takenTotal,
                BuildMultiplayerTakenRows(snapshot),
                KeepZeroRows: true));
            titleParts.Add($"承伤 {takenTotal}");
        }

        if (settings.ShowBlockGained)
        {
            sections.Add(new DamageSectionData(
                "队伍格挡排行",
                blockTotal,
                BuildMultiplayerBlockRows(snapshot),
                KeepZeroRows: true));
            titleParts.Add($"格挡 {blockTotal}");
        }

        if (settings.ShowEnergySpent)
        {
            sections.Add(new DamageSectionData(
                "队伍能量排行",
                energyTotal,
                BuildMultiplayerEnergyRows(snapshot),
                KeepZeroRows: true));
            titleParts.Add($"能量 {energyTotal}");
        }

        return new DamageViewData(
            BuildTitle("总计", isMultiplayer: true, playerName: null, titleParts),
            sections);
    }


    private static bool HasEnabledModules(DpsMeterSettingsData settings)
    {
        return settings.ShowDealtBreakdown
            || settings.ShowTakenBreakdown
            || settings.ShowBlockGained
            || settings.ShowEnergySpent;
    }

    private static string BuildTitle(
        string scopeLabel,
        bool isMultiplayer,
        string? playerName,
        IReadOnlyList<string> values)
    {
        var detail = values.Count == 0 ? "0" : string.Join(" / ", values);
        var playerPrefix = string.IsNullOrWhiteSpace(playerName) ? string.Empty : $"{playerName} · ";
        var multiplayerPrefix = isMultiplayer ? $"多人 · {playerPrefix}" : string.Empty;
        return $"{multiplayerPrefix}{scopeLabel}：{detail}";
    }

    private static IReadOnlyList<DamageRowData> BuildMultiplayerOutputRows(CombatStatsSnapshot snapshot)
    {
        return snapshot.Players
            .OrderByDescending(player => player.TotalDamage)
            .ThenBy(player => player.PlayerNetId)
            .Select(player => new DamageRowData(
                player.DisplayName,
                player.TotalDamage,
                $"{player.CharacterName} · {player.Dps:0.0} DPS · {player.Dpt:0.0} DPT · {player.HitCount} 次",
                player.NameColorHex,
                player.IsLocalPlayer))
            .ToArray();
    }

    private static IReadOnlyList<DamageRowData> BuildMultiplayerTakenRows(CombatStatsSnapshot snapshot)
    {
        return snapshot.Players
            .OrderByDescending(player => player.IncomingDamage + player.SelfLossDamage)
            .ThenBy(player => player.PlayerNetId)
            .Select(player => new DamageRowData(
                player.DisplayName,
                player.IncomingDamage + player.SelfLossDamage,
                $"{player.CharacterName} · 敌方 {player.IncomingDamage} · 自损 {player.SelfLossDamage} · " +
                $"命中 {player.IncomingHits + player.SelfLossHits}",
                player.NameColorHex,
                player.IsLocalPlayer))
            .ToArray();
    }

    private static IReadOnlyList<DamageRowData> BuildMultiplayerBlockRows(CombatStatsSnapshot snapshot)
    {
        return snapshot.Players
            .OrderByDescending(player => player.BlockGained)
            .ThenBy(player => player.PlayerNetId)
            .Select(player => new DamageRowData(
                player.DisplayName,
                player.BlockGained,
                $"{player.CharacterName} · 格挡获得 {player.BlockGained}",
                player.NameColorHex,
                player.IsLocalPlayer))
            .ToArray();
    }

    private static IReadOnlyList<DamageRowData> BuildMultiplayerEnergyRows(CombatStatsSnapshot snapshot)
    {
        return snapshot.Players
            .OrderByDescending(player => player.EnergySpent)
            .ThenBy(player => player.PlayerNetId)
            .Select(player => new DamageRowData(
                player.DisplayName,
                player.EnergySpent,
                $"{player.CharacterName} · 消耗能量 {player.EnergySpent}",
                player.NameColorHex,
                player.IsLocalPlayer))
            .ToArray();
    }

    private static IReadOnlyList<DamageRowData> BuildCurrentRows(PlayerStatsSnapshot player)
    {
        return player.Sources
            .Select(source => new DamageRowData(source.Source, source.Damage))
            .ToArray();
    }

    private static IReadOnlyList<DamageRowData> BuildTurnRows(TurnStatsSnapshot? turn)
    {
        if (turn is null)
        {
            return Array.Empty<DamageRowData>();
        }

        return turn.Cards
            .GroupBy(card => card.CardId ?? card.GroupId, StringComparer.Ordinal)
            .Select(group => new DamageRowData(
                group.First().DisplayName,
                group.Sum(card => card.Damage)))
            .ToArray();
    }

    private static IReadOnlyList<DamageRowData> BuildTurnTakenRows(TurnStatsSnapshot? turn)
    {
        return new[]
        {
            new DamageRowData("敌方伤害", turn?.IncomingDamage ?? 0),
            new DamageRowData("自损", turn?.SelfLossDamage ?? 0)
        };
    }

    private static IReadOnlyList<DamageRowData> BuildMetricRows(string label, long value)
    {
        return new[] { new DamageRowData(label, value) };
    }

    private static IReadOnlyList<DamageRowData> BuildTakenRows(PlayerStatsSnapshot player)
    {
        return player.IncomingSources
            .Concat(player.SelfLossSources)
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
        AddDamageRow(settings, new DamageRowData(labelText, damage), percentage);
    }

    private static void AddDamageRow(
        DpsMeterSettingsData settings,
        DamageRowData rowData,
        double percentage)
    {
        var labelText = rowData.Label;
        var damage = rowData.Damage;
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

        Control nameControl;
        if (rowData.Details is not null)
        {
            var richName = new RichTextLabel
            {
                Text = BuildRichRowText(rowData),
                BbcodeEnabled = true,
                FitContent = true,
                ScrollActive = false,
                AutowrapMode = TextServer.AutowrapMode.Off,
                ClipContents = true,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                CustomMinimumSize = new Vector2(0, rowHeight),
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            richName.AddThemeColorOverride("default_color", PrimaryText);
            richName.AddThemeColorOverride("font_outline_color", Colors.Black);
            richName.AddThemeConstantOverride("outline_size", 2);
            richName.AddThemeFontSizeOverride(
                "normal_font_size",
                settings.CompactMode ? 12 : 13);
            nameControl = richName;
        }
        else
        {
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
            nameControl = nameLabel;
        }
        textRow.AddChild(nameControl);

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

    private static string BuildRichRowText(DamageRowData rowData)
    {
        var escapedName = rowData.Label.EscapeBbcodeTags();
        var colorHex = NormalizeColorHex(rowData.NameColorHex);
        var text = $"[b][color=#{colorHex}]{escapedName}[/color][/b]";

        if (rowData.IsLocalPlayer)
        {
            text += " [color=#7fd3ff]（你）[/color]";
        }

        if (!string.IsNullOrWhiteSpace(rowData.Details))
        {
            text += $" [color=#9aa4b2]{rowData.Details.EscapeBbcodeTags()}[/color]";
        }

        return text;
    }

    private static string NormalizeColorHex(string? value)
    {
        var colorHex = value?.Trim().TrimStart('#') ?? string.Empty;
        if (colorHex.Length != 6 || colorHex.Any(character => !Uri.IsHexDigit(character)))
        {
            return "FFFFFF";
        }

        return colorHex.ToUpperInvariant();
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

    private static void NormalizeScopeSelection()
    {
        _scopeIndex = Math.Clamp(_scopeIndex, ScopeCombat, ScopeTotal);
        _scopeOption!.Selected = _scopeIndex;
        for (var index = ScopeCombat; index <= ScopeTotal; index++)
        {
            _scopeOption.SetItemDisabled(index, disabled: false);
        }
    }

    private static void UpdateNavigationState(int playerCount)
    {
        var canSwitchPlayer = _scopeIndex != ScopeTotal && playerCount > 1;
        _previousPlayerButton!.Disabled = !canSwitchPlayer;
        _nextPlayerButton!.Disabled = !canSwitchPlayer;
        _previousPlayerButton.TooltipText = canSwitchPlayer
            ? "上一个玩家"
            : _scopeIndex == ScopeTotal ? "总计显示队伍汇总" : "当前仅有一个玩家";
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
        if (players.Count <= 1 || _scopeIndex == ScopeTotal)
        {
            return;
        }

        _selectedPlayerIndex = (_selectedPlayerIndex + delta + players.Count) % players.Count;
        Refresh();
    }

    private static void OnScopeSelected(long index)
    {
        _scopeIndex = Math.Clamp((int)index, ScopeCombat, ScopeTotal);
        _selectedPlayerIndex = 0;
        Refresh();
    }
    private static void OnSettingsPressed()
    {
        DpsMeterSettingsUi.ToggleVisibility();
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
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(104, 30),
            MouseFilter = Control.MouseFilterEnum.Stop,
            TooltipText = "选择统计范围"
        };
        _scopeOption.AddItem("本场战斗");
        _scopeOption.AddItem("本回合");
        _scopeOption.AddItem("总计");
        _scopeOption.Selected = ScopeCombat;
        _scopeOption.ItemSelected += OnScopeSelected;
        _scopeOption.AddThemeColorOverride("font_color", PrimaryText);
        _scopeOption.AddThemeFontSizeOverride("font_size", 12);
        ApplyDropdownStyle(_scopeOption);
        headerRow.AddChild(_scopeOption);

        _settingsButton = CreateArrowButton("设", OnSettingsPressed);
        _settingsButton.TooltipText = "打开设置";
        headerRow.AddChild(_settingsButton);

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

    private sealed record DamageRowData(
        string Label,
        long Damage,
        string? Details = null,
        string? NameColorHex = null,
        bool IsLocalPlayer = false);

    private sealed record DamageSectionData(
        string? Heading,
        long Total,
        IReadOnlyList<DamageRowData> Rows,
        bool KeepZeroRows = false);

    private sealed record DamageViewData(
        string Title,
        IReadOnlyList<DamageSectionData> Sections);
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

}
