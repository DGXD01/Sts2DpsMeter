using System;
using System.Collections.Generic;
using Godot;

namespace Sts2DpsMeter;

public static partial class DpsMeterSettingsUi
{
    private static ThemeDefinition Palette => DpsHudThemes.Get(DpsMeterSettings.Current.HudTheme);
    private static Color HeadingColor => Palette.Title;
    private static Color MutedColor => Palette.Muted;
    private static Color BodyColor => Palette.Primary;
    private static int FontSize(HudFontRole role) => DpsHudTypography.Size(role, DpsMeterSettings.Current.FontScale);

    private static PanelContainer? _panel;
    private static VBoxContainer? _content;
    private static ScrollContainer? _scroll;
    private static int _contentGeneration;
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

        var bar = _scroll!.GetVScrollBar();
        var fraction = DpsSettingsLayout.ScrollFraction(_scroll.ScrollVertical, bar.MaxValue, bar.Page);
        var generation = ++_contentGeneration;
        DpsHudThemeStyles.Apply(_panel!, DpsMeterSettings.Current.HudTheme, DpsMeterSettings.Current.FontScale);
        _panel!.AddThemeStyleboxOverride("panel", DpsHudThemeStyles.Panel(HudPanelKind.Settings, Palette));
        _titleLabel!.AddThemeColorOverride("font_color", HeadingColor);
        _introductionLabel!.AddThemeColorOverride("font_color", MutedColor);
        _titleLabel.AddThemeFontSizeOverride("font_size", FontSize(HudFontRole.SettingsTitle));
        _introductionLabel.AddThemeFontSizeOverride("font_size", FontSize(HudFontRole.Note));
        UpdateLocalizedHeader();
        RebuildContent();
        _panel!.Visible = _visible;
        ApplyPanelBounds();
        // 原生容器完成本轮排序后恢复滚动比例；旧重建任务不能覆盖新内容。
        Callable.From(() =>
        {
            if (generation != _contentGeneration || !IsAlive(_scroll)) return;
            var currentBar = _scroll!.GetVScrollBar();
            _scroll.ScrollVertical = DpsSettingsLayout.RestoreScroll(fraction, currentBar.MaxValue, currentBar.Page);
        }).CallDeferred();
    }

    private static void BuildPanel(CanvasLayer canvas)
    {
        ++_contentGeneration;
        _panel = new PanelContainer
        {
            Name = "Sts2DpsMeterSettingsPanel",
            Position = new Vector2(400, 45),
            Size = new Vector2(485, 650),
            CustomMinimumSize = new Vector2(485, 650),
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        DpsHudThemeStyles.Apply(_panel, DpsMeterSettings.Current.HudTheme, DpsMeterSettings.Current.FontScale);

        _panel.AddThemeStyleboxOverride("panel", DpsHudThemeStyles.Panel(HudPanelKind.Settings, Palette));
        canvas.AddChild(_panel);
        var viewport = _panel.GetViewport();
        viewport.SizeChanged += ApplyPanelBounds;
        _panel.TreeExiting += () => viewport.SizeChanged -= ApplyPanelBounds;

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
        _panel.AddChild(root);

        _titleLabel = new Label
        {
            Text = DpsLocalization.Get("Sts2 DPS Meter 设置", "Sts2 DPS Meter Settings"),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _titleLabel.AddThemeColorOverride("font_color", HeadingColor);
        _titleLabel.AddThemeFontSizeOverride("font_size", FontSize(HudFontRole.SettingsTitle));
        root.AddChild(_titleLabel);

        _introductionLabel = new Label
        {
            Text = DpsLocalization.Get("点击 HUD 齿轮（⚙）打开或关闭设置。单人可切换本场战斗、本回合；多人增加全队汇总和玩家排行。各组选项可单独调整并保存。", "Use the HUD gear (⚙) to open or close Settings. Single-player offers This Combat and This Turn; multiplayer adds Team summaries and player rankings. Options in each section can be adjusted and saved individually."),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _introductionLabel.AddThemeColorOverride("font_color", MutedColor);
        _introductionLabel.AddThemeFontSizeOverride("font_size", FontSize(HudFontRole.Note));
        root.AddChild(_introductionLabel);

        _scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
            // 高度由面板剩余空间分配，较大的说明字号不再叠加固定 520px 明细下限。
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        root.AddChild(_scroll);

        _content = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _content.AddThemeConstantOverride("separation", 7);
        _scroll.AddChild(_content);
        ApplyPanelBounds();
    }

    private static void ApplyPanelBounds()
    {
        if (!IsAlive(_panel)) return;
        var viewport = _panel!.GetCanvasTransform().AffineInverse() * _panel.GetViewport().GetVisibleRect();
        var bounds = DpsSettingsLayout.Calculate(viewport.Size);
        _panel.CustomMinimumSize = bounds.Size;
        _panel.Size = bounds.Size;
        _panel.Position = viewport.Position + bounds.Position;
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
                "点击 HUD 齿轮（⚙）打开或关闭设置。单人可切换本场战斗、本回合；多人增加全队汇总和玩家排行。各组选项可单独调整并保存。",
                "Use the HUD gear (⚙) to open or close Settings. Single-player offers This Combat and This Turn; multiplayer adds Team summaries and player rankings. Options in each section can be adjusted and saved individually.");
        }
    }

    private static void RebuildContent()
    {
        ClearChildren(_content!);
        var settings = DpsMeterSettings.Current;
        foreach (var section in DpsSettingsLayout.Sections)
        {
            AddSection(DpsSettingsLayout.Label(section, DpsLocalization.IsChinese));
            switch (section)
            {
                case HudSettingsSection.Hud: AddHudSettings(settings); break;
                case HudSettingsSection.Position: AddPositionSettings(settings); break;
                case HudSettingsSection.Statistics: AddStatisticsSettings(settings); break;
                case HudSettingsSection.Rows: AddRowsSettings(settings); break;
                case HudSettingsSection.Multiplayer: AddMultiplayerSettings(settings); break;
                case HudSettingsSection.CombatEnd: AddCombatEndSettings(settings); break;
                case HudSettingsSection.Presets: AddPresetsSettings(settings); break;
            }
        }
        AddFooter();
    }

    private static void AddLayoutPresets()
    {
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 6);
        foreach (var preset in new[] { HudLayoutPreset.Minimal, HudLayoutPreset.Default, HudLayoutPreset.Detailed, HudLayoutPreset.Multiplayer })
        {
            var button = new Button
            {
                Text = DpsHudPresets.Label(preset, DpsLocalization.IsChinese),
                TooltipText = DpsHudPresets.Description(preset, DpsLocalization.IsChinese),
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(0, 34),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            button.AddThemeFontSizeOverride("font_size", FontSize(HudFontRole.Body));
            button.AddThemeColorOverride("font_color", BodyColor);
            button.Pressed += () =>
            {
                if (!DpsHudPresets.Apply(DpsMeterSettings.Current, preset)) return;
                SaveAndRefresh();
                // 先处理原生按钮事件，再重建设置，避免在回调中移除当前按钮。
                Callable.From(Refresh).CallDeferred();
            };
            grid.AddChild(button);
        }
        _content!.AddChild(grid);
        AddText(DpsLocalization.Get(
            "点击一次复制展示参数；之后仍可逐项修改并保存，不会被预设持续覆盖。悬停按钮可查看具体配置。位置、锁定、磁吸、透明度、低干扰及战斗总结保持原值。四套预设均按伤害降序；多人预设在单人不会强制多人显示，新战斗默认全队，其他预设默认当前玩家，当前战斗手选保持。",
            "Click once to copy display settings; individual changes remain editable and saved without being overwritten. Hover a button for details. Position, lock, snapping, opacity, combat visibility and combat summary keep their values. All presets sort by damage. Multiplayer does not force multiplayer UI in solo; it defaults new multiplayer combats to Team, while other presets use Player. The current combat selection is preserved."), MutedColor);
    }

    private static void AddSection(string title)
    {
        var label = new Label
        {
            Text = title,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride("font_color", HeadingColor);
        label.AddThemeFontSizeOverride("font_size", FontSize(HudFontRole.Header));
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
        toggle.AddThemeFontSizeOverride("font_size", FontSize(HudFontRole.Body));
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
        option.AddThemeFontSizeOverride("font_size", FontSize(HudFontRole.Body));
        // 弹出菜单是独立窗口，显式共享字体主题，不能只依赖面板继承。
        var popup = option.GetPopup();
        popup.Theme = _panel!.Theme;
        popup.AddThemeFontSizeOverride("font_size", FontSize(HudFontRole.Body));
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
        valueLabel.AddThemeFontSizeOverride("font_size", FontSize(HudFontRole.Body));

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
        label.AddThemeFontSizeOverride("font_size", FontSize(HudFontRole.Body));
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
        label.AddThemeFontSizeOverride("font_size", FontSize(HudFontRole.Note));
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
