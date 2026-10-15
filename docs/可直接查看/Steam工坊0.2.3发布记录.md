# Steam 创意工坊 0.2.3 更新记录

日期：2026-10-15（首次尝试）；2026-10-15 13:16 补发成功。

**状态：已发布。`Sts2 DPS Meter - 轻量化战斗统计` 0.2.3 已成功更新到 Steam 创意工坊条目 `3804100979`，沿用原条目，未创建新条目。**

用户本轮授权：更新已有 Steam 创意工坊 Mod 并提交更新日志；随后另行授权执行 `git add`、`commit`、`push`。本次没有创建或合并 PR，没有打标签，没有改动 `main`。

## 1. 发布目标与版本

- 原工坊条目：`3804100979`，沿用原 `mod_id.txt`，上传同时显式指定该 ID，未创建新条目。
- 标题：`Sts2 DPS Meter - 轻量化战斗统计`。
- 可见性：保留 `unlisted`。
- 已发布版本：`0.2.3`，程序集版本：`0.2.3.0`。
- 内容范围：UI 重构第 1 步的布局、齿轮设置入口、节点复用与刷新合并，不代表 14 项增强功能完整完成。
- 原发布分支 `chore/WorkshopRelease0.2.2` 与 `origin/main` 的 C# 文件无差异；此次以 main 为代码基线不会回退上一工坊版本的归因修复。独立的 debuff 功能分支未合并。
- 保留工坊原有中英文“作者有话说”、条目标题、封面、预览图、标签和依赖设置；正文增加测试版说明，并将旧设置入口文字同步为齿轮。

## 2. 本轮修改文件与原因

| 文件 | 修改原因 |
| --- | --- |
| `Sts2DpsMeter.csproj` | 程序集版本同步到 `0.2.3` |
| `manifest.json` | 安装清单版本同步到 `0.2.3` |
| `CHANGELOG.md` | 新增 `0.2.3` 中文更新记录，明确测试范围与未完成验收 |
| `README.md` | 同步当前版本、分支和测试状态 |
| `workshop/workshop.json` | 设置中文 changeNote，保留原作者段落，更新齿轮入口及测试版说明 |
| `workshop/README.md` | 保留上一版修复说明，加入 `0.2.3` 更新日志 |
| `docs/可直接查看/HUD第1步_实现与验证记录.md` | 追加加载源核对、停止游戏操作与发布状态 |
| `docs/可直接查看/Steam工坊0.2.3发布记录.md` | 保存本次发布核验、失败证据与成功证据，以及可复用的重试入口 |

前一轮 UI 代码和测试脚本仍是同一工作区的未提交改动，完整逐文件原因见 [第 1 步记录](HUD第1步_实现与验证记录.md)。

## 3. 发布前检查

- 真实 `v0.107.1` 游戏程序集 Release 非增量构建成功，0 错误、2 条既有 `CS8602` 警告；归因文件未改动，基线复现情况见 TD-022。
- `dotnet format --verify-no-changes --no-restore`、`git diff --check` 均通过。
- 清单与程序集版本一致，已核对最终上传 DLL 的 SHA-256：
  `FA037892FE8B39E5F7180E059766AD6BE5E9FC16F3D2021A48C3721BFFC39984`。
- 本次新打包的 `content` 只含 `Sts2DpsMeter.dll` 和 `manifest.json`，未包含游戏程序集、用户存档、凭据或日志。
- 重试使用此前成功上传的原工作区，沿用其中已有的 `content/README.md`。该文件是上一版本的公开说明文件，原工作区已完整备份。
- 补发前再次逐文件核对 SHA-256，上传工作区与 0.2.3 更新包完全一致（`content/manifest.json` = `669C1850…`，`content/Sts2DpsMeter.dll` = `FA037892…C39984`，`workshop.json` = `F387EA41…`，`image.png` = `BA420527…`，`previews/*` 一致，`mod_id.txt` 仍为 `3804100979`）。
- 中文更新日志明确写出新版单人/多人实机验收未完成；刷新并发检查通过，不能替代游戏内验收。

### 仓库校验脚本的环境边界

`scripts/validate-repo.ps1` 面向 PowerShell 7（`pwsh`）编写，依赖 `Get-Content` 与 `Parser::ParseFile` 的 UTF-8 默认解码。本机只安装 Windows PowerShell 5.1，这两处会按 GBK 解码 UTF-8 无 BOM 文件。

- 清单、版本、封面、必要路径与禁止文件各项检查通过（显式指定 UTF-8 读取后运行）。
- 脚本语法检查项在 PS 5.1 下误报；改用 UTF-8 读入后通过 `Parser::ParseInput` 独立复核，`build-and-stage.ps1`、`test-hud-refresh.ps1`、`validate-repo.ps1` 三个脚本均无语法错误。
- 该差异属于运行环境，不是仓库缺陷；在有 `pwsh` 的机器上按文档原命令执行即可。

## 4. 上传结果

使用已安装的官方 `ModUploader.exe upload --workspace ... --id 3804100979`，已登录 Steam 账号 `dgxd520`（PersonaName「多关心的」，SteamID64 `76561198402078980`）。

| 尝试 | 时间 | 工作区 | Steam 结果 |
| --- | --- | --- | --- |
| 第一次 | 2026-10-15 11:47 | 本次独立更新包目录 | `k_EResultFail`；Steam 日志：`Timeout uploading manifest (size 587)` |
| 第二次 | 2026-10-15 11:48 | 此前成功上传的原工作区 | `k_EResultFail`；Steam 日志：`Timeout uploading manifest (size 695)` |
| 第三次 | 2026-10-15 11:51 | 原工作区 | `k_EResultFail`；Steam 日志：`Timeout uploading manifest (size 695)` |
| **第四次（成功）** | 2026-10-15 13:16 | 原工作区 `E:\Slay the Spire 2\ModUploader-win-x64\Sts2DpsMeterWorkshop` | **`Upload finished for workshop item 3804100979 : OK`** |

前三次均在上传内容阶段超时，判断为临时连接故障；第四次在 Steam 客户端重新启动并完成登录后一次通过，未改动任何系统网络或 VPN 配置。

成功后关键证据：

- 上传器退出码 `0`，输出 `Successfully uploaded 'Sts2 DPS Meter - 轻量化战斗统计' to the workshop with id 3804100979!`，并完成 `166987` 字节内容上传（`Sts2DpsMeter.dll` 166400 + `manifest.json` 587）。
- Steam 客户端 `logs\workshop_log.txt`：

  ```text
  [2026-10-15 13:16:25] [AppID 2868840] Upload starting for workshop item 3804100979 by AppID 2868840
  [2026-10-15 13:16:31] [AppID 2868840] Uploaded new content ( ManifestID 126582588667946711 ) for item 3804100979.
  [2026-10-15 13:16:31] [AppID 2868840] Uploaded new workshop preview file ...\Sts2DpsMeterWorkshop\image.png
  [2026-10-15 13:16:33] [AppID 2868840] Uploaded new workshop preview file ...\previews\01-combat-overview.jpg
  [2026-10-15 13:16:34] [AppID 2868840] Uploaded new workshop preview file ...\previews\02-hud-closeup.jpg
  [2026-10-15 13:16:34] [AppID 2868840] Upload finished for workshop item 3804100979 : OK
  ```

- 新的工坊内容 ManifestID：`126582588667946711`。
- 工坊公开页面 <https://steamcommunity.com/sharedfiles/filedetails/?id=3804100979> 抓取结果：标题与中英文正文正常显示，正文包含 `0.2.3`，已不再包含 `0.2.2`。

日志中的 `Status: k_EItemUpdateStatusInvalid` 是上传器在提交完成后读取到的终止状态枚举值，上一版 0.2.2 成功上传时同样出现，不影响结果，不能与 `k_EResultFail` 混淆。

## 5. 文件位置与后续入口

- 本次上传使用的工作区：`E:\Slay the Spire 2\ModUploader-win-x64\Sts2DpsMeterWorkshop`。
- 可检查的更新包目录：`E:\Slay the Spire 2\可直接查看\Steam工坊更新_0.2.3`。
- ZIP：`E:\Slay the Spire 2\可直接查看\Sts2DpsMeter_0.2.3_工坊更新包.zip`。
- 中文日志：更新包目录下 `更新日志_0.2.3.md`。
- 上传前核验、前一版本工作区备份、三次失败日志和第四次成功日志：`E:\Slay the Spire 2\辅助生成文件\Steam工坊更新_0.2.3`。
- 发布结果总记录：`E:\Slay the Spire 2\可直接查看\Steam工坊更新_0.2.3_发布结果.md`。
- 上传器日志：`E:\Slay the Spire 2\ModUploader-win-x64\mod-uploader.log`（已替换为本次成功运行的日志，上一版日志保留为同目录 `mod-uploader.log.before-upload-20261015-131622.bak`）。

后续更新步骤：同步版本号、`manifest.json`、`CHANGELOG.md` 与 `workshop/workshop.json` 的 `changeNote`，重新构建生成工作区，保留 `mod_id.txt`，然后执行：

```powershell
& 'E:\Slay the Spire 2\ModUploader-win-x64\ModUploader.exe' upload --workspace 'E:\Slay the Spire 2\ModUploader-win-x64\Sts2DpsMeterWorkshop' --id 3804100979
```

上传前须确认 Steam 客户端已启动并登录 `dgxd520`：`steamwebhelper` 正在运行，且 `HKCU:\Software\Valve\Steam\ActiveProcess\ActiveUser` 为 `441813252`（SteamID64 `76561198402078980`）。本次前三次失败均发生在上传器运行时 Steam 客户端未处于正常工作状态的情况下。

上传器在工作目录下也会写入一份同名 `mod-uploader.log`，因此运行上传时需确认当前目录，避免日志散落到仓库或会话目录。本次原有 `$null` 文件保留，未纳入任何发布内容，也未纳入 Git 提交。
