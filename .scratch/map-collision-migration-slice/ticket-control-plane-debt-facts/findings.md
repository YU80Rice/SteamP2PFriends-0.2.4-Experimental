# 控制面债 vs Collision 接线不变量 — 事实调查

- **调查日期**: 2026-09-18
- **HEAD**: `4593770`（`docs: archive 09-17 join triage and reported-bug notes`，2026-09-18 08:51 +0800）
- **工作树**: 生产源码与 `audit/` 未改；`.scratch/map-collision-migration-slice/` 为本图未跟踪工作区
- **范围**: 只读对照当前生产调用图与 Runtime 审计。不裁定哪些债阻塞 Collision 接线。
- **不得当作当前状态**: `audit/2026-09-04/ReviewRecheck-0.2.4.8-0933.md`（停在 Ticket 11 瘫痪期）
- **原判决（非当前证据）**: `ARCHITECTURE-REVIEW-0.2.4-Experimental.md` §3.5 / §3.6（2026-08-26；里程碑注记写明 §3 翻案进度曾指向 09-04 Recheck）

判定口径：

| 标签 | 含义 |
|---|---|
| 仍被违反 | 当前生产调用图仍使该不变量不成立 |
| 已部分修复 | 在某一层/某一域成立，其它层仍缺口 |
| 仅静态可判定 | 代码路径存在或可证明，但无对应 Runtime 命中/未命中证据 |

当前生产控制面形状（判定前提）：

- Coordinator 只配置了 Resource 生产接缝：`Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDomainModules.cs:53-61` `ConfigureResourceProduction(resource)`。Collision 仅 `RegisterLifecycle(new LevelObjectCollisionAdapter())`（同文件 `:71`），**无** `ConfigureCollision*`。
- Collision 覆盖谓词仍是私有轮询 ∪ 死/未驱动 SPI 分支：`Adapters/Collision/Patches/LevelObjectRemoteCollisionPatch.cs:198`、`:218`。
- 树木碰撞在 SPI lease 与旧轮询之间 **OR**：`Adapters/Resource/Patches/LevelGroundRemoteTreeCollisionPatch.cs:85-86`。

---

## 六条不变量对照表

| # | 不变量 | 状态 | 证据 |
|---|---|---|---|
| 1 | 单个观察者或单个区域记录异常不会撤销其它区域的有效需求 | **已部分修复** | **Resource 区域级 acquire：已隔离。** `ResourceProductionControlSeam.ProcessSingleRegionEntry`（`Adapters/Resource/ResourceProductionControlSeam.cs:651-723`）对快照不可用/其它 acquire 失败 `return` 本区域并 `ScheduleAcquireRetry`，注释写明不触发 `UpdateObserver` 整批回滚、不进 `ShadowFaultBackoff`。契约：`Test_M6P31_CaptureRegionFailureIsolationKeepsOtherRegions` / `Test_M6P32_SnapshotUnavailableDefersRetryThenAcquires`（`WhitelistTests/Evidence/PureMemory/Adapters/Resource/ResourceProductionControlSeamTests.cs:560-630`）。Runtime：`audit/2026-09-11/Runtime-0.2.4.8-Ticket12-0031.md` §1/§2 — 0 次 `ObserverUpdate failed`、0 次回滚、0 次 M0 fault、0 次 `fault-recovery` 会话重建。R4 治本+兜底：同文件 `ProcessExited` 登记撤销补偿（`:554-559`）与 `stale-exit-without-demand` 跳过（`:564-570`）；契约 M6P33/34/35（测试文件 `:633-734`）；实施记录 `audit/2026-09-10/Implementation-0.2.4.8-Ticket12-0027.md` §2–§3。Ticket 11 窗口仍见「单区 deferred 退出 → underflow → 整局重建」：`audit/2026-09-09/Runtime-0.2.4.8-Ticket11-2314.md` §3（4 次 fault / epoch 1→3→5→7），该层已被 09-11 归零，**不得**再当当前 Runtime 状态。 **Coordinator 观察者级 capture：仍整批 fail-closed，但不直接 Decrement。** `CaptureSamples` 任一条 `invalid-observer-record` / `connection-generation-rejected` 即 `Incomplete`（`MultiObserverShadowCoordinator.cs:324-331`、`:356-359`、`:398-404`），`Tick` 见 `!capture.IsComplete` 则 `SafeMismatch("capture-incomplete")` 并 `return`（`:172-176`），**不调用** `Ledger.Reconcile` / `ReconcileResourceProduction`。因此已建立的 demand/lease **本拍不被撤销**，但**所有**观察者的本拍更新被冻结。`MultiObserverShadowLedger.Decrement` 仍对下溢 `throw`（`Core/ControlPlane/MultiObserverShadowLedger.cs:468-471`）；一旦 reconcile 走到不一致计数，仍会经 `SteamP2PFriendsPlugin.cs:252-260` → `HandleTickFailure`（coordinator `:252-267`）→ 恢复时 `EndSessionIfNeeded("fault-recovery")`（`:118-123`、`:601-616`）清掉 **全部** Resource lease。该 throw 路径 **仅静态可判定**（09-11 未再命中）。原判决 §3.5 把「整批丢样本 + 下溢 throw→退避」绑在一起；前者结构仍在，后者 Resource 已知退出路径已不再把整局需求拆掉。 |
| 2 | Session 身份缺失或错过通知后存在可重建、重试或显式熔断路径，不能永久静默 | **仍被违反** | 身份门仍是硬 `return`：`MultiObserverShadowCoordinator.Tick` `:134-144` — `currentSessionId` 空、`_hostSessionId` 空、或二者 Ordinal 不等，则 `SafeMismatch("host-session-identity", … shadow reconcile suppressed)` 后 **return**；不 `BeginSession`、不 `EndSessionIfNeeded`、无重试计数、无熔断到显式安全态。`SafeMismatch`（`:670-678`）按 key+detail 去重，外部通常只见一条事件。通知入口仍只一处且可被吞：`Platform/Host/HostManager.cs:1904` `BeginSession` → `TryNotifyMultiObserverSessionStarted`（`:1935-1948`）try/catch 只打 `session-start notification failed: {TypeName}`，**不**回写 Coordinator、不重试。`NotifyHostSessionStarted`（coordinator `:223-233`）本身在空 id 时 throw，会被该 catch 吃掉。结束通知同样可被吞（HostManager `:1951-1964`）。Whitelist 无 `host-session-identity` / `NotifyHostSessionStarted` 契约（对 `WhitelistTests` 检索为空）。Runtime：`audit/2026-09-11/Runtime-0.2.4.8-Ticket12-0031.md` 全程 `sessionEpoch=1`、唯一 `session-end` 为 `host-session-ended:DisconnectCompleted`，**未观察**到该失明路径；`audit/2026-09-17` / `audit/2026-09-18` 的 join-routing 验收也不审计该 mismatch。故「错过一次通知会整局失明」是 **调用图事实**，不是 09-11 实机命中。原判决 §3.6 与当前源码结构同构（行号已从 `:106-116` 迁到 `:134-144`；通知从当时的 `HostManager.cs:1922` 迁到 `:1939`）。 |
| 3 | 一个领域失败不会释放其它领域的 Lease | **仍被违反**（共享故障域；Collision SPI 租约尚未存在） | 生产接缝目前只有 Resource。Coordinator `EndSessionIfNeeded`（`:601-616`）在任何会话结束/fault-recovery 时调用 `ResourceProduction?.EndSession()`，`ResourceProductionControlSeam.EndSession`（`:166-202`）再 `ClearManagedSessionState`（`:205-220`）清空 **该接缝全部** demand/lease/retry。跨域耦合在插件 Update：`SteamP2PFriendsPlugin.cs:252-260` 将 `MultiObserverShadowCoordinator.Tick` 与 `ZombieRegionLifecycleAdapter.Tick()` 放进 **同一 try**；任一 throw → `HandleTickFailure` → 退避恢复时拆掉 Resource 会话。Coordinator **没有**按 `DomainId` 隔离的 catch。Collision `LevelObjectCollisionAdapter` 虽注册为 lifecycle（`PatchRegistrationDomainModules.cs:71`，编排要求 `DomainIds.Collision, true, false` 于 `PatchRegistrationOrchestration.cs:37`），但 Coordinator 从不对其 `OnAcquire`/`OnRelease`。因此「其它领域的 Lease」在 Collision 侧 **生产上还不存在**；若 Collision 挂上同一 `Tick`/`HandleTickFailure`，将继承「一域 throw → 全接缝 EndSession」的形状。域 **内** 单区域失败不再释放其它 Resource 区域（见不变量 1 的 M6P31），不能外推为跨域隔离。本条无 09-11/09-17 Runtime 反例（未构造跨域故障注入），机制依赖静态调用图。 |
| 4 | retry/compensation 以 `(domain, region, observer/session identity)` 为足够区分度，不发生单槽互吞 | **仍被违反** | 登记结构仍是 `Dictionary<RegionKey, AcquireRetry>`（`ResourceProductionControlSeam.cs:65-66`）。`AcquireRetry` 虽带 `ObserverId`（`:31-42`），`ScheduleAcquireRetry`（`:781-790`）按 **区域单键覆写**；仅当 `previous.ObserverId == observerId` 时累加 attempts，否则第二观察者直接占槽。注释宣称「登记按观察者归属,避免多观察者同区域时互相吞并」（`:29-30`）与字典形状矛盾。退出 head-check 要求 `leavingRetry.ObserverId == observerId`（`:554-559`），被吞观察者走完整退出 → demand=0 → 现走 `stale-exit-without-demand` 跳过。Runtime 根因已写死：`audit/2026-09-11/Runtime-0.2.4.8-Ticket12-0031.md` §2 — 51 次容忍全部 `outcome=skipped`；互吞被指为失衡制造源；遗留项①明确「需把登记键改为 (region, observer) 二元组」，本票未改。契约 M6P31–35 均为 **单观察者**；无双观察者同区 retry 测试。domain 维度：当前仅 Resource 接缝，key 不含 `DomainId`。compensation 列表按单次 `UpdateObserver` 事务（`:251-278`）回滚该观察者快照，**不能**补上被其它观察者覆写的 retry 槽。Collision 尚未拥有独立 retry 表；复制 Resource 接缝会复制该单槽。 |
| 5 | Coordinator 重启或会话切换后不会让旧 generation 获得写入资格 | **已部分修复** | **Resource 写入门闩已在适配器层成立。** `ResourceRegionLifecycleAdapter.TryCommitAcquire`（`:227-238`）`SessionEpoch != expectedEpoch` → `session-generation-mismatch`；`ResourceDomainAdapter.OnAcquire`（`:90-100`）拒绝后 throw。Release 同等校验（adapter `:127-138`；seam Flush `:394-405` 对 pending 的 epoch 不匹配保留 pending、不提交）。会话重置清租约：`Test_M6P04_ReconnectAndSessionResetInvalidateState`（测试文件 `:67-88`）；repair 拒绝新会话：`Test_M6P30`（`:542-557`）。Ledger `BeginSession`/`EndSession` 均 `AdvanceEpoch`（`MultiObserverShadowLedger.cs:204-226`、`:476-480`），这解释了 Ticket 11 重建呈奇数 epoch（1→3→5→7）。09-11 Runtime 全程 epoch=1，结束 `nextEpoch=2`。 **Coordinator 身份不匹配不会撤旧写资格。** Tick `:134-144` 在 mismatch 时 **不** `EndSessionIfNeeded`；若 `_hostSessionId` 仍指向已开始的 Resource 会话而 `HostManager.CurrentSessionId` 已变，旧 epoch lease 保持可写直到 listen-host 失活分支（`:126-131`）或成功的 end 通知。end 通知失败可被 HostManager catch 吞掉（`:1951-1964`）。listen-host 失活时仍会 EndSession，故「主机关房」未必泄漏；「通知丢失但 listen 仍 active」才是旧代次窗口。 **Collision 适配器 acquire 无 epoch 门闩。** `LevelObjectCollisionAdapter.OnAcquire`（`:161-169`）仅 `ticket.Valid` 则 `CommitAcquire`，不读 `ticket.SessionEpoch`。Ledger 的 epoch 拒绝只在 `TryCommitRelease`（`:80-88`）；契约 `Test_M6C05_StaleSessionCannotCommitRelease` / `M6C06`（`WhitelistTests/Evidence/PureMemory/Adapters/Collision/LevelObjectCollisionAdapterTests.cs:62-85`）覆盖 **释放** 而非 acquire。Collision 生产写入者仍是 `RemoteCoverage`（patch `:211-218`、`:532-546`），与 Coordinator epoch **解耦**。 |
| 6 | 故障必须可观测，且不会悄悄恢复旧私有轮询 | **已部分修复**（可观测）/ **仍被违反**（故障时退回旧轮询） | **可观测（Resource/M0 主路径）。** 结构化 `ResourceObservability`（path/spiActive/outcome）；M0 `SafeEvent`/`SafeWarn`/`fault=` 配额（coordinator `:252-267`、`:697-713`）；09-11 Runtime 能点数 `LeaseAcquire`/`RegionExit skipped`/`CollisionActivation`/`session-end reason=`。R2 把 deferred Info 在 attempts≥5 降级（seam `:59-62`、`:700-710`；09-11 §3 `attempts=6+` = 0），重试仍在，只是日志变少。`host-session-identity` / `capture-incomplete` 依赖 `SafeMismatch` 去重，持续静默期只有首次事件（coordinator `:670-678`）。Whitelist 无 mismatch 持续性契约。 **旧私有轮询仍是故障回退 Writer。** 树：`LevelGroundRemoteTreeCollisionPatch.cs:85-86` `IsRegionActive \|\| IsRegionCovered`。Resource `EndSession`/`fault-recovery` 清 `_activeRegions`（lifecycle ledger `:176-181`）后，第一条件为 false，第二条件 `LevelObjectRemoteCollisionPatch.IsRegionCovered`（`:211-218`，只读 `RemoteCoverage` ∪ `IsRemoteCollisionRequired`）继续决定覆盖。`IsRemoteCollisionRequired` 读 Collision ledger（`LevelObjectCollisionAdapter.cs:127-132`），生产无 `OnAcquire` → 恒 false，与原判决 §1.3 死分支同构，但 **第一条件在 Resource 有 lease 时已不再恒 false**（09-11 `CollisionActivation` 10090 且 `spiActive` 随 Resource 会话；Ticket 12 §4）。物件碰撞主路径仍是 `RemoteCoverage` 私有扫描（patch `LevelObjectsUpdate_Postfix` `:221-225` → `ReconcileRemoteCoverage`）。故障时树碰撞 **功能上恢复旧轮询**；日志带 `spiActive=false`/`path=Native|Legacy`，因此不是完全无埋点，但是 **Writer 切回**，不是 fail-closed。原判决 §1.3 称新机制未接线、旧轮询独自工作——Resource 切片后变为「SPI 有 lease 时两路 OR，SPI 无 lease 时旧路独活」，不是 08-26 的纯死分支，也不是已切断的旧 Writer。 |

---

## 对「Collision 准入门」grilling 有用的派生事实

以下是调用图/审计可引用的事实，**不是**准入裁定。

1. **Resource 切片带着 Coordinator 级 fail-closed 走通了 Runtime。** 09-11 在 `e878358` 上证明 Resource 主路径 0 fault / 0 会话重建，同时 **留下** capture 整批 Incomplete、`host-session-identity` 硬 return、`_acquireRetries` 单槽、Tick 与 Zombie 同 try。grilling 需要区分「域内区域隔离已证明」与「Coordinator 观察者/会话故障域未证明」。
2. **「不撤销其它区域有效需求」在 Resource 接缝与 Coordinator capture 上不是同一层。** 接缝：失败区域 skip+retry，其它区域 lease 保留（M6P31，09-11）。Coordinator：坏观察者 → 整批样本丢弃 → 本拍所有观察者都不 reconcile；已有 demand 冻结而非 Decrement。若 Collision 需求也从同一 `CaptureSamples` 驱动，加入中的不完整 `SteamPlayer` 会卡住 **Collision 与 Resource** 的本拍需求推进。
3. **会话身份没有自愈。** 唯一 writer 是 `Stage6ASessionContext.BeginSession` 里那一次 notify。失败被 catch。Tick 侧没有「用 `HostManager.CurrentSessionId` 重建 `_hostSessionId`」或「N 次 mismatch 后 EndSession/熔断」。09-11 健康局不能当作该路径已修。
4. **跨域 Lease 隔离目前无从 Runtime 证实。** 只有 Resource 接缝会被 `EndSessionIfNeeded` 拆掉。Collision SPI lease 未进入生产。共享故障域已经存在（Coordinator.Tick + Zombie.Tick）。把 Collision 接进同一 `Tick` 而不拆故障域，会让 Zombie/Resource 异常释放 Collision lease（一旦存在）。
5. **单槽互吞已被 09-11 实机定性，且被容忍路径无害化，而非被修掉。** 51 次 `stale-exit-without-demand` 是互吞的观测代理。损害评估（同报告 §2）：被吞观察者丢失该区域重试资格，退出重进自愈；demand/spatialIndex 因跳过保持一致。若 Collision 复用「按区域单槽」retry，多观察者同区会再现资格互吞。
6. **generation 防旧写在 Resource acquire/release 上闭合，在 Coordinator 会话门与 Collision acquire 上未闭合。** Collision `OnAcquire` 不校验 epoch；私有 `RemoteCoverage` 完全不参与 `SessionEpoch`。会话切换后旧 RemoteCoverage 是否残留取决于 patch 自己的 `ResetAll`（`Core/Lifecycle/SteamP2PFriendsPlugin.PatchRegistry.cs:96`）与 disconnect dispatcher（`SessionDisconnectDispatcher.cs:72`），不是 Coordinator epoch。
7. **旧私有轮询不是旁路日志，而是覆盖谓词的第二子句。** 树路径在 SPI 失活时自动继续用 `IsRegionCovered`。Collision 物件路径本来就只用它。若准入门要求「故障不得恢复旧 Writer」，当前树碰撞 OR 与物件私有扫描都与之冲突；若只要求「故障可观测」，09-11 的 `spiActive`/`path` 字段已能区分。
8. **Ledger 下溢 throw 仍在，但 09-11 不再是 Resource 退出路径的主故障。** 原 §3.5 的「指数退避最长 60s」仍是 `ShadowFaultBackoff` 代码（`MultiObserverShadowLedger.cs:30-59`，上限 60s；契约 `Test_M11_ShutdownGateAndFaultBackoffAreBounded`）。当前实证故障源已从「单区 acquire 整批回滚」转为「未再命中」。
9. **09-17/09-18 Runtime 不能替代 09-11 作为控制面债证据。** `audit/2026-09-17/RuntimeAcceptance-0.2.4.8-Ticket05-2320.md` 与 `audit/2026-09-18/*` 服务 join-routing，不审计 Resource 接缝或 host-session-identity。

---

## 本票不裁定的事项

- **不裁定** 上表六条里哪些构成 Collision Control-Plane Readiness Gate 的阻塞前置（那是 `ticket-control-plane-readiness-gate`）。
- **不裁定** 单槽互吞在「容忍已无害化」之后是否仍算阻塞。
- **不裁定** capture 整批抑制（冻结、不撤销）是否算违反「不撤销有效需求」。
- **不裁定** 树碰撞 OR 回退算不算「悄悄恢复旧私有轮询」，或可观测的 fail-open 是否可接受。
- **不裁定** Collision 应复用 Resource 接缝、提炼共享编排，还是先改 Coordinator。
- **不关闭** 本调查票；关闭由主会话处理。
