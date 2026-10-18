# Steam工坊0.2.19发布与提交记录

日期：2026-10-18（Asia/Shanghai）。分支：refactor/HUD设置整合与最终清理。仓库 https://github.com/DGXD01/Sts2DpsMeter.git 。

## 授权与结果

用户明确要求“commit，push，上传并告诉我应该如何检验”。据此原子提交第十步重构c35a961、测试e193adf、审核文档98add92，升级0.2.19，更新原工坊条目并提交推送发布材料。没有创建PR、外部评论或Issue、Git标签，没有启动/重启游戏、修改真实配置或安装游戏DLL。原未跟踪$null保留。

官方ModUploader首次上传退出0，Steam日志回执：

- 2026-10-18 09:02:57：Upload starting，AppID2868840，条目3804100979。
- 2026-10-18 09:03:03：Uploaded new content，ManifestID1982441929655714076。
- 2026-10-18 09:03:05：Upload finished : OK。

工坊条目：https://steamcommunity.com/sharedfiles/filedetails/?id=3804100979 。标题、unlisted可见性、原中英作者正文、标签/依赖、封面、两预览和mod_id保持；仅版本摘要和中文更新日志改变。上传不表示用户端已下载、游戏加载或验收通过。

## 逐文件发布补充

第十步13文件原因详见 [第十步审核](HUD第10步_实现与审核记录.md)；本次版本元数据5文件和发布记录1文件补充如下。README、计划、技术债、审核及最终清单另更新当前发布结果/验收入口，保留历史时点。

| 文件 | 修改原因 |
| --- | --- |
| Sts2DpsMeter.csproj | 程序集版本升至0.2.19。 |
| manifest.json | Mod版本升至0.2.19，Schema保持10。 |
| CHANGELOG.md | 新增第十步分组/滚动/边界与验证记录，不改旧发布历史。 |
| workshop/workshop.json | 新版摘要及中文更新日志，保护非版本元数据/作者正文。 |
| workshop/README.md | 增加0.2.19说明，保留0.2.18及更早历史。 |
| docs/可直接查看/Steam工坊0.2.19发布与提交记录.md | 授权、回执、包哈希、逐文件原因、验证和检验入口。 |

## 发布验证

真实v0.107.1程序集Release非增量构建通过，0错误，仅DpsAttribution.cs:784/902两条既有CS8602（TD-022）。前十步托管回归通过，包括43设置属性真实IL绑定、198合成Schema0–10升级及辅助文件往返、64滚动计算场景与既有全部回归。

独立官方Godot4.5.1设置48场景（四分辨率/双语/六字号）、字号576布局/12大字总结和原总结45布局通过；dotnet format verify、validate-repo及Diff检查通过。原生检查从产品提取文字/参数后构造代表场景，不执行游戏C#控件事件、全部最长菜单项或真实重启，不能代替实机验收。没有用户历史配置全样本升级结论。

9文件严格白名单包和上传工作区逐文件SHA一致；content合计298561字节，DLL271872字节，程序集版本0.2.19。无游戏程序集、真实设置、日志、凭据、脚本或$null；未生成ZIP。旧上传目录先完整备份，元数据和媒体保护检查通过。原工作区基线fetch成功，无落后，origin/main在历史中，未rebase或强推。

## 发布文件

| 文件 | 字节 | SHA256 |
| --- | --- | --- |
| README.md | 26079 | `E6CBBAAA0650A27128DFC7BD7CB693BF9C1327AF39EDE7F13EF1D4EDA00F9260` |
| content/README.md | 26079 | `E6CBBAAA0650A27128DFC7BD7CB693BF9C1327AF39EDE7F13EF1D4EDA00F9260` |
| content/Sts2DpsMeter.dll | 271872 | `F8B00D8443919FCBDF832325EDB8E0D142E6B881CEF26E0BC2E882CE3FF1E2EB` |
| content/manifest.json | 610 | `BF487AD027E54703102F6E331974D819840529E6F2A5DC69DA8C7D3D3B2539B8` |
| image.png | 289366 | `BA420527307690D331F234C962F09BB8CD63BAA29AE77E6D19E8EF3F42239FFD` |
| mod_id.txt | 12 | `C63154C308D41FF26D02A4F36958250BFFCAF039BE9A1A3B03E618B7A595678D` |
| previews/01-combat-overview.jpg | 240310 | `0A3370A60819263FF0F4D6948C549B119752D830301BF7995E84154EFED8F1F3` |
| previews/02-hud-closeup.jpg | 84659 | `8129E3962B957584167F41444371BAC7587D5FE80BE3DFE4194B1DC4B1AE83BC` |
| workshop.json | 5766 | `D6C260EF04A3E886EBBF1C4B527626FBA71BD88F1618232B4AC30D053E36C7DC` |

## 用户检验顺序

先等Steam更新，用户正常重启确认加载0.2.19，点击HUD齿轮看到HUD/位置/统计内容/数据行/多人/战斗结束/预设七组。若仍旧分组，先查下载和加载路径。按 [最终验收清单](HUD十阶段_最终验收清单.md) 的12项顺序执行：重点检查主题/字号切换或勾选摘要/字段后的滚动位置、底部关闭按钮可达、四预设后的手改保留及正常退出重启恢复，再复验磁吸/字段/提示/排序/低干扰/总结。多人按原安排统一测试。

真实控件/滚动恢复、退出重启、四分辨率/双语/两种缩放与主题预设完整组合继续待测，TD-032及既有待测不自动关闭；完整阶段/功能验收仍0/10、0/14，开发/离线交付完成不等于完整矩阵通过。

## 文件与Git交接

- 可上传包：E:/Slay the Spire 2/可直接查看/Steam工坊更新_0.2.19。
- 构建/哈希/日志：E:/Slay the Spire 2/辅助生成文件/Steam工坊更新_0.2.19。
- 上传前备份：E:/Slay the Spire 2/辅助生成文件/Steam工坊更新_0.2.19/上传前0.2.18备份_20261018-090158。
- 成功回执仅保留本条目三行；完整上传器日志存辅助目录，不进Git。
- 第十步审核、最终清单、计划、技术债、README和本发布记录的可查看副本同步到E:/Slay the Spire 2/可直接查看。

发布元数据、发布记录按本轮授权分别提交推送。最终全部提交ID、远程ls-remote及0领先/0落后和工作区状态另存辅助目录Git提交与推送回执.txt，不为填写自身提交哈希循环改写本文。
