using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Godot;
using MegaCrit.Sts2.Core.Helpers;

namespace Sts2DpsMeter;

internal static class DpsMeterModuleUi
{
    private const float MinimumModuleWidth = 320f;
    private const float MultiplayerMinimumWidth = 430f;
    private const float MinimumModuleHeight = 1f;
    private const float DetachHandleHeight = 12f;
    private const float SnapDistance = 16f;
    private const float ViewportMargin = 8f;

    private static readonly Color PanelBackground = new(0f, 0f, 0f, 0.92f);
    private static readonly Color PanelBorder = new(0.92f, 0.95f, 1f, 0.88f);
    private static readonly Color TitleBlue = new(0.2f, 0.6f, 1f);
    private static readonly Color ArrowBlue = new(0.18f, 0.54f, 1f);
    private static readonly Color BarBackground = new(0.11f, 0.12f, 0.145f, 0.96f);
    private static readonly Color BarFill = new(0.82f, 0.08f, 0.1f, 1f);
    private static readonly Color PrimaryText = new(0.98f, 0.99f, 1f);
    private static readonly Color MutedText = new(0.68f, 0.72f, 0.78f);

    private static readonly Dictionary<string, ModuleView> ModuleViews = new(StringComparer.Ordinal);

    private static CanvasLayer? _canvas;
    private static bool _visible = true;
    private static bool _visibilityInitialized;
    private static int _selectedPlayerIndex;
    private static int _deferredLayoutQueued;
    private static DragState? _dragState;

    internal static void EnsureAttached(CanvasLayer canvas)
    {
        if (!ReferenceEquals(_canvas, canvas))
        {
            ModuleViews.Clear();
            _canvas = canvas;
            _visibilityInitialized = false;
            _dragState = null;
        }

        if (!_visibilityInitialized)
        {
            _visible = DpsMeterSettings.Current.StartVisible;
            _visibilityInitialized = true;
        }
    }

    internal static void Refresh(CanvasLayer canvas)
    {
        if (!GodotObject.IsInstanceValid(canvas))
        {
            return;
        }

        _canvas = canvas;
        if (!_visibilityInitialized)
        {
            _visible = DpsMeterSettings.Current.StartVisible;
            _visibilityInitialized = true;
        }

        var settings = DpsMeterSettings.Current;
        if (!_visible)
        {
            CancelDrag();
            foreach (var view in ModuleViews.Values)
            {
                if (GodotObject.IsInstanceValid(view.Panel))
                {
                    view.Panel.Visible = false;
                }
            }

            return;
        }

        var snapshot = DpsStats.GetSnapshot();
        if (snapshot.Players.Count > 0)
        {
            _selectedPlayerIndex = Math.Clamp(_selectedPlayerIndex, 0, snapshot.Players.Count - 1);
        }
        else
        {
            _selectedPlayerIndex = 0;
        }

        foreach (var moduleKey in DpsMeterSettings.ModuleOrder)
        {
            if (!DpsMeterSettings.IsModuleEnabled(settings, moduleKey))
            {
                if (ModuleViews.TryGetValue(moduleKey, out var disabledView) &&
                    GodotObject.IsInstanceValid(disabledView.Panel))
                {
                    disabledView.Panel.Visible = false;
                }

                continue;
            }

            var view = GetOrCreateModuleView(canvas, moduleKey);
            view.Panel.Visible = true;
            UpdateModuleView(view, settings, snapshot);
        }

        NormalizeDockRelations(settings);
        ApplyDockLayout(settings);
        QueueDeferredLayout();
    }

    internal static bool HandleGlobalInput(InputEvent @event)
    {
        if (_dragState is null)
        {
            return false;
        }

        if (@event is InputEventMouseButton mouseButton &&
            mouseButton.ButtonIndex == MouseButton.Left &&
            !mouseButton.Pressed)
        {
            CompleteDrag();
            return true;
        }

        if (@event is InputEventMouseMotion mouseMotion)
        {
            UpdateDrag(mouseMotion.GlobalPosition);
            return true;
        }

        return false;
    }

    private static ModuleView GetOrCreateModuleView(CanvasLayer canvas, string moduleKey)
    {
        if (ModuleViews.TryGetValue(moduleKey, out var existing) &&
            GodotObject.IsInstanceValid(existing.Panel))
        {
            return existing;
        }

        if (existing is not null)
        {
            ModuleViews.Remove(moduleKey);
        }

        var created = BuildModuleView(canvas, moduleKey);
        ModuleViews[moduleKey] = created;
        return created;
    }

    private static ModuleView BuildModuleView(CanvasLayer canvas, string moduleKey)
    {
        var minimumWidth = GetMinimumModuleWidth(moduleKey);
        var panel = new PanelContainer
        {
            Name = $"Sts2DpsMeterModule_{moduleKey}",
            Position = Vector2.Zero,
            Size = new Vector2(minimumWidth, MinimumModuleHeight),
            CustomMinimumSize = new Vector2(minimumWidth, 0f),
            MouseFilter = Control.MouseFilterEnum.Stop,
            TooltipText = "拖动标题区域移动；靠近其他 HUD 边缘会自动吸附。已吸附模块拖动上边框可拆分。"
        };

        var style = new StyleBoxFlat
        {
            BgColor = PanelBackground,
            BorderColor = PanelBorder,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 9,
            ContentMarginTop = 8,
            ContentMarginRight = 9,
            ContentMarginBottom = 8
        };
        panel.AddThemeStyleboxOverride("panel", style);
        panel.GuiInput += @event => OnModuleGuiInput(moduleKey, @event);
        canvas.AddChild(panel);

        var layout = new VBoxContainer
        {
            Name = "Layout",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        layout.AddThemeConstantOverride("separation", 6);
        panel.AddChild(layout);

        var header = new HBoxContainer
        {
            Name = "Header",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        header.AddThemeConstantOverride("separation", 5);
        layout.AddChild(header);

        Button? previousButton = null;
        Button? nextButton = null;
        if (IsPlayerModule(moduleKey))
        {
            previousButton = CreateModuleButton("◀", OnPreviousPlayerPressed, "上一个玩家");
            header.AddChild(previousButton);
        }

        var title = new Label
        {
            Text = GetDefaultTitle(moduleKey),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 30f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        title.AddThemeColorOverride("font_color", TitleBlue);
        title.AddThemeColorOverride("font_outline_color", Colors.Black);
        title.AddThemeConstantOverride("outline_size", 3);
        title.AddThemeFontSizeOverride("font_size", 16);
        header.AddChild(title);

        if (IsPlayerModule(moduleKey))
        {
            nextButton = CreateModuleButton("▶", OnNextPlayerPressed, "下一个玩家");
            header.AddChild(nextButton);
        }

        var settingsButton = CreateModuleButton("设", OnSettingsPressed, "打开设置");
        header.AddChild(settingsButton);

        var collapseButton = CreateModuleButton("−", () => OnCollapsePressed(moduleKey), "收起当前模块");
        header.AddChild(collapseButton);

        var divider = new ColorRect
        {
            Color = new Color(PanelBorder.R, PanelBorder.G, PanelBorder.B, 0.18f),
            CustomMinimumSize = new Vector2(0, 1),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        layout.AddChild(divider);

        var rows = new GridContainer
        {
            Name = "Rows",
            Columns = 1,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        rows.AddThemeConstantOverride("h_separation", 5);
        rows.AddThemeConstantOverride("v_separation", 5);
        layout.AddChild(rows);

        return new ModuleView(
            moduleKey,
            panel,
            style,
            title,
            rows,
            divider,
            collapseButton,
            previousButton,
            nextButton);
    }

    private static void UpdateModuleView(
        ModuleView view,
        DpsMeterSettingsData settings,
        CombatStatsSnapshot snapshot)
    {
        var layout = DpsMeterSettings.GetModuleLayout(settings, view.Key);
        view.Panel.Scale = new Vector2(settings.UiScale, settings.UiScale);
        view.Panel.PivotOffset = Vector2.Zero;
        view.Panel.ZIndex = _dragState?.Key == view.Key ? 100 : 0;
        view.Style.BgColor = new Color(
            PanelBackground.R,
            PanelBackground.G,
            PanelBackground.B,
            settings.PanelOpacity);
        view.Style.BorderColor = PanelBorder;

        view.Divider.Visible = !layout.Collapsed;
        view.Rows.Visible = !layout.Collapsed;
        view.CollapseButton.Text = layout.Collapsed ? "+" : "−";
        view.CollapseButton.TooltipText = layout.Collapsed
            ? "展开当前模块"
            : "收起当前模块";

        var data = BuildModuleViewData(view.Key, settings, snapshot);
        view.Title.Text = data.Title;
        UpdateNavigationState(view, snapshot.Players);

        ClearChildren(view.Rows);
        if (data.Sections.Count == 0)
        {
            AddDamageRow(view, settings, new DamageRowData("暂无记录", 0), 0d);
        }
        else
        {
            foreach (var section in data.Sections)
            {
                if (!string.IsNullOrWhiteSpace(section.Heading))
                {
                    AddDamageSectionHeader(view, section.Heading);
                }

                var rows = section.Rows
                    .Where(row => section.KeepZeroRows || row.Damage > 0)
                    .OrderByDescending(row => row.Damage)
                    .ThenBy(row => row.Label, StringComparer.Ordinal)
                    .Take(settings.MaxRows)
                    .ToArray();

                if (rows.Length == 0)
                {
                    AddDamageRow(view, settings, new DamageRowData("暂无记录", 0), 0d);
                    continue;
                }

                foreach (var row in rows)
                {
                    var percentage = section.Total > 0
                        ? Math.Clamp(row.Damage / (double)section.Total * 100d, 0d, 100d)
                        : 0d;
                    AddDamageRow(view, settings, row, percentage);
                }
            }
        }

        FitModuleToContent(view, settings);
    }

    private static void UpdateNavigationState(ModuleView view, IReadOnlyList<PlayerStatsSnapshot> players)
    {
        if (view.PreviousButton is null || view.NextButton is null)
        {
            return;
        }

        var canSwitch = players.Count > 1;
        view.PreviousButton.Disabled = !canSwitch;
        view.NextButton.Disabled = !canSwitch;

        var playerName = players.Count > 0
            ? players[Math.Clamp(_selectedPlayerIndex, 0, players.Count - 1)].DisplayName
            : "无";
        view.PreviousButton.TooltipText = canSwitch
            ? $"上一个玩家（当前：{playerName}）"
            : "当前仅有一个统计对象";
        view.NextButton.TooltipText = canSwitch
            ? $"下一个玩家（当前：{playerName}）"
            : "当前仅有一个统计对象";
    }

    private static DamageViewData BuildModuleViewData(
        string moduleKey,
        DpsMeterSettingsData settings,
        CombatStatsSnapshot snapshot)
    {
        if (snapshot.Players.Count == 0)
        {
            return new DamageViewData(GetDefaultTitle(moduleKey), Array.Empty<DamageSectionData>());
        }

        var player = snapshot.Players[Math.Clamp(_selectedPlayerIndex, 0, snapshot.Players.Count - 1)];
        switch (moduleKey)
        {
            case DpsMeterSettings.ModuleTurnDamage:
                {
                    var rows = BuildTurnRows(player);
                    var total = rows.Sum(row => row.Damage);
                    return new DamageViewData(
                        $"本回合伤害：{total}",
                        new[] { new DamageSectionData(null, total, rows) });
                }
            case DpsMeterSettings.ModuleCombatDamage:
                {
                    var rows = BuildTotalRows(snapshot);
                    var total = snapshot.Players.Sum(item => item.TotalDamage);
                    return new DamageViewData(
                        $"本场战斗伤害：{total}",
                        new[] { new DamageSectionData(null, total, rows) });
                }
            case DpsMeterSettings.ModuleTakenDamage:
                {
                    var rows = BuildTakenRows(player);
                    var total = player.IncomingDamage + player.SelfLossDamage;
                    return new DamageViewData(
                        $"受到伤害：{total}",
                        new[] { new DamageSectionData(null, total, rows) });
                }
            case DpsMeterSettings.ModuleMultiplayerRanking:
                return BuildMultiplayerView(settings, snapshot);
            default:
                {
                    var rows = BuildCurrentRows(player);
                    return new DamageViewData(
                        $"当前输出：{player.TotalDamage}",
                        new[] { new DamageSectionData(null, player.TotalDamage, rows) });
                }
        }
    }

    private static DamageViewData BuildMultiplayerView(
        DpsMeterSettingsData settings,
        CombatStatsSnapshot snapshot)
    {
        var dealtTotal = snapshot.Players.Sum(player => player.TotalDamage);
        var takenTotal = snapshot.Players.Sum(player => player.IncomingDamage + player.SelfLossDamage);
        var sections = new List<DamageSectionData>();

        if (settings.ShowDealtBreakdown)
        {
            sections.Add(new DamageSectionData(
                "队伍输出排行",
                dealtTotal,
                BuildMultiplayerOutputRows(snapshot),
                KeepZeroRows: true));
        }

        if (settings.ShowTakenBreakdown)
        {
            sections.Add(new DamageSectionData(
                "队伍承伤排行",
                takenTotal,
                BuildMultiplayerTakenRows(snapshot),
                KeepZeroRows: true));
        }

        var title = (settings.ShowDealtBreakdown, settings.ShowTakenBreakdown) switch
        {
            (true, true) => $"多人排行：输出 {dealtTotal} / 承伤 {takenTotal}",
            (true, false) => $"多人输出排行：{dealtTotal}",
            (false, true) => $"多人承伤排行：{takenTotal}",
            _ => "多人排行：0"
        };

        return new DamageViewData(title, sections);
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
            .GroupBy(card => card.CardId ?? card.GroupId, StringComparer.Ordinal)
            .Select(group => new DamageRowData(
                group.First().DisplayName,
                group.Sum(card => card.Damage)))
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
        ModuleView view,
        DpsMeterSettingsData settings,
        DamageRowData rowData,
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
                Text = rowData.Label,
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
            Text = rowData.Damage.ToString(),
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

        view.Rows.AddChild(row);
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

    private static void AddDamageSectionHeader(ModuleView view, string heading)
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
        view.Rows.AddChild(label);
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

    private static Button CreateModuleButton(string text, Action pressed, string tooltip)
    {
        var button = new Button
        {
            Text = text,
            Flat = true,
            CustomMinimumSize = new Vector2(26, 26),
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Stop,
            TooltipText = tooltip
        };
        button.AddThemeColorOverride("font_color", ArrowBlue);
        button.AddThemeColorOverride("font_hover_color", new Color(0.45f, 0.78f, 1f));
        button.AddThemeColorOverride("font_pressed_color", new Color(0.1f, 0.4f, 0.9f));
        button.AddThemeColorOverride("font_disabled_color", new Color(ArrowBlue.R, ArrowBlue.G, ArrowBlue.B, 0.28f));
        button.AddThemeFontSizeOverride("font_size", 16);
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

    private static void OnModuleGuiInput(string moduleKey, InputEvent @event)
    {
        if (@event is not InputEventMouseButton mouseButton ||
            mouseButton.ButtonIndex != MouseButton.Left ||
            !mouseButton.Pressed)
        {
            return;
        }

        var settings = DpsMeterSettings.Current;
        if (settings.LockPosition ||
            !ModuleViews.TryGetValue(moduleKey, out var view))
        {
            return;
        }

        var layout = DpsMeterSettings.GetModuleLayout(settings, moduleKey);
        var attached = IsAttachedToVisibleModule(settings, layout);
        var onTitle = IsTitleDragArea(view, mouseButton.GlobalPosition);
        var onDetachBorder = attached && mouseButton.Position.Y <= DetachHandleHeight;
        if (!onTitle && !onDetachBorder)
        {
            return;
        }

        TryBeginDrag(moduleKey, mouseButton.GlobalPosition);
    }

    private static bool IsTitleDragArea(ModuleView view, Vector2 globalPosition)
    {
        return GodotObject.IsInstanceValid(view.Title) &&
               view.Title.GetGlobalRect().Grow(2f).HasPoint(globalPosition);
    }

    private static bool TryBeginDrag(string moduleKey, Vector2 globalPosition)
    {
        if (!ModuleViews.TryGetValue(moduleKey, out var view) ||
            !GodotObject.IsInstanceValid(view.Panel))
        {
            return false;
        }

        var settings = DpsMeterSettings.Current;
        var layout = DpsMeterSettings.GetModuleLayout(settings, moduleKey);
        if (IsAttachedToVisibleModule(settings, layout))
        {
            layout.X = view.Panel.Position.X;
            layout.Y = view.Panel.Position.Y;
            layout.AttachTo = null;
            layout.DockSide = DpsMeterSettings.DockSideNone;
        }

        var movingKeys = CollectSubtree(moduleKey);
        _dragState = new DragState(
            moduleKey,
            globalPosition - view.Panel.Position,
            movingKeys);
        view.Panel.ZIndex = 100;
        return true;
    }

    private static void UpdateDrag(Vector2 globalPosition)
    {
        if (_dragState is null ||
            !ModuleViews.TryGetValue(_dragState.Key, out var view) ||
            !GodotObject.IsInstanceValid(view.Panel))
        {
            return;
        }

        var settings = DpsMeterSettings.Current;
        var layout = DpsMeterSettings.GetModuleLayout(settings, _dragState.Key);
        var proposed = globalPosition - _dragState.GrabOffset;
        layout.AttachTo = null;
        layout.DockSide = DpsMeterSettings.DockSideNone;
        layout.X = proposed.X;
        layout.Y = proposed.Y;
        ApplyDockLayout(settings);
    }

    private static void CompleteDrag()
    {
        var drag = _dragState;
        _dragState = null;
        if (drag is null)
        {
            return;
        }

        var settings = DpsMeterSettings.Current;
        if (!ModuleViews.TryGetValue(drag.Key, out var view) ||
            !GodotObject.IsInstanceValid(view.Panel))
        {
            return;
        }

        view.Panel.ZIndex = 0;
        var layout = DpsMeterSettings.GetModuleLayout(settings, drag.Key);
        layout.X = view.Panel.Position.X;
        layout.Y = view.Panel.Position.Y;

        if (settings.EnableSnapping &&
            TryFindSnapCandidate(settings, drag.Key, drag.MovingKeys, out var snap))
        {
            layout.AttachTo = snap.TargetKey;
            layout.DockSide = snap.Side;
        }
        else
        {
            layout.AttachTo = null;
            layout.DockSide = DpsMeterSettings.DockSideNone;
        }

        ApplyDockLayout(settings);
        DpsMeterSettings.Save();
        DpsMeterSettingsUi.Refresh();
        QueueDeferredLayout();
    }

    private static void CancelDrag()
    {
        if (_dragState is null)
        {
            return;
        }

        if (ModuleViews.TryGetValue(_dragState.Key, out var view) &&
            GodotObject.IsInstanceValid(view.Panel))
        {
            view.Panel.ZIndex = 0;
        }

        _dragState = null;
    }

    private static HashSet<string> CollectSubtree(string rootKey)
    {
        var settings = DpsMeterSettings.Current;
        var result = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(rootKey);
        result.Add(rootKey);

        while (queue.Count > 0)
        {
            var parentKey = queue.Dequeue();
            foreach (var moduleKey in DpsMeterSettings.ModuleOrder)
            {
                if (result.Contains(moduleKey))
                {
                    continue;
                }

                var layout = DpsMeterSettings.GetModuleLayout(settings, moduleKey);
                if (string.Equals(layout.AttachTo, parentKey, StringComparison.Ordinal))
                {
                    result.Add(moduleKey);
                    queue.Enqueue(moduleKey);
                }
            }
        }

        return result;
    }

    private static bool TryFindSnapCandidate(
        DpsMeterSettingsData settings,
        string draggedKey,
        HashSet<string> movingKeys,
        out SnapResult result)
    {
        result = default!;
        if (!ModuleViews.TryGetValue(draggedKey, out var draggedView) ||
            !GodotObject.IsInstanceValid(draggedView.Panel))
        {
            return false;
        }

        var draggedRect = GetVisualRect(draggedView.Panel, settings.UiScale);
        var bestScore = float.MaxValue;
        SnapResult? best = null;

        foreach (var candidateKey in DpsMeterSettings.ModuleOrder)
        {
            if (movingKeys.Contains(candidateKey) ||
                !DpsMeterSettings.IsModuleEnabled(settings, candidateKey) ||
                !ModuleViews.TryGetValue(candidateKey, out var candidateView) ||
                !GodotObject.IsInstanceValid(candidateView.Panel) ||
                !candidateView.Panel.Visible)
            {
                continue;
            }

            var candidateRect = GetVisualRect(candidateView.Panel, settings.UiScale);
            var overlapX = Overlap1D(
                draggedRect.Position.X,
                draggedRect.Position.X + draggedRect.Size.X,
                candidateRect.Position.X,
                candidateRect.Position.X + candidateRect.Size.X);
            var overlapY = Overlap1D(
                draggedRect.Position.Y,
                draggedRect.Position.Y + draggedRect.Size.Y,
                candidateRect.Position.Y,
                candidateRect.Position.Y + candidateRect.Size.Y);
            var centerX = draggedRect.Position.X + draggedRect.Size.X * 0.5f;
            var candidateCenterX = candidateRect.Position.X + candidateRect.Size.X * 0.5f;
            var centerY = draggedRect.Position.Y + draggedRect.Size.Y * 0.5f;
            var candidateCenterY = candidateRect.Position.Y + candidateRect.Size.Y * 0.5f;

            ConsiderSnap(
                candidateKey,
                DpsMeterSettings.DockSideBottom,
                Math.Abs(draggedRect.Position.Y - (candidateRect.Position.Y + candidateRect.Size.Y)),
                overlapX,
                Math.Abs(centerX - candidateCenterX));
            ConsiderSnap(
                candidateKey,
                DpsMeterSettings.DockSideTop,
                Math.Abs(candidateRect.Position.Y - (draggedRect.Position.Y + draggedRect.Size.Y)),
                overlapX,
                Math.Abs(centerX - candidateCenterX));
            ConsiderSnap(
                candidateKey,
                DpsMeterSettings.DockSideRight,
                Math.Abs(draggedRect.Position.X - (candidateRect.Position.X + candidateRect.Size.X)),
                overlapY,
                Math.Abs(centerY - candidateCenterY));
            ConsiderSnap(
                candidateKey,
                DpsMeterSettings.DockSideLeft,
                Math.Abs(candidateRect.Position.X - (draggedRect.Position.X + draggedRect.Size.X)),
                overlapY,
                Math.Abs(centerY - candidateCenterY));
        }

        if (best is null)
        {
            return false;
        }

        result = best;
        return true;

        void ConsiderSnap(string targetKey, string side, float gap, float overlap, float alignment)
        {
            if (gap > SnapDistance || (overlap < 24f && alignment > 60f))
            {
                return;
            }

            var score = gap + alignment * 0.05f;
            if (score >= bestScore)
            {
                return;
            }

            bestScore = score;
            best = new SnapResult(targetKey, side);
        }
    }

    private static void NormalizeDockRelations(DpsMeterSettingsData settings)
    {
        foreach (var moduleKey in DpsMeterSettings.ModuleOrder)
        {
            var layout = DpsMeterSettings.GetModuleLayout(settings, moduleKey);
            if (string.IsNullOrWhiteSpace(layout.AttachTo))
            {
                layout.AttachTo = null;
                layout.DockSide = DpsMeterSettings.DockSideNone;
                continue;
            }

            var attachedKey = layout.AttachTo!;
            var attachedToVisibleModule =
                !string.Equals(attachedKey, moduleKey, StringComparison.Ordinal) &&
                DpsMeterSettings.IsModuleEnabled(settings, attachedKey) &&
                ModuleViews.TryGetValue(attachedKey, out var attachedView) &&
                GodotObject.IsInstanceValid(attachedView.Panel) &&
                attachedView.Panel.Visible;

            if (attachedToVisibleModule)
            {
                layout.DockSide = NormalizeDockSide(layout.DockSide);
                continue;
            }

            if (ModuleViews.TryGetValue(moduleKey, out var view) &&
                GodotObject.IsInstanceValid(view.Panel))
            {
                layout.X = view.Panel.Position.X;
                layout.Y = view.Panel.Position.Y;
            }

            layout.AttachTo = null;
            layout.DockSide = DpsMeterSettings.DockSideNone;
        }
    }

    private static void ApplyDockLayout(DpsMeterSettingsData settings)
    {
        var resolved = new Dictionary<string, Vector2>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        foreach (var moduleKey in DpsMeterSettings.ModuleOrder)
        {
            if (DpsMeterSettings.IsModuleEnabled(settings, moduleKey))
            {
                ResolvePosition(moduleKey);
            }
        }

        Vector2 ResolvePosition(string moduleKey)
        {
            if (resolved.TryGetValue(moduleKey, out var existingPosition))
            {
                return existingPosition;
            }

            if (!ModuleViews.TryGetValue(moduleKey, out var view) ||
                !GodotObject.IsInstanceValid(view.Panel) ||
                !view.Panel.Visible)
            {
                return GetModuleLayoutPosition(settings, moduleKey);
            }

            if (!visiting.Add(moduleKey))
            {
                return view.Panel.Position;
            }

            var layout = DpsMeterSettings.GetModuleLayout(settings, moduleKey);
            Vector2 position;
            if (!string.IsNullOrWhiteSpace(layout.AttachTo) &&
                DpsMeterSettings.IsModuleEnabled(settings, layout.AttachTo!) &&
                ModuleViews.TryGetValue(layout.AttachTo!, out var parentView) &&
                GodotObject.IsInstanceValid(parentView.Panel) &&
                parentView.Panel.Visible)
            {
                var parentPosition = ResolvePosition(layout.AttachTo!);
                var parentSize = GetVisualSize(parentView.Panel, settings.UiScale);
                var childSize = GetVisualSize(view.Panel, settings.UiScale);
                position = layout.DockSide switch
                {
                    DpsMeterSettings.DockSideTop => parentPosition - new Vector2(0f, childSize.Y),
                    DpsMeterSettings.DockSideLeft => parentPosition - new Vector2(childSize.X, 0f),
                    DpsMeterSettings.DockSideRight => parentPosition + new Vector2(parentSize.X, 0f),
                    _ => parentPosition + new Vector2(0f, parentSize.Y)
                };
            }
            else
            {
                position = ClampRootPosition(
                    view.Panel,
                    new Vector2(layout.X, layout.Y),
                    settings.UiScale);
            }

            view.Panel.Position = position;
            layout.X = position.X;
            layout.Y = position.Y;
            resolved[moduleKey] = position;
            visiting.Remove(moduleKey);
            return position;
        }
    }

    private static Vector2 GetModuleLayoutPosition(DpsMeterSettingsData settings, string moduleKey)
    {
        var layout = DpsMeterSettings.GetModuleLayout(settings, moduleKey);
        return new Vector2(layout.X, layout.Y);
    }

    private static Vector2 ClampRootPosition(Control panel, Vector2 position, float scale)
    {
        var viewportSize = GetViewportSize();
        var scaledSize = GetVisualSize(panel, scale);
        return new Vector2(
            Mathf.Clamp(position.X, 0f, Mathf.Max(0f, viewportSize.X - scaledSize.X - ViewportMargin)),
            Mathf.Clamp(position.Y, 0f, Mathf.Max(0f, viewportSize.Y - scaledSize.Y - ViewportMargin)));
    }

    private static void FitModuleToContent(ModuleView view, DpsMeterSettingsData settings)
    {
        if (!GodotObject.IsInstanceValid(view.Panel))
        {
            return;
        }

        var minimumWidth = GetMinimumModuleWidth(view.Key);
        view.Panel.CustomMinimumSize = new Vector2(minimumWidth, 0f);
        var minimum = view.Panel.GetCombinedMinimumSize();
        var viewportSize = GetViewportSize();
        var width = Mathf.Clamp(minimum.X, minimumWidth, Mathf.Max(minimumWidth, viewportSize.X - ViewportMargin * 2f));
        var height = Mathf.Clamp(minimum.Y, MinimumModuleHeight, Mathf.Max(MinimumModuleHeight, viewportSize.Y - ViewportMargin * 2f));
        view.Panel.Size = new Vector2(width, height);
    }

    private static void QueueDeferredLayout()
    {
        if (!GodotObject.IsInstanceValid(_canvas) ||
            Interlocked.Exchange(ref _deferredLayoutQueued, 1) == 1)
        {
            return;
        }

        try
        {
            Callable.From(FlushDeferredLayout).CallDeferred();
        }
        catch
        {
            Interlocked.Exchange(ref _deferredLayoutQueued, 0);
        }
    }

    private static void FlushDeferredLayout()
    {
        Interlocked.Exchange(ref _deferredLayoutQueued, 0);
        var settings = DpsMeterSettings.Current;
        foreach (var moduleKey in DpsMeterSettings.ModuleOrder)
        {
            if (DpsMeterSettings.IsModuleEnabled(settings, moduleKey) &&
                ModuleViews.TryGetValue(moduleKey, out var view) &&
                GodotObject.IsInstanceValid(view.Panel))
            {
                FitModuleToContent(view, settings);
            }
        }

        NormalizeDockRelations(settings);
        ApplyDockLayout(settings);
    }

    private static bool IsAttachedToVisibleModule(
        DpsMeterSettingsData settings,
        HudModuleLayoutData layout)
    {
        if (string.IsNullOrWhiteSpace(layout.AttachTo))
        {
            return false;
        }

        return DpsMeterSettings.IsModuleEnabled(settings, layout.AttachTo!) &&
               ModuleViews.TryGetValue(layout.AttachTo!, out var parentView) &&
               GodotObject.IsInstanceValid(parentView.Panel) &&
               parentView.Panel.Visible;
    }

    private static string NormalizeDockSide(string? value)
    {
        return value switch
        {
            DpsMeterSettings.DockSideTop => DpsMeterSettings.DockSideTop,
            DpsMeterSettings.DockSideBottom => DpsMeterSettings.DockSideBottom,
            DpsMeterSettings.DockSideLeft => DpsMeterSettings.DockSideLeft,
            DpsMeterSettings.DockSideRight => DpsMeterSettings.DockSideRight,
            _ => DpsMeterSettings.DockSideNone
        };
    }

    private static float Overlap1D(float startA, float endA, float startB, float endB)
    {
        return Math.Max(0f, Math.Min(endA, endB) - Math.Max(startA, startB));
    }

    private static Rect2 GetVisualRect(Control panel, float scale)
    {
        return new Rect2(panel.Position, GetVisualSize(panel, scale));
    }

    private static Vector2 GetVisualSize(Control panel, float scale)
    {
        return panel.Size * scale;
    }

    private static float GetMinimumModuleWidth(string moduleKey)
    {
        return moduleKey == DpsMeterSettings.ModuleMultiplayerRanking
            ? MultiplayerMinimumWidth
            : MinimumModuleWidth;
    }

    private static string GetDefaultTitle(string moduleKey)
    {
        return moduleKey switch
        {
            DpsMeterSettings.ModuleTurnDamage => "本回合伤害：0",
            DpsMeterSettings.ModuleCombatDamage => "本场战斗伤害：0",
            DpsMeterSettings.ModuleTakenDamage => "受到伤害：0",
            DpsMeterSettings.ModuleMultiplayerRanking => "多人排行：0",
            _ => "当前输出：0"
        };
    }

    private static bool IsPlayerModule(string moduleKey)
    {
        return moduleKey == DpsMeterSettings.ModuleCurrentOutput ||
               moduleKey == DpsMeterSettings.ModuleTurnDamage ||
               moduleKey == DpsMeterSettings.ModuleTakenDamage;
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
        RefreshCurrentCanvas();
    }

    private static void OnCollapsePressed(string moduleKey)
    {
        var settings = DpsMeterSettings.Current;
        var layout = DpsMeterSettings.GetModuleLayout(settings, moduleKey);
        layout.Collapsed = !layout.Collapsed;
        DpsMeterSettings.Save();
        RefreshCurrentCanvas();
    }

    private static void OnSettingsPressed()
    {
        DpsMeterSettingsUi.ToggleVisibility();
    }

    private static void RefreshCurrentCanvas()
    {
        if (GodotObject.IsInstanceValid(_canvas))
        {
            Refresh(_canvas!);
        }
    }

    private static Vector2 GetViewportSize()
    {
        var viewport = _canvas?.GetViewport();
        return viewport?.GetVisibleRect().Size ?? new Vector2(1280f, 720f);
    }

    private static void ClearChildren(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    private sealed class ModuleView
    {
        public ModuleView(
            string key,
            PanelContainer panel,
            StyleBoxFlat style,
            Label title,
            GridContainer rows,
            ColorRect divider,
            Button collapseButton,
            Button? previousButton,
            Button? nextButton)
        {
            Key = key;
            Panel = panel;
            Style = style;
            Title = title;
            Rows = rows;
            Divider = divider;
            CollapseButton = collapseButton;
            PreviousButton = previousButton;
            NextButton = nextButton;
        }

        public string Key { get; }
        public PanelContainer Panel { get; }
        public StyleBoxFlat Style { get; }
        public Label Title { get; }
        public GridContainer Rows { get; }
        public ColorRect Divider { get; }
        public Button CollapseButton { get; }
        public Button? PreviousButton { get; }
        public Button? NextButton { get; }
    }

    private sealed record DragState(
        string Key,
        Vector2 GrabOffset,
        HashSet<string> MovingKeys);

    private sealed record SnapResult(
        string TargetKey,
        string Side);

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
