# 0006: 行为保持不变的结构基线与 Resource 迁移样板

状态：Accepted（已确认）

在 0.2.4-Experimental 中，先暂停功能扩展，建立行为保持不变的 Structure Baseline：统一仓库与插件目录、namespace、领域身份、版本元数据、PatchRegistry 分层，以及测试/构建证据门禁。最终生产路径以 Production Control Seam 为唯一调度接缝；MultiObserverShadowCoordinator、旧版直接调用和私有轮询只作为迁移期间的受控兼容对象，不能继续作为并行权威写入者。Resource 被选为首个 Migration Slice，因为它是 0.2.3 中最早修复的世界同步领域，也能同时验证 RegionKey、Region Lease、碰撞、采伐和状态复制的接缝。所有迁移必须保持行为等价，逐域证明旧 writer 已退出后再进入下一个领域；版本、标签和稳定发布边界保持不变。

## Considered Options

- 继续直接修复单个功能：暂不采用，因为会扩大目标架构与实际生产架构的分叉。
- 一次性重写全部领域：暂不采用，因为无法保持变更局部性，也难以定位行为回归。
- 先迁移 Zombie：暂不采用，Resource 能更早暴露统一 RegionKey 与旧私有轮询的结构冲突。

## Consequences

- 首阶段交付物是结构基线和可追踪证据，不是新功能。
- 测试 PASS 必须区分纯内存、静态 IL、构建产物和运行时证据；不能用 ledger 测试替代生产因果验证。
- 每个 Migration Slice 只能保留一个生产权威 writer；旧路径的退出要有明确清单和验证记录。

## Structure Baseline Shape

仓库采用“按职责分层、领域内部聚合”的目标形状：`Core/ControlPlane`、`Core/Identity`、`Core/Registration`、`Adapters/<Domain>`、`Transport/<Route>` 和按 Evidence Class 划分的测试入口。根目录 `Patches/` 只保留尚未归属到具体领域的跨领域补丁，并逐步清空。

`Patch Registration Orchestrator` 保留一个顶层编排接缝，但具体注册逻辑分别归属 Domain、Transport、Security、Diagnostics 和 Registration Verification 模块。编排模块不拥有领域行为。

## Verification Gate

每个 Migration Slice 必须分别报告 `PureMemory`、`StaticIL`、`BuildArtifact` 和 `Runtime` 四类 Evidence Class。四类证据不可互相替代；结构迁移阶段可以暂缺 Runtime，但必须明确标记为未完成的运行时门禁。

## Metadata and Authority

领域身份、版本元数据和 Region identity 采用单一来源；日志、README、测试和审计输出不得各自维护版本真相。迁移期间旧路径和新路径不得同时成为同一状态的 Authority Writer。

## Structural Change Contract

结构基线阶段只能进行 Behavior-Preserving Structural Change：Harmony 目标、优先级、执行顺序、P2P 频道、SteamID、存档、网络协议、配置键、插件 GUID、状态机和版本标签保持不变。补丁按 Domain Ownership 归属迁移；无法证明归属的文件暂不移动。Resource 的目录整理与 Production Control Seam 接线是两个独立阶段。

## Migration Batches

迁移按以下可审计批次推进：结构基线与 Metadata Source；Patch Registration Orchestrator 拆分；目录和 namespace 归属；Evidence Class 测试/构建门禁；Resource 生产接缝迁移；旧 Authority Writer 退出与 Runtime 验证。每批次都应能独立构建、审查和回滚。

## Pipeline Shape

Domain Adapter Pipeline 由插件实例拥有的注册表组装；Patch Registration Orchestrator 只编排，不持有领域行为。`ILifecycleDomainAdapter` 与 `IStateReplicationAdapter` 作为独立角色登记，同一领域可以由同一实现同时承担两个角色，但不得依赖隐式绑定。完成 Registration Closure 后，本次插件会话不再动态添加适配器。

跨接缝的领域身份统一为不可变 Domain Id，显示名称另行管理。二维世界区域使用类型化 Region Key；Zombie 导航区域使用独立 Bound Key。所有编码和解码集中在 identity module，不允许各适配器自行打包裸整数。

测试物理结构迁移到按 Evidence Class 组织的 `Tests/`；主项目和测试项目共同导入 `Build/Version.props` 作为 Metadata Source。结构阶段必须通过 Behavior-Preserving Structural Change 的 Evidence Gate，之后才允许 Resource 进入行为迁移。

## Version and SDK Baseline

本轮新构建使用 `0.2.4.8` 作为版本值；此前已经归档的版本保持冻结，不回写、不重新解释。`0.2.4-Experimental` 的当前标签和发布边界保持不变。

注册顺序不以本 ADR 中的抽象阶段列表为运行时真相，而以 `D:\Agent-工作目录\U3-SDK` 的实际调用链为准。本次结构工作必须记录该 SDK 的固定 commit（当前只读核对得到 `ea7b4973af5ba10f62baad2bfde36ab2e5b060eb`）及其对应的注册/生命周期位置，再映射到插件的 Registration Orchestrator。

## Branching

结构整理使用 `codex/structure-baseline-0.2.4` 分支，不直接修改 `master`，不创建或修改发布标签；用户提供的未跟踪复核报告继续保留，不纳入结构提交。

## Evidence and Test Shape

插件运行时输出 Build Fingerprint，至少包含版本值、AssemblyVersion/FileVersion、MVID、已加载 DLL 的 SHA-256、插件 GUID 和共享 Case-ID。该输出是自报告；验收时对提交的 DLL 进行 Independent Artifact Verification，日志和 DLL 必须能通过同一 Case-ID 关联。用户不需要手工计算 hash，但最终报告不得把自报告等同于独立验证。

测试第一阶段保持一个测试项目和一个测试可执行入口，物理目录按 Evidence Class 组织为 `Tests/PureMemory`、`Tests/StaticIL`、`Tests/BuildArtifact` 和 `Tests/Runtime`。结构阶段可以暂缺 Runtime，但必须在 Migration Manifest 和审计报告中明确标记。

跨领域补丁的目标 namespace 为 `SteamP2PFriends.Core.Patches`；领域补丁为 `SteamP2PFriends.Adapters.<Domain>.Patches`；Transport 和 Security 使用各自职责 namespace。每个批次维护 Migration Manifest，记录文件移动、namespace、patch target/owner/priority/order、测试入口和未决项。

## Batch 1 Confirmation

第一批只建立 Structure Baseline 和 Metadata Source：版本值为 `0.2.4.8`，旧归档版本冻结；固定 U3-SDK 对照 commit；主项目和测试项目共同导入 `Build/Version.props`；建立 `docs/architecture/` 下的结构基线、Registration Trace 和 Migration Manifest。第一批不移动源码、不统一 namespace、不接入 Resource 生产控制接缝，也不修复功能问题。注册顺序以 U3-SDK 的真实调用链追踪结果为准。

Build Fingerprint 归入后续 Evidence Class 构建门禁批次；第一批只固定其数据来源和追踪位置。
