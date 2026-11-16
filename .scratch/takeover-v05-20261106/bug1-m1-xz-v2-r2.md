# t77 交付报告：M1-XZ-RELATIVE-v2 规格轴补齐（T74-F1/F2/F3 + T75-F2）

- 任务：t77（repair round 2），attempt 1，attempt_id `59f148fd-7d22-459c-80c5-d6c9adeb06c8`
- 被修：t73 交付（t74 规格轴 needs_revision、t75 工程轴 pass）
- 改动范围：仅 `WhitelistTests/Evidence/Runtime/Bug1ProbeM1RuleTests.cs`、`Platform/Diagnostics/Patches/Bug1ProbeJudge.cs`
- `WhitelistTests/Program.cs` **未动**（不在 t77 inScope）：新增夹具全部塞进已注册的 R02/R07/R10 方法，用例编号仍 Bug1-70…Bug1-81

## 1. 改前 / 改后哈希

| 文件 | 改前 | 改后 | 行数 |
| --- | --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1ProbeJudge.cs` | `615080f4f132ea964acd30592203d4db5d98ca3717e6b400ba062e7b744723f0` | `ee0ef9f0b4fda028a177311624bf722ae21dc3c5ef68648fb76a8ef1c15708b1` | 692 → 700 |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeM1RuleTests.cs` | `0b4b019179bbfc0ec8177eb75288ed8602e98dc031117d3a7107a78439870749` | `b81d72a4eb72a88a78535f2913407a1b538dd75545a05abfcf7f998af4d0cb8e` | 584 → 651 |
| `WhitelistTests/Program.cs`（未动） | `71d5268ad401ae842ee97c71ba368f590a1554ba86b0f7dae8087c9949095c8e` | 同左 | 1085 |
| `SteamP2PFriends.csproj`（未动） | `ed629ae7064015d55d2e1bee15b01ba1eb6a7a565dcdb80ac36892499350b948` | 同左 | — |
| `bin/Release/SteamP2PFriends.dll` | `81da407a7bec87e88e0a642a5b1885e37541c28d7418259fdb897f2212341f27` | `0e6ebcae13ba7fb0fe88875fd3a3d95136a033c4be92858f7501ae1cd33108c7` | — |

## 2. T74-F1（medium）：R07 没构造出「累计跨度大」

**问题**：原 R07 用 `x = 0.0005 * i`（i=0..3），累计仅 0.0015U **< ε=0.001732U**，`RenderRootSpanXZ <= ε` 只证明了「小跨度」，证不了「累计大 ≠ 落后证据」。

**修法**：`Window(9)`，帧 0..7 `x = 0.0008 * i`（逐帧 0.0008 ≤ ε，累计 0.0056 > 3ε=0.0052），帧 8 释放到 0.08。断言：
`StrictM1Events==0`、`LagWitnessFrame==-1`、`HorizontalStallSegments==1`、`HorizontalCandidates==1`、
`CandidateRejections` 含 `LAG_WITNESS_MISSING@8`、`CandidateA==-1`、`Label==NoThresholdEvent`。

**一处与 t74 原文的偏差（已由 m02645 裁决）**：t74 要求断言「存在代表段」，但 v2 语义下代表段只赋给 M1 / AUTH 段，NON_M1 段没有代表。实测该例 `RenderRootSpanXZ == 0`（该字段只在代表段计算），所以「`RenderRootSpanXZ > ε`」在此例**不可能成立**。本票改为：
- `RenderRootSpanXZ == 0`（设计如此，代表段未赋给 NON_M1）；
- 夹具算术 `0.0008 * 7 > 3ε`（在用例里直接断言，证明「累计大」这个前提真的成立）；
- 行为证据靠 `HorizontalCandidates==1` + `LAG_WITNESS_MISSING@8`。

没有为了一条断言去改生产语义（那正是前几轮假绿的来源）。

## 3. T74-F2（low）：AUTH 用「距段首」而非段内最大成对距离

**问题**：`IsAuthStepLocked` 原实现逐帧比较 `HorizontalDistance(A_i, A_a) <= eps`，§3.4 原文是 `max‖A_i−A_j‖ ≤ eps`。对称抖动可让段内真实跨度 ≈2ε 仍判 AUTH。

**修法**：改成段内**真实最大成对水平距离**（O(n²) 双层循环，段长几十点、不在热路径，不做近似）。明确不采用「逐轴 max−min 合成」——它是成对距离的下界，会把实际超 ε 的段判成 AUTH，方向是放宽而非收紧（m02645 已否掉）。

**行为变化（必须记录）**：这条改动会让**部分窗口的标签从 AUTH 变为 NON_M1**，仅当段内 A 抖动幅度 > ε 但距段首 ≤ ε。依据是裁决书 §3.4 原文，属于「按裁决书补实现」，不是扩语义。回归面已核：R02（A 恒 0）、R03（真实 trace A 恒 0）、R08（长 AUTH 段 A 恒 0）、R11（yaw 跨零段 A=V 步进）仍判 AUTH。

**配套夹具**（R02 内）：`Window(5)`，V 全程 0，A = 0, +0.001, −0.001, 0（量化后每帧与段首差 0.001 ≤ ε，成对跨度 0.002 > ε），帧 4 释放 A=V=0.08 →
断言 `Label==NoThresholdEvent`、`AuthSegments==0`、`NonM1Segments==1`、`StrictM1Events==0`、`CandidateRejections` 含 `LAG_WITNESS_MISSING@4`。
> 取值受 `q=0.001` 量化网格约束：第一次写 ±0.0017，量化后变 ±0.002，与段首差已 > ε，两种口径都判 NON_M1，**变异 MV-2 不红**；改成 ±0.001 后 MV-2 才真正区分两种实现（见 §6）。

## 4. T74-F3（low）：R10 未覆盖关联失败与端点未解析

R10 内追加两条（均先构造出候选，否则会被 `CandidateA < 0` 短路到 NO_THRESHOLD_EVENT）：
- `Frames[2].HasLastInputSeq=false` → `Label==Unknown`、`Reason=="INPUT_SEQ_MISSING"`；
- `Frames[2].ModelEndpointResolved=false` → `Label==Unknown`、`Reason=="MODEL_ENDPOINT_UNRESOLVED"`。

## 5. T75-F2（medium）：R03 缺 trace 时静默 skip 却计 PASS

**问题**：`ResolveTraceDirectory()` 返回 null 时只打印一行 skip 就 `return`，`RunTest` 记为 PASS —— 换台机器就复现不出这个绿，等于把「没跑」记成「跑过」。

**选择：直接 FAIL（不是 SKIP）**，理由：
1. 本套件没有 SKIP 计数维度（`Program.cs` 的 `RunTest` 只有 total/passed/failed），而 `Program.cs` 不在 t77 inScope，加 SKIP 就得越界改汇总逻辑；
2. FAIL 让数字自己说话：缺输入时汇总从 `766/768` 变成 `765/768` 且红名单里明确出现 `Bug1-72`，比「总数不变但少跑一条」诚实。

**实测证据**：把 `启动器/.../bug1-20261107T111121Z/HOST/trace.jsonl` 临时改名后跑主树 → `765/768 PASS (Failed: 3)`，唯一新增红为
`FAIL [Runtime] Bug1-72 R03 Real Trace Candidate Is Auth Not M1 (T71): Bug1ProbeM1: R03 需要真实 trace 输入但未找到：请设置 SPF_BUG1_TRACE_JSONL 指向 trace.jsonl，或把 bug1-20261107T111121Z/HOST/trace.jsonl 放在仓库父目录的 启动器/UnturnedModManager/publish/UMM-v2.2.1-win-x64 下`。
随后已把文件改回并核对 sha256 仍为 `f0627abd6e1da009bf30fe49ff91fa0128fa577614e89491afb5db37eb0580f8`（原始 11409404B 未变）。

## 6. low 三项

| 项 | 处理 |
| --- | --- |
| 变异表哈希不可逐字节复现 | 本报告已注明：**副本重建出的 DLL 哈希可能与主树不同（构建时间戳/路径），以源码 sha256 与红名单为准**；下表 DLL 仅作该次副本构建的标识 |
| `SpeedBoundary` 生产零调用 | 确实只被用例调用 → 改为让生产 `ClassifyHorizontalMotion` 用它算 `B_r = cap×dt+ε`（生产与用例同一算式），不再是死代码 |
| 回放硬编码 10MHz | 改为从 trace 读：`MonoFrequency` 取每帧 `"mono_frequency"`，`NativeSpeedCapUS` 取 `RUN_BEGIN` 的 `"native_speed_cap_u_s"`（缺失即 `return null` → R03 失败，不再用默认值兜底） |

## 7. 变异证据（仓库外副本 `../_t77mut`，跑完已 `rm -rf`）

副本基线：`766/768 PASS`，DLL `0e6ebcae…`（与主树逐字节一致）；每轮先还原 `Bug1ProbeJudge.cs` 与测试文件再改，先建生产工程再建测试工程。

| 变异 | 改法 | 变异后 DLL | 红名单 | 总数 |
| --- | --- | --- | --- | --- |
| MV-1 | `if (witness >= 0 && converged)` → `if (ComputeRenderRootSpanXZ(frames, s - 1, e) > eps)` | `8cb9c5a27e1f93c57f4d131ee4e5724928b8b73a3b80ac4f676d62385dab8d84` | **Bug1-77**（另连带 11/71/74/75） | 761/768 |
| MV-2 | `IsAuthStepLocked` 回退成「距段首 ≤ ε」 | `943335645bcb956489cfcc910b2cab1ce80e92b20d6526774a0da86e1c7ef2bb` | **Bug1-71** | 765/768 |
| MV-3 | `if (!ValidateCandidateAssociations(...))` → `if (false && !ValidateCandidateAssociations(...))` | `ec6f5f919aac3888ab0df3e82e3919a4c49792557e16ebc00b72a55e9221e9dd` | **Bug1-80** | 765/768 |
| MV-4 | 删 `!f.ModelEndpointResolved → MODEL_ENDPOINT_UNRESOLVED` 检查（改为 `false &&`） | `862ba59cfe9eb2374c511cc08b455be023ce47f1aa4b7a3c2158943a92a0efd1` | **Bug1-80** | 765/768 |

还原：`Bug1ProbeJudge.cs` 回到 `ee0ef9f0…`，副本重建 DLL 回到 `0e6ebcae…`（与主树一致）。

## 8. 三命令（主树最终）

| 命令 | 结果 |
| --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 已成功生成，0 警告 0 错误 |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 已成功生成，4 条既有警告 0 错误 |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `=== Final Result: 766/768 PASS (Failed: 2) ===` |

失败仅 `[PureMemory] GBA01 Native First Observe Binding` 与 `[PureMemory] NIL01 Real Native Lookup Contract`（既有环境失败，`TypeInitializationException: SDG.Unturned.SteamPlayer` 链）。Bug1 段 82/82 PASS，其中 Bug1-70…Bug1-81 为 M1 规则用例。exe 退出码 1 属既有约定（有失败即返回 1）。

## 9. 边界核验

- Bug3 五文件哈希未变：`a89f40b949f841d0`（`P2PGroupStateRecoveryPatch.cs`）、`7a82de8e38fa0f6a`（`GroupStateRecoveryCore.cs`）、`3d5044f7870fbb17`（`GroupLeaseLedger.cs`）、`a45533850f31bd96`（`GroupLoadPlanner.cs`）、`5b0c4e9ba4c87cbe`（`P2PGroupRegistration.cs`）。
- `Release/`、`publishi/` 未写：`find Release publishi -newermt "2026-11-07 21:00" -type f` 无输出。
- `WhitelistTests/Program.cs` 未改（sha256 与改前一致），t69 的 T69-01 注册行未被触碰且仍 PASS。
- 原始 `result.json`（`bug1-20261107T111121Z/HOST/`）未被覆盖或删除，R03 仍断言其保留 `M1StallCatchup`。

## 10. 未改项与待裁决

- **T75-F1（右截断优先级）未动**：同一步内已有闭合 M1、末尾静止无释放帧时输出 `label=Unknown reason=RIGHT_CENSORED m1=1`，属优先级语义未定义，等 Architecture 裁决后再改分类顺序与用例。
- 判定阈值、`D=0.020U`、`B_r` 严格 `>`、有效性门、`epsilon_u` 全部未调整。
- 本票只修判定器与用例，不修角色顿挫；「零 M1」不等于 Bug1 已修复。

## 11. 已自动化可证 vs 仍需实机

**已自动化可证**：M1 在 A/V 重合的窗口上数学上不可能成立（R02/R03/R07/R09）；AUTH 的段内最大成对距离口径（R02 对称抖动 + MV-2）；释放门严格 `>` 与 2cm 网格（R06）；全段遍历不遮蔽（R08 + MV-1）；关联/端点失败先判 UNKNOWN（R10 + MV-3/MV-4）；真实 trace 候选段回放归 AUTH（R03，缺输入即 FAIL）。

**仍需 1H+1G 实机**：Mono 下 `Queue<PlayerInputPacket>` 的 Enqueue/Dequeue detour 是否真触发；启动期重试在真实关卡时序下是否命中；`isDismissed` 是否存在；`a_pos` 与 `v_pos` 在真实帧上是否始终同点（本 trace 为 5737/5737 同点，但仅一台机一份样本）；右截断在真实局末的表现（待 T75-F1 裁决）。
