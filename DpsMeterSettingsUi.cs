using System;
using System.Collections.Generic;
using Godot;

namespace Sts2DpsMeter;

public static class DpsMeterSettingsUi
{
    private static readonly Color SettingsBackground = new(0.035f, 0.045f, 0.07f, 0.97f);
    private static readonly Color SettingsBorder = new(0.32f, 0.72f, 1f, 0.95f);
    private static readonly Color HeadingColor = new(0.56f, 0.86f, 1f);
    private static readonly Color MutedColor = new(0.82f, 0.85f, 0.9f);
    private static readonly Color BodyColor = Colors.White;

    private static PanelContainer? _panel;
    private static VBoxContainer? _content;
    private static Label? _titleLabel;
    private static Label? _introductionLabel;
    private static bool _visible;

    public static bool IsVisible => _visible;

    internal static bool CoversMouse() => IsAlive(_panel) && _panel!.IsVisibleInTree() &&
        new Rect2(Vector2.Zero, _panel.Size).HasPoint(_panel.GetLocalMousePosition());

    public static void EnsureAttached(CanvasLayer canvas)
    {
        DpsLocalization.EnsureSubscribed();

        if (IsAlive(_panel))
        {
            return;
        }

        try
        {
            BuildPanel(canvas);
        }
        catch (Exception exception)
        {
            GD.PushError($"[{ModEntry.ModId}] 创建设置面板失败：{exception}");
        }
    }

    public static void ToggleVisibility()
    {
        if (!IsAlive(_panel))
        {
            return;
        }

        _visible = !_visible;
        _panel!.Visible = _visible;
        if (_visible)
        {
            Refresh();
        }

        GD.Print($"[{ModEntry.ModId}] 设置面板：{(_visible ? "显示" : "隐藏")}");
    }

    public static void Refresh()
    {
        if (!IsAlive(_content))
        {
            return;
        }

        UpdateLocalizedHeader();
        RebuildContent();
        _panel!.Visible = _visible;
    }

    private static void BuildPanel(CanvasLayer canvas)
    {
        _panel = new PanelContainer
        {
            Name = "Sts2DpsMeterSettingsPanel",
            Position = new Vector2(400, 45),
            Size = new Vector2(485, 650),
            CustomMinimumSize = new Vector2(485, 650),
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        DpsHudTypography.Apply(_panel);

        var style = new StyleBoxFlat
        {
            BgColor = SettingsBackground,
            BorderColor = SettingsBorder,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 7,
            CornerRadiusTopRight = 7,
            CornerRadiusBottomLeft = 7,
            CornerRadiusBottomRight = 7,
            ContentMarginLeft = 12,
            ContentMarginTop = 10,
            ContentMarginRight = 12,
            ContentMarginBottom = 10
        };
        _panel.AddThemeStyleboxOverride("panel", style);
        canvas.AddChild(_panel);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
        _panel.AddChild(root);

        _titleLabel = new Label
        {
            Text = DpsLocalization.Get("Sts2 DPS Meter 设置", "Sts2 DPS Meter Settings"),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _titleLabel.AddThemeColorOverride("font_color", HeadingColor);
        _titleLabel.AddThemeFontSizeOverride("font_size", DpsHudTypography.SettingsTitleSize);
        root.AddChild(_titleLabel);

        _introductionLabel = new Label
        {
            Text = DpsLocalization.Get("伤害发生后于下一帧刷新，同帧请求合并处理。点击 HUD 的齿轮（⚙）按钮打开或关闭此面板；单人范围为本场战斗、本回合；多人增加全队，显示当前战斗的队伍汇总和玩家排行。", "Refreshes on the next frame after damage, combining requests from the same frame. Use the HUD gear (⚙) to open or close this panel. Single-player scopes are This Combat and This Turn. Multiplayer adds Team for current-combat summaries and player rankings."),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _introductionLabel.AddThemeColorOverride("font_color", MutedColor);
        _introductionLabel.AddThemeFontSizeOverride("font_size", DpsHudTypography.SettingsNoteSize);
        root.AddChild(_introductionLabel);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
            // 高度由面板剩余空间分配，较大的说明字号不再叠加固定 520px 明细下限。
            CustomMinimumSize = new Vector2(455, 0)
        };
        root.AddChild(scroll);

        _content = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(440, 0)
        };
        _content.AddThemeConstantOverride("separation", 7);
        scroll.AddChild(_content);
    }

    private static void UpdateLocalizedHeader()
    {
        if (IsAlive(_titleLabel))
        {
            _titleLabel!.Text = DpsLocalization.Get("Sts2 DPS Meter 设置", "Sts2 DPS Meter Settings");
        }

        if (IsAlive(_introductionLabel))
        {
            _introductionLabel!.Text = DpsLocalization.Get(
                "伤害发生后于下一帧刷新，同帧请求合并处理。点击 HUD 的齿轮（⚙）按钮打开或关闭此面板；单人范围为本场战斗、本回合；多人增加全队，显示当前战斗的队伍汇总和玩家排行。",
                "Refreshes on the next frame after damage, combining requests from the same frame. Use the HUD gear (⚙) to open or close this panel. Single-player scopes are This Combat and This Turn. Multiplayer adds Team for current-combat summaries and player rankings.");
        }
    }

    private static void RebuildContent()
    {
        ClearChildren(_content!);
        var settings = DpsMeterSettings.Current;

        AddSection(DpsLocalization.Get("显示与行为", "Display & Behavior"));
        AddToggle(DpsLocalization.Get("启动时显示 HUD", "Start with HUD visible"), settings.StartVisible, value =>
        {
            settings.StartVisible = value;
            SaveAndRefresh();
        });
        AddToggle(DpsLocalization.Get("锁定 HUD 位置", "Lock HUD position"), settings.LockPosition, value =>
        {
            settings.LockPosition = value;
            SaveAndRefresh();
        });
        AddToggle(DpsLocalization.Get("紧凑模式", "Compact mode"), settings.CompactMode, value =>
        {
            settings.CompactMode = value;
            SaveAndRefresh();
        });

        AddSection(DpsLocalization.Get("多人视图", "Multiplayer View"));
        AddOptionRow(DpsLocalization.Get("多人默认视图", "Default multiplayer view"),
            new[] { HudPlayerSelection.Label(HudPlayerView.CurrentPlayer, DpsLocalization.IsChinese),
                HudPlayerSelection.Label(HudPlayerView.Self, DpsLocalization.IsChinese), HudPlayerSelection.Label(HudPlayerView.Team, DpsLocalization.IsChinese) },
            (int)settings.MultiplayerDefaultView, value =>
            {
                settings.MultiplayerDefaultView = (HudPlayerView)value;
                SaveAndRefresh();
            });
        AddText(DpsLocalization.Get(
            "默认视图在进入新多人战斗时应用；当前战斗可用 HUD 快捷框切换当前玩家、自己、全队。左右箭头会切到当前玩家视图；全队时箭头禁用。自己按本地身份识别，未识别时等待，不显示其他玩家冒充自己。单人不显示快捷框。",
            "The default applies when entering a new multiplayer combat. Use the HUD selector for Player, Self, or Team in the current combat. Arrows switch to Player view and are disabled in Team. Self waits for local-player identification instead of showing another player. The selector is hidden in single-player."), MutedColor);

        AddSection(DpsLocalization.Get("标题摘要", "Title Summary"));
        AddSummaryToggle(DpsLocalization.Get("摘要：总伤害", "Summary: damage"), settings.SummaryDamage, value => settings.SummaryDamage = value);
        AddSummaryToggle(DpsLocalization.Get("摘要：DPS", "Summary: DPS"), settings.SummaryDps, value => settings.SummaryDps = value);
        AddSummaryToggle(DpsLocalization.Get("摘要：DPT", "Summary: DPT"), settings.SummaryDpt, value => settings.SummaryDpt = value);
        AddSummaryToggle(DpsLocalization.Get("摘要：承伤", "Summary: damage taken"), settings.SummaryTaken, value => settings.SummaryTaken = value);
        AddSummaryToggle(DpsLocalization.Get("摘要：回合数", "Summary: turn number"), settings.SummaryTurn, value => settings.SummaryTurn = value);
        AddSummaryToggle(DpsLocalization.Get("摘要：命中次数", "Summary: hits"), settings.SummaryHits, value => settings.SummaryHits = value);
        AddText(DpsLocalization.Get(
            "最多同时选择三项，需先取消一项再选其他项；全关只显示范围。摘要独立于统计模块，标题过长可悬停查看全文。承伤含敌方伤害和自损。回合数为当前战斗回合号；本回合 DPS 使用本回合耗时，DPT 为本回合伤害。全队按当前战斗合计计算。",
            "Select up to three; deselect one before adding another. None shows only the scope. Summary is independent of modules; hover a clipped title for full text. Taken includes enemy damage and self-loss. Turn is the current combat round; Turn DPS uses time in that round, and Turn DPT equals its damage. Team uses current-combat totals."), MutedColor);

        AddSection(DpsLocalization.Get("统计模块", "Statistics Modules"));
        AddToggle(DpsLocalization.Get("玩家造成伤害", "Damage Dealt"), settings.ShowDealtBreakdown, value =>
        {
            settings.ShowDealtBreakdown = value;
            SaveAndRefresh();
        });
        AddToggle(DpsLocalization.Get("玩家受到伤害", "Damage Taken"), settings.ShowTakenBreakdown, value =>
        {
            settings.ShowTakenBreakdown = value;
            SaveAndRefresh();
        });
        AddToggle(DpsLocalization.Get("格挡防御", "Block Gained"), settings.ShowBlockGained, value =>
        {
            settings.ShowBlockGained = value;
            SaveAndRefresh();
        });
        AddToggle(DpsLocalization.Get("能量使用", "Energy Spent"), settings.ShowEnergySpent, value =>
        {
            settings.ShowEnergySpent = value;
            SaveAndRefresh();
        });
        AddToggle(DpsLocalization.Get("给队友能量", "Ally Energy Given"), settings.ShowEnergyGivenToAllies, value =>
        {
            settings.ShowEnergyGivenToAllies = value;
            SaveAndRefresh();
        });
        AddToggle(DpsLocalization.Get("给队友护盾", "Ally Block Given"), settings.ShowBlockGivenToAllies, value =>
        {
            settings.ShowBlockGivenToAllies = value;
            SaveAndRefresh();
        });
        AddToggle(DpsLocalization.Get("给队友回血", "Ally Healing Given"), settings.ShowHealingGivenToAllies, value =>
        {
            settings.ShowHealingGivenToAllies = value;
            SaveAndRefresh();
        });
        AddToggle(DpsLocalization.Get("给队友抽牌", "Cards Drawn for Allies"), settings.ShowCardsDrawnForAllies, value =>
        {
            settings.ShowCardsDrawnForAllies = value;
            SaveAndRefresh();
        });
        AddText(DpsLocalization.Get("队友增益仅在多人模式显示；单人模式下这些开关不会新增 HUD 模块。", "Ally support is shown only in multiplayer; these toggles do not add HUD modules in single-player."), MutedColor);
        AddText(DpsLocalization.Get("可在下方勾选需要显示的模块；统计范围仅切换数据的时间维度，不改变模块开关。", "Select the modules to display below; scope changes only the time range, not the module toggles."), MutedColor);

        AddSection(DpsLocalization.Get("数据行", "Data Rows"));
        AddOptionRow(DpsLocalization.Get("排序方式", "Sort by"), new[]
        {
            DpsLocalization.Get("总伤害/数值", "Damage / Value"), DpsLocalization.Get("百分比", "Percentage"),
            DpsLocalization.Get("命中次数", "Hits"), DpsLocalization.Get("最大伤害", "Max hit"),
            DpsLocalization.Get("名称", "Name"), DpsLocalization.Get("最近触发", "Most recent")
        }, (int)settings.SortMode, value =>
        {
            settings.SortMode = (HudSortMode)value;
            SaveAndRefresh();
        });
        AddText(DpsLocalization.Get(
            "名称按字符顺序升序，其余降序；同值按名称保持稳定顺序，先排序再限制显示行数。缺失的命中/最大/触发记录排在已有记录之后；辅助指标没有对应记录时按名称排序。百分比分母是整个模块总量，不因截断行数改变。",
            "Name sorts ascending by character order; others descending. Ties keep a stable name order; sorting happens before the row limit. Missing hit/max/trigger records follow known records; support metrics without them use name order. Percentages use the full module total, including hidden rows."), MutedColor);
        AddToggle(DpsLocalization.Get("显示来源图标", "Show source icons"), settings.ShowSourceIcons, value =>
        {
            settings.ShowSourceIcons = value;
            SaveAndRefresh();
        });
        AddOptionRow(DpsLocalization.Get("悬停提示详细度", "Tooltip detail"),
            new[] { DpsLocalization.Get("简洁", "Simple"), DpsLocalization.Get("详细", "Detailed"), DpsLocalization.Get("开发者", "Developer") },
            (int)settings.TooltipDetailMode, value =>
        {
            settings.TooltipDetailMode = (HudTooltipDetail)value;
            SaveAndRefresh();
        });
        AddToggle(DpsLocalization.Get("点击行展开详情", "Click rows for details"), settings.EnableRowDetails, value =>
        {
            settings.EnableRowDetails = value;
            SaveAndRefresh();
        });
        AddToggle(DpsLocalization.Get("显示名称", "Show name"), settings.ShowFieldName, value => SetField(() => settings.ShowFieldName = value));
        AddToggle(DpsLocalization.Get("显示数值", "Show value"), settings.ShowFieldDamage, value => SetField(() => settings.ShowFieldDamage = value));
        AddToggle(DpsLocalization.Get("显示百分比", "Show percentage"), settings.ShowFieldPercentage, value => SetField(() => settings.ShowFieldPercentage = value));
        AddToggle(DpsLocalization.Get("显示命中次数", "Show hits"), settings.ShowFieldHits, value => SetField(() => settings.ShowFieldHits = value));
        AddToggle(DpsLocalization.Get("显示最大伤害", "Show max hit"), settings.ShowFieldMaxHit, value => SetField(() => settings.ShowFieldMaxHit = value));
        AddToggle(DpsLocalization.Get("显示平均伤害", "Show average"), settings.ShowFieldAverage, value => SetField(() => settings.ShowFieldAverage = value));
        AddText(DpsLocalization.Get(
            "隐藏名称时保留行号，悬停可查看完整名称；字段全部关闭时保留名称。命中/最大/平均仅在有对应记录时显示。点击来源行查看详情，拖动标题栏移动 HUD。",
            "Hidden names use row numbers; hover to identify each row. At least the name is retained when all fields are off. Hits/max/average require matching records. Click rows for details; drag the header to move the HUD."), MutedColor);

        AddSection(DpsLocalization.Get("布局与密度", "Layout & Density"));
        AddSliderRow(DpsLocalization.Get("伤害来源显示行数", "Damage source rows"), settings.MaxRows, 1, 12, 1, value =>
        {
            settings.MaxRows = Math.Clamp((int)value, 1, 12);
        });

        AddSliderRow(DpsLocalization.Get("面板透明度", "Panel opacity"), settings.PanelOpacity, 0.35, 1.0, 0.01, value =>
        {
            settings.PanelOpacity = Mathf.Clamp((float)value, 0.35f, 1f);
        });
        AddSliderRow(
            DpsLocalization.Get("界面缩放", "UI scale"),
            settings.UiScale,
            DpsMeterSettings.MinUiScale,
            DpsMeterSettings.MaxUiScale,
            DpsMeterSettings.UiScaleStep,
            value =>
        {
            settings.UiScale = DpsMeterSettings.ClampUiScale((float)value);
        });
        AddText(DpsLocalization.Get("界面缩放仅通过上方滑块调整，不注册键盘或鼠标滚轮热键。", "UI scale is adjusted only by the slider above; no keyboard or mouse-wheel hotkeys are registered."), MutedColor);

        var resetScale = new Button
        {
            FocusMode = Control.FocusModeEnum.None,
            Text = DpsLocalization.Get("重置 HUD 大小（100%）", "Reset HUD Size (100%)")
        };
        resetScale.Pressed += () =>
        {
            settings.UiScale = 1f;
            SaveAndRefresh();
        };
        _content!.AddChild(resetScale);

        AddSection(DpsLocalization.Get("位置", "Position"));
        AddToggle(DpsLocalization.Get("屏幕边缘磁吸", "Snap to screen edges"), settings.SnapEnabled, value =>
        {
            settings.SnapEnabled = value;
            SaveAndRefresh();
        });
        AddSliderRow(DpsLocalization.Get("吸附距离", "Snap distance"), settings.SnapDistance, 4, 80, 1, value => settings.SnapDistance = (float)value);
        AddSliderRow(DpsLocalization.Get("屏幕留白", "Screen margin"), settings.ScreenMargin, 0, 64, 1, value => settings.ScreenMargin = (float)value);
        AddText(DpsLocalization.Get(
            "拖动标题栏时蓝框预览吸附位置，松开后吸附并保存；四角及四边均可吸附。距离和留白使用视口像素，不随 HUD 缩放倍增；磁吸关闭时不保留屏幕留白。已吸附边缘随缩放或分辨率变化重新对齐。",
            "Drag the header to preview the drop position with a blue outline; release to snap and save. All four edges and corners are supported. Distance and margin use viewport pixels, independent of HUD scale; disabling snapping also disables the margin. Docked edges realign after scale or resolution changes."), MutedColor);
        var resetPosition = new Button
        {
            FocusMode = Control.FocusModeEnum.None,
            Text = DpsLocalization.Get("重置 HUD 位置到左上角", "Reset HUD Position to Top-Left"),
            TooltipText = DpsLocalization.Get("默认位置为 (18, 18)；HUD 未锁定时可直接拖动。", "Default position is (18, 18); drag the HUD directly when it is unlocked.")
        };
        resetPosition.Pressed += () =>
        {
            settings.PositionX = 18f;
            settings.PositionY = 18f;
            settings.SnapAnchor = HudSnapAnchor.None;
            SaveAndRefresh();
        };
        _content!.AddChild(resetPosition);

        AddSection(DpsLocalization.Get("战斗低干扰", "Combat visibility"));
        AddOptionRow(DpsLocalization.Get("低干扰模式", "Low-interference mode"),
            new[] { DpsLocalization.Get("关闭", "Off"), DpsLocalization.Get("降低透明度", "Fade"), DpsLocalization.Get("自动折叠", "Auto-collapse") },
            (int)settings.LowInterferenceMode, value =>
            {
                settings.LowInterferenceMode = (HudLowInterferenceMode)value;
                SaveAndRefresh();
            });
        AddSliderRow(DpsLocalization.Get("淡化后不透明度", "Faded HUD opacity"), settings.LowInterferenceOpacity,
            0.25, 0.4, 0.01, value => settings.LowInterferenceOpacity = (float)value);
        AddText(DpsLocalization.Get(
            "默认关闭。战斗中鼠标离开 HUD 约 1 秒后淡化或折叠；淡出和移回恢复均约 0.18 秒渐变。打开设置、拖动或战斗结束也会恢复。淡化影响整个 HUD，不覆盖原面板透明度；自动折叠保留标题和按钮，不改变手动折叠状态。",
            "Off by default. During combat, leaving the HUD for about one second fades or collapses it. Fading out and restoring on hover both take about 0.18 seconds. Opening Settings, dragging, or ending combat also restores it. Fading affects the whole HUD without changing the saved panel opacity. Auto-collapse keeps the header and controls and preserves manual collapse."), MutedColor);

        AddSection(DpsLocalization.Get("战斗结束", "Combat end"));
        AddToggle(DpsLocalization.Get("显示战斗总结", "Show combat summary"), settings.ShowCombatSummary, value =>
        {
            settings.ShowCombatSummary = value;
            SaveAndRefresh();
        });
        AddSliderRow(DpsLocalization.Get("总结持续时间（秒）", "Summary duration (seconds)"), settings.CombatSummaryDuration,
            3, 15, 1, value => settings.CombatSummaryDuration = (float)value);
        AddText(DpsLocalization.Get(
            "默认显示 5 秒，可调 3–15 秒（含 3/5/8 秒），时长在下一次总结生效。单人显示本场数据，多人显示全队本场汇总；不受模块、摘要字段、范围或排序影响。总结不拦截鼠标，到时或进入下一场消失。关闭后立即取消且不重播；设置打开时暂时隐藏，计时继续。HUD 隐藏时不显示总结。",
            "Shown for 5 seconds by default; adjustable from 3 to 15 seconds, including 3/5/8. Duration changes apply to the next summary. Single-player shows this combat; multiplayer shows team totals, independent of modules, title fields, scope or sorting. Mouse input passes through. It closes on timeout or the next combat. Disabling cancels it without replay. Settings temporarily hides it while the timer continues. A hidden HUD suppresses summaries."), MutedColor);

        AddSection(DpsLocalization.Get("操作", "Actions"));
        var resetAll = new Button
        {
            FocusMode = Control.FocusModeEnum.None,
            Text = DpsLocalization.Get("恢复全部推荐值", "Restore Recommended Values")
        };
        resetAll.Pressed += () =>
        {
            DpsMeterSettings.ResetToRecommended();
            DpsMeterUi.Refresh();
            Refresh();
        };
        _content!.AddChild(resetAll);

        var close = new Button
        {
            FocusMode = Control.FocusModeEnum.None,
            Text = DpsLocalization.Get("关闭设置面板", "Close Settings")
        };
        close.Pressed += () => ToggleVisibility();
        _content!.AddChild(close);

        AddText(DpsLocalization.Get("配置文件", "Config File"), MutedColor);
        AddText(DpsMeterSettings.FilePath, MutedColor);
    }

    private static void AddSection(string title)
    {
        var label = new Label
        {
            Text = title,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride("font_color", HeadingColor);
        label.AddThemeFontSizeOverride("font_size", DpsHudTypography.HeaderSize);
        _content!.AddChild(label);
    }

    private static void AddToggle(string title, bool value, Action<bool> changed)
    {
        var toggle = new CheckBox
        {
            FocusMode = Control.FocusModeEnum.None,
            Text = title,
            ButtonPressed = value
        };
        toggle.AddThemeColorOverride("font_color", BodyColor);
        toggle.AddThemeFontSizeOverride("font_size", DpsHudTypography.BodySize);
        toggle.Toggled += pressed => changed(pressed);
        _content!.AddChild(toggle);
    }

    private static void AddOptionRow(string title, string[] options, int selected, Action<int> changed)
    {
        var row = CreateRow(title);
        var option = new OptionButton
        {
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        foreach (var item in options)
        {
            option.AddItem(item);
        }

        option.Selected = Math.Clamp(selected, 0, options.Length - 1);
        option.AddThemeColorOverride("font_color", BodyColor);
        option.AddThemeFontSizeOverride("font_size", DpsHudTypography.BodySize);
        // 弹出菜单是独立窗口，显式共享字体主题，不能只依赖面板继承。
        var popup = option.GetPopup();
        popup.Theme = _panel!.Theme;
        popup.AddThemeFontSizeOverride("font_size", DpsHudTypography.BodySize);
        option.ItemSelected += index => changed((int)index);
        row.AddChild(option);
        _content!.AddChild(row);
    }

    private static void AddSliderRow(
        string title,
        float value,
        double min,
        double max,
        double step,
        Action<double> changed)
    {
        var row = CreateRow(title);
        var valueLabel = new Label
        {
            Text = FormatSettingValue(value, step),
            CustomMinimumSize = new Vector2(58, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        valueLabel.AddThemeColorOverride("font_color", BodyColor);
        valueLabel.AddThemeFontSizeOverride("font_size", DpsHudTypography.BodySize);

        var slider = new HSlider
        {
            FocusMode = Control.FocusModeEnum.None,
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = value,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(160, 0)
        };
        slider.ValueChanged += next =>
        {
            valueLabel.Text = FormatSettingValue((float)next, step);
            changed(next);
        };
        slider.DragEnded += valueChanged =>
        {
            if (valueChanged)
            {
                SaveAndRefresh();
            }
        };
        row.AddChild(slider);
        row.AddChild(valueLabel);
        _content!.AddChild(row);
    }

    private static HBoxContainer CreateRow(string title)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 7);
        var label = new Label
        {
            Text = title,
            CustomMinimumSize = new Vector2(150, 0),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride("font_color", BodyColor);
        label.AddThemeFontSizeOverride("font_size", DpsHudTypography.BodySize);
        row.AddChild(label);
        return row;
    }

    private static string FormatSettingValue(float value, double step)
    {
        return step < 1 ? value.ToString("0.00") : Math.Round(value).ToString("0");
    }

    private static void AddText(string text, Color color)
    {
        var label = new Label
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", DpsHudTypography.SettingsNoteSize);
        _content!.AddChild(label);
    }

    private static void SaveAndRefresh()
    {
        DpsMeterSettings.Save();
        DpsMeterUi.Refresh();
    }

    private static void SetField(Action update)
    {
        update();
        SaveAndRefresh();
        // 同步“全部关闭时保留名称”的归一化结果，不在事件回调中直接移除控件。
        Callable.From(Refresh).CallDeferred();
    }

    private static void AddSummaryToggle(string title, bool selected, Action<bool> changed)
    {
        AddToggle(title, selected, value =>
        {
            changed(value);
            if (DpsHudSummary.SelectedCount(DpsMeterSettings.Current) > DpsHudSummary.MaxFields) changed(false);
            SaveAndRefresh();
            Callable.From(Refresh).CallDeferred();
        });
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
}
