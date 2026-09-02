# 🏁 结构基线第一批实施报告 - 0.2.4.8

## 一、需求执行概述

在 `codex/structure-baseline-0.2.4` 分支完成第一批行为保持不变的结构基线：固定 `0.2.4.8` Metadata Source、固定 U3-SDK 对照 commit、让主项目和测试项目共同导入 `Build/Version.props`，并建立结构基线、Registration Trace 与 Migration Manifest。未移动源码、未统一 namespace、未接入 Resource Production Control Seam、未修复功能问题。

## 二、源码溯源清单

| 决策点 | 落实位置 |
|---|---|
| 版本值为 `0.2.4.8`、实验通道保持不变 | `Build/Version.props` |
| U3-SDK commit 固定 | `Build/Version.props`、`docs/architecture/registration-trace.md` |
| 两个项目共同导入 Metadata Source | `SteamP2PFriends.csproj:4`、`WhitelistTests/SteamP2PFriends.WhitelistTests.csproj:4` |
| 目标结构和行为不变量 | `docs/architecture/structure-baseline.md` |
| 原生注册/生命周期追踪 | `docs/architecture/registration-trace.md` |
| 批次、范围和未决项 | `docs/architecture/migration-manifest.md` |
| 术语和不可逆架构决策 | `CONTEXT.md`、`docs/adr/0006-behavior-preserving-structure-baseline-and-resource-migration.md` |

## 三、代码变更清单

### 新增

- `Build/Version.props`
- `docs/architecture/structure-baseline.md`
- `docs/architecture/registration-trace.md`
- `docs/architecture/migration-manifest.md`
- `audit/2026-08-26/Implementation-0.2.4.8-1033.md`

### 修改

- `CONTEXT.md`
- `SteamP2PFriends.csproj`
- `WhitelistTests/SteamP2PFriends.WhitelistTests.csproj`
- `docs/adr/0006-behavior-preserving-structure-baseline-and-resource-migration.md`
- `docs/architecture/migration-manifest.md`

### 明确未修改

- 生产 C# 源码目录和 namespace；
- Harmony target、owner、priority 和执行顺序；
- Resource、Zombie、Item、Route B、连接路由功能；
- 已归档版本；
- 用户提供的未跟踪 `ARCHITECTURE-REVIEW-0.2.4-Experimental.md`。

## 四、编译与测试验证

最终使用：

```text
C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe SteamP2PFriends.csproj /t:Rebuild /p:Configuration=Release /v:minimal
C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe WhitelistTests\SteamP2PFriends.WhitelistTests.csproj /t:Rebuild /p:Configuration=Release /v:minimal
WhitelistTests\bin\Release\SteamP2PFriends.WhitelistTests.exe
```

结果：

- 主项目：构建成功，0 errors；
- 测试项目：构建成功，0 errors；
- 测试入口：`181/181 PASS`；
- `git diff --check`：通过；
- Runtime：未执行，不能据此宣称功能修复完成。

构建产物指纹：

| 产物 | SHA-256 |
|---|---|
| `bin/Release/SteamP2PFriends.dll` | `150CEFCBCF225285782E60BAA2D7A95E2608C0AB4BD11164CC99750780876767` |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `6BB935FC75C5DAC1979713554E8BDA8075C824602FE98FA1E05C71143ADEBFB8` |

## 五、独立审核记录

| 审核轮次 | 判定 | 说明 |
|---|---|---|
| 第 1 次 | FAIL（范围） | 发现仓库根目录临时文件 `-`，不属于批次；已确认其为构建期间生成的 `.csproj` 文本并移至系统临时目录保留 |
| 第 2 次 | PASS | 确认额外未跟踪项仅为用户提供的第三方报告；版本、SDK commit、Import 和文档一致；无源码/namespace/功能意外变化 |

非阻断建议已处理：迁移清单中的两个项目导入状态已更新为“已验证”。

## 六、偏离与妥协说明

- 首次调用 `msbuild` 因命令别名不存在而失败；改用本机实际 MSBuild 绝对路径后构建成功。不是代码或项目错误。
- 本批次仅建立 Metadata Source，不改写现有 `AssemblyInfo.cs`、插件特性、README 和启动日志中的历史版本常量；这些属于后续版本元数据迁移批次，避免把结构基线与发布语义混在一起。
- 无其他偏离。

## 七、下一批次建议

进入 `Patch Registration Orchestrator` 拆分前，先完成 U3-SDK Registration Trace 的剩余逐补丁依赖映射；然后按既定批次进行注册器拆分、目录/namespace 迁移和 Evidence Class 门禁实现。Resource 生产接缝迁移仍不得提前开始。

## 八、最终结论

第一批结构基线已完成，主项目和测试项目构建通过，现有测试 `181/181 PASS`，独立审核最终 PASS。Runtime 未执行，当前交付仅代表结构/构建基线完成，不代表 0.2.4.8 功能验收或发布通过。
