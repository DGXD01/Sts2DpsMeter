using System;
using System.Linq;
using System.Text;
using Godot;

namespace Sts2DpsMeter;

public static class DpsMeterUi
{
    private static CanvasLayer? _canvas;
    private static PanelContainer? _panel;
    private static Label? _summary;
    private static Label? _body;
    private static DpsMeterHost? _host;
    private static bool _visible = true;

    public static void EnsureAttached()
    {
        if (IsAlive(_canvas) && IsAlive(_host))
        {
            _canvas!.Visible = _visible;
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

            _canvas!.Visible = _visible;
            Refresh();
        }
        catch (Exception exception)
        {
            GD.PushError($"[{ModEntry.ModId}] 创建统计面板失败：{exception}");
        }
    }

    public static void Refresh()
    {
        if (!IsAlive(_summary) || !IsAlive(_body))
        {
            return;
        }

        var snapshot = DpsStats.GetSnapshot();
        _summary!.Text =
            $"战斗耗时：{snapshot.ElapsedSeconds:F1} 秒    回合：{snapshot.CurrentTurn}    " +
            $"状态：{(snapshot.IsActive ? "战斗中" : "已结束")}";

        if (snapshot.Players.Count == 0)
        {
            _body!.Text = "尚无伤害数据。";
        }
        else
        {
            var text = new StringBuilder();
            var lineCount = 1;

            foreach (var player in snapshot.Players)
            {
                text.AppendLine(
                    $"{player.DisplayName}  |  总伤害 {player.TotalDamage}  " +
                    $"DPS {player.Dps:F1}  DPT {player.Dpt:F1}  命中 {player.HitCount}  " +
                    $"最高 {player.MaxHit}");

                foreach (var source in player.Sources.Take(4))
                {
                    text.AppendLine($"  来源 {source.Source}：{source.Damage}");
                    lineCount++;
                }

                var turns = string.Join(" / ", player.Turns.Select(turn => $"T{turn.Turn}:{turn.Damage}"));
                if (!string.IsNullOrEmpty(turns))
                {
                    text.AppendLine($"  回合伤害：{turns}");
                    lineCount++;
                }

                lineCount += 2;
            }

            _body!.Text = text.ToString().TrimEnd();
            if (IsAlive(_panel))
            {
                _panel!.Size = new Vector2(620, Mathf.Max(170, 105 + lineCount * 18));
            }
        }
    }

    public static void ToggleVisibility()
    {
        if (!IsAlive(_canvas))
        {
            return;
        }

        _visible = !_visible;
        _canvas!.Visible = _visible;
        GD.Print($"[{ModEntry.ModId}] 统计面板：{(_visible ? "显示" : "隐藏")}");
    }

    private static void BuildPanel(CanvasLayer canvas)
    {
        _panel = new PanelContainer
        {
            Name = "Sts2DpsMeterPanel",
            Position = new Vector2(18, 18),
            Size = new Vector2(620, 170),
            CustomMinimumSize = new Vector2(620, 170),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.05f, 0.08f, 0.92f),
            BorderColor = new Color(0.25f, 0.65f, 0.95f, 0.95f),
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
        _panel.AddThemeStyleboxOverride("panel", style);
        canvas.AddChild(_panel);

        var layout = new VBoxContainer
        {
            Name = "Sts2DpsMeterLayout"
        };
        layout.AddThemeConstantOverride("separation", 4);
        _panel.AddChild(layout);

        var title = new Label
        {
            Text = "Sts2 DPS 统计  |  F8 显示/隐藏"
        };
        title.AddThemeColorOverride("font_color", new Color(0.55f, 0.84f, 1f));
        title.AddThemeFontSizeOverride("font_size", 15);
        layout.AddChild(title);

        _summary = new Label
        {
            Text = "等待战斗开始..."
        };
        _summary.AddThemeColorOverride("font_color", new Color(0.85f, 0.9f, 0.95f));
        layout.AddChild(_summary);

        _body = new Label
        {
            Text = "尚无统计数据。"
        };
        _body.AddThemeColorOverride("font_color", new Color(0.9f, 0.94f, 1f));
        layout.AddChild(_body);
    }

    private static bool IsAlive(GodotObject? value)
    {
        return value is not null && GodotObject.IsInstanceValid(value);
    }
}

internal sealed class DpsMeterHost : Node
{
    private bool _togglePressed;
    private double _refreshTimer;

    public override void _Process(double delta)
    {
        DpsStats.Tick(delta);

        var pressed = Input.IsPhysicalKeyPressed(Key.F8);
        if (pressed && !_togglePressed)
        {
            DpsMeterUi.ToggleVisibility();
        }

        _togglePressed = pressed;
        _refreshTimer -= delta;
        if (_refreshTimer <= 0)
        {
            _refreshTimer = 0.25;
            DpsMeterUi.Refresh();
        }
    }
}
