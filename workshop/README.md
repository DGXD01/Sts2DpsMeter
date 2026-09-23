# Sts2 DPS Meter - 轻量化战斗统计

Sts2 DPS Meter 是一款轻量化的《杀戮尖塔 2》战斗统计 Mod。它仅监听战斗事件，不修改卡牌、敌人、奖励或存档，也不会改变游戏平衡。

## 0.2.2 修复

- 修复多人模式下中毒、灾厄击杀等共享伤害被漏记或整段归给单一玩家的问题，现按玩家贡献份额拆分。
- 修复多人中毒与加速剂层数来源、格挡/有效伤害/溢出伤害拆分，以及异步效果来源串扰。
- 力量等增伤仍归因到原始攻击来源。

## 当前功能

- 常驻 HUD 体积小、半透明，可拖拽、缩放和锁定。
- 支持“本场战斗 / 本回合 / 总计”三档统计范围。
- 单人模式显示伤害来源；多人模式显示队伍汇总和玩家输出排行。
- 多人排行使用富文本按职业颜色区分玩家，本机玩家带有“（你）”标识。
- 默认仅启用核心伤害统计。承伤、格挡、能量使用，以及给队友能量、护盾、回血、抽牌等模块可在游戏内“设”面板独立开关。
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
