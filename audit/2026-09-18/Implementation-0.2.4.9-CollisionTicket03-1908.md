# Ticket 03 实施与交付报告：Resource 经领域端口迁入共享生命周期编排引擎

- **日期**：2026-09-18 19:08
- **基线**：HEAD `11269eb`（feat(ticket02): route Resource demand through shared projection engine）
- **票据**：`.scratch/collision-migration-slice/issues/03-shared-lifecycle-orchestration-for-resource.md`（规格 `.scratch/collision-migration-slice/spec.md`）
- **范围**：Collision Migration Slice 实施票 03——共享 Lifecycle Orchestration Engine 拥有需求聚合、0→1 Acquire、N→0 滞回 Release、身份校验、retry、补偿调度、局部故障隔离与统一诊断；Resource 只经 Domain Execution Port 操作原生状态，生产接缝退化为薄门面，原有通用生命周期状态机退出生产权威。生产 Writer 仍唯一，Collision 仍无生产写入资格；切片 Runtime 验收归票 09，本票不宣称任何 Runtime PASS。

## 1. 结论

票面 8 项静态闭环。Control Plane 新增 `Lifecycle` 模块（编排引擎 + 领域执行端口 + 领域政策声明 + 统一诊断出口）；Resource 侧新增执行端口、Lifecycle Policy 声明与诊断出口，`ResourceProductionControlSeam` 由 1037 行状态机重写为薄门面（零编排集合字段、零嵌套编排类型）；票 01 表征门与票 02 投影契约全部保持绿。**唯一入口测试 335/335 PASS**、Release 双构建 0 error / 0 warning、三门禁 PASS、`git diff --check` CLEAN；**双轴审查 round 1–4 逐轮新实例，round 4 双 CLEAN**；扰动负控制 12 组逐项证伪。不创建 git tag，不发 GitHub Release，版本仍为票 01 授予的 `0.2.4.9`。

## 2. 变更清单（修改 9 文件 + 新增 11 文件）

| 文件 | 变更 |
|---|---|
| `Core/ControlPlane/Lifecycle/LifecycleOrchestrationEngine.cs` | 新增：编排状态机与多域注册。每域一份状态（需求引用计数、租约与区域代次、滞回登记、retry 登记、观察者与连接代次、时钟、repair-required）；`Observe`/`RemoveObserver` 为事务单元（失败回滚本域编排状态 + 投影贡献 + 原生副作用）；`AdvanceTime`/`Flush` 推进时间轴、刷新代次、提交到点释放并驱动领域心跳；查询面覆盖诊断与测试所需全部计数 |
| `Core/ControlPlane/Lifecycle/IDomainExecutionPort.cs` | 新增：领域执行端口（会话开始/结束、复制重置、Acquire/Release、生命周期/复制心跳、复制进入退出、断开、代次读取、失败分类、五组可逆捕获/还原） |
| `Core/ControlPlane/Lifecycle/LifecyclePolicy.cs` | 新增：领域声明的 Lifecycle Policy（滞回窗口 + 暂缓/一般 retry 节奏与静默阈值），`RetrySchedule.IntervalFor` 复现既有倍增上限语义 |
| `Core/ControlPlane/Lifecycle/LifecycleDiagnostic.cs` | 新增：统一转换诊断记录（领域、区域、事件、结果、级别、路径、会话/连接/区域代次、观察者、尝试、详情）与 `ELifecycleOutcome`/`ELifecyclePath`/`ELifecycleSeverity` |
| `Core/ControlPlane/Lifecycle/LifecycleEvents.cs` | 新增：转换事件名词汇 |
| `Core/ControlPlane/Lifecycle/ILifecycleDiagnostics.cs` | 新增：诊断出口（`Transition` / `TransitionOnce`）与默认实现 |
| `Adapters/Resource/ResourceExecutionPort.cs` | 新增：Resource 执行端口，把引擎的编排决策翻译成领域原生操作并翻译失败分类；不聚合需求、不持有租约或滞回 |
| `Adapters/Resource/ResourceLifecyclePolicy.cs` | 新增：Resource 的 Lifecycle Policy 声明（滞回取资源域常量；retry 节奏与原常量逐值一致） |
| `Adapters/Resource/ResourceLifecycleDiagnostics.cs` | 新增：Resource 诊断出口，把统一记录落成既有 `[ResourceObs]` 取证格式（事件名/路径/结果词表与迁移前一致） |
| `Adapters/Resource/ResourceProductionControlSeam.cs` | 已重写：薄门面（构造执行端口 + 注册引擎 + 转发）；对外行为面（会话/观察者更新/移除/时间推进/刷新/查询）签名与语义不变；`UpdateObserver`/`RemoveObserver` 返回类型由 `SpatialRelevanceDiff` 改为共享引擎的 `DomainDemandProjection`，收口票 02 具名的 `ToSpatialRelevanceDiff` Middle Man |
| `WhitelistTests/Evidence/StaticIL/LifecycleOrchestrationStaticILContractTests.cs` | 新增契约（7 项）：类型存在且 Lifecycle 命名空间零原生依赖、接缝零编排状态机、引擎只经端口触达原生状态、引擎消费 typed demand 不重新投影、Resource 自声明 Lifecycle Policy、全程序集唯一领域注册点、引擎无领域分支 |
| `WhitelistTests/Evidence/PureMemory/MultiObserver/LifecycleOrchestrationEngineTests.cs` | 新增（LOE01–LOE15）：见 §4 |
| `WhitelistTests/Evidence/StaticIL/IlContractProbe.cs` | 已扩：IL 计数入口接受 `MethodBase` 并纳入构造函数（原先跳过构造函数会让「唯一注册点」这类契约假绿） |
| `WhitelistTests/Evidence/StaticIL/ResourceProductionControlStaticILContractTests.cs` | 已改：代次读取契约指向 `ResourceExecutionPort.ReadRegionGeneration`；分类边界与取证 helper 契约改指共享引擎的 `ProcessEntered`/`ProcessSingleRegionEntry`/`DescribeFailure` |
| `SteamP2PFriends.csproj` | 已改：登记 9 个新增编译项 |
| `WhitelistTests/Program.cs` | 已改：注册 22 项新证据，目标数 313 → 335（测试工程用 `Evidence\**\*.cs` 通配，无需登记文件） |
| `docs/architecture/migration-manifest.md` | 已改：新增 Batch 14（变更清单、证据类状态、扰动负控制、不可变语义、审查链、判断项延期、票 04 移交） |
| `.scratch/collision-migration-slice/issue.md` | 已改：前沿 03 → 04；新增「票 03 → 票 04 的具名交接」段 |
| `.scratch/collision-migration-slice/issues/03-…md` | 已改：8 项 checklist 勾选、状态置 `implemented-pending-runtime` |

## 3. 红链与负控制（TDD 闭环，逐轮实测）

1. **RED-A（结构契约）**：先落地 `LifecycleOrchestrationStaticILContractTests.cs`（纯字符串 `GetType` + `IlContractProbe`，可对未改动的生产程序集编译）并注册 → 实测 **313/320**：新增 7 项结构契约全 FAIL，既有 313 项全绿。
2. **RED-B（引擎实现）**：`Lifecycle` 模块与 Resource 侧端口/政策/诊断同期落地，`ResourceProductionControlSeam` 重写为门面。此步**没有**先落骨架再补实现——引擎一旦拆分就必须同期迁移，否则票 01 表征门在中间态全红。如实具名该取舍：本票的「红」以 RED-A（7 项结构契约）与下述扰动形态取得，而非「先空实现后补」。
3. **GREEN**：迁移后实测 **311/320**，9 项 FAIL 全部为**契约指向待更新或测试夹具缺口**，逐项具名并修正：
   - `M6P35` 反射目标随 retry 登记迁入引擎（NRE）→ 改为两级反射（engine → per-domain state → retry 表），断言不变；
   - 4 项旧契约（代次读取无回退猜测、失败分类不解析异常文本、取证 helper 读 Message、单区域处理体零 `get_Message`）的断言目标随逻辑迁入引擎/端口，**逐项等价重定向**（断言强度不变，其中代次读取契约额外增加「接缝已无 `ReadGeneration`」的反向断言）；
   - 2 项新契约（单域注册点、引擎经端口触达原生）因探针实现缺陷（`SumOverDeclaredMethods` / `SumOverMethodsInNamespace` 跳过构造函数）而假绿，修正探针后转绿。
4. **新门禁红点取证（LOE01–LOE12）**：这些契约锁定的能力在实现落地时已存在，因此以扰动逐项取得红点（见 §5），而非以初始缺失取得。
5. **审查修复轮引入的新门禁（LOE13–LOE15）**：各自对应一条审查 BLOCKING，均在修复前以「回退该修复」实测红（注册闭合 → LOE13 FAIL；成功 Acquire 诊断 → LOE14 FAIL；熔断领域跳过 → LOE15 FAIL），修复后转绿。

## 4. 新证据清单

| 证据 | 锁定内容 |
|---|---|
| LOE01 | 引擎消费共享投影算出的 typed demand：两域共用唯一空间事实、各自拿到本域需求与租约 |
| LOE02 | 需求聚合：多观察者同区只 0→1 Acquire 一次；一人离开不释放仍被需要的租约 |
| LOE03 | N→0 只调度滞回释放；窗口内保留，到点提交；窗口取自领域政策 |
| LOE04 | 滞回窗口内需求回升撤销释放，且不产生第二次 Acquire |
| LOE05 | 区域代次回退时释放被拒绝并保留状态；代次前进重排窗口；一致后提交 |
| LOE06 | 会话边界清空 pending/租约/需求/事实；会话结束后引擎拒绝推进（失败闭合）；重复 `EndSession` 幂等；新会话重建 |
| LOE07 | retry 节奏取自领域政策（起步间隔内不重试、到点重试），成功即清除登记 |
| LOE08 | 单区域 acquire 失败只隔离该区域：其它区域照常建租约，失败区留登记，不整批回滚、不抛出 |
| LOE09 | 跨域隔离：一个领域的释放失败只影响该领域，另一领域的需求/租约/释放不受影响 |
| LOE10 | 复制阶段失败回滚：需求聚合、租约、投影贡献与原生副作用全部还原，异常原样抛回 |
| LOE11 | 释放提交幂等：登记与租约同时消失，再次推进/刷新不产生第二次释放 |
| LOE12 | 统一诊断：释放与调度记录带领域/区域/会话代次/区域代次；观察者贡献记录带观察者与连接代次、尝试与原因 |
| LOE13 | 注册闭合：会话前可注册、会话中拒绝（含新领域）、会话结束后重新开放、新会话再次闭合 |
| LOE14 | 成功 Acquire 可观测：结果、区域、会话/区域代次、观察者/连接代次、尝试次数与原因齐备 |
| LOE15 | 熔断领域不拖停其它领域（显式写入仍被拒绝，推进路径只跳过它并留去重诊断） |

## 5. 验证矩阵（静态门禁，全部实测）

| 门禁 | 结果 |
|---|---|
| 主 DLL Release Rebuild ×2 | 0 个错误 / 0 个警告（`warnaserror+`），两次指纹一致 |
| 测试 exe Release Rebuild ×2 | 0 个错误 / 0 个警告，两次指纹一致 |
| 全套测试（唯一入口） | **335/335 PASS** |
| `Tools/Verify-BuildFingerprintArtifact.ps1` | PASS（`INDEPENDENT_ARTIFACT_VERIFICATION_PASS`） |
| `Tools/Verify-EvidenceClassLayout.ps1` | PASS（`EVIDENCE_CLASS_LAYOUT_PASS`） |
| `Tools/Verify-Ticket09Documentation.ps1` | PASS（`TICKET09_DOCUMENTATION_METADATA_PASS`） |
| `git diff --check` | CLEAN |

### 扰动负控制（12 组，逐次还原并复核回全绿）

| 扰动 | 实测 FAIL |
|---|---|
| 引擎去掉滞回窗口 | LOE03、M6P02、M6P03、M6P37（4） |
| 接缝加回一个编排集合字段 | Lifecycle Seam Holds No State Machine 与合成门（2） |
| 引擎持有具体端口实现类型 | Lifecycle Engine Reaches Native Only Via Port 与合成门（2） |
| 引擎读取 Domain Id 常量 | Lifecycle No Domain Branch 与合成门（2） |
| 引擎内置默认 Lifecycle Policy | Lifecycle Resource Policy Declaration 与合成门（2） |
| 第二处领域注册点（模拟 Collision 接线） | Lifecycle Single Domain Registration 与合成门（2） |
| 改快一般失败 retry 起步间隔 | M6P39（1） |
| 引擎不再登记 acquire retry | LOE07、LOE08、M6P31、M6P32、M6P38、M6P39、M6P40（7） |
| 引擎跳过补偿执行 | LOE10、M6P13、M6P14、M6P17、M6P23（5） |
| 去掉注册闭合 | LOE13（1） |
| 去掉成功 Acquire 转换诊断 | LOE14（1） |
| 熔断领域重新阻断推进路径 | LOE15（1） |

其中「引擎不再登记 retry」与「跳过补偿」同时打到票 01 表征门（M6P31/M6P32/M6P38/M6P39/M6P40 与 M6P13/M6P14/M6P17/M6P23），证明资源已验收语义确实由共享引擎承载而非旁路。

## 6. 产物身份（可复现构建，两次 Rebuild 指纹一致）

| 产物 | SHA-256 | MVID |
|---|---|---|
| `bin/Release/SteamP2PFriends.dll` | `BA51753FC80E0F90547F46EF6BF73F733DC7E183B1DA0899A4CAFDAF663441FC` | `aa9e2ab1-1754-426c-9987-c1ba0367f17a` |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `BE7268B74AF43E775BF5F8EDB3271C372E8D5BA2EFB18FFD58320FA1AD2E1C20` | `753984f6-b506-4bda-8822-338539da1f8d` |

版本 `0.2.4.9`，插件 GUID `com.yu80rice.steamp2pfriends`，默认 Case-ID `SPF-0.2.4.9-Experimental-CollisionSlice`。**未创建 git tag、未发 GitHub Release。**

## 7. 票面逐项映射

| 票面要求 | 落地证据 |
|---|---|
| 引擎消费 typed Resource Demand | `LifecycleOrchestrationEngine.Observe` 调用共享投影引擎并消费 `DomainDemandProjection`；LOE01 断言两域共用唯一空间事实 |
| 共享引擎拥有需求聚合、0→1、N→0、滞回、身份校验、retry、补偿调度、诊断 | 上述八项全部位于 `LifecycleOrchestrationEngine`；接缝侧只剩转发（StaticIL + 扰动 P2/P8/P9 锁定）；LOE02/03/05/06/07/10/11/12 行为面锁定 |
| Resource 经 Domain Execution Port 操作原生状态，不扫描观察者、不重算需求、不持有第二套状态机 | `ResourceExecutionPort` 只转发原生操作与失败分类；接缝零泛型字段、零嵌套编排类型；`Test_SeamHoldsNoOrchestrationState` 锁定 |
| 可保留薄门面，原通用状态机退出生产权威 | 接缝 1037 行 → 薄门面；`IsLeased`/`GetDemand` 等查询改为引擎的每域查询 |
| 引擎消费 Session/Connection/Region 代次，不重新生成或合并 | 端口只有 `ReadRegionGeneration` 读取面；会话代次由 `BeginSession` 传入、连接代次由 `Observe` 传入；LOE05/LOE06 锁定身份门 |
| 01 表征行为保持 | M6P36–M6P41、SPI05、半径来源契约全绿；扰动 P1/P7/P8 反向证明这些门仍有咬合力 |
| Collision 仍不具备生产写入资格 | 全程序集唯一领域注册点位于 Resource 接缝构造函数内；Lifecycle 命名空间零 Collision 引用；扰动 P6 证明该门有效 |
| PureMemory、StaticIL、BuildArtifact 通过，标记 implemented-pending-runtime | §5；票面、`issue.md` 前沿、`migration-manifest.md` Batch 14 三处一致；本报告不宣称任何 Runtime PASS |

## 8. Seam gaps 与取舍（全部具名，无静默跳过）

1. **引擎与迁移同期落地，未先落空骨架**：拆出引擎的那一刻票 01 表征门就会在中间态全红，因此本票的绿链是「RED-A 结构契约（7 项）→ 迁移并修正契约指向 → 扰动取得行为面红点」，而不是「先空实现后补」。如实具名，不冒充「先红后绿」的完整形态。
2. **返回类型变更（已具名的票 02 延期项）**：`UpdateObserver`/`RemoveObserver` 由 `SpatialRelevanceDiff` 改为 `DomainDemandProjection`，删除 `ToSpatialRelevanceDiff` Middle Man。无生产调用方与测试使用该返回值（已核对），变更属票 02 审计 §8-2 具名的「待票 03 迁编排时一并收口」。
3. **`M6P35` 的反射目标迁移**：失衡残留由公开 API 无法构造，故把原先对 `_acquireRetries` 的一级反射改为对引擎每域状态的两级反射，避免新增「仅供测试」的生产 API。断言与语义不变。
4. **探针缺陷（工具侧，已修）**：`IlContractProbe` 的 `SumOverDeclaredMethods` / `SumOverMethodsInNamespace` 原先遍历 `GetMethods` 而跳过构造函数，注册与接线调用只出现在构造函数里，会让「唯一注册点」「不得触达某组类型」这类契约假绿。本票扩展为 `MethodBase` 并纳入构造函数；既有契约在纳入构造函数后仍全绿（Demand 命名空间零原生依赖等断言未反转）。
5. **诊断级别改为显式声明**：原日志级别不由结果类别唯一决定（「跳过」在 acquire 路径是错误、在过时退出路径是常规信息），因此 `LifecycleDiagnostic.Severity` 由发出转换的一方显式给出，而非从 `Outcome` 推导。
6. **诊断去重遵循既有模式**：`TransitionOnce` 的 key 仍是字符串拼接，与票 02 前的 `NoticeOnce` 键模式一致，未新增长期形态（判断项，见 §9-6）。
7. **补丁计数与登记不变**：本票不新增/不删除 Patch、不改 Harmony 元数据、不改协议与配置键。
8. **单一 Writer 未变**：`TryCommitAcquire` / `CommitRelease` 仍是唯一原生生命周期提交入口（既有 StaticIL 契约保持绿）；本票只改调用这些入口的编排层。

## 9. 判断性坏味道与存量缺陷（全部具名；双轴确认不阻断）

1. **`Report(...)` 的可选参数簇**（Standards round 1–4）：领域/区域/代次/观察者/尝试/角色/级别七个可选参数构成数据泥团；合并为上下文对象会把调用点变成 builder 链；延期。
2. **`AcquireAttemptCount` 为纯转发**（Standards round 4）：保留它使成功 Acquire 调用点的取值意图可读；若出现第二个消费方即应内联；延期。
3. **`AdvanceTime`/`Flush` 循环入口四行守卫重复**（Standards round 4）：仅两处、四行，再抽一层守卫不划算；延期。
4. **`IsRegistrationClosed` 与 `IsSessionActive` 读同一字段**（Standards round 3/4）：已用 XML 注释固定「闭合边界＝会话本身」的视角；若两者语义分叉必须拆开；延期。
5. **`Build` 与 `Report` 的构造形状仍近似**（Standards round 2）：派生逻辑已收敛到 `IsSharedPath`/`DeriveSeverity`，两者携带的关联字段不同，强行合并会退回第 1 项；延期。
6. **诊断去重键为字符串拼接**（Standards round 2）：沿用本文件既有键模式；延期。
7. **生产接缝现为薄门面（Middle Man 形状）**（Standards round 1）：ADR 0011 明确要求保留兼容门面，不构成违规；不延期——按规格保留。
8. **测试辅助的三级反射链**（Standards round 1）：为不新增生产 API 而接受的耦合；引擎结构再变时需同步该辅助；延期。

## 10. 双轴审查链（每轮全新实例，无延续、无复用）

| 轮 | 本轮增量 | Standards | Spec |
|---|---|---|---|
| 1 | 全量首轮（Lifecycle 模块 + Resource 端口/政策/诊断 + 接缝重写 + 22 项新证据 + 文档） | CLEAN（0 硬违规；5 判断项） | **BLOCKING 3**：Registration Closure 缺失；成功 Acquire 无转换诊断；RepairRequired 阻断跨域刷新 |
| 2 | 注册闭合 + 成功 Acquire 诊断 + 熔断领域跳过 + 枚举裁剪 + 诊断构造收敛 + LOE13–LOE15 | CLEAN（0 硬违规；新增 1 项重复代码、1 项命名副作用） | **BLOCKING 2**：注册闭合被过度收紧为永久闭合；成功 Acquire 诊断缺尝试次数与原因 |
| 3 | 闭合边界收窄为会话级 + 补齐 `attempt`/`reason` + 守卫拆分 + LOE13/LOE14 扩展 | **CLEAN**（0 硬违规；1 项重复代码待收口） | **CLEAN**（两条 BLOCKING 逐项闭合；未越界票 04） |
| 4 | `NextAttemptNumber` 抽出复用 + 删除有副作用的谓词 | **CLEAN**（0 硬违规；2 项新增判断项） | **CLEAN**（逐项「无差距」） |

每轮均为全新实例、独立上下文（未使用 SendMessage 续接任何上一轮实例）。round 4 双轴 CLEAN，审查链闭合。审查期间发现的**测试自身错误**（LOE06 在会话结束后仍调 `AdvanceTime`；假体把一次被拒绝的释放计入释放次数；注入的复制失败同时挡住了补偿用的重新进入）与**探针缺陷**（跳过构造函数）已在本报告 §3 具名并修正。

## 11. Runtime 判读口径（移交票 09）

- `0.2.4.9` 构建本身尚无三端运行日志；本票不宣称 Runtime PASS。
- 三端运行必须使用**正式切换候选**、三端同一 SHA-256；影子 DLL 日志不得当作该条 PASS。
- 票 01 表征门与票 02 投影契约在票 03 迁入编排引擎前后已保持绿；三端日志中 `resourceDemandRegions`（已物化租约）与 `resourceProjectedDemandRegions`（已投影需求）的分叉应能被解释为暂缓/失败重试路径。
- 本票新增运行时可观测点：引擎转换诊断经 `ResourceLifecycleDiagnostics` 落成既有 `[ResourceObs]` 格式（事件名、路径、结果词表与迁移前一致），三端日志字段口径不变；新增的 `LeaseAcquire ... success`（带 `attempt` 与 `reason=region-demand-entered`）用于在运行日志里直接看到 0→1 转换，不必再从租约计数反推。
