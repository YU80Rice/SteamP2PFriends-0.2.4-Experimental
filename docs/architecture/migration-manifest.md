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
