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
| StaticIL | PASS（由 Ticket 04 补齐） | Ticket 02 当时的待办已由 `audit/2026-08-26/ticket-04-static-metadata-snapshot.md` 与 Registration Trace 65 单元矩阵完成 |
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
| `audit/2026-08-26/ticket-04-static-metadata-snapshot.md` | 新增 | Release 产物与结构 StaticIL 的可复核快照 |

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

状态：**完成**——旧 Resource 租约释放 Writer 已从生产调用图退出；Runtime Gate 已通过（三端 1H+2G 动态测试 + 独立审核 PASS，2026-09-09）；**Resource Migration Slice 完成**，可推进依赖该结论的其他领域迁移。

### Authority Writer 状态

| Writer/路径 | 当前状态 | 证据与边界 |
|---|---|---|
| 旧租约释放路径：`ResourceDomainAdapter.OnRelease` → `ResourceRegionLifecycleAdapter.OnObserverRelease` | 已删除 | `ResourceRegionLifecycleAdapter.OnObserverRelease` 不再编译；`ResourceAuthorityRetirementStaticILContractTests` 断言旧入口不存在 |
| 当前租约权威：`ResourceProductionControlSeam` → `ResourceDomainAdapter.OnRelease` → `CommitRelease` | 已接线 | Ticket 10 生产接缝与 session/region generation 校验；本轮 StaticIL 再验证唯一调用 |
| U3-SDK `ResourceManager` 原生数据/协议执行器 | 保留，非本轮删除对象 | 继续负责原生资源状态、快照编码和 `ReceiveResources` 协议；是否已完全服从接缝的实际运行因果必须由 Runtime Gate 证明 |

### Runtime Gate 状态

Runtime Gate 已于 2026-09-09 通过。同一 `0.2.4.8` DLL（SHA-256 `5DF5A1F3…`、MVID `2ff47d8f…`，经独立跨路径实验证实为源码复现身份）、共享 Case-ID `SPF-0.2.4.8-Experimental-StructureBaseline` 下的三端（1 Host + 2 Guest）运行日志已取得：

- Host/Guest 日志与当次 DLL 的 SHA-256、MVID、版本、插件 GUID 关联：**通过**（三端自报逐字节一致，`Runtime-0.2.4.8-Ticket11-2314.md`）；
- Resource 碰撞、采伐、租约释放、generation 防护、快照与增量复制：**通过**（CollisionActivation 6376、HarvestDead 7/7 accepted、LeaseAcquire failed 0、滞回/重入/双端复制事件齐全）；
- 唯一 Authority Writer 因果链成立；已知工程缺陷 R4（deferred 区域退出事务 demand 不平衡致 4 次会话重建，用户无感）登记为审核后首批修复票，其 Runtime 验证归属 R4 票；
- 独立审核正式 **PASS**：`audit/2026-09-09/IndependentReview-0.2.4.8-Ticket11-2337.md`（7 项门禁全过，Spec/Standards 双轴 CLEAN）。

### 本轮静态交付

- 新增 `ResourceAuthorityRetirementStaticILContractTests` 并接入单一测试入口；
- 证明旧租约释放调用退出，同时明确原生协议执行器仍需 Runtime 验收，避免“删除旧 writer”造成未验证的协议回归；
- 详细报告：见 `audit/2026-08-27/Implementation-0.2.4.8-Ticket11.md`。

## Batch 12：Collision Migration Slice 基线（0.2.4.9）

状态：**静态完成**——Metadata Source 授予 `0.2.4.9`，Resource 表征门建立；Collision 未接线、生产 Writer 未变；Runtime 归 Collision 切片关单票 09。

版本：`0.2.4.9` / `Experimental`；默认 Case-ID：`SPF-0.2.4.9-Experimental-CollisionSlice`（末段标识迁移切片，与 `0.2.4.8` 的 `StructureBaseline` 区分）。不创建 git tag，不发 GitHub Release。

### 变更清单

| 项目 | 状态 | 说明 |
|---|---|---|
| `Build/Version.props` | 已更新 | 版本 `0.2.4.8` → `0.2.4.9`；默认 Case-ID 末段 `StructureBaseline` → `CollisionSlice`；发布通道仍为 `Experimental` |
| `ResourceProductionControlSeamTests.cs`（PureMemory） | 新增表征门 | M6P36 生产半径切比雪夫方形投影、M6P37 滞回常量与窗口、M6P38/M6P39 暂缓与一般失败的重试节奏及上限、M6P40 重试登记按观察者归属、M6P41 同区引用计数租约 |
| `SpatialObserverIndexTests.cs`（PureMemory） | 新增表征门 | SPI05 二维投影形状与世界边界裁剪 |
| `ResourceProductionControlStaticILContractTests.cs` | 新增契约 | 生产接缝半径读原版物件区域常量、世界尺寸读 `Regions.WORLD_SIZE`，不读 `ItemManager.ITEM_REGIONS`（半径不得升格为共享默认值） |
| `BuildArtifactEvidenceTests.cs` | 新增身份门 | 本阶段版本/通道/默认 Case-ID 冻结字面量与程序集自报身份一致 |
| `README.md`、`build-fingerprint-artifact-evidence.md`、本文件 | 已同步 | 展示文档与 Metadata Source 一致 |
| `Tools/Verify-Ticket09Documentation.ps1` | 已解耦 | 当前版本展示文档按 `Build/Version.props` 校验；`0.2.4.8` 历史记录按冻结版本校验，不再要求历史审计文件出现新版本号 |
| 生产源码（Resource/Collision 补丁与适配器） | 未触碰 | 本批次不是生产行为变更；Collision 仍不接线 |

### 证据类状态

| Evidence Class | 状态 | 说明 |
|---|---|---|
| PureMemory | PASS | Resource 表征门 6 项 + 空间投影表征 1 项；Resource 既有接缝/生命周期/快照/采伐测试全绿 |
| StaticIL | PASS | 半径来源契约；既有 Resource 生产控制、退休与采伐注册契约保持绿 |
| BuildArtifact | PASS | `Verify-BuildFingerprintArtifact.ps1` 独立核验版本、FileVersion、MVID、GUID、SHA-256 与 Case-ID |
| Runtime | PENDING | 本批次不宣称 Runtime；Collision 切片三端运行验收见票 09，影子与正式候选指纹区分见票 05/07 |

### 必须保留到 Runtime 的验收项

- Collision 切片的三端（1 Host + 2 Guest）运行必须使用正式切换候选，三端同一 SHA-256；
- 表征门（M6P36–M6P41、SPI05、半径来源契约）在票 02/03 迁入共享引擎前后必须保持绿，任何语义漂移都视为迁移失败。

## Batch 13：Resource 接入共享 Demand Projection Engine（0.2.4.9 / 票 02）

状态：**静态完成**——Control Plane 建立唯一观察者空间事实与共享投影引擎，Resource 生产接缝经声明式 Demand Policy 消费 typed Resource Demand，不再持有自己的观察者索引；租约、滞回、retry、补偿仍在接缝（票 03 迁编排引擎），生产 Writer 仍唯一。Runtime 归 Collision 切片关单票 09。

### 变更清单

| 项目 | 状态 | 说明 |
|---|---|---|
| `Core/ControlPlane/Demand/`（新增 6 文件） | 新增模块 | `ObserverPresence`（唯一空间事实）、`ObserverSpatialAuthority`（事实存储）、`DemandPolicy` + `EDemandRegionShape`（声明式政策）、`DomainDemand` / `DomainDemandProjection`（typed demand 与投影差异）、`DemandProjectionEngine`（枚举/裁剪/去重/计数/差异） |
| `Adapters/Resource/ResourceDemandPolicy.cs` | 新增 | Resource 域的政策声明：Domain Id、切比雪夫方形、资源半径来源、玩法资格 |
| `Adapters/Resource/ResourceProductionControlSeam.cs` | 已改 | 删除私有 `SpatialObserverIndex` 与 `worldSize`/`radius` 字段，改为注入 `DemandProjectionEngine` + `DemandPolicy`；构造期拒绝非 Resource 身份政策；对外新增 `ProjectedDemandRegionCount` 与带资格的重载 |
| `Core/ControlPlane/MultiObserverShadowCoordinator.cs` | 已改 | 接线点建立唯一权威+引擎、注册 Resource 政策并注入接缝；提交样本时携带玩法资格；汇总日志新增投影需求区域数与半径来源 |
| `Core/ControlPlane/Spatial/SpatialObserverIndex.cs` | 已扩 | 新增按区域计数（`CountObserversPerRegion` / `CountObserversInRegion`），计数从活跃集合现算，不另存会漂移的计数表 |
| `WhitelistTests/Evidence/PureMemory/MultiObserver/DemandProjectionEngineTests.cs` | 新增 | DPE01–DPE11：唯一事实与幂等、切比雪夫裁剪、去重计数、进入退出差异、连接代次失效、领域隔离、资格暂缓（Deferred Observer Demand）、声明失败闭合、移除释放、投影还原、暂缓中代次失效 |
| `WhitelistTests/Evidence/PureMemory/Adapters/Resource/ResourceProductionControlSeamTests.cs` | 新增 | M6P42–M6P46：事实与 typed demand 归 Control Plane、拒绝异域政策、资格由政策声明、移除清事实与投影、会话边界清事实与投影；既有 41 项经 `CreateSeam` 测试宿主接线保持原样 |
| `WhitelistTests/Evidence/StaticIL/DemandProjectionStaticILContractTests.cs` | 新增契约 | 唯一空间事实与唯一引擎、接缝无观察者索引字段/构造、Demand 命名空间零 Unturned/Unity 依赖、无跨域共享默认半径、引擎不调用领域执行、Resource 自声明政策、资格交由政策判定 |
| `WhitelistTests/Evidence/StaticIL/IlContractProbe.cs` | 重构 | IL 遍历探针收敛为共享实现；票 01 审计 §8-4 具名的跨文件重复在此消除 |
| `WhitelistTests/Evidence/StaticIL/ResourceProductionControlStaticILContractTests.cs` | 已改 | 私有 IL 遍历实现改为转发共享探针（调用点与断言不变）；`UpdateObserver` 断言按 4 参重载消歧 |
| `WhitelistTests/Program.cs` | 已改 | 注册 23 项新证据（DPE01–DPE11、M6P42–M6P46、Demand StaticIL 7 项）；唯一入口目标数 290 → 313 |

### 证据类状态

| Evidence Class | 状态 | 说明 |
|---|---|---|
| PureMemory | PASS | 新增 DPE01–DPE11 与 M6P42–M6P46；票 01 表征门与既有 Resource 接缝/快照/生命周期测试保持绿 |
| StaticIL | PASS | 新增 7 项结构契约；既有 Resource 生产控制、退休、采伐注册契约保持绿（`UpdateObserver` 断言按 4 参重载消歧） |
| BuildArtifact | PASS | `Verify-BuildFingerprintArtifact.ps1` 独立核验通过；版本身份仍为 `0.2.4.9` / `SPF-0.2.4.9-Experimental-CollisionSlice` |
| Runtime | PENDING | 本批次不宣称 Runtime；投影迁移的三端运行验收见票 09 |

### 必须保留到 Runtime 的验收项

- 票 01 表征门（M6P36–M6P41、SPI05、半径来源契约）与票 02 新增投影契约在票 03 迁入编排引擎前后必须保持绿；
- 三端日志里 `resourceDemandRegions` 与 `resourceProjectedDemandRegions` 的差异应可解释为「已物化租约」与「已投影需求」之差（暂缓/失败重试路径），不允许出现长期无解释的漂移。

### 不可变语义（票 02 起生效，后续票不得回退）

- 资格不合格 **不等于** 确认离开：领域投影保留既有贡献并记为 Deferred Observer Demand，只有确认离开、连接代次失效、会话重置或有界恢复策略才清理（`DemandProjectionEngine` 内注释与 DPE07/DPE11 锁定）；
- 观察者事实只有一份：领域接缝不得再持有自己的观察者索引或空间位置（StaticIL 锁定）；
- 不存在跨域共享默认半径：半径只经领域 Demand Policy 声明（StaticIL 锁定）。

### 移交票 04（本批不修，具名）

- 样本捕获层的 `capture-incomplete` 整体冻结仍在（`spec.md:61` 要求单条不可用记录不得冻结其它有效观察者的本拍更新）；票 02 已提供「资格不合格＝暂缓」的原语，票 04 需补样本不可用路径与其有界恢复策略（票 04 checklist 第 1–2 项）。

## Batch 14：Resource 经领域端口迁入共享生命周期编排引擎（0.2.4.9 / 票 03）

状态：**静态完成**——共享 Lifecycle Orchestration Engine 拥有需求聚合、0→1 Acquire、N→0 滞回 Release、身份校验、retry、补偿调度、局部故障隔离与统一诊断；Resource 只经 Domain Execution Port 操作原生状态，生产接缝退化为薄门面，原有通用生命周期状态机退出生产权威。生产 Writer 仍唯一，Collision 仍无生产写入资格。Runtime 归 Collision 切片关单票 09。

### 变更清单

| 项目 | 状态 | 说明 |
|---|---|---|
| `Core/ControlPlane/Lifecycle/`（新增 6 文件） | 新增模块 | `LifecycleOrchestrationEngine`（编排状态机 + 多域注册）、`IDomainExecutionPort` + `EDomainFailureKind`（领域执行端口与失败分类）、`LifecyclePolicy` + `RetrySchedule`（领域声明的滞回与 retry 节奏）、`ILifecycleDiagnostics` + `DefaultLifecycleDiagnostics`（统一诊断出口）、`LifecycleDiagnostic` + 结果/路径/级别枚举、`LifecycleEvents`（转换事件名） |
| `Adapters/Resource/ResourceExecutionPort.cs` | 新增 | Resource 域执行端口：把引擎的编排决策翻译成领域原生操作，把原生失败翻译成引擎可执行的结果分类；不聚合需求、不持有租约或滞回 |
| `Adapters/Resource/ResourceLifecyclePolicy.cs` | 新增 | Resource 的 Lifecycle Policy 声明（滞回窗口 + 暂缓/一般 retry 节奏与静默阈值），滞回值取自资源域自己的常量 |
| `Adapters/Resource/ResourceLifecycleDiagnostics.cs` | 新增 | Resource 诊断出口：把统一转换记录落成既有 `[ResourceObs]` 取证格式，运行日志字段/事件名/路径/结果词表与迁移前一致 |
| `Adapters/Resource/ResourceProductionControlSeam.cs` | 已重写 | 退化为薄门面：零编排集合字段、零嵌套编排类型，唯一持有的编排对象是共享引擎；对外行为面（会话/观察者更新/移除/时间推进/刷新/查询）保持不变 |
| `WhitelistTests/Evidence/StaticIL/IlContractProbe.cs` | 已扩 | `CountOperands`/`CountMethodCalls`/`CountFieldLoads`/`CountMemberReferences`/`SumOver*` 改为接受 `MethodBase` 并纳入构造函数——注册与接线调用只出现在构造函数里，漏掉会造成「唯一注册点」「不得触达某组类型」这类契约假绿 |
| `WhitelistTests/Evidence/StaticIL/LifecycleOrchestrationStaticILContractTests.cs` | 新增契约 | 引擎/端口/政策/诊断类型存在且 Lifecycle 命名空间零原生依赖、接缝零编排状态机、引擎只经端口触达原生状态、引擎消费 typed demand 不重新投影、Resource 自声明 Lifecycle Policy、全程序集唯一领域注册点、引擎无领域分支 |
| `WhitelistTests/Evidence/StaticIL/ResourceProductionControlStaticILContractTests.cs` | 已改 | 代次读取契约移向 `ResourceExecutionPort.ReadRegionGeneration`（唯一 RegionKey 入口、经 `RegionGeneration.FromNative`）；失败分类边界与取证 helper 契约改指共享引擎的 `ProcessEntered` / `ProcessSingleRegionEntry` / `DescribeFailure` |
| `WhitelistTests/Evidence/PureMemory/MultiObserver/LifecycleOrchestrationEngineTests.cs` | 新增 | LOE01–LOE15：消费共享投影的 typed demand、需求聚合与一次 Acquire、滞回调度与重入、区域代次与会话代次身份门、retry 随领域政策并在成功后清除、单区域隔离、跨域隔离、事务回滚与补偿、释放幂等、诊断关联字段、注册闭合（会话内拒绝 / 会话间开放）、成功 Acquire 可观测、熔断领域不拖停其它领域 |
| `WhitelistTests/Evidence/PureMemory/Adapters/Resource/ResourceProductionControlSeamTests.cs` | 已改 | M6P35 的失衡残留反射目标随 retry 登记迁入引擎的每域状态（两级反射，不新增仅供测试的生产 API）；断言不变 |
| `WhitelistTests/Program.cs` | 已改 | 注册 22 项新证据（LOE01–LOE15、Lifecycle StaticIL 7 项）；唯一入口目标数 313 → 335 |

### 证据类状态

| Evidence Class | 状态 | 说明 |
|---|---|---|
| PureMemory | PASS | 新增 LOE01–LOE15；票 01 表征门（M6P36–M6P41、SPI05、半径来源契约）与票 02 投影契约（DPE01–DPE11、M6P42–M6P46）全部保持绿 |
| StaticIL | PASS | 新增 7 项结构契约；既有 Resource 生产控制、退休、采伐注册与 Demand 投影契约保持绿 |
| BuildArtifact | PASS | `Verify-BuildFingerprintArtifact.ps1` 独立核验通过；版本身份仍为 `0.2.4.9` / `SPF-0.2.4.9-Experimental-CollisionSlice` |
| Runtime | PENDING | 本批次不宣称 Runtime；编排迁移的三端运行验收见票 09 |

### 扰动负控制（证明新门禁非同义反复）

| 扰动 | 实测 FAIL |
|---|---|
| 引擎去掉滞回窗口（释放立即到期） | LOE03、M6P02、M6P03、M6P37（4） |
| 接缝加回一个编排集合字段 | Lifecycle Seam Holds No State Machine 与合成门（2） |
| 引擎持有具体端口实现类型 | Lifecycle Engine Reaches Native Only Via Port 与合成门（2） |
| 引擎读取 Domain Id 常量（引入领域分支） | Lifecycle No Domain Branch 与合成门（2） |
| 引擎内置默认 Lifecycle Policy | Lifecycle Resource Policy Declaration 与合成门（2） |
| 出现第二处领域注册点（模拟 Collision 接线） | Lifecycle Single Domain Registration 与合成门（2） |
| 改快一般失败 retry 起步间隔 | M6P39（1） |
| 引擎不再登记 acquire retry | LOE07、LOE08、M6P31、M6P32、M6P38、M6P39、M6P40（7） |
| 引擎跳过补偿执行 | LOE10、M6P13、M6P14、M6P17、M6P23（5） |
| 去掉注册闭合（会话内可新增领域） | LOE13（1） |
| 去掉成功 Acquire 转换诊断 | LOE14（1） |
| 熔断领域重新阻断推进路径 | LOE15（1） |

扰动逐次还原并复核回到全绿；「引擎不再登记 retry」与「跳过补偿」两组同时打到票 01 表征门，证明资源已验收语义确实由共享引擎承载而非旁路。

### 不可变语义（票 03 起生效，后续票不得回退）

- 唯一对外行为接缝是 Lifecycle Orchestration Engine；Resource 与后续领域都是该接缝上的 Domain Execution Port（StaticIL 锁定）；
- 领域不得持有编排状态机：需求计数、租约、滞回登记、retry 登记与观察者集合都归引擎（StaticIL + LOE 行为面锁定）；
- 引擎不得触达领域执行类型、不得读取 Domain Id 常量、不得内置默认政策或默认半径（StaticIL 锁定）；
- 编排引擎消费 Session Epoch / Connection Generation / Region Generation，不重新生成也不合并这些轴（引擎无对应写入面，身份门由 LOE05/LOE06 锁定）；
- 故障隔离单元至少是 Domain Id + Region Key + Transition（LOE08 单区域、LOE09 跨域）。

### 移交票 04（本批不修，具名）

- **retry 登记仍是「区域单槽 + 拥有者观察者」**：本票以行为保持方式迁移，未改变票 01 表征的 retry 语义（M6P38/M6P39/M6P40 锁定的是该语义）；`spec.md` 要求的「观察者贡献事务区分领域/区域/观察者/连接代次/会话，禁止区域单槽互吞」仍归票 04（其 checklist 第 5 项）。引擎已把登记收进每域状态，改键为事务粒度不需要再动引擎结构；
- **样本捕获层 `capture-incomplete` 整体冻结**与「无效样本未进入 Deferred Observer Demand」仍归票 04（checklist 第 1–2 项），本票未触碰该路径；
- **会话身份有界恢复/显式熔断**仍归票 04（checklist 第 4 项）：本票保留既有失败闭合（`repair-required` + 新写入拒绝）语义，未新增有界恢复策略。

### 双轴审查链（每轮全新实例，无延续、无复用）

| 轮 | 本轮增量 | Standards | Spec |
|---|---|---|---|
| 1 | 全量首轮（Lifecycle 模块 6 文件 + Resource 执行端口/政策/诊断 + 接缝重写 + 22 项新证据 + 文档） | CLEAN（0 硬违规；5 判断项，其中枚举死成员已修） | **BLOCKING 3**：Registration Closure 缺失；成功 Acquire 无转换诊断；RepairRequired 阻断跨域刷新 |
| 2 | 注册闭合 + 成功 Acquire 诊断 + 熔断领域跳过 + 枚举裁剪 + 诊断构造收敛 + LOE13–LOE15 | CLEAN（0 硬违规；新增 1 项重复代码、1 项命名副作用） | **BLOCKING 2**：注册闭合被过度收紧为永久闭合（`EndSession` 后不重开），与「本次会话」措辞不符；成功 Acquire 诊断缺尝试次数与原因 |
| 3 | 闭合边界收窄为会话级 + 成功 Acquire 补齐 `attempt`/`reason` + `EnsureSessionActive`/`IsAdvanceSkipped` 拆分 + LOE13/LOE14 扩展 | **CLEAN**（0 硬违规；新增 1 项重复代码需收口） | **CLEAN**（两条 BLOCKING 逐项闭合；未越界票 04） |
| 4 | `NextAttemptNumber` 抽出供两处复用 + 删除有副作用的谓词改为 `ReportSkippedFaultedDomain` | **CLEAN**（0 硬违规；2 项新增判断项） | **CLEAN**（逐项「无差距」：取值时机与表达式等价、跳过路径状态转移等价、去重键与事件字段保持、无范围扩张） |

审查期间的三处**测试自身错误**（LOE06 在会话结束后仍调 `AdvanceTime`、假体把一次被拒绝的释放计入释放次数、注入失败同时挡住了补偿用的重新进入）已在审计报告 §3 具名并修正；一处**探针缺陷**（`IlContractProbe` 的原先实现跳过构造函数，会让「唯一注册点」契约假绿）已具名并修正。

### 判断项延期（全部具名；双轴确认不阻断）

1. `Report(...)` 的可选参数簇（领域/区域/代次/观察者/尝试/角色/级别）——第 1–4 轮持续具名；合并为上下文对象会把调用点变成 builder 链，本切片不引入；
2. `AcquireAttemptCount` 现为对 `NextAttemptNumber` 的纯转发（第 4 轮具名）——保留它使成功 Acquire 调用点的取值意图可读；若后续另有第二个消费方即应内联；
3. `AdvanceTime`/`Flush` 循环入口的四行守卫重复（第 4 轮具名）——仅两处、四行，再抽一层守卫不划算；
4. `IsRegistrationClosed` 与 `IsSessionActive` 读同一字段（第 3/4 轮具名）——已用 XML 注释固定「闭合边界＝会话本身」的视角，若将来两者语义分叉必须拆开；
5. `Build` 与 `Report` 的构造形状仍近似（第 2 轮具名）——派生逻辑已收敛到 `IsSharedPath`/`DeriveSeverity`；两者携带的关联字段不同，强行合并会退回第 1 项；
6. 诊断去重键为字符串拼接（第 2 轮具名）——沿用本文件既有 `TransitionOnce` 键模式，未新增长期形态；
7. 生产接缝现为薄门面（Middle Man 形状，第 1 轮具名）——ADR 0011 明确要求保留兼容门面，不构成违规；
8. `ResourceProductionControlSeamTests` 的失衡残留辅助走三级反射（第 1 轮具名）——为不新增「仅供测试」的生产 API，接受该耦合；引擎结构再变时需同步该辅助。

## Batch 15：共享控制面正式切换准入不变量（0.2.4.9 / 票 04）

状态：**静态完成**——正式切换所需的控制面不变量已在共享引擎上落地：坏样本只暂缓它自己的观察者、暂缓不触发破坏性释放、暂缓/熔断/挂起都有有界心跳与恢复闭环、会话身份有界恢复或显式熔断、retry 与观察者贡献事务粒度一致（区域单槽退出）、故障隔离单元至少是 Domain Id + Region Key + Transition、共享外层捕获不再跨域结束会话。**本批不切换 Collision Authority Writer，也不宣称准入门 Runtime 通过**（Collision 执行端口与切换分别归票 06/08，Runtime 归票 09）。

### 变更清单

| 项目 | 状态 | 说明 |
|---|---|---|
| `Core/ControlPlane/Lifecycle/BoundedHeartbeat.cs` | 新增 | `HeartbeatPolicy`（间隔 + 重复上限）与 `BoundedHeartbeat` 状态机：首条 → 按间隔重复 → 用尽写显式终止记录 → 等待状态变化重新起搏；持续状态既有间隔下限又不无限刷屏 |
| `Core/ControlPlane/Lifecycle/SessionIdentityGate.cs` | 新增 | `ESessionIdentityState`（Ready/Recovering/CircuitBroken）与身份门：身份缺失或不一致先有限恢复（自带心跳），窗口耗尽显式熔断，熔断后恢复要求重建会话；它只产出决策，不结束任何会话 |
| `Core/ControlPlane/Demand/ObserverSampleAdmission.cs` | 新增 | 逐条样本准入策略：单条记录不可用只暂缓该观察者，整批不可判定才整批暂缓；身份不可读会关上本拍的缺席移除资格；重复记录不改变结论 |
| `Core/ControlPlane/Lifecycle/LifecyclePolicy.cs` | 已改 | 新增领域声明的 `HeartbeatPolicy`（持续状态诊断节奏）；滞回与 retry 语义不变 |
| `Core/ControlPlane/Lifecycle/LifecycleEvents.cs` | 已改 | 新增 `ObserverDeferred`（样本暂缓及其心跳/闭环）、`SessionSuspended`（写入闸门）、`DomainFault`（熔断及其心跳/恢复） |
| `Core/ControlPlane/Lifecycle/LifecycleOrchestrationEngine.cs` | 已改 | ①retry 登记键由「区域」改为「区域 + 观察者 + 连接代次」事务身份，连接代次失效时撤销陈旧登记（进补偿列表）；②新增 `DeferObserver`（Deferred Observer Demand：贡献原样保留、不递减需求、不登记释放、不触达领域释放，持续时有界心跳、恢复时闭环）；③新增 `SuspendWrites`/`ResumeWrites`（会话身份不确定或共享面故障恢复期间拒绝写入与破坏性释放，租约/需求/暂缓态全部保留，持续时有界心跳）；④熔断领域的有界心跳与「会话收尾即有限恢复」闭环；⑤`BeginSession` 按 Domain Id 隔离熔断领域（不参与新会话，但不再挡住其它领域；无任何领域可参与时仍失败闭合）；⑥熔断标记只由会话收尾恢复，不在开始新会话时静默清除 |
| `Core/ControlPlane/MultiObserverShadowCoordinator.cs` | 已改 | ⓪共享面故障通道改为有界心跳 + 走通时写 `fault-cleared` 闭环（替换「计数器封顶后永久静默」），退避等待期与身份不确定期推进引擎时钟；①样本捕获改为逐条准入：单条记录不可用（含连接代次不可得）只暂缓该观察者，整批不可判定才整批暂缓且不移除任何人；暂缓观察者的连接身份与令牌在提交时被显式续用（不因本拍没读到而作废）；②`ReconcileResourceProduction` 只对「既缺席又不在暂缓集」的观察者走 `RemoveObserver`，且**仅在本拍具备缺席移除资格（无身份不可读记录）时**才执行移除，不具备资格时保留既有追踪集合留待下一拍判定；③宿主会话身份改由身份门裁决（有界心跳 → 显式熔断），身份不确定时挂起写入并保留租约，不再只打一条去重日志后静默；④共享面故障恢复不再 `EndSession`，改为挂起写入并保留会话；⑤熔断后身份重新可用时重建会话（旧 Session Epoch 与旧代次无写入资格）；⑥汇总日志新增 `resourceDeferredObservers` 与 `resourceWritesSuspended` 两个运行时可观测字段 |
| `SteamP2PFriendsPlugin.cs` | 已改 | 更新入口拆成两个独立故障边界（`TickMultiObserverShadowIsolated` / `TickZombieIsolated`）：僵尸 tick 抛错只落僵尸域自己的故障入口，不再经共享外层捕获去结束其它领域的会话 |
| `Adapters/Zombie/ZombieRegionLifecycleAdapter.cs` | 已改 | 新增 `ReportTickFailure`：本域 tick 故障的独立入口——持续故障按本域声明的有界心跳写出（首条 + 按间隔重复 + 显式终止），本域 tick 重新走通时写 `tick-fault-cleared` 恢复闭环并重新起搏；既不结束会话也不清空其它领域 |
| `Adapters/Resource/ResourceProductionControlSeam.cs` | 已改 | 薄门面新增转发：`DeferObserver` / `SuspendWrites` / `ResumeWrites` / `IsWriteSuspended` / `DeferredObserverCount` |
| `Adapters/Resource/ResourceLifecyclePolicy.cs` | 已改 | 声明持续状态心跳节奏（5s 间隔、重复上限 6） |
| `WhitelistTests/Evidence/StaticIL/ControlPlaneReadinessStaticILContractTests.cs` | 新增契约 | 9 项（含聚合门）：事务粒度 retry 键、暂缓路径零释放、身份门纯粹且不结束会话、协调器经逐条准入策略、共享外层捕获不结束会话（含更新入口只经两个独立边界）、持续故障心跳不是一次去重 |
| `WhitelistTests/Evidence/StaticIL/IlContractProbe.cs` | 已扩 | 新增 `CountExceptionHandlers`：证明某个域 tick 处于自己的异常边界内 |
| `WhitelistTests/Evidence/PureMemory/MultiObserver/LifecycleOrchestrationEngineTests.cs` | 已扩 | ARI01–ARI10（见下），并扩测试假体：按观察者归属登记复制贡献、`ThrowOnSessionEnd`、`SessionBeginCalls`、`RestoreRegionStateCalls` |
| `WhitelistTests/Evidence/PureMemory/MultiObserver/ObserverSampleAdmissionTests.cs` | 新增 | SAM01–SAM03：逐条准入、整批判定、身份不可读与重复记录 |
| `WhitelistTests/Evidence/PureMemory/MultiObserver/SessionIdentityGateTests.cs` | 新增 | SIG01–SIG03：直接驱动身份门——窗口内恢复不要求重建会话、窗口耗尽显式熔断且心跳有界、熔断恢复要求重建会话、`Reset` 回到未确认态 |
| `WhitelistTests/Evidence/PureMemory/Adapters/Resource/ResourceProductionControlSeamTests.cs` | 已改 | M6P35 的失衡残留辅助按「事务键上的 Region 字段」匹配后移除（键不再是裸 `RegionKey`）；断言不变 |
| `WhitelistTests/Program.cs` | 已改 | 注册 16 项新证据（ARI01–ARI10、SAM01–SAM03、SIG01–SIG03）与 9 项 StaticIL 契约；唯一入口目标数 335 → 360 |

### 证据类状态

| Evidence Class | 状态 | 说明 |
|---|---|---|
| PureMemory | PASS | 新增 ARI01–ARI10、SAM01–SAM03 与 SIG01–SIG03；票 01 表征门、票 02 投影契约、票 03 编排契约全部保持绿 |
| StaticIL | PASS | 新增 9 项准入结构契约（含僵尸域故障闭环绕过共享日志配额、共享面故障通道有界心跳 + 恢复闭环）；既有 Lifecycle/Demand/Resource 契约保持绿 |
| BuildArtifact | PASS | `Verify-BuildFingerprintArtifact.ps1` 独立核验通过；版本身份为 `0.2.4.9` / `SPF-0.2.4.9-Experimental-CollisionSlice`；两次 Release Rebuild 指纹一致 |
| Runtime | PENDING | 本批次不宣称 Runtime；Shadow/正式切换候选与三端验收见票 05–09 |

### 新证据清单

| 证据 | 锁定内容 |
|---|---|
| ARI01 | 单条不可用样本只暂缓它自己的观察者：其它观察者仍提交本拍更新并在新区域建租约，另一领域与暂缓者自身贡献不受影响 |
| ARI02 | 暂缓不触发破坏性释放：持续 20 拍暂缓 + 推进 + 刷新后需求/租约/复制贡献原样保留；确认离开仍走滞回释放 |
| ARI03 | 暂缓心跳有界且必留闭环：首条 + 重复到上限 + 显式终止（不再刷屏），观测恢复即闭环清标记；新观察者进入暂缓重新起搏 |
| ARI04 | 暂缓贡献仍计入需求：同区另一观察者离开后需求只降到暂缓者那一份，租约不提前进入滞回 |
| ARI05 | retry 事务粒度：同区两观察者各自保留资格、互不吞并，各自到点只补齐自己的复制贡献 |
| ARI06 | 陈旧连接代次无写入资格：换连接后旧登记撤销、只剩一份资格，新事务尝试序号从 1 起算（不继承），新代次驱动重试 |
| ARI07 | 身份不确定阻断写入与破坏性释放，但保留全部租约/需求/暂缓态；恢复写闭环并重新放行 |
| ARI08 | 身份不可恢复＝显式熔断：持续留痕、贡献保留、重建会话后旧 epoch/代次不再有资格 |
| ARI09 | 故障隔离单元至少是 Domain Id + Region Key + Transition：同域一区 acquire 失败与另一区、另一领域无关；一区释放失败只保留该区待释放登记且不升级成领域熔断 |
| ARI10 | 熔断领域持续异常有界心跳；不参与新会话（未收到 `OnSessionBegin`）也不挡住其它领域；会话收尾成功即恢复并闭环 |
| SAM01–SAM03 | 逐条准入策略：单条不可用只暂缓该观察者；整批不可判定整批暂缓；身份不可读关上缺席移除；重复记录不改变结论 |
| SIG01–SIG03 | 身份门行为面：窗口内恢复（不要求重建）、窗口耗尽显式熔断 + 有界心跳（首条 + 重复到上限 + 显式终止）、熔断后恢复必须重建会话（旧 epoch/代次无资格）、`Reset` 回到未确认态 |

### 扰动负控制（证明新门禁非同义反复）

| 扰动 | 实测 FAIL |
|---|---|
| retry 事务键塌回「区域单槽」 | ARI05、ARI06、M6P31、M6P32、M6P34、M6P38、M6P39、M6P40、LOE07、LOE08（10） |
| 暂缓心跳每拍重新起搏 | ARI03（1） |
| 取消挂起写入闸门 | ARI07、ARI08（2） |
| 熔断领域心跳退回「一条记录后静默」 | ARI10（1） |
| 熔断领域照常参与新会话 | ARI10、M6P30（2，加固 ARI10 后复测） |
| 陈旧登记两处撤销同时取消 | ARI06、M6P33（2） |
| 逐条准入塌回整批冻结 | SAM01、SAM03（2） |
| 更新入口直连两域（共享外层捕获复活） | Readiness Outer Catch Does Not End Session 与合成门（2） |
| 故障处理入口重新结束会话 | Readiness Outer Catch Does Not End Session 与合成门（2） |
| 恢复闭环额外要求心跳仍在运行 | Readiness Shared Fault Channel 与合成门（2） |
| 同一故障 episode 内重启心跳 | **无咬合**（协调器故障通道无纯内存宿主，见审计报告 §8-5） |

仅关「连接代次失效撤销」或仅关「区域退出撤销」两处扰动各自**无咬合**（355/355）：这两条撤销路径互为冗余，单独存在即可维持不变量——如实具名，并由两者同时取消时的 ARI06 + M6P33 取证。扰动逐次还原并复核回到全绿（见 `.scratch/collision-migration-slice/evidence/ticket04-perturbation.md`，本地证据）。

### 不可变语义（票 04 起生效，后续票不得回退）

- 样本不可用不等于零需求、不等于离开：单条记录不可用只进 Deferred Observer Demand，其贡献保留；清理只由确认离开、代次失效、会话重置或有界恢复策略触发（ARI01/ARI02/ARI04 + StaticIL 暂缓路径零释放锁定）；
- 持续异常必须有界心跳：样本暂缓、领域熔断、写入挂起三者都要「首条 + 有界重复 + 显式终止 + 恢复闭环」，不得一条去重日志后永久静默（ARI03/ARI10 + StaticIL 锁定）；
- 会话身份不确定时阻断写入与破坏性释放，但不清空任何领域；恢复不了就显式熔断，熔断后的恢复必须重建会话（ARI07/ARI08 + StaticIL 身份门锁定）；
- retry 身份与观察者贡献事务粒度一致：区域 + 观察者 + 连接代次，禁止区域单槽互吞（ARI05/ARI06 + StaticIL 事务键锁定）；
- 共享外层捕获不得结束会话：僵尸与共享面各有独立故障边界，更新入口只经这两个边界调用各域 tick（StaticIL 锁定）。

### 双轴审查实际轮次记录（含流程偏离；非合规宣称）

| 轮 | 本轮增量 | Standards | Spec |
|---|---|---|---|
| 1 | 全量首轮（3 个纯类型 + 引擎/协调器/插件改动 + 7 项结构契约 + ARI01–ARI10 + SAM01–SAM03 + 文档） | CLEAN（0 硬违规；3 判断项） | **BLOCKING 2**：身份不可读时仍会删除既有 Resource 观察者；暂缓观察者的连接身份实际未保留 |
| 2 | 两项 BLOCKING 修复（缺席移除闸门进删除点、连接身份显式续用）+ 三项判断项收口（心跳影子标记改用 `IsRunning`、暂缓集只构造一次、两个只读属性接入汇总日志）+ 收紧逐条准入契约 | CLEAN（0 硬违规；2 判断项） | **BLOCKING 1**：故障恢复清空追踪 → 永久幽灵需求 |
| 3 | 故障恢复分支不再重置追踪（改为 `ForceImmediateReconcile()` + 挂起写入）、抽出具名 `ForceImmediateReconcile`、补齐审计报告与产本身份 | **CLEAN**（0 硬违规） | **BLOCKING 2**：审计报告尚未落盘而票面已宣称闭环；09-17 未跟踪材料属范围外（已具名并从提交范围排除） |
| 4 | 补 SIG01–SIG03 直接驱动身份门（并修正身份门「状态转换记录与心跳首条」双计数）；僵尸域故障入口改为有界心跳 + `tick-fault-cleared` 恢复闭环 | **CLEAN**（0 硬违规） | **BLOCKING 2**：身份门行为面无直接证据（ARI07/08 只驱动引擎挂起）；僵尸域故障入口「打到上限即永久静默」且无恢复闭环 |
| 5 | 僵尸域故障心跳/闭环绕过共享日志配额（新 `SafeFault` 出口 + StaticIL 契约）；票面/报告/manifest 数字与 `git diff --check` 修正 | **CLEAN**（0 硬违规） | **BLOCKING 2**：故障心跳/闭环仍可能被共享日志配额静默吞掉；文档数字互相矛盾 |
| 6 | 共享面故障通道改为有界心跳 + `fault-cleared` 闭环（替换计数器封顶）、退避等待期与身份不确定期推进引擎时钟（新 StaticIL 契约）；全部数字统一为 360/360 与 9 项契约 | **CLEAN**（0 硬违规） | **BLOCKING 2**：持续故障期间引擎挂起心跳因提前返回而无法推进；文档数字仍互相矛盾 |
| 7 | 共享面恢复闭环改由故障 episode 决定（心跳 Exhausted 后恢复仍写闭环，契约收紧）、manifest 契约计数修正、去提前结论 | **CLEAN**（0 硬违规；判断项含新增契约需补扰动——本轮以 P11b 补齐） | **BLOCKING 3**：心跳用尽后恢复不写闭环；manifest 契约计数仍写 7 项；票面提前宣称双 CLEAN |
| 8 | 同一故障 episode 内不得重启心跳（退避失败不再无限重新起搏）、扰动台账补齐 P11/P11b/P11c 并统一计数、票面去提前结论 | **CLEAN**（0 硬违规） | **BLOCKING 2**：§1 扰动计数未同步；产本身份与代码改动后的产物不一致 |
| 9 | 计数同步、代码冻结后重新双构建并回填最终身份、双次 Rebuild 的 Run 1/Run 2 逐次身份留存、manifest 与审计的审查链对齐 | 本轮未派（不符合成文流程） | **CLEAN 不计入链**：由同一实例 SendMessage 续接产出 |
| 10 | 补做零上下文双轴复审（对象=提交 `9171641` 冻结产物），并按结论修正流程记录、把双次 Rebuild 与扰动证据随提交交付 | **CLEAN**（0 硬违规） | **BLOCKING 2**：审查链自我声明与事实不符；双 Rebuild／扰动证据未随提交交付 |
| 11 | 修正本报告标题、证据交付随提交落地（`60e12b2`）后的零上下文双轴复审（Standards 侧） | **CLEAN**（0 硬违规；2 项判断项记入报告 §9-10） | —（Spec 侧见第 12/13 轮） |
| 12 | 修正 manifest Batch 15 失实标题与 Batch 14 被误改标题（`6985a09`） | — | **BLOCKING 1**：Batch 15 标题仍为合规宣称且误改了 Batch 14 标题 |
| 13 | 标题修正后的零上下文 Spec 收尾复核 | — | **CLEAN**（无发现） |

审查按轮推进：第 4–6、9 轮未按「每轮两轴各一新实例」执行、第 9 轮判词为续接产物（不计入链）；第 10–13 轮以零上下文双轴复审收尾并授予产物身份（Standards r11 CLEAN / Spec r13 CLEAN）。逐轮处置见 `audit/2026-09-18/Implementation-0.2.4.9-CollisionTicket04-2330.md` §10）。

### 判断项延期（全部具名；双轴确认不阻断）

1. 三处「有界心跳」起搏样板近似（`MarkRepairRequired` / `StartDeferredHeartbeat` / `SuspendWrites`）——重复量小，第四处消费者出现时应抽 `BoundedHeartbeat.Restart`；
2. `Tick` 的变更原因持续累积（关机闩、故障退避、身份门、采样核对、汇总日志、故障恢复挂起）——仓库无 `Tick` 单一职责的成文约束；
3. 诊断原因码仍为裸 `string`；4. 汇总日志跨多个只读属性拼串（轻微 Feature Envy，同时充当新只读属性的生产消费者）；5. `ObserverSampleAdmissionPlan` 构造参数 6 个（Data Clumps 边界，构造为 `internal`）；
6. 票 03 已具名的存量判断项（`Report` 可选参数簇、`AcquireAttemptCount` 纯转发、`AdvanceTime`/`Flush` 守卫重复、注册闭合与 `IsSessionActive` 同字段、`Build`/`Report` 形状近似、诊断键字符串拼接、薄门面 Middle Man、测试三级反射）本票未新增未恶化；薄门面因新增 5 个转发成员略增面积，仍属 ADR 0011 要求的兼容门面。

### 缝合缺口（全部具名，无静默跳过）

- **协调器接线无纯内存宿主**：`MultiObserverShadowCoordinator` 与插件更新入口依赖 Unity/Unturned 类型，PureMemory 无法构造其接线。因此「暂缓观察者连接身份续用」「不具备缺席移除资格时保留追踪集合」「故障恢复不结束会话且不清空追踪」三点只有结构契约 + 代码级注释 + 审查复核，没有行为门直接覆盖（审计报告 §8-1）。
- **两处扰动无咬合**（P6/P6b：陈旧代次登记的两条撤销路径互为冗余；P10a：闸门读取当时还在日志分支）已如实具名，不作为已验证计入（审计报告 §8-2/§8-3）。

### 移交票 06/07/08/09（本批不做，具名）
- Collision 的 Demand Policy、只读影子与执行端口归票 05/06；本批**未新增任何 Collision 执行类型、未切换 Authority Writer**（StaticIL 单域注册点契约仍绿）；
- 正式切换与旧 RemoteCoverage Writer 退役归票 08；准入门 Go/No-Go 证据闭合归票 07；
- Runtime（三端、Shadow 与正式候选指纹区分）归票 09：本批只落静态准入不变量，不宣称任何 Runtime PASS。

## Batch 16：Collision 声明式 Demand Policy 与只读影子验证（0.2.4.9 / 票 05）

状态：**静态完成**——Collision 按自己声明的 Demand Policy（原版物件区域半径的切比雪夫投影）从**同一份** canonical 观察者事实算出 typed Collision Demand，**含 Host**、不扫描客户端名册；只读影子把新旧投影按观察者贡献与聚合 `Domain Id + Region Key` 逐条分类（预期差异 4 类 / 禁止差异 7 类，另带原因词，无法归因即失败闭合），并按「确认离开才清理」消费准入计划（暂缓不等于离开）。**旧 `RemoteCoverage` Writer 仍是唯一生产写入者**，影子零原生写入；只读 1H2G 影子 Runtime 待人工执行，且不得当作正式切换证据（Execution Port 与切换分别归票 06/08，Runtime 归票 09）。

### 变更清单

| 项目 | 状态 | 说明 |
|---|---|---|
| `Adapters/Collision/CollisionDemandPolicy.cs` | 新增 | Collision 域的政策声明：Domain Id、切比雪夫形状、半径来源字面量 `LevelObjects.OBJECT_REGIONS`、资格=观察者事实的玩法资格（Host 亦在其列）；只声明，不扫描名册、不持有位置、半径不升格为共享默认值 |
| `Adapters/Collision/CollisionShadowFrame.cs` | 新增 | 影子对照一拍输入（新侧当前/上一拍区域、旧侧覆盖、两侧认领者）与两种认领者类型；旧侧认领者按四态（在场且合格/在场但不合格/暂缓/已离开）表达，暂缓不得被当成离开 |
| `Adapters/Collision/CollisionShadowReport.cs` | 新增 | 差异分类枚举（4 预期 + 7 禁止）、处置、差异（聚合键 + 观察者/连接代次归因 + 原因词）与报告计数 |
| `Adapters/Collision/CollisionShadowComparator.cs` | 新增 | 纯函数分类器：异域整帧失败闭合、越界键、Host 新增覆盖、旧侧无在场合格认领者、canonical 生命周期补齐、授权认领者缺区、静止抖动、跨观察者错误释放、凭空需求 |
| `Adapters/Collision/Patches/LevelObjectRemoteCollisionPatch.cs` | 已改 | **只读** `CaptureShadowSnapshot`（抄旧覆盖区域与旧侧中心，编码转换留在适配器边界内）；旧 Writer 写入路径一字未动 |
| `Core/ControlPlane/MultiObserverShadowCoordinator.cs` | 已改 | 接线点 `ConfigureResourceProduction` → `ConfigureControlPlane`（同时注册 Resource 与 Collision 两条政策，世界尺寸只读一次）；`RunCollisionShadow`（提交事实→**确认离开才清理**（消费准入计划的缺席移除资格与暂缓集）→抄旧覆盖→分类→记录，零原生写入调用）；禁止差异有界心跳 + 恢复闭环；汇总日志新增 6 个 Collision 字段；会话边界清影子基线 |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDomainModules.cs` | 已改 | 唯一调用点改名 |
| `Build/Version.props` | 已改 | 新增 `SteamP2PFriendsCandidateRole`（默认 `ReadOnlyShadow`），默认 Case-ID 追加角色后缀 |
| `Properties/AssemblyInfo.cs` | 已改 | 候选角色进程序集元数据（角色成为产物身份的一部分） |
| `Core/Build/BuildFingerprint.cs` | 已改 | 快照新增 `CandidateRole`（缺失即身份不完整，fail-closed），自报告行输出 `candidateRole` |
| `SteamP2PFriends.csproj` | 已改 | 登记 4 个新增编译项；`BuildMetadata` 新增 `CandidateRole` |
| `Tools/Verify-BuildFingerprintArtifact.ps1` | 已改 | 解析并校验产物内嵌候选角色，输出 `CandidateRole` |
| `Tools/Verify-Ticket09Documentation.ps1` | 已改 | Case-ID 解析补上候选角色占位符（文档门禁与产物身份同源） |
| `WhitelistTests/Evidence/PureMemory/Adapters/Collision/CollisionShadowComparatorTests.cs` | 新增 | CSC01–CSC17 |
| `WhitelistTests/Evidence/StaticIL/CollisionShadowStaticILContractTests.cs` | 新增契约 | 政策自声明、资格沿用观察者事实、影子路径纯内存且不扫名册、旧覆盖快照只读、影子零原生写入、旧 Writer 仍是唯一生产写入者、比较器唯一只读消费点（含聚合门共 8 项） |
| `WhitelistTests/Evidence/BuildArtifact/BuildArtifactEvidenceTests.cs` | 已改 | 切片身份门更新为带角色的默认 Case-ID；新增候选角色可区分门 |
| `WhitelistTests/Program.cs` | 已改 | 注册 26 项新证据；唯一入口目标数 360 → 386 |
| `docs/architecture/build-fingerprint-artifact-evidence.md` | 已改 | 候选角色字段、Case-ID 新结构、`candidateRole` 自报告字段 |

### 证据类状态

| Evidence Class | 状态 | 说明 |
|---|---|---|
| PureMemory | PASS | 新增 CSC01–CSC17；票 01 表征门、票 02 投影契约、票 03 编排契约、票 04 准入契约全部保持绿 |
| StaticIL | PASS | 新增 8 项结构契约（含聚合门）；既有 Demand/Lifecycle/Resource/Readiness 契约保持绿 |
| BuildArtifact | PASS | `Verify-BuildFingerprintArtifact.ps1` PASS（影子候选）；两次 Release Rebuild 指纹逐项一致；候选角色可区分（双角色实测 SHA-256 / MVID `DIFFER`） |
| Runtime | PENDING | 只读 1H2G 影子运行待人工执行；正式切换候选与三端验收归票 09 |

### 影子候选与正式切换候选的构建指纹

- 默认 `SteamP2PFriendsCandidateRole = ReadOnlyShadow`，默认 Case-ID 为 `SPF-0.2.4.9-Experimental-CollisionSlice-ReadOnlyShadow`；`-p:SteamP2PFriendsCandidateRole=Cutover` 构建得到另一套产物身份（实测 SHA-256 与 MVID 逐项不同），角色同时进程序集元数据与运行日志；
- 两类候选的验收日志不得混用；影子 DLL 日志不构成正式切换 Runtime PASS（ADR 0012 后果）。

### 不可变语义（票 05 起生效，后续票不得回退）

- Collision 的半径来源是原版物件区域常量，**不得升格为跨域共享默认半径**；Collision 政策只产出 Collision 需求，不得应用于 `ResourceSpawnpoint`（StaticIL + CSC12 锁定）；
- 影子对照**只读**：不写原生状态、不驱动旧 Writer、不改 Resource 接缝、只把旧覆盖当对照物而不是金标（StaticIL 三项 + 扰动 P8/P11 锁定）；
- 差异必须可分类：预期差异 4 类、禁止差异 7 类，分类不出来即失败闭合为禁止差异（CSC 行为面 + 扰动 P1/P13 锁定）；暂缓者保留的需求必须落进对照帧的新侧，并以「最后已知事实」合成暂缓认领来解释它（CSC16/CSC17 锁定；缺失任一项都会把保留的 Deferred Demand 误报成「新侧缺区」或「凭空需求」）；旧侧认领者必须区分「在场且合格 / 在场但不合格 / 本拍样本不可用（暂缓）/ 已不在事实里」四态与「无认领者」，不得合并——暂缓不等于离开（CSC15 + 扰动 P12 锁定）；
- 影子路径「确认离开才清理」：消费准入计划的缺席移除资格与暂缓集合，暂缓者的既有贡献保留（扰动 P14/P15 锁定）；
- 「含 Host」由门禁把守：影子路径不得按本地玩家过滤（结构契约断言 `IsLocalPlayer` 只被读一次且用于 Host 归因；扰动 P10 证明其有咬合）。

### 移交票 06/07/08/09（本批不做，具名）

- Collision 仍**没有** Domain Execution Port、Lifecycle Policy 与 Acquisition Receipt，也**未**进入 Lifecycle Orchestration Engine 的领域注册（`Lifecycle Single Domain Registration` 契约仍绿）——这些归票 06；
- 影子比较器、旧覆盖快照与协调器影子路径必须在票 08 随旧 Writer 退役一并删除；`Collision Shadow Single Consumer` 契约会在影子仍被调用时变红，作为退役完成度的机械信号；
- 默认候选角色在票 08 翻转为正式切换候选；票 09 三端验收必须使用该候选且三端同一 SHA-256；
- 影子运行若报告禁止差异，按票面回写票 04，不在票 05 强行关单。
