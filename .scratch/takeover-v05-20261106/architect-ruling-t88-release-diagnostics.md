# T88 正式包诊断归属：同一程序集保留，默认关闭，安全内核常驻

2026-11-08，gpt-architect。发布策略裁决；仅新增本文，未改生产/项目/打包文件，未构建或运行游戏。本裁决不等于现有探针已通过审查，也不批准任何未审字节。

## 1. 唯一选型

**正式0.2.4.11采用同一主程序集保留已审诊断代码、诊断默认OFF；不设诊断条件编译分叉，不拆独立诊断DLL。** 正式与定向诊断使用同一套经审Release字节，区别在显式运行配置/执行场景，而不是两个容易混淆的生产二进制。

原因：当前最需要锁住的是Bug3保存安全和真实验证身份，而不是为少量休眠工具新增第二套构建/测试身份。现有项目已把诊断与恢复内核一起显式Compile，控制入口也已存在；离线测试引用主Release DLL。同一字节在OFF状态验证Bug3，能直接回答“日常不运行诊断时恢复是否仍有效”，无需用另一构建代替证明。

不选条件编译：会新增编译项/注册入口/类型引用/测试分组的成对维护；排除整目录可能误排保护，遗漏一处调用会断编译或留下依赖。需要至少两种生产身份、两种实际加载DLL测试和两份制品清单，尚无证据说明此成本能换来本轮必要收益。

不选独立程序集：目前大量内部类型、BepInEx配置和Harmony所有权依附主模组；拆分要处理内部接口/加载顺序/版本兼容/卸载清理/可选依赖，超出本轮发布策略所需。不能为了“诊断独立”暴露新公共API或形成循环依赖。两条被否选路线不是留给Captain选择的备选。

这不是安全沙箱：代码被编入不等于永不可调用；OFF也不等于没有配置绑定或启动日志。未通过适用审查的诊断增量，不因默认OFF就自动获得随正式包发布许可。T57允许明确登记低风险限制，但未关闭的高危/阻断项仍阻塞候选。

## 2. 文件级边界：按责任，不按文件夹或名字裁剪

| 文件/落点 | 责任与正式包归属 |
|---|---|
| [P2PGroupStateRecoveryPatch](../../Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs#L615-L665) | **常驻安全/修复内核**：Prefix_Load/Prefix_Save、实例Managed/NativeLoadCompleted写入证明、部分load失败后保旧档、同代次授权、异常fail-closed。不得放在诊断Enable之下 |
| [GroupStateRecoveryCore](../../Core/Patches/P2PGroupStateRecovery/GroupStateRecoveryCore.cs#L245-L327) | **常驻**：当前epoch/map组表Ready、单次授权/取消、旧token无效、未就绪不执行load。UNKNOWN/不可确定就绪必须拒绝，不可当默认成功 |
| [GroupLeaseLedger](../../Core/Patches/P2PGroupStateRecovery/GroupLeaseLedger.cs)、[GroupLoadPlanner](../../Core/Patches/P2PGroupStateRecovery/GroupLoadPlanner.cs)、[P2PGroupRegistration](../../Core/Patches/P2PGroupStateRecovery/P2PGroupRegistration.cs) | **常驻**：执行/通知至多一次、状态转换与注册失败事实；不是因为包含计数/原因码就可以删的遥测 |
| [PatchRegistrationAuditModules](../../Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationAuditModules.cs) | 必需补丁/生命周期订阅的真实注册及失败传播常驻；恢复内核不依赖Bug1诊断启动成功。不得把“诊断没开”当作不登记写安全前缀的理由 |
| [PlayerMovementInitializePlayerPrefixPatch](../../Core/Patches/PlayerMovementInitializePlayerPrefixPatch.cs)、[PlayerUpdateGuardPatch](../../Core/Patches/PlayerUpdateGuardPatch.cs)、[SteamPlayerIsLocalServerHostPatch](../../Core/Patches/SteamPlayerIsLocalServerHostPatch.cs)、[ClientMethodLoopbackPatch](../../Core/Patches/ClientMethodLoopbackPatch.cs) | 现有角色/实体初始化/实际消息分派等功能逻辑保留；不能按Probe/日志关键字扫删或用本策略重新设计这些模块 |
| [Bug1ProbeControl](../../Platform/Diagnostics/Patches/Bug1ProbeControl.cs)、[Bug1ProbeEntryLogic](../../Platform/Diagnostics/Patches/Bug1ProbeEntryLogic.cs)、[Bug1ProbePatch](../../Platform/Diagnostics/Patches/Bug1ProbePatch.cs)、[Bug1ProbeRxPhases](../../Platform/Diagnostics/Patches/Bug1ProbeRxPhases.cs) | **诊断控制/采样**：可选，正式包中保留但默认不采集、不安装其定向hook；与恢复功能开关独立 |
| [Bug1ProbeRuntime](../../Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs)、[Bug1ProbeModel](../../Platform/Diagnostics/Patches/Bug1ProbeModel.cs)、[Bug1ProbeJudge](../../Platform/Diagnostics/Patches/Bug1ProbeJudge.cs)、[Bug1ProbeWindowBoundary](../../Platform/Diagnostics/Patches/Bug1ProbeWindowBoundary.cs) | **诊断记录/判定**：关闭时不执行采样/文件输出。Judge的UNKNOWN意味着测量证据不足，不是Bug3保存许可；它的存在不是业务内核依赖 |
| [Bug1ProbeNativeSpeed](../../Platform/Diagnostics/Patches/Bug1ProbeNativeSpeed.cs)、[Bug1ProbeTargetSelector](../../Platform/Diagnostics/Patches/Bug1ProbeTargetSelector.cs) | **诊断证据/目标选择**；不是用来放宽角色权限或更改实际速度的功能模块。保留T57蹲趴速度口径限制 |
| [Bug1GateProbe](../../Platform/Diagnostics/Patches/Bug1GateProbe.cs)、[Bug1GateProbeLogic](../../Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs) | **侵入性诊断实验**：短暂0.10U渲染叶偏移/BakeMesh/受控验证，不是持续插值修复。默认OFF、授权单目标、一次有界试验；本方案保留代码但不得日常自动运行 |
| [PatchRegistrationDiagnosticsModules](../../Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs#L59-L91) | 诊断入口接线保留；关闭诊断不能清除必需业务补丁。诊断不可用应拒绝诊断并明确状态，不得卸载内核保护；也不能用“可选诊断”掩盖真正必需注册失败 |

“可排除的诊断”在责任上是上表采样、轨迹输出、离线式分类和G实验这一功能闭包，**本次选择不在二进制里排除它们**。该分类用来防止将来拆工具时误删内核，不授权本轮条件裁剪。目录`Platform/Diagnostics`不是统一可删集合，业务使用的安全逻辑即使落在此目录也必须保留。

必须区分三种失败关闭：①业务数据不确定时阻断不安全load/save，常驻；②探针证据缺失时UNKNOWN、不可报PASS，随探针正确性保留；③G或未来插值在未知profile/恢复不确定时停写/撤销自有偏移，**只要该执行功能随包，其安全门就必须随包**。禁止只删②的UNKNOWN来“简化诊断”，也禁止保留③写者却删除其回滚。

安全日志不是可随意删除的海量诊断：SaveBlocked、未恢复成功、本局Quests进度不能保存、需要正常退出重建等最少告知必须保留。日志/遥测异常不能改变`return false`为允许写坏档；告知失败也不能冒称恢复成功。

## 3. 默认OFF的真实含义与配置处理

现有入口已能支撑此选型：[Bug1ProbePatch:30–39](../../Platform/Diagnostics/Patches/Bug1ProbePatch.cs#L30-L39)在未Enabled时只保存注册上下文，不安装采样hook；[Bug1ProbeControl:42–55](../../Platform/Diagnostics/Patches/Bug1ProbeControl.cs#L42-L55)绑定`[Bug1Probe] Enable=false`；[G入口:45–79](../../Platform/Diagnostics/Patches/Bug1GateProbe.cs#L45-L79)绑定`[Bug1Gate] Enable=false`。

正式包不捆绑用户配置文件、已启用诊断的cfg、trace/result原始包、临时mutation DLL或测试进程输出。正常安装首次生成的两个诊断Enable必须为false；关闭状态允许配置绑定/控制事件订阅及必要入口日志，不许据此夸称“完全零代码/零I/O”。G关闭时不能存在帧偏移、BakeMesh试验或相机测量回调。

**默认值不会覆盖用户旧配置。** 两个入口发现已有Enable=true会启动/尝试启动，不是每次升级都强制OFF。升级部署前须检查`SteamP2PFriends.Bug1Probe.cfg`及`SteamP2PFriends.Bug1Gate.cfg`；发现true，部署者先停止发布动作，告知操作者确认关闭，不能静默覆盖整份用户配置。用户之后按明确测试步骤主动开启属于有意诊断，不是默认日常模式。不新增自动擦配置、一次性nonce框架或全局禁用内核的总开关。

G文案不能复制源码中的泛化“不改变任何玩家表现”：它确实临时写渲染叶，意图是测量并恢复，不是永久平滑。正式包说明必须写清试验性/临时偏移/恢复边界，不把侵入性G试验标成纯只读采样。

## 4. 构建、审查、验证与打包的可执行机制

不新增诊断`DefineConstants`，不新增`ReleaseDiagnostics`或自定义Configuration，不改测试加载路径。[主项目](../../SteamP2PFriends.csproj#L27-L34)保持Release/AnyCPU、TRACE与`bin/Release`；[诊断/恢复显式Compile项](../../SteamP2PFriends.csproj#L278-L295)保留；[测试工程](../../WhitelistTests/SteamP2PFriends.WhitelistTests.csproj#L42-L44)继续引用`../bin/Release/SteamP2PFriends.dll`，不改成另一flavor的替身。

后续集成者按以下顺序执行；本票不执行：

1. 锁定候选source/依赖/配置默认值、工作集与审查副本。沿用`dotnet build SteamP2PFriends.csproj -c Release`、`dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release`及正式runner。若仓库现用封装脚本则记录其完整实际命令/exit，不另造发布CLI。禁止共同bin/obj被并行审查/变异覆盖。
2. 记录实际构建和**测试加载**的主DLL SHA/MVID，必须一致。工程轴验证实际默认绑定、OFF无采样hook/帧写者/轨迹输出，G明确开启才做有界试验；配置false→true变异要能使默认值守卫红，不是另写常量自证。
3. Bug3验证使用这一个DLL，两个诊断Enable=false，并证明其必要补丁仍登记：真实生产registrar/owner/目标、组表Ready、受控load恰一次、members不增、未就绪不load、partial load后反复save拒绝且隔离临时文件不变、跨epoch对象隔离、通知失败与load成功不混淆。保存失败方向依T23，不能只看正常成功例。独立变异至少覆盖：把恢复注册误绑到诊断Enable、删除保存前缀真实登记、失败实例放行save、删Ready授权门；默认OFF注册/隔离临时文件/未就绪用例须分别真红，不能仅反射同名方法存在。
4. SDK/原生组件边界复放已有T1a同一fixture/原生存档/事件顺序，不能用Probe方法回填状态；有真实G/测量开启的情形再对照Bug3权威/写安全不变。另做诊断拒绝/IO异常负控：诊断失败只停止该诊断；不能卸载保存保护或把未知恢复当成功。无法完成的宿主/实机证明按UNKNOWN登记。
5. 规格轴检查诊断OFF与修复功能分离、G临时写入的真实文案、默认配置和旧cfg行为、未启用/未包含的措辞；工程轴检查调用接线、异常路径、实际注册/卸载所有权、二进制与默认值守卫。正式生产/配置语义变化仍双轴，不拿默认OFF豁免。
6. 完成适用审查后，只把已审同一字节经现有打包流程复制/压缩；manifest记录主DLL SHA/MVID、Release配置、适用审查、功能清单及未闭合验收。拆包再次核对DLL SHA，不靠文件名/版本号推定。若版本元数据/签名/运行配置又改变字节，重新绑定并验证受影响差量，不能沿用旧哈希；无需重做无关历史调查。

本方案**没有诊断裁剪版**，不声称测试过“排掉诊断代码后的DLL”；证明的是诊断执行路径关闭时，正式同件仍有完整Bug3修复/保护。若T1a/T1b及队伍其它独立验收未实证闭合，即使全套离线绿也不得说“已证明实机修复全部Bug3”。正式发布是否已具备该证据须查验收账，不由本裁决补授。

日常OFF、显式Bug1采样、显式G试验是同字节的三种运行场景，不是三个可混用哈希的发行版。测试总数/FAIL/SKIP/缺fixture仍按T79如实列，不能因诊断默认OFF就跳过它的离线守卫并计PASS。

## 5. 未来插值开关的归属

**若插值成为经验证的用户可选修复，功能与其开关必须进入正式主程序集，默认OFF；用户不应为启用已承诺的功能再安装诊断包。** 不能把持续每帧写者藏进`Bug1GateProbe`，也不能把Bug1Probe.Enable/Gate.Enable当作插值Enable。

判据是发布契约和安全闭包，不是“有开关就都是诊断”：

- 一次性观测、采样导出、G的0.10U扰动/BakeMesh属于工具；开它只产证据，不承诺日常改善。
- 用户日常开启、持续改变模型呈现并可改善体验的是产品功能。其profile身份核验、角色/代次/单目标限制、采样/断包/姿态reset、禁碰撞/权威写、所有权校验、sticky失败和撤销自有偏移必须与写者一起常驻；不能因叫debug/实验就删安全门。
- 可分离的是G自动扰动和大体量trace，不是运行时资格。若将来功能依赖profile，正式件用已验profile指纹与实时只读资格核验；未知/变更profile拒绝开启并解释原因，不能“诊断未安装所以跳过G当成功”。仍需主动G试验的未知profile继续留在实验验证范围，不能承诺普适用户功能。

**当前没有获得插值可行性/收益验收，因此正式.11不能以本票名义加入已开或默认开插值，更不能靠G探针宣称插值修复。** 以后如纳入可选修复，执行[T76](bug1-minimal-render-interpolation-t76.md)：默认OFF、单目标、有界G、实际表面证明、A/B/判定一致性、自动回退和适用双轴；未验证功能继续UNKNOWN，不私自承诺延期到.12或缩减原用户目标。

## 6. 成本与误发布检出

以下估算是流程/维护面，不是本票实测构建分钟数；真实时长随宿主与全套用例变化。

| 方案 | 构建/维护成本 | 发布操作成本与主要误发风险 |
|---|---|---|
| **已选：同一程序集默认OFF** | 一个生产身份/构建链；保留现有内部访问，无新公共接口。需要三种运行场景的正确性检查；含休眠IL和配置绑定开销，不能说体积零增长 | 一套主DLL/manifest。主要风险为旧cfg仍true、误把G叫只读/修复、漏业务保护、混入未经审功能。用默认绑定测试、升级配置检查、类型/方法清单与包内SHA检出 |
| 未选：条件编译两flavor | 至少两个生产身份/完整构建与加载测试；维护条件Compile/注册/导出/用例分组。每次修复都需验证两者安全闭包，无诊断版的真实Bug3不能由有诊断版代证 | 两套清单/打包标签，容易误发Diag或把安全代码排掉；必须做双向类型presence/absence及原生保护验证。当前不引入这笔成本 |
| 未选：独立诊断程序集 | 新程序集工程、可选依赖/接口、加载卸载及版本组合；最少主件/工具件两制品，兼容矩阵多于单件 | 需防漏依赖、错误配对、同钩多owner/重复加载；要分别验证仅主件与加工具件。当前不引入 |

已选方案打包门禁（不能仅靠开关文案）：

1. **应有保护存在**：Cecil/IL或等价静态检查确认主程序集包含五个恢复内核类型、Prefix_Load/Prefix_Save与真实注册调用；真实Harmony/组件测试再证明目标与owner生效。仅存在同名类不算运行时保护。任何缺失阻断发布。
2. **诊断代码清单明确**：正式件允许并预期含`Bug1ProbeControl/Runtime/Patch/Judge`、`Bug1GateProbe/Logic`；不能把它们的存在错报为“该排除没排除”。manifest用`contains_diagnostic_code=true`、`diagnostics_default_enabled=false`；记录所选类型/方法清单及代码差量，未审新增的持续视觉写者阻断。
3. **应排除的是非交付物**：无已启用用户cfg、原始诊断trace/认证数据、mutation/中间DLL、测试runner、任务临时文件。安全的`.cfg.example`可随说明提供，但不是会自动被读入的用户配置。Zip解包核验及清单断言不能只查主目录。
4. **默认态与升级态分别测**：新空配置目录绑定两个Enable=false；已有true配置不得被描述成自动OFF，升级流程检查并提醒。OFF时无Bug1采样hook或G帧偏移；显式开关失败必须拒绝并留下可核验原因，不能假装已启用。
5. **包内说明与真实能力对照**：当前功能清单写`visual_interpolation=not_present`，而`G_gate_trial=present_default_off`；不是笼统`experimental_features=none`。若后续实际含功能，只能在已审清单里明确它的状态，不能以名字中没有Interpolation为证。

默认OFF是运行选择，不是对恶意反射/私改配置的隔离保证；本裁决不宣称防用户主动调用内部工具。安全承诺限经审路径与明确支持环境，仍须保留工具本身的约束和失败回退。

## 7. Release notes与包内说明的定稿要求

当前功能状态的建议文案（版本/hash/已验范围由交付者据实填，不能空造PASS）：

> 本包包含队伍初始化与保存安全的修复逻辑；已验证及待验证的独立范围见验收清单。Bug1定向测量和G几何验证工具随主程序集提供，默认关闭，不会因安装就持续进行测量或视觉插值。G工具显式开启后会临时偏移目标渲染叶进行一次验证并尝试恢复，仅用于授权测试场景。当前不包含持续视觉插值修复；G结果和离线判定不代表顿挫已消除。升级前请确认两个诊断cfg未遗留Enable=true。

允许说“诊断探针默认关闭”，**必须同时解释旧配置不会自动重置**；允许在当前确无持续插值写者的清单下说“未含视觉插值功能”。禁止说“未含任何实验功能”“诊断代码已剔除”“所有探针纯只读绝不写对象”“整个正式包零诊断开销”——与所选机制/G行为不符。

如果将来正式件含可选插值，文案必须改为“包含可选实验视觉插值，默认关闭、仅支持已验单目标/profile；开启条件和回退见说明”，不能仍写“未含插值”。也不能把默认OFF或审查CLEAN当作三Bug实机修复完成；尚未闭合的M1/M2/M3/M4、枪声S1–S4/互听、队伍各项须分别报告，正式稳定发布的承诺必须有其真实证据。

说明同时给出：实际主DLL SHA/MVID、适用审查及配置路径/开启边界、已知限制、明确恢复方法和备份/回退注意事项。正常用户不用开启诊断去获得Bug3保存保护。

## 8. 既有裁决对齐与执行授权边界

- [T57](architect-ruling-t57-work-policy.md)：保留冻结输入/不可变审查/串行共享接线和review-before-preview。本选型不改csproj Configuration、不拆测试路径；后续若需改默认值/注册/manifest的生产输入，必须专票拥有共享文件并重新绑定。仅文案纠错可证据核验，不能借此跳过生产双轴；适用审查范围中未关闭的高/阻断项不能默认OFF豁免。
- [T33](architect-ruling-t33-gate-and-null-safety.md)：不新增虚构Provider.user-null修复，不拆R5-05合取布尔；不为这类文案再次扰动已审字节。不会将可选诊断策略变成放宽原角色判定。
- [T76](bug1-minimal-render-interpolation-t76.md)：默认OFF、授权单目标、G未知不直接开插值、所有权与恢复不确定即停止。保留最小路径一次有界证伪，没通过不能把工具改名为修复。未取代它的安全/实机门，仅明确代码随包归属。
- [T22](architecture-decision-t22-flags-and-retired-lease.md)、[T23](architecture-rescue-t23-save-failure-direction.md)：flags仍单次multicast，保留可核验退役标识；宁显式拒保存也不写坏档。日志、UNKNOWN与诊断开关均不能改这些业务状态机。

**本票不取代上述裁决，也不允许在被冻结的共享文件上直接施工。** 交付者先按此唯一单件策略建立候选清单和门禁，再由实现/集成与独立审查完成所需增量。本轮仅裁决和文件/来源核验；没有实测OFF/ON/G、原生回放或正式包，不能把未来验证计划写成已通过证据。