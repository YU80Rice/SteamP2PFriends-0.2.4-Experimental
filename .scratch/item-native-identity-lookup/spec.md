# Item-NativeIdentityLookup 执行合同

- Label: ready-for-human
- Status: in-progress
- 基线：`fb2acbe31ceb786f3dd9e2dbebce185cc700ac9b` / `codex/structure-baseline-0.2.4`
- 版本：0.2.4.10 Experimental；实施负责人 Vela `08ddf56c-a3ee-41f1-a275-4ed3e0e06ad7` / native `01a173f7-c7f6-7031-a4ae-9a99bbe74afe`。用户于2026-10-26恢复同票实施、验证、审查、限定提交和独立测试包交付授权；部署与R04实机验收仍待后续。
- 根：Vela `0249e965-1143-4b6b-96f9-67484a145008`；指导：`6c55c1a2-e8b8-4f78-90d8-f3f63ca713b7`，Codex gpt-6.1-sol/high。
- 相对路径除链接外均以仓库根为准；后续实施先核真实工作树，保护用户tracked/untracked及根的R04记录。

## 1. 目标、证据与禁止项

有效Guest的原生ID查找不得因引用判空调用 `SteamPlayerID.op_Equality/op_Inequality`。空容器、空ID、缺失目标安全拒绝，非空条目仍按steamID值匹配；首次绑定与补偿解绑必须共用修后的真实查找。owner/transport/Core token、原异常传播、一致性保护及生命周期合同保持。

证据复用，不重做DLL分解/哈希或跨端归属调查：

- [本轮诊断](../matt-orchestrate/item-runtime-diagnosis-20261025-2329/expert/diagnosis.md)：Host实际补偿stack落FindClient；首次SnapshotEnqueue首抛点仍是推断，僵尸来源未确认，Guest同场归属未严格闭合。
- [Host真实类型反编译](../matt-orchestrate/item-runtime-diagnosis-20261025-2329/expert/host-game-SteamPlayerID.decompiled.cs.txt)、[模块身份](../matt-orchestrate/item-runtime-diagnosis-20261025-2329/expert/host-game-metadata.json)、[行号摘录](../matt-orchestrate/item-runtime-diagnosis-20261025-2329/expert/evidence-excerpts.json)。
- [根核对故障链](../matt-orchestrate/item-runtime-diagnosis-20261025-2329/inventory/root-confirmed-first-failure-chain.txt)。原草案保留，根负责引用衔接。

只允许ItemPreparationAuthority内判空/直接查找及其必要接缝与回归。不得重构生成器、改僵尸/dropLoot/概率、清fault或repair-required、扩大重试、绕过门、吞异常、修改SDK运算符、新增身份表/仓库/Writer/协议。不能把“新候选未再触发此错误”写成“旧故障进程已恢复”。

## 2. A/F：精确修改表与同类型比较清单

下列为当前基线真实接口；带“拟新增”的名称不是已有接口。

| 现存位置 | 危险/现有行为 | 最小意图、直接调用者与边界 |
|---|---|---|
| `Adapters/Item/ItemPreparationAuthority.cs:509–517`，`DefaultNativeItemWarehouseBridge.FindClient(ulong)`，private static | 515：`sp?.playerID != null && sp.playerID.steamID.m_SteamID == observerId` | 捕获id引用，使用object.ReferenceEquals判空；Provider.clients空返回null、遍历顺序/首个匹配返回及steamID值比较不变。 |
| 同文件258–278，`BindGuestBaseline(ulong,ulong,byte,byte)` | 262直接调用FindClient，随后client/transport/Core token/manager检查及askItems分流 | 必须仍走同一查找，不默认修改此方法；零ID/token、缺对象、transport及Core身份门/异常保持。 |
| 同文件490–498，`UnbindGuestBaseline(ulong,ulong,byte,byte)` | 493直接调用FindClient，之后安全读取loadedRegions并清目标标志 | 必须仍走同一查找；observerId=0早退、缺玩家/投影不写、边界检查与其它异常传播保持。 |
| 同文件：其余ID相关`== null`/`!= null` | 定向核对未发现第二处SteamPlayerID直接空比较 | 不把SteamPlayer、数组、ItemData或ulong的比较当作本缺陷批量替换。 |
| **范围外** `Adapters/Item/Patches/ItemRecoveryCommandPatch.cs:234` | `player.playerID == null` | 另票/根后续决策；本票不修，不推荐命令绕过当前fault。 |
| **范围外** `Adapters/Item/ItemLocalPresentationDispatch.cs:316/327` | `owner?.playerID == null`、`client.playerID == null` | 属TryReadBoundGeneration旧helper；其调用者ReadStepFiveFacts当前无生产调用，不修/不清理。 |

本清单仅覆盖本文件和上述已知直接相关Item位置，不是全仓null审计；实施若发现新的必要范围变更先回根，不默默扩票。

后续授权写入清单：生产仅`Adapters/Item/ItemPreparationAuthority.cs`；测试复用第4节两份现有测试源及`WhitelistTests/Program.cs`，必要证据/票据记录由根的构建派发明确。表中其它生产调用者默认只读验证，不因列入矩阵就获得修改权；不得改csproj/配置或范围外清单。

## 3. B：语言、真实类型与最小样板

- `SteamP2PFriends.csproj:13/15/368`、`WhitelistTests/SteamP2PFriends.WhitelistTests.csproj:13/15/143` 均目标`.NET Framework v4.7.2`、`LangVersion=10`，导入Microsoft.CSharp.targets。当前可用dotnet SDK清单为`10.0.400-preview.0.26322.102`，历史构建日志为.NET MSBuild18.9；本轮没有调用编译器构建。
- `is not null` 是C#9语法，项目声明C#10支持；本票仍首选显式`object.ReferenceEquals`，不改语言/框架配置，不要求升级编译器。目标框架不决定运算符重载选择。
- 原生`SteamPlayerID`是class，SDK `Provider/SteamPlayerID.cs:136–143`和已保存实际游戏文本均显示`==`解引用两端steamID，`!=`取反。`sp=null`时`sp?.playerID`得到null，**其后比较仍执行重载**；非空ID比较右侧null同样危险。

首选直接修复意图（伪码；本轮不写生产）：

```csharp
SteamPlayer sp = Provider.clients[i];
SteamPlayerID id = sp?.playerID;
if (object.ReferenceEquals(id, null)) continue;
if (id.steamID.m_SteamID == observerId) return sp;
```

只改变引用判空，不改value match或异常策略；不把它替换成另一种仍重载的`id == null`。若走第5节条件接缝，匹配谓词只能有一个生产实现，不能同时留一套fixture算法。

## 4. C：现存测试入口、DLL选择与前置验证

| 已存在的入口 | 实际合同/用途 |
|---|---|
| `.scratch/item-runtime-repair-20261023/run-single-test.ps1` | 必填`-Type`短类名、`-Method`方法名；可选`-LogPath`。自动前缀`SteamP2PFriends.WhitelistTests.`，先装现有BattlEye类型解析桩，反射调用无参static方法。只有返回bool true才exit0，其余exit1；不支持传完整namespace或测试参数。 |
| `WhitelistTests/Program.cs:16–30,842–897` | Main不读取args做筛选，无`--filter`合同；全量runner无参数运行。`RunTest`捕获异常/false记失败。新增正式测试须注册，不能只有单测脚本可见。 |
| `WhitelistTests/Evidence/PureMemory/Adapters/Item/ItemPreparationAndFirstDemandTests.cs:13,15,23,3021` | 现存测试类、`CreateTicket/CreateTestPolicy`、`TestNativeItemWarehouseBridge`，可复用控制面fixture。后者3112–3115仅记录bind/unbind调用，**不能**当作原生FindClient或重载的覆盖。 |
| 同文件`Test_IT20_WarehouseBridgeExceptionPropagationAndIdentityCheck`:674–711、IT145:2382–2415 | 异常传播/缺失client、真实remove seam诊断；均不覆盖非空真实原生条目查找。 |
| `WhitelistTests/Evidence/StaticIL/IlContractProbe.cs:19/25/78/139` | 现存`CountMethodCalls/CountCallsWhere/CountExceptionHandlers/CountOperands`，供真生产IL引用检查；不造新反编译/测试平台。 |
| `WhitelistTests/Evidence/StaticIL/Adapters/Item/ItemExecutionStaticILContractTests.cs` | 现有StaticIL测试类，可加本票调用链与危险运算符断言。`Properties/AssemblyInfo.cs`已给WhitelistTests内部访问权。 |

**DLL适用性：** 生产引用`../Libs/Assembly-CSharp.dll`，Whitelist引用`../../Libs/Assembly-CSharp.dll`且Private=True，两者指同一文件；测试插件来自`../bin/Release/SteamP2PFriends.dll`，不是ProjectReference，因此每次先构建生产再构建Whitelist。编译引用MVID=`dc7bb31c-d2f3-4609-950a-d545f6f7347c`，现场Host游戏MVID=`54793ea7-706c-40bf-9e5a-e3a126378d7c`。已存`expert/local-reference-SteamPlayerID.decompiled.cs.txt:106–135`与Host文本147–155证明两者相关构造/危险比较语义一致，不宣称DLL相同。未来测试输出实际加载Assembly-CSharp路径/MVID及插件路径/MVID；使用现有引用，不擅自替换Libs/安装Unity。真实类型无需替身；若实际无法加载，按下述停止条件处理。

**首个前置验证（拟新增临时定向方法`Test_NIL00_NativeFixturePreflight`）：** 在现有测试类中先确认真实SteamPlayerID构造/属性/运算符可用，再尝试以真实SteamPlayer条目装入受控Provider.clients、反射调用既有private FindClient。保存/最终恢复Provider._clients等实际更改，不在游戏进程操作；每次使用脚本独立PowerShell进程。必要反射字段须按真实类型核对，不能假定SDK私有字段必定存在于引用DLL。

使用真实ID的现存构造形状为`SteamPlayerID(CSteamID,byte,string,string,string,CSteamID,byte[][])`；可构造两个不同id、相同id的不同引用。SDK SteamPlayer的`_playerID`/playerID在35–36行，Provider._clients/clients在478–480行；这些是前置核对对象，不是已验证可用fixture。历史`expert`所引用`messages/r3-host-resolver-red.log`实有SteamPlayer→NetReflection静态初始化/ReflectionTypeLoadException（设置字段时触发），不准确统称为“已证Provider cctor失败”。该日志对应的旧测试方法已不在当前测试源中，不能作为现存可执行入口。

**停止条件：** 任何TypeInitializationException、ReflectionTypeLoadException、ECall、方法/字段找不到或依赖缺失发生在目标查找前，均为fixture前置失败，不算业务Red。完整保存inner/loader异常与是否已进入FindClient，停止这条原生执行路线，不扫装依赖、手抛NRE或默换fake。NIL00仅定向探测，不把确定失败的环境探测注册为永久业务验收门；后续转第5节接缝决策，或带证据回根。

## 5. C/D：真实Red→同测Green与条件接缝

所有下列NIL方法均**拟新增、当前不存在/未运行**；放入第4节的既有PureMemory或StaticIL测试类，每次只插一个方法≤60行，正式断言注册Program。编号不冒用既有IT；落地时再核名称冲突。

### 5.1 任何生产改动前的基线

1. 先仅增加测试：拟新增StaticIL方法`Test_NIL02_LookupDoesNotCallNativeNullOperators`，读取当前FindClient真实IL，要求null guard不调用**SteamPlayerID**的`op_Equality/op_Inequality`；当前515行为应产生目标Red，保存调用成员证据。只禁止该类型的空比较，不能误禁CSteamID/ulong正常值比较。缺方法/IL解析失败不算目标Red。
2. NIL00若可构造真实条目，则拟新增`Test_NIL01_RealNativeLookupContract`直接反射调用原private FindClient，先跑第5.3表，预期原代码因真实运算符NRE失败；这是首选真实执行Red。目标stack应含FindClient/实际原生比较，不能手抛。
3. NIL02是生产IL的回归Red，不等于执行型Red或游戏症状复现；只跑它不能宣告第一层验收通过。若NIL00失败，只保留这一未修生产基线与准确接缝缺口，进入下面的显式条件决策。

### 5.2 原生条目静态初始化确实阻断后的备选

先让根消费NIL00实际失败并确认实施授权涵盖**保行为接缝准备**；未包含即停回根，不能先把生产修掉再补Red。本轮仅提出精确备选，不假定它已经可执行：

保行为提取仍是生产变更；若根保持“任何生产变更前都必须观察到执行型Red”，本备选不得启动，应停在正确原生fixture/运行环境缺口。NIL02不能代替这一要求。本规划不自行改变该时序合同；未来授权如允许接缝准备，须明确记录准备前的IL基线与准备后的真实typed Red分别证明什么。

- **拟新增**同一`ItemPreparationAuthority.cs`内、DefaultNativeItemWarehouseBridge之外的`internal static class NativeItemObserverIdentity`，无静态字段/初始化器；签名`internal static bool Matches(SteamPlayerID id, ulong observerId)`。避免把helper放在会触发ItemManager反射字段初始化的Bridge类内。
- 先保行为提取：Matches初版仍为旧`id != null && id.steamID.m_SteamID == observerId`，FindClient唯一谓词改为`NativeItemObserverIdentity.Matches(sp?.playerID, observerId)`。遍历/返回不动；bind:262、unbind:493→FindClient:509→Matches成为生产必经链。记录这一步是接缝准备，不是修复；它只在未来显式授权后可做。
- 对**这一生产helper**运行拟新增`Test_NIL01_RealNativeLookupContract`，用真实SteamPlayerID，旧谓词因实际重载产生Red；然后仅将helper判空改为ReferenceEquals，同一方法/输入Green。不复制测试版查找、不用手抛或安全运算符替身。若真实ID自身也不能加载/构造，停止回根，不能转假Red。
- NIL02改为核FindClient→Matches真实链接及Matches中无危险重载；新增拟定StaticIL方法`Test_NIL03_BindAndUnbindUseSameLookup`核两入口均调用FindClient、该查找只有一个谓词实现、未新增吞异常handler。ReferenceEquals可能编译为ceq，不强求IL保留同名call。
- 本备选可自动验证真实ID谓词和生产链接；**不能伪称执行了非空SteamPlayer列表、真实Provider、transport或Unity loadedRegions**。首位/非首位/空容器到ID的完整原生执行留第7节矩阵/R04；直接路线能执行则优先保留直接覆盖。源码阶段接受此接缝边界须由根明确记录，不能仅凭typed谓词通过消除原生缺口。

### 5.3 测试输入与Green判据

| 用例 | 旧行为/修后预期；证据层 |
|---|---|
| Provider.clients集合为null或空 | 现存FindClient都返回null；保持原早退/零次发送，不能让本来可用入口依赖新初始化。需直接原生路线；否则StaticIL+Runtime待验。 |
| 集合中的SteamPlayer容器为null | `sp?.playerID`产生null，旧比较仍调用重载/NRE；修后跳过。备选helper只直接证明null ID可拒绝，完整容器取样边界不得冒称已执行。 |
| SteamPlayer存在但playerID为null | 旧比较NRE，修后跳过。与“集合为null”分开，不以空列表测试代替非空列表坏条目。 |
| ID非空且值匹配/不匹配 | 同一真实ID谓词分别true/false，不能改成对象引用匹配。两个不同SteamPlayerID引用但相同steamID必须仍能匹配。 |
| 目标首位/非首位、前置非目标/空条目、多个同值条目 | 按原顺序继续查找并返回第一个匹配的**SteamPlayer引用**；无目标返回null，不创建/替换玩家。直接路线执行，否则保留原循环IL/源码核对并将完整运行列R04缺口。 |
| observerId=0 | Bind既有参数拒绝、Unbind既有早退保留；不得给private查找/Matches擅加与原值比较不同的零值规则。 |
| 引用判空与原生重载的差异控制 | 拟新增`Test_NIL04_RealIdNullComparisonControl`：真实非空id的ReferenceEquals(id,null)=false、null ID为true；真实`id==null`/`id!=null`及null/null调用抛真实NRE（受控catch验证）。它是环境/语义控制，不是生产修复Red，也不要求SDK被改为安全。 |
| 不调用重载/不吞异常 | NIL02只查生产FindClient及条件Matches的SteamPlayerID调用；NIL03核bind/unbind共用它。IT20及IT145维持外部异常向上抛，不能返回成功/空列表掩盖未知故障。 |

不采用自造SteamPlayerID替身作为默认路线。若有人建议替身，须先给出与已存真实DLL的成员签名、运算符及null解引用顺序可核对照，并经根重新确认；没有真实谓词执行与生产链接证据仍不通过第一层。

## 6. 定向命令与未运行记录模板

以下命令原为**后续获准构建者**从仓库根运行的计划；实施阶段结果见 `.scratch/item-native-identity-lookup/evidence/`。`NIL01`名在直接/条件路线中保持同一正式测试名，Red→Green不换输入或降低断言。每个阶段先保存候选身份，按第8节构建顺序更新生产与测试DLL，再启动独立进程，不能直接用历史exe。

```powershell
# 拟新增前置方法；-Type必须是短类名
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .scratch/item-runtime-repair-20261023/run-single-test.ps1 -Type ItemPreparationAndFirstDemandTests -Method Test_NIL00_NativeFixturePreflight -LogPath .scratch/item-native-identity-lookup/evidence/preflight-summary.log
# 修复前/后用相同命令；分别保存red/green总输出及summary
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .scratch/item-runtime-repair-20261023/run-single-test.ps1 -Type ItemPreparationAndFirstDemandTests -Method Test_NIL01_RealNativeLookupContract -LogPath .scratch/item-native-identity-lookup/evidence/nil01-summary.log
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .scratch/item-runtime-repair-20261023/run-single-test.ps1 -Type ItemExecutionStaticILContractTests -Method Test_NIL02_LookupDoesNotCallNativeNullOperators -LogPath .scratch/item-native-identity-lookup/evidence/nil02-summary.log
```

实施时创建`evidence/`；脚本的LogPath只保存一行摘要，**不会保存测试内所有诊断/stdout或完整inner stack**。调用者另将stdout/stderr保存到阶段命名的`*-red.log`/`*-green.log`并立即记录`$LASTEXITCODE`，不要只读脚本摘要。NIL03沿StaticIL短类名、NIL04沿PureMemory短类名；现有IT145/148沿PureMemory类，IT146/147/149沿`ItemObserverReplicationAdapterTests`短类名。不向全量runner传不存在的filter参数。

每条执行记录一行或一个结构化块，填写实际值，不预填PASS：

| 字段 | 本轮状态/后续需填写 |
|---|---|
| 测试名/路线（直接或获准接缝）/阶段 | NIL00/01/02/03/04及受影响现有测试，均待运行 |
| 实际开始/结束时间（含时区）、命令、cwd、退出码 | 待运行；目前无Red/Green/build结果 |
| HEAD、branch、生产/测试工作树候选diff路径、编译DLL/实际加载DLL路径与MVID | 待记录；HEAD相同不能代替候选diff，Red与Green各留快照，不纳入受保护改动 |
| 输入、预期、实际输出、返回bool、首个失败与inner stack | 待运行；区分真实比较NRE、静态IL目标失败、fixture初始化失败 |
| 完整日志/summary路径、同测同输入关系 | 待记录；Red失败原因必须是目标缺陷，Green不得换DLL语义/删断言 |

若首次真正执行即Green，先核被测生产成员、DLL版本和是否绕过危险行；不能补写一条“应当Red”。若Red只在加载/构造前失败，按第4–5节处理，不能算TDD门已过。

## 7. E：调用路径覆盖矩阵

| 路径与现存调用链 | 自动化设计/现存证据 | 真实运行缺口及最小补证 |
|---|---|---|
| 首次Guest基线：`MultiObserverShadowCoordinator.ReconcileItemProduction:1241`→`ItemProductionControlSeam.UpdateObserver:206`→engine Observe/复制→`ItemExecutionPort.OnObserverReplicationEntered:301`→coordinator `BindGuestItemBaseline:1297`→bridge BindGuestBaseline:258→FindClient:509 | NIL01真实查找/谓词RedGreen、NIL02/03实际IL链接；IT20只覆盖异常/缺失client，不能代替非空条目。 | 原生Provider玩家列表、有效transport/Core代次及askItems实际清单发送。记录目标owner/token/region，基线发送与Guest可见状态。直接fixture能做的才计自动覆盖。 |
| 返回/结算后binder：`ItemReturnSettlement.Finish/Consume`→`ItemExecutionPort.DeliverSettlementBaseline`→同一coordinator/bridge BindGuestBaseline→FindClient | 现存IT137（ItemPreparationAndFirstDemandTests:2046起）及受影响R02 delivery-only测试保留；NIL03共用查找链接。 | 不重跑已完成世界结算，不改变SafeToRetry/unknown分类；真实native transport发送/loaded提交仍Runtime。 |
| 加入异常的补偿：engine `LifecycleOrchestrationEngine:971–988,1374–1395`→`OnObserverReplicationExited:341`→coordinator `UnbindGuestItemBaseline:1304`→bridge Unbind:490→FindClient | NIL01/NIL03核共用修后谓词；IT145:2382核action/identity/stack与抛出，但其异常注入不算本缺陷Red。 | 真原生loadedRegions访问/对象时序未自动执行则留Runtime。未知外部异常仍按原合同传播，不能为消除fault而吞异常。 |
| Guest精确旧代次退出：`ItemManagerRegionSyncPatch.OnEnemyDisconnected:793`→coordinator `OnItemObserverDisconnected:244`→seam `RemoveObserver(observer,token):237`→engine RemoveObserver→OnObserverReplicationExited→同一Unbind/FindClient；后续精确adapter移除 | IT146/147/149在ItemObserverReplicationAdapterTests:116/133/152；IT143/144/148在ItemPreparationAndFirstDemandTests，保留旧owner/transport、重连token及另一observer隔离断言。 | 真SteamPlayer/原生投影失效时序；晚到旧回调不能误删新连接。exact-cleanup单条文案不等于解除repair-required。 |
| 健康Guest进入/退出后其它observer/Host新区域：coordinator ReconcileItemProduction→同一engine/ItemExecutionPort，Host分支→PresentRegionToHost | 拟新增必要时才加`Test_NIL05_HealthyGuestExitKeepsHostAndOtherObserver`，复用既有DemandProjectionEngine/ItemProductionControlSeam/CreateTestPolicy fixture；需断言目标撤销、另一Guest仍有效及Host新区域acquire/present，不仅断言“没异常”。 | fixture外部bind/present动作替身不能声称验证了真实FindClient/模型。真实查找链由NIL01+IL链接分层证明，最终正常房间体验由R04验证，不将控制面替身算成全原生闭环。 |

现有IT145/148为seam与撤销合同，IT146/147/149为adapter/Core身份合同；都不是本票真实SteamPlayerID缺陷的替代Red。发生其它根因的异常即停止归因于本缺陷，保存第一个新stack回根；不把共享fault策略修复偷偷并入。

## 8. 四层验收与交付门

### 第一层：定向真实类型与生产链接

- [ ] NIL00前置探测结果及路线明确；接缝准备如发生有根授权、保行为diff与旧谓词Red记录。
- [ ] 修复前生产IL目标Red；真实typed生产路径/共用谓词旧行为Red→最小修复**同测同输入**Green，NIL01/02/03/04及必要NIL05有实际日志。静态IL独自通过不替代真实typed执行。
- [ ] null集合/null容器/null ID、非空值匹配/不匹配、相同值不同引用、首位/非首位与缺失目标按第5.3节分层标明；没自动执行的原生分支仍列Runtime，不写全覆盖。
- [ ] bind/unbind实际生产共用查找；ID判断不调用原生重载；owner/transport/Core token不放宽，异常不吞，外部动作替身不冒充真实SDK行为。
- [ ] 若必须采用接缝备选且根未接受其源码阶段/Runtime边界，第一层保持未闭合，不自行越过。

### 第二层：当前修后候选重新完整自动化

用户明确要求**重新跑完整自动化**；历史619/619仅历史，不替代本票结果。下面是现存项目和runner的真实入口，所有命令本轮未运行。Red阶段也先依此顺序构建含新测试、生产仍未修的候选；最终Green候选重新执行完整三步：

```powershell
& 'C:/Program Files/dotnet/dotnet.exe' msbuild SteamP2PFriends.csproj /t:Build /p:Configuration=Release /v:minimal
$productionExit = $LASTEXITCODE
if ($productionExit -ne 0) { throw "production build failed: $productionExit" }
& 'C:/Program Files/dotnet/dotnet.exe' msbuild WhitelistTests/SteamP2PFriends.WhitelistTests.csproj /t:Build /p:Configuration=Release /v:minimal
$testBuildExit = $LASTEXITCODE
if ($testBuildExit -ne 0) { throw "test build failed: $testBuildExit" }
& './WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe'
$suiteExit = $LASTEXITCODE
if ($suiteExit -ne 0) { throw "full suite failed: $suiteExit" }
```

获准执行时逐步保存独立完整stdout/stderr、真实exit；失败立即停止，不拿旧bin继续。记录实际时间/时区、HEAD+branch、生产与测试完整工作树候选diff、命令、实际通过/失败数、警告及日志路径。新增业务测试须进Program正确Evidence Class注册；NIL00环境探测不冒作业务通过数。Release生产TreatWarningsAsErrors=True（csproj:35），构建失败不可通过改配置消警绕过。

本轮各层均为待执行；不预设最终测试总数。若审查修了生产或测试，需对修后同候选重建并获得适用的定向/完整回归证据，不能复用另一个候选的619或未来首次Green计数。

### 第三层：同一完整候选的独立原生双轴

未来实施候选按`docs/agents/output-review-loop.md`，并采用本票主调度指定的Codex原生配置：每轮**两个全新独立并行subagent**，Standards/Spec各一个，均`gpt-6-luna/high`。不复用旧实例、不用Vela独立审查窗口；指导本轮不派发。派发时提供共同基线、本票完整范围diff、issue/spec和同候选验证记录，不能只审最后一行或只有报告转述。

- Standards：核确定不调SteamPlayerID重载的引用判断、范围纪律、原异常/一致性保持、新null风险、真实DLL与替身语义、保行为提取和真正修复的区别、测试是否仍走生产共用谓词。
- Spec：核ID值匹配、首个匹配/缺失拒绝、绑定/补偿/旧代次撤销、Red/Green真实性、当前完整回归、自动与原生缺口及范围外清单；不得把无NRE或静态CLEAN扩大成物资/僵尸整体恢复。
- 每位保存native agentSessionId、父实际SubAgentActivity/启动记录及子自己的rollout session_meta/turn_context，确认独立上下文、真实模型/effort；saved/launch和Vela ID不能替代原生运行来源。同一冻结候选形成两份原始报告才构成完整轮；审中变更不伪称同候选。
- **双CLEAN才源码交付。** 发现阻断由同一被授权构建者修复，再用新实例复审；同轮可澄清事实，不把续聊当新一轮。两个有效完整NON-CLEAN即停技术修改并向常驻指导/根求助；根决定范围/预算和是否新方案，不能自动开启窗口或归零。
- 新独立票目前完整轮0，预算未分配；两失败门是停止规则，不是本次授予两轮执行权。R01/R02/R03旧失败/方案/成本/CLEAN历史分别保留，不把换会话或新模型当作删除历史的理由。

### 第四层：用户实机R04，独立于源码交付

只在新候选源码阶段交付后由根统一安排，沿`docs/agents/real-machine-test-loop.md`和`auto-rm-test-sop.md`的1 Host+2 Guest人工形态，不部署未授权DLL。玩家只执行游戏操作、描述观察并提供UMM诊断包；版本/模块/错误与跨端关联由根或获准诊断者读取，不要求玩家打开/阅读BepInEx日志。

1. 根确认本场实际运行候选身份，开启新有效会话；Host独自初始区域物资正常，能拾取且无幽灵/重复。记录区域与操作顺序，不拿先前故障进程证明修复。
2. Guest加入同一世界，Guest所在范围收到可用物资基线；另一Guest按根的三端安排参与，验证一个人的加入/离开不伤另一个有效观察者。
3. Host进入先前未访问区域，观察物资呈现、拾取与唯一性；自然刷新按原生资格和冻结ADR，不强制零点位/概率不满足区域非空，不用再次generate作为测试手段。
4. Guest退出，Host再进新区域，另一有效Guest继续；记录有无继续出现物资、幽灵/重复或交互失败。根核故障链不再由本票查找触发、精确旧代次清理未破坏新连接；单条exact-cleanup不替代体验。
5. 僵尸单独记录谁在何区击杀、有无实际掉落及UMM包关联；无掉落时不改Loot_Chance/数量。没有击杀→loot→instance证据不判同根因，**本票源码/自然物资通过不自动给僵尸PASS**，相关未确认状态留R04。
6. 提交三端UMM包与简短步骤/观察，根按本端行号/运行基准及peer/session关联，不按Guest失准墙钟强排因果。对尚未自动覆盖的真实Provider列表首位/非首位、空/失效投影边界，根补最小原生证据或明确保持未验，不强迫玩家做内部故障注入。

源码状态与Runtime状态分别记录：可为`implemented-pending-runtime`，不能因第一至三层完成就勾满R04。Runtime出现新错误保留新首栈并回根诊断；原现场NRE未确认部分仍不得回填“已经证明修好”。

## 9. 风险、与R01–R04关系及交接

- R01唯一呈现/pending/committed身份门保持；R02共享结算及delivery-only安全续送/unknown隔离/人工资格保持；R03精确旧owner/transport/token撤销、诊断与异常传播保持。本票局限于共同原生ID查找，不重开旧构建者。
- 该危险行在R01/R02/R03源码已存在；历史CLEAN不作本票Red/Green或当前全回归。10月13日退出SnapshotRemove异常缺stack，不由本场加入补偿FindClient栈倒推同因。
- 真实ID构造及原生非空SteamPlayer fixture本轮未运行；历史SteamPlayer静态初始化障碍是选择接缝的证据，不保证当前新fixture必失败。先前置探测，再按授权路线；不把提取后的typed谓词测试伪称原未改代码的完整查找重现。
- Guest本场归属/僵尸来源、会话结束后再挂起独立留项，不扩本票；当前无需改变用户玩法决策。实施阶段若遇接缝或自动/Runtime验收边界冲突，由根确认条件路线，不通过删AC或换fake自行解决。
- 原规划阶段仅交付`issue.md + spec.md`，当时没有实现或验证结果；用户已明确恢复本票实施。当前有效真实Red与后续实施/审查/测试包证据均记录在本票 `evidence/`，不把旧规划状态当作当前状态。
- 最终交根后本指导只读按需待命；规划交付正文/真实message-id/回执保存到本次诊断run `expert/`。旧`drafts/item-native-identity-lookup.md`及根R04记录不由本作者覆盖。
