using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sts2DpsMeter;

public static partial class DpsMeterUi
{
    private static bool UpdateDamageSections(DpsMeterSettingsData settings, DamageViewData view)
    {
        var changed = false;
        var sectionCount = Math.Max(1, view.Sections.Count);
        var activeRows = new List<DamageRowControls>();

        for (var index = 0; index < sectionCount; index++)
        {
            if (index == SectionPool.Count)
            {
                var section = new DamageSectionControls();
                SectionPool.Add(section);
                _moduleGrid!.AddChild(section.Container);
                changed = true;
            }

            var data = view.Sections.Count > 0 ? view.Sections[index] : null;
            var rows = data?.Rows
                .Where(row => data.KeepZeroRows || row.Damage > 0)
                .OrderByDescending(row => row.Damage)
                .ThenBy(row => row.Label, StringComparer.Ordinal)
                .Take(settings.MaxRows)
                .ToArray() ?? Array.Empty<DamageRowData>();
            if (rows.Length == 0)
            {
                rows = new[] { new DamageRowData(L("暂无记录", "No records"), 0) };
            }

            changed |= SectionPool[index].Update(
                data?.Heading, rows, data?.Total ?? 0, settings.CompactMode, activeRows);
        }

        for (var index = sectionCount; index < SectionPool.Count; index++)
        {
            changed |= SetVisible(SectionPool[index].Container, false);
        }

        // 统一测量所有可见行，较长数字不会只挤压某一行的名称列。
        var damageWidth = 42f;
        var percentageWidth = 42f;
        foreach (var row in activeRows)
        {
            damageWidth = Math.Max(damageWidth, row.MeasureDamageWidth());
            percentageWidth = Math.Max(percentageWidth, row.MeasurePercentageWidth());
        }

        foreach (var row in activeRows)
        {
            changed |= row.SetColumnWidths(damageWidth, percentageWidth);
        }

        return changed;
    }

    private static bool SetVisible(Control control, bool visible)
    {
        if (control.Visible == visible)
        {
            return false;
        }

        control.Visible = visible;
        return true;
    }

    private sealed class DamageSectionControls
    {
        public VBoxContainer Container { get; } = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };

        private readonly Label _heading = new()
        {
            CustomMinimumSize = new Vector2(0, 20),
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        private readonly List<DamageRowControls> _rows = new();

        public DamageSectionControls()
        {
            Container.AddThemeConstantOverride("separation", 4);
            _heading.AddThemeColorOverride("font_color", TitleBlue);
            _heading.AddThemeColorOverride("font_outline_color", Colors.Black);
            _heading.AddThemeConstantOverride("outline_size", 2);
            _heading.AddThemeFontSizeOverride("font_size", 14);
            Container.AddChild(_heading);
        }

        public bool Update(
            string? heading,
            IReadOnlyList<DamageRowData> rows,
            long total,
            bool compact,
            List<DamageRowControls> activeRows)
        {
            var changed = SetVisible(Container, true);
            changed |= SetVisible(_heading, !string.IsNullOrWhiteSpace(heading));
            if (_heading.Text != (heading ?? string.Empty))
            {
                _heading.Text = heading ?? string.Empty;
                changed = true;
            }

            for (var index = 0; index < rows.Count; index++)
            {
                if (index == _rows.Count)
                {
                    var row = new DamageRowControls();
                    _rows.Add(row);
                    Container.AddChild(row.Root);
                    changed = true;
                }

                var percentage = total > 0
                    ? Math.Clamp(rows[index].Damage / (double)total * 100d, 0d, 100d)
                    : 0d;
                changed |= _rows[index].Update(rows[index], percentage, compact);
                activeRows.Add(_rows[index]);
            }

            for (var index = rows.Count; index < _rows.Count; index++)
            {
                changed |= SetVisible(_rows[index].Root, false);
            }

            return changed;
        }
    }

    private sealed class DamageRowControls
    {
        public Control Root { get; } = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };

        private readonly ProgressBar _progress;
        private readonly Label _name;
        private readonly RichTextLabel _richName;
        private readonly Label _damage;
        private readonly Label _percentage;
        private DamageRowData? _lastData;
        private double _lastPercentage;
        private bool _compact;

        public DamageRowControls()
        {
            _progress = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 100,
                ShowPercentage = false,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            _progress.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _progress.AddThemeStyleboxOverride("background", CreateBarStyle(BarBackground, 4));
            _progress.AddThemeStyleboxOverride("fill", CreateBarStyle(BarFill, 4));
            Root.AddChild(_progress);

            var overlay = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            overlay.AddThemeConstantOverride("margin_left", 7);
            overlay.AddThemeConstantOverride("margin_right", 7);
            overlay.AddThemeConstantOverride("margin_top", 2);
            overlay.AddThemeConstantOverride("margin_bottom", 2);
            Root.AddChild(overlay);

            var columns = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            columns.AddThemeConstantOverride("separation", 6);
            overlay.AddChild(columns);

            // 为第 2 阶段来源图标保留固定列，当前不显示来源图标。
            columns.AddChild(new Control
            {
                Name = "IconColumn",
                CustomMinimumSize = new Vector2(16, 0),
                MouseFilter = Control.MouseFilterEnum.Ignore
            });

            var nameColumn = new Control
            {
                Name = "NameColumn",
                CustomMinimumSize = new Vector2(48, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                ClipContents = true,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            columns.AddChild(nameColumn);

            _name = CreateTextColumn("Name", HorizontalAlignment.Left);
            _name.ClipText = true;
            _name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            _name.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            nameColumn.AddChild(_name);

            _richName = new RichTextLabel
            {
                Name = "RichName",
                BbcodeEnabled = true,
                FitContent = false,
                ScrollActive = false,
                AutowrapMode = TextServer.AutowrapMode.Off,
                VerticalAlignment = VerticalAlignment.Center,
                ClipContents = true,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            _richName.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _richName.AddThemeColorOverride("default_color", PrimaryText);
            _richName.AddThemeColorOverride("font_outline_color", Colors.Black);
            _richName.AddThemeConstantOverride("outline_size", 2);
            nameColumn.AddChild(_richName);

            _damage = CreateTextColumn("DamageColumn", HorizontalAlignment.Right);
            _percentage = CreateTextColumn("PercentageColumn", HorizontalAlignment.Right);
            columns.AddChild(_damage);
            columns.AddChild(_percentage);
        }

        public bool Update(DamageRowData data, double percentage, bool compact)
        {
            var changed = SetVisible(Root, true);
            if (_lastData == data && _lastPercentage == percentage && _compact == compact)
            {
                return changed;
            }

            if (_lastData is null || _compact != compact)
            {
                var fontSize = compact ? 12 : 13;
                Root.CustomMinimumSize = new Vector2(0, compact ? 24 : 28);
                _name.AddThemeFontSizeOverride("font_size", fontSize);
                _richName.AddThemeFontSizeOverride("normal_font_size", fontSize);
                _richName.AddThemeFontSizeOverride("bold_font_size", fontSize);
                _damage.AddThemeFontSizeOverride("font_size", fontSize);
                _percentage.AddThemeFontSizeOverride("font_size", fontSize);
            }

            if (_lastData != data)
            {
                _name.Text = data.Label;
                _richName.Text = data.Details is not null ? BuildRichRowText(data) : string.Empty;
                _name.Visible = data.Details is null;
                _richName.Visible = data.Details is not null;
                _damage.Text = data.Damage.ToString();
            }

            _percentage.Text = $"{Math.Round(percentage):0}%";
            _progress.Value = percentage;
            _lastData = data;
            _lastPercentage = percentage;
            _compact = compact;
            return true;
        }

        public float MeasureDamageWidth() => MeasureTextWidth(_damage);

        public float MeasurePercentageWidth() => MeasureTextWidth(_percentage);

        public bool SetColumnWidths(float damageWidth, float percentageWidth)
        {
            var changed = false;
            if (_damage.CustomMinimumSize.X != damageWidth)
            {
                _damage.CustomMinimumSize = new Vector2(damageWidth, 0);
                changed = true;
            }

            if (_percentage.CustomMinimumSize.X != percentageWidth)
            {
                _percentage.CustomMinimumSize = new Vector2(percentageWidth, 0);
                changed = true;
            }

            return changed;
        }

        private static float MeasureTextWidth(Label label)
        {
            return Mathf.Ceil(label.GetThemeFont("font").GetStringSize(
                label.Text, HorizontalAlignment.Left, -1, label.GetThemeFontSize("font_size")).X);
        }

        private static Label CreateTextColumn(string name, HorizontalAlignment alignment)
        {
            var label = new Label
            {
                Name = name,
                HorizontalAlignment = alignment,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            label.AddThemeColorOverride("font_color", PrimaryText);
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 2);
            return label;
        }
    }
}
