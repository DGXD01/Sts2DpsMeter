extends SceneTree

# 独立原生场景，读取生产七组文字与C#导出尺寸/字号；不启动游戏或写真实设置。
var failures: Array[String] = []
var config: Dictionary
var cases := 0

func _initialize() -> void:
    run.call_deferred()

func require(ok: bool, message: String) -> void:
    if not ok and failures.size() < 30:
        failures.append(message)

func label(text: String, size: int, wrap: bool = false) -> Label:
    var result := Label.new()
    result.text = text
    result.mouse_filter = Control.MOUSE_FILTER_IGNORE
    result.add_theme_font_size_override("font_size", size)
    if wrap:
        result.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
    return result

func add_item(content: VBoxContainer, item: Dictionary, font: Dictionary, chinese: bool) -> void:
    var text: String = item.cn if chinese else item.en
    if item.type == "Text":
        content.add_child(label(text, int(font.note), true))
    elif item.type == "Toggle":
        var toggle := CheckBox.new()
        toggle.text = text
        content.add_child(toggle)
    elif item.type == "Button":
        var button := Button.new()
        button.text = text
        content.add_child(button)
    else:
        var row := HBoxContainer.new()
        row.add_theme_constant_override("separation", 7)
        var title := label(text, int(font.body), true)
        title.custom_minimum_size = Vector2(150, 0)
        row.add_child(title)
        if item.type == "SliderRow":
            var slider := HSlider.new()
            slider.custom_minimum_size = Vector2(160, 0)
            slider.size_flags_horizontal = Control.SIZE_EXPAND_FILL
            row.add_child(slider)
            var number := label("1.00", int(font.body))
            number.custom_minimum_size = Vector2(58, 0)
            number.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
            row.add_child(number)
        else:
            var option := OptionButton.new()
            # 代表性选中项；原生菜单的真实事件/全部资源仍需游戏验收。
            option.add_item("详细" if chinese else "Detailed")
            option.size_flags_horizontal = Control.SIZE_EXPAND_FILL
            row.add_child(option)
        content.add_child(row)

func build(screen: Dictionary, font: Dictionary, chinese: bool) -> Dictionary:
    var panel := PanelContainer.new()
    var system_font := SystemFont.new()
    system_font.font_names = PackedStringArray(config.families)
    system_font.allow_system_fallback = true
    system_font.oversampling = 2.0
    var theme := Theme.new()
    theme.default_font = system_font
    theme.default_font_size = int(font.body)
    panel.theme = theme
    var style := StyleBoxFlat.new()
    style.content_margin_left = 12
    style.content_margin_right = 12
    style.content_margin_top = 10
    style.content_margin_bottom = 10
    panel.add_theme_stylebox_override("panel", style)
    panel.position = Vector2(float(screen.x), float(screen.y))
    panel.custom_minimum_size = Vector2(float(screen.panelWidth), float(screen.panelHeight))
    panel.size = panel.custom_minimum_size
    root.add_child(panel)
    var box := VBoxContainer.new()
    box.add_theme_constant_override("separation", 8)
    panel.add_child(box)
    box.add_child(label("Sts2 DPS Meter 设置" if chinese else "Sts2 DPS Meter Settings", int(font.title)))
    box.add_child(label("点击HUD齿轮打开或关闭设置。单人可切换本场战斗、本回合；多人增加全队汇总和玩家排行。各组选项可单独调整并保存。" if chinese else str(config.introduction), int(font.note), true))
    var scroll := ScrollContainer.new()
    scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
    scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
    box.add_child(scroll)
    var content := VBoxContainer.new()
    content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    content.add_theme_constant_override("separation", 7)
    scroll.add_child(content)
    var headings: Array[Label] = []
    for section in config.metrics.sections:
        var heading := label(str(section.cn if chinese else section.en), int(font.heading))
        content.add_child(heading)
        headings.append(heading)
        if section.id == "Presets":
            var grid := GridContainer.new()
            grid.columns = 2
            grid.add_theme_constant_override("h_separation", 8)
            grid.add_theme_constant_override("v_separation", 6)
            for preset in config.metrics.presets:
                var button := Button.new()
                button.text = str(preset.cn if chinese else preset.en)
                button.custom_minimum_size = Vector2(0, 34)
                button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
                grid.add_child(button)
            content.add_child(grid)
        for item in config.groups[section.id]:
            add_item(content, item, font, chinese)
    for item in config.groups.Footer:
        add_item(content, item, font, chinese)
    var last := label("settings.json", int(font.note), true)
    content.add_child(last)
    return { panel = panel, scroll = scroll, content = content, headings = headings, last = last }

func run() -> void:
    config = JSON.parse_string(FileAccess.get_file_as_string("res://production.json"))
    for screen in config.metrics.screens:
        root.size = Vector2i(int(screen.width), int(screen.height))
        for font in config.metrics.fonts:
            for chinese in [true, false]:
                var scene := build(screen, font, chinese)
                for i in range(4):
                    await process_frame
                var panel: PanelContainer = scene.panel
                var scroll: ScrollContainer = scene.scroll
                require(panel.position.x + panel.size.x <= float(screen.width) + 1.0 and panel.position.y + panel.size.y <= float(screen.height) + 1.0, "设置面板超出原生视口：" + str(font.scale))
                require(scroll.size.y >= 100, "设置正文必须保留可操作的滚动高度")
                require(scene.headings.size() == 7, "七组缺失")
                for heading in scene.headings:
                    require(heading.size.y >= heading.get_theme_font("font").get_height(int(font.heading)), "分组标题被上下裁切")
                for child in scene.content.get_children():
                    if child is Control:
                        require(child.position.x >= 0 and child.position.x + child.size.x <= scene.content.size.x + 1.0, "数据行/按钮横向溢出")
                require(scene.content.size.x <= scroll.size.x + 1, "内容不能依赖隐藏的横向滚动才能访问")
                scroll.scroll_vertical = int(scroll.get_v_scroll_bar().max_value)
                await process_frame
                var bottom: Control = scene.last
                require(bottom.get_global_rect().end.y <= scroll.get_global_rect().end.y + 1.0, "无法滚动到最后的设置项")
                var fraction := 0.65
                var limit := maxf(0, scroll.get_v_scroll_bar().max_value - scroll.get_v_scroll_bar().page)
                scroll.scroll_vertical = roundi(fraction * limit)
                await process_frame
                require(absf(scroll.scroll_vertical - fraction * limit) <= 1.0, "原生滚动比例恢复偏差")
                panel.free()
                cases += 1
    if not failures.is_empty():
        for failure in failures:
            printerr(failure)
        quit(1)
        return
    print("通过：", cases, "个独立原生设置场景，七组/六字号/四分辨率/双语，边界、标题高度、正文滚动及末项可达；真实Mod控件事件仍待实机验证。")
    quit(0)
