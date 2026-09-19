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
| 当前 Mod 版本 | `0.1.0` |
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

### 1. 参考资料分析

- 已参考《杀戮尖塔2》多人伤害统计 Mod，确认 Mod 初始化、战斗 Hook 和伤害事件的实际接入方式。
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
| `DpsMeterUi.cs` | Godot 游戏内面板，支持 `F8` 显示或隐藏 |
| `Sts2DpsMeter.csproj` | .NET 9、x64、真实游戏程序集引用和版本配置 |
| `manifest.json` | Mod ID、版本、游戏版本、DLL 和依赖声明 |

当前统计口径：

```text
主输出 = 格挡伤害 + 未格挡伤害 + 过量伤害
有效掉血 = 未格挡伤害
```

当前行为：

- 统计玩家和宠物对敌人造成的输出，并将宠物伤害归因给主人。
- 分别记录输出、承伤和自损，避免把敌方施加的状态伤害误算为玩家输出。
- 通过模型上下文栈、最近模型、`cardSource` 和 `PowerModel.Applier` 回退识别来源。
- 覆盖卡牌、状态、遗物、药水、充能球、宠物、敌方和事件来源。
- 显示中毒、火焰、闪电、束缚、反伤、充能球被动/激发等语义标签。
- 显示总伤害、有效掉血、格挡、未格挡、过量、命中次数、最高单次、DPS 和 DPT。
- 显示每个回合的伤害、承伤和自损来源。
- 战斗中按 `F8` 显示或隐藏统计面板。
- 战斗结束时在 Godot 日志中输出本场汇总。
- 游戏暂停时停止累计战斗时间。

### 4. 构建、校验与更新流程

| 文件 | 已完成内容 |
| --- | --- |
| `scripts/validate-repo.ps1` | 校验清单、版本、封面、PowerShell 脚本和禁止提交的文件 |
| `scripts/build-and-stage.ps1` | 自动查找游戏目录、Release 编译并暂存 Steam 上传内容 |
| `.github/workflows/validate.yml` | 在 `main` 推送和 PR 上运行静态仓库校验 |
| `.github/pull_request_template.md` | 中文 PR 验证、风险和审查模板 |
| `.github/CODEOWNERS` | 指定仓库所有者审查 |
| `docs/开发与更新流程.md` | 分支、merge、版本、GitHub 和 Steam 上传流程 |
| `docs/技术债.md` | 记录运行验证和后续技术债 |
| `CHANGELOG.md` | 记录 `0.1.0` 的已完成项和待验证项 |
| `workshop/workshop.json` | Steam 创意工坊标题、描述、私有可见性和标签 |
| `workshop/image.png` | 小于 1 MB 的占位封面，正式上传前需要替换 |

### 5. 本机验证结果

游戏目录：

```text
E:\STEAM\steam\steamapps\common\Slay the Spire 2
```

已执行的检查：

| 检查 | 结果 |
| --- | --- |
| `dotnet format --verify-no-changes --no-restore` | 通过 |
| `scripts/validate-repo.ps1` | 通过，Mod 版本 `0.1.0` |
| `dotnet build -c Release -warnaserror` | 成功，0 警告、0 错误 |
| `scripts/build-and-stage.ps1 -Configuration Release -SkipStage` | 成功，0 警告、0 错误 |

验证产物：

```text
artifacts\build\Sts2DpsMeter.dll
```

该目录已加入 `.gitignore`，不会提交到 GitHub。

## 三、当前 Git 流程

- `main`：仅包含初始提交，尚未合并功能代码。
- `feature/DpsMeterCore`：包含本阶段 DPS Mod 实现和文档。
- 本次只提交并推送功能分支，不创建 PR，也不修改 `main`。
- 后续需要合并时，必须另行授权创建和合并 PR。

## 四、尚未完成的事项

| 优先级 | 事项 | 退出标准 |
| --- | --- | --- |
| P0 | 游戏内运行验证 | 单人和多人战斗各至少验证一场，确认面板、伤害、耗时和日志正确 |
| P1 | 间接伤害运行态归因 | 覆盖毒伤、末日、反伤、敌方施加状态等间接伤害 |
| P1 | 游戏版本兼容性 | 确认 `min_game_version` 和实际可运行版本范围 |
| P1 | 拼接式自定义 UI | 将固定列表拆分为可拖拽、可缩放和可配置的拼接模块 |
| P1 | 许可证 | 公开发布前确定 LICENSE |
| P2 | 创意工坊封面 | 用正式 PNG 替换占位图，且小于 1 MB |
| P2 | Steam 首次上传 | 使用官方上传器完成私有测试上传 |
| P2 | 更新链路 | 首次上传后提交 `mod_id.txt`，并验证一次后续更新 |
| P2 | CI 编译 | 解决游戏程序集版权限制下的本地编译或授权 Runner 方案 |

## 五、后续建议步骤

1. 将 `workshop\content\Sts2DpsMeter.dll` 和 `manifest.json` 放入游戏 Mod 加载目录进行运行测试。
2. 记录游戏版本、角色、模式和日志中的 `[Sts2DpsMeter]` 输出。
3. 根据运行结果修复 UI 布局、事件时序或伤害归因问题。
4. 修复完成并重新验证后，再创建中文 PR，审查并合并到 `main`。
5. 合并后替换正式封面、确定许可证，并执行 Steam 私有上传测试。
6. 首次上传后保存并提交 `mod_id.txt`，再执行后续更新验证。

> 当前状态是“已实现并通过真实程序集编译，但尚未经过游戏内运行验证”，不能把 `0.1.0` 标记为稳定发布版。
