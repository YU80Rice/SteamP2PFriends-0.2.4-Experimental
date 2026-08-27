# 0.2.4-Experimental Migration Manifest

## Batch 1：Structure Baseline and Metadata Source

状态：第一批完成，Runtime 未执行

分支：`codex/structure-baseline-0.2.4`

版本：`0.2.4.8` / `Experimental`

U3-SDK：`ea7b4973af5ba10f62baad2bfde36ab2e5b060eb`

### 变更清单

| 项目 | 状态 | 说明 |
|---|---|---|
| `Build/Version.props` | 新增 | 建立版本与 SDK commit 的构建元数据来源 |
| 主 `.csproj` 导入版本文件 | 已验证 | Release 构建成功，不改变现有编译项和输出路径 |
| 测试 `.csproj` 导入版本文件 | 已验证 | Release 构建成功，保持单项目、单测试入口 |
| `docs/architecture/structure-baseline.md` | 新增 | 目标结构与行为不变量 |
| `docs/architecture/registration-trace.md` | 新增 | U3-SDK 原生生命周期锚点 |
| 本文件 | 新增 | 批次追踪与证据状态 |
| 源码目录/namespace | 未开始 | 本批次明确不移动 |
| Resource 生产控制接缝 | 未开始 | 后续行为迁移阶段 |
| 旧归档版本 | 冻结 | 不回写、不修改 |
| 第三方复核报告 | 保留未跟踪 | 不纳入本批次提交 |

### 行为保持检查

- Harmony target/owner/priority/order：待构建后静态快照核对；
- 插件 GUID、配置键、协议和当前标签：未计划改变；
- 当前旧 Authority Writer：保持生产权威；
- Runtime：本批次未执行，不得宣称功能修复完成。

### 后续批次

1. 拆分 Patch Registration Orchestrator；
2. 按 Domain Ownership 迁移目录和 namespace；
3. 建立 Evidence Class 测试与 Build Fingerprint 门禁；
4. Resource 通过 Production Control Seam 完成行为迁移；
5. 旧 Authority Writer 退出并完成 Host/Client Runtime 验证。

## Batch 2：Patch Registration Orchestrator

状态：代码、构建与纯内存测试完成；Runtime 未执行；等待人工运行验收

### 变更清单

| 项目 | 状态 | 说明 |
|---|---|---|
| `Core/Lifecycle/SteamP2PFriendsPlugin.PatchRegistry.cs` | 已拆薄 | 仅保留生命周期相关逻辑与顶层注册委托，不再承载完整注册/验证实现 |
| `Core/Registration/PatchRegistrationOrchestrator.cs` | 新增 | 按既有 Registration Trace 编排阶段、关闭适配器注册和触发最终验证 |
| `Core/Registration/RegistrationClosure.cs` | 新增 | 纯内存 Domain Id、Lifecycle/Replication 角色、重复/未知/缺失/关闭后变更校验 |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationTransportModules.cs` | 新增 | Transport 手工注册模块；保留原调用块顺序 |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationSecurityModules.cs` | 新增 | Route B 安全注册模块 |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs` | 新增 | 资产审计与诊断阶段编排模块 |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs` | 新增 | 原有诊断补丁登记调用块，保持既有登记顺序 |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDomainModules.cs` | 新增 | 领域补丁、生命周期/复制适配器登记与 Closure 接入 |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationAuditModules.cs` | 新增 | 资产完整性与审计补丁登记实现 |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationHarmonyVerification.cs` | 新增 | Harmony target、owner、数量与签名验证辅助实现 |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationCriticalVerification.cs` | 新增 | 关键注册结果聚合与 fail-closed 诊断门 |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationVerification.cs` | 新增 | Route B、统一连接、单端口及附加注册验证入口 |
| `WhitelistTests/Core/RegistrationClosureTests.cs` | 新增 | 通过公共 Closure seam 覆盖角色、重复、未知、缺失、顺序冲突和不可变性 |
| `docs/architecture/registration-closure.md` | 新增 | Closure 接口、顺序和证据规则 |

### 行为保持与边界

- U3-SDK 注册顺序仍由 `registration-trace.md` 的实际调用链定义；编排阶段只包裹既有调用，不按抽象领域顺序重排 Harmony 登记。
- Harmony target、owner、priority、既有手工登记方法和注册后验证逻辑保持原实现；本批次未改 Patch target 或 patch method。
- `ResourceDomainAdapter` 仅进入适配器登记目录；Resource Production Control Seam、旧 Authority Writer 和生产写入权未迁移。
- `SteamP2PFriendsPlugin` 的存档复用、P2P 协议、SteamID、配置键、GUID、当前标签和归档版本均未修改。

### Evidence Gate

| Evidence Class | 结果 | 证据 |
|---|---|---|
| PureMemory | PASS | RegistrationClosure seam 场景并入现有测试入口 |
| StaticIL | PASS（由 Ticket 04 补齐） | Ticket 02 当时的待办已由 `ticket-04-static-metadata-snapshot.md` 与 Registration Trace 65 单元矩阵完成 |
| BuildArtifact | PASS | 主项目、WhitelistTests Release 构建 0 errors / 0 warnings |
| Runtime | PENDING | 未执行 SP、U3DS 或 P2P 运行验证 |

## Batch 5：Resource、Collision 领域所有权与命名空间

状态：源码结构、Release 构建、PureMemory 与结构 StaticIL 已完成；Runtime Pending

### 变更清单

| 项目 | 状态 | 说明 |
|---|---|---|
| `Adapters/Resource/Patches/ResourceManagerWorldSyncDiagnosticPatch.cs` | 已整理 | namespace 与物理目录统一为 `SteamP2PFriends.Adapters.Resource.Patches` |
| `Adapters/Resource/Patches/ResourceManagerRegionSyncPatch.cs` | 已整理 | 保留 Resource 区域同步与发送 writer 逻辑及全部注册元数据 |
| `Adapters/Collision/Patches/LevelObjectRemoteCollisionPatch.cs` | 已整理 | namespace 与物理目录统一为 `SteamP2PFriends.Adapters.Collision.Patches` |
| 注册、复位、断线清理、关键验证引用 | 已整理 | 统一指向上述领域入口，未创建兼容副本 |
| `ModuleOwnershipCatalog` | 已扩展 | 显式记录 `Adapters.Resource` 与 `Adapters.Collision` 的唯一权威入口 |
| `ResourceCollisionOwnershipStaticILContractTests` | 新增 | 编译产物级唯一命名空间/旧入口缺失接缝 |
| `docs/architecture/resource-collision-ownership.md` | 新增 | 领域边界、入口映射、跨领域依赖与未决项 |

### 保持不变与未决项

- 原生 U3-SDK Resource step 3、Object/Collision step 4、Harmony target、patch method、owner、priority、登记调用顺序与注册后验证保持不变；
- P2P 通道、SteamID、配置键、插件 GUID、日志语义、当前标签和已归档版本未修改；
- 旧 Resource 生产 Authority Writer 仍是唯一生产权威；Production Control Seam 暂不接线；
- Resource 既有 PureMemory 测试未复制，只增加结构 StaticIL；
- Harmony 最终运行排序、碰撞/采伐/状态复制实际双端行为以及 SP/U3DS/P2P Runtime 仍 Pending，需在 Ticket 10/11 验证。

### Evidence Gate

| Evidence Class | 结果 | 证据 |
|---|---|---|
| PureMemory | PASS | 全量入口中的 182 项 PureMemory 测试通过；其中 Resource 22 项、Collision 8 项；总入口另含 3 项 StaticIL 测试，因此总结果为 `185/185 PASS` |
| StaticIL | PASS | `ResourceCollisionOwnershipStaticILContractTests`；Resource/Collision 五个补丁均有且仅有一个期望 FullName，五个旧 `Core.Patches` 权威类型均不存在 |
| BuildArtifact | PASS | 最终 Release 重建产物：插件 DLL SHA-256 `FE6A8D3EF30711A20285E86F5642F17FEC1CE29E8AAAFA693583C9025245FD15`、MVID `b8093107-d624-4aa8-8f44-bdfd265e8a08`；测试 EXE SHA-256 `EBAC52B78F2D3E2D9827D15C2638478E708560505BE52D958D3986348A539160`、MVID `26483703-8494-4de1-bd52-e8e791cb7727` |
| Runtime | PENDING | 本票不执行运行时迁移验收 |

勘误：此前 `Implementation-0.2.4.8-1742.md` 的测试 EXE 指纹和“185 项均为 PureMemory”的表述属于初版记录；本 Batch 5 当前权威值以上述独立复核结果为准。该初版报告保留，不覆盖。

证据边界：本 Batch 5 的 BuildArtifact 证据是验收侧独立重算的 DLL/EXE SHA-256、MVID 和版本；运行时 Build Fingerprint、共享 Case-ID 及日志-DLL关联属于 Ticket 09 与 Runtime 门禁，本票保持 Runtime `PENDING`，不以静态产物证据替代运行时证据。

## Batch 3：Domain Ownership、Namespace 与 Identity

状态：源码、构建、PureMemory 与身份 StaticIL 验证完成；Runtime 未执行

### 变更清单

| 项目 | 状态 | 说明 |
|---|---|---|
| `Core/Identity/DomainId.cs` | 新增 | 不可变 Domain Id 与 `DomainIds` 单一来源；显示名称分离 |
| `Core/Identity/RegionKey.cs` | 新增 | 二维坐标、Packed 编码/解码和边界校验集中管理 |
| `Core/Identity/BoundKey.cs` | 新增 | 一维导航 Bound 与无效 sentinel 的独立身份类型 |
| `Core/Identity/LifecycleAxes.cs` | 新增 | Session、Connection、Region、Entity generation 的正交值对象 |
| `Core/Identity/BarricadeKey.cs` | 新增 | Region 与 plant 的复合身份，避免结构状态键碰撞 |
| `MultiObserver/SPI/*` | 已整理 | SPI 与 `LeaseTicket` 使用 DomainId/RegionKey；Lifecycle/Replication 角色仍独立 |
| `MultiObserver/SPI/IBoundStateReplicationAdapter.cs` | 新增 | Bound 状态复制与二维 Region 状态复制分离 |
| `MultiObserver/Spatial/SpatialObserverIndex.cs` | 已整理 | Region 与 Bound 差异集合分离，连接 token 失效语义保持 |
| `MultiObserver/MultiObserverShadowLedger.cs` | 已整理 | RegionKey 与 BoundKey 分别承载二维需求和一维 Zombie demand |
| `Adapters/Animal/*` | namespace 已整理 | 生命周期/快照由旧 MultiObserver 命名空间归入 Animal 领域 |
| `Adapters/Zombie/*` | namespace 已整理 | 生命周期/快照由旧 MultiObserver 命名空间归入 Zombie 领域 |
| `Adapters/Zombie/ZombieSnapshotAdapter.cs` | 已整理 | Zombie snapshot ledger 全部使用 BoundKey |
| `WhitelistTests/Core/IdentityContractTests.cs` | 新增 | PureMemory 身份编码、边界、sentinel 与显示名称分离测试 |
| `WhitelistTests/StaticIL/IdentityStaticILContractTests.cs` | 新增 | 编译后接口/字段形状与隐式转换禁用契约 |
| `docs/architecture/domain-identity.md` | 新增 | 类型、边界转换、领域所有权和未决迁移说明 |

### 保持不变

- Harmony target、owner、priority、注册顺序和注册后验证未重排；Ticket 02 的 Registration Closure 仍是顶层登记接缝。
- P2P 频道、SteamID、存档、配置键、插件 GUID、状态机语义、当前标签和归档版本未修改。
- Resource Production Control Seam、旧 Authority Writer 和功能行为未迁移。

### Evidence Gate

| Evidence Class | 结果 | 证据 |
|---|---|---|
| PureMemory | PASS | `IdentityContractTests` 与现有 Spatial/Registration 测试入口 |
| StaticIL | PASS（Ticket 03 身份契约；后续由 Ticket 04 扩展） | `IdentityStaticILContractTests`；注册单元/Harmony target 静态矩阵已在 Ticket 04 快照中补齐 |
| BuildArtifact | PASS | 主项目与 WhitelistTests Release 构建 0 errors / 0 warnings |
| Runtime | PENDING | 未执行 SP、U3DS 或 P2P 运行验证 |

### 未决项

- U3-SDK 原生 `int`/`byte` 仍在 patch 边界出现，但进入 Core、MultiObserver 和领域 ledger 前均显式转换为值对象。
- Ticket 03 当时的完整注册单元/Harmony target 静态快照待办已由 Ticket 04 的静态元数据快照补齐；最终 Harmony 运行排序与 Runtime 仍 Pending。

## Batch 4：Core、Platform、Security 所有权

状态：源码结构、Release 构建和结构 StaticIL 已完成；Runtime Pending

### 变更清单

| 项目 | 状态 | 说明 |
|---|---|---|
| `Core/Patches/*` | 已迁移 | 根级跨领域补丁的唯一受控物理入口；未复制实现 |
| `Core/ControlPlane/*` | 已迁移 | Multi-Observer 控制面和 SPI 的唯一物理入口 |
| `Core/Shared/*` | 已迁移 | 核心共享协议/状态/枚举的物理归属 |
| `Platform/Client/*`、`Platform/Host/*` | 已迁移 | 外部运行时适配按 Platform 子模块归属 |
| `Security/*` | 已迁移 | P2PApprovalManager、P2PWhitelistService 与准入补丁唯一入口 |
| `Core/Ownership/ModuleOwnershipCatalog.cs` | 新增 | 模块 ID、物理根、权威类型和 Trace 覆盖的单一结构目录 |
| `WhitelistTests/StaticIL/ModuleOwnershipStaticILContractTests.cs` | 新增 | 唯一权威入口与 Security/Registration Closure 的编译产物契约 |
| `docs/architecture/module-ownership.md` | 新增 | 归属表、跨领域保留原因和行为不变量 |
| `docs/architecture/ticket-04-static-metadata-snapshot.md` | 新增 | Release 产物与结构 StaticIL 的可复核快照 |

### 保持不变

- Harmony target、owner、priority、执行顺序、P2P 通道、SteamID、配置键、插件 GUID、Route B 状态机和日志语义未改动；
- `PatchRegistrationOrchestrator` 仍是唯一顶层注册编排入口，`RegistrationClosure` 仍是唯一适配器关闭入口；
- Resource Production Control Seam、旧 Authority Writer 和 Runtime 行为未迁移。

### Evidence Gate

| Evidence Class | 结果 | 证据 |
|---|---|---|
| PureMemory | PASS | 既有 183 项测试 + Ticket 04 结构契约入口，共 184 项，未复制领域测试 |
| StaticIL | PASS（结构 + 注册元数据） | `ModuleOwnershipStaticILContractTests` 通过；Registration Trace 71 行/65 单元矩阵已绑定 target、patch type/method、owner、priority、order |
| BuildArtifact | PASS（独立快照） | Release DLL/测试 EXE 的版本、FileVersion、MVID 与 SHA-256 已写入 Ticket 04 交付报告；本次重建产物以报告值为准 |
| Runtime | PENDING | 未执行 SP、U3DS 或 P2P 运行验证 |

## Batch 6：Item、Zombie 领域所有权与命名空间

状态：源码结构、Release 构建、现有 PureMemory 与结构 StaticIL 已完成；Runtime Pending

### 变更清单

| 项目 | 状态 | 说明 |
|---|---|---|
| `Adapters/Item/Patches/*` | 已整理 | Item 区域同步、世界同步诊断、生成 Authority Gate、预生成与 level-loaded 重生补丁统一到 Item 领域 |
| `Adapters/Zombie/Patches/*` | 已整理 | Zombie 世界同步诊断、Bound 生命周期、生成、状态复制和实体映射诊断补丁统一到 Zombie 领域 |
| `Adapters/Item/*` / `Adapters/Zombie/*` | 已确认 | Item/Zombie domain adapter、Region/Bound ledger 和快照适配器保持单一物理根 |
| `Core/Identity/*` | 保持唯一 | `RegionKey`、`BoundKey`、生命周期轴不复制；Zombie 继续使用 `BoundKey`，Item 的原生 byte 转换保留在边界 |
| `WhitelistTests/StaticIL/ItemZombieOwnershipStaticILContractTests.cs` | 新增 | 验证领域 FullName 唯一、旧 Core 权威类型缺失和 ModuleOwnershipCatalog 记录 |
| 注册/复位/验证调用方 | 已整理 | 仅更新命名空间引用；U3-SDK 注册顺序、Harmony target/owner/priority 和日志语义不变 |

### 保持不变与未决项

- Item generation gate、区域快照事务、Zombie Acquire/Hysteresis Release、Snapshot token、Region/Bound 和 generation 行为保持原实现；
- 未新增并行 Authority Writer，未将结构迁移描述成功能修复；Resource Production Control Seam 未接线；
- 现有 Item/Zombie PureMemory 测试未复制，只增加结构 StaticIL 接缝；
- `ZombieEntityMappingDiagnosticPatch` 随 Zombie 诊断入口归属 Zombie，但仍只读观察，不拥有 Zombie 生产状态；
- SP、listen-host、U3DS、P2P Runtime 及最终 Harmony 运行排序仍 Pending。

### Evidence Gate

| Evidence Class | 结果 | 证据 |
|---|---|---|
| PureMemory | PASS | 现有 Item/Zombie 测试并入全量入口；本次结果 `186/186 PASS` |
| StaticIL | PASS | `ItemZombieOwnershipStaticILContractTests`；领域入口各 1 个，旧 Core Item/Zombie 权威类型为 0 |
| BuildArtifact | PASS | 主项目与 WhitelistTests Release 构建均 0 errors / 0 warnings；本次交付报告绑定重新计算的 DLL/EXE SHA-256 与 MVID |
| Runtime | PENDING | 未执行 SP、listen-host、U3DS 或 P2P 运行验收 |

## Batch 7：Animal、Structure、Barricade 与其他领域所有权

状态：源码结构、Release 构建、PureMemory 与结构 StaticIL 已完成；Runtime Pending

### 变更清单

| 项目 | 状态 | 说明 |
|---|---|---|
| `Adapters/Animal/Patches/AnimalManagerP0C2SendAnimalStatesPatch.cs` | 已整理 | namespace 与物理目录统一为 `SteamP2PFriends.Adapters.Animal.Patches` |
| `Adapters/Animal/Patches/AnimalManagerWorldSyncDiagnosticPatch.cs` | 已整理 | namespace 与物理目录统一为 `SteamP2PFriends.Adapters.Animal.Patches`，共享 Diagnostics core |
| `Adapters/Structure/Patches/BarricadeManagerRegionSyncPatch.cs` | 已迁移 | 从 `Core/Patches` 移入 Structure/Barricade 领域唯一入口 |
| `Adapters/Structure/Patches/StructureManagerRegionSyncPatch.cs` | 已迁移 | 从 `Core/Patches` 移入 Structure 领域唯一入口 |
| `Adapters/Structure/Patches/P0EBarricadeLifecycle/*` | 已迁移 | Barricade lifecycle 相关 Helper、Matcher、Registration、Transpiler 统一归属 |
| `Core/Registration/*`、Host/Session reset 引用 | 已更新 | 仅指向新入口，保持 Registration Trace 顺序 |
| `Core/Lifecycle/SteamP2PFriendsPlugin.PatchRegistry.cs` | 已更新 | Session reset 指向 Structure/Barricade 新入口；仅更新 namespace 引用 |
| `WhitelistTests/StaticIL/AnimalStructureOwnershipStaticILContractTests.cs` | 新增 | 验证 Animal/Structure/Barricade 唯一入口、旧类型缺失、Vehicle/Object Pending、Registration Trace 覆盖与注册入口形状 |
| `docs/architecture/animal-structure-ownership.md` | 新增 | 逐领域状态、Pending 原因、边界与证据 |
| `Core/Patches/Vehicle*` | Pending | Vehicle 归属证据不足，保留原位置，不创建并行目录/权威入口 |
| `Core/Patches/ObjectManager*`、`Issue7ObjectBinaryStateDiagnosticPatch.cs` | Pending | Object 区域同步、世界诊断和二进制观察尚无单一领域适配器边界，保留原位置 |
| 其他未确认跨领域补丁 | Pending | 保留在 `Core/Patches`，由 `Adapters.OtherPending` 记录原因 |

### 行为保持与证据边界

- Harmony target、owner、priority、patch method、注册顺序、生命周期、网络协议和生产 Authority Writer 未改变。
- Resource Production Control Seam 未接线；本批次不宣称功能修复或 Runtime 通过。
- PureMemory/既有领域测试与结构 StaticIL 全量入口通过；Runtime 仍需分别执行 SP、listen-host、U3DS、P2P 验收。
- 独立 Standards/Spec 审核记录与 BuildArtifact 指纹归档于本票 `audit/2026-08-26/Implementation-0.2.4.8-2334.md`。

## Batch 8：Evidence Class 测试结构与门禁

状态：四类测试物理结构、分类入口、Release 构建与 PureMemory/StaticIL/BuildArtifact 门禁已完成；Runtime Pending

### 变更清单

| 项目 | 状态 | 说明 |
|---|---|---|
| `WhitelistTests/Evidence/PureMemory/*` | 已迁移 | 既有纯内存领域测试、Platform/Security 测试、MultiObserver 测试与 Fakes 的唯一物理根；混合测试中的纯逻辑断言保留于此，未复制测试 |
| `WhitelistTests/Evidence/StaticIL/*` | 已迁移 | 身份、模块归属、Resource/Collision、Item/Zombie、Animal/Structure，以及 RC/HC/IUI/M1I/M2I/B11 的编译产物、Harmony 或 U3 元数据门禁 |
| `WhitelistTests/Evidence/BuildArtifact/BuildArtifactEvidenceTests.cs` | 新增 | 版本、FileVersion、MVID、插件 GUID、产物文件与 SHA-256 可重算性接缝 |
| `WhitelistTests/Evidence/Runtime/RuntimeEvidenceStatus.cs` | 新增 | 真实游戏 Runtime 状态占位，明确 SP/listen-host/U3DS/P2P 仍 Pending |
| `WhitelistTests/Program.cs` | 已更新 | 保持单一 `Program.Main`，按四类输出；Runtime 不计入 PASS 数量；Batch 8 历史回归 `189/189 PASS` |
| `docs/architecture/evidence-class-test-gates.md` | 新增 | 物理布局、证明边界、覆盖接缝和门禁解释 |
| `Tools/Verify-EvidenceClassLayout.ps1` | 新增 | 构建前核验四类目录、单一项目、单一入口和无旧测试根目录 |

### 证明边界

- PureMemory 只证明纯逻辑/内存接缝；StaticIL 只证明编译结构、注册形状和结构不变量；BuildArtifact 只证明当前加载产物身份可追踪；Runtime 必须通过真实游戏 Host/Guest 运行日志和共享 Case-ID 才能通过。
- BuildArtifact 测试中的 hash 是当前进程对加载 DLL 的重算形状检查；交付报告中的 hash/MVID 仍须由验收侧独立重算，不能互相替代。
- Runtime 本批次明确为 `PENDING`，没有用 PureMemory、StaticIL 或 BuildArtifact 的 PASS 升级替代运行时证据。

### Evidence Gate

| Evidence Class | 结果 | 证据 |
|---|---|---|
| PureMemory | PASS | 单一入口中的既有纯内存测试与 EvidenceClass Catalog/Registration Closure 测试；`IUI1–IUI4`、`M1I01–M1I05`、`M2I01–M2I08/M2I10–M2I11`、`B1–B10/B12` 已按范围保留 |
| StaticIL | PASS | 单一入口中的五组结构契约，加上 RC/HC/IUI/M1I/M2I/B11 的结构、Harmony 或 U3 元数据断言 |
| BuildArtifact | PASS | `BuildArtifactEvidenceTests`；Release DLL 版本、FileVersion、MVID、插件 GUID 和 SHA-256 重算 |
| Runtime | PENDING | `RuntimeEvidenceStatus`；未执行 SP、listen-host、U3DS、P2P 真实运行验证 |

完整回归结果与最终 SHA-256/MVID 归档于 Ticket 08 报告 `audit/2026-08-27/Implementation-0.2.4.8-0915.md`。

## Batch 9：Build Fingerprint 与独立产物关联

状态：运行时 Fingerprint 接缝、BuildArtifact 测试、Release 构建和独立 DLL 验证已完成；真实游戏 Runtime 仍 Pending。

本 Batch 的构建元数据统一来自 `Build/Version.props`：版本 `0.2.4.8`，发布通道 `Experimental`，插件 GUID `com.yu80rice.steamp2pfriends`，Default Case-ID `SPF-0.2.4.8-Experimental-StructureBaseline`。

### 变更清单

| 项目 | 状态 | 说明 |
|---|---|---|
| `Build/Version.props` | 已扩展 | 增加插件 GUID 与构建级 Default Case-ID；继续作为唯一版本元数据源 |
| `SteamP2PFriends.csproj` | 已扩展 | 编译前生成 `BuildMetadata` 常量，程序集属性由该常量消费 |
| `Properties/AssemblyInfo.cs` | 已收敛 | Assembly/File/Informational Version 与 AssemblyMetadata 不再重复硬编码版本值 |
| `WhitelistTests/Properties/AssemblyInfo.cs` 与测试项目生成目标 | 新增 | 测试程序集也从 `Build/Version.props` 生成并消费独立的统一元数据常量 |
| `Core/Build/BuildFingerprint.cs` | 新增 | 从加载程序集/DLL 读取版本、MVID、SHA-256、GUID 和共享 Case-ID |
| `SteamP2PFriendsPlugin.cs` | 已接线 | Awake 日志输出 Runtime Self-Reported Fingerprint；不改变注册与生产逻辑 |
| `SteamP2PFriendsPlugin.PatchRegistrationCriticalVerification.cs` | 已接线 | Fingerprint 不完整时并入既有 `DiagnosticBuildValid` fail-closed 门 |
| `WhitelistTests/Evidence/BuildArtifact/BuildArtifactEvidenceTests.cs` | 已加强 | 验证 Fingerprint 完整性、hash 独立重算与 Case-ID 共享接缝 |
| `Tools/Verify-BuildFingerprintArtifact.ps1` | 新增 | 独立读取交付 DLL，输出 Independent Artifact Verification |
| `docs/architecture/build-fingerprint-artifact-evidence.md` | 新增 | 记录元数据来源、证据边界、Case-ID 关联和测试接缝 |

### 证据门禁

| Evidence Class | 结果 | 证据 |
|---|---|---|
| PureMemory | PASS | 单一入口既有纯内存回归保持通过 |
| StaticIL | PASS | 单一入口既有结构、注册与结构不变量断言保持通过 |
| BuildArtifact | PASS | `192/192 PASS`；Release DLL 与测试 EXE 的统一版本、FileVersion、MVID、GUID、Case-ID 和 SHA-256 均可读取；独立脚本在带日志的默认及外部 Case-ID 关联模式均输出 `INDEPENDENT_ARTIFACT_VERIFICATION_PASS` |
| Runtime | PENDING | 本 Ticket 未执行真实 SP、listen-host、U3DS 或 P2P 场景；自报告日志不升级为 Runtime PASS |

本 Ticket 不修改 P2P 通道、SteamID、配置键、Harmony 注册顺序、Authority Writer、Resource Production Control Seam、当前标签或已归档版本。

## Batch 10：Resource Production Control Seam

状态：Production Control Seam 已接线；PureMemory、StaticIL、BuildArtifact 通过；真实 Runtime Pending

### 变更清单

| 项目 | 状态 | 说明 |
|---|---|---|
| `Adapters/Resource/ResourceProductionControlSeam.cs` | 新增 | 以 `SpatialObserverIndex` 为输入，合并 Host、Guest 和多观察者的 Resource 区域需求 |
| `Adapters/Resource/ResourceDomainAdapter.cs` | 已调整 | `OnRelease` 只提交已由控制接缝完成滞回校验的区域释放；区域退出按 Connection Token 清理快照 |
| `Adapters/Resource/ResourceRegionLifecycleAdapter.cs` | 已扩展 | 增加会话代与区域代校验的直接 Release Commit；旧资源状态账本仍为唯一领域状态入口 |
| `Core/ControlPlane/MultiObserverShadowCoordinator.cs` | 已接线 | 真实玩家采样驱动 Resource 接缝；会话、断线、重连和逐帧 Tick 均纳入同一入口 |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDomainModules.cs` | 已接线 | Resource 生命周期与复制适配器完成 Registration Closure 后配置控制接缝 |
| `WhitelistTests/Evidence/PureMemory/Adapters/Resource/ResourceProductionControlSeamTests.cs` | 新增 | 覆盖并集 Acquire、2 秒滞回、重入、重连/复位、真实领域适配器和区域代次 |
| `WhitelistTests/Evidence/StaticIL/ResourceProductionControlStaticILContractTests.cs` | 新增 | 验证控制接缝、协调器接线、唯一 Resource Release Commit 调用和旧安排入口无生产调用 |

### Authority Writer 与行为边界

- Resource 原生状态与网络写入仍由现有 `ResourceManager` 路径负责；本批次未新增第二个原生状态写入者。
- `ResourceProductionControlSeam` 唯一拥有观察者需求、区域租约和滞回释放调度；`ResourceDomainAdapter` 是唯一领域适配器提交入口。
- Resource 碰撞通过 `ResourceRegionLifecycleAdapter.IsRegionActive` 读取接缝已提交的区域资格；原有 Harmony target、owner、priority、注册顺序与日志语义保持不变。
- 采伐/重生仍由 `ServerSetResourceDead`/`ServerSetResourceAlive` 原生 Authority Writer 触发，之后更新 Resource 区域代次与快照账本；控制接缝发现待释放票据代次过期时会延后并刷新票据，不把过期票据提交给领域适配器。
- 只有 Host/授权 Guest 进入 Resource 需求并集；快照初始进入、按旧/新 Connection Token 的退出与重连清理、采伐增量和会话复位均经由已登记的 Resource 复制适配器；过期 Region Generation 的增量先拒绝并要求新基线，没有复制一套并行网络协议。

### Evidence Gate

| Evidence Class | 结果 | 证据 |
|---|---|---|
| PureMemory | PASS | Resource Production Control Seam 接缝测试 8 项；包含真实 ResourceDomainAdapter 高层链路、重连旧 token 负向校验和过期 Region Generation 延迟释放；全量入口最终结果见 Ticket 10 审计报告 |
| StaticIL | PASS | `ResourceProductionControlStaticILContractTests`；Resource 控制入口、唯一 Release Commit 调用及接线存在性均有产物契约 |
| BuildArtifact | PASS | 主项目与测试项目 Release 构建、版本/MVID/SHA-256 独立核验见 Ticket 10 审计报告 |
| Runtime | PENDING | 尚未取得同一 DLL/Case-ID 下的 SP、listen-host、U3DS、P2P Host/Guest 运行日志；不得以本批次静态或纯内存证据替代 |

### 必须保留的运行时验收项

- Host、授权 Guest、多个观察者同区/跨区的需求并集与碰撞覆盖；
- 0 -> 1 Acquire、N -> 0 后 2 秒 Hysteresis Release、滞回期间重新进入；
- Guest 重连的 Connection Generation、资源区域重建的 Region Generation 和 Session Reset；
- 远端资源碰撞、采伐/重生、区域重建、快照与增量复制的 Host/Guest 因果日志；
- 当 DLL 或核心生产源码再次变化时，重新生成共享 Case-ID 并独立核验 DLL hash。

## Batch 11：Resource 旧 Authority Writer 退出与 Runtime Gate

状态：旧 Resource 租约释放 Writer 已从生产调用图退出；真实 Runtime Gate `PENDING`，Resource Migration Slice 未完成。

### Authority Writer 状态

| Writer/路径 | 当前状态 | 证据与边界 |
|---|---|---|
| 旧租约释放路径：`ResourceDomainAdapter.OnRelease` → `ResourceRegionLifecycleAdapter.OnObserverRelease` | 已删除 | `ResourceRegionLifecycleAdapter.OnObserverRelease` 不再编译；`ResourceAuthorityRetirementStaticILContractTests` 断言旧入口不存在 |
| 当前租约权威：`ResourceProductionControlSeam` → `ResourceDomainAdapter.OnRelease` → `CommitRelease` | 已接线 | Ticket 10 生产接缝与 session/region generation 校验；本轮 StaticIL 再验证唯一调用 |
| U3-SDK `ResourceManager` 原生数据/协议执行器 | 保留，非本轮删除对象 | 继续负责原生资源状态、快照编码和 `ReceiveResources` 协议；是否已完全服从接缝的实际运行因果必须由 Runtime Gate 证明 |

### Runtime Gate 状态

当前仓库没有同一 `0.2.4.8` DLL、共享 Case-ID 下的 Host、Guest、多观察者、重连和离开/重新进入原始运行日志；本机也未发现可执行的 Unturned Runtime。因此：

- Host/Guest 日志与当次 DLL 的 SHA-256、MVID、版本、插件 GUID 关联：`PENDING`；
- Resource 碰撞、采伐、2 秒租约释放、generation 防护、快照与增量复制：`PENDING`；
- 不得把 Ticket 10 的 PureMemory/StaticIL/BuildArtifact PASS 升级为 Ticket 11 Runtime PASS；
- 不得把本 Batch 标记为 `Resource Migration Slice complete`，也不得推进依赖该结论的其他领域迁移。

### 本轮静态交付

- 新增 `ResourceAuthorityRetirementStaticILContractTests` 并接入单一测试入口；
- 证明旧租约释放调用退出，同时明确原生协议执行器仍需 Runtime 验收，避免“删除旧 writer”造成未验证的协议回归；
- 详细报告：见 `audit/2026-08-27/Implementation-0.2.4.8-Ticket11.md`。
