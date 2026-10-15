# Sts2 DPS Meter - 轻量化战斗统计

Sts2 DPS Meter 是一款轻量化的《杀戮尖塔 2》战斗统计 Mod。它仅监听战斗事件，不修改卡牌、敌人、奖励或存档，也不会改变游戏平衡。

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
