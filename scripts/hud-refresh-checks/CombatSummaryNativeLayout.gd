extends SceneTree

# 使用真实 Godot 控件验证原生排版，非游戏进程；实际产品设置由 PowerShell 从 C# 源码提取。
var failures: Array[String] = []
var production: Dictionary

func _initialize() -> void:
    run.call_deferred()

func require(value: bool, description: String) -> void:
    if not value:
        failures.append(description)

func make_panel(old: bool, lines: Array) -> PanelContainer:
    var panel := PanelContainer.new()
    panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
    var style := StyleBoxFlat.new()
    style.content_margin_left = 12
    style.content_margin_right = 12
    style.content_margin_top = 10
    style.content_margin_bottom = 10
    panel.add_theme_stylebox_override("panel", style)
    root.add_child(panel)
    var layout := VBoxContainer.new()
    layout.mouse_filter = Control.MOUSE_FILTER_IGNORE
    layout.add_theme_constant_override("separation", 5)
    panel.add_child(layout)
    for text in lines:
        var label := Label.new()
        label.text = text
        label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
        label.max_lines_visible = int(production.max_lines)
        label.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS if old or production.overrun == "TrimEllipsis" else TextServer.OVERRUN_NO_TRIMMING
        label.mouse_filter = Control.MOUSE_FILTER_IGNORE
        label.add_theme_font_size_override("font_size", 16)
        layout.add_child(label)
    return panel

func size_like_production(panel: PanelContainer, width: float, zoom: float) -> void:
    panel.custom_minimum_size = Vector2(width, 0)
    panel.size = Vector2(width, 0)
    panel.scale = Vector2.ONE * zoom

func rows(panel: PanelContainer) -> Array:
    var result := []
    for label in panel.get_child(0).get_children():
        result.append({"height": label.size.y, "visible": label.get_visible_line_count()})
    return result

func run() -> void:
    production = JSON.parse_string(FileAccess.get_file_as_string("res://production-label.json"))
    require(production.wrap == "WordSmart", "测试场景须覆盖实际产品的 WordSmart 自动换行")
    require(production.overrun in ["NoTrimming", "TrimEllipsis"], "未支持的产品省略设置，需更新原生检查")
    var chinese := ["战斗总结 · 本场战斗", "总伤害 96 · DPS 2.4", "最高来源：火焰药水（20）", "最大单次：20（火焰药水）", "回合数 2 · 耗时 39.2 秒"]
    var english := ["Combat summary · This combat", "Damage 96 · DPS 2.4", "Top source: Fire Potion (20)", "Max hit: 20 (Fire Potion)", "Turns 2 · Time 39.2s"]
    var old := make_panel(true, chinese)
    for frame in range(6):
        size_like_production(old, 420, 1)
        await process_frame
    var old_rows := rows(old)
    require(old.size.y == 45 and old_rows.all(func(row): return row.height == 1 and row.visible == 0), "旧设置必须重现 45px 空框及五行 1px/不可见，确保本检查能捕获该缺陷")
    print("旧布局复现：", JSON.stringify({"panel_height": old.size.y, "rows": old_rows}))
    old.queue_free()

    var cases := 0
    for lines in [chinese, english, [chinese[0], chinese[1], "最高来源：" + "长来源".repeat(33), "最大单次：" + "LongSource".repeat(11), chinese[4]]]:
        var panel := make_panel(false, lines)
        for width in [160.0, 240.0, 360.0, 420.0, 740.0]:
            for zoom in [0.5, 1.0, 2.0]:
                for frame in range(6):
                    size_like_production(panel, width, zoom)
                    await process_frame
                for label in panel.get_child(0).get_children():
                    require(label.size.y >= label.get_line_height(), "实际产品设置不得把文字压到字体高度以下")
                    require(label.get_visible_line_count() >= 1, "中文/英文/长来源及缩放后每个总结字段至少可见一行")
                    require(label.get_visible_line_count() <= int(production.max_lines), "保持最多两行限制")
                    require(label.mouse_filter == Control.MOUSE_FILTER_IGNORE, "修复不得破坏鼠标穿透")
                require(panel.size.y >= 5 * 16 + 40, "总结面板必须容纳五行与边距/间距")
                cases += 1
        var before := panel.size.y
        panel.hide()
        await process_frame
        panel.show()
        for frame in range(6):
            size_like_production(panel, 740, 2)
            await process_frame
        require(panel.size.y == before and rows(panel).all(func(row): return row.visible >= 1), "隐藏/恢复后不能再次变空框或高度漂移")
        panel.queue_free()
    if failures.is_empty():
        print("通过：旧空框复现、实际产品设置的 ", cases, " 个中文/英文/长来源/宽度/缩放布局，以及重复缩高、两行上限、鼠标属性和隐藏恢复。")
        quit(0)
    else:
        for failure in failures:
            push_error(failure)
        quit(1)
