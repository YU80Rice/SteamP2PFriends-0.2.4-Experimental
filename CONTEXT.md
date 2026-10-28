# SteamP2PFriends & Unturned Multiplayer Context

Unturned P2P listen-host multiplayer ecosystem that provides server-authoritative co-op without dedicated servers (U3DS), powered by a Minecraft LAN-style chunk lease architecture.

## Architecture & Lifecycle

**Control Plane**:
The pure in-memory coordination engine (zero Unturned/Unity dependency) managing observers, leases, epochs, generational tokens, and spatial indexing.
_Avoid_: Core patch, background manager

**Data Plane**:
The execution layer consisting of domain adapters and transport channels that perform IL hook interception, RPC transmission, and game state mutation.
_Avoid_: Worker thread, hook manager

**Multi-Observer Core**:
The central engine that tracks player presence, computes region demand unions, and manages activation leases.
_Avoid_: Patch pool, global ticker

**Spatial Observer Index**:
The Control Plane store of canonical World Presence Observer spatial facts used by the Demand Projection Engine; it is not a shared business demand mask and is not owned per domain.
_Avoid_: Per-adapter distance check, polling grid, shared coverage radius, per-domain observer registry

**Domain Adapter Pipeline**:
A declarative, pluggable registry of `ILifecycleDomainAdapter` and `IStateReplicationAdapter` implementations driven by the Control Plane.
_Avoid_: Hardcoded switch-case, patch dispatcher

**Production Control Seam**:
The production attachment of a domain to the Lifecycle Orchestration Engine; a domain may keep a thin facade, but must not own a second generic lifecycle state machine.
_Avoid_: Shadow path, direct adapter call, copied ResourceProductionControlSeam, coordinator domain switch

**Observer Spatial Authority**:
The Control Plane role that is the only source of World Presence Observer positions, region enter/exit diffs, and the factual basis for demand counts.
_Avoid_: Demand Authority, coverage set, RemoteCoverage, per-adapter observer scan

**Domain Demand Authority**:
The domain role that owns that domain's typed Region Demand / Region Lease produced by the Demand Projection Engine from a Demand Policy; one domain's demand never grants another domain's execution.
_Avoid_: Demand Authority, implicit RegionKey activation, shared coverage, per-domain observer scan

**Execution Authority**:
The Domain Execution Port role that performs plugin-side native activate, release, and recovery for that domain only after the Lifecycle Orchestration Engine issues Acquire or Release; it supplements or overrides native activation and does not replace Native State Authority.
_Avoid_: coverage owner, collision owner (when meaning demand), sole native writer, hysteresis owner, demand counter

**Native State Authority**:
Unturned native managers that hold entity state, saves, and native network protocol; a Migration Slice does not rebuild a second entity warehouse.
_Avoid_: plugin world state, copied entity ledger

**Native World Synchronization（原生世界同步）**:
通过 Unturned 已有权威仓库和游戏网络协议同步世界状态；SPF 修复听主机观察者与执行资格时接续该链，不自动成为上层功能模组的自定义频道同步。
_Avoid_: 强制以 LMN 重写全部原生 RPC、将协议存在视为 Runtime 验收

**Gameplay Mod（功能性联机模组）**:
在游戏和联机基础设施之上提供额外玩法功能、同步自身功能状态的模组；与 SPF 的原生世界联机基础设施职责区分。
_Avoid_: 将 SPF 原生世界同步迁移自动视作功能模组频道接入

**Demand Policy**:
A domain-declared description of observer eligibility, spatial shape, radius or distance source, and DomainId; it is not an executable projector and does not scan clients or store observer positions.
_Avoid_: shared radius, universal OBJECT_REGIONS, coverage radius (when meaning policy), per-domain projector

**Demand Projection Engine**:
The shared Control Plane executor that applies Demand Policies to canonical observer facts, performing region enumeration, world-bound clipping, demand counting, and Enter/Exit diffs, and emitting typed Domain Demand as DomainId plus RegionKey.
_Avoid_: per-domain SpatialObserverIndex, shared demand mask, RemoteCoverage rebuild

**Domain Demand Projection State**:
Per-domain demand counts and Enter/Exit results produced by the Demand Projection Engine; it may be isolated per DomainId but must consume the same Observer Spatial Authority.
_Avoid_: second observer registry, independent spatial index, private coverage set

**Lifecycle Orchestration Engine**:
The shared Control Plane executor that turns typed Domain Demand into Acquire and Release, owns hysteresis, identity checks, retry, compensation scheduling, and local fault isolation, and consumes Session Epoch, Connection Generation, and Region Generation without recreating them.
_Avoid_: per-domain seam state machine, coordinator domain switch, interface-only contract

**Domain Execution Port**:
The narrow domain adapter that declares Lifecycle Policy and performs native operations, returning success, retryable failure, rejection, or a compensatable result; it does not scan observers, recompute demand, or hold orchestration state.
_Avoid_: full-region snapshot requirement, second lifecycle writer, observer scanner

**Lifecycle Policy**:
A domain-declared description of hysteresis, retry limits, and safe-failure behaviour consumed by the Lifecycle Orchestration Engine.
_Avoid_: hardcoded Resource constants as shared defaults, per-domain retry dictionary

**Read-Only Shadow Computation**:
A migration-period, side-effect-free comparison of new typed Domain Demand against a legacy path while that legacy path remains the only Authority Writer; it classifies differences and must not enable, disable, submit native state, or become a fallback query source.
_Avoid_: dual writer, parallel writer, runtime fallback to old writer, equality-to-legacy gold standard, similarity threshold

**Session-Boundary Cutover**:
The rule that a deployable build chooses exactly one Collision Authority Writer at plugin start or new Session Epoch, never mid-session, never by region, player, or percentage, and never by flipping back to the legacy writer in the same process.
_Avoid_: live traffic split, hot switch, per-region cutover, in-session rollback to RemoteCoverage

**Control-Plane Readiness Gate**:
The invariants that a Session-Boundary Cutover candidate must already satisfy; defects that violate them block official Collision wiring, while side-effect-free shadow runs may proceed earlier and cannot prove the gate.
_Avoid_: inherit Resource path, coordinator rewrite, shadow DLL as cutover evidence, skip-bad-record-as-zero-demand

**Deferred Observer Demand**:
The holding state for an observer's previous region contributions when the current sample is unavailable; it is not confirmed exit and must not become destructive Release until leave, generation invalidation, Session Reset, or a bounded recovery policy says so.
_Avoid_: missing sample as zero demand, freeze-all-observers, silent skip

**Collision Override**:
A lease-scoped plugin-side covering of LevelObject, door-animation, collider, or culling state acquired for Collision; releasing it returns those dimensions to Native State Authority rather than forcing the region disabled.
_Avoid_: whole-region disable, inverse of enable, ResourceSpawnpoint write

**Acquisition Receipt**:
The identity-bound record created by a successful Acquire that lists the Collision Overrides actually taken and how they may be safely revoked; a new Acquire invalidates the previous receipt.
_Avoid_: acquire-implies-disable, region-wide snapshot as ownership, Host scan in Execution Port

**Structure Baseline**:
The agreed repository shape, naming, metadata, evidence, and collaboration rules that must be established before functional repairs are migrated.
_Avoid_: Feature freeze, temporary cleanup

**Migration Slice**:
A bounded domain move from the legacy production path to the Production Control Seam, with behavior preserved and evidence proving the old writer is no longer authoritative.
_Avoid_: Big-bang rewrite, parallel writer

**Resource Migration Sample**:
The first Migration Slice, using Resource as the reference domain for Region Lease, spatial demand, collision, harvest, and replication integration.
_Avoid_: Resource feature, M6 implementation

**Authority Writer**:
The one production path allowed to mutate a given domain state during a Migration Slice.
_Avoid_: primary service, active implementation

**Patch Registration Orchestrator**:
The top-level registration module that orders domain, transport, security, diagnostic, and verification registration without owning their details.
_Avoid_: patch pool, mega registry

**Evidence Class**:
The type of proof attached to a claim: PureMemory, StaticIL, BuildArtifact, or Runtime.
_Avoid_: test level, confidence score

**Behavior-Preserving Structural Change**:
A repository or plugin organization change that preserves patch targets, order, protocols, configuration, state-machine semantics, and release identity.
_Avoid_: safe refactor, cleanup refactor

**Domain Ownership**:
The rule that assigns a patch, adapter, test, or document to the one domain or responsibility whose behavior it serves.
_Avoid_: file grouping, folder preference

**Module Ownership Catalog**:
The structure-only catalog that records each module's physical authority root, authority type,
and Registration Trace coverage; it does not register patches or own runtime state.
_Avoid_: runtime registry, duplicate module index

**Platform Boundary**:
The physical boundary for client, host, transport, UI, and diagnostics integrations with
Unturned, Steamworks, Unity, or other external runtime services.
_Avoid_: Core business logic, second transport entry

**Controlled Cross-Domain Patch Set**:
The single `Core/Patches` location for patches whose call chain cannot prove one domain owner;
each retained patch requires a documented reason and is registered only by the Patch Registration Orchestrator.
_Avoid_: catch-all folder, copied compatibility patch

**Metadata Source**:
The single build-owned source from which version identity is propagated to assembly metadata, logs, documentation checks, and audit output.
_Avoid_: version string, release label

**Domain Id**:
The immutable machine identity of a domain, distinct from its display name and stable across localization or presentation changes.
_Avoid_: DomainName, DomainKey, display name

**Region Key**:
The canonical typed identity of a two-dimensional world region, including its coordinates and encoding rules.
_Avoid_: raw region int, packed coordinate

**Bound Key**:
The canonical identity of a navigation bound used by the Zombie domain, kept distinct from a two-dimensional Region Key.
_Avoid_: zombie region key, byte region

**Evidence Gate**:
The acceptance rule that requires the relevant Evidence Class before a Migration Slice can advance.
_Avoid_: green build, PASS flag

**Registration Closure**:
The point after bootstrap at which the Domain Adapter Pipeline becomes immutable for the current plugin session.
_Avoid_: late registration, dynamic patch load

**SDK Registration Order**:
The ordering of plugin registration and lifecycle hooks derived from the traced U3-SDK runtime call chain, not from an abstract plugin-only sequence.
_Avoid_: startup guess, arbitrary initialization order

**Build Fingerprint**:
The runtime-visible identity evidence for the loaded plugin build, combining version metadata with independently reproducible assembly identity data.
_Avoid_: log version, build label

**Independent Artifact Verification**:
The acceptance-side recomputation of a supplied plugin artifact's identity, used to corroborate rather than merely trust a runtime self-report.
_Avoid_: user hash, log confirmation

**Registration Trace**:
The documented mapping from a plugin registration module to the U3-SDK event, callback, or native call-chain position it depends on.
_Avoid_: startup order list, patch order guess

**Migration Manifest**:
The per-batch record of moved files, namespace ownership, preserved patch metadata, retired writers, evidence classes, and unresolved items.
_Avoid_: change summary, file list

**Adapter Fault Supervisor**:
The universal circuit breaker that detects demand mismatches or runtime exceptions in specific adapters/regions, quarantining destructive actions without affecting other domains.
_Avoid_: Global try-catch, crash handler

**World Presence Observer**:
A connected player (Host, authorized Guest, or pending Guest) who physically occupies the world and projects region demand.
_Avoid_: Client, visitor, user

**Region Lease**:
A functional capability ticket granted to a domain adapter when at least one observer requires that region.
_Avoid_: Chunk lock, region claim

**Hysteresis Release**:
最后一个观察者的领域需求消失后延迟释放租约，期间重入可取消待释放，用于避免反复激活与释放；它本身不意味着删除原生世界业务数据。
_Avoid_: Immediate unload, instant GC

**Dormant Progress Reconciliation（无人进度补算）**:
在无人区域恢复需求或世界保存等已批准的结算边界，按原生配置速率、状态条件和关键事件顺序结算休眠期间应发生的进度变化；不要求复刻逐帧检查或卡顿造成的计时偏差。
_Avoid_: 无人即暂停、每帧补跑、仅凭离开时长推算所有事件

**Dormant Key Event Processing（休眠关键事件处理）**:
区域休眠期间对必要的原生事件进行有限的判定或记录，用于保留进度补算所需的事件结果与依赖顺序。
_Avoid_: 区域每帧保活、统一按时长计算全部事件

**Item Expiry（物品到期移除）**:
房间运行期间，实际掉落物达到对应配置的消失时间后从权威世界状态移除，其清理不依赖玩家重新进入区域；已核实原生来源的到期分类对齐U3DS，实际掉落用掉落期限，地图自然物品用自然期限。自然物品的计时启动与休眠结算遵循Item领域已批准的周期政策。
_Avoid_: 实际掉落物返区才删除、租约释放即删除、已启动周期离区暂停玩法时间

**Natural Item（自然物品）**:
地图物资点通过首次生成或周期刷新产生的物品，区别于玩家或生物等玩法动作产生的实际掉落物；生成来源不能仅凭原生 isDropped 标志判定。
_Avoid_: 所有 isDropped=false 的物品、所有地面物品

**Item Cycle Activation（物品周期启动）**:
同一会话内，首次 Item 领域需求启用某区域自然物品计时的事件；区别于准备 ItemData、观察者接收基线或返区恢复执行。
_Avoid_: 每次进区重新生成、每个玩家独立启动世界周期、重置物品寿命

**Item Preparation Readiness（Item 数据准备就绪）**:
当前地图全图 Item 数据已完成准备且合法空区可用的状态，是相应 Item 联机运行的前置；区别于房间已发布、首次周期启动或客户端全部模型显示。
_Avoid_: Host 活动即数据就绪、全图模型常驻、在地图输入未建立时提前生成

**Natural Item Reconciliation（自然物品结算）**:
返区时依据当前权威物品、到期时间和原生刷新资格结算自然物资当前结果，省略无人期间的中间随机刷新批次；每次按区域共用一次结算，首个有效需求启动，后来者等待同次结果。有效会话/区域中已启动结算在需求消失后继续收尾，已执行变化不撤销或重做，无等待者不发清单，完成即休眠；下次需求从收尾后的状态重新判断。新补物品从本次生成时计时，存活旧物品不重置年龄；清单与呈现/拾取消费完成后的状态，正常分帧结算属于正常进度。
_Avoid_: 进区无条件补货、逐批回放全部随机历史、保证补满

**Item Recovery（Item 恢复）**:
对物资结算或区域同步未就绪/失败进行有界恢复，按实际失败依赖局部处理；区别于正常进区自动加载或每次强制重同步。
_Avoid_: 每次进区等待、全房间停用、失败当正常空区

**Item Recovery Command（Item 恢复命令）**:
候选 Item 路径在有效 Host 会话中登记的精确 `/spfitemretry <x> <y>` 入口；它只消费
当前观察者、Core Connection Generation、Session Epoch、区域需求和耗尽资格，不替代普通聊天，
也不发起隐藏传输协议。可能半包的观察者必须先完整离开 `ITEM_REGIONS` 加载范围再返区。
_Avoid_: 管理员万能命令、普通聊天即控制协议、陈旧连接代次恢复、区内盲目重发

## State & Epoch Axes

**Session Epoch**:
The unique identifier of an entire listen-host world instance, incrementing only on map transitions or server restart.
_Avoid_: World version, game ID

**Connection Generation**:
A monotonic sequence allocated to a specific observer, incrementing only when that observer reconnects.
_Avoid_: Client token, player epoch

**Region Generation**:
A sequence tracking physical reconstruction of an authoritative world region or bound.
_Avoid_: Spawn counter, map version

**Entity Generation**:
A sequence tracking single-entity recreation or slot reuse.
_Avoid_: Object ID, entity version

## Admission & Security

**Route B Quarantine**:
A 30-second in-game soft-isolation state where unapproved visitors exist in the world but cannot move, attack, interact, or take damage.
_Avoid_: Pre-join queue, handshake block, hard whitelist

**Gameplay Authorization**:
The administrative permission granting an observer full gameplay rights, completely decoupled from world presence and rendering.
_Avoid_: World admission, connection permit

**Listen-Host Dedicated Gate**:
The listen-host eligibility that must match a dedicated-server early-return so world simulation (respawn, despawn, periodic tick) actually runs for a P2P host.
_Avoid_: B-batch, vanilla-fix batch, U3DS alignment batch, Route B, Admission spec

**Join Routing**:
The pre-world classification and `Provider.connect` parameter assembly for SteamID and Direct-IP, including password and `:port` suffix handling.
_Avoid_: Admission spec, Route B, handshake, quarantine

**Listen-Host Session Password**:
The optional host password that exists only in the current listen-host session as `Provider.serverPassword`; empty means an open room.
_Avoid_: room setting, persisted password, connect password, whitelist

**Animal Pack Model**:
The unresolved design choice for Animal: vanilla AnimalManager has no per-region generate, so a Region Lease slice must not be wired until the project decides full-map pack lifetime versus a new observer model.
_Avoid_: animal migration, bound lease for animals, SPI-ready animal adapter
