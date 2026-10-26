# Item-GuestBindingAdmission：执行合同与最小实施蓝图

- Label: ready-for-human；Status: implemented-pending-runtime。单票完整集成保持原验收范围，不拆玩家可见阶段。
- 本规格原为实施前合同；其验收/执行假设已由[issue.md](issue.md)、当前审计及本票交付记录补充结果。当前代码/自动化和双轴已完成，R04 Runtime仍未执行。
- 本票沿CONTEXT、ADR0019–0023/0026–0027及原R02合同；正常路径自动交付，合法等待不当故障，真正未知不盲重发。

## 1. 用户故事与完成边界

1. Guest首次加入即按当前合法物理绑定收到物资，不因领域token尚未发布而收到九区半包警告。
2. 正常等待后自动续做未完成交付；不要求再移动、重连、命令或Host到场。
3. 旧连接/会话请求不发送、不修改新连接；真正未发送失败有限恢复，未知发送结果局部隔离，另一Guest/Host继续。
4. 用户最终拿到一个已验证的完整修复DLL/ZIP，按说明直接实机测试；不把源码CLEAN等同实机通过，不推定僵尸恢复。

## 2. 现存落点、签名与调用时点（不是拟议接口）

| 现存位置 | 实际合同/本票必要变化 |
|---|---|
| `Core/ControlPlane/MultiObserverShadowCoordinator.cs:966–980` | `internal static bool TryReadCommittedItemConnection(ulong observerId, out object owner, out object transport, out ulong token)`；从既有Connections只读返回，**没有epoch参数**。保留签名/账本，不假定它单独证明会话资格。 |
| 同文件`:502/518/1241–1254` | Tick先CommitConnectionIdentities，再ReconcileItemProduction→ObserveItemAuthority→seam.UpdateObserver。本票复用已提交物理事实，不再用未完成领域Observe的token结果作其binder前置。 |
| `Adapters/Item/ItemProductionControlSeam.cs:34–72` | 构造函数给真实nativeBridge的现有`Func<ulong,ulong,bool> CoreConnectionCurrent`接到领域TryGetConnectionGeneration；替换此绑定的来源/参数，保留其它settlement/step5的领域current门，不全局替换。 |
| `Adapters/Item/ItemPreparationAuthority.cs:258–278,490–498,509–519` | public BindGuestBaseline/UnbindGuestBaseline仍保留INativeItemWarehouseBridge入口；FindClient仍唯一实际查找、ReferenceEquals判空/SteamID值匹配不改。Bind拿到的真实client及transport必须参加物理同一性比较。Unbind只允许精确当前身份写loaded，防本区失败补偿/旧回调误清新连接。 |
| `Adapters/Item/ItemExecutionPort.cs:288–339,375–408` | 即时OnObserverReplicationEntered与结算后DeliverSettlementBaseline均进同binder；新增准入类别映射，不能让普通前置异常都默认半包。 |
| `Core/ControlPlane/Lifecycle/IDomainExecutionPort.cs:22–33` | 现有复制disposition只有Retry=0、Quarantine=1，**没有Deferred/Rejected复制结果**；EDomainFailureKind另有Deferred/Rejected。拟议扩展及映射见§4，不能声称现有返回值已支持丢弃。 |
| `LifecycleOrchestrationEngine.cs:332–385,966–1006,1063–1148` | 入区回调在ConnectionTokens提交前；Retry按ClassifyRegionFailure是否Deferred排AcquireRetries，非Retry返回。只允许必要的通用分类/诊断适配，禁止提前写token或加Item分支。 |
| `Adapters/Item/ItemReturnSettlement.cs:180–203,526–643,903–949` | Hold/Finish/ConsumeDeliveries/HandleDeliveryFailure必须一致处理本票等待/拒绝；已完成世界结算不重做、不增demand，不把等待加入真实失败预算。 |

现有committed读取调用者为`NativeItemStepFiveReader.ReadBinding:73–78`、`ItemLocalPresentationDispatch.ResolveCurrentHostPlayer:286–300`及R01测试。它们是step5/本地呈现时点，继续各自原门，不因新增binder读取而放宽。直接首次链为coordinator→UpdateObserver→engine.ProcessEntered→port.OnObserverReplicationEntered→coordinator.BindGuestItemBaseline→bridge；step5经DispatchStepFive/Admission→ObserveForNativeStepFive，同一port；返区/重连可Hold后由Finish/ConsumeDeliveries→DeliverSettlementBaseline→同bridge；退出经RemoveObserver→OnObserverReplicationExited→Unbind，旧owner/transport/token撤销链保持。

## 3. 首选生产接口与共享实现（以下均为拟新增/拟修改）

新增一个小的Item模块`Adapters/Item/ItemGuestBindingAdmission.cs`，生产csproj显式Compile一次。只放临时请求/快照值和准入判定，不建表、队列或第二身份源：

```csharp
// 拟议形状；字段可只读构造，不是当前已有类型。
internal enum ItemGuestAdmission { Accepted, Waiting, Rejected }
internal struct ItemGuestBindingRequest { public ulong Epoch, Observer, Token; public byte X, Y; }
internal struct ItemCommittedBinding { public bool SessionActive, Found; public ulong Epoch, Token; public object Owner, Transport; }
internal static ItemGuestAdmission Evaluate(ItemGuestBindingRequest request,
    object actualOwner, object actualTransport, ItemCommittedBinding committed);
internal sealed class ItemGuestAdmissionException : InvalidOperationException {
    public ItemGuestAdmission Admission { get; } /* 构造函数接明确类别/原因 */ }
```

bridge拟以`Func<ulong> BindingEpochReader`和`Func<ulong,ItemCommittedBinding> CommittedBindingReader`替换旧bool CoreConnectionCurrent；后者由coordinator ConfigureItemControlPlane接入，前者由seam读取其现有engine活跃SessionEpoch（非活跃返回0）。seam构造函数可在现有可选sharedEngine之后追加可选reader，仅对真实nativeBridge要求生产接线；fake bridge的旧调用保持可用，不给原用途加新前置。

coordinator拟新增薄`ReadItemCommittedBinding(ulong observer)`：组合既有`_itemWriterEpoch`、Ledger.SessionEpoch、ItemAuthorityWriterOwnsSession、ItemProduction活跃epoch与TryReadCommittedItemConnection。只有writer活跃、全局/Item epoch同源时SessionActive=true；快照Epoch为既有writer epoch，不新增epoch账本，不扫描Provider/分配token。请求epoch也须相同；snapshot失败不得Accepted。生产接线由StaticIL/实际调用证据锁定。public Bind在同步调用开始捕获不可变request.Epoch，再读取快照；排队作业仍先核原job.Session/Generation/资格，不把旧job重标当前epoch。无外部epoch参数的旧入口不伪称能识别任意伪造历史调用。

判定顺序：请求epoch/id/token为0、session不活跃/epoch不同→Rejected；同epoch尚无committed事实，或当前Provider玩家/transport尚未可读且无相反身份事实→Waiting；已确定token不同或非空实际owner/transport与committed引用不同→Rejected；只有三者同源且非空时Accepted。先判已知矛盾，不能用“对象缺失”掩盖已确定旧token。保留Guest原本非空transport要求，不改Host合法形态。

### 3.1 静态Unity边界：仅在native发送/投影端留一个薄seam

现有native bridge没有发送注入点。允许提取一个**必经的internal重载**，public Bind仅构造请求并调用它；其内部仍执行真实FindClient→真实transport→同一Evaluate→既有DispatchCandidateGuestAskItems。拟议签名：

```csharp
internal void BindGuestBaselineCore(ItemGuestBindingRequest request,
    Func<Action<SDG.NetTransport.ITransportConnection,byte,byte>> prepareSend,
    Action<SDG.Unturned.SteamPlayer,byte,byte> commitLoaded);
```

生产prepareSend只解析/校验现有manager/AskItemsMethod，返回调用同一AskItemsMethod.Invoke的委托；不在factory内发送。测试只替换这个外部发送端和loaded投影副作用，不替换FindClient或生产准入算法。Factory明确未发送的真实解析失败可用既有ItemObserverReplicationFailureException(SafeToRetry=true)传递；进入Dispatch后异常仍按false/unknown。不要catch全部native读取异常然后假称“尚未发送所以安全”——只标记确证的阶段。

伪代码：捕获request→FindClient/transport→读取同一committed快照→Evaluate；Waiting/Rejected抛携带类别的准入异常；Accepted才prepareSend；再DispatchCandidateGuestAskItems(() => sender(transport,x,y), () => commitLoaded(client,x,y))。保留原生反射异常/OriginalRan与loaded提交的unknown处理，不吞未知异常。

Unbind复用同一物理身份检查，只有Accepted才修改该client目标loaded；Waiting/Rejected不动别人的投影，不制造半包通知。可同样提取拟新增必经`internal void UnbindGuestBaselineCore(ItemGuestBindingRequest request, Action<SteamPlayer,byte,byte> clearLoaded)`，public Unbind调用它；原生loaded读取/写入位于生产clearLoaded内，测试只控制该外部动作并观察精确对象，不能复制身份算法。仍允许精确旧连接在正常断开移除顺序中、其身份尚是当前时完成原清理；不得先删coordinator身份再要求Unbind证明旧绑定。对象/数组读取的其它异常继续原传播与R03诊断，不用catch-all吞掉。

## 4. 准入与发送结果在engine、settlement的实际映射

| 类别 | 即时Observe→port | 已结算Finish/待交付Consume | 预算/通知 |
|---|---|---|---|
| Accepted | 同一native binder执行，成功才贡献/loaded | 同一binder执行，成功CompleteDelivery | 不增加失败预算/错误通知 |
| Waiting（准入事实尚不可用） | port.Handle返回**现有Retry**；ClassifyRegionFailure把新准入异常映射**现有Deferred**，走现有DeferredRetry/AcquireRetries，不调用ReportActualFault | 在**既有PendingDelivery/DeliveryWaiterKey**中保留delivery-only等待；不丢waiter、不重结算。见下段状态适配 | 正常等待不发半包/耗尽通知，不计真实失败次数；靠既有Tick/Consume推进并接受原身份失效清理 |
| Rejected（错epoch/token/物理引用） | 拟在复制disposition追加**Rejected=2**；port只记受控拒绝原因，不建半包门。engine返回前精确撤销本请求已有AcquireRetry（如有，沿原事务补偿模式可恢复），不排新重试 | Finish跳过此旧waiter，Consume移除该精确delivery/retry key；不标delivered、不触碰新代次/别的waiter | 无重试/半包通知；等下一真实有效样本按原准入推进，不重标旧请求身份 |
| 确认未发送的真实失败 | **现有SafeToRetry=true**→Retry+Failed | **现有QueueDeliveryRetry/FailedRetryController** | 原首次+最多3自动、最多10秒及原人工恢复资格/冷却不变 |
| 已进入发送/提交后结果未知 | **现有SafeToRetry=false/未识别异常**→Quarantine | 原unknown隔离并停止此观察者/区域续送 | 同链同区一次通知，保持完整覆盖离返区合同，不盲重发 |

`ClassifyRegionFailure`只对本票携带类别的异常区分Deferred/Rejected，不能按异常消息字符串猜。engine复制catch可复用该通用分类把Waiting/Rejected记成Deferred/Rejected而非Failed；不改变其它域默认Failed、补偿/事务提交顺序，不加Item-specific分支。新Rejected枚举只是让“无半包隔离的业务拒绝”有准确名字；不得把Quarantine返回但不隔离伪装成Rejected。现有ILocalObserverReplicationFailurePort签名不变。

settlement必要状态改动限定为既有PendingDelivery增加`AwaitingAdmission`（拟新增）及其消费分支，不另建调度器：Waiting在Finish保留同session/generation/observer/token记录；Consume先核原DeliveryIdentityCurrent，再按既有每帧budget尝试一次同binder。等待尝试不TryClaimAttempt、不ReportActualFault，不开人工预算；再次Waiting留记录，Rejected精确撤销，Accepted原CompleteDelivery。转真实confirmed-not-sent才登记首次实际故障并交原FailedRetryController，unknown原隔离。既有真实失败重试中遇Waiting时，不重复ReportActualFault、不得通过Waiting重置旧失败起点/冷却/预算；没有故障的正常等待与已有故障链分别保留事实，真正耗尽仍按原恢复合同处理。初始无jobs时也不能饿死pending delivery。

根采纳的等待通知时点澄清：Waiting可按原期限刷新已有真实故障链的`Exhausted`，但此时不claim、不通知、不改FirstFaultAt/次数/冷却。若事实恢复Accepted而该旧链已耗尽，原TryClaimAttempt拒绝自动续送并仅通知一次（`possibleHalfPacket=false`），之后仍须满足现有人工恢复资格；没有真实故障链的纯Waiting在Accepted后自动送达，不凭空耗尽。

为避免“先claim后发现正常等待”消耗次数，拟新增bridge `ReadGuestAdmission(ItemGuestBindingRequest, out SteamPlayer, out ITransportConnection)`，承载真实FindClient/快照读取/同一Evaluate，BindCore必经它；settlement只通过seam接入的薄`DeliveryAdmission(epoch,observer,token,region)`委托在**claim之前**复用此方法查询（Host分支不套Guest门）。Waiting只留等待，Rejected精确撤销，Accepted才按原TryClaimAttempt及同binder执行；不缓存permit跨帧，不创建第二套检查算法。binder仍在真正调用前复核，同game-thread串行无yield；若接缝实际存在重入导致claim后又Waiting，必须明确退回未开始claim的最小方案再实现，不能默记为一次真实失败。

另一个已核实预算依赖：现有`FailedRetryController.IsExhausted(key)`只读标志，时间到期只在TryClaimAttempt/RetryFailed里刷新。故为“旧真实故障链等待时不claim但仍守10秒”拟增一个小的`RefreshExhaustion(string workKey,float now)`方法：复用现有DeadlineReached/RetryLimitReached，只刷新既有非in-flight链的Exhausted并返回；不创建链、不增RetriesStarted、不改FirstFaultAt/NextAt。只由新等待分支调用，旧caller合同不变，补时间到期/零次数变化定向。这个必要相邻修改纳入本票范围，不通过静默延长旧失败窗口逃预算。

当某个waiter无效时只移除它，不清已完成世界结果/其他waiter。ReleaseObserver、ReleaseExitedRegion、ResetIdentity及RegionGeneration失效沿原精确键清理两种delivery状态；不缓存ItemData、不扩大需求、不为错误身份续送。`_firstObserverFailureKeys`是日志去重不是恢复授权；新Waiting/Rejected不得伪写其半包键。

## 5. 实施范围与禁止项

必要生产范围：ItemGuestBindingAdmission拟新增模块；现有ItemPreparationAuthority、ItemProductionControlSeam、ItemExecutionPort、ItemReturnSettlement、coordinator仅对应reader/接线、IDomainExecutionPort追加复制Rejected、engine通用分类/精确retry撤销，以及FailedRetryController上述无claim到期刷新。生产csproj只增必需Compile，不改TFM/C#、引用或依赖。若现有调用者证明还需相邻最小修复，先定位合同/调用者再纳入同票，不机械限一文件，也不开展全Core重构。

必要测试范围：现有Item PureMemory/StaticIL测试、Program的薄接入、拟新增runner模块/必要协议保障测试、既有host源的受控复用和Whitelist csproj Compile。本票形成完整候选，保护根基线快照中的五个用户tracked/无关untracked，不stage-all、不stash/reset/clean。不改SDK/Libs/游戏/稳定包，不清fault/repair-required，不增加失败上限，不改生成器/僵尸/概率/LMN/ACK/reset/07距离。

公开旧INativeItemWarehouseBridge.Bind/Unbind签名不变；new reader仅新native准入使用，旧fake桥无需提供新静态Unity输入。原有调用者外部异常与一致性保护不降低；R01呈现/owned pending及原committed物理绑定校验保持。

## 6. 第一个可执行Red与同测Green

**拟新增正式方法**：`ItemPreparationAndFirstDemandTests.Test_GBA01_NativeFirstObserveBindingAdmission`，无参static bool，正式Main登记；不是仓库已有方法。本票其余纯状态/分类/IL测试各自登记，不能只留单测脚本。

1. 先复用已验证x86 CoreCLR10.0.9 host的源码/依赖闭包，创建本票`native-host/`受控副本，保留旧run源码、输入与所有历史失败/Green证据。增加只允许GBA01的精确入口（§7），不改旧NIL01方法/输入/断言。当前host实有`Program.cs/Lookup.cs/IdentityProbe.csproj`，项目net10.0、RuntimeFrameworkVersion10.0.9、RollForward Disable、x86、UseAppHost=false；不是让生产改TFM。
2. 原生fixture首个前置核验：沿NIL01真实SteamPlayer/SteamPlayerID与Provider._clients保存/恢复方式；从实际引用DLL的继承层读取transportConnection可写成员，不能凭空假定字段名。现SDK显示SteamPlayer继承SteamConnectedClientBase，concrete TransportConnection_SteamNetworkingSockets为managed internal class，可用反射/FormatterServices只分配引用对象，不调用网络构造/服务。核设置后真实属性读回同一引用；找不到实际字段/类型、初始化或引擎服务失败即停本路线交根，不换fake类型/Libs/TFM、不回CLR4加载全游戏。
3. 必要的行为保持接缝准备先于业务修复：提取§3.1的BindGuestBaselineCore，**此时保留原领域CoreConnectionCurrent检查及原分类不变**。另拟提取`ItemProductionControlSeam.ConfigureGuestBindingBridge(DefaultNativeItemWarehouseBridge bridge)`，由真实构造函数的native cast分支必经调用；测试也调用同一方法，不手写一份旧lambda算法。新增reader参数可先保留未用于旧guard。记录这个版本为“基线+seam准备”，不谎称字节未改原DLL。检查public bind→core及构造→configure单一必经关系/原异常传播；不得准备时顺带修guard。
4. GBA01用现有TestNativeItemWarehouseBridge提供外部世界仓库/准备数据、真实ItemPreparationAuthority/ItemExecutionPort/DemandProjection/ItemProductionControlSeam和真实nativeBridge提供身份binder。constructor与fixture都走同一ConfigureGuestBindingBridge；port的guest binder调用真实nativeBridge.BindGuestBaselineCore，只有sender和loaded外部副作用受控，不复制FindClient/准入。nativeBridge的Provider列表包含真实client/ID/transport。
5. 仿R01 `FixtureHarness.StageAndCommitIdentity:2060`，用实际TryGetStagedConnectionToken→CaptureResult/CommitConnectionIdentities生成/提交真实owner/transport/token，不硬填自有token。fixture可反射取得现有私有Ledger，调用真实MultiObserverShadowLedger.BeginSession(string)取得epoch，再以相同epoch调用现有TrySelectItemAuthorityWriter、BindItemAuthorityProduction、seam.BeginSession；fake世界仓库先完成准备，避免启动游戏初始化。生产快照reader及TryReadCommittedItemConnection实际读回对应事实；不注入另一个“永远true”身份算法。独立child finally恢复Provider列表、结束所建session/释放writer/清本fixture身份；不在游戏进程操作这些状态。
6. 第一次`seam.UpdateObserver(guest,T,center)`时领域TryGetConnectionGeneration原本无条目，sender计数0。断言最终准入发送/loaded各一次、错误通知0、无halfPacket门、会话/另一观察者健康；sender回调只作为外部native askItems边界，按现有`NoteAskItemsOriginalRan(true)`报告本次实际进入的受控动作。修前真实旧guard在binder现场抛stale，动作0、通知/隔离出现，**让该方法实际返回false、宿主非零**。保存实际目标栈/日志，不能把fixture初始化失败当Red。
7. 在同一GBA01内继续断言同token新区、同SteamID不同owner、错transport/token/epoch各零动作，当前重连可交付、旧回调不改新投影；每个子场景有独立标签及失败原因。首场景Red在前，不用后续假失败代替。记录真实类型构造成功后才进入Observe的边界。
8. 仅此后实施准入来源/分类/等待状态修复；**同方法同输入同断言**Green。生产sender/loaded仅外部边界被控制，报告不称真实RPC/客户端完整交付已测。真实native public wrapper→core、Configure→reader、Bind/Unbind共用判定由StaticIL/调用证据补齐；纯内存时序测试只算状态证据。

首条计划命令（本轮未运行；host新binding模式是本票须先实现的入口，不是假称当前已有）：
```powershell
& 'C:/Program Files (x86)/dotnet/dotnet.exe' '.scratch/item-guest-binding-admission/native-host/bin/Release/net10.0/IdentityProbe.dll' binding
$bindingExit = $LASTEXITCODE
```

运行前先Release构建含GBA01及seam准备但尚未业务修复的生产/Whitelist，复制**同版本**到新host/input，设置生产/测试MVID与目标方法身份期望字段（§7协议），保存命令/时间/cwd/退出码/完整stdout/stderr/输入与候选diff。Green同命令，更新真实新MVID；不可拿旧88f119d8或旧8b9bf1fe副本测新源码。第一次Red是0→目标拒绝，不要求重新运行历史NIL01缺陷Red。

## 7. 必要runner维护：现在处理NIL-D01触发条件

本票明确新增第二个跨运行时目标GBA01，不把它塞入NIL01方法或偷偷扩大旧NIL01合同。重评结果：保留单个正式Framework Main/原全部登记与计数，把上一票约175行执行传输/账本从Program收束进**一个拟新增**`WhitelistTests/Infrastructure/RegisteredTestExecution.cs`，csproj显式Compile一次。Program只初始化、把原delegate交给它、原passed/failed计数/输出、完成时核必需路由完整；不再逐票增加native分支。

该模块职责是一次登记执行及其证据，最小接口拟为BeginRun/Execute(Func<bool>,ordinal,name,EvidenceClass)/AppendResult/ValidateCompleteness（返回真实结果记录，异常仍由原RunTest计FAIL）。保留原本地调用默认语义；正式跨runtime脚本显式启用时路由表固定两条：精确NIL01 MethodInfo→lookup；精确GBA01 MethodInfo→binding。其余全部原delegate本地Framework执行；匹配声明类型/方法/签名/模块，拒闭包，不按display name/结果选择runtime。路由表不是第二suite注册表，不能枚举Test_*重建Main或离线拼结果。

现有host Lookup只支持硬编码NIL01，不能直接声称能跑GBA01。本票host副本保留lookup原同测合同，新增binding精确白名单；可复用一个小的共同invoke/protocol函数，不允许任意Type/Method参数执行。每次child校验父期望生产/测试MVID、调用方法完整名/token、关联生产目标token、x86/10.0.9；lookup继续核FindClient，binding核真实BindGuestBaselineCore/准入目标。父校验同字段、exit及唯一bool结果；不能只取最后一行true。

ledger用本次正式Main runId+连续ordinal；每项当前delegate身份、真实runtime/本地或child、bool/异常、子exit、MVID/路径、开始结束/完整stdoutstderr路径对应。两个child分开日志，不沿用旧单NIL01日志文件名覆盖。要求两条必需路由各恰好一次、登记总数/顺序和实际结果逐项闭合；数字按实际新增注册报告，不硬写624/不删旧项。父/任一子失败或ledger失败则整体非零。

在Framework内为提取后的纯协议校验增加必要小测试：false、非零exit、缺失/重复协议、错MVID/方法/token/runtime均拒绝；超时用受控process边界证明超时走FAIL，不等60秒制造慢矩阵。这些是runner保障，不是业务Red。无fallback、skip、常量true、读历史Green。注册/断言不移植到CoreCLR，Harmony/旧E02不改；只维护已证明必要的双运行时边界。

NIL-D01在本票audit写“本次第二目标触发重评，抽离为单一职责执行模块/显式两路白名单”的实际处理及残留成本，不改上票audit、不继续沿用单目标延期理由。host只复用原声明依赖闭包；仅真实新首错才论证最小增量，需引擎/native服务或不能保持相同真实类型/断言时硬停回根。

## 8. 验收矩阵（合同保留；按当前证据标记完成边界）

AC1–AC9的源码/自动化部分已按本表与audit通过；真实完整RPC、三端表现及AC10的Runtime仍须R04，不由632/632或双轴CLEAN替代。当前DLL/ZIP、版本身份、指纹及安装回滚说明已交付。

| 编号/场景 | 必须观察的行为 | 自动化/实机边界 |
|---|---|---|
| AC1 首次合法Guest | GBA01真实Observe→native查找/生产准入旧guard Red→修后同测Green；错误提示0、可推进、实际epoch/物理三元组匹配 | 外部sender/loaded受控，真实RPC完整交付仍R04 |
| AC2 同token新区/step5 | 新区binder通过；step5捕获epoch及R01 committed物理绑定门不退化；不因领域提交时点额外半包 | 当前seam+IL/定向；实际模型由R04 |
| AC3 错epoch/token/owner/transport | sender/loaded动作0；旧请求不改当前/另一Guest投影，不排旧身份重试、不通知半包 | 真实native引用+同生产Evaluate，及旧回调清理测试 |
| AC4 合法等待 | 无真实故障链时事实补齐自动续送；旧故障链Waiting不通知/不改期限或次数，超期仍标exhausted；Accepted后拒绝自动续送、单次耗尽通知，合格人工恢复才可送 | GBA01真实coordinator读取/Commit切换及生产settlement、真实未发送链→等待→10秒后到期→Accepted→TryManualRecovery；GBA03/GBA08作状态补充 |
| AC5 重连/退出 | waiting-settlement后只当前连接交付，旧token/epoch delivery精确revoke，旧退出回调不清新绑定 | 保留IT143–149；GBA01真实SteamPlayer/transport A/TA→B/TB，B正向loaded投影、旧Unbind及旧disconnect均不得清B |
| AC6 明确未发送 | adapter.NoteAskItemsOriginalRan(false)或明确pre-send准备失败，按原限额/冷却恢复；不重结算/不重抽 | 生产wrapper+即时/settlement两入口；不得手造标志代替全部验证 |
| AC7 未知发送 | 真实受控invoke开始后抛错、未观测、loaded提交失败仍隔离；同链同区一次，不盲发 | wrapper真实分类与实际通知gate；九区各一次≠同区重复 |
| AC8 不连带 | 另一Guest/Host可继续；R01 pending/呈现、R02共享结算、R03精确撤销、NIL00–04与07距离保持 | 定向保留+当前全量，不重新宣称旧历史根因 |
| AC9 Runner完整 | 原624及全部新增正式登记本次实际执行；两条精确child路由、其余Framework；反协议/子exit/身份拒绝保障 | 一趟新Main生成新ledger；旧两次失败/旧624不拼接或替代 |
| AC10 集成可测包 | 当前构建、双轴CLEAN、限定提交、独立有效DLL及最终ZIP/清单/安装回滚说明齐备 | 新候选已交付、Runtime仍pending；不自动部署 |

回归优先复用现有IT20、IT133–144及IT145/146/147/148/149、R01E02SliceATests、NIL00–04和相关StaticIL，不新增无关跨域矩阵。构建者记录实际命令/开始结束与时区/HEAD+工作树候选/真实生产测试host路径和MVID/exit/通过数/全stdoutstderr；Red失败必须指向目标guard，不把loader/native初始化失败算Red。截图/grep不是测试，预检不是业务PASS。

## 9. 当前构建与完整正式Main

现存项目仍net472/C#10；测试引用bin/Release生产DLL而非ProjectReference，必须先生产再测试。下列为立票时记录的真实命令模板；实施中按它构建，当前Main和每条命令/exit证据见本票正式审计及`.scratch/matt-orchestrate/item-guest-binding-admission-20261027/`：
```powershell
& 'C:/Program Files/dotnet/dotnet.exe' msbuild SteamP2PFriends.csproj /t:Build /p:Configuration=Release /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'production build failed' }
& 'C:/Program Files/dotnet/dotnet.exe' msbuild WhitelistTests/SteamP2PFriends.WhitelistTests.csproj /t:Build /p:Configuration=Release /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'tests build failed' }
# 更新新host input到同候选，必要host源码改变才构建该host；详记实际命令/exit。
```

拟新增本票脚本`run-integrated.ps1 -EvidenceDir <绝对新目录> -HostDll <新host的绝对IdentityProbe.dll>`：只在当前进程设置已记录的分运行时配置，cwd仓库根，启动原`WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe`，不移动该exe；并行保存双流，保留Main真实exit，验证实际ledger/完整登记/双child结果后返回0或1。保留既有SPF_NIL01_*配置调用的兼容性，若新增通用命名只作显式兼容别名，不默默失去NIL01分派。脚本不是当前已有参数合同，须由本票实现并在audit记录最终确切命令。

每次生产/测试/host或断言改动后，重建受影响输入并同步MVID；最终完整候选必须重新跑一次全Main，不复用历史619/624。支持日志须明确“Framework本地N项+CoreCLR x86两项”，而非单CLR全套；各正式登记真实结果与总数闭合。失败保留原日志，不通过改断言/跳过条目/移植Harmony来求绿。测试或生产改后旧验证不冒充当前；仅文档补正且执行输入未变可复用适用验证，不为安心再跑全套。

## 10. 冻结审查、失败门与交付

新唯一构建者领取时登记真实Vela/native/model/effort、保护快照及本票范围。派审前冻结完整候选（生产、测试、runner/host、票/spec/audit/交付状态），预先写清seam准备与真实Red来源、NIL-D01已触发处理、原生/实机缺口，避免审中改文件制造快照不一致。

每轮两个全新并行原生Standards/Spec子代理，明确gpt-6-luna/high，不用Vela独立审查窗、不续聊当新轮。Standards核准入物理引用/epoch、范围、补偿/异常、等待与预算、runner职责/协议/新null风险；Spec核AC1–10、真实Red同测Green、当前完整登记/身份、旧门与Runtime边界。保留父实际SubAgentActivity及子session_meta/turn_context/完整原文，不能以saved模型或Vela ID代替运行来源。两轴同候选CLEAN才交付。

本新票完整轮从0起；上票历史不混计、不删除。连续两完整轮任一轴阻塞→构建者冻结并直连现有指导，同时同步根；缺正确seam/冻结决策冲突可提前带证据求助。新方案匹配后由根登记一次窗口/预算，历史不归零；同根因再反复或越界停回根。专家蓝图/receipt不是CLEAN，禁止无限新开窗口。

双CLEAN后限定stage/commit本票文件，不包含用户五个预存改动/无关untracked。使用实际Release输出与既有`Tools/Verify-BuildFingerprintArtifact.ps1 -Path <实际交付DLL> -ExpectedCandidateRole Cutover -LogPath <日志>`；Case-ID沿Build/Version.props当前合同读取，不硬造新身份。核实际非空可读DLL、包含本票准入/分类IL、版本/文件版本/程序集版本、net472、实际架构与共享依赖；宿主net10及测试文件不得随插件当运行依赖。

拟交付`artifacts/Item-GuestBindingAdmission/<实现提交短号>-<时间>/`及`Item-GuestBindingAdmission-<实现提交短号>-final.zip`，不得覆盖旧可用包或留多个不明“最终版”。按用户明确产物要求计算最终DLL SHA256、MVID，记录来源提交/构建时间配置命令/Case-ID/Cutover/依赖；核ZIP实际存在、可打开、内含同一DLL与完整清单/说明。若审后生产/测试变化必须重验/复审；仅提交号/最终hash记录收尾不重复无意义验证。

说明用简体中文：关闭游戏→各端备份旧插件→只替换本DLL→核包身份→UMM新会话；失败退出后恢复备份，不在旧fault会话验证。测试包绝不自动安装/部署/发布。audit采用`audit/YYYY-MM-DD/Implementation-0.2.4.10-Item-GuestBindingAdmission.md`，票/delivery/索引准确写implemented-pending-runtime；不得改写上票audit或原R04判定。

## 11. 实机留项与停止点

本规格冻结时，R04由根在DLL/ZIP交付后安排。当前独立测试包已交付，R04 1 Host+2 Guest待玩家执行：Host初区拾取→Guest 1首次加入→Guest 2加入同区、双方拾取并丢下→Guest 1远区后退出而Guest 2留场→Host新区→Guest 1重连→Host新开世界、Guest 1首次加入。记录区域/操作顺序，玩家只游戏操作和UMM诊断包，不读BepInEx；包内README及R04票已有步骤。实际发送、接收、模型/拾取与提示分别判定，不能以loaded/networked或Prefix send作完整交付。真正异常分类仍需根审日志，不要求玩家故障注入。僵尸击杀/掉落单列记录，不随本票自动PASS；07距离及原其它Runtime门不抹除。

下列事项是立票时标注的未运行前置，后续已在当前候选完成对应验证：transport真实成员/对象、真实Observe→binder首Red、reader epoch事实、旧失败预算等待语义及双目标宿主协议；Red/Green、G1和正式Main证据见audit及本票builder记录。它们不再是交付阻塞。若未来实机出现需引擎服务的新缺口、冻结ADR冲突或当前调用者无法证明旧请求会话来源，仍按本规格停在具体入口/证据交根；不猜字段、不扩大deps/全Core/no-op吞错、不催玩家补技术说明。

无新的玩家玩法/预算选项需用户决定；本票技术路径已选。构建者可在上述职责/同一生产算法边界内作局部命名/签名调整并记实据，不需逐片问根；真实范围/授权/冻结决策冲突才回根。准备接缝、首Red、功能修复、runner整理都是同票内部顺序，不形成分阶段用户交付。最终一次vtell根交DLL/ZIP路径及SHA/MVID/来源提交、当前全部回归与原生双轴来源、指导历史、安装回滚及Runtime留项，保存正文/ID/回执；收到前不称已消费。
