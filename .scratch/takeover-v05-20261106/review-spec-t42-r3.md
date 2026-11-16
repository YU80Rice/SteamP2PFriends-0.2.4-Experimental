# T43 规格轴复审 r3 —— Bug1 仪表修复轮（t42）：接线穿透与 sim_call_id 诚实性

- 审查者：reviewer-spec（规格轴）
- 被审任务：t42「Bug1 仪表修复轮：用例穿透生产接线 + sim_call_id 诚实性 + 守卫失败可观测」
- 本票 attempt：`4e12d938-d608-4511-a2ce-ac742e10c58f`
- 规格基准：`bug1-instrumentation-spec.md` §2/§3/§4；上一轮 `review-spec-t35-r1.md`
- 判定边界：只判「规格与取证诚实性是否闭合」，不重复工程轴 t44 的代码质量与假绿风险判定

## 1. 结论

**verdict = pass**（本判据只对第 2 节列出的字节有效）。

四条指控的处理结果：

| 指控 | 结果 | 决定性证据 |
|---|---|---|
| R6-01 用例绕开生产接线（删掉调用仍全绿） | **闭合** | 我自删 `Bug1ProbePatch.cs:176/:182` 两处调用后重建 → Bug1-21 红（边数=0），706/709 |
| F1 CONSUME 的 `sim_call_id` 填上一次 simulate | **闭合** | 出队记录恒 `null` + `unavailable.sim_call_id=consumer_simulate_not_determinable_at_dequeue`；`_lastSimCallId` 全仓 0 命中 |
| F2 守卫失败静默丢弃且同调用仍产 RX_REJECT | **闭合** | 先置 `_receiveEnqueued` 再判守卫；失败计 `GuardRejected` 并落 `valid:false` 记录；我回归该顺序 → Bug1-15 立刻红 |
| F3/F5/R6-02 schema 与淘汰/复用 | **闭合** | `unavailable` 统一 `{字段:原因}`；`valid` 由 invalidReasons 计算；淘汰用 `KeyValuePair<seq,rx>`；复用计 `superseded` 并打 `rx_seq_reuse` |

**pass ≠ Bug1 已诊断，更 ≠ 已修复**：本票只证明「测量能力已交付且缺样时如实 UNKNOWN」，见第 7 节。

## 2. 被审字节绑定（本机实测）

| 文件 | 字节 | sha256 |
|---|---|---|
| `Platform/Diagnostics/Patches/Bug1ProbePatch.cs` | 8872 | `12f8cbf0e1b9960bc6fc9512aeb4538d367f63db67da2d32930501d3ded031fd` |
| `Platform/Diagnostics/Patches/Bug1ProbeRxPhases.cs` | 16693 | `8d0d36fc94cb1d6b8ba98fb99e73c6e1691ccee04e2f7d928ad3830f1964ca4a` |
| `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs` | 41195 | `7f8a21f4396f6d11b6d5847f6ac53708b207fdd37f0dc792bf852258888e4301` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeRxPhaseTests.cs` | 20013 | `88371a2e789af84b006ba825e4e521a051bdbf27812b9b487cf09172372d1cb2` |
| `WhitelistTests/Program.cs` | 135010 | `ce65f375781e9a517d42442b2ee3ccba5d6e68fc8d7dde766728a6ffce21c0de` |
| `SteamP2PFriends.csproj` | 27174 | `a4ed529c93c3e5bf33a9575cbd81bb1db0a3cdc96ec19e89f659cdcef52e4280` |
| `bin/Release/SteamP2PFriends.dll`（= 测试目录同名副本） | 1462272 | `77700e378483f7d62c08cb986f415f8cd1233e86a5c4c44c34bc0abc2aba1aa5` |

与派工给定的五个哈希、DLL 哈希逐字一致，无交付漂移。`Bug1ProbePatch.cs` 哈希与 t35 相同（t42 未改接线文件），与 t42 自报的 changedPaths（4 文件）一致。

## 3. 审查方式与取证条件（必须在案）

本票的两条变异**没有在主工作树里跑**。原因：取证时发现工程轴 t44（`reviewer-eng`，in_progress）正在**同一棵树上做同样两条变异**——03:08 观测到 `Bug1ProbePatch.cs` 变为 `3700f6a4…`（两处生产调用被删）、03:09:25 观测到 `Bug1ProbeRuntime.cs` 变为 `4546e735…`（两处 `ReferenceEquals` 过滤被删），两者随后复原。我的第一条变异命令因此被"文件已是删除态"打断（`sed` 未命中、`grep MUT-A` 返回 0 → 链式中断，未发生构建与运行）。

为避免双方互相污染红名单，我改用**字节级隔离副本** `../spf-t43-iso`（仓库外同级目录，`..\Libs` 相对引用仍成立）：

- 副本中五个文件哈希 = 第 2 节送审值（逐项核对）；
- 副本基线 `-t:Rebuild` 产出的 DLL = `77700e37…`（与送审逐字节一致）、运行 **707/709**、Bug1 段 **24/24**；
- 每条变异后均重建、重跑、再 `cp` 还原并复验哈希与基线；
- 主树在全部取证结束后复核：五个文件均回到送审哈希，DLL 仍为 `77700e37…`。

结论：判定绑定的是送审字节；变异证据来自字节相同的隔离副本，不采信 Builder 自述。

## 4. 逐条验收判定

### 4.1 R6-01 是否真闭合 —— **pass**

不采信自述，自跑两条删除：

| 变异 | 变异后源码 / DLL sha256 | 结果 |
|---|---|---|
| A：同时删 `OnEnqueue`+`OnDequeue` 两处调用 | Patch `a74d91b0…` / DLL `00b39417…` | **706/709**，红 **Bug1-21**：`Postfix_Enqueue 未调用 Bug1ProbeRuntime.OnEnqueue，实际边数=0` |
| A2：只删 `OnDequeue` 一处 | Patch `99cc51f3…` / DLL `5374b393…`（见第 5 节全值） | **706/709**，红 **Bug1-21**：`Postfix_Dequeue 未调用 Bug1ProbeRuntime.OnDequeue，实际边数=0` |

A2 的意义：证明四条边是**逐条独立断言**、且失败信息能定位到具体哪条边（不是笼统一条"wiring ok"）。历史上"删掉调用仍全绿"的缺陷形态已被覆盖。

### 4.2 Bug1-21 的 IL 契约能否抓 ABI 漂移 —— **pass**

- `WhitelistTests/Evidence/Runtime/Bug1ProbeRxPhaseTests.cs:167-188` 用 `hooks[]`/`entries[]` 数组逐边断言 `IlContractProbe.CountMethodCalls(hook, runtimeName, entry) >= 1`，断言消息含具体 hook 名与边数（实测输出即点名 `Postfix_Dequeue`）。
- `IlContractProbe.CountMethodCalls`（`WhitelistTests/Evidence/StaticIL/IlContractProbe.cs`）是真 IL 解析：`GetMethodBody()` + 遍历操作数 + `ResolveMethod(InlineMethod)`，不是字符串/反射桩。
- `EnqueueTarget/DequeueTarget.DeclaringType == typeof(Queue<PlayerInputPacket>)` 位于 `Assert(...)` 内（:182-187），且整段**不在 try/catch 内**，抛错即红，无吞异常。
- 残余（不构成 finding，见 O2）：契约证明"编译产物里存在这条调用边"，但不会执行 hook 本体；hook 若被提前 `return` 短路仍可通过。

### 4.3 Bug1-22 的测试缝是否真经生产接线 —— **pass**

- `Bug1ProbeRxPhaseTests.cs:194-231`：真实 `new Queue<PlayerInputPacket>()`、真实 `PlayerInputPacket`（只反射赋 `clientSimulationFrameNumber`，不构造 Unity 对象），经 `__TestEnable(...)` 后**直接调用 `Bug1ProbeRuntime.OnEnqueue/OnDequeue`** 两条生产入口，而不是直调 `Bug1RxPhaseLedger`。
- 非目标队列过滤是真断言：同一用例对 `foreign` 队列也 `Enqueue` + `OnEnqueue(foreign, packet)`，断言 `trace.Length == 2`。
- 我自跑变异 B：删掉 `Bug1ProbeRuntime.cs:327/:348` 的 `!ReferenceEquals(queue,_serversidePackets)`（源码 `0d8efa26…`、DLL `6fe814fd…`）→ **706/709**，唯一新增红 **Bug1-22**：`目标队列应恰好产出 2 条相位记录（非目标队列必须被过滤），实际=3`。

→ 该断言不是假绿；若过滤被移除，用例必红。

### 4.4 F1 是否诚实 —— **pass**

- 出队记录：`Bug1ProbeRxPhases.cs:147` 恒写 `"sim_call_id":null`；`:153-158` 写入 `unavailable:"sim_call_id":"consumer_simulate_not_determinable_at_dequeue"`（原因具体，不是占位）。
- `_lastSimCallId` **全仓 0 命中**（Platform/Core/WhitelistTests 三目录扫描，only `_simCallIdOpen`、`sim.SimCallId` 命中，属 SIM 相位自身 ID）。
- 下游不存在"把 null 当 0 / 当上一次 simulate"的路径：`sim_call_id` 的非空产出点只有 `Bug1ProbeRuntime.cs:432`（SIM BEFORE）与 `:466`（SIM AFTER）；`Bug1ProbeJudge.cs:268/:382` 只按 SIM 相位自带的 `SimCallId` 配对未配对项，**从不读取 CONSUME 片段的 sim_call_id**；跨相位配对改由 `TakePending` + `consume_id` 完成（`Bug1ProbeRxPhases.cs:309-315`）。
- 用例侧同步固定：:`128-131` 断言 null 与 reason 同时出现，任何人把旧"上一次 simulate"写法改回来都会红。

### 4.5 F2 是否可观测且不误标 —— **pass**

- 顺序修正确实在关键处：`Bug1ProbeRxPhases.cs:266` 在判守卫**之前**置 `_receiveEnqueued = true;`，`:267-272` 之后才判 `guardRejected`。因此 `CompleteReceive`（:245-255）在"真实 Enqueue 已发生但守卫未过"时返回 null，不再落 RX_REJECT。
- 我自跑变异 C（把 `_receiveEnqueued = true` 挪回守卫之后，即 t35 旧行为）：RxPhases `ccc967c8…`、DLL `261136c0…` → **706/709**，唯一新增红 **Bug1-15**，报错正文正是 F2 症状：`真实入队已发生（只是不干净）时不得再记成 RX_REJECT，实际="reject_reason":null,...`。→ 该修复被用例真实绑定。
- 守卫失败**同时**有计数与不可用记录：`RxPhases:270` `_guardRejected++`；`Bug1ProbeRuntime.cs:335-337` 落 `phase=ENQUEUE_GUARD_REJECTED` 且传 `invalidReasons:{"enqueue_depth_discontinuity"}` → `Record` 写 `valid:false` + `invalid_reasons`（`Runtime:534/551-553`）。`LastError` 亦由异常分支兜底。
- RUN_END 四个计数确实取自运行时计数器而非常量：`Runtime:616-619` 分别读 `_rx.GuardRejected` / `_rx.EvictedRxEntries` / `_rx.SupersededRxEntries` / `_traceDropped`；`_traceDropped` 在 `Record` 上限分支（`:533`）真实自增。

### 4.6 F3 / F5 / R6-02 —— **pass**

- `unavailable` 统一为 object（字段→原因）：`Bug1PhaseJson.AppendUnavailable`（`RxPhases:49-64`）与 `Missing`（:67-68）生成 `{...}`；RX、ENQUEUE_GUARD_REJECTED、RX_REJECT、CONSUME 四类片段同一形状；manifest（`Runtime:596` 附近）与 FRAME 关联（`:520`）同为 object。全仓已无 `"unavailable":[` 数组形状（grep 0 命中）。
- 记录级 `valid` 真实计算：`Runtime:534` `bool valid = invalidReasons == null || invalidReasons.Length == 0;`，非空时追加 `invalid_reasons`（:552-553）。调用方按语义传因（守卫拒绝、CONSUME 缺序号、RENDER_READY 关联不全）。
- 淘汰防误删：`_rxOrder` 为 `Queue<KeyValuePair<uint,ulong>>`，淘汰时先比对"当前映射是否仍是该 rx"再删（`RxPhases:327-337`）；复用计入 `_superseded` 且记录带 `rx_seq_reuse:true`（:321-326、:81）。对应用例 Bug1-23（容量淘汰）与 Bug1-24（重复序号可观测）均 PASS。

### 4.7 是否越界 —— **pass**

- Bug3 五文件哈希未变：`P2PGroupStateRecoveryPatch.cs a89f40b9…`、`GroupStateRecoveryCore.cs 7a82de8e…`、`GroupLeaseLedger.cs 3d5044f7…`、`GroupLoadPlanner.cs a4553385…`、`P2PGroupRegistration.cs 5b0c4e9b…`。
- `SteamP2PFriends.csproj`：`git diff HEAD` 删除行数 **0**，新增两处均为 `Compile Include` 纯追加（其中 `Bug1ProbeRxPhases.cs` 为新增文件的编译登记，属必要追加）。
- `WhitelistTests/Program.cs`：`git diff HEAD` 删除行数 **0**，新增 15 + 63 行（全部为新增 RunTest 登记）。
- `Release/`（最新 01:32）、`publishi/`（20:03）、`Build/Version.props`（20:01，版本号 0.2.4.10→0.2.4.11 的既有改动）时间戳均早于 t42 窗口（02:41–02:57），本轮未被触碰。

### 4.8 测量能力 vs 诊断 —— **pass**

t42 交付说明与代码均未宣称根因；三个相位只读原生状态、不插值、不伪造。仍须实机的条目见第 7 节。

## 5. 变异记录汇总（隔离副本，均已还原）

| 编号 | 改动 | 源码 sha256 | DLL sha256 | 结果 | 红名单 |
|---|---|---|---|---|---|
| 基线 | 无 | 送审五值 | `77700e37…` | 707/709 | 仅 GBA01/NIL01（环境） |
| A | 删 Patch:176+182 两处调用 | Patch `a74d91b0…` | `00b39417…` | 706/709 | Bug1-21 |
| A2 | 只删 Patch:182 | Patch `99cc51f347401fa801dc2fcdb1ce82f554d669e6ca88b69cbe1ca8bca06b1fb7` | `5374b3931de00e09d275e23776f1e3152380dcee572b1dc4c12e24b8e09d321f` | 706/709 | Bug1-21（点名 Postfix_Dequeue） |
| B | 删 Runtime:327/348 队列身份过滤 | Runtime `0d8efa26fba116260ad93b89e94db43550a2c5ac277fa5fc6357515b371c9d50` | `6fe814fdb6b6fe486414c43cc2fc4a2c8baa0bbaf5067d42df2acaccdffe9e7c` | 706/709 | Bug1-22 |
| C | 把 `_receiveEnqueued=true` 移到守卫之后 | RxPhases `ccc967c8529fda428c94a6ed50bdca44dba36911ef6f901886e398b641ed4161` | `261136c0fb9de56835ea72e202ec6be9209a84d23b9c0d6dc5e78ea930c31859` | 706/709 | Bug1-15 |

还原后：Patch `12f8cbf0…`、RxPhases `8d0d36fc…`、Runtime `7f8a21f4…`、DLL `77700e37…`、707/709、Bug1 24/24。

## 6. 观察项（不构成 findings，不影响 pass）

- **O1（流程）**：本票取证期间，工程轴 t44 在同一工作树上做同源变异（03:08 Patch→`3700f6a4…`、03:09 Runtime→`4546e735…`），两个审查官若同时构建会互相污染红名单。本轮以隔离副本规避；建议后续「变异类复审」统一在仓库外副本执行，或由 Captain 串行派工。
- **O2（覆盖）**：Bug1-21 是 IL 静态契约 + Bug1-22 是 Runtime 入口直调，两者合起来覆盖"hook 存在且调用 Runtime"与"Runtime 产生正确相位"，但**都不执行 Harmony hook 本体**；hook 若在调用前提前 `return`，两条用例都不会红。目前 hook 只限制于 `if (!Bug1ProbeRuntime.Enabled) return;`，属必要门控，暂无风险。
- **O3（口径）**：RX_REJECT 记录仍是 `valid:true`（其 null rx_id/inputseq 已配 `unavailable` 原因）。按 §3.1「全部 null 必须伴 unavailable」合规；若下游按 `valid` 反向排除"无关联"记录，需在 SDK 阶段确认该口径。

## 7. 仍需 1H+1G 才能判定（UNKNOWN，不计入本轮 pass）

1. **R6-05**：真实 Mono 下 `Queue<PlayerInputPacket>.Enqueue/Dequeue`（BCL 泛型实例方法）是否被内联/未被 detour → 若相位永不触发，表现是窗口缺样（UNKNOWN），不会伪造样本。
2. `PlayerInput.isDismissed` 是否存在于发布版；缺失时 RX_REJECT 的 `reject_reason` 只能 null + `native_reject_branch_unresolved`。
3. `PlayerInput.ReceiveInputs` 的提前拒绝分支（早返回）是否仍留可辨 bracket；不留则 RX_REJECT 相位在实机中可能缺样。
4. 出队前深度 `queue_depth_before` 无独立接缝（已按 null+unavailable 处理，属设计内限制）。
5. 全部相位字段在真实 1H+1G trace 中的联动是否成立（`valid` 关联链、RUN_END 计数与丢样一致性）。

## 8. 判定边界

- 本判定只对第 2 节字节有效；任何后续重建、任何人再改动上述文件，本结论不自动延续。
- 本票不判代码风格、不判假绿之外的工程质量（t44 负责），不判 Bug1 根因。
- 本票未修改任何生产/测试源码；变异只在仓库外隔离副本执行并已还原。

---

本报告主体（不含本行）字节绑定：14353 B / sha256 `6e279117b13ad6d9d91e723e33173e2752de7b541e52db9d101935dbc138ee3f`。本判定只对 t43 派工所列六字节（Bug1ProbePatch 12f8cbf0 / Bug1ProbeRxPhases 8d0d36fc / Bug1ProbeRuntime 7f8a21f4 / Bug1ProbeRxPhaseTests 88371a2e / Program.cs ce65f375 / csproj a4ed529c）与 DLL 77700e37 有效；变异证据取自字节相同的隔离副本 ../spf-t43-iso 并已还原。
