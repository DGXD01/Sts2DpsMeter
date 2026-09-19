# Sts2DpsMeter

《杀戮尖塔2》的 DPS 统计 Mod。仓库用于跟踪源码、构建流程、版本变更和 Steam 创意工坊更新。

## 当前状态

| 项目 | 状态 |
| --- | --- |
| GitHub 仓库 | 已创建，私有 |
| 当前开发分支 | `feature/DpsMeterCore` |
| 当前 Mod 版本 | `0.2.1` 铁甲战士测试版 |
| 目标游戏版本 | `0.107.1` |
| 真实游戏程序集编译 | 已通过：Release、0 警告、0 错误 |
| 静默猎手单人卡牌 | 静态伤害路径已覆盖；游戏内运行态待验证 |
| 储君单人卡牌 | 静态伤害路径已覆盖；游戏内运行态待验证 |
| 亡魂契约师单人卡牌 | 静态伤害路径已覆盖；游戏内运行态待验证 |
| 故障机器人单人卡牌 | 静态伤害路径已覆盖；游戏内运行态待验证 |
| 游戏内运行 | 未验证 |
| Steam 上传 | 未执行 |
| 许可证 | 尚未确定，公开发布前必须补充 |

> 程序集编译通过不等于运行验证通过。当前版本可以作为开发版继续检查，但在完成游戏内验证前不要标记为“已发布”或“可稳定安装”。

## 当前功能

- 统计玩家和宠物对敌人造成的输出伤害；宠物伤害归因给宠物主人。
- 统计面板的“当前”范围按 `格挡伤害 + 未格挡伤害 + 过量伤害` 汇总，并按卡牌名、状态、遗物、药水、充能球、宠物和其他来源划分。
- 面板切换到“受到”范围后，统一汇总敌方造成、自损、格挡吸收、生命伤害、过量伤害和命中次数；切换到“同时”后可并列查看造成伤害与受到伤害。
- 通过伤害 Hook、模型上下文栈、最近模型和伤害来源状态回退，识别卡牌、状态、遗物、药水、充能球、宠物、敌方和事件来源。
- 支持中毒、火焰、闪电、束缚、反伤、充能球被动/激发等效果的语义标签。
- 连锁伤害优先归因实际直接来源，并将触发卡牌作为二级信息。
- HUD 使用单个深色半透明圆角面板，以红色进度条展示来源伤害；标题左右为玩家切换箭头，右上角下拉框可选择“当前”“本回合”“受到”“总计”“同时”，最右侧 `+ / −` 按钮用于折叠或展开来源明细。
- 卡牌、遗物、药水、状态和机制伤害统一并入玩家造成伤害的来源划分；卡牌按稳定卡牌 ID 聚合，同名卡牌合并为一个统计类型。
- 已补充静默猎手单人卡牌的静态伤害路径覆盖：88 张卡牌中 27 张直接伤害卡，Shiv 独立统计，中毒来源暂为 `Deferred`，Abrasive/Outbreak/SerpentForm/Speedster/Strangle 的能力伤害按对应规则归因；详见 [docs/静默猎手伤害覆盖.md](docs/静默猎手伤害覆盖.md)。
- 已补充储君单人卡牌的静态伤害路径覆盖：核对 88 张卡牌，整理 32 张直接伤害卡、`SovereignBlade` / `MinionStrike` / `MinionDiveBomb` 派生统计，以及 `BlackHolePower` / `ReflectPower` 两条能力间接伤害；详见 [docs/储君伤害覆盖.md](docs/储君伤害覆盖.md)。
- 已补充亡魂契约师单人卡牌的静态伤害路径覆盖：核对 88 张卡牌，整理 24 张直接伤害卡、12 张 Osty 攻击卡和 `SweepingGaze` Token，补充 `HauntPower` / `NecroMasteryPower` / `SleightOfFleshPower` 三条唯一来源映射，并新增灾厄击杀统计：仅当灾厄层数达到敌方当前生命值且实际触发击杀时，按击杀前 HP 记入 Mod 统计，不修改游戏战斗数值；详见 [docs/亡魂契约师伤害覆盖.md](docs/亡魂契约师伤害覆盖.md)。
- 已补充故障机器人单人卡牌的静态伤害路径覆盖：核对 88 张卡牌，整理 29 张直接伤害卡、20 张通道卡、4 张激发卡和 Lightning / Dark / Glass 充能球伤害，并补充 `HailstormPower` / `SmokestackPower` / `ThunderPower` 三条能力间接伤害映射；详见 [docs/故障机器人伤害覆盖.md](docs/故障机器人伤害覆盖.md)。
- HUD 不使用键盘热键；所有交互控件禁用键盘焦点并仅响应鼠标，点击标题栏的“设”按钮打开或关闭设置面板，未锁定时可拖动 HUD 并保存位置。
- HUD 支持 `50%` 至 `200%` 缩放，只通过设置面板中的“界面缩放”滑块调整，不使用鼠标滚轮组合键。
- 每次伤害事件被成功归类后立即请求刷新：主线程直接刷新，后台线程安全投递到 Godot 主线程；同一帧内的连锁伤害合并为一次刷新。
- 战斗结束时向 Godot 日志输出输出、承伤和自损明细。
- 暂停游戏时停止累计 DPS 计时。

当前不会修改战斗数值、卡牌、敌人或存档。部分没有稳定模型上下文的间接伤害仍会标记为推断或未知，需要运行态验证。

## 界面与设置

设置文件保存到：

```text
OS.GetUserDataDir()/Sts2DpsMeter/settings.json
```

可在 HUD 的“设”按钮打开的设置面板中调整：

- 启动时显示 HUD、位置锁定和紧凑模式；
- “玩家造成伤害”和“玩家受到伤害”下拉范围是否可用，以及“同时”范围是否正确并列显示两组明细；
- 伤害来源显示行数；
- 面板透明度、界面缩放（`50%` 至 `200%`）；
- HUD 位置重置、HUD 大小重置为 `100%` 和全部推荐值恢复。

推荐默认值为伤害和受到范围均可用、最多显示 5 条来源、透明度 `0.92`、缩放 `1.0`。界面缩放请在设置面板中调整；锁定位置后不能拖动 HUD。

## 目录结构

```text
.
├── .github/
│   ├── CODEOWNERS
│   ├── pull_request_template.md
│   └── workflows/validate.yml
├── docs/
│   ├── 技术债.md
│   ├── 开发与更新流程.md
│   ├── 储君伤害覆盖.md
│   ├── 亡魂契约师伤害覆盖.md
│   ├── 故障机器人伤害覆盖.md
│   └── 静默猎手伤害覆盖.md
├── scripts/
│   ├── build-and-stage.ps1
│   └── validate-repo.ps1
├── workshop/
│   ├── image.png                # 上传封面占位图，发布前替换
│   ├── README.md                # 会随 ZIP 一起提供
│   └── workshop.json
├── DpsAttribution.cs            # 伤害来源识别、分类和置信度
├── DpsHook.cs                   # 战斗 Hook 接入
├── DpsMeterSettings.cs          # 设置数据、推荐值和持久化
├── DpsMeterSettingsUi.cs        # 原生设置面板
├── DpsMeterUi.cs                # 深色圆角 HUD、范围选择和设置入口
├── DpsModels.cs                 # 快照模型
├── DpsSourceContext.cs          # 模型上下文栈和最近来源追踪
├── DpsStats.cs                  # 线程安全统计计算
├── ModEntry.cs                  # Mod 初始化和生命周期
├── Sts2DpsMeter.csproj
└── manifest.json
```

## 开发环境

### 必需

- Windows
- .NET SDK 9 或更高版本
- 已安装《杀戮尖塔2》
- Steam 客户端，仅在上传创意工坊时需要

### 编译引用

`Sts2DpsMeter.csproj` 从游戏目录读取以下程序集，但不会把它们打包进仓库或 Steam 内容：

- `sts2.dll`
- `GodotSharp.dll`
- `0Harmony.dll`

如果游戏不在默认 Steam 路径，设置环境变量：

```powershell
$env:STS2_GAME_DIR = 'E:\STEAM\steam\steamapps\common\Slay the Spire 2'
```

本机已确认的测试路径为：

```text
E:\STEAM\steam\steamapps\common\Slay the Spire 2
```

## 本地验证与打包

先执行静态校验：

```powershell
pwsh -NoProfile -File .\scripts\validate-repo.ps1
```

校验通过后执行 Release 构建、暂存和 ZIP 打包：

```powershell
pwsh -NoProfile -File .\scripts\build-and-stage.ps1 -GameDir 'E:\steam\steam\steamapps\common\Slay the Spire 2'
```

只构建、不暂存或只跳过 ZIP：

```powershell
pwsh -NoProfile -File .\scripts\build-and-stage.ps1 -SkipStage
pwsh -NoProfile -File .\scripts\build-and-stage.ps1 -SkipArchive
```

脚本会：

1. 自动查找 Steam 库或使用 `STS2_GAME_DIR`；
2. 检查游戏程序集是否存在；
3. 运行 `dotnet build`；
4. 将 `Sts2DpsMeter.dll` 和 `manifest.json` 复制到 `workshop/content/`；
5. 检查封面是否为小于 1 MB 的 PNG；
6. 生成创意工坊 ZIP，并检查必需条目和禁止文件。

当前产物：

```text
artifacts/build/Sts2DpsMeter.dll
artifacts/workshop/Sts2DpsMeter-Ironclad-Test-0.2.1.zip
```

ZIP 根目录结构：

```text
content/Sts2DpsMeter.dll
content/manifest.json
workshop.json
image.png
README.md
```

`artifacts/` 已加入 `.gitignore`，不会提交到 GitHub。

## 版本与更新规则

- Mod 版本使用三段式语义化版本，例如 `0.2.1`。
- `manifest.json` 的 `version` 与项目文件 `<Version>` 必须一致。
- 每次发布前更新 `CHANGELOG.md`。
- 首次 Steam 上传后，上传器生成的 `mod_id.txt` 必须保留并提交，后续更新依靠它定位已有创意工坊条目。
- Steam 上传只允许在当前登录 Steam 的 Windows 机器上手动执行；不要把 Steam 凭据或上传器日志提交到仓库。
- 不要在仓库中提交游戏自带的 DLL、PCK、密钥、缓存或构建产物。

完整流程见 [docs/开发与更新流程.md](docs/开发与更新流程.md)，已知问题见 [docs/技术债.md](docs/技术债.md)，静默猎手单人卡牌覆盖见 [docs/静默猎手伤害覆盖.md](docs/静默猎手伤害覆盖.md)，储君单人卡牌覆盖见 [docs/储君伤害覆盖.md](docs/储君伤害覆盖.md)，亡魂契约师单人卡牌覆盖见 [docs/亡魂契约师伤害覆盖.md](docs/亡魂契约师伤害覆盖.md)，故障机器人单人卡牌覆盖见 [docs/故障机器人伤害覆盖.md](docs/故障机器人伤害覆盖.md)。

## 已知限制与待验证项

- 尚未在游戏内完成单人和多人战斗验证，面板布局和事件时序仍可能调整。
- `AfterDamageGiven`、`DamageResult`、事件签名已用 `0.107.1` 的 `sts2.dll` 编译验证，但还需要运行态验证。
- 已实现模型栈、最近模型和 `PowerModel.Applier` 回退，但中毒、灾厄、反伤和敌方施加状态仍需要按真实战斗日志核对。
- 静默猎手单人卡牌的静态路径已整理，但 88 张卡牌、27 张直接伤害卡、Shiv 派生伤害、`ThornsPower` 混合来源和四种能力间接伤害仍需按真实战斗逐项验证。
- 储君单人卡牌的静态路径已整理，但 88 张卡牌、32 张直接伤害卡、锻造/重放/生成派生和 `BlackHolePower` / `ReflectPower` 仍需按真实战斗逐项验证。
- 亡魂契约师单人卡牌的静态路径已整理，但 88 张卡牌、24 张直接伤害卡、12 张 Osty 攻击卡、`SweepingGaze` 派生、三条能力来源映射和 Doom 击杀归属/重复计数仍需按真实战斗逐项验证。
- 故障机器人单人卡牌的静态路径已整理，但 88 张卡牌、29 张直接伤害卡、充能球被动/激发、通道与能力触发、`HailstormPower` / `SmokestackPower` / `ThunderPower` 三条来源映射仍需按真实战斗逐项验证。
- 中毒当前只按独立机制统计，具体施毒来源保留为 `Deferred`，尚未标记施毒者。
- HUD 在每次成功记录伤害后的下一帧立即重建统计模块，并保留 0.25 秒轮询用于计时数值兜底；长时间或高频连锁伤害战斗需要观察性能和拖动手感。
- 尚未实现多场战斗历史对比和独立结算弹窗。
- `workshop/image.png` 仍是占位图，正式上传前必须替换。
- `min_game_version` 当前记录为 `0.107.1`，兼容性范围仍待测试。
- 仓库目前没有 `LICENSE`；没有明确许可证前不要公开分发。

## 发布前检查

- [x] 在目标游戏版本上编译通过
- [x] 生成可上传 Steam 创意工坊的 ZIP
- [ ] 在单人和多人战斗中各跑至少一局
- [ ] 检查日志中的伤害、耗时和 DPS 是否合理
- [ ] 验证“设”按钮、设置面板关闭按钮、范围下拉框、玩家切换箭头、拖拽、位置保存和 50%–200% 缩放
- [ ] 确认没有修改游戏存档或破坏原版逻辑
- [ ] 替换正式创意工坊封面
- [ ] 同步更新版本号、清单、更新日志和创意工坊说明
- [ ] 首次上传后提交 `mod_id.txt`
- [ ] 将 `workshop.json` 的 `visibility` 从 `private` 改为目标可见性
