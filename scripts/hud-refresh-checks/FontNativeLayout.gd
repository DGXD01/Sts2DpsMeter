extends SceneTree

# 独立场景使用C#导出的字号/宽度预算及产品字体名，不启动游戏或加载Mod原生控件。
var failures: Array[String] = []
var cases := 0
var config: Dictionary

func _initialize() -> void:
    run.call_deferred()

func require(value: bool, message: String) -> void:
    if not value and failures.size() < 30:
        failures.append(message)

func font_theme(metric: Dictionary) -> Theme:
    var font := SystemFont.new()
    font.font_names = PackedStringArray(config.families)
    font.allow_system_fallback = true
    font.oversampling = 2.0
    var bold := font.duplicate() as SystemFont
    bold.font_weight = 700
    var theme := Theme.new()
    theme.default_font = font
    theme.default_font_size = int(metric.sizes.Body)
    theme.set_font("normal_font", "RichTextLabel", font)
    theme.set_font("bold_font", "RichTextLabel", bold)
    theme.set_font_size("normal_font_size", "RichTextLabel", int(metric.sizes.Body))
    theme.set_font_size("bold_font_size", "RichTextLabel", int(metric.sizes.Body))
    for type in ["Label", "RichTextLabel", "Button", "OptionButton"]:
        theme.set_constant("outline_size", type, int(metric.outline))
    return theme

func make_panel(theme: Theme) -> PanelContainer:
    var panel := PanelContainer.new()
    panel.theme = theme
    var style := StyleBoxFlat.new()
    style.content_margin_left = 8
    style.content_margin_right = 8
    style.content_margin_top = 6
    style.content_margin_bottom = 6
    panel.add_theme_stylebox_override("panel", style)
    root.add_child(panel)
    return panel

func make_hud(metric: Dictionary, layout: Dictionary, compact: bool, chinese: bool, full: bool, theme: Theme) -> Dictionary:
    var panel := make_panel(theme)
    var box := VBoxContainer.new()
    box.add_theme_constant_override("separation", 4)
    panel.add_child(box)
    var header := HBoxContainer.new()
    header.add_theme_constant_override("separation", 4)
    box.add_child(header)
    for text in ["◀", "本场战斗 · 伤害 1234 · DPS 56.7" if chinese else "This Combat · Damage 1234 · DPS 56.7", "▶", "scope", "⚙", "−"]:
        if "Damage" in text or "伤害" in text:
            var title := Label.new()
            title.text = text
            title.clip_text = true
            title.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
            title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
            title.add_theme_font_size_override("font_size", int(metric.sizes.Header))
            header.add_child(title)
        elif text == "scope":
            var option := OptionButton.new()
            option.add_item("本场战斗" if chinese else "This Combat")
            option.custom_minimum_size = Vector2(104, 30)
            option.add_theme_font_size_override("font_size", int(metric.sizes.CompactBody))
            header.add_child(option)
        else:
            var button := Button.new()
            button.text = text
            button.custom_minimum_size = Vector2(30, 30)
            button.add_theme_font_size_override("font_size", int(metric.sizes.Navigation))
            header.add_child(button)
    var heading := Label.new()
    heading.text = "造成伤害" if chinese else "Damage dealt"
    heading.add_theme_font_size_override("font_size", int(metric.sizes.ModuleHeading))
    box.add_child(heading)
    var rows := []
    var size: int = int(metric.sizes.CompactBody if compact else metric.sizes.Body)
    var row_font: Font = theme.default_font
    for rank in range(3):
        var row := Control.new()
        row.custom_minimum_size = Vector2(0, ceil(max(26 if compact else 30, row_font.get_height(size) + float(metric.rowPadding))))
        box.add_child(row)
        var overlay := MarginContainer.new()
        row.add_child(overlay)
        overlay.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
        overlay.add_theme_constant_override("margin_left", 7)
        overlay.add_theme_constant_override("margin_right", 7)
        overlay.add_theme_constant_override("margin_top", 2)
        overlay.add_theme_constant_override("margin_bottom", 2)
        var columns := HBoxContainer.new()
        columns.add_theme_constant_override("separation", 6)
        overlay.add_child(columns)
        var icon := Control.new()
        icon.custom_minimum_size = Vector2(16, 16)
        columns.add_child(icon)
        var name_column := Control.new()
        name_column.custom_minimum_size = Vector2(float(metric.nameMinimum), 0)
        name_column.size_flags_horizontal = Control.SIZE_EXPAND_FILL
        name_column.clip_contents = true
        columns.add_child(name_column)
        var name: Control
        if rank == 1:
            var rich := RichTextLabel.new()
            rich.bbcode_enabled = true
            rich.text = "[b]很长的玩家名称[/b]" if chinese else "[b]Long player name[/b]"
            rich.scroll_active = false
            rich.autowrap_mode = TextServer.AUTOWRAP_OFF
            rich.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
            rich.add_theme_font_size_override("normal_font_size", size)
            rich.add_theme_font_size_override("bold_font_size", size)
            name = rich
        else:
            var label := Label.new()
            label.text = "火焰药水与超级长来源" if chinese else "A very long source name"
            label.clip_text = true
            label.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
            label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
            label.add_theme_font_size_override("font_size", size)
            name = label
        name_column.add_child(name)
        name.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
        var caps: Array = layout.five if full else layout.two
        var labels := []
        var texts := ["1234", "68%", "12次" if chinese else "12h", "峰234" if chinese else "↑234", "均12.5" if chinese else "~12.5"]
        for index in range(5 if full else 2):
            var value := Label.new()
            value.text = texts[index]
            value.clip_text = true
            value.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
            value.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
            value.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
            value.add_theme_font_size_override("font_size", size)
            value.custom_minimum_size = Vector2(float(caps[index]), 0)
            columns.add_child(value)
            labels.append(value)
        rows.append({"row": row, "name": name, "labels": labels})
    panel.custom_minimum_size = Vector2(float(layout.width), 0)
    panel.size = Vector2(float(layout.width), 0)
    panel.scale = Vector2.ONE * float(layout.ui)
    return {"panel": panel, "header": header, "rows": rows}

func check_hud(scene: Dictionary, context: String) -> void:
    var panel: PanelContainer = scene.panel
    require(panel.size.x * panel.scale.x <= root.size.x + 1, "面板横向越界 " + context)
    require(panel.size.y * panel.scale.y <= root.size.y + 1, "三个来源行和标题不应超过视口 " + context)
    for control in scene.header.get_children():
        require(control.position.x + control.size.x <= scene.header.size.x + 1, "标题操作控件被挤出 " + context)
    for row in scene.rows:
        var name: Control = row.name
        if name is Label:
            require(name.get_visible_line_count() >= 1 and name.size.y >= name.get_line_height(), "普通名称字号被压空 " + context)
        else:
            require(name.get_visible_line_count() >= 1 and name.size.y + 1 >= name.get_content_height(), "职业色富文本行高不足 " + context)
        for index in range(row.labels.size()):
            var value: Label = row.labels[index]
            require(value.get_visible_line_count() >= 1 and value.size.y >= value.get_line_height(), "数字行高不足 " + context)
            require(value.get_global_rect().end.x <= panel.get_global_rect().end.x + 1, "数值列越界 " + context)
            var first: Label = scene.rows[0].labels[index]
            require(abs(first.get_global_rect().end.x - value.get_global_rect().end.x) <= 0.1, "同模块数字列不齐 " + context)

func check_overlays(metric: Dictionary, chinese: bool, theme: Theme) -> void:
    var panel := make_panel(theme)
    var box := VBoxContainer.new()
    panel.add_child(box)
    var texts := ["战斗总结 · 本场战斗", "总伤害 1234 · DPS 56.7", "最高来源：" + "火焰药水".repeat(15), "最大单次：234（火焰药水）", "回合数 8 · 耗时 75.3 秒"] if chinese else ["Combat summary · This Combat", "Damage 1234 · DPS 56.7", "Top source: " + "Long source ".repeat(15), "Max hit: 234 (Fire Potion)", "Turns 8 · Time 75.3s"]
    for text in texts:
        var label := Label.new()
        label.text = text
        label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
        label.max_lines_visible = int(config.maxLines)
        label.text_overrun_behavior = TextServer.OVERRUN_NO_TRIMMING
        label.add_theme_font_size_override("font_size", int(metric.sizes.Body))
        box.add_child(label)
    panel.custom_minimum_size = Vector2(float(metric.summaryWidth), 0)
    panel.size = Vector2(float(metric.summaryWidth), 0)
    for frame in range(3):
        await process_frame
    for label in box.get_children():
        require(label.get_visible_line_count() >= 1 and label.get_visible_line_count() <= 2, "总结字号放大后空白/超两行")
    panel.queue_free()
    await process_frame

func run() -> void:
    config = JSON.parse_string(FileAccess.get_file_as_string("res://production.json"))
    for metric in config.metrics:
        var theme := font_theme(metric)
        for layout in metric.layouts:
            root.size = Vector2i(int(layout.screen[0]), int(layout.screen[1]))
            for compact in [false, true]:
                for chinese in [false, true]:
                    for full in [false, true]:
                        var scene := make_hud(metric, layout, compact, chinese, full, theme)
                        for frame in range(3):
                            await process_frame
                        check_hud(scene, "%s/%s/%s/%s/%s" % [metric.fontScale, layout.ui, layout.screen, compact, chinese])
                        scene.panel.queue_free()
                        await process_frame
                        cases += 1
        for chinese in [false, true]:
            await check_overlays(metric, chinese, theme)
    if failures.is_empty():
        print("通过：", cases, "个独立原生字号/UiScale/1080p–4K/中英文/紧凑/六字段布局，普通与富文本行高、数字对齐、标题控件和12个大字总结场景。")
        quit(0)
    else:
        for failure in failures:
            push_error(failure)
        quit(1)
