# Implementation-0.2.4.9-09-R1-1042

**票**：`.scratch/collision-lifecycle-diagnostic-repair/`（票 09 的独立返修票 09-R1：Collision 生命周期诊断修复）
**轴**：实现（红先行 TDD + 双轴独立审查至双 CLEAN）
**产物**：`bin/Release/SteamP2PFriends.dll`（Cutover 候选角色，未部署）

## 1. 问题与已核实根因

用户多轮 1H2G 人工观察确认 Collision 功能正常（远区进入、需求归零、滞回内返回、集装箱门/洗衣机门动画与碰撞），但诊断证据不可判读。经逐行核实，缺陷为**诊断身份丢失**，非状态污染：

1. **领域错标**：`MultiObserverShadowCoordinator.ConfigureControlPlane` 把共享编排引擎的唯一诊断出口固定为 `ResourceLifecycleDiagnostics.Instance`；`ResourceObservability.Format` 硬编码 `[ResourceObs] ... domain=Resource`。Collision 域转换与引擎级转换（SessionBegin/End/WriteGate/DomainFault）全部流经该出口被错标为 Resource。票 09 S3 取证（4293 条 Acquire 全拒）后的 2026-10-06 UMM 中 Host 行 28755 `Collision ReleaseScheduled`/28949 同区域同代次 Reentry 只能靠 authority 关联间接判读。
2. **Reentry 缺 authority**：引擎 `ProcessSingleRegionEntry` 的 `LeaseReentry` detail 只有 `hysteresisCancelled=true`，单行无法识别领域权威。
3. **receipt 身份缺失**：`CollisionExecutionPort` 释放拒绝路径 `DescribeReleaseRejection` 对无 receipt 打 `receiptAcquireGeneration=0`（虚构零值），且缺领域/区域/receipt 会话与区域代次；成功行只有 `receiptAcquireGeneration=N`。另有加重缺陷：`TryRelease` 的短路与使 `CanCommit` 失败时 receipt 即使在场也被打成 0。

## 2. 修复内容（11 文件，+421/-30）

生产（`SteamP2PFriends.csproj` 登记 1 个新文件）：

- **新增 `Core/ControlPlane/Lifecycle/DomainRoutingLifecycleDiagnostics.cs`**：数据驱动的领域路由出口。路由表由接线以 `IReadOnlyDictionary<DomainId, ILifecycleDiagnostics>` 注入，未登记领域（含引擎级 default）走 fallback；本类不读取任何领域常量、不认识具体领域（守住 `Test_EngineHasNoDomainBranch` 全命名空间门禁）。
- `Core/ControlPlane/MultiObserverShadowCoordinator.cs`：接线改为路由出口——`{Resource → ResourceLifecycleDiagnostics.Instance, Collision → DefaultLifecycleDiagnostics.Instance}`，fallback = Default。
- `Adapters/Resource/ResourceLifecycleDiagnostics.cs`：防错标守门——非 Resource 领域记录（含引擎级）原样转投 `DefaultLifecycleDiagnostics`，绝不进入 `[ResourceObs]` 格式化路径；Transition 与 TransitionOnce 双路径同守。即被误接线也只会走默认出口，不会产生 `[ResourceObs] domain=Collision` 冲突身份行。
- `Core/ControlPlane/Lifecycle/LifecycleOrchestrationEngine.cs`：Reentry detail 补 `authority=<Port.DisplayName>`。
- `Core/ControlPlane/Lifecycle/ILifecycleDiagnostics.cs`：`DefaultLifecycleDiagnostics` 消息组装抽为 `internal static string FormatLine(...)`（逐字节等价的纯重构）使 `[LifecycleObs]` 生产行格式可契约测试；`TransitionOnce` 落地接口既承诺的按 key 去重（静态 `OnceKeys`，lock 保护）；新增只读观测缝 `IsOnceKeyRecorded` / `WriteCount`（Interlocked）与 `ResetProbeState` 复位缝。
- `Adapters/Collision/CollisionExecutionPort.cs`：`TryRelease` 两重载与所有权路径统一走 `FormatReleaseDiagnostic`——拒绝/成功/ownership-unproven/stale-receipt 四出口均携带 `domain=Collision region=<r> sessionEpoch=<命令> regionGeneration=<命令>` + 在场 receipt 的 `receiptSessionEpoch/receiptRegionGeneration/receiptAcquireGeneration`；receipt 缺失显式 `receipt=missing`，不再虚构零值。既有 token（`receiptAcquireGeneration=`、`ownership-unproven`、`reason=release-identity-rejected`）全部保留，CEP10-13 既有断言不动即绿。
- `Adapters/Resource/ResourceObservability.cs`：只读探针 `IsNoticeRecorded`（观测 NoticeOnce 去重集合，不改行为）。

测试（`WhitelistTests`，10 个新方法，全部先观测红后转绿）：

| 测试 | 契约 | 红点（旧实现） |
|---|---|---|
| LIR01 | 真实桥不把 Collision 诊断送入 Resource 格式化/去重出口 | `collisionReachedResourceExit=True` |
| LIR02 | 路由出口按记录领域分发 + fallback 透传（新类结构锁） | （新 API，绿锁） |
| LIR03 | `[LifecycleObs]` 行单行满足 Reentry 契约 + 各 severity 保留（新 API，绿锁） | （同上） |
| LIR04 | 默认出口 TransitionOnce 按 key 去重：首次 `WriteCount+1`、重复不增 | 探针恒 false / 重复仍写出 |
| LOE16 | 同区双领域滞回重入：domain/authority 双真实 + pending 取消/无最终 Release/无额外 Acquire + `FormatLine` 端到端行断言 | detail 缺 `authority=` |
| CEP20 | 无 receipt 显式 `receipt=missing` + 命令身份，无零值伪装 | `receiptAcquireGeneration=0` |
| CEP21 | 拒绝行同时携带命令与 receipt 双侧身份 | 缺 receiptSessionEpoch/RegionGeneration/domain/region |
| CEP22 | 成功释放行完整身份 | 仅 `receiptAcquireGeneration=1` |
| CEP23 | 跨会话相同数字代次（acq=1）靠 receiptSessionEpoch 区分 | 两行 receipt 侧不可区分 |
| CEP24 | 真实生产身份门拒绝异会话命令 + Store 零原生写入 | 拒绝行无 receipt/命令身份 |
| CEP25 | stale-receipt 拒绝行具名新旧两个 receipt 代次 | （第 3 轮补，绿锁） |

接线门禁 `Test_ProductionDiagnosticsWiringRoutesByDomain`（StaticIL）：`ConfigureControlPlane` 必须构造 `DomainRoutingLifecycleDiagnostics` 恰 1 次、引擎 ctor 恰 1 次、`ResourceLifecycleDiagnostics.Instance` 字段加载恰 1 次（Resource 路由必须登记）、`Dictionary`2.Add` 恰 2 次。旧接线下红。

## 3. 验证与红绿链

- 每次生产改动后先重编插件再重编测试宿主（HintPath 顺序，`bin\Release\SteamP2PFriends.dll`）。
- 红观测：基线 413/413 → 第 1 轮 8 处红（LIR01/LOE16/CEP20-24/接线门禁）逐条核对红点均对准缺陷本体 → 绿实现 → 423/423。LIR02/LIR03/CEP25 为新增结构绿锁。
- 审查修复轮：P1 去重回归（见 §4）→ LIR04 红（`onceKeyConsumed=False`）→ 绿 → 424/424；门禁路由表断言初版因 IL 形态误判（Instance 为静态字段非属性、泛型 FullName 带构造后缀）红 → 修正探针（`CountFieldLoads` + `DeclaringType.Name == "Dictionary\`2"`）→ 绿 → 425/425。
- 最终：插件/测试宿主 Release 构建 **0 error 0 warning**，全量 **425/425 PASS**（基线 413 全部保持，无一修改既有断言）。

## 4. 双轴独立审查链（全新实例并行，每轮两个新上下文）

| 轮 | Standards | Spec | 处置 |
|---|---|---|---|
| R1 | **CLEAN**（deferrable：diff 证据不完整、默认出口去重语义落差、测试辅助重复） | **GAPS**（P1：引擎级 TransitionOnce 去重丢失——旧接线经 `NoticeOnce` 去重，改道 fallback 后无去重，而生产非托管状态每帧调 `EndSession` 幂等分支，会每帧刷 `[LifecycleObs] SessionEnd` 行） | P1 修复：默认出口落地按 key 去重（红先行 LIR04）；LOE16 补端到端行断言（消 P2）；重生成完整 diff |
| R2 | **CLEAN**（deferrable：门禁为代理计数、去重三元组重复、数据泥团、WriteCount 注释口径、LIR01 无 finally、null 语义不对称） | **GAPS**（P2：LIR04 弱断言——「只记录不去重」的错误实现同样转绿；P3：Transition 分支守门无直接测试、OnceKeys 进程级、stale-receipt 行格式无锁、`outcome=Success` 词汇口径） | LIR04 升级为写计数 delta 断言 + try/finally 复位；接线门禁补路由表内容断言；新增 CEP25 |
| R3 | **CLEAN** | **CLEAN** | 闭环 |

## 5. 具名接缝缺口（未冒称 Runtime 证据）

- **CEP24**：纯内存宿主无法执行真实 `LevelObjectCollisionAdapter.OnSessionBegin`（`RestoreOwnedNativeState` 含 Unity extern 调用点，JIT 编译即抛 ECall，与可达无关——票 10 CEP18 已具名同类缺口）。该测试以真实 `CollisionExecutionPort`（CanCommit 会话/区域代次门 + receipt 比对——被测的生产身份门本体）+ 真实代次源 `LevelObjectCollisionAdapter.GetGeneration` + 可证身份的 Fake store 执行，并断言 `store.Revoked.Count==0`（零原生写入）。**本票自动化全绿不冒称 Runtime PASS**。
- **陈旧 receipt 的 Runtime 可达性**：端口在 `OnSessionBegin` 清空 receipts 并重置 Acquire 代次——正常会话清理令旧 receipt 不再可提交，生产日志中**未自然触发**旧 receipt 拒绝属预期，不得据无拒绝日志推断用户未完成测试。父规格 Runtime 陈旧 receipt 门不在本票静默删改；受控探针（如需）须单独建票审查，本票不部署、不新增网络指令。

## 6. 产物身份（双次 Rebuild 独立重采）

- SHA-256（两次 `-t:Rebuild` Release 一致）：`2C3AFF22651386117316F474797A15FAF15E16C27382C758B85B4966DD2850A6`
- MVID：`c9aab7c6-b7ee-411a-88b4-2f92915acf91`
- Case-ID / 候选角色：`SPF-0.2.4.9-Experimental-CollisionSlice-Cutover` / `Cutover`
- `Tools/Verify-BuildFingerprintArtifact.ps1 -ExpectedCandidateRole Cutover` → **PASS**（INDEPENDENT_ARTIFACT_VERIFICATION_PASS）
- 证据：`.scratch/ticket09r1-rebuild1-identity.txt`、`.scratch/ticket09r1-rebuild2-identity.txt`

## 7. Deferrable 残留（具名，双方轴 R3 接受为可延期）

1. 接线门禁为代理计数，无法验证路由表「键→出口」配对方向（若接成 `{Resource→Default, Collision→Resource桥}` 门禁仍绿；建议后续补生产路由实例行为断言）。
2. 真实 Resource 桥 `Transition`（非 Once）分支守门无直接 PureMemory 断言（写路径经 `RoleLogger` 在纯宿主不可观测；第一层路由 + IL 门禁 + 守门三层防护在位）。
3. `DefaultLifecycleDiagnostics.OnceKeys` 进程级、不随会话清理；引擎级 onceKey 为封闭常量集（2 个），实际有界。语义裁定：这些 key 都是幂等分支防刷屏通知，进程粒度足够；`ResetProbeState` 为观测缝复位，生产不复位。
4. 「lock + HashSet + IsXxxRecorded」去重三元组在 Default 出口与 ResourceObservability 并存（Duplicated Code，收敛留后续票）；`IsOnceKeyRecorded(null)` 映射空串与 `TransitionOnce(null)` 免去重不对称。
5. `WriteCount` 在日志层调用前自增且异常被吞，实为「尝试写出」计数。
6. `FormatReleaseDiagnostic` 布尔字面量实参可读性（可改 `Nullable<receipt>`）。
7. 词汇口径：spec 契约 2 写 `outcome=success`（小写），`[LifecycleObs]` 行沿用引擎枚举渲染 `outcome=Success`（`[ResourceObs]` 行保持小写 `success` 词表不变）；判定为措辞差异，非行为缺陷。
8. 级别语义变化（具名）：引擎级 onceKey 通知（如 `SessionEnd reason=session-not-active`）旧经 `NoticeOnce` 固定 Warn 级且错标 Resource；修复后按记录声明级别（Info）经 `[LifecycleObs]` 输出——领域错标与固定 Warn 都是旧出口的改写行为，本票恢复记录自带的级别与领域身份。
9. LIR01 测试无 finally 复位 Resource 去重集合（键名 `09R1-*` 唯一命名空间，现无串扰）。

## 8. 边界与归属

- 未修改需求半径、滞回、Writer、门玩法状态；未恢复旧 Writer；未加默认运行的破坏性测试注入；诊断保持只观察。
- 票 09 仍未关单：本票完成不等于父票自动关单；诊断修复后的最小 Runtime 补证矩阵（1H1G 冒烟 + 多观察者沿用范围裁定）待人工实机轮次，按 `docs/agents/real-machine-test-loop.md` 执行。
- 工作区中 `audit/README.md`、`docs/agents/output-review-loop.md`、`.scratch/collision-migration-slice/*` 等未提交修改为前轮遗留，**非本票范围**，不随本票提交。
- 历史人工功能证据（多次滞回内返回、门动画/碰撞正常）与新诊断修复证据分开归档：前者保留于票 09 及 2026-10-05 2313 审计，未被本票改判。
