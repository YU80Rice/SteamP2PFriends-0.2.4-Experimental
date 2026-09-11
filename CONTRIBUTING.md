# 贡献指南

本仓库是 0.2.4 实验线，不是 0.2.3 稳定插件的续写。默认语言为简体中文。默认分支为 `codex/structure-baseline-0.2.4`。当前**不发 GitHub Release**，改动以源码和审计文档为准。

## 先读

1. [README.md](./README.md) — 产品边界、已知未修项、构建命令。
2. [ARCHITECTURE-REVIEW-0.2.4-Experimental.md](./ARCHITECTURE-REVIEW-0.2.4-Experimental.md) — 为何要重做控制面。
3. [audit/2026-09-04/ReviewRecheck-0.2.4.8-0933.md](./audit/2026-09-04/ReviewRecheck-0.2.4.8-0933.md) — 评审条目的当前进度。
4. [CONTEXT.md](./CONTEXT.md) — 领域词汇；写代码和文档时用这里的词，不要另造近义词。

## 报告问题

请用 GitHub Issue 模板。联机问题必须同时附房主和客机的 `BepInEx/LogOutput.log`（推荐 UMM 诊断包），并写明：

- 双方 `SteamP2PFriends.dll` 的 SHA-256（`certutil -hashfile SteamP2PFriends.dll SHA256`）
- 连接方式（SteamID / IPv4）
- Unturned 与 BepInEx 版本
- 其他已装插件

不要只贴截图或只贴一端日志。日志里的真实 SteamID 可以打码，但不要删时间戳、异常栈和插件加载行。

维护者内部工单在 [`.scratch/`](./.scratch/) 的本地 Markdown 跟踪器，不替代 GitHub Issue。

## 开发约定

- **频道**：功能性联机改动必须走 `LaunchMultiplayerNet` 命名频道，形成双端闭环。
- **单一权威写入**：同一领域同一时刻只允许一条生产写入路径。Resource 域的模板是 `ResourceProductionControlSeam`；不要在迁移中途保留旧 Writer。
- **证据分类**：PureMemory / StaticIL / BuildArtifact / Runtime 四类不可互相替代。没有真实多机日志，不得在 PR 或文档里写 Runtime PASS。
- **实验区隔离**：不要把未验收改动写进已归档的 0.2.3 仓库。
- **提交**：Conventional Commit。禁止创建 git tag。
- **敏感信息**：新的真实 SteamID、token、邮箱、公网 IP 不得写入仓库文件。测试夹具使用假号。
- **分批改动**：实现与测试每次一个小 Edit；不要整文件重写生产代码。文档与审计报告不受这条限制，但仍应单一职责。

## 构建与静态门禁

```powershell
dotnet msbuild SteamP2PFriends.csproj -t:Rebuild -p:Configuration=Release
dotnet msbuild WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -t:Rebuild -p:Configuration=Release
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe
```

Git Bash 下使用 `-t:Rebuild`，不要写 `/t:Rebuild`。贡献前至少保证 Release 0 error / 0 warning，以及测试入口全绿。指纹与布局门禁脚本见 `Tools/` 与 `docs/architecture/`。

## 架构方向（避免做反）

香草 listen-host 早退（僵尸重生、掉落物 despawn/respawn、连接路由）与把 SPI 铺到新域是两条线，不要并进同一张修复票。Resource 已是模板域；下一个迁移域是 Collision，然后才是 Item / Zombie。Animal 域先要设计决策（评审认为它在为不存在的机制建模），不要直接接 SPI。

详细审查闭环见 [docs/agents/output-review-loop.md](./docs/agents/output-review-loop.md)。实机测试是 1 Host + 2 Guest，由维护者部署同哈希 DLL 后回收 UMM 诊断包。

## 许可证

贡献即同意以 [MIT License](./LICENSE) 授权。
