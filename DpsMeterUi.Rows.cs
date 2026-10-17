using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Sts2DpsMeter;

public static partial class DpsMeterUi
{
    private static readonly List<DamageRowControls> ActiveRows = new();
    private static readonly HudRowSelection RowSelection = new();
    private static readonly float[] ColumnWidths = new float[5];

    private static bool UpdateDamageSections(DpsMeterSettingsData settings, DamageViewData view, string language)
    {
        var changed = false;
        var sectionCount = Math.Max(1, view.Sections.Count);
        var fields = DpsHudRowFields.GetVisible(settings);
        ActiveRows.Clear();
        if (!settings.EnableRowDetails) RowSelection.Clear();

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
            var rows = data is null ? Array.Empty<DamageRowData>() : DpsHudSorting.VisibleRows(
                data.Rows, data.Total, data.KeepZeroRows, settings.SortMode, settings.MaxRows);
            if (rows.Length == 0) rows = new[] { new DamageRowData(L("暂无记录", "No records"), 0) };
            changed |= SectionPool[index].Update(data?.Heading, rows, data?.Total ?? 0,
                settings, fields, language, ActiveRows);
        }
        for (var index = sectionCount; index < SectionPool.Count; index++)
        {
            changed |= SetVisible(SectionPool[index].Container, false);
        }
        if (RowSelection.SelectedKey is { } selected && !ActiveRows.Any(row => row.Key == selected))
        {
            RowSelection.Clear();
        }

        // 只测量失效缓存，所有可见行共用一组列宽；超宽数字裁切，完整值仍在提示/详情中。
        Array.Fill(ColumnWidths, 0f);
        foreach (var row in ActiveRows) row.IncludeColumnWidths(ColumnWidths);
        var available = (_contentScroll!.Size.X > 0 ? _contentScroll.Size.X : MinimumPanelWidth - 16)
            - _contentScroll.GetVScrollBar().GetCombinedMinimumSize().X;
        DpsHudRowFields.LimitWidths(ColumnWidths, available, fields, settings.FontScale);
        foreach (var row in ActiveRows) changed |= row.SetColumnWidths(ColumnWidths);
        return changed;
    }

    private static bool SetVisible(Control control, bool visible)
    {
        if (control.Visible == visible) return false;
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
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        private readonly List<DamageRowControls> _rows = new();
        private HudTheme? _theme;
        private float? _fontScale;

        public DamageSectionControls()
        {
            Container.AddThemeConstantOverride("separation", 4);
            _heading.AddThemeColorOverride("font_color", TitleColor);
            _heading.AddThemeColorOverride("font_outline_color", Palette.Outline);
            _heading.AddThemeConstantOverride("outline_size", DpsHudTypography.OutlineSize);
            _heading.AddThemeFontSizeOverride("font_size", FontSize(HudFontRole.ModuleHeading));
            Container.AddChild(_heading);
        }

        public bool Update(string? heading, IReadOnlyList<DamageRowData> rows,
            long total, DpsMeterSettingsData settings, HudRowField fields, string language,
            List<DamageRowControls> activeRows)
        {
            if (_theme != DpsHudThemes.Normalize(settings.HudTheme))
            {
                _theme = DpsHudThemes.Normalize(settings.HudTheme);
                _heading.AddThemeColorOverride("font_color", DpsHudThemes.Get(_theme.Value).Title);
            }
            var fontScale = DpsHudTypography.NormalizeScale(settings.FontScale);
            var fontChanged = _fontScale != fontScale;
            if (fontChanged)
            {
                _fontScale = fontScale;
                _heading.AddThemeFontSizeOverride("font_size", DpsHudTypography.Size(HudFontRole.ModuleHeading, fontScale));
            }
            var changed = SetVisible(Container, true) || fontChanged;
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
                var percentage = total > 0 ? Math.Clamp(rows[index].Damage / (double)total * 100, 0, 100) : 0;
                var key = $"{heading}:{rows[index].Key ?? rows[index].Info?.InternalId ?? rows[index].Label}";
                changed |= _rows[index].Update(rows[index], percentage, settings, fields, language, key, index + 1);
                activeRows.Add(_rows[index]);
            }
            for (var index = rows.Count; index < _rows.Count; index++) changed |= SetVisible(_rows[index].Root, false);
            return changed;
        }
    }

    private sealed class DamageRowControls
    {
        public VBoxContainer Root { get; } = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        public string Key { get; private set; } = string.Empty;
        private readonly Control _row = new() { MouseFilter = Control.MouseFilterEnum.Pass };
        private readonly ProgressBar _progress;
        private readonly TextureRect _icon;
        private readonly Control _nameColumn;
        private readonly Label _name;
        private readonly RichTextLabel _richName;
        private readonly Label[] _values = new Label[5];
        private readonly Label _details;
        private readonly float[] _measuredWidths = new float[5];
        private DamageRowData? _lastData;
        private double _lastPercentage;
        private bool _compact;
        private string? _lastLanguage;
        private (bool Icons, HudTooltipDetail Tooltip, HudRowField Fields, bool Details, bool Expanded, int Rank)? _lastPresentation;
        private bool _measurementsDirty = true;
        private HudTheme? _theme;
        private float? _fontScale;
        private bool _canExpand;
        private bool _pressed;

        public DamageRowControls()
        {
            Root.AddThemeConstantOverride("separation", 2);
            Root.AddChild(_row);
            _row.GuiInput += OnGuiInput;
            _row.MouseExited += () => _pressed = false;
            _progress = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 100,
                ShowPercentage = false,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            _progress.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _progress.AddThemeStyleboxOverride("background", DpsHudThemeStyles.Flat(BarBackground, Colors.Transparent, 4));
            _progress.AddThemeStyleboxOverride("fill", DpsHudThemeStyles.Flat(BarFill, Colors.Transparent, 4));
            _row.AddChild(_progress);

            var overlay = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            overlay.AddThemeConstantOverride("margin_left", 7);
            overlay.AddThemeConstantOverride("margin_right", 7);
            overlay.AddThemeConstantOverride("margin_top", 2);
            overlay.AddThemeConstantOverride("margin_bottom", 2);
            _row.AddChild(overlay);
            var columns = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            columns.AddThemeConstantOverride("separation", 6);
            overlay.AddChild(columns);

            _icon = new TextureRect
            {
                Name = "IconColumn",
                CustomMinimumSize = new Vector2(DpsHudSourceIcons.ColumnSize, DpsHudSourceIcons.ColumnSize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                SelfModulate = TitleColor,
                TextureFilter = CanvasItem.TextureFilterEnum.Linear,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            columns.AddChild(_icon);
            _nameColumn = new Control
            {
                CustomMinimumSize = new Vector2(48, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                ClipContents = true,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            columns.AddChild(_nameColumn);
            _name = CreateTextColumn("Name", HorizontalAlignment.Left);
            _name.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _nameColumn.AddChild(_name);
            _richName = new RichTextLabel
            {
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
            _richName.AddThemeColorOverride("font_outline_color", Palette.Outline);
            _richName.AddThemeConstantOverride("outline_size", DpsHudTypography.OutlineSize);
            _nameColumn.AddChild(_richName);
            for (var index = 0; index < _values.Length; index++)
            {
                _values[index] = CreateTextColumn(DpsHudRowFields.NumericFields[index].ToString(), HorizontalAlignment.Right);
                _values[index].ThemeChanged += InvalidateMeasurements;
                columns.AddChild(_values[index]);
            }
            _details = new Label
            {
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = false
            };
            _details.AddThemeColorOverride("font_color", MutedText);
            _details.AddThemeFontSizeOverride("font_size", FontSize(HudFontRole.Detail));
            Root.AddChild(_details);
        }

        public bool Update(DamageRowData data, double percentage, DpsMeterSettingsData settings,
            HudRowField fields, string language, string key, int rank)
        {
            var theme = DpsHudThemes.Normalize(settings.HudTheme);
            var themeChanged = _theme != theme;
            if (themeChanged) ApplyTheme(theme);
            var fontScale = DpsHudTypography.NormalizeScale(settings.FontScale);
            var fontChanged = _fontScale != fontScale;
            var compact = settings.CompactMode;
            var expanded = settings.EnableRowDetails && RowSelection.SelectedKey == key && data.Info is not null;
            var presentation = (settings.ShowSourceIcons, settings.TooltipDetailMode, fields,
                settings.EnableRowDetails, expanded, rank);
            var changed = SetVisible(Root, true);
            if (Key == key && _lastData == data && _lastPercentage == percentage && _compact == compact &&
                _lastLanguage == language && _lastPresentation == presentation && !themeChanged && !fontChanged) return changed;
            if (Key != key) _pressed = false;
            Key = key;
            _canExpand = settings.EnableRowDetails && data.Info is not null;
            if (!_canExpand) _pressed = false;
            if (_lastData is null || _compact != compact || fontChanged)
            {
                _fontScale = fontScale;
                var fontSize = DpsHudTypography.Size(compact ? HudFontRole.CompactBody : HudFontRole.Body, fontScale);
                var textHeight = _name.GetThemeFont("font").GetHeight(fontSize);
                _row.CustomMinimumSize = new Vector2(0, DpsHudTypography.RowHeight(compact, textHeight));
                _details.AddThemeFontSizeOverride("font_size", DpsHudTypography.Size(HudFontRole.Detail, fontScale));
                _name.AddThemeFontSizeOverride("font_size", fontSize);
                _richName.AddThemeFontSizeOverride("normal_font_size", fontSize);
                _richName.AddThemeFontSizeOverride("bold_font_size", fontSize);
                foreach (var value in _values) value.AddThemeFontSizeOverride("font_size", fontSize);
                _measurementsDirty = true;
                changed = true;
            }
            var showName = fields.HasFlag(HudRowField.Name);
            _nameColumn.CustomMinimumSize = new Vector2(DpsHudRowFields.NameMinimum(fields, fontScale), 0);
            _name.Text = showName ? data.Label : $"#{rank}";
            _richName.Text = showName && data.Details is not null ? BuildRichRowText(data, theme) : string.Empty;
            _name.Visible = !showName || data.Details is null;
            _richName.Visible = showName && data.Details is not null;
            var chinese = language == "zhs";
            for (var index = 0; index < _values.Length; index++)
            {
                var field = DpsHudRowFields.NumericFields[index];
                changed |= SetVisible(_values[index], fields.HasFlag(field));
                var text = DpsHudRowFields.Text(field, data.Damage, percentage, data.Info, chinese);
                if (_values[index].Text != text)
                {
                    _values[index].Text = text;
                    _measurementsDirty = true;
                }
            }
            if (_lastPresentation?.Fields != fields) { _measurementsDirty = true; changed = true; }
            _progress.Value = percentage;
            _icon.Texture = settings.ShowSourceIcons && data.Info?.SourceType is { } sourceType
                ? DpsHudSourceIcons.GetTexture(sourceType) : null;
            _row.TooltipText = data.Info is null ? string.Empty : DpsHudSourceInfo.Tooltip(
                data.Label, data.Damage, percentage, data.Info, settings.TooltipDetailMode, chinese);
            changed |= SetVisible(_details, expanded);
            if (expanded)
            {
                var details = DpsHudSourceInfo.Tooltip(data.Label, data.Damage, percentage,
                    data.Info, HudTooltipDetail.Detailed, chinese);
                if (_details.Text != details) { _details.Text = details; changed = true; }
            }
            _lastData = data;
            _lastPercentage = percentage;
            _compact = compact;
            _lastLanguage = language;
            _lastPresentation = presentation;
            return changed;
        }

        private void ApplyTheme(HudTheme theme)
        {
            _theme = theme;
            var palette = DpsHudThemes.Get(theme);
            _progress.AddThemeStyleboxOverride("background", DpsHudThemeStyles.Flat(palette.BarTrack, Colors.Transparent, 4));
            _progress.AddThemeStyleboxOverride("fill", DpsHudThemeStyles.Flat(palette.BarFill, Colors.Transparent, 4));
            _icon.SelfModulate = palette.Title;
            foreach (var label in _values.Append(_name))
            {
                label.AddThemeColorOverride("font_color", palette.Primary);
                label.AddThemeColorOverride("font_outline_color", palette.Outline);
            }
            _richName.AddThemeColorOverride("default_color", palette.Primary);
            _richName.AddThemeColorOverride("font_outline_color", palette.Outline);
            _details.AddThemeColorOverride("font_color", palette.Muted);
        }

        private void OnGuiInput(InputEvent input)
        {
            if (!_canExpand || input is not InputEventMouseButton button || button.ButtonIndex != MouseButton.Left) return;
            if (button.Pressed) _pressed = true;
            else if (_pressed)
            {
                _pressed = false;
                RowSelection.Toggle(Key);
                RequestImmediateRefresh();
            }
            // 来源行用于查看详情；拖动标题栏移动 HUD。滚轮仍交给外层滚动容器。
            _row.AcceptEvent();
        }

        public void IncludeColumnWidths(float[] widths)
        {
            if (_measurementsDirty)
            {
                for (var index = 0; index < _values.Length; index++)
                {
                    var value = _values[index];
                    _measuredWidths[index] = value.Visible ? Mathf.Ceil(value.GetThemeFont("font").GetStringSize(
                        value.Text, HorizontalAlignment.Left, -1, value.GetThemeFontSize("font_size")).X) : 0;
                }
                _measurementsDirty = false;
            }
            for (var index = 0; index < widths.Length; index++) widths[index] = Math.Max(widths[index], _measuredWidths[index]);
        }

        private void InvalidateMeasurements()
        {
            _measurementsDirty = true;
            RequestImmediateRefresh();
        }

        public bool SetColumnWidths(float[] widths)
        {
            var changed = false;
            for (var index = 0; index < _values.Length; index++)
            {
                if (_values[index].CustomMinimumSize.X == widths[index]) continue;
                _values[index].CustomMinimumSize = new Vector2(widths[index], 0);
                changed = true;
            }
            return changed;
        }

        private static Label CreateTextColumn(string name, HorizontalAlignment alignment)
        {
            var label = new Label
            {
                Name = name,
                HorizontalAlignment = alignment,
                VerticalAlignment = VerticalAlignment.Center,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            label.AddThemeColorOverride("font_color", PrimaryText);
            label.AddThemeColorOverride("font_outline_color", Palette.Outline);
            label.AddThemeConstantOverride("outline_size", DpsHudTypography.OutlineSize);
            return label;
        }
    }
}
