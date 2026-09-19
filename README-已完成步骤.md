# Sts2DpsMeter 已完成步骤

> 本文档面向仓库所有者，用于快速查看当前开发进度、验证结果和后续事项。Mod 的安装、编译与发布说明请阅读 `README.md`。
>
> 更新时间：2026-09-19

## 一、当前结果

| 项目 | 当前状态 |
| --- | --- |
| GitHub 仓库 | 已创建并配置远程地址 |
| 初始 `main` | 已建立并推送，提交为 `0669372 chore(Sts2DpsMeter)：初始化仓库` |
| 功能分支 | `feature/DpsMeterCore` |
| 目标游戏版本 | `0.107.1` |
| 当前 Mod 版本 | `0.2.0` 铁甲战士测试版 |
| 真实游戏程序集编译 | 已通过，Release、0 警告、0 错误 |
| 游戏内运行验证 | 尚未完成 |
| Steam 创意工坊上传 | 尚未执行 |
| PR | 尚未创建 |

远程仓库：

```text
https://github.com/DGXD01/Sts2DpsMeter.git
```

本地目录：

```text
C:\Users\lenovo\Documents\Codex\2026-09-19\new-chat\outputs\Sts2DpsMeter
```

## 二、已完成的步骤

### 1. 参考资料与独立实现

- 已参考公开的《杀戮尖塔2》伤害统计 Mod 和 Steam 创意工坊 `rDPS Meter` 的信息层级，并将战斗内 HUD 收敛为“玩家造成伤害”和“玩家受到伤害”两个划分。
- 新项目采用独立实现，没有复制参考 Mod 的 DLL、反编译源码、图片或本地化资源。

### 2. GitHub 仓库与变更控制

- 已建立私有 GitHub 仓库并配置 `origin`。
- 已建立初始 `main` 并完成首次推送。
- 已从初始 `main` 建立符合规范的功能分支 `feature/DpsMeterCore`。
- 已建立 `.gitignore`，排除编译产物、游戏程序集、Steam 暂存目录和上传器日志。
- 已建立中文 PR 模板、代码所有者规则和开发流程文档。

### 3. DPS 统计核心

| 文件 | 已完成内容 |
| --- | --- |
| `ModEntry.cs` | Mod 初始化、对局开始、战斗开始和战斗结束事件接入 |
| `DpsHook.cs` | 通过 `AfterDamageGiven` 监听真实伤害事件 |
| `DpsAttribution.cs` | 伤害来源识别、输出/承伤/自损分类、置信度和语义标签 |
| `DpsSourceContext.cs` | Harmony 模型上下文栈、最近状态/遗物模型追踪 |
| `DpsStats.cs` | 线程安全统计、玩家和宠物归因、DPS、DPT、回合伤害和来源统计 |
| `DpsModels.cs` | UI 与日志使用的只读统计快照模型 |
| `DpsMeterSettings.cs` | 设置数据、推荐值、`settings.json` 读写和规范化 |
| `DpsMeterSettingsUi.cs` | `F7` 设置面板、两个统计模块开关、布局参数、50%–200% 缩放和恢复推荐值 |
| `DpsMeterUi.cs` | 玩家造成/受到伤害双模块 HUD、拖动位置、`Ctrl+滚轮` 快捷缩放和 `F8` 显示开关 |
| `Sts2DpsMeter.csproj` | .NET 9、x64、真实游戏程序集引用和版本配置 |
| `manifest.json` | Mod ID、版本、游戏版本、DLL 和依赖声明 |

当前统计口径：

```text
主输出 = 格挡伤害 + 未格挡伤害 + 过量伤害
有效掉血 = 未格挡伤害
```

当前行为：

- 统计玩家和宠物对敌人造成的输出，并将宠物伤害归因给主人。
- 底层分别记录输出、承伤和自损，避免把敌方施加的状态伤害误算为玩家输出；HUD 将承伤、自损和格挡统一归入“玩家受到伤害”。
- 通过模型上下文栈、最近模型、`cardSource` 和 `PowerModel.Applier` 回退识别来源。
- 覆盖卡牌、状态、遗物、药水、充能球、宠物、敌方和事件来源。
- 按稳定卡牌 ID 聚合卡牌伤害，同名卡牌合并为一个统计类型，并并入玩家造成伤害的来源划分。
- 显示中毒、火焰、闪电、束缚、反伤、充能球被动/激发等语义标签。
- 中毒归入独立机制并预留施毒来源字段；当前不追踪具体施毒者。
- HUD 只显示“玩家造成伤害”和“玩家受到伤害”两个模块；命中次数、格挡、生命伤害和过量伤害在各模块内汇总。
- 逐回合、排行、概要、核心指标、卡牌伤害和机制伤害不再作为独立 HUD 模块显示，底层回合数据仍保留。
- `F7` 打开设置，`F8` 显示或隐藏 HUD；未锁定时可拖动并保存位置。
- HUD 支持 `50%` 至 `200%` 缩放：可在 `F7` 面板拖动“界面缩放”滑块，也可将鼠标移到 HUD 上后按住 `Ctrl` 滚动滚轮快速缩放；锁定位置时仍可快捷缩放。
- 设置面板提供“重置 HUD 大小（100%）”，缩放值会保存到 `settings.json`。
- 伤害 Hook 每次成功记录伤害后立即请求刷新，Godot 主线程在下一帧更新 HUD；同一帧内的连锁伤害合并处理。
- 战斗结束时在 Godot 日志中输出本场汇总。
- 游戏暂停时停止累计战斗时间。

### 4. 构建、校验与创意工坊打包

| 文件 | 已完成内容 |
| --- | --- |
| `scripts/validate-repo.ps1` | 校验清单、版本、新增源码文件、封面、PowerShell 脚本和禁止提交的文件 |
| `scripts/build-and-stage.ps1` | 自动查找游戏目录、Release 编译、暂存上传内容并生成 ZIP |
| `.github/workflows/validate.yml` | 在 `main` 推送和 PR 上运行静态仓库校验 |
| `.github/pull_request_template.md` | 中文 PR 验证、风险和审查模板 |
| `.github/CODEOWNERS` | 指定仓库所有者审查 |
| `docs/开发与更新流程.md` | 分支、merge、版本、GitHub 和 Steam 上传流程 |
| `docs/技术债.md` | 记录运行验证和后续技术债 |
| `CHANGELOG.md` | 记录 `0.2.0` 的已完成项和待验证项 |
| `workshop/workshop.json` | Steam 创意工坊标题、描述、私有可见性和标签 |
| `workshop/image.png` | 小于 1 MB 的占位封面，正式上传前需要替换 |

当前 ZIP：

```text
artifacts\workshop\Sts2DpsMeter-Ironclad-Test-0.2.0.zip
```

ZIP 根目录：

```text
content/Sts2DpsMeter.dll
content/manifest.json
workshop.json
image.png
README.md
```

### 5. 本机验证结果

游戏目录：

```text
E:\STEAM\steam\steamapps\common\Slay the Spire 2
```

已执行的检查：

| 检查 | 结果 |
| --- | --- |
| `dotnet format --verify-no-changes --no-restore` | 通过 |
| `scripts/validate-repo.ps1` | 通过，Mod 版本 `0.2.0` |
| `dotnet build -c Release -warnaserror` | 成功，0 警告、0 错误 |
| `scripts/build-and-stage.ps1 -Configuration Release` | 成功，生成创意工坊 ZIP |

验证产物：

```text
artifacts\build\Sts2DpsMeter.dll
artifacts\workshop\Sts2DpsMeter-Ironclad-Test-0.2.0.zip
```

`artifacts/` 已加入 `.gitignore`，不会提交到 GitHub。

## 三、当前 Git 流程

- `main`：仅包含初始提交，尚未合并功能代码。
- `feature/DpsMeterCore`：包含本阶段 DPS Mod 实现和文档。
- 本次只操作功能分支，不创建 PR，也不修改 `main`。
- 后续需要合并时，必须另行授权创建和合并 PR。

## 四、尚未完成的事项

| 优先级 | 事项 | 退出标准 |
| --- | --- | --- |
| P0 | 游戏内运行验证 | 单人和多人战斗各至少验证一场，确认面板、伤害、耗时和日志正确 |
| P0 | UI 交互验证 | 验证 `F7`、`F8`、两个统计模块开关、拖动、位置保存、50%–200% 缩放和 Ctrl+滚轮快捷缩放 |
| P1 | 间接伤害运行态归因 | 覆盖中毒、末日、反伤、敌方施加状态等间接伤害 |
| P1 | 中毒来源追踪 | 在预留字段中记录具体施毒者并区分玩家/敌人来源 |
| P1 | 游戏版本兼容性 | 确认 `min_game_version` 和实际可运行版本范围 |
| P1 | 性能检查 | 确认伤害触发的即时 HUD 重建和 0.25 秒计时兜底不会造成明显卡顿或拖动抖动 |
| P1 | 许可证 | 公开发布前确定 LICENSE |
| P2 | 创意工坊封面 | 用正式 PNG 替换占位图，且小于 1 MB |
| P2 | Steam 首次上传 | 使用官方上传器完成私有测试上传 |
| P2 | 更新链路 | 首次上传后提交 `mod_id.txt`，并验证一次后续更新 |
| P2 | CI 编译 | 解决游戏程序集版权限制下的本地编译或授权 Runner 方案 |

## 五、后续建议步骤

1. 将 ZIP 解压到官方上传器工作区，或把 `workshop\content\` 和 `workshop.json` 放入上传器目录进行运行测试。
2. 记录游戏版本、角色、模式和日志中的 `[Sts2DpsMeter]` 输出。
3. 验证 HUD、两个统计模块、设置、拖拽和来源划分，并记录截图和异常。
4. 根据运行结果修复 UI 布局、事件时序或伤害归因问题。
5. 修复完成并重新验证后，再创建中文 PR，审查并合并到 `main`。
6. 合并后替换正式封面、确定许可证，并执行 Steam 私有上传测试。
7. 首次上传后保存并提交 `mod_id.txt`，再执行后续更新验证。

> 当前状态是“已实现并通过真实程序集编译和创意工坊打包，但尚未经过游戏内运行验证”，不能把 `0.2.0` 标记为稳定发布版。
