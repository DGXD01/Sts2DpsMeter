using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Godot;

namespace Sts2DpsMeter;

public static class DpsMeterUi
{
    private static readonly Color PanelBackground = new(0.035f, 0.045f, 0.07f, 0.92f);
    private static readonly Color PanelBorder = new(0.25f, 0.65f, 0.95f, 0.95f);
    private static readonly Color AccentCyan = new(0.42f, 0.86f, 1f);
    private static readonly Color AccentGreen = new(0.48f, 0.92f, 0.62f);
    private static readonly Color AccentOrange = new(1f, 0.67f, 0.32f);
    private static readonly Color AccentRed = new(1f, 0.44f, 0.48f);
    private static readonly Color AccentPurple = new(0.78f, 0.58f, 1f);
    private static readonly Color MutedText = new(0.68f, 0.74f, 0.82f);
    private static readonly Color PrimaryText = new(0.91f, 0.95f, 1f);

    private static CanvasLayer? _canvas;
    private static PanelContainer? _panel;
    private static StyleBoxFlat? _panelStyle;
    private static GridContainer? _moduleGrid;
    private static Label? _header;
    private static DpsMeterHost? _host;
    private static bool _visible = true;
    private static bool _visibilityInitialized;
    private static bool _dragging;
    private static int _immediateRefreshRequested;

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
                    Name = "Sts2DpsMeterHost"
                };
                root.AddChild(_host);
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
        if (!IsAlive(_panel) || !IsAlive(_moduleGrid) || !IsAlive(_header))
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
        _header!.Text = BuildHeader(settings);
        ApplyPanelStyle(settings);

        ClearChildren(_moduleGrid!);
        _moduleGrid!.Columns = settings.Columns;

        if (snapshot.Players.Count == 0)
        {
            AddEmptyState(settings);
        }
        else
        {
            AddModules(settings, snapshot);
        }

        EnsurePanelSize(settings);
    }

    public static void RequestImmediateRefresh()
    {
        Interlocked.Exchange(ref _immediateRefreshRequested, 1);
    }

    internal static bool ConsumeImmediateRefreshRequest()
    {
        return Interlocked.Exchange(ref _immediateRefreshRequested, 0) == 1;
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

    private static string BuildHeader(DpsMeterSettingsData settings)
    {
        var hint = settings.LockPosition ? "位置已锁定" : "可拖动";
        var scalePercent = Mathf.RoundToInt(settings.UiScale * 100f);
        return $"Sts2 DPS  |  {hint}  |  缩放 {scalePercent}%  |  Ctrl+滚轮  |  F7 设置  F8 开关";
    }

    private static void AddEmptyState(DpsMeterSettingsData settings)
    {
        var panel = CreateModule("等待伤害数据", AccentCyan, settings.CompactMode ? 260 : 310);
        AddTextLine(panel, "战斗开始后，这里会按你选择的模块显示伤害统计。", MutedText);
        _moduleGrid!.AddChild(panel.GetParent());
    }

    private static void AddModules(DpsMeterSettingsData settings, CombatStatsSnapshot snapshot)
    {
        if (settings.ShowDealtBreakdown)
        {
            AddDealtBreakdownModule(settings, snapshot);
        }

        if (settings.ShowTakenBreakdown)
        {
            AddTakenBreakdownModule(settings, snapshot);
        }
    }

    private static void AddDealtBreakdownModule(DpsMeterSettingsData settings, CombatStatsSnapshot snapshot)
    {
        var body = CreateModule("玩家造成伤害", AccentCyan, GetModuleWidth(settings));
        foreach (var player in snapshot.Players)
        {
            AddSubHeader(body, player.DisplayName, AccentCyan);
            AddKeyValueLine(body, "总伤害（含格挡）", $"{player.TotalDamage}（{player.HitCount} 次）");
            AddKeyValueLine(body, "格挡吸收", player.BlockedDamage.ToString(), AccentOrange);
            AddKeyValueLine(body, "生命伤害", player.UnblockedDamage.ToString(), AccentGreen);
            AddKeyValueLine(body, "过量伤害", player.OverkillDamage.ToString(), AccentOrange);
            AddTextLine(body, "按来源划分", MutedText, 11);

            var sources = player.Sources.Take(settings.MaxRows).ToArray();
            if (sources.Length == 0)
            {
                AddTextLine(body, "暂无伤害来源。", MutedText);
                continue;
            }

            foreach (var source in sources)
            {
                AddKeyValueLine(
                    body,
                    $"{FormatSource(source)}（{source.Hits} 次）",
                    source.Damage.ToString(),
                    AccentCyan);
            }
        }

        AddModuleToGrid(body);
    }

    private static void AddTakenBreakdownModule(DpsMeterSettingsData settings, CombatStatsSnapshot snapshot)
    {
        var body = CreateModule("玩家受到伤害", AccentRed, GetModuleWidth(settings));
        foreach (var player in snapshot.Players)
        {
            var sources = player.IncomingSources
                .Concat(player.SelfLossSources)
                .OrderByDescending(source => source.Damage)
                .ThenBy(source => source.Source)
                .ToArray();
            var blockedDamage = sources.Sum(source => source.BlockedDamage);
            var unblockedDamage = sources.Sum(source => source.UnblockedDamage);
            var overkillDamage = sources.Sum(source => source.OverkillDamage);
            var totalDamage = player.IncomingDamage + player.SelfLossDamage;
            var totalHits = player.IncomingHits + player.SelfLossHits;

            AddSubHeader(body, player.DisplayName, AccentRed);
            AddKeyValueLine(body, "总受到伤害（含格挡）", $"{totalDamage}（{totalHits} 次）", AccentRed);
            AddKeyValueLine(body, "敌方造成", player.IncomingDamage.ToString(), AccentRed);
            AddKeyValueLine(body, "自损", player.SelfLossDamage.ToString(), AccentOrange);
            AddKeyValueLine(body, "格挡吸收", blockedDamage.ToString(), AccentCyan);
            AddKeyValueLine(body, "生命伤害", unblockedDamage.ToString(), AccentGreen);
            AddKeyValueLine(body, "过量伤害", overkillDamage.ToString(), AccentOrange);
            AddTextLine(body, "按来源划分", MutedText, 11);

            var visibleSources = sources.Take(settings.MaxRows).ToArray();
            if (visibleSources.Length == 0)
            {
                AddTextLine(body, "暂无受到伤害来源。", MutedText);
                continue;
            }

            foreach (var source in visibleSources)
            {
                AddKeyValueLine(
                    body,
                    $"{FormatSource(source)}（{source.Hits} 次）",
                    source.Damage.ToString(),
                    AccentRed);
            }
        }

        AddModuleToGrid(body);
    }
    private static VBoxContainer CreateModule(string title, Color accent, float width)
    {
        var card = new PanelContainer
        {
            CustomMinimumSize = new Vector2(width, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        var cardStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.07f, 0.105f, 0.82f),
            BorderColor = new Color(accent.R, accent.G, accent.B, 0.72f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            ContentMarginLeft = 8,
            ContentMarginTop = 7,
            ContentMarginRight = 8,
            ContentMarginBottom = 7
        };
        card.AddThemeStyleboxOverride("panel", cardStyle);

        var body = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        body.AddThemeConstantOverride("separation", 3);
        card.AddChild(body);

        var heading = new Label
        {
            Text = title,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        heading.AddThemeColorOverride("font_color", accent);
        heading.AddThemeFontSizeOverride("font_size", 14);
        body.AddChild(heading);

        var divider = new ColorRect
        {
            Color = new Color(accent.R, accent.G, accent.B, 0.25f),
            CustomMinimumSize = new Vector2(0, 1),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        body.AddChild(divider);
        return body;
    }

    private static void AddModuleToGrid(VBoxContainer body)
    {
        if (body.GetParent() is Control card)
        {
            _moduleGrid!.AddChild(card);
        }
    }

    private static void AddSubHeader(VBoxContainer body, string text, Color color)
    {
        var label = new Label
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", 13);
        body.AddChild(label);
    }

    private static void AddTextLine(VBoxContainer body, string text, Color color, int fontSize = 12)
    {
        var label = new Label
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        body.AddChild(label);
    }

    private static void AddKeyValueLine(VBoxContainer body, string key, string value, Color? valueColor = null)
    {
        var row = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        row.AddThemeConstantOverride("separation", 6);
        body.AddChild(row);

        var keyLabel = new Label
        {
            Text = key,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        keyLabel.AddThemeColorOverride("font_color", PrimaryText);
        keyLabel.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(keyLabel);

        var valueLabel = new Label
        {
            Text = value,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        valueLabel.AddThemeColorOverride("font_color", valueColor ?? AccentCyan);
        valueLabel.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(valueLabel);
    }

    private static void AddModuleToGrid(Control module)
    {
        _moduleGrid!.AddChild(module);
    }

    private static float GetModuleWidth(DpsMeterSettingsData settings)
    {
        return settings.CompactMode ? 270f : 320f;
    }

    private static void EnsurePanelSize(DpsMeterSettingsData settings)
    {
        _panel!.CustomMinimumSize = new Vector2(320f, 180f);
        var minimum = _panel.GetCombinedMinimumSize();
        var viewportSize = GetViewportSize();
        var maxWidth = Mathf.Max(320f, viewportSize.X - 24f);
        var maxHeight = Mathf.Max(180f, viewportSize.Y - 24f);
        var width = Mathf.Clamp(minimum.X, 320f, maxWidth);
        var height = Mathf.Clamp(minimum.Y, 180f, maxHeight);
        _panel.Size = new Vector2(width, height);

        var scaledSize = _panel.Size * settings.UiScale;
        _panel.Position = new Vector2(
            Mathf.Clamp(_panel.Position.X, 0f, Mathf.Max(0f, viewportSize.X - scaledSize.X)),
            Mathf.Clamp(_panel.Position.Y, 0f, Mathf.Max(0f, viewportSize.Y - scaledSize.Y)));
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
            Size = new Vector2(680, 240),
            CustomMinimumSize = new Vector2(360, 180),
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
            ContentMarginLeft = 10,
            ContentMarginTop = 8,
            ContentMarginRight = 10,
            ContentMarginBottom = 8
        };
        _panel.AddThemeStyleboxOverride("panel", _panelStyle);
        _panel.GuiInput += OnPanelGuiInput;
        canvas.AddChild(_panel);

        var layout = new VBoxContainer
        {
            Name = "Sts2DpsMeterLayout",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        layout.AddThemeConstantOverride("separation", 6);
        _panel.AddChild(layout);

        _header = new Label
        {
            Text = "Sts2 DPS  |  等待战斗开始...",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _header.AddThemeColorOverride("font_color", AccentCyan);
        _header.AddThemeFontSizeOverride("font_size", 14);
        layout.AddChild(_header);

        _moduleGrid = new GridContainer
        {
            Name = "Sts2DpsMeterModules",
            Columns = 2,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _moduleGrid.AddThemeConstantOverride("h_separation", 8);
        _moduleGrid.AddThemeConstantOverride("v_separation", 8);
        layout.AddChild(_moduleGrid);
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
                    settings.PositionY = _panel!.Position.Y;
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

    private static string FormatSource(SourceStatsSnapshot source)
    {
        var parts = new List<string>
        {
            GetSourceTypeLabel(source.SourceType),
            source.Source
        };

        if (!string.IsNullOrWhiteSpace(source.SemanticTag))
        {
            parts.Add(source.SemanticTag);
        }

        if (!string.IsNullOrWhiteSpace(source.TriggerCard))
        {
            parts.Add($"触发卡牌：{source.TriggerCard}");
        }

        if (source.TrackingStatus == SourceTrackingStatus.Deferred &&
            source.Mechanism == DamageMechanismKind.Poison)
        {
            parts.Add("中毒来源待标记");
        }

        return string.Join(" / ", parts);
    }

    private static string GetSourceTypeLabel(DamageSourceType sourceType)
    {
        return sourceType switch
        {
            DamageSourceType.Card => "卡牌",
            DamageSourceType.Power => "状态",
            DamageSourceType.Relic => "遗物",
            DamageSourceType.Potion => "药水",
            DamageSourceType.Orb => "充能球",
            DamageSourceType.Pet => "宠物",
            DamageSourceType.Enemy => "敌方",
            DamageSourceType.Event => "事件",
            DamageSourceType.Self => "自损",
            _ => "未知"
        };
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

internal sealed class DpsMeterHost : Node
{
    private bool _toggleHudPressed;
    private bool _toggleSettingsPressed;
    private double _refreshTimer;

    public override void _Process(double delta)
    {
        DpsStats.Tick(delta);

        var hudPressed = Input.IsPhysicalKeyPressed(Key.F8);
        if (hudPressed && !_toggleHudPressed)
        {
            DpsMeterUi.ToggleVisibility();
        }

        _toggleHudPressed = hudPressed;

        var settingsPressed = Input.IsPhysicalKeyPressed(Key.F7);
        if (settingsPressed && !_toggleSettingsPressed)
        {
            DpsMeterUi.ShowSettings();
        }

        _toggleSettingsPressed = settingsPressed;

        var immediateRefresh = DpsMeterUi.ConsumeImmediateRefreshRequest();
        _refreshTimer -= delta;
        if (immediateRefresh || _refreshTimer <= 0)
        {
            _refreshTimer = 0.25;
            DpsMeterUi.Refresh();
        }
    }
}
