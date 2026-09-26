# Implementation 0.2.4.9 — Collision Ticket 08

日期：2026-09-26
票据：`.scratch/collision-migration-slice/issues/08-session-boundary-cutover-and-legacy-writer-retirement.md`
状态：`implemented-pending-runtime`
版本：`0.2.4.9 / Experimental`
正式候选：`CandidateRole=Cutover`
Case-ID：`SPF-0.2.4.9-Experimental-CollisionSlice-Cutover`

## 1. 结论

票 08 已完成受控正式切换的静态与纯内存实现：插件启动/新 Session Epoch 使用共享 `LifecycleOrchestrationEngine` 注册唯一 Collision `CollisionExecutionPort`；正式 Collision 写入只经该端口进入 `ICollisionOverrideStore`。旧 `RemoteCoverage` Writer、观察者扫描、reconcile、刷新写入、树越权写入、Resource 对旧覆盖谓词读取和生产影子比较均退出正式调用图。

- 红测：CEP14 在真实测试宿主执行，`405/406 PASS`，失败原因为 Receipt 绑定 Acquire 前 ticket generation，而生产 Store 在 Acquire 中推进了区域代次。
- 绿测：端口改为 Store Acquire 成功后读取实际 `RegionGeneration` 构造 Receipt；新增 CEP15 验证 Port→Store Session Begin/End 转发，CEP16 验证空原生目标的区域级 Receipt 仍可释放；区域快照按 `RegionKey` 隔离并恢复原生所有权，重复 Acquire 会重新纳入同区已有 targets，Resource 与 Collision 的批量样本拒绝分别进入各自 Domain 的 Deferred Observer Demand；最终 `409/409 PASS (Failed: 0)`。
- 插件 Release 构建：0 error / 0 warning。
- WhitelistTests Release 构建：0 error / 0 warning。
- 两次 Cutover Release Rebuild 身份逐项一致；独立 `Verify-BuildFingerprintArtifact.ps1 -ExpectedCandidateRole Cutover`：`INDEPENDENT_ARTIFACT_VERIFICATION_PASS`。
- Runtime：**PENDING**，归票 09；本票不宣称正式 SP、listen-host、U3DS 或 P2P Runtime PASS。
- 不保留同一会话内旧 Writer 回退；失败/retry 耗尽只由共享引擎 fail-closed/结束会话语义处理，不转调旧 Writer。

## 2. 变更清单

### 生产源码

- `Core/ControlPlane/MultiObserverShadowCoordinator.cs`：创建唯一共享投影/生命周期引擎；Resource 与 Collision 各注册一次；canonical observer samples 同时提交 Collision；正式 Tick 不运行影子比较；整批拒绝将 Collision 观察者置于 Deferred Observer Demand；摘要改为正式 Collision demand/lease 字段。
- `Adapters/Collision/CollisionExecutionPort.cs`：Acquire 后读取 Store 实际区域代次构造 Receipt；Session Begin/End 转发到 Store，确保生产注册闸门和 Ledger 状态与 Epoch 同步。
- `Adapters/Collision/LevelObjectCollisionAdapter.cs`：实现 `ICollisionOverrideStore`，提供身份确定、Acquire 后真实 LevelObject/门动画原生 Override、明确的 `RegionLeaseMarker` 空区域级所有权、同区重复 Acquire 的完整 Receipt 覆盖、按 RegionKey 隔离的原生所有权快照/精确代次恢复、所有权证明、原子撤销与会话清理。
- `Adapters/Collision/Patches/LevelObjectRemoteCollisionPatch.cs`：旧 Harmony 注册退役；旧 Refresh/RemoteCoverage Writer 不再生产写入。
- `Adapters/Resource/Patches/LevelGroundRemoteTreeCollisionPatch.cs`：移除 `LevelObjectRemoteCollisionPatch.IsRegionCovered` 读取；Resource 只读取自身生命周期状态。
- `Core/Registration/*`、`Core/Lifecycle/*`、`Platform/Host/HostManager.cs`：移除旧 Collision Writer 的手动注册、断线 Remove、Shutdown/Abort/Stop Reset 调用。
- `Adapters/Resource/ResourceProductionControlSeam.cs`：生产控制面接收共享生命周期引擎；保留旧构造签名仅供既有 PureMemory seam，生产配置使用共享实例。

### PureMemory / StaticIL / BuildArtifact

- `CollisionExecutionPortTests.cs`：新增 CEP14，覆盖 Store 推进实际 generation 后 Receipt 必须绑定新代次；新增 CEP15，覆盖 Port→Store Session Begin/End；新增 CEP16，覆盖空原生目标的区域级 Receipt 释放；区域隔离与重复 Acquire 语义由最新回归/StaticIL 门覆盖。
- `CollisionShadowStaticILContractTests.cs`：旧“唯一生产 Writer/影子单消费者”语义改为旧 Writer 与影子生产调用均为零；保留纯内存比较器测试。
- `WhitelistTests/Program.cs`：目标更新为 `409`；CEP14、CEP15、CEP16、Deferred Domain 隔离与票 08 StaticIL 语义测试接入单一入口。
- `BuildArtifactEvidenceTests.cs`、测试元数据、`Verify-BuildFingerprintArtifact.ps1`：正式身份固定 Cutover，Case-ID 与 ReadOnlyShadow 可区分。

## 3. 红→绿链

1. CEP14 首次真实执行，测试宿主输出 `FAIL [PureMemory] CEP14 Receipt Actual Generation`，总计 `405/406 PASS`。
2. 修正测试 Store 假体为 Acquire 前 generation=7、Acquire 后 generation=8，确认红测锁定的是端口 Receipt 语义而非准入门。
3. `CollisionExecutionPort.TryAcquire` 保持 Acquire 前 `CanCommit` 身份门不变；Store 成功后调用 `ReadRegionGeneration`，用实际代次构造 Receipt。
4. 按 HintPath 依赖顺序先重编插件、再重编测试宿主；CEP14 绿测通过。
5. 更新旧 StaticIL 测试命名/注释、退役 Patch 为纯兼容壳、Store 原生 LevelObject/门动画执行门与启动器目标；最终宿主 `409/409 PASS`。

## 4. 生产调用图与不变量

- 唯一正式 Collision Writer：`LifecycleOrchestrationEngine → CollisionExecutionPort → ICollisionOverrideStore`。
- `LifecycleOrchestrationEngine.Register` 生产注册总数为 2：Resource 一处、Collision 一处；端口自身零注册。
- 旧 `LevelObjectRemoteCollisionPatch.RegisterManual` 不调用 Harmony `Patch`；旧 `RemoveRemotePlayer`、`ResetAll`、旧 Refresh 生产调用退出。
- `RunCollisionShadow`、`ReportCollisionShadow`、`StartCollisionShadowForbiddenHeartbeat`、`CollisionShadowDemandRegionCount`、`ResetCollisionShadow`、影子生产状态字段和旧 Snapshot 入口均已删除；退役 Patch 已无 Snapshot/扫描/刷新方法，正式程序集对 `CollisionShadowComparator.Compare` 与旧 Patch 原生写入的生产调用数为 0。
- Collision 不访问 `ResourceSpawnpoint`/可采集树；Resource 不读取 `IsRegionCovered`。
- Acquire Receipt 绑定 Domain、Region、Session Epoch、Store 执行后的 Region Generation、Acquire Generation 与实际 Override 集合。
- Release 只对当前有效 Receipt 做全量所有权预检后的原子撤销；身份不确定、需求仍在、代次陈旧或所有权不明时 fail-closed 且不破坏状态。
- 回滚边界是会话结束/上一份已验收构建，不存在同会话旧 Writer fallback。

## 5. 验证矩阵

| Evidence Class / Gate | 结果 | 证据 |
|---|---|---|
| PureMemory | PASS | CEP01–CEP16、领域 Deferred 隔离负向门与既有回归；唯一入口最终 `409/409 PASS` |
| StaticIL | PASS | Collision Cutover、Shadow No Production Consumer、Lifecycle 双领域注册、Readiness 与 Resource ownership 契约 |
| BuildArtifact | PASS | 两次 Cutover Release Rebuild 身份逐项一致；独立核验 PASS |
| Diff | PASS | `git diff --check` 无错误（仅 CRLF 提示） |
| Runtime | **PENDING** | 正式 1 Host + 2 Guest、第二连接代次会话恢复与实机旧 Writer 归零归票 09 |

## 6. 两次正式 Cutover Rebuild 身份

固定参数：`/p:SteamP2PFriendsCandidateRole=Cutover`。

| 产物 | Run 1 | Run 2 | 结论 |
|---|---|---|---|
| 插件 DLL SHA-256 | `CC29F77911C36F5EF571D4C6DCC28C3C70F51B3A0B88AC8B023EC62945E4F0D6` | `CC29F77911C36F5EF571D4C6DCC28C3C70F51B3A0B88AC8B023EC62945E4F0D6` | 一致 |
| 插件 DLL MVID | `a5465633-3708-4500-affe-fd96d577d180` | `a5465633-3708-4500-affe-fd96d577d180` | 一致 |
| 测试 EXE SHA-256 | `A3FF0772AC26E25A50A42BED85791A3AC7313B98CA8C5B6D6D2DC99BE5A2BF92` | `A3FF0772AC26E25A50A42BED85791A3AC7313B98CA8C5B6D6D2DC99BE5A2BF92` | 一致 |
| 测试 EXE MVID | `57d67aaa-013f-44d2-a958-a1418a169e82` | `57d67aaa-013f-44d2-a958-a1418a169e82` | 一致 |

正式身份：版本 `0.2.4.9`，CandidateRole `Cutover`，Case-ID `SPF-0.2.4.9-Experimental-CollisionSlice-Cutover`，插件 GUID `com.yu80rice.steamp2pfriends`。

## 7. 缝合缺口与延期项

- `MultiObserverShadowCoordinator` 与真实插件更新入口依赖 Unity/Unturned；PureMemory 不能替代真实多机运行，正式 Runtime 归票 09。
- 票 08 的构建/静态证据不能证明第二连接代次退出—等待—重进的实机因果；必须由票 09 采集。
- 票面曾有票 05/06 的历史“影子唯一 Writer”措辞；本轮已更新票 08 相关 StaticIL 方法名、注释与启动器标签，历史审计保持冻结。
- 当前工作树包含用户既有未提交文档与未跟踪实验目录；本票提交不得纳入这些范围外材料。

## 8. 双轴审查状态

最终修复轮由全新零上下文实例完成：Standards Reviewer = **CLEAN**，Spec Reviewer = **CLEAN**。本轮前 code-review 发现的默认候选角色、伪原生区域 marker 与补偿动画恢复问题已修复并复核：默认 CandidateRole 固定为 `Cutover`；空原生区域使用明确的 `RegionLeaseMarker`，不冒充 LevelObject target；活动区域补偿恢复门动画保持 `AlwaysAnimate`。同时，409/409、最终双次身份与独立核验、按 Domain 的 Deferred Observer Demand 均已复核。Runtime 仍为 **PENDING**，正式 1 Host + 2 Guest 与第二连接代次会话恢复归票 09。

## 9. 交付边界

本票授予 `implemented-pending-runtime`，不发 Release、不打 tag、不把影子 Runtime 升级为正式 Runtime PASS。票 09 负责正式 Cutover 候选的 1 Host + 2 Guest Runtime、会话恢复、第二连接代次与最终 Collision Slice 关单。
