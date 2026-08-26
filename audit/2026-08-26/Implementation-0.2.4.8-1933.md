# Ticket 07 Animal / Structure / Barricade 结构迁移交付报告

## 一、需求执行概述

完成 `0.2.4.8` Ticket 07 的行为保持型领域结构整理：Animal、Structure、Barricade 的已确认归属与物理目录/namespace 对齐；Vehicle、Object 及其他无法证明单一领域边界的内容保留在 `Core/Patches` 并明确标记 Pending。未接线 Resource Production Control Seam，未改变当前生产 Authority Writer、版本标签或已归档版本。

## 二、源码溯源矩阵

| 需求 | 落实位置 |
|---|---|
| Animal 归属确认与迁移 | `Adapters/Animal/Patches/AnimalManagerP0C2SendAnimalStatesPatch.cs`、`AnimalManagerWorldSyncDiagnosticPatch.cs`；`Core/Registration/*` 仅更新类型引用 |
| Structure/Barricade 归属确认与迁移 | `Adapters/Structure/Patches/BarricadeManagerRegionSyncPatch.cs`、`StructureManagerRegionSyncPatch.cs`、`P0EBarricadeLifecycle/*` |
| Vehicle Pending | `Core/Patches/VehicleManagerP0C1ReplicationPatch.cs`、`VehicleManagerWorldSyncDiagnosticPatch.cs`、`VehicleEnterDiagnosticPatch.cs` 原位保留 |
| Object Pending | `Core/Patches/ObjectManagerRegionSyncPatch.cs`、`ObjectManagerWorldSyncDiagnosticPatch.cs`、`Issue7ObjectBinaryStateDiagnosticPatch.cs` 原位保留；`ModuleOwnershipCatalog` 独立记录 |
| 注册、复位、验证入口保持 | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistration*.cs`、`Core/Lifecycle/SteamP2PFriendsPlugin.PatchRegistry.cs`、`Core/Lifecycle/SessionDisconnectDispatcher.cs`、`Platform/Host/HostManager.cs` |
| 唯一权威与 Pending 原因 | `Core/Ownership/ModuleOwnershipCatalog.cs`、`docs/architecture/animal-structure-ownership.md`、`docs/architecture/module-ownership.md` |
| 静态注册/结构接缝 | `WhitelistTests/StaticIL/AnimalStructureOwnershipStaticILContractTests.cs`、既有 `ModuleOwnershipStaticILContractTests`、`docs/architecture/registration-trace.md` |
| 迁移清单 | `docs/architecture/migration-manifest.md`、`.scratch/structure-baseline-0-2-4-8/issues/07-animal-structure-domain-ownership.md` |

## 三、逐域状态

| 领域 | 状态 | 处理 |
|---|---|---|
| Animal | Confirmed | namespace 与 `Adapters/Animal/Patches` 对齐，旧 `Core.Patches` 类型缺失 |
| Structure | Confirmed | 区域同步与生命周期补丁迁入 `Adapters/Structure/Patches` |
| Barricade | Confirmed | 与 Structure 共用 `BuildingDomainAdapter`，独立补丁入口保留 |
| Vehicle | Pending | 保留在 `Core/Patches`，不创建并行适配器 |
| Object | Pending | 保留在 `Core/Patches`，区域同步/诊断/二进制观察边界待后续证明 |
| Resource/Collision/Item/Zombie | Confirmed（前票） | 本票不改动前票结构或 Resource 生产权威 |
| 其他跨领域诊断/审计 | Pending | 受控保留在 `Core/Patches` 并登记原因 |

## 四、代码变更清单

- Animal 两个补丁的 namespace 与物理目录统一。
- Structure/Barricade 区域同步补丁和 `P0EBarricadeLifecycle` 六个文件完成物理迁移与 namespace 更新。
- 更新项目 Compile Include、注册模块、关键验证、Host/session reset 引用；未保留旧兼容副本。
- 新增 Ticket 07 StaticIL 接缝，覆盖新 FullName 唯一、Animal/Structure/Barricade 旧入口缺失、Vehicle/Object Pending 与 Registration Trace 覆盖。
- 新增/更新领域所有权、Registration Trace 和 Migration Manifest 文档。

## 五、保持不变与偏离

### 保持不变

- U3-SDK 原生 Structure step 1、Barricade step 2、Animal 调用链及既有生命周期语义。
- Harmony target、patch method、owner、priority、注册顺序和注册后验证。
- P2P 通道、SteamID、配置键、插件 GUID、日志语义、网络协议和当前生产 Authority Writer。
- 当前标签与已归档版本；Resource Production Control Seam 未接线。

### 偏离与妥协说明

- Vehicle 与 Object 没有被强行迁入虚构的 `Adapters/<Domain>` 目录；这是对“无法证明单一领域归属则保留 Pending”约束的遵守。
- Barricade 逻辑模块名与物理目录 `Adapters/Structure` 不完全同名，因为现有 `BuildingDomainAdapter` 同时拥有 Structure/Barricade 生命周期与复制；该关系在 Catalog 和架构文档中显式记录。
- 本票不执行 Runtime 迁移验收；Singleplayer、listen-host、U3DS、P2P 和最终 Harmony 执行排序仍为 Pending，不以静态测试或构建替代。

## 六、编译与测试验证

执行环境：Windows，Release。

```text
dotnet build SteamP2PFriends.csproj -c Release --nologo
结果：0 errors / 0 warnings

dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release --nologo
结果：0 errors / 0 warnings

WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe
结果：187/187 PASS，Failed: 0

git diff --check
结果：PASS
```

## 七、BuildArtifact 独立指纹

版本来源：`Build/Version.props`，插件版本 `0.2.4.8`。

| 产物 | SHA-256 | MVID | Assembly Version |
|---|---|---|---|
| `bin/Release/SteamP2PFriends.dll` | `CF212C7449B7975E1439A940D01EF8BF16DB9639E788FC49AC63D841A9CEC903` | `90dfe1b5-b3be-4012-8179-f9c83d871c6e` | `0.2.4.8` |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `A6334240AD871179E88CA091CE97883B4BCC41CCFF18B71B913B11E6B2A97988` | `7c4dc63d-7080-4c5c-a5a8-1bb4a17c8a61` | `0.0.0.0` |

SHA-256 由最终 Release 文件独立重算；MVID 由 Mono.Cecil 读取最终模块元数据。Runtime Build Fingerprint 与日志-DLL关联仍属于 Ticket 09/运行时门禁。

## 八、独立审核记录

| 轮次 | Standards | Spec | 处理 |
|---|---|---|---|
| 第一轮 | FAIL：缺少最终审计报告；StaticIL 对旧入口与注册证据覆盖不足 | FAIL：Object 未逐项记录；旧入口断言不完整；暂存集缺少交付证据 | 新增 Object Pending、补齐 StaticIL、生成并纳入本报告 |
| 第二轮 | FAIL：issue tracker 冻结图格式例外未声明；报告状态与 Ticket 不一致 | FAIL：报告状态未记录本轮结论；Migration Manifest 遗漏 Object/其他 Pending；StaticIL 尚未显式核验注册元数据 | 声明冻结图例外；补齐 Pending 与报告路径；扩展注册元数据静态接缝；第三轮复核待执行 |
| 第三轮 | PASS：无阻断 | PASS：结构、Pending 状态、静态接缝和证据一致；状态闭环后通过 | 已确认暂存白名单闭合、`git diff --cached` 检查通过；Ticket 已完成 |

## 九、测试建议

1. 运行现有 PureMemory/StaticIL 入口，确认新旧 FullName、Registration Trace 覆盖和 Pending 目录边界保持一致。
2. 在真实游戏环境分别验证 Singleplayer、listen-host、U3DS、P2P 的 Animal、Structure、Barricade 生命周期与状态复制；每次绑定本报告指纹和共享 Case ID。
3. 对 Vehicle/Object 先收集源码调用链与运行时观察证据，再单独提出领域迁移票据；在边界证明前不要创建并行 Authority Writer。
4. 验证断线复位与重新连接不会因 namespace 迁移导致重复登记或遗漏清理。

## 十、当前结论

代码结构、Release 构建、回归测试、静态结构/注册接缝和第三轮 Standards/Spec 独立审核均通过；Runtime 保持 Pending，不以本票静态证据替代 Singleplayer、listen-host、U3DS、P2P 验收。当前分支可提交。
