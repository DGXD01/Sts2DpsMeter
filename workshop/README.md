# Sts2 DPS Meter - 轻量化战斗统计

Sts2 DPS Meter 是一款轻量化的《杀戮尖塔 2》战斗统计 Mod。它仅监听战斗事件，不修改卡牌、敌人、奖励或存档，也不会改变游戏平衡。

## 0.2.7 测试版更新

0.2.7 测试版：来源图标修复与设置页清晰度调整

1. 修复勾选“显示来源图标”后图标仍为空白的问题：用缓存的图形纹理替换字体特殊字符。卡牌显示卡片轮廓，其他类型使用对应小图标；不依赖系统符号字体，关闭后仍保持列对齐。
2. 图标采用透明背景与高分辨率采样，按类型复用；未知类型及解码异常有问号图形回退，不拦截悬停、点击或滚动。
3. 设置页沿用已获用户确认的 HUD 字体和 2 倍采样，标题 18 号、主选项及数值 16 号、说明 14 号；同步改善下拉菜单、对比度、换行和滚动区域。
4. 保留来源悬停三级提示、点击展开/收起和六字段配置。设置 Schema 仍为 5，不改变伤害归因、统计口径或范围含义。

已有用户反馈：0.2.6 的 HUD 清晰度、单人悬停与提示详细度、行详情及关闭、字段开关及全关回退、范围切换、新战斗和设置保存正常。
验证：真实 v0.107.1 程序集构建、刷新/布局/配置/快照回归、十类图标及 30 个离线渲染场景通过；保留两条既有归因空引用警告。
本版图标修复和设置页清晰度仍待更新后的游戏内复验；多人、完整分辨率与缩放矩阵未验收。第二阶段尚未完整验收，第三阶段未开始。

## 0.2.6 测试版更新

0.2.6 测试版：HUD 文字清晰度调整

1. HUD 使用独立系统字体，优先微软雅黑 UI / 微软雅黑，保留中英文及符号回退；启用 2 倍采样与字形微调，不修改游戏共享字体或全局渲染设置。
2. 正常正文和数字由 13 号增至 16 号，紧凑模式由 12 号增至 14 号；标题、范围和详情字号同步调整，并微调行高避免挤压。
3. 文字描边减至 1 像素，提亮标题蓝色、箭头和次要文字，正文使用白色，提高辨识度。
4. 单人模式隐藏无效的玩家切换箭头，为标题留出空间；多人保留原有切换逻辑。
5. 面板布局后的原点对齐整像素，右/下边界向内取整，避免小数保存位置带来的模糊和越屏。

保留 0.2.5 的来源图标、三级 Tooltip、点击行详情及六字段配置，设置 Schema 仍为 5；不改变伤害归因、统计口径或 Scope 语义。
验证：真实 v0.107.1 程序集构建及格式/仓库检查通过；362 个布局场景（含 48 个像素边界场景）、64 种字段组合、256 个列宽场景与既有刷新及统计展示回归通过。保留 2 条既有 CS8602 警告。
本次仍为测试版：实际字体观感、2K/4K 与各档缩放、图标、标题裁切和多人交互尚待游戏内对照，不能保证所有环境的清晰度一致。欢迎反馈截图和日志。

## 0.2.5 测试版更新

0.2.5 测试版：来源展示与详情增强（UI 重构第 2 步）

1. 新增来源类型图标，区分卡牌、状态/能力、遗物、药水、充能球、宠物、事件等；未知来源安全回退，可在设置中关闭，图标列保持固定宽度。
2. 新增来源行悬停提示：简洁、详细、开发者三级。详细模式展示快照实际具备的命中、最大、平均及伤害拆分；归因置信度、追溯状态和内部 ID 仅在开发者模式显示。
3. 点击统计行展开或收起详情，同时最多展开一行；切换范围、玩家或进入新战斗时清除展开状态。拖动标题栏移动 HUD，滚轮查看长明细。
4. 新增名称、数值、百分比、命中次数、最大伤害、平均伤害六个显示字段开关。默认保持名称+数值+百分比；隐藏名称时保留行号并可悬停识别来源，字段全部关闭时保留名称。
5. 设置 Schema 升级为 5，保留旧配置已有值；来源、回合卡牌和机制快照补充最大单次记录，不改变伤害归因或统计总量口径。

验证：使用 v0.107.1 游戏程序集构建；刷新并发、314 个布局计算场景、64 种字段组合、256 个列宽场景及真实累加器/回合行/配置 JSON 往返检查通过。保留 2 条既有 CS8602 警告。
本次仍为测试版：原生图标字体、Tooltip、点击/滚动/拖动、不同分辨率及多人实机验收尚未完成，不代表 14 项增强功能全部完成。欢迎反馈问题和日志。
兼容性：旧版本 Schema 4 不接受 Schema 5 配置；如需回退旧版，请保留并恢复旧设置备份。

## 0.2.4 测试版更新

0.2.4 测试版：HUD 刷新入口修复与第 1 步完善

1. 针对 0.2.3 仅显示标题栏、伤害明细空白的问题，刷新入口改为直接连接 SceneTree.ProcessFrame 原生信号，移除对未生成引擎绑定的自定义 _Process 回调的依赖。
2. 明细区域支持垂直滚动并限制高度，保留固定标题和设置入口，避免多模块或大量数据行将面板撑出视口。
3. 切换统计范围、玩家或进入新战斗时回到明细顶部；普通伤害刷新保留滚动位置。
4. 缓存数字列宽度测量并复用可见行列表，语言及字体主题变化时更新相关缓存。
5. 保留 0.2.3 的齿轮设置入口、紧凑列布局、节点复用和同帧刷新合并。

验证：使用 v0.107.1 游戏程序集构建；刷新并发、实际 Host 调度及 314 个布局计算场景检查通过。保留 2 条原有归因空引用警告。
本次仍为 UI 重构第 1 步测试版，尚未完成游戏内单人/多人显示与交互验收；不代表 14 项增强功能全部完成，也不宣称空白问题已通过实机验收。欢迎反馈问题和日志。

## 0.2.3 测试版更新（历史，曾因明细空白回滚）

0.2.3 测试版：HUD 布局与刷新基础（UI 重构第 1 步）

1. 设置入口由“设/S”改为齿轮（⚙），同步中英文说明。
2. 缩小标题字号、圆角和面板边距，长标题支持裁切及悬停查看全文。
3. 名称列伸缩裁切，伤害和百分比列统一对齐，并预留固定图标位置。
4. 统计分组与数据行改为节点复用，未变化的行不重复更新。
5. 同帧刷新请求由主线程合并处理，面板尺寸上限计入界面缩放。

本次仅包含第 1 步基础改动，不代表 14 项增强功能全部完成；来源图标、行详情及自定义字段开关等将在后续阶段实现。
验证情况：真实 v0.107.1 程序集构建、格式、仓库校验和刷新并发检查通过；仍有 2 条已确认属于原代码的归因空引用警告。新版单人/多人实机验收尚未完成，请作为测试版使用，欢迎反馈问题。

## 0.2.2 修复

- 修复多人模式下中毒、灾厄击杀等共享伤害被漏记或整段归给单一玩家的问题，现按玩家贡献份额拆分。
- 修复多人中毒与加速剂层数来源、格挡/有效伤害/溢出伤害拆分，以及异步效果来源串扰。
- 力量等增伤仍归因到原始攻击来源。

## 当前功能

- 常驻 HUD 体积小、半透明，可拖拽、缩放和锁定。
- 支持“本场战斗 / 本回合 / 总计”三档统计范围。
- 单人模式显示伤害来源；多人模式显示队伍汇总和玩家输出排行。
- 多人排行使用富文本按职业颜色区分玩家，本机玩家带有“（你）”标识。
- 默认仅启用核心伤害统计。承伤、格挡、能量使用，以及给队友能量、护盾、回血、抽牌等模块可在游戏内齿轮（⚙）设置面板独立开关。
- 队友增益不计入伤害、DPS、DPT 或来源伤害排行。
- Mod 界面会自动跟随游戏语言：简体中文环境显示中文，其他语言显示英文，无需单独安装英文版或手动切换。

## 上传内容

构建脚本会把以下文件复制到上传工作区：

- `Sts2DpsMeter.dll`
- `manifest.json`
- `workshop.json`
- `README.md`
- `image.png`
- `previews/` 下的实机运行截图

游戏自带的 `sts2.dll`、`GodotSharp.dll` 和 `0Harmony.dll` 只用于编译，不进入创意工坊内容。

## English

Sts2 DPS Meter is a lightweight combat statistics mod for Slay the Spire 2. It listens to combat events only: it does not modify cards, enemies, rewards, or saves, and does not affect game balance.

## Version 0.2.2 Fixes

- Fixed multiplayer shared damage such as Poison and Doom kills being dropped or assigned to a single player; damage is now split by player contribution.
- Fixed Poison/Accelerant ownership, per-player Block, effective damage, overkill allocation, and asynchronous effect-source interference.
- Strength and similar damage bonuses remain attributed to the original attack source.

The compact, semi-transparent HUD can be dragged, scaled, and locked. It supports three scopes: Current Combat, Current Turn, and Total. Single-player mode shows damage sources; multiplayer mode automatically switches to a team summary and player rankings, using rich text and class colors to distinguish players.

Only core damage tracking is enabled by default. Damage taken, Block, Energy spent, and support metrics for allies (Energy, Block, Healing, and Cards Drawn given to allies) can be enabled independently from the in-game Settings panel. Ally support is not counted toward damage, DPS, DPT, or damage-source rankings.

Mod text automatically follows the game language. Simplified Chinese is used when the game is set to Chinese; all other languages use English. No separate English build or manual language switch is required.
