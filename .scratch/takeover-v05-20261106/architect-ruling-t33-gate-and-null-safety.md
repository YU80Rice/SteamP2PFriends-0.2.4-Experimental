# T33 裁决：本轮不为 R5-03/04/05 重出待测包

2026-11-07／gpt-architect。**唯一处置：保留当前生产字节；R5-03 不修，R5-05 不拆字段，R5-04 立即以本文定稿更新交付说明，不改生产注释。** 用户继续验证已发待测件；本裁决不取代 t32 的测试守卫闭环。

## 1. 决策绑定与今晚的成本判断

[工程轴 t31 报告](review-eng-t30.md#L7-L19) 已确认：生产判空修复正确，未闭合的是生产调用点的测试守卫，不是再发现一个同等现场故障。本裁决仅处理三条生产侧低级条目，不把假绿推给生产，也不冒认 t32 已完成验证。

| 对象 | 绑定 SHA-256 |
|---|---|
| [生产补丁](../../Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs) | `a89f40b949f841d03eb1a7b2a135fa1b50aa8ffb8bfec4d69629fad516cea265` |
| [已发 preview2 DLL](../../Release/0.2.4.11-preview2/SteamP2PFriends.dll) | `d9254a2461009daddf36e65baa320c38295b7aaec2dd7bf24e87f44d3fa4bb95` |
| [t31 报告](review-eng-t30.md) | `be6371f634134e69f225795a2c46f716a1fe6ae471041ffadec18ea6f43c52fa` |

用户在等可测 DLL；任何生产改动都须重建、用例及双轴复审，预计额外40分钟以上，且新件不再是上表同一字节。R5-03 的 null 前提被类型事实排除，R5-05 可用准确说明与定向证据弥补，因此**今晚不值得为这两项使待测件作废**。R5-04 是交付措辞更正，无需另造DLL。只有新实机证据证明另一个真实阻断，才另行裁决；这不是本轮的可选“顺手修”。

## 2. R5-03：不改生产；所称 null NRE 不可达

### 2.1 关键类型事实，不是拿 catch 当安全保证

[Provider.user 声明](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Provider/Provider.cs#L469-L470) 是 `CSteamID` **值返回**。本轮用 .NET Framework `ReflectionOnlyLoadFrom` 只读类型元数据，未执行目标静态构造器或游戏代码，得到：

| 实际依赖 | CSteamID 元数据 | SHA-256 |
|---|---|---|
| [构建依赖](../../../Libs/com.rlabrecque.steamworks.net.dll) | `Steamworks.CSteamID`，`IsValueType=True`，`Base=System.ValueType` | `2eeba5bf8bc9d9d9b698e37964bc82fd2547c1809efb726210e6b0a8129e3e1b` |
| [当前游戏依赖](E:/Steam/steamapps/common/Unturned/Unturned_Data/Managed/com.rlabrecque.steamworks.net.dll) | 同为值类型，`Base=System.ValueType` | `63ea73cec0e5b8ac13eb38140bfefc5bc604fbaffe4efdbbed9a5463a0d1f29c` |

两者MVID均为 `707e2d2b-1dfa-4ed1-ac62-28f0e331c838`，但字节哈希不同，**不将相同MVID当成相同文件**；两份各自的类型证据一致。与确实为class的SteamPlayerID不可混淆。

因此 [BuildEligibility:966](../../Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs#L951-L977) 取 `Provider.user.m_SteamID` 不存在“Provider.user引用为null”的状态；未赋值是default(CSteamID)，其ulong值为0。**裁决三态为：不可达——仅指R5-03所述该行的null解引用NRE，不宣称整个BuildEligibility在所有环境绝不抛其他异常。** 不为不存在的异常加ReferenceEquals/try-catch，不把它包装成用户已接受的NRE风险。

### 2.2 调用顺序与 dedicated/listen-host 的真实边界

- [Provider.awake](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Provider/Provider.cs#L6548-L6588) 先设置isInitialized，再走Steam初始化；所以**单看isInitialized不能证明user已赋值**。
- dedicated路径在[6751](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Provider/Provider.cs#L6742-L6758)返回，不经过客户端的[6806–6808](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Provider/Provider.cs#L6806-L6808) `_user = SteamUser.GetSteamID(); _client = user;`；默认零值仍然是有效值类型，不是null。
- 正常客户端先完成Steam初始化/本地ID赋值，再由[GAME回调](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Provider/Provider.cs#L1508-L1556)以client构造SteamPlayerID/addPlayer/InitializePlayer。SPF [HostManager启动](../../Platform/Host/HostManager.cs#L199-L201)还先以Provider.user做白名单bootstrap；[OnServerHosted:399–404](../../Platform/Host/HostManager.cs#L399-L404)已读取本地ID。正常已开局的1H+1G并无另一个“user是null”的早期窗口。
- [BuildEligibility:951–977](../../Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs#L951-L977) 确实没有按dedicated/P2P提早短路，而是先映射facts，后在 [IsEligible:57–67](../../Core/Patches/P2PGroupStateRecovery/GroupStateRecoveryCore.cs#L57-L67) 判断。因此**dedicated或控制台混用可触及966这一行**，不能说“模式门会让该行进不去”；但触及值字段不造成R5-03所述null异常。

外层 [catch:564–604](../../Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs#L564-L604) 对真正异常仍会区分Host fail-closed与非Host原生回退；本裁决不改变这种数据安全方向。它不是本条null风险成立的证据，也不是判定不可达的依据。

**对二选一的执行回答：本轮不修、不重出包。** 真实支持边界按以下文本登记，不能照录错误null前提：

> 当前待测包只对本地Steam身份有效且完整满足门控的P2P Host提供队伍延后恢复。Steam尚未初始化、身份为零或dedicated/离线测试宿主不满足门控时，不承诺该恢复路径；正常结果是NotEligible并保留原生load。`Provider.user`为值类型，不会仅因其默认零值产生本条声称的NullReferenceException。若日志实际出现保存阻断，应按真实异常栈另查，不能归因于“Provider.user=null”。

## 3. R5-04：可直接引用的最终措辞

> 本次NRE根因是SDK的SteamPlayerID重载`==/!=`时直接解引用两侧对象：任一侧为null都会抛异常，因此`playerID != null`即使在playerID已绑定时也会因右侧null触发异常。当前修复改用ReferenceEquals验证owner与playerID引用，再读取SteamID，消除了这一判空触发点；队伍恢复效果仍以待测件的实机结果为准。

依据为 [SteamPlayerID class声明](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Provider/SteamPlayerID.cs#L9-L12) 与 [operator实现:136–143](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Provider/SteamPlayerID.cs#L136-L143)。不再写“playerID恰好为null时才抛”。本轮只订正交付说明/known-issues；源码注释后续有必要改生产时再合并，不因此废弃当前件。

## 4. R5-05：本轮不拆；准确解释合取与证据缺口

[HasBoundPlayerId:1005–1010](../../Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs#L1005-L1010) 与 [gate:1026](../../Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs#L1020-L1032) 的真实语义为：

> `owner=true` 表示“owner存在且owner.playerID已绑定”，不是单独的owner存在性；非零SteamID另由ownerSteamIdNonZero表达。`owner=false`不能独自诊断哪一个引用缺失。

**不值得今晚为拆两个布尔重跑40分钟以上并换包。** 当前FormatGateLine只收到合取后的eligibility，若要真正输出两个事实，还得在映射层分别捕获并传递；不能只换字段名或从合取值拆出两个假布尔。合理实现属于纯观测增强，必须保持原12项门控判定不变，但仍改变生产字节/日志schema，故推迟，不能混入t32测试侧工作。

现件的定向取证按下表执行，不假装合并日志能反推出全部现场：

| 同一gate的证据 | 能得出的结论 |
|---|---|
| owner=true | owner和playerID均存在；再看非零/loopback/唯一ID等其他门 |
| owner=false且loopback=true | 依[964](../../Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs#L961-L966)，真实loopback连接证明owner存在，因此缺的是playerID绑定 |
| owner=false且loopback=false | 可能owner缺失，也可能owner存在但playerID未绑定且非loopback；**现行gate本身无法唯一分辨**，记UNKNOWN，不伪填某个原因 |

需要分辨最后一行时，补**同一判定时点的只读对象证据**，对BuildEligibility已经取到的局部owner分别记录`!ReferenceEquals(owner, null)`与（owner存在时）`!ReferenceEquals(owner.playerID, null)`，不可用SDK重载`!= null`，也不另读一次channel来混入其他时点的状态。可在已有获准调试/对象检查会话中于BuildEligibility取完owner之后暂停只读查看；不改内存、不直接调用load/save、不要求用户为普通验证先安装调试器。没有此能力就保留UNKNOWN，下一次确有该现场阻断时再派最小观测增量；本票不声称已取得这种快照。

[SteamPlayer构造顺序:788–804](../bug3-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Provider/SteamPlayer.cs#L788-L804) 是同期佐证：先使用newPlayerID，再给_playerID赋值，之后才发布channel.owner并setup。标准正常构造不支持“已发布owner但ID任意未绑”的随意推断；构造完成日志只能佐证生命周期，不能替代失败gate同一时点的对象快照。

交付说明追加这一句即可：

> gate中的owner是“owner存在且playerID已绑定”的合取值；owner=false须结合loopback及初始化记录判断，仍有歧义时按UNKNOWN回传原始日志，勿据此断定playerID为空。当前不新增诊断DLL，仅在实际歧义阻断复现后安排只读对象取证。

## 5. 交付边界与指纹约定

R5-03不成立的null风险不构成换包前置；R5-04文案立即更正；R5-05登记观测歧义而不改当前字节。t31关于根因已修对的结论保持，测试守卫由t32承接；本裁决不把t31的needs_revision偷改成CLEAN，不关闭未做的真实存档/实机验证，也不借本票扩大生产改动。

本文件末行self_sha256是**正文原始字节SHA-256**：覆盖末行之前的全部UTF-8字节（含此前换行），不含末行本身；避免把内嵌哈希错误宣称成自引用的全文件哈希。含末行的完整文件哈希另在任务交付记录给出。本票未修改生产、未运行游戏/构建/用例、未执行git；类型核验仅使用ReflectionOnly元数据读取。
self_sha256: 7482ab2bd132393dc16ffed4b2a21c5c4096e6f08eea0436edd0d14bd7196dfd
