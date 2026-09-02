# 需求规格说明书：0.2.4.8 仓库与插件整体结构整理

## Problem Statement

当前 0.2.4-Experimental 已经积累了 Multi-Observer、领域适配器、手工补丁和测试资产，但“设计上的目标架构”与“实际生产调用链”仍未完全重合。生产注册仍集中在大型 Patch Registry 中，部分领域适配器和 Spatial Observer Index 只有静态或纯内存信号，尚未成为统一的生产调度路径；U3-SDK 注册顺序的追踪也尚未覆盖所有手工注册点。

这使维护者和后续开发者难以回答几个关键问题：一个补丁由哪个领域负责、它在原生生命周期的哪个位置生效、哪个模块拥有状态写入权、一个测试到底证明了纯内存逻辑还是生产因果、当前 DLL 是否与测试日志对应。结果是功能修复容易继续扩大旧路径与目标架构之间的分叉，结构重构也可能在没有明确证据的情况下改变 Harmony 顺序、协议、配置或运行时行为。

用户需要一个可持续维护的仓库和插件结构：能够定位责任边界，能够用最少的高层测试接缝验证关键行为，能够把构建产物、日志、自报告和独立校验关联起来，并能够逐域迁移而不产生并行 Authority Writer。

## Solution

以 `0.2.4.8` 为新的实验构建版本，在 `codex/structure-baseline-0.2.4` 分支上建立行为保持不变的 Structure Baseline。基线阶段只整理职责、身份、元数据、注册追踪、测试证据和迁移记录，不改变已有生产行为。

随后按可回滚、可审计的批次推进：先补全 U3-SDK 原生注册追踪并拆分 Patch Registration Orchestrator，再按 Domain Ownership 迁移目录和 namespace，建立四类 Evidence Class 测试与 Build Fingerprint 门禁，最后以 Resource 作为首个 Migration Slice 接入唯一的 Production Control Seam。每一批都必须独立构建、审查并记录；只有在旧 Authority Writer 已被明确退出后，才能完成该领域迁移并进入下一个领域。

## User Stories

1. 作为项目维护者，我希望仓库中的每个核心模块都有唯一职责归属，以便能够快速判断修改影响范围。
2. 作为后续开发者，我希望控制面、数据面、平台层和注册编排层具有稳定边界，以便新增功能不会继续堆积到插件入口或大型 Registry 中。
3. 作为领域适配器开发者，我希望每个领域拥有清晰的生命周期适配和状态复制适配入口，以便独立实现 Resource、Zombie、Item、Animal、Structure、Collision、Vehicle 和 Security 行为。
4. 作为维护者，我希望 Patch Registration Orchestrator 只负责编排，不拥有领域业务逻辑，以便注册顺序变化不会迫使我修改领域行为。
5. 作为审计员，我希望每个手工注册点都能映射到 U3-SDK 的原生事件、回调或调用链位置，以便注册顺序有事实依据而不是经验推断。
6. 作为审计员，我希望能看到 Harmony target、owner、priority 和执行顺序的结构迁移前后快照，以便证明整理没有改变补丁语义。
7. 作为插件开发者，我希望生命周期适配器和状态复制适配器分别登记，以便同一领域是否承担两种角色是显式决定而不是隐式绑定。
8. 作为控制面维护者，我希望 Registration Closure 之后注册表不可变，以便运行时不会出现晚注册、重复注册或领域顺序漂移。
9. 作为控制面维护者，我希望领域身份使用不可变 Domain Id，显示名称独立管理，以便本地化或 UI 调整不会改变机器协议身份。
10. 作为空间同步维护者，我希望二维世界区域统一使用类型化 Region Key，以便所有领域遵守同一套坐标与编码规则。
11. 作为 Zombie 领域维护者，我希望导航 Bound Key 与二维 Region Key 保持独立，以便不会把两种不同拓扑的区域身份混用。
12. 作为测试开发者，我希望测试按 PureMemory、StaticIL、BuildArtifact 和 Runtime 分类，以便测试结论不会被错误地解释为更高等级的证据。
13. 作为测试开发者，我希望优先在控制面到适配器管道的高层接缝测试外部行为，以便减少对私有实现和文件布局的耦合。
14. 作为发布维护者，我希望版本元数据由单一构建来源传播到程序集、日志、测试和审计输出，以便不会出现版本字符串漂移。
15. 作为测试人员，我希望运行时日志打印 Build Fingerprint，包括版本、MVID、DLL SHA-256、插件 GUID 和 Case-ID，以便用户无需手工计算也能提供独立校验所需的线索。
16. 作为验收审计员，我希望自报告 Fingerprint 与验收侧重新计算的 DLL SHA-256 被明确区分，以便日志不会被误当成独立证据。
17. 作为 Resource 领域维护者，我希望 Resource 成为首个 Production Control Seam 迁移样板，以便用一个边界清晰的领域验证 Region Lease、空间需求、碰撞、采伐和状态复制的完整链路。
18. 作为房主玩家，我希望结构整理不改变当前 P2P、存档、配置和游戏内行为，以便在迁移过程中仍可使用已验证的旧生产路径。
19. 作为客机玩家，我希望 Resource 迁移后在远离房主的区域仍能获得一致的资源碰撞、采伐和状态结果，以便结构迁移不会牺牲联机体验。
20. 作为多观察者测试人员，我希望 Host、Guest 和多个观察者的进入、离开、重连 generation 与滞回释放都可被验证，以便发现区域租约泄漏和重复写入。
21. 作为发布负责人，我希望每个迁移批次都有 Migration Manifest，记录变更、未决项、证据类别和旧 writer 退出状态，以便可以暂停、回滚或交接。
22. 作为协作者，我希望旧归档版本和当前实验标签保持冻结，以便历史构建仍可复现，实验结构整理不会污染历史记录。
23. 作为自动化代理，我希望规格明确列出阻塞关系、验收条件和禁止事项，以便能够逐 ticket 执行而不重新猜测架构意图。
24. 作为审查者，我希望源码、构建、静态 IL 和运行时证据分别呈现，以便能够准确判断哪些结论已经确认、哪些仍需真实环境验证。
25. 作为项目负责人，我希望结构基线通过后再开始功能迁移，以便功能修复能够落在稳定的模块边界上，而不是进一步扩大结构债务。

## Implementation Decisions

### 1. 目标与不变量

- 本规格覆盖“仓库与插件整体结构整理”以及结构基线通过后的 Resource 首个迁移准备和行为迁移门禁。
- 结构整理必须是 Behavior-Preserving Structural Change。
- Harmony target、owner、priority、执行顺序、P2P 频道、SteamID、存档格式、配置键、插件 GUID、状态机语义和当前发布标签不得改变。
- 新构建版本固定为 `0.2.4.8`；历史归档版本冻结，不回写、不重新解释。
- 工作在 `codex/structure-baseline-0.2.4` 分支完成，不直接修改 `master`，不创建或修改发布标签。
- 用户提供的第三方复核报告保留为输入材料，不被结构整理覆盖或改写。

### 2. 模块与职责边界

- Core 负责纯内存 Control Plane、Identity、Lifecycle、Registration 和未归属的跨领域补丁。
- Multi-Observer Core 负责 World Presence Observer、Spatial Observer Index、Session Epoch、Connection Generation、Region Lease 和 Hysteresis Release。
- Adapters 按 Domain Ownership 聚合领域行为；每个领域就近拥有其生命周期、状态复制和领域补丁。
- Platform 负责 Steam P2P、直连路由、UI、诊断和外部运行时适配，不向 Control Plane 注入游戏引擎细节。
- Transport、Security、Diagnostics 和 Registration Verification 作为独立职责登记，不被塞入领域适配器或顶层编排器。
- 无法由源码和调用链证明唯一归属的文件暂不移动，必须登记为未决项。

### 3. Patch Registration Orchestrator

- 顶层编排器只负责建立注册顺序、调用各职责模块的注册入口、执行验证并宣布 Registration Closure。
- 编排器不得拥有领域状态、领域轮询、领域补丁实现或领域业务判断。
- 生命周期适配器和状态复制适配器必须分别登记；同一实现承担两种角色时也必须留下两个显式注册结果。
- Registration Closure 后注册表不可变；重复注册、未知 Domain Id、缺少角色实现或顺序冲突必须在关闭前失败并产生可审计诊断。
- 具体顺序必须来自 U3-SDK 固定 commit `ea7b4973af5ba10f62baad2bfde36ab2e5b060eb` 的实际调用链，而不是来自抽象里程碑顺序。

### 4. Registration Trace

- 为每个手工 Patch Registration 和关键 Harmony 注册点建立“插件注册模块 → U3-SDK 原生锚点 → 依赖条件 → 生产行为”的映射。
- 记录原生 Start、地图加载、连接建立、玩家初始化、服务器连接回调等事件的真实前后关系。
- 对 Harmony PatchAll 与手工注册分别记录 owner、priority、target 和相对顺序；不能以 `onServerConnected` 单独推断连接已完成隔离。
- 待追踪位置保持 Pending，不以经验补齐；Pending 项阻塞相应的结构迁移批次。

### 5. Identity 与命名

- 跨模块身份统一使用不可变 Domain Id；显示名称不得参与协议、注册或持久化判断。
- 二维区域统一使用 Region Key，坐标编码、解码和边界规则集中在 Identity 模块。
- Zombie 导航拓扑使用独立 Bound Key，不得复用 Region Key 或裸整数别名。
- 生命周期、复制、会话、连接、区域和实体 generation 必须保持正交，禁止用一个版本号承担多个生命周期含义。
- 新增类型、成员和测试名称必须遵循现有领域词汇，避免使用 PatchPool、GlobalTicker、Client、Visitor 等已被否定的模糊名称。

### 6. Metadata Source 与 Build Fingerprint

- 统一的构建版本元数据来源作为主项目与测试项目的共同输入；两者必须消费同一来源。
- 版本迁移必须继续检查 AssemblyVersion、FileVersion、插件特性、启动日志、文档检查和审计输出等消费者，不能仅因构建文件已统一就宣称单一真相全部落地。
- 后续 Evidence Class 构建门禁必须让运行时输出 Build Fingerprint，至少包含版本值、AssemblyVersion/FileVersion、MVID、已加载 DLL 的 SHA-256、插件 GUID 和共享 Case-ID。
- Fingerprint 日志属于自报告；验收必须对交付 DLL 重新计算 SHA-256，并用同一 Case-ID 与 Host/Client 日志关联。
- 用户不需要手工计算 hash，但交付报告必须明确区分自报告与 Independent Artifact Verification。

### 7. 测试物理结构与证据分类

- 保持一个测试项目和一个测试入口，先按 Evidence Class 建立 `PureMemory`、`StaticIL`、`BuildArtifact` 和 `Runtime` 的物理分类。
- 现有测试按 Domain Ownership 和 Evidence Class 迁移；迁移过程中不得通过复制同一测试制造虚假的覆盖率或 PASS 数量。
- PureMemory 只证明控制面和纯抽象的外部行为；StaticIL 证明目标、owner、priority、顺序和关键调用形状；BuildArtifact 证明版本、产物、引用和 Fingerprint 可追踪；Runtime 证明真实 Host/Guest 因果。
- 结构阶段可以暂缺 Runtime，但必须显式登记为未完成，不能用构建或 ledger 测试替代。

### 8. Migration Manifest 与 Authority Writer

- 每一批迁移维护独立 Migration Manifest，记录文件/模块归属、namespace、注册追踪、保留的 patch 元数据、测试入口、Evidence Class、旧 writer 状态和未决项。
- 同一领域在一个 Migration Slice 中只能有一个生产 Authority Writer。
- 旧路径可以作为受控兼容对象保留，但不得与新 Production Control Seam 并行写入同一状态。
- 每批必须可独立构建、审查和回滚；未完成的批次不能被描述为已完成的功能修复。

### 9. Resource 首个 Migration Slice

- Resource 是结构基线之后的第一个行为迁移样板，因为它能同时验证 Region Key、Region Lease、Spatial Observer Index、碰撞、采伐和状态复制。
- Resource 迁移必须首先定义唯一 Production Control Seam，再逐步接入生命周期适配和状态复制适配。
- 迁移必须验证房主、授权客机、多观察者、观察者离开、2 秒 Hysteresis Release、重新进入、Connection Generation 和 Region Generation。
- 采伐、资源状态变化和重建必须使用明确的 Region/Entity generation，拒绝过期快照、重复复制和跨会话状态污染。
- Resource 行为迁移与目录/namespace 整理是两个可独立验收的阶段；先完成结构证据，再开启生产接线。

### 10. 工程闭环与交付

- 每个代码或项目变更后必须执行 Release 构建、现有测试入口、`git diff --check` 以及独立审核。
- 独立审核至少覆盖规格符合性、线程安全、状态一致性、环境适配、注册顺序和 Evidence Class 诚实性。
- 审核未通过时，修复后必须重新构建、重新测试和重新审核；不能沿用旧报告或旧 DLL 的 PASS。
- 详细报告归档到当前项目的 `audit/YYYY-MM-DD/`，并记录版本、分支、变更、命令、警告、测试结果、hash、Case-ID 和运行时证据状态。

## Testing Decisions

### 1. 已确认的测试接缝

- **注册接缝**：`Patch Registration Orchestrator → Registration Closure`。验证 U3-SDK 注册顺序、Patch owner/priority/order、模块边界、重复注册失败和关闭后的不可变性。
- **生产行为接缝**：`World Presence Observer → SpatialObserverIndex → Domain Adapter Pipeline`。这是 Resource 的核心接缝，验证观察者集合、区域需求并集、Region Lease、Hysteresis Release、generation 和适配器事件。
- **构建证据接缝**：版本元数据 → Build Fingerprint → 运行日志/交付 DLL → Independent Artifact Verification。验证版本、MVID、插件 GUID、SHA-256 和共享 Case-ID 的一致关联。

### 2. 测试原则

- 测试外部可观察行为和契约，不测试私有字段、当前文件位置或实现细节。
- 优先使用现有纯内存 Fake、空间索引测试、领域适配器测试、静态兼容性审计和当前 Release 构建入口。
- 高层接缝覆盖多个下游消费者时，禁止为每个适配器重新实现一套独立的空间计算或租约测试。
- 每项结论必须标注 Evidence Class；PureMemory PASS 不得升级为 Runtime PASS。
- 测试失败应能区分逻辑回归、静态注册变化、产物不一致和环境/运行时缺证。

### 3. 必须覆盖的行为

- 注册顺序与 Registration Closure：正常注册、重复注册、未知身份、角色缺失、顺序冲突和关闭后变更。
- 多观察者：Host、Guest、多 Guest 同区、不同区、进入/离开、断线、重连 generation、会话重置。
- 租约：0→1 Acquire、1→N 合并、N→0 延迟释放、滞回期间重新进入、过期 ticket 和跨会话 ticket 拒绝。
- Resource：远端区域碰撞、采伐状态变化、区域重建、快照/增量复制和双端 Case-ID 关联。
- 构建证据：0.2.4.8 元数据传播、MVID、DLL hash、插件 GUID、日志 Fingerprint 与独立重算结果。
- 结构不变量：Harmony target/owner/priority/order、P2P 频道、SteamID、存档、配置键和标签保持不变。

### 4. Runtime 门禁

- 结构基线阶段可将 Runtime 标记为 Pending，但必须在审计和 Migration Manifest 中明确说明。
- Resource 行为迁移完成前，必须取得共享 Case-ID 的 Host/Guest 运行日志，并绑定到当次 DLL 的独立 hash。
- 至少覆盖 Host、单 Guest、多观察者、Guest 重连、观察者离开后重新进入以及采伐/碰撞/复制场景。
- 任一 DLL 或核心生产源码发生变化后，必须重新取得对应的运行时证据；不得沿用旧构建的 Runtime PASS。

## Out of Scope

- 本规格不在结构基线阶段修复 Zombie、Item、Route B、连接路由、载具或其他独立功能问题。
- 不在未完成结构 Evidence Gate 前直接把 Resource 宣称为已迁移或已修复。
- 不启动或部署外部 U3DS 独立服务端，不改变单人存档格式，不全局伪造 Dedicated Server 标记。
- 不改变 P2P 协议、网络频道、SteamID 语义、配置键、插件 GUID、Harmony 行为或当前发布标签。
- 不修改历史归档版本、历史审计报告或用户提供的第三方复核原文。
- 不通过新增并行 writer、全局 try-catch、静默降级、硬编码顺序或复制旧实现来规避结构问题。
- 不在本规格内引入未经批准的第三方库或重写所有领域。
- 不把自报告日志、纯内存测试、静态 IL 结果或构建成功等同于真实多机运行时验收。

## Further Notes

- 当前已完成的第一批只建立 Structure Baseline 和 Metadata Source；源码物理迁移、namespace 统一、Resource 生产接线和 Build Fingerprint 自动输出仍属于后续批次。
- 推荐执行顺序为：注册追踪补全 → Patch Registration Orchestrator 拆分 → Domain Ownership 目录/namespace 迁移 → Evidence Class 与 Build Fingerprint 门禁 → Resource Production Control Seam → 旧 Authority Writer 退出 → Host/Guest Runtime 验证。
- U3-SDK 注册顺序必须以固定 commit 的实际源码调用链为准；本规格中的模块顺序不是原生运行时顺序的替代品。
- 结构整理期间，未能证明归属的补丁留在受控兼容位置并登记 Pending；不得为了目录整洁提前移动。
- Resource 迁移是样板，不代表其他领域已自动获得生产接线；后续领域必须重复自己的 Domain Ownership、Registration Trace、Authority Writer 和 Evidence Gate。
- 任何批次都必须在交付报告中分别列出 PureMemory、StaticIL、BuildArtifact 和 Runtime 证据，并说明缺失证据对发布结论的限制。
