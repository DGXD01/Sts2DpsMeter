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
    private static bool _visible;

    public static bool IsVisible => _visible;

    public static void EnsureAttached(CanvasLayer canvas)
    {
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

        var title = new Label
        {
            Text = "Sts2 DPS Meter 设置",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        title.AddThemeColorOverride("font_color", HeadingColor);
        title.AddThemeFontSizeOverride("font_size", 16);
        root.AddChild(title);

        var introduction = new Label
        {
            Text = "“偏好”决定显示哪些独立 HUD；每个勾选项都以长方形框体显示。拖动标题可移动，靠近其他 HUD 边缘会磁力吸附，拖动已吸附 HUD 的上边框或标题可拆分。",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        introduction.AddThemeColorOverride("font_color", MutedColor);
        introduction.AddThemeFontSizeOverride("font_size", 12);
        root.AddChild(introduction);

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

    private static void RebuildContent()
    {
        ClearChildren(_content!);
        var settings = DpsMeterSettings.Current;

        AddSection("偏好");
        AddToggle("当前输出", settings.ShowCurrentOutput, value =>
        {
            DpsMeterSettings.SetModuleEnabled(settings, DpsMeterSettings.ModuleCurrentOutput, value);
            SaveAndRefresh();
        });
        AddToggle("本回合伤害统计", settings.ShowTurnDamage, value =>
        {
            DpsMeterSettings.SetModuleEnabled(settings, DpsMeterSettings.ModuleTurnDamage, value);
            SaveAndRefresh();
        });
        AddToggle("本场战斗伤害统计", settings.ShowCombatDamage, value =>
        {
            DpsMeterSettings.SetModuleEnabled(settings, DpsMeterSettings.ModuleCombatDamage, value);
            SaveAndRefresh();
        });
        AddToggle("受到伤害统计", settings.ShowTakenDamage, value =>
        {
            DpsMeterSettings.SetModuleEnabled(settings, DpsMeterSettings.ModuleTakenDamage, value);
            SaveAndRefresh();
        });
        AddToggle("多人排行", settings.ShowMultiplayerRanking, value =>
        {
            DpsMeterSettings.SetModuleEnabled(settings, DpsMeterSettings.ModuleMultiplayerRanking, value);
            SaveAndRefresh();
        });
        AddToggle("启用磁力吸附", settings.EnableSnapping, value =>
        {
            settings.EnableSnapping = value;
            SaveAndRefresh();
        });
        AddText("每个勾选项都是独立的长方形 HUD；拖动标题移动，拖动已吸附 HUD 的上边框或标题可拆分。", MutedColor, 11);

        AddSection("多人排行内容");
        AddToggle("玩家造成伤害", settings.ShowDealtBreakdown, value =>
        {
            settings.ShowDealtBreakdown = value;
            SaveAndRefresh();
        });
        AddToggle("玩家受到伤害", settings.ShowTakenBreakdown, value =>
        {
            settings.ShowTakenBreakdown = value;
            SaveAndRefresh();
        });
        AddText("这两个开关只控制“多人排行”HUD 内部是否显示输出和承伤区段。", MutedColor, 11);

        AddSection("显示与行为");
        AddToggle("启动时显示 HUD", settings.StartVisible, value =>
        {
            settings.StartVisible = value;
            SaveAndRefresh();
        });
        AddToggle("锁定 HUD 位置", settings.LockPosition, value =>
        {
            settings.LockPosition = value;
            SaveAndRefresh();
        });
        AddToggle("紧凑模式", settings.CompactMode, value =>
        {
            settings.CompactMode = value;
            SaveAndRefresh();
        });

        AddSection("布局与密度");
        AddSliderRow("伤害来源显示行数", settings.MaxRows, 1, 12, 1, value =>
        {
            settings.MaxRows = Math.Clamp((int)value, 1, 12);
            SaveAndRefresh();
        });

        AddSliderRow("面板透明度", settings.PanelOpacity, 0.35, 1.0, 0.01, value =>
        {
            settings.PanelOpacity = Mathf.Clamp((float)value, 0.35f, 1f);
            SaveAndRefresh();
        });
        AddSliderRow(
            "界面缩放",
            settings.UiScale,
            DpsMeterSettings.MinUiScale,
            DpsMeterSettings.MaxUiScale,
            DpsMeterSettings.UiScaleStep,
            value =>
        {
            settings.UiScale = DpsMeterSettings.ClampUiScale((float)value);
            SaveAndRefresh();
        });
        AddText("界面缩放仅通过上方滑块调整，不注册键盘或鼠标滚轮热键。", MutedColor, 11);

        var resetScale = new Button
        {
            FocusMode = Control.FocusModeEnum.None,
            Text = "重置 HUD 大小（100%）"
        };
        resetScale.Pressed += () =>
        {
            settings.UiScale = 1f;
            SaveAndRefresh();
        };
        _content!.AddChild(resetScale);

        AddSection("位置");
        var resetPosition = new Button
        {
            FocusMode = Control.FocusModeEnum.None,
            Text = "重置全部 HUD 位置与吸附关系",
            TooltipText = "全部 HUD 将回到默认网格，并清除模块之间的吸附关系。"
        };
        resetPosition.Pressed += () =>
        {
            settings.PositionX = 18f;
            settings.PositionY = 18f;
            DpsMeterSettings.ResetModuleLayouts(settings);
            SaveAndRefresh();
        };
        _content!.AddChild(resetPosition);

        AddSection("操作");
        var resetAll = new Button
        {
            FocusMode = Control.FocusModeEnum.None,
            Text = "恢复全部推荐值"
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
            Text = "关闭设置面板"
        };
        close.Pressed += () => ToggleVisibility();
        _content!.AddChild(close);

        AddText("配置文件", MutedColor, 12);
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
