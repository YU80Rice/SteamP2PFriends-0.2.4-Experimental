# Ticket 06 Item / Zombie 结构迁移交付报告

## 一、需求执行概述

完成 0.2.4.8 的 Item / Zombie 行为保持型结构迁移：将领域适配器、补丁、生命周期辅助类型和测试入口统一到各自目录与命名空间，更新注册/复位/验证引用，不改变生产行为。

本票没有接线 Resource Production Control Seam，没有新增并行 Authority Writer，也没有修改当前标签或已归档版本。

## 二、源码溯源矩阵

| 需求 | 落实位置 |
|---|---|
| Item 领域归属 | `Adapters/Item/*`、`Adapters/Item/Patches/*`、`Core/Ownership/ModuleOwnershipCatalog.cs` |
| Zombie 领域归属 | `Adapters/Zombie/*`、`Adapters/Zombie/Patches/*`、`Core/Ownership/ModuleOwnershipCatalog.cs` |
| 身份边界保持 | `Core/Identity/RegionKey.cs`、`Core/Identity/BoundKey.cs`；本票未复制身份类型 |
| 注册顺序和验证保持 | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs`、`SteamP2PFriendsPlugin.PatchRegistrationCriticalVerification.cs`；仅更新类型命名空间 |
| 生命周期/状态复制/generation 保持 | `Adapters/Item/ItemGenerationAuthorityAdapter.cs`、`ItemObserverReplicationAdapter.cs`、`Adapters/Zombie/ZombieRegionLifecycleAdapter.cs`、`ZombieSnapshotAdapter.cs`及其原有补丁实现 |
| 测试接缝 | `WhitelistTests/StaticIL/ItemZombieOwnershipStaticILContractTests.cs` |
| 验收与边界 | `.scratch/structure-baseline-0-2-4-8/issues/06-item-zombie-ownership.md`、`docs/architecture/item-zombie-ownership.md` |

## 三、代码变更清单

### 物理迁移与命名空间

- Item：`AuthoritativeItemGenerationGatePatch`、`ItemManagerP0B3PreGeneratePatch`、`ItemManagerP0B6RegenerateOnLevelLoadedPatch`、`ItemManagerRegionSyncPatch`、`ItemManagerWorldSyncDiagnosticPatch` → `SteamP2PFriends.Adapters.Item.Patches`。
- Zombie：`ZombieManagerWorldSyncDiagnosticPatch`、`ZombieManagerP0C1SendZombieStatesPatch`、`ZombieManagerP0DGenerateZombiesPatch`、`ZombieLifecyclePatch`、`ZombieLifecycleState`、`ZombieLifecycleOwnerVerify`、`ZombieEntityMappingDiagnosticPatch` → `SteamP2PFriends.Adapters.Zombie.Patches`。
- Item/Zombie 测试保持在 `WhitelistTests/Adapters/Item` 与 `WhitelistTests/Adapters/Zombie`，仅更新引用；没有复制行为测试。
- `ModuleOwnershipCatalog`、项目 Compile Include、Registration Trace 相关调用方和 Host/session reset 引用已同步更新。

### 行为边界

- U3-SDK 原生 Item step 5、Zombie Bound 生命周期位置、Harmony target、patch method、owner、priority、注册调用顺序和注册后验证未改变。
- Item generation gate、区域快照事务、Zombie Acquire/Hysteresis Release、Snapshot token、Region/Bound、Session/Connection/Region/Entity generation 语义未改写。
- `ZombieEntityMappingDiagnosticPatch` 仍为只读诊断，不拥有 Zombie 生产状态。
- Resource Production Control Seam 与旧 Resource Authority Writer 未触碰。

## 四、编译与测试验证

执行环境：Windows / MSBuild 18.9.1；配置：Release。

```text
MSBuild.exe SteamP2PFriends.csproj /t:Build /p:Configuration=Release /v:minimal
结果：0 errors / 0 warnings

MSBuild.exe WhitelistTests\SteamP2PFriends.WhitelistTests.csproj /t:Build /p:Configuration=Release /v:minimal
结果：0 errors / 0 warnings

WhitelistTests\bin\Release\SteamP2PFriends.WhitelistTests.exe
结果：186/186 PASS，Failed: 0

git diff --cached --check
结果：PASS
```

关键测试包括：Item 既有 generation/replication 测试、Zombie 既有 lifecycle/snapshot 测试、`ItemZombieOwnershipStaticILContractTests`、Identity/Resource-Collision/Module Ownership StaticIL 契约。

## 五、独立审核记录

审核基线：`0ea8bd6`；审核范围：本票暂存变更及 Ticket 06 规格。

| 轴 | 首轮结论 | 处理 |
|---|---|---|
| Standards | PASS，无阻断 | 保留非阻断建议：后续可整理 `ZombieEntityMappingDiagnosticPatch.cs:186` 缩进，并抽取测试中的重复反射断言 |
| Spec | 初审 FAIL：缺少自包含交付报告、构建日志和 hash/MVID；第二轮 PASS | 本报告补齐证据并绑定本次提交；第二轮复核确认五项验收无阻断 |

初审指出的“StaticIL 不能证明 Runtime”属于正确边界：本票不将静态证据、构建或测试结果替代 Singleplayer、listen-host、U3DS、P2P Runtime 验证。Runtime 仍为 `PENDING`。

## 六、BuildArtifact 独立指纹

版本来源：`Build/Version.props`，插件版本 `0.2.4.8`。

| 产物 | SHA-256 | MVID | Assembly Version |
|---|---|---|---|
| `bin/Release/SteamP2PFriends.dll` | `96C4B44F4B6418EB4EFE6F12108DA18B655766FFC2418C45F185CBFEA4263387` | `8616e2d5-dbf6-41c3-b9c6-237e935fd87e` | `0.2.4.8` |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `E151F2BCA17B3C066087FCB9167C994527172E6760FB63FB15A9CFA73B28960B` | `13d6405e-29e8-4c31-bfec-09f50dd7aef9` | `0.0.0.0` |

SHA-256 由验收侧对最终 Release 文件独立重算；MVID 由 Mono.Cecil 读取最终模块元数据。后续 Runtime Build Fingerprint 与日志-DLL关联仍属于 Ticket 09 / Runtime 门禁。

## 七、最终结论

代码结构、测试接缝、Release 构建和静态证据已完成；第二轮 Standards/Spec 独立审核均 PASS、无阻断。Runtime 保持 Pending，不宣称功能修复完成。
