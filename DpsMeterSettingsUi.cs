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
            Text = "Sts2 DPS Meter 设置  |  F7 关闭",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        title.AddThemeColorOverride("font_color", HeadingColor);
        title.AddThemeFontSizeOverride("font_size", 16);
        root.AddChild(title);

        var introduction = new Label
        {
            Text = "模块可以自由拼接；设置会立即保存，战斗面板每 0.25 秒刷新。",
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

        AddSection("模块拼接");
        AddToggle("战斗概要", settings.ShowSummary, value =>
        {
            settings.ShowSummary = value;
            SaveAndRefresh();
        });
        AddToggle("玩家排行（rDPS 风格）", settings.ShowRanking, value =>
        {
            settings.ShowRanking = value;
            SaveAndRefresh();
        });
        AddToggle("核心指标", settings.ShowCoreMetrics, value =>
        {
            settings.ShowCoreMetrics = value;
            SaveAndRefresh();
        });
        AddToggle("伤害拆分", settings.ShowDamageBreakdown, value =>
        {
            settings.ShowDamageBreakdown = value;
            SaveAndRefresh();
        });
        AddToggle("输出来源", settings.ShowSources, value =>
        {
            settings.ShowSources = value;
            SaveAndRefresh();
        });
        AddToggle("逐回合伤害", settings.ShowTurns, value =>
        {
            settings.ShowTurns = value;
            SaveAndRefresh();
        });
        AddToggle("卡牌伤害", settings.ShowCards, value =>
        {
            settings.ShowCards = value;
            SaveAndRefresh();
        });
        AddToggle("机制伤害（含中毒）", settings.ShowMechanisms, value =>
        {
            settings.ShowMechanisms = value;
            SaveAndRefresh();
        });
        AddToggle("承伤", settings.ShowIncoming, value =>
        {
            settings.ShowIncoming = value;
            SaveAndRefresh();
        });
        AddToggle("自损", settings.ShowSelfLoss, value =>
        {
            settings.ShowSelfLoss = value;
            SaveAndRefresh();
        });

        AddSection("布局与密度");
        AddOptionRow(
            "面板列数",
            new[] { "1 列", "2 列", "3 列" },
            Math.Clamp(settings.Columns, 1, 3) - 1,
            index =>
            {
                settings.Columns = Math.Clamp(index + 1, 1, 3);
                SaveAndRefresh();
            });
        AddSliderRow("排行/来源行数", settings.MaxRows, 1, 12, 1, value =>
        {
            settings.MaxRows = Math.Clamp((int)value, 1, 12);
            SaveAndRefresh();
        });
        AddSliderRow("显示回合数", settings.MaxTurns, 1, 20, 1, value =>
        {
            settings.MaxTurns = Math.Clamp((int)value, 1, 20);
            SaveAndRefresh();
        });
        AddSliderRow("面板透明度", settings.PanelOpacity, 0.35, 1.0, 0.01, value =>
        {
            settings.PanelOpacity = Mathf.Clamp((float)value, 0.35f, 1f);
            SaveAndRefresh();
        });
        AddSliderRow("界面缩放", settings.UiScale, 0.75, 1.35, 0.05, value =>
        {
            settings.UiScale = Mathf.Clamp((float)value, 0.75f, 1.35f);
            SaveAndRefresh();
        });

        AddSection("位置");
        var resetPosition = new Button
        {
            Text = "重置 HUD 位置到左上角",
            TooltipText = "默认位置为 (18, 18)；HUD 未锁定时可直接拖动。"
        };
        resetPosition.Pressed += () =>
        {
            settings.PositionX = 18f;
            settings.PositionY = 18f;
            SaveAndRefresh();
        };
        _content!.AddChild(resetPosition);

        AddSection("操作");
        var resetAll = new Button
        {
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
            Text = "关闭设置面板（F7）"
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
