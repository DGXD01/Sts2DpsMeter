using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Godot;
using MegaCrit.Sts2.Core.Helpers;

namespace Sts2DpsMeter;

public static partial class DpsMeterUi
{
    private const float MinimumPanelWidth = DpsHudLayout.PreferredWidth;
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
    private static VBoxContainer? _layout;
    private static HBoxContainer? _headerRow;
    private static ScrollContainer? _contentScroll;
    private static GridContainer? _moduleGrid;
    private static Label? _header;
    private static Button? _previousPlayerButton;
    private static Button? _nextPlayerButton;
    private static OptionButton? _scopeOption;
    private static Button? _settingsButton;
    private static Button? _collapseButton;
    private static Control? _contentDivider;
    private static Node? _host;
    private static bool _visible = true;
    private static bool _visibilityInitialized;
    private static bool _dragging;
    private static bool _contentCollapsed;
    private static int _selectedPlayerIndex;
    private static int _scopeIndex = ScopeCombat;
    private static int _immediateRefreshRequested;
    private static int _deferredPanelFitQueued;
    private static readonly List<DamageSectionControls> SectionPool = new();
    private static PanelLayoutState? _lastLayoutState;

    public static void EnsureAttached()
    {
        if (IsAlive(_canvas) && IsAlive(_host))
        {
            DpsMeterSettingsUi.EnsureAttached(_canvas!);
            _canvas!.Visible = true;
            ResetContentScroll();
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
                var host = new Node
                {
                    Name = "Sts2DpsMeterHost",
                    ProcessMode = Node.ProcessModeEnum.Always
                };
                _host = host;
                root.AddChild(host);
                host.SetProcess(true);
                var refreshHost = new DpsMeterHost(DpsStats.Tick, RefreshNow);
                void OnProcessFrame() => refreshHost.ProcessFrame(host.GetProcessDeltaTime());
                // 本项目未使用 Godot 源码生成器，直接连接原生信号，避免依赖自定义 _Process 绑定。
                tree!.ProcessFrame += OnProcessFrame;
                host.TreeExiting += () => tree.ProcessFrame -= OnProcessFrame;
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
        RequestImmediateRefresh();
    }

    internal static void RefreshNow()
    {
        if (!IsAlive(_panel) ||
            !IsAlive(_moduleGrid) ||
            !IsAlive(_header) ||
            !IsAlive(_scopeOption) ||
            !IsAlive(_settingsButton) ||
            !IsAlive(_collapseButton) ||
            !IsAlive(_contentDivider) ||
            !IsAlive(_contentScroll))
        {
            return;
        }

        var settings = DpsMeterSettings.Current;
        var snapshot = DpsStats.GetSnapshot();

        _panel!.Visible = _visible;
        if (!_dragging && (_lastLayoutState is null ||
            settings.PositionX != _lastLayoutState.PositionX ||
            settings.PositionY != _lastLayoutState.PositionY))
        {
            _panel.Position = new Vector2(settings.PositionX, settings.PositionY);
        }

        _panel.Scale = new Vector2(settings.UiScale, settings.UiScale);
        _panel.PivotOffset = Vector2.Zero;
        ApplyPanelStyle(settings);
        NormalizeScopeSelection();
        _contentDivider!.Visible = !_contentCollapsed;
        _contentScroll!.Visible = !_contentCollapsed;
        UpdateCollapseButton();

        var view = BuildDamageView(settings, snapshot);
        if (_header!.Text != view.Title)
        {
            _header.Text = view.Title;
        }
        UpdateNavigationState(snapshot.Players.Count);

        _header.TooltipText = view.Title;
        var language = DpsLocalization.LanguageCode;
        var contentChanged = !_contentCollapsed && UpdateDamageSections(settings, view, language);
        var layoutState = new PanelLayoutState(
            language, settings.CompactMode,
            settings.UiScale, settings.PositionX, settings.PositionY,
            _contentCollapsed, GetViewportSize());
        if (contentChanged || layoutState != _lastLayoutState)
        {
            _lastLayoutState = layoutState;
            FitPanelToContent(settings);
            QueueDeferredPanelFit();
        }
    }

    public static void RequestImmediateRefresh()
    {
        // 任意线程只标记请求；Host 每帧至多消费一次，合并连锁伤害及设置刷新。
        // 不在伤害 Hook 或后台线程中访问 Godot 控件。
        Interlocked.Exchange(ref _immediateRefreshRequested, 1);
    }

    internal static bool ConsumeImmediateRefreshRequest()
    {
        return Interlocked.Exchange(ref _immediateRefreshRequested, 0) == 1;
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
        var isMultiplayer = snapshot.Players.Count > 1;
        if (!HasEnabledModules(settings, isMultiplayer))
        {
            return new DamageViewData(L("未启用统计模块", "No statistics modules enabled"), Array.Empty<DamageSectionData>());
        }

        if (snapshot.Players.Count == 0)
        {
            return new DamageViewData(L("暂无记录", "No records"), Array.Empty<DamageSectionData>());
        }

        _selectedPlayerIndex = Math.Clamp(_selectedPlayerIndex, 0, snapshot.Players.Count - 1);
        var player = snapshot.Players[_selectedPlayerIndex];

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
        string? scopeLabel = null)
    {
        scopeLabel ??= L("本场战斗", "This Combat");
        var sections = new List<DamageSectionData>();
        var titleParts = new List<string>();
        var takenDamage = player.IncomingDamage + player.SelfLossDamage;

        if (settings.ShowDealtBreakdown)
        {
            sections.Add(new DamageSectionData(
                L("造成伤害", "Damage Dealt"),
                player.TotalDamage,
                BuildCurrentRows(player)));
            titleParts.Add($"{L("伤害", "Damage")} {player.TotalDamage}");
        }

        if (settings.ShowTakenBreakdown)
        {
            sections.Add(new DamageSectionData(
                L("受到伤害", "Damage Taken"),
                takenDamage,
                BuildTakenRows(player)));
            titleParts.Add($"{L("承伤", "Taken")} {takenDamage}");
        }

        if (settings.ShowBlockGained)
        {
            sections.Add(new DamageSectionData(
                L("格挡防御", "Block"),
                player.BlockGained,
                BuildMetricRows(L("格挡获得", "Block Gained"), player.BlockGained),
                KeepZeroRows: true));
            titleParts.Add($"{L("格挡", "Block")} {player.BlockGained}");
        }

        if (settings.ShowEnergySpent)
        {
            sections.Add(new DamageSectionData(
                L("能量使用", "Energy"),
                player.EnergySpent,
                BuildMetricRows(L("消耗能量", "Energy Spent"), player.EnergySpent),
                KeepZeroRows: true));
            titleParts.Add($"{L("能量", "Energy")} {player.EnergySpent}");
        }

        if (isMultiplayer && settings.ShowEnergyGivenToAllies)
        {
            sections.Add(new DamageSectionData(
                L("给队友能量", "Ally Energy Given"),
                player.EnergyGivenToAllies,
                BuildMetricRows(
                    L("给予队友能量", "Energy Given to Allies"),
                    player.EnergyGivenToAllies),
                KeepZeroRows: true));
            titleParts.Add($"{L("队友能量", "Ally Energy")} {player.EnergyGivenToAllies}");
        }

        if (isMultiplayer && settings.ShowBlockGivenToAllies)
        {
            sections.Add(new DamageSectionData(
                L("给队友护盾", "Ally Block Given"),
                player.BlockGivenToAllies,
                BuildMetricRows(
                    L("给予队友护盾", "Block Given to Allies"),
                    player.BlockGivenToAllies),
                KeepZeroRows: true));
            titleParts.Add($"{L("队友护盾", "Ally Block")} {player.BlockGivenToAllies}");
        }

        if (isMultiplayer && settings.ShowHealingGivenToAllies)
        {
            sections.Add(new DamageSectionData(
                L("给队友回血", "Ally Healing Given"),
                player.HealingGivenToAllies,
                BuildMetricRows(
                    L("给予队友回血", "Healing Given to Allies"),
                    player.HealingGivenToAllies),
                KeepZeroRows: true));
            titleParts.Add($"{L("队友回血", "Ally Healing")} {player.HealingGivenToAllies}");
        }

        if (isMultiplayer && settings.ShowCardsDrawnForAllies)
        {
            sections.Add(new DamageSectionData(
                L("给队友抽牌", "Ally Draw"),
                player.CardsDrawnForAllies,
                BuildMetricRows(
                    L("为队友抽牌", "Cards Drawn for Allies"),
                    player.CardsDrawnForAllies),
                KeepZeroRows: true));
            titleParts.Add($"{L("队友抽牌", "Ally Draw")} {player.CardsDrawnForAllies}");
        }

        return new DamageViewData(
            BuildTitle(scopeLabel, isMultiplayer, LocalizeKnownValue(player.DisplayName), titleParts),
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
        var energyGivenToAllies = turn?.EnergyGivenToAllies ?? 0;
        var blockGivenToAllies = turn?.BlockGivenToAllies ?? 0;
        var healingGivenToAllies = turn?.HealingGivenToAllies ?? 0;
        var cardsDrawnForAllies = turn?.CardsDrawnForAllies ?? 0;
        var takenDamage = (turn?.IncomingDamage ?? 0) + (turn?.SelfLossDamage ?? 0);

        if (settings.ShowDealtBreakdown)
        {
            sections.Add(new DamageSectionData(
                L("造成伤害", "Damage Dealt"),
                damage,
                BuildTurnRows(turn)));
            titleParts.Add($"{L("伤害", "Damage")} {damage}");
        }

        if (settings.ShowTakenBreakdown)
        {
            sections.Add(new DamageSectionData(
                L("受到伤害", "Damage Taken"),
                takenDamage,
                BuildTurnTakenRows(turn),
                KeepZeroRows: true));
            titleParts.Add($"{L("承伤", "Taken")} {takenDamage}");
        }

        if (settings.ShowBlockGained)
        {
            sections.Add(new DamageSectionData(
                L("格挡防御", "Block"),
                blockGained,
                BuildMetricRows(L("格挡获得", "Block Gained"), blockGained),
                KeepZeroRows: true));
            titleParts.Add($"{L("格挡", "Block")} {blockGained}");
        }

        if (settings.ShowEnergySpent)
        {
            sections.Add(new DamageSectionData(
                L("能量使用", "Energy"),
                energySpent,
                BuildMetricRows(L("消耗能量", "Energy Spent"), energySpent),
                KeepZeroRows: true));
            titleParts.Add($"{L("能量", "Energy")} {energySpent}");
        }

        if (isMultiplayer && settings.ShowEnergyGivenToAllies)
        {
            sections.Add(new DamageSectionData(
                L("给队友能量", "Ally Energy Given"),
                energyGivenToAllies,
                BuildMetricRows(
                    L("给予队友能量", "Energy Given to Allies"),
                    energyGivenToAllies),
                KeepZeroRows: true));
            titleParts.Add($"{L("队友能量", "Ally Energy")} {energyGivenToAllies}");
        }

        if (isMultiplayer && settings.ShowBlockGivenToAllies)
        {
            sections.Add(new DamageSectionData(
                L("给队友护盾", "Ally Block Given"),
                blockGivenToAllies,
                BuildMetricRows(
                    L("给予队友护盾", "Block Given to Allies"),
                    blockGivenToAllies),
                KeepZeroRows: true));
            titleParts.Add($"{L("队友护盾", "Ally Block")} {blockGivenToAllies}");
        }

        if (isMultiplayer && settings.ShowHealingGivenToAllies)
        {
            sections.Add(new DamageSectionData(
                L("给队友回血", "Ally Healing Given"),
                healingGivenToAllies,
                BuildMetricRows(
                    L("给予队友回血", "Healing Given to Allies"),
                    healingGivenToAllies),
                KeepZeroRows: true));
            titleParts.Add($"{L("队友回血", "Ally Healing")} {healingGivenToAllies}");
        }

        if (isMultiplayer && settings.ShowCardsDrawnForAllies)
        {
            sections.Add(new DamageSectionData(
                L("给队友抽牌", "Ally Draw"),
                cardsDrawnForAllies,
                BuildMetricRows(
                    L("为队友抽牌", "Cards Drawn for Allies"),
                    cardsDrawnForAllies),
                KeepZeroRows: true));
            titleParts.Add($"{L("队友抽牌", "Ally Draw")} {cardsDrawnForAllies}");
        }

        return new DamageViewData(
            BuildTitle(L("本回合", "This Turn"), isMultiplayer, LocalizeKnownValue(player.DisplayName), titleParts),
            sections);
    }

    private static DamageViewData BuildTotalView(
        DpsMeterSettingsData settings,
        CombatStatsSnapshot snapshot,
        bool isMultiplayer)
    {
        if (!isMultiplayer)
        {
            return BuildCombatView(
                settings,
                snapshot.Players[0],
                isMultiplayer: false,
                scopeLabel: L("总计", "Total"));
        }

        var dealtTotal = snapshot.Players.Sum(player => player.TotalDamage);
        var takenTotal = snapshot.Players.Sum(player => player.IncomingDamage + player.SelfLossDamage);
        var blockTotal = snapshot.Players.Sum(player => player.BlockGained);
        var energyTotal = snapshot.Players.Sum(player => player.EnergySpent);
        var energyGivenTotal = snapshot.Players.Sum(player => player.EnergyGivenToAllies);
        var blockGivenTotal = snapshot.Players.Sum(player => player.BlockGivenToAllies);
        var healingGivenTotal = snapshot.Players.Sum(player => player.HealingGivenToAllies);
        var cardsDrawnForAlliesTotal = snapshot.Players.Sum(player => player.CardsDrawnForAllies);
        var sections = new List<DamageSectionData>();
        var titleParts = new List<string>();

        if (settings.ShowDealtBreakdown)
        {
            sections.Add(new DamageSectionData(
                L("队伍输出排行", "Team Damage Ranking"),
                dealtTotal,
                BuildMultiplayerOutputRows(snapshot),
                KeepZeroRows: true));
            titleParts.Add($"{L("伤害", "Damage")} {dealtTotal}");
        }

        if (settings.ShowTakenBreakdown)
        {
            sections.Add(new DamageSectionData(
                L("队伍承伤排行", "Team Damage Taken Ranking"),
                takenTotal,
                BuildMultiplayerTakenRows(snapshot),
                KeepZeroRows: true));
            titleParts.Add($"{L("承伤", "Taken")} {takenTotal}");
        }

        if (settings.ShowBlockGained)
        {
            sections.Add(new DamageSectionData(
                L("队伍格挡排行", "Team Block Ranking"),
                blockTotal,
                BuildMultiplayerBlockRows(snapshot),
                KeepZeroRows: true));
            titleParts.Add($"{L("格挡", "Block")} {blockTotal}");
        }

        if (settings.ShowEnergySpent)
        {
            sections.Add(new DamageSectionData(
                L("队伍能量排行", "Team Energy Ranking"),
                energyTotal,
                BuildMultiplayerEnergyRows(snapshot),
                KeepZeroRows: true));
            titleParts.Add($"{L("能量", "Energy")} {energyTotal}");
        }

        if (settings.ShowEnergyGivenToAllies)
        {
            sections.Add(new DamageSectionData(
                L("队友能量排行", "Ally Energy Ranking"),
                energyGivenTotal,
                BuildMultiplayerAllySupportRows(
                    snapshot,
                    player => player.EnergyGivenToAllies,
                    L("给予队友能量", "Energy Given to Allies")),
                KeepZeroRows: true));
            titleParts.Add($"{L("队友能量", "Ally Energy")} {energyGivenTotal}");
        }

        if (settings.ShowBlockGivenToAllies)
        {
            sections.Add(new DamageSectionData(
                L("队友护盾排行", "Ally Block Ranking"),
                blockGivenTotal,
                BuildMultiplayerAllySupportRows(
                    snapshot,
                    player => player.BlockGivenToAllies,
                    L("给予队友护盾", "Block Given to Allies")),
                KeepZeroRows: true));
            titleParts.Add($"{L("队友护盾", "Ally Block")} {blockGivenTotal}");
        }

        if (settings.ShowHealingGivenToAllies)
        {
            sections.Add(new DamageSectionData(
                L("队友回血排行", "Ally Healing Ranking"),
                healingGivenTotal,
                BuildMultiplayerAllySupportRows(
                    snapshot,
                    player => player.HealingGivenToAllies,
                    L("给予队友回血", "Healing Given to Allies")),
                KeepZeroRows: true));
            titleParts.Add($"{L("队友回血", "Ally Healing")} {healingGivenTotal}");
        }

        if (settings.ShowCardsDrawnForAllies)
        {
            sections.Add(new DamageSectionData(
                L("队友抽牌排行", "Ally Draw Ranking"),
                cardsDrawnForAlliesTotal,
                BuildMultiplayerAllySupportRows(
                    snapshot,
                    player => player.CardsDrawnForAllies,
                    L("为队友抽牌", "Cards Drawn for Allies")),
                KeepZeroRows: true));
            titleParts.Add($"{L("队友抽牌", "Ally Draw")} {cardsDrawnForAlliesTotal}");
        }

        return new DamageViewData(
            BuildTitle(L("总计", "Total"), isMultiplayer: true, playerName: null, titleParts),
            sections);
    }

    private static string L(string chinese, string english)
    {
        return DpsLocalization.Get(chinese, english);
    }

    private static string LocalizeKnownValue(string? value)
    {
        return DpsLocalization.LocalizeKnownValue(value);
    }

    private static string FormatHits(long count)
    {
        return count == 1 ? L("1 次", "1 hit") : L($"{count} 次", $"{count} hits");
    }

    private static bool HasEnabledModules(
        DpsMeterSettingsData settings,
        bool isMultiplayer)
    {
        return settings.ShowDealtBreakdown
            || settings.ShowTakenBreakdown
            || settings.ShowBlockGained
            || settings.ShowEnergySpent
            || (isMultiplayer && (
                settings.ShowEnergyGivenToAllies
                || settings.ShowBlockGivenToAllies
                || settings.ShowHealingGivenToAllies
                || settings.ShowCardsDrawnForAllies));
    }

    private static string BuildTitle(
        string scopeLabel,
        bool isMultiplayer,
        string? playerName,
        IReadOnlyList<string> values)
    {
        var detail = values.Count == 0 ? "0" : string.Join(" / ", values);
        var playerPrefix = string.IsNullOrWhiteSpace(playerName) ? string.Empty : $"{playerName} · ";
        var multiplayerPrefix = isMultiplayer ? $"{L("多人 · ", "Multiplayer · ")}{playerPrefix}" : string.Empty;
        return $"{multiplayerPrefix}{scopeLabel}{L("：", ": ")}{detail}";
    }

    private static IReadOnlyList<DamageRowData> BuildMultiplayerOutputRows(CombatStatsSnapshot snapshot)
    {
        return snapshot.Players
            .OrderByDescending(player => player.TotalDamage)
            .ThenBy(player => player.PlayerNetId)
            .Select(player => new DamageRowData(
                LocalizeKnownValue(player.DisplayName),
                player.TotalDamage,
                $"{LocalizeKnownValue(player.CharacterName)} · {player.Dps:0.0} DPS · {player.Dpt:0.0} DPT · {FormatHits(player.HitCount)}",
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
                LocalizeKnownValue(player.DisplayName),
                player.IncomingDamage + player.SelfLossDamage,
                $"{LocalizeKnownValue(player.CharacterName)} · {L("敌方", "Enemy")} {player.IncomingDamage} · {L("自损", "Self")} {player.SelfLossDamage} · " +
                $"{L("命中", "Hits")} {player.IncomingHits + player.SelfLossHits}",
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
                LocalizeKnownValue(player.DisplayName),
                player.BlockGained,
                $"{LocalizeKnownValue(player.CharacterName)} · {L("格挡获得", "Block Gained")} {player.BlockGained}",
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
                LocalizeKnownValue(player.DisplayName),
                player.EnergySpent,
                $"{LocalizeKnownValue(player.CharacterName)} · {L("消耗能量", "Energy Spent")} {player.EnergySpent}",
                player.NameColorHex,
                player.IsLocalPlayer))
            .ToArray();
    }

    private static IReadOnlyList<DamageRowData> BuildMultiplayerAllySupportRows(
        CombatStatsSnapshot snapshot,
        Func<PlayerStatsSnapshot, long> selector,
        string label)
    {
        return snapshot.Players
            .OrderByDescending(selector)
            .ThenBy(player => player.PlayerNetId)
            .Select(player => new DamageRowData(
                LocalizeKnownValue(player.DisplayName),
                selector(player),
                $"{LocalizeKnownValue(player.CharacterName)} · {label} {selector(player)}",
                player.NameColorHex,
                player.IsLocalPlayer))
            .ToArray();
    }

    private static IReadOnlyList<DamageRowData> BuildCurrentRows(PlayerStatsSnapshot player)
    {
        return player.Sources
            .Select(source => new DamageRowData(LocalizeKnownValue(source.Source), source.Damage))
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
                LocalizeKnownValue(group.First().DisplayName),
                group.Sum(card => card.Damage)))
            .ToArray();
    }

    private static IReadOnlyList<DamageRowData> BuildTurnTakenRows(TurnStatsSnapshot? turn)
    {
        return new[]
        {
            new DamageRowData(L("敌方伤害", "Enemy Damage"), turn?.IncomingDamage ?? 0),
            new DamageRowData(L("自损", "Self"), turn?.SelfLossDamage ?? 0)
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
            .Select(group => new DamageRowData(LocalizeKnownValue(group.Key), group.Sum(source => source.Damage)))
            .ToArray();
    }


    private static string BuildRichRowText(DamageRowData rowData)
    {
        var escapedName = rowData.Label.EscapeBbcodeTags();
        var colorHex = NormalizeColorHex(rowData.NameColorHex);
        var text = $"[b][color=#{colorHex}]{escapedName}[/color][/b]";

        if (rowData.IsLocalPlayer)
        {
            text += $" [color=#7fd3ff]{L("（你）", "(You)")}[/color]";
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
        _scopeOption!.SetItemText(ScopeCombat, L("本场战斗", "This Combat"));
        _scopeOption.SetItemText(ScopeTurn, L("本回合", "This Turn"));
        _scopeOption.SetItemText(ScopeTotal, L("总计", "Total"));
        _scopeOption.TooltipText = L("选择统计范围", "Select scope");
        _scopeOption.Selected = _scopeIndex;
        for (var index = ScopeCombat; index <= ScopeTotal; index++)
        {
            _scopeOption.SetItemDisabled(index, disabled: false);
        }

        if (IsAlive(_settingsButton))
        {
            _settingsButton!.Text = "⚙";
            _settingsButton.TooltipText = L("打开设置", "Open settings");
        }
    }

    private static void UpdateNavigationState(int playerCount)
    {
        var canSwitchPlayer = _scopeIndex != ScopeTotal && playerCount > 1;
        _previousPlayerButton!.Disabled = !canSwitchPlayer;
        _nextPlayerButton!.Disabled = !canSwitchPlayer;
        _previousPlayerButton.TooltipText = canSwitchPlayer
            ? L("上一个玩家", "Previous player")
            : _scopeIndex == ScopeTotal ? L("总计显示队伍汇总", "Total shows the team summary") : L("当前仅有一个玩家", "Only one player in this combat");
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
        ResetContentScroll();
        Refresh();
    }

    private static void OnScopeSelected(long index)
    {
        _scopeIndex = Math.Clamp((int)index, ScopeCombat, ScopeTotal);
        _selectedPlayerIndex = 0;
        ResetContentScroll();
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
        _collapseButton.TooltipText = _contentCollapsed ? L("展开伤害明细", "Expand damage details") : L("收起伤害明细", "Collapse damage details");
    }

    private static void FitPanelToContent(DpsMeterSettingsData settings)
    {
        if (!IsAlive(_panel) || !IsAlive(_panelStyle) || !IsAlive(_layout) ||
            !IsAlive(_headerRow) || !IsAlive(_contentScroll) || !IsAlive(_moduleGrid))
        {
            return;
        }

        var chromeMinimum = _panelStyle!.GetMinimumSize() + _headerRow!.GetCombinedMinimumSize();
        if (!_contentCollapsed)
        {
            chromeMinimum.Y += _contentDivider!.GetCombinedMinimumSize().Y
                + 2 * _layout!.GetThemeConstant("separation");
        }

        // ScrollContainer 隔离明细的最小高度，避免容器把已限高的面板重新撑出屏幕。
        var bounds = DpsHudLayout.Calculate(
            GetViewportSize(), _panel!.Position, settings.UiScale, chromeMinimum,
            _moduleGrid!.GetCombinedMinimumSize().Y, _contentCollapsed);
        _contentScroll!.CustomMinimumSize = new Vector2(0, bounds.BodyHeight);
        _panel.CustomMinimumSize = new Vector2(bounds.Size.X, 0);
        _panel.Size = bounds.Size;
        _panel.Position = bounds.Position;
    }

    private static void ResetContentScroll()
    {
        if (IsAlive(_contentScroll))
        {
            _contentScroll!.ScrollVertical = 0;
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
        SectionPool.Clear();
        ActiveRows.Clear();
        _lastLayoutState = null;
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
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 8,
            ContentMarginTop = 6,
            ContentMarginRight = 8,
            ContentMarginBottom = 6
        };
        _panel.AddThemeStyleboxOverride("panel", _panelStyle);
        _panel.GuiInput += OnPanelGuiInput;
        canvas.AddChild(_panel);

        var layout = new VBoxContainer
        {
            Name = "Sts2DpsMeterLayout",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        layout.AddThemeConstantOverride("separation", 4);
        _layout = layout;
        _panel.AddChild(layout);

        var headerRow = new HBoxContainer
        {
            Name = "Sts2DpsMeterHeader",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        headerRow.AddThemeConstantOverride("separation", 4);
        _headerRow = headerRow;
        layout.AddChild(headerRow);

        _previousPlayerButton = CreateArrowButton("◀", OnPreviousPlayerPressed);
        headerRow.AddChild(_previousPlayerButton);

        _header = new Label
        {
            Text = L("伤害：0", "Damage: 0"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _header.AddThemeColorOverride("font_color", TitleBlue);
        _header.AddThemeColorOverride("font_outline_color", Colors.Black);
        _header.AddThemeConstantOverride("outline_size", 3);
        _header.AddThemeFontSizeOverride("font_size", 14);
        headerRow.AddChild(_header);

        _nextPlayerButton = CreateArrowButton("▶", OnNextPlayerPressed);
        headerRow.AddChild(_nextPlayerButton);

        _scopeOption = new OptionButton
        {
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(104, 30),
            MouseFilter = Control.MouseFilterEnum.Stop,
            TooltipText = L("选择统计范围", "Select scope")
        };
        _scopeOption.AddItem(L("本场战斗", "This Combat"));
        _scopeOption.AddItem(L("本回合", "This Turn"));
        _scopeOption.AddItem(L("总计", "Total"));
        _scopeOption.Selected = ScopeCombat;
        _scopeOption.ItemSelected += OnScopeSelected;
        _scopeOption.AddThemeColorOverride("font_color", PrimaryText);
        _scopeOption.AddThemeFontSizeOverride("font_size", 12);
        ApplyDropdownStyle(_scopeOption);
        headerRow.AddChild(_scopeOption);

        _settingsButton = CreateArrowButton("⚙", OnSettingsPressed);
        _settingsButton.TooltipText = L("打开设置", "Open settings");
        headerRow.AddChild(_settingsButton);

        _collapseButton = CreateArrowButton(_contentCollapsed ? "+" : "−", OnCollapsePressed);
        _collapseButton.TooltipText = _contentCollapsed ? L("展开伤害明细", "Expand damage details") : L("收起伤害明细", "Collapse damage details");
        headerRow.AddChild(_collapseButton);

        _contentDivider = new ColorRect
        {
            Color = new Color(PanelBorder.R, PanelBorder.G, PanelBorder.B, 0.18f),
            CustomMinimumSize = new Vector2(0, 1),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        layout.AddChild(_contentDivider);

        _contentScroll = new ScrollContainer
        {
            Name = "Sts2DpsMeterScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Pass,
            MouseForcePassScrollEvents = false
        };
        _contentScroll.GetVScrollBar().FocusMode = Control.FocusModeEnum.None;
        layout.AddChild(_contentScroll);

        _moduleGrid = new GridContainer
        {
            Name = "Sts2DpsMeterRows",
            Columns = 1,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _moduleGrid.AddThemeConstantOverride("h_separation", 6);
        _moduleGrid.AddThemeConstantOverride("v_separation", 4);
        _contentScroll.AddChild(_moduleGrid);
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

    private sealed record PanelLayoutState(
        string Language,
        bool CompactMode,
        float UiScale,
        float PositionX,
        float PositionY,
        bool Collapsed,
        Vector2 ViewportSize);
}

internal sealed class DpsMeterHost
{
    private readonly Action<double> _tick;
    private readonly Action _refresh;
    private double _refreshTimer;

    internal DpsMeterHost(Action<double> tick, Action refresh)
    {
        _tick = tick;
        _refresh = refresh;
    }

    internal void ProcessFrame(double delta)
    {
        _tick(delta);

        var immediateRefresh = DpsMeterUi.ConsumeImmediateRefreshRequest();
        _refreshTimer -= delta;
        if (immediateRefresh || _refreshTimer <= 0)
        {
            _refreshTimer = 0.25;
            _refresh();
        }
    }

}
