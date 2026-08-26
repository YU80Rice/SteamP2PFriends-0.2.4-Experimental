# Ticket 05 Resource / Collision 结构迁移交付报告

## 一、需求执行概述

完成 0.2.4.8 Experimental 中 Resource 与 Collision 的领域归属、物理目录/namespace 对齐、注册引用迁移和静态结构接缝建设。未接线 Resource Production Control Seam，未改变旧生产 Authority Writer。

## 二、源码溯源清单

| 需求点 | 落实位置 |
|---|---|
| Resource 补丁归属明确 | `Adapters/Resource/Patches/ResourceManagerWorldSyncDiagnosticPatch.cs`、`ResourceManagerRegionSyncPatch.cs`、`ResourceManagerHarvestReplicationPatch.cs`、`LevelGroundRemoteTreeCollisionPatch.cs` 均使用 `SteamP2PFriends.Adapters.Resource.Patches` |
| Collision 补丁归属明确 | `Adapters/Collision/Patches/LevelObjectRemoteCollisionPatch.cs` 使用 `SteamP2PFriends.Adapters.Collision.Patches` |
| 注册归属保持单一 | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationTransportModules.cs` 与 `PatchRegistrationLegacyDiagnostics.cs` 继续由既有 Orchestrator 阶段调用同一实现 |
| 会话/断线引用一致 | `Core/Lifecycle/SteamP2PFriendsPlugin.PatchRegistry.cs`、`SessionDisconnectDispatcher.cs`、`Platform/Host/HostManager.cs` 指向领域 namespace 下同一入口 |
| 注册后验证保持 | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationCriticalVerification.cs` 指向迁移后的 Resource/Collision 类型，未改验证逻辑 |
| 结构目录可审计 | `Core/Ownership/ModuleOwnershipCatalog.cs` 显式登记 `Adapters.Resource` 与 `Adapters.Collision` |
| 测试接缝先行 | `WhitelistTests/StaticIL/ResourceCollisionOwnershipStaticILContractTests.cs` 先以失败约束旧 namespace，迁移后验证五个补丁 FullName 唯一及五个旧类型不存在 |

## 三、保持不变与未决项

- 未修改 Harmony target、patch method、owner、priority、登记调用顺序或注册后验证逻辑；U3-SDK Resource step 3、Object/Collision step 4 保持原注册链语义。
- 未修改 P2P 通道、SteamID、配置键、插件 GUID、日志语义、当前标签或已归档版本。
- 旧 Resource Authority Writer 仍为唯一生产权威；Production Control Seam 尚未接线。
- `LevelGroundRemoteTreeCollisionPatch` 虽依赖 Collision 的区域覆盖判定，仍归属 Resource，因为其原生 target 是 `ResourceSpawnpoint.SetIsActiveInRegion(bool)`；没有复制状态或第二 writer。
- Runtime（Singleplayer、listen-host、U3DS、P2P）、Harmony 最终运行排序、碰撞/采伐/状态复制的双端实际行为仍 Pending，留给 Ticket 10/11。

## 四、代码变更清单

- 修改 5 个 Resource/Collision 补丁文件的 namespace/依赖引用；
- 修改注册、生命周期复位、断线清理、Host 清理和验证入口引用；
- 扩展 `ModuleOwnershipCatalog`；
- 新增 `ResourceCollisionOwnershipStaticILContractTests.cs`，并纳入测试项目/主入口；
- 更新 `docs/architecture/module-ownership.md`、`domain-identity.md`、`registration-trace.md`、`migration-manifest.md`；
- 新增 `docs/architecture/resource-collision-ownership.md`。

## 五、编译与测试验证

命令：

```text
dotnet msbuild SteamP2PFriends.csproj /t:Rebuild /p:Configuration=Release /v:minimal
dotnet msbuild WhitelistTests/SteamP2PFriends.WhitelistTests.csproj /t:Rebuild /p:Configuration=Release /v:minimal
WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe
git diff --check
pwsh -ExecutionPolicy Bypass -File audit/2026-08-26/Verify-Ticket04Metadata.ps1
```

结果：

- 主项目 Release：0 errors / 0 warnings；
- 测试项目 Release：0 errors / 0 warnings；
- 回归测试：`185/185 PASS`；
- `git diff --check`：通过；
- StaticIL：Resource/Collision 五个补丁各有且仅有一个期望 FullName，五个旧 `Core.Patches` 类型均不存在；
- Resource 既有 PureMemory 测试全部通过，未复制原有行为测试。

## 六、BuildArtifact 独立证据

| 产物 | 路径 | Assembly/File Version | SHA-256 | MVID |
|---|---|---|---|---|
| 插件 DLL | `bin/Release/SteamP2PFriends.dll` | `0.2.4.8 / 0.2.4.8` | `FE6A8D3EF30711A20285E86F5642F17FEC1CE29E8AAAFA693583C9025245FD15` | `b8093107-d624-4aa8-8f44-bdfd265e8a08` |
| 测试 EXE | `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `0.0.0.0 / 0.0.0.0` | `EBAC52B78F2D3E2D9827D15C2638478E708560505BE52D958D3986348A539160` | `26483703-8494-4de1-bd52-e8e791cb7727` |

SHA-256 由验收侧 `Get-FileHash` 重新计算；MVID 由 `Verify-Ticket04Metadata.ps1` 的 PE metadata 读取器提取。该证据不替代 Runtime。

## 七、独立审核记录

| 审核轴 | 第一轮 | 修复 | 第二轮 |
|---|---|---|---|
| Standards | FAIL：BuildArtifact 尚写“待最终重建” | 回填最终 DLL/EXE SHA-256/MVID | PASS，无阻断 |
| Spec | FAIL：BuildArtifact 未回填；旧 namespace 排除只覆盖 3 个类型 | 回填最终产物证据；StaticIL 扩展到 5 个新/旧类型 | PASS，无阻断 |

审核代理确认 Runtime Pending 不属于本票阻断；本票没有独立运行时验收结论。

## 八、最终结论

Ticket 05 的结构整理、测试接缝、Release 构建、回归测试、独立产物证据和双轴独立审核均通过，可提交当前分支。下一依赖阶段仍须保持 Resource 生产接缝未接线，按 Ticket 08/09 先完成证据门禁，再进入 Ticket 10/11。
