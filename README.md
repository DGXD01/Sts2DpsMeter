# Sts2DpsMeter

《杀戮尖塔2》的 DPS 统计 Mod。仓库用于跟踪源码、构建流程、版本变更和 Steam 创意工坊更新。

本项目参考“皮皮统计: Skada”的公开行为与伤害统计口径，重新实现独立代码；不包含或复制其 DLL、反编译源码、图片和本地化资源。

## 当前状态

| 项目 | 状态 |
| --- | --- |
| GitHub 仓库 | 已创建，私有 |
| 当前开发分支 | `feature/DpsMeterCore` |
| 目标游戏版本 | `0.107.1` |
| 真实游戏程序集编译 | 已通过：Release、0 警告、0 错误 |
| 游戏内运行 | 未验证 |
| Steam 上传 | 未执行 |
| 许可证 | 尚未确定，公开发布前必须补充 |

> 程序集编译通过不等于运行验证通过。当前版本可以作为开发版继续检查，但在完成游戏内验证前不要标记为“已发布”或“可稳定安装”。

## 当前功能

- 统计玩家和宠物对敌人造成的伤害；宠物伤害归因给宠物主人。
- 伤害口径为 `格挡伤害 + 未格挡伤害 + 过量伤害`，与 Skada 的当前伤害统计逻辑一致。
- 显示总伤害、命中次数、最高单次伤害、DPS 和每回合伤害。
- 按卡牌标题或宠物名汇总伤害来源。
- 战斗期间显示 Godot 面板，按 `F8` 显示或隐藏。
- 战斗结束时向 Godot 日志输出汇总数据。
- 暂停游戏时停止累计 DPS 计时。

当前不会修改战斗数值、卡牌、敌人或存档。

## 目录结构

```text
.
├── .github/
│   ├── CODEOWNERS
│   ├── pull_request_template.md
│   └── workflows/validate.yml
├── docs/
│   ├── 技术债.md
│   └── 开发与更新流程.md
├── scripts/
│   ├── build-and-stage.ps1
│   └── validate-repo.ps1
├── workshop/
│   ├── content/                 # 上传器实际打包的 DLL/JSON
│   ├── image.png                # 上传封面占位图，发布前替换
│   └── workshop.json
├── DpsHook.cs                   # 战斗 Hook 接入
├── DpsMeterUi.cs                # Godot DPS 面板和 F8 开关
├── DpsModels.cs                 # 快照模型
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

## 本地验证

先执行静态校验：

```powershell
pwsh -NoProfile -File .\scripts\validate-repo.ps1
```

校验通过后执行 Release 构建并暂存到 Steam 工作区：

```powershell
pwsh -NoProfile -File .\scripts\build-and-stage.ps1
```

只构建、不暂存：

```powershell
pwsh -NoProfile -File .\scripts\build-and-stage.ps1 -SkipStage
```

脚本会：

1. 自动查找 Steam 库或使用 `STS2_GAME_DIR`；
2. 检查游戏程序集是否存在；
3. 运行 `dotnet build`；
4. 将 `Sts2DpsMeter.dll` 和 `manifest.json` 复制到 `workshop/content/`；
5. 检查封面是否为小于 1 MB 的 PNG。

## 版本与更新规则

- Mod 版本使用三段式语义化版本，例如 `0.1.0`。
- `manifest.json` 的 `version` 与项目文件 `<Version>` 必须一致。
- 每次发布前更新 `CHANGELOG.md`。
- 首次 Steam 上传后，上传器生成的 `mod_id.txt` 必须保留并提交，后续更新依靠它定位已有创意工坊条目。
- Steam 上传只允许在当前登录 Steam 的 Windows 机器上手动执行；不要把 Steam 凭据或上传器日志提交到仓库。
- 不要在仓库中提交游戏自带的 DLL、PCK、密钥、缓存或构建产物。

完整流程见 [docs/开发与更新流程.md](docs/开发与更新流程.md)，已知问题见 [docs/技术债.md](docs/技术债.md)。

## 已知限制与待验证项

- 尚未在游戏内完成单人和多人战斗验证，面板布局和事件时序仍可能调整。
- `AfterDamageGiven`、`DamageResult`、事件签名已用 `0.107.1` 的 `sts2.dll` 编译验证，但还需要运行态验证。
- 毒伤、末日等没有直接 `dealer`/`cardSource` 的伤害归因不完整。
- 当前没有独立的战斗结算弹窗、历史战斗对比和配置界面。
- `workshop/image.png` 仍是占位图，正式上传前必须替换。
- `min_game_version` 当前记录为 `0.107.1`，兼容性范围仍待测试。
- 仓库目前没有 `LICENSE`；没有明确许可证前不要公开分发。

## 发布前检查

- [x] 在目标游戏版本上编译通过
- [ ] 在单人和多人战斗中各跑至少一局
- [ ] 检查日志中的伤害、耗时和 DPS 是否合理
- [ ] 确认没有修改游戏存档或破坏原版逻辑
- [ ] 同步更新版本号、清单、更新日志和创意工坊说明
- [ ] 首次上传后提交 `mod_id.txt`
- [ ] 将 `workshop.json` 的 `visibility` 从 `private` 改为目标可见性
