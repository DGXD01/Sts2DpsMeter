using System;
using System.Collections.Generic;
using Godot;

namespace Sts2DpsMeter;

public static class DpsMeterSettingsUi
{
    private static readonly Color SettingsBackground = new(0.035f, 0.045f, 0.07f, 0.97f);
    private static readonly Color SettingsBorder = new(0.32f, 0.72f, 1f, 0.95f);
    private static readonly Color HeadingColor = new(0.56f, 0.86f, 1f);
    private static readonly Color MutedColor = new(0.68f, 0.74f, 0.82f);
    private static readonly Color BodyColor = new(0.9f, 0.94f, 1f);

    private static PanelContainer? _panel;
    private static VBoxContainer? _content;
    private static Label? _titleLabel;
    private static Label? _introductionLabel;
    private static bool _visible;

    public static bool IsVisible => _visible;

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
        _titleLabel.AddThemeFontSizeOverride("font_size", 16);
        root.AddChild(_titleLabel);

        _introductionLabel = new Label
        {
            Text = DpsLocalization.Get("伤害发生后于下一帧刷新，同帧请求合并处理。点击 HUD 的齿轮（⚙）按钮打开或关闭此面板；右上角范围固定为本场战斗、本回合、总计，多人游戏会自动使用队伍汇总和玩家排行。", "Refreshes on the next frame after damage, combining requests from the same frame. Use the HUD gear (⚙) to open or close this panel. The scope in the top-right is fixed to This Combat, This Turn, and Total; multiplayer automatically uses team summaries and player rankings."),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _introductionLabel.AddThemeColorOverride("font_color", MutedColor);
        _introductionLabel.AddThemeFontSizeOverride("font_size", 12);
        root.AddChild(_introductionLabel);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(455, 520)
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
                "伤害发生后于下一帧刷新，同帧请求合并处理。点击 HUD 的齿轮（⚙）按钮打开或关闭此面板；右上角范围固定为本场战斗、本回合、总计，多人游戏会自动使用队伍汇总和玩家排行。",
                "Refreshes on the next frame after damage, combining requests from the same frame. Use the HUD gear (⚙) to open or close this panel. The scope in the top-right is fixed to This Combat, This Turn, and Total; multiplayer automatically uses team summaries and player rankings.");
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
        AddText(DpsLocalization.Get("队友增益仅在多人模式显示；单人模式下这些开关不会新增 HUD 模块。", "Ally support is shown only in multiplayer; these toggles do not add HUD modules in single-player."), MutedColor, 11);
        AddText(DpsLocalization.Get("可在下方勾选需要显示的模块；统计范围仅切换数据的时间维度，不改变模块开关。", "Select the modules to display below; scope changes only the time range, not the module toggles."), MutedColor, 11);

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
        AddText(DpsLocalization.Get("界面缩放仅通过上方滑块调整，不注册键盘或鼠标滚轮热键。", "UI scale is adjusted only by the slider above; no keyboard or mouse-wheel hotkeys are registered."), MutedColor, 11);

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
            SaveAndRefresh();
        };
        _content!.AddChild(resetPosition);

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

        AddText(DpsLocalization.Get("配置文件", "Config File"), MutedColor, 12);
        AddText(DpsMeterSettings.FilePath, MutedColor, 11);
    }

    private static void AddSection(string title)
    {
        var label = new Label
        {
            Text = title,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride("font_color", HeadingColor);
        label.AddThemeFontSizeOverride("font_size", 14);
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
        valueLabel.AddThemeFontSizeOverride("font_size", 12);

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
            CustomMinimumSize = new Vector2(130, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride("font_color", BodyColor);
        label.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(label);
        return row;
    }

    private static string FormatSettingValue(float value, double step)
    {
        return step < 1 ? value.ToString("0.00") : Math.Round(value).ToString("0");
    }

    private static void AddText(string text, Color color, int fontSize)
    {
        var label = new Label
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        _content!.AddChild(label);
    }

    private static void SaveAndRefresh()
    {
        DpsMeterSettings.Save();
        DpsMeterUi.Refresh();
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
