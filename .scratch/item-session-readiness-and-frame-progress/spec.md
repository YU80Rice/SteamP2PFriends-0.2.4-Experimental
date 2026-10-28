# Item-SessionReadinessAndFrameProgress 执行合同

## 1. 用户目标、证据与冻结边界

同一进程首次及再次世界，都必须准备当前地图自然ItemData，再以当前epoch/真实物理连接准入呈现；正常返区工作按有限帧预算推进，而非等待空间核对一秒一次或后来的自然补货。玩家可继续活动，不新增整区ACK/所有模型同帧门。

基线为 `5c496c0b3c8aa61ba825348bec637071759223c0`。这是已授权待唯一新builder领取的单票，A/B是内部验证顺序，不分玩家阶段交付。原五个tracked用户改动、无关untracked及根本轮记录均保护，不stash/reset/clean/stage-all；不恢复GBA/NIL旧builder。

引用而不重抄：[诊断](../matt-orchestrate/item-runtime-diagnosis-20261027-delayed-items/expert/diagnosis.md)、同run根证据索引/时序。实际Host是234815、Guest是234812；epoch1有全图生成，epoch3/5首次Guest各九区空基线，之后有从instanceID=1开始的增量。约50秒是Host两个入口的阶段间隔，不是可见延迟测量，空区可能合法。当前无玩家原症状自动化Red。

保留CONTEXT及ADR0019–0023/0026–0027：原生仓库/协议、全图数据准备、首次需求启计、返区同区共用、完成前不交旧结果、无人后有限收尾。单Writer、Core提交/回滚、GBA owner/transport/token/epoch、R01 pending/呈现、R02真实故障限额/Waiting期限、R03精确撤销、NIL引用判空、07距离均不放宽。不改Resource观测NRE、候选C模型消费者、僵尸/dropLoot/概率、LMN/ACK/reset/fault政策；若直接接缝证明不可分，先向根报具体依赖。

## 2. 用户故事

1. 作为Host，我希望新开世界立即使用本世界已准备数据，而不是把上一世界Ready带来、等自然补货。
2. 作为Guest，我希望首次合法加入和返区消费当前一致结果，不接收遗漏准备的空仓，也不新增半包误报。
3. 作为同区观察者，我希望共用一次结算，正常工作逐帧完成；另一人离开不重做或停止我的有效工作。
4. 作为玩家，我希望输入尚未建立时等待真正就绪，而非旧数据放行；不用为正常加载重连/手动命令。
5. 作为交付使用者，我只接收一份已自动化及双轴验过的独立DLL和短说明，Runtime由根统一安排。

## 3. 已核实落点与现存合同

| 现存位置（基线行号） | 现状/本票必要改动 |
|---|---|
| `SteamP2PFriendsPlugin.cs:218–278` | 主线程Update→TickMultiObserverShadowIsolated→coordinator.Tick；保留原异常故障边界，不新增第二MonoBehaviour/timer |
| `Core/ControlPlane/MultiObserverShadowCoordinator.cs:305–360,454–465` | Resource和Item共享同一engine；Resource先Begin；保留这一事实，不换独立engine规避 |
| 同文件 `BeginItemAuthoritySession:167–190` | 178–180用共享active提前返回，漏BindCoreSession/Item.Begin/Prepare；改为真实Item局部幂等 |
| 同文件 `Tick:472–525,1378–1381` | 477经Resource加一次共享时钟、519经ControlPlane又加一次；520/521/525三次别名Flush；525唯一Item工作入口在一秒门后 |
| `Adapters/Resource/ResourceProductionControlSeam.cs:63,89–92` | Begin/Advance/Flush均转发共享engine；不是Resource私有时钟，不修改其现存公共合同 |
| `Adapters/Item/ItemProductionControlSeam.cs:122–123,148–161,186–218` | Admission捕获epoch；Begin重建局部状态；Flush/Tick同时驱动engine和Item工作，需为生产拆出局部进度接口而保留原独立调用合同 |
| `Adapters/Item/ItemPreparationAuthority.cs:700–828,936–959` | PrepareFullMap/BindCoreSession已存在；IsRegionPrepared只看Ready+坐标；显式核当前准备身份，不能重定义全部旧IsReady调用者 |
| `Adapters/Item/ItemReturnSettlement.cs:388–438,441–523,949–970` | Refresh扫描世界需求；Consume预算常量1；Finish后交付。不把Refresh全图扫描移到每帧 |
| `Adapters/Item/ItemDemandCycle.cs:56–65` | Step含natural/respawn及dropped-expiry扫描；保持原低频调用，不随帧pump放大 |
| `Core/ControlPlane/Lifecycle/LifecycleOrchestrationEngine.cs:92–159,466–533` | 同epoch Begin幂等；AdvanceTime确实加共享和各域Clock；Flush不加Clock。无需重写引擎 |

## 4. A：当前世界准备与局部Admission一起初始化

以下为**拟新增**而非现存接口，命名可按约定微调，含义不得降级：

- Item seam加一个实例字段 `private SessionEpoch _initializedSessionEpoch;`，以及 `internal bool IsInitializedFor(SessionEpoch epoch)`。它仅标记局部初始化完成，不分配身份、不存observer映射；真实epoch仍来自共享Core。
- Preparation加 `internal bool IsPreparedFor(SessionEpoch epoch, string worldIdentity)`：IsReady且自身SessionEpoch/WorldIdentity相符。保留IsReady/IsRegionPrepared旧API含义与入口；新会话准入/帧消费用显式当前身份检查。
- 保留现存 `BeginSession(SessionEpoch)`/`EndSession()`；同当前局部epoch重复Begin不清jobs、manual cooldown、观察者或Admission。新局部epoch才ResetIdentity/建立当前Admission并最后标记完成；初始化抛错不得留下完成标志。End清局部标志和其持有的Admission投影。

coordinator最小顺序伪码（表达合同，不是已提交补丁）：
```text
选择/核唯一writer(epoch, world)；旧writer边界清理仍按真实共享epoch
prep.BindCoreSession(epoch, world)       // 新世界先撤旧Ready；同世界幂等
item.BeginSession(epoch)                // engine已被Resource启动也须完成Item局部初始化
若prep非Ready且非同身份Failed：尝试PrepareFullMap(当前epoch/world/已存在原生输入)
```

不保留“共享active即Item完成”的提前返回；也不能只改472的!IsReady而漏Admission。TryPrepare兜底按显式当前准备身份判断，输入晚到在同epoch继续准备，不重复初始化局部状态。输入缺失不得沿旧Ready Acquire/基线/本地消费；合法空区与缺输入仍分别处理。不得每次入区generate、重复首次启计或清真实故障求绿。

`BeginItemAuthoritySession:170–175`在旧writer与新epoch/world不符时还会调用Item.EndSession：实施者须核当前共享engine是否已由Resource切到目标epoch。不能为清旧Item局部态又结束已启动的新共享会话；必要时把局部清理放入新epoch Item.Begin，只有旧共享会话仍活动时才走原共享结束。保留旧世界真实清理/失败传播，不用无条件no-op避开。此为A直接调用者边界，须以跨世界组合验证，不扩为全Core重构。

## 5. B：生产必经的帧调度接缝

选择在**现有coordinator内**提取时钟/一秒门与帧尾，不造通用调度平台。拟议接口：

- `internal static void RunActiveFrame(float now, float deltaTime, Func<bool> reconcileCurrentObservers)`：由健康/身份可用/当前世界启动之后的Tick调用；拥有现存`_nextReconcileAt`的一秒门和唯一共享时钟推进，测试从这里穿过实际门。
- `private static bool ReconcileCurrentObservers(float now)`：承接原485–582的capture→admission→commit→三个域reconcile→共享Flush→审计/恢复记录；usable成功返回true，原capture-batch-rejected保留Defer后返回false。删除其中重复Advance及门面别名Flush/Item工作；异常原样回原插件故障边界。
- 新增一个coordinator布尔`_itemFrameProgressAllowed`：本会话已有usable核对才可帧推进；due核对前先置false，成功才true，拒绝/异常不置true。Begin/End/ResetManagedState清false。它是执行资格，不复制空间/连接身份；逐观察者准入仍由现存真实reader/结算检查。
- `AdvanceEngineClock(float deltaTime)`为拟新增私有重载；现存无参包装只取Time.deltaTime后调用它。经现有共享引用只加一次delta；健康帧由RunActiveFrame负责，故障/身份不确定早退分支沿原单次心跳推进、不得又进入健康帧pump。

Item seam仅拆出以下拟新增internal方法，均从旧StepItemWork提取同实现：

| 方法 | 工作/调用频率 |
|---|---|
| `void RefreshSettlementDemand()` | `_settlement.Refresh()`；空间核对成功后一次，及现有需求变更入口；不是每帧全图扫描 |
| `int ConsumeFrameSettlement()` | `_settlement.Consume(ItemReturnSettlement.ActionsPerStep)`；合格帧最多一次，返回实际已用动作；预算仍1，不清故障/不Advance共享clock |
| `void StepDemandCycle(bool settlementIdle)` | `_cycle.Step(settlementIdle, true)`；只在原一秒核对due且成功的帧一次；不把自然/掉落扫描增频 |

现存公开Flush/Tick保留其独立调用者原合同，内部可复用上述方法；**生产新帧路径不再调用门面Flush/Tick**，避免重复消费/共享Flush。原StepItemWork可作为这些方法的兼容组合，不复制第二算法。

最终RunActiveFrame行为伪码：
```text
AdvanceEngineClock(deltaTime)                         // 恰一次
due = now >= _nextReconcileAt
if due:
    _nextReconcileAt = now + ReconcileIntervalSeconds
    _itemFrameProgressAllowed = false
    if !reconcileCurrentObservers(): return
    _itemFrameProgressAllowed = true
if 非当前writer/局部初始化/当前prep就绪，或共享挂起/Item repair-required: return
if !_itemFrameProgressAllowed: return
if due: item.RefreshSettlementDemand()
used = item.ConsumeFrameSettlement()                   // 每合格帧一次，预算1
if due: item.StepDemandCycle(used == 0)                // 维持原低频周期资格
```

Tick的关闭/非活动、FaultBackoff、IdentityGate早退仍在此接缝之前；不得finally无条件pump。capture拒绝后不在随后非due帧偷偷继续直到下一次usable核对；另一观察者自身失败只由原局部gate隔离，不整域停帧。无需求但已启动有效job仍允许收尾，不能把`demand==0`加成整帧拒绝。会话/区域身份失效则沿原精确失效路径撤销，不能继续旧仓。

### 必需的时钟/共享Flush整理

基线477和519真实指向同一engine，核对帧会多加一份delta；520/521/525也会三次遍历共享Flush。这不是三个域的独立工作。生产改为：每帧一次Advance，共享Flush仍只在原核对阶段一次（其他域不新增逐帧Flush），Item局部Consume另走上述帧方法。保持`LifecycleOrchestrationEngine`和Resource门面的公共含义，不顺带改引擎重试策略或跨域算法。

用精确clock增量/实际滞回或retry边界证明总进度只等于输入delta之和，包含due/非due、挂起早退。Item失败控制器仍用既有真实时钟，不改起点/期限/次数/冷却；提高消费调用频率不得增加尚未到期的故障尝试。单次Finish给有效waiter的原交付合同保持，不另造广播或ACK预算。

## 6. 真实Red/同测Green与接缝准备

### 已有入口与可复用fixture

- `WhitelistTests/Evidence/PureMemory/Adapters/Item/ItemWriterCutoverTests.cs:104–140` 的IT91直接调用真实coordinator，但当前建的是独立Item engine，未覆盖Resource先Begin。保留原测试，增加组合覆盖。
- `ItemPreparationAndFirstDemandTests.cs:3558–3632` 的`TestNativeItemWarehouseBridge`、`OnSubmitGeneration`和实际ItemData集合可隔离仓库外部动作；不能用它复制准备/Admission/settlement算法。
- `ResourceProductionControlSeamTests.cs:17–30,1241起`有实际Resource seam构造与私有外部adapter fixture；构造器确有第7参`LifecycleOrchestrationEngine sharedEngine`。测试侧必要时仅调整fixture可见性/复用它，不新增生产fake API。
- `R01E02SliceATests.RawNative`（:99）与`FixtureHarness`（:1996）已有绑定facts/StageAndCommitIdentity；可复用验证真实`Admission.Admit/DispatchStepFive`，不是仅检查字段或注入Accepted枚举。真实SteamPlayer/transport合同继续由现存GBA01覆核，无须新native路由。
- 现存`ItemPreparationAndFirstDemandTests.Test_IT41_ReturnWaitsAndDeliversWithoutFailureBudget`（:1902）直接Tick，不能拿它替代B穿门证据。

### 拟新增测试（下列名称均非现存方法）

建议集中于拟新增 `WhitelistTests/Evidence/PureMemory/Adapters/Item/ItemSessionReadinessAndFrameProgressTests.cs`，在测试csproj显式Compile并在现存Program.Main逐项RunTest登记。可按相同承重边界合并断言，不泛增矩阵：

| 拟议方法 | 必经生产链/承重断言 |
|---|---|
| `Test_SFP01_SharedBeginPreparesEachWorldAndAdmission` | 同一projection/engine注册Resource+Item→真实Resource.Begin→coordinator.BeginItemAuthoritySession；首世界准备并当前Admission成功；End→换原生仓库输入→新epoch/world仍Resource先Begin→重新准备；旧bound request拒绝、当前请求成功，无第二engine |
| `Test_SFP02_LateInputsAndDuplicateBeginPreserveWork` | 世界变更先输入缺失，旧Ready不放行；输入恢复后同epoch完成一次准备、Admission不反复重建；重复Begin保留已提交观察者/在途job与本链预算；不吞Prepare异常 |
| `Test_SFP03_RealFrameGateProgressesBoundedSettlement` | 真settlement有12个过期自然项；调用生产RunActiveFrame，第一次due callback实际建立需求/job，此后20帧共约1/3秒、无空间变化；旧代码最多首帧动作，修后逐帧≤1动作、完成前零交付、完成后一次；不直接Tick代替门 |
| `Test_SFP04_SharedWaitersAndDepartedWrapUp` | 同job两观察者不重复生成/删除；精确一人退出不清另一人；都退出仍有限收尾且不发清单；再次进入从已收尾数据判断；旧epoch请求/旧退出不修改新仓 |
| `Test_SFP05_FrameCadenceClockAndFailClosed` | 同一真实shared engine钟增量=各帧delta之和，due/非due恰一次；capture/reconcile/共享Flush/natural cycle只due；每帧Consume最多一次；capture拒绝/抛错后非due帧、写挂起、Item故障/未准备均不偷推进，恢复usable才继续 |
| `Test_SFP06_FrameProgressRetainsFailureBudget` | 提高帧调用频率不使确认未发送提前claim、越次数/期限；纯Waiting不吃预算；旧真实失败Waiting不延长期限；未知仍隔离，另一个有效waiter可继续。使用生产adapter实际OriginalRan观测；真实reader等待边界保留当前候选GBA01覆盖，不能只手造SafeToRetry或枚举证明全部 |

A首Red应在未改生产基线上直接观察错误prep身份/生成次数/旧Admission拒绝。组合fixture不得预先手动Item.BeginSession、MarkReady或借用会自动Begin的Harness路径填好承重事实；RawNative只隔离绑定输入，当前Admission必须由被测真实入口建立。记录是PureMemory直接生产控制流证据，不称已自动重现屏幕延迟。已有IT91只测独立engine，不够；空callback/预设bool也不够。

B若静态Unity入口不能直接调用，允许**先作保行为提取**：将原一秒门、原时钟调用及原due回调原样移入拟议RunActiveFrame，生产Tick必经该方法，基线仍只有due时才调Item.Flush。用必要原测试及StaticIL映射证明提取无B行为改变，再让SFP03观察停滞Red；后改同方法消费位置等得到同测Green。记录版本必须称“基线5c496c0 + 已有A修复增量（如有）+ 保行为B seam + 新测试”，不能称原始二进制Red。不得先把门移好再补一个从未红过的测试。

`Func<bool>`仅隔离外部capture/平台；生产传`() => ReconcileCurrentObservers(now)`，测试callback执行真实engine/Item Observe与Flush，不复制gate/clock/settlement实现。必须有StaticIL/注册证据锁住 `Plugin.Update→Tick→RunActiveFrame` 与真实callback、唯一Item局部帧consumer、due自然周期；原有StaticIL若因提取需改，沿新真实调用链继续证明同一合同，不删除承重断言。

### 首个技术前置与停止条件

构建者先验证共享Resource fixture可在现有Framework runner构造、真实coordinator入口/Admission及新调度seam可达；保存所有改过的静态绑定并finally恢复，不能污染正式Main后续测试。A可先红后修，再作B保行为接缝与Red，最终只交集成候选。

若失败发生在Provider/Unity/NetReflection初始化、MethodInfo不存在或成员类型不符，保留具体first exception/loader链并停在该目标，不记业务Red、不先改Libs/游戏DLL/宿主。不要重建既有native宿主求安心，也不先扩第三native路由；真实必要的执行边界变化带证据交根/常驻指导。若原始共享Tick要求无法保行为提取，停止并给真实阻断及最小生产必经替代，而非复制调度器到测试。

所有Red/Green待运行。每条记录测试完整名、实际UTC/本地时区、HEAD+生产增量/seam基线、生产/测试MVID、命令、exit/bool/目标失败原因与完整日志路径。同测同断言Green；目标前环境失败不能与后来true拼作Red/Green。每次Edit≤60行，一次只插一个测试方法，自动连续推进。

## 7. 定向、完整正式Main与当前候选身份

本项目生产/Whitelist均为经典csproj，net472、C#10、Release AnyCPU；测试引用生产 `bin/Release/SteamP2PFriends.dll`，不是ProjectReference，须先生产后测试。命令合同（本轮规格不执行）：
```powershell
& 'C:/Program Files/dotnet/dotnet.exe' msbuild SteamP2PFriends.csproj /t:Build /p:Configuration=Release /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'production build failed' }
& 'C:/Program Files/dotnet/dotnet.exe' msbuild WhitelistTests/SteamP2PFriends.WhitelistTests.csproj /t:Build /p:Configuration=Release /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'tests build failed' }
```

定向可复用Framework反射调用现存internal静态bool测试，以下是**未来执行模板**，不是现存过滤参数/已跑结果。Program.Main目前无定向参数合同，不凭空用`--filter`：
```powershell
$a = [Reflection.Assembly]::LoadFrom((Resolve-Path 'WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe').Path)
$flags = [Reflection.BindingFlags]'Static,NonPublic,Public'
$p = $a.GetType('SteamP2PFriends.WhitelistTests.Program', $true)
$p.GetMethod('InstallBattlEyeTypeResolutionStub', $flags).Invoke($null, @())
$t = $a.GetType('SteamP2PFriends.WhitelistTests.ItemSessionReadinessAndFrameProgressTests', $true)
$m = $t.GetMethod('Test_SFP03_RealFrameGateProgressesBoundedSettlement', $flags)
try { $ok = $m.Invoke($null, @()); Write-Output "target=$($m.Name) result=$ok"; if ($ok -ne $true) { exit 1 }; exit 0 }
catch { Write-Error $_.Exception.ToString(); exit 1 }
```
以独立PowerShell进程/cwd仓库根运行并保存双流、exit，逐个替换拟新增目标方法。缺方法或prelude初始化异常即前置失败，不把exit1自动判目标Red；完整正式Main不以此定向替代。既有IT88/91/93、IT05/10/39/41/42/49、R01呈现/E02、R02恢复、IT143–149、NIL00–04、GBA01/等待与退出，以及M6P共享时钟/滞回和相关StaticIL按影响优先定向复用，不另跑无关矩阵。

新增SFP测试默认走Framework本地，外部输入隔离；不在新本地测试内再次嵌套执行GBA01或把一个目标结果借给另一个条目。当前正式Main入口始终 `WhitelistTests.Program.Main`，已存在脚本合同：
```powershell
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File WhitelistTests/run-integrated.ps1 `
  -EvidenceDir '<本次新run的绝对full-suite目录>' `
  -HostDll '<既有native-host/bin/Release/net10.0/IdentityProbe.dll的绝对路径>'
```

现成host为 `.scratch/matt-orchestrate/item-guest-binding-admission-20261027/native-host/bin/Release/net10.0/IdentityProbe.dll`。无需重建host源码；优先将该已验证输出及声明依赖闭包复制到本票新run的隔离host目录，再仅同步当前生产/测试到其**输出旁**`input/`，记录实际HostDll位置及源/目标MVID。不能只更新host工程源input却运行旧输出input；不覆盖旧host输入/证据，不扩依赖，不读旧日志作本轮通过。

保留 `Infrastructure/RegisteredTestExecution.cs` 的精确NIL01/GBA01两路白名单、父子MVID/方法token/runtime/exit/唯一协议校验；其余新旧delegate本地执行。完整Main所有原632及新增登记本次实际执行，有同一runId/连续ordinal/逐项runtime及结果。实际总数按登记清单给，不固定写638、不删旧项、不拼两个失败suite为PASS；无skip/fallback/常量true。确需第三native目标时先报告真实阻碍/维护方案给根，不擅自扩路由或框架。

每次生产/测试/执行输入变化更新受影响构建与身份；最后完整候选必须实际跑一趟新正式Main，记录真实时间/时区、HEAD及tracked/untracked候选清单、命令、exit、总数及stdout/stderr/ledger。仅文档变化且输入未变可复用已有效证据；旧632不代替本轮。静态IL只证明接线/包含修复，不能独自成为A/B业务Red或视觉PASS。

## 8. 验收矩阵

全部为未来执行合同，立票时没有新增PASS。

| AC | 自动化必须闭合 | Runtime/证据边界 |
|---|---|---|
| 1 当前初始化 | SFP01真实共享Resource→Item入口Red/Green，当前Admission可用、旧epoch请求拒绝 | 外部仓库可隔离；真实地图生成与可见性仍R04 |
| 2 跨世界/晚输入 | SFP01/02新仓重新准备、合法空区、等待输入恢复、幂等不清观察者/预算/job；不结束已启动的新Resource会话 | 原生scope变化可观测，不用旧Ready或补货冒充准备 |
| 3 真实帧进度 | SFP03穿过实际生产门，<1秒多帧推进，≤原每帧动作预算，非仅直接Tick | 不承诺所有区域/模型同帧或为大工作量硬编码毫秒SLA |
| 4 一致共享/离开 | SFP03/04无半结算清单、各有效waiter一次，同区共用，无人已启动job仍收尾 | 不重算随机历史，不取消已提交变化 |
| 5 节拍/时钟 | SFP05实际调用者接线与唯一clock、共享Flush、due采样/周期、失败闭合；相关M6P不退化 | 不扩大其他域调度频率，不使自然年龄/重试加速 |
| 6 原边界 | SFP06+当前GBA/NIL/R01/R02/R03/距离相关定向与全量 | loaded/networked/bound不等于完整交付；不顺带修C/NRE |
| 7 正式全量 | 当前全部注册逐项执行，同runId完整ledger与两真实child、当前MVID、真实exit | 历史632/619等不替代当前，无漏测或离线拼PASS |
| 8 可测单DLL | 同候选双CLEAN、限定提交、真实Release/IL与最终独立身份门、短安装回滚说明 | 不生成ZIP、不自动安装/发布 |
| 9 R04 | 当前旧症状本场未复现准确保留；本票体验效果待后续已审DLL实机 | 当前1H1G不能授1H2G/原06/07/僵尸PASS |

## 9. 审查、两失败门及最终DLL

新唯一builder登记真实Vela/native/model/effort、基线/保护差异及run，当前本票完整轮0。A/B必要内部Red与集成Green属于同票，不因换接缝抹历史。缺目标seam、初始化失败、冻结ADR/范围冲突可提前带证据求指导；不能反复换依赖/建宿主绕过。

每轮冻结完整候选（生产、测试、必要csproj/runner、票/spec、正式audit与准确交付状态）。两位全新独立并行**原生**Standards/Spec subagent均Codex `gpt-6-luna/high`，不用Vela独立窗、不续旧reviewer作新轮。身份由父实际SubAgentActivity的agent_thread_id及子session_meta/turn_context外部核验；子看不到自己ID不是代码finding。同轮来源澄清补证允许，不重开实例、不重复有效测试；四舍五入日志或送达回执不代验收。

- Standards：当前世界引用/epoch与局部幂等、无第二权威、scope、异常/回滚/精确退出；生产唯一帧入口、时钟别名去重、采样/自然周期频率、无新null风险；fixture复用/接缝无算法复制；判断性smell仅在正式audit具名延期后才不阻断。
- Spec：逐AC1–9及A/B各自真实Red→同测Green；基线+seam标识；多观察者/无人收尾/预算不扩大；输入晚到不沿旧Ready；当前全量双runtime身份和所有登记；单DLL与Runtime边界，不能要求审前已完成双CLEAN后的最终打包来制造循环。

两完整轮任一轴仍非CLEAN→冻结候选直接求原指导并同步根；保留原判、累计/窗口/成本，不自开第三轮。根登记一次新方案/恢复窗口后才按匹配路线继续；同根因再反复或越界回根。专家意见不代CLEAN。与旧GBA/NIL/R01–03失败历史分别记录，不混计也不删除。源码/测试/runner有效候选变化后才更新相关验证并重新冻结，不在审查中悄改文件。

双CLEAN后限定stage/commit本票文件，用户预存变更/无关untracked不加入。正式audit拟用 `audit/YYYY-MM-DD/Implementation-0.2.4.10-Item-SessionReadinessAndFrameProgress.md`，保留全部轮次与真实证据；交付记录为本票`delivery.md`，源码交付状态为implemented-pending-runtime。本轮规格作者不提前创建或修改这些正式记录。

生产仍net472/C#10/AnyCPU Release，基于本票已审当前源码。独立交付拟为 `artifacts/Item-SessionReadinessAndFrameProgress/<实现提交短号>-<时间>/SteamP2PFriends.dll`，不覆盖旧有效包、**不生成ZIP**。按实际Release输出核非空可读、版本/Case-ID/Cutover、TFM/PE架构及依赖；IL须包含当前准备/Admission初始化及分帧唯一调度，不能拷错旧DLL。net10宿主和测试材料不是插件运行依赖，不夹带到游戏目录。

最终DLL使用现存独立门一次记录其SHA/MVID：
```powershell
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools/Verify-BuildFingerprintArtifact.ps1 `
  -Path '<独立交付SteamP2PFriends.dll绝对路径>' -ExpectedCandidateRole Cutover -LogPath '<本run最终产物核验日志>'
```
保存实际来源实现commit、配置/构建命令/时间、生产测试MVID、最终DLL SHA/MVID/架构/共享依赖及独立门结果；引用该次结果，不为安心重复hash。若编译路径/输入/产物实际变化则重新确认其适用性，不能把旧hash放进新说明。审后业务代码变化须重新验证/审查；只填最终提交号/指纹不重跑无变化候选。

交付给玩家主链接只指向本DLL；短UTF8中文说明：两端关闭游戏→各备份旧插件→仅替换本DLL→核新指纹/UMM启动新世界→异常正常退出并恢复原备份。不要用旧fault会话验修复；不要求看BepInEx或注入故障，不部署/发布。

## 10. Runtime和结束条件

本轮1H1G旧stale/半包/共享停写未复现可保留为**该场**结论；新的进入物资区延迟尚未验收。玩家今晚测试结束不阻塞Agent获授权自动化，但不催其重测。由根拿本票已审DLL后安排：Host初区→Guest首次加入→同区与新区观察/交互→Guest退出Host继续新区→同进程再开世界Guest加入；分别记录数据就绪、队列/模型及拾取，不以bound宣告显示完成。

缺第二Guest的1H2G/原06多观察者、07距离运行项、僵尸击杀掉落继续OPEN/未确认；不随本票静态/实机局部结果关掉。若A/B修后仍有nonempty→pending→model延迟，C作为有具体锚的独立后续候选交根，不擅自扩大本票。

实施者最终一次UTF8 vtell根，交单DLL路径/来源commit/指纹、当前正式Main与双轴真实来源、安装回滚/R04留项；保存正文/message-id/回执，sent不冒充消费。不能停在“源码阶段完成”或交待审DLL。正常技术步骤连续推进到已验证单DLL；只有具体无法自主解决的权限/接缝/范围阻断才停根。
