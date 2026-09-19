## 变更目的

<!-- 说明本 PR 要解决的问题，以及为什么现在需要修改。 -->

## 变更范围

<!-- 列出主要文件、行为和不在本次范围内的内容。 -->

## 验证方式

- [ ] `pwsh -NoProfile -File .\scripts\validate-repo.ps1`
- [ ] `dotnet build` 编译通过
- [ ] 单人战斗运行验证
- [ ] 多人战斗运行验证
- [ ] 已检查 Godot 日志中的伤害、DPS 和异常

验证环境：

- 游戏版本：
- Mod 版本：
- 模式：
- 复现步骤：
- 实际结果：

## 版本与发布影响

- [ ] 已同步 `Sts2DpsMeter.csproj` 的 `Version`
- [ ] 已同步 `manifest.json` 的 `version`
- [ ] 已更新 `CHANGELOG.md`
- [ ] 已确认 `workshop.json` 的 `changeNote`
- [ ] 没有提交游戏 DLL、PCK、日志、Steam 凭据或 `mod-uploader.log`

## 风险与回滚

<!-- 说明已知风险、未覆盖场景和回滚方式。 -->

## 审查信息

- 主要改动：
- 未完成事项：
- 需要人工确认：