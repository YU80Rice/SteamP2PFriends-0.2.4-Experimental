# T37 工程轴复审（第 2 轮）：Bug1 三相位仪表的有界缓冲、热路径、假绿与扩张面

- 复审任务：t37｜被审任务：t35｜审查官：reviewer-eng
- 结论：**verdict = needs_revision**（生产实现本身未发现正确性缺陷；**测试证据没有穿透生产接线**，验收第 6 条不成立）
- 本轮为跑变异临时改过 `Bug1ProbeRxPhases.cs` 与 `Bug1ProbePatch.cs`，全部 `cp` 还原并核 sha256（见 §2 末）；未做 git 操作。

## 0. 直接回答本轮最要紧的问题

> 这些新用例到底证明了「生产接线真的在跑」，还是只证明了「状态机自己会算」？

**只证明了状态机自己会算。** 证据是实测的，不是推断：

- Bug1-14..19 六条用例全部形如 `var ledger = new Bug1RxPhaseLedger(); ledger.OnEnqueue(...)`（`Bug1ProbeRxPhaseTests.cs:24/40/51/66/85/97/121`），
  对 `Bug1ProbeRuntime.OnEnqueue/OnDequeue/OnReceiveInputsBefore/After` 与 `Bug1ProbePatch.Postfix_*` 的引用数为 **0**（全 `WhitelistTests/` grep）。
- 我把生产接线整段删掉（`Bug1ProbePatch.cs:176` 与 `:182` 两处 `Bug1ProbeRuntime.OnEnqueue/OnDequeue(...)` 换成注释），
  重建 DLL `e928ab8ee5c45362…`，全量跑 **703/705 全绿**，与基线一模一样。
- 也就是说：**即使三相位在真机上一条样本都不产出，这套用例也会报绿。**

缺的断言（具体要求见 R6-01）：IL 契约（钩子→Runtime→Ledger 的调用边）+ 一条能触达 Runtime 的运行时用例；
以及在实机清单里把「零 AFTER_ENQUEUE 行」定义为**仪表故障**而不是「无异常」。

## 1. 被审字节与实际字节锁

| 文件 | 字节 | sha256（前 16） | 说明 |
| --- | --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1ProbeRxPhases.cs` | 12052 | **734e6022**694efc80 | 与 Captain 给的一致 |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeJudgmentTests.cs` | — | **4ac41859**2146c27c | 与给的一致，但**不含**新用例 |
| `WhitelistTests/Program.cs` | 134202 | **684ae1a7**6beae712 | 与给的一致（Bug1-14..20 注册于 :915-921） |
| `bin/Release/SteamP2PFriends.dll` | — | **0e2d52e1**aeb7a388 | 与给的一致（Rebuild 后仍同哈希） |
| **`WhitelistTests/Evidence/Runtime/Bug1ProbeRxPhaseTests.cs`** | 11685 | **91d9d01e**d1c8c95f | **未在清单**：Bug1-14..20 的真正实现全在此文件 |
| **`Platform/Diagnostics/Patches/Bug1ProbePatch.cs`** | 8872 | **12f8cbf0**e1b9960b | **未在清单**：三相位接线（Enqueue/Dequeue/ReceiveInputs 钩子）在此 |
| `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs` | 36671 | 66af670c b2d71871 | **未在清单**：`_rx` 宿主与取数逻辑在此 |

→ 见 R6-03：按现有清单无法复核"真正被测的字节"，建议下轮补齐后三项。

## 2. 复跑（三条命令，本人实测）

| # | 命令 | exit | 结果 |
| --- | --- | --- | --- |
| 1 | `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 | 0 警告 0 错误；DLL `0e2d52e1…` |
| 2 | `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 0 | 0 错误 4 警告（CS0649×3 既有 + 1 既有） |
| 3 | `./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | 1 | **703/705 PASS (Failed: 2)** |

- Failed 仅 `GBA01 Native First Observe Binding`、`NIL01 Real Native Lookup Contract`（`TypeInitializationException: SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`，控制台缺 Unity），与 t35 自述一致。
- Bug1 段共 **20 条**（Bug1-01..20），Bug1-14..20 全 PASS。
- 还原后：`Bug1ProbeRxPhases.cs` = `734e6022…`、`Bug1ProbePatch.cs` = `12f8cbf0…`、重建 DLL = `0e2d52e1…`、复跑 703/705 —— 逐字节回到送审值。

## 3. 变异（自跑，不采信自述）

| 变异 | 改动点 | 变异 DLL sha256（前 16） | 结果 | 红名单 |
| --- | --- | --- | --- | --- |
| 基线 | — | 0e2d52e1 aeb7a388 | 703/705 | — |
| **M1** | `Bug1ProbeRxPhases.cs:222` 未命中时回退 `rxId = 1UL; hasRx = true;` | **ec975137**e50c41f9 | 702/705 | **Bug1-19** |
| M2 初版（作废） | `CompleteReceive` 条件改为 `\|\| Environment.TickCount == int.MinValue` | 7b63cbf2 4a5bb87b | 703/705（绿） | —（**行为等价变异**：条件恒 false，等于没改；绿是正常现象，不计入结论） |
| **M2** | `CompleteReceive` 首行 `if (active \|\| !active) return null;`（恒 null） | **22562868**c6d198f4 | 702/705 | **Bug1-17** |
| **M3** | `Bug1ProbePatch.cs:176/:182` 删除两处生产接线调用（钩子仍在、只不调 Runtime） | **e928ab8e**e5c45362 | **703/705（全绿）** | **无 —— 关键证据** |

- M1、M2 与 t35 自述的红名单一致（Bug1-19 / Bug1-17），本轮自述在这两条上没失真；DLL 哈希不同属预期（变异写法不同）。
- M3 是验收第 6 条要求的反向检验：**不红**，因此该条不通过。

## 4. 逐条 acceptance

| # | 判据 | 结论 |
| --- | --- | --- |
| 1 | 三命令复跑、Bug1 段用例数、DLL 哈希 | **通过**：703/705、20 条、0e2d52e1（与自述一致） |
| 2 | 512 FIFO 静默丢样本 + 下游是否误判为"无异常" | **通过（判 low，非 blocker）**：淘汰确实无计数，但缺 `rx_id` 在下游会写 UNKNOWN（`Bug1ProbeJudge.cs:373/390/402`），不会假绿；见 R6-02 |
| 3 | 热路径开销（关闭/开启两态） | **通过**：关闭态钩子不装（`Bug1ProbePatch.cs:38`），已装后每个 Enqueue/Dequeue 只做 1 次静态 bool 读 + 队列身份比较，无分配、无字符串拼接；开启态仅目标队列进入 JSON 构造（`StringBuilder(192)`/条，约 30 条/秒量级）。无法量化到 µs 级，但结构上可忽略 |
| 4 | 线程/重入与跨局残留 | **通过**：两处接缝均在 Unity 主线程（`ReceiveInputs` 与 `PlayerInput.Update`），静态状态仅 `_rx`；`OnSessionReset → Disable → _rx.Reset()`（`Bug1ProbeRuntime.cs:728`）+ `Enable` 也 Reset（:104），跨局不残留 |
| 5 | 副作用/只读性 | **通过**：全为 Postfix（或只读 Prefix），无 `return false`、不改参数；只读 `queue.Count` 与 `clientSimulationFrameNumber` 反射取值，不改包/队列，不消费枚举器 |
| 6 | 假绿：Bug1-15..20 是否走生产接线；删接线/恒空是否变红 | **不通过**：见 §0 与 M3；用例只测状态机 |
| 7 | 自跑 ≥2 条变异并逐字节还原 | **通过**：M1/M2/M3 三条 + 还原核验 |
| 8 | manifest 两项运行时身份为真值 | **通过**：`game_mvid = typeof(PlayerInput).Assembly.ManifestModule.ModuleVersionId`（`Bug1ProbeRuntime.cs:605`），`spf_assembly_sha256` = 读自身 `Assembly.Location` 流式现算（:621-629），无硬编码；用例以现算值比对（`Bug1ProbeRxPhaseTests.cs:157/176`） |
| 9 | 扩张面 | **通过（附归属说明）**：生产侧 t35 只动 `Bug1ProbePatch.cs`、`Bug1ProbeRxPhases.cs`（`Bug1ProbeRuntime.cs` 01:47 亦在窗口内）；`SteamP2PFriends.csproj` 相对暂存区仅 **+1 行**（`Bug1ProbeRxPhases.cs`，append）；Bug3 五文件哈希未变（见 §6）；`Build/Version.props` 的 0.2.4.10→0.2.4.11 属发布票（mtime 20:01，早于本票窗口），非 t35 改动 |
| 10 | 五条禁止项 / 原生 load 恰好一次 | 不适用（本票为 Bug1 仪表；契约中的该条针对 Bug3） |

## 5. findings

### R6-01（high）测试未穿透生产接线，"删掉实现仍然全绿"
- 位置：`WhitelistTests/Evidence/Runtime/Bug1ProbeRxPhaseTests.cs:22-131`（六条用例）、`Platform/Diagnostics/Patches/Bug1ProbePatch.cs:161-183`（未被任何用例引用）。
- 证据：M3 变异（删除 `:176`、`:182` 两处 Runtime 调用）后 703/705，与基线逐条一致。
- 影响：三相位在真机上可能**完全不产出**而自动化仍全绿；若采集者只看到"没有异常事件"，会把"仪表没接上"误读成"没有 Bug1 现象"。
- requiredFix（二选一必做，建议都做）：
  1. 用仓库已有的 `IlContractProbe`（Bug3 已用它验证过调用边）加 IL 契约：`Postfix_Enqueue → Bug1ProbeRuntime.OnEnqueue`、`Postfix_Dequeue → Bug1ProbeRuntime.OnDequeue`、`Postfix_ReceiveInputs → OnReceiveInputsAfter`、`Prefix_ReceiveInputs → OnReceiveInputsBefore`，各 `>= 1`；再加 `Bug1ProbePatch.ResolveTargets()` 返回 true 且 `EnqueueTarget/DequeueTarget.DeclaringType == typeof(Queue<PlayerInputPacket>)`（防 ABI 漂移后被 FailClosed 静默跳过）。
  2. 给 `Bug1ProbeRuntime` 开一个 `internal static` 测试缝（只赋 `_serversidePackets`/`_input` 引用，不触碰 Unity 类型），用真 `Queue<PlayerInputPacket>` 调 `OnEnqueue/OnDequeue`，断言 trace 出现 `AFTER_ENQUEUE`/`AFTER_DEQUEUE` 且非目标队列被过滤。
- 若本轮不修，则必须在实机说明里写死判据：**一次 run 的 trace 至少 1 条 `AFTER_ENQUEUE` 与 1 条 `AFTER_DEQUEUE`，缺失即判仪表故障（UNKNOWN），不得解释为"无异常"**。

### R6-02（medium）有界 FIFO 淘汰无计数、容量路径零覆盖
- 位置：`Bug1ProbeRxPhases.cs:251-263`（`Allocate` 的 `while (_rxOrder.Count > _capacity) _rxByInputSeq.Remove(_rxOrder.Dequeue());`）、`:143-146`（容量构造仅测试可达）。
- 事实：淘汰时不计数、不记日志；全部用例用默认 512 构造，`Bug1RxPhaseLedger(int capacity)` 无任何用例覆盖。
- 影响：不会假绿（下游 `RX_ID_MISSING/RX_ID_NULL` → UNKNOWN，见 `Bug1ProbeJudge.cs:373/390/402`），但分析时无法区分"被容量挤掉"与"探针晚启动/包从未见过"；淘汰逻辑本身写错也没有信号。
- requiredFix：`DroppedRxEntries` 计数并在 `RUN_END` 暴露；补一条 `capacity = 2` 的用例断言淘汰顺序与计数。

### R6-03（medium，交付完整性）被审字节清单漏了真正的新用例与接线文件
- 清单里的 `Bug1ProbeJudgmentTests.cs`（4ac41859…）只含旧 14 条；实际新增的 Bug1-14..20 在 `Bug1ProbeRxPhaseTests.cs`（91d9d01e…），接线在 `Bug1ProbePatch.cs`（12f8cbf0…），宿主在 `Bug1ProbeRuntime.cs`（66af670c…）。
- requiredFix：下轮字节表补这三项哈希；本报告已按实测哈希复核，结论对它仍有效，但"按清单可复核"的交付要求当前不满足。

### R6-04（low）"一次 ReceiveInputs 至多一次 Enqueue"是不变量却写成隐式
- 位置：`Bug1ProbeRxPhases.cs:203-211`——`_receiveEnqueued = true` 放在深度连续性检查**之后**。
- 事实：若将来原生在一次 `ReceiveInputs` 里入队两次（当前不可能：`PlayerInput.cs:1487` 是唯一入队点，且之前多条 early return），第二条会因 `depthAfter != before+1` 返回 null，`_receiveEnqueued` 仍为 false，随后 `CompleteReceive` 会补一条 `RX_REJECT`——同时出现"入队过"与"拒绝"两条记录，破坏互斥。
- requiredFix：把 `_receiveEnqueued = true;` 提到方法首行（语义变成"本次调用出现过入队尝试"），或在方法注释中把该 SDK 前提写死。

### R6-05（low，UNKNOWN）BCL 泛型方法 detour 的实际触发能力未证
- 位置：`Bug1ProbePatch.cs:122-123`（`typeof(Queue<PlayerInputPacket>).Enqueue/Dequeue`）。
- 事实：这是进程级 detour，`Verify()` 只证明"已注册"，不证明"会触发"；若 Mono 把 `Enqueue` 内联进 `ReceiveInputs`，postfix 可能永远不执行。离线无法证伪。
- requiredFix：不改代码，但必须由实机清单兜底（同 R6-01 的"零样本 = 故障"判据）。

## 6. 与 Bug3 的相互干扰（哈希未变）

`GroupStateRecoveryCore.cs` 7a82de8e、`P2PGroupStateRecoveryPatch.cs` a89f40b9、`GroupLeaseLedger.cs` 3d5044f7、
`GroupLoadPlanner.cs` a4553385、`P2PGroupRegistration.cs` 5b0c4e9b —— 与本票之前完全一致；Bug3 段用例本轮全绿。

## 7. 复现命令

```bash
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe        # 703/705，Failed=GBA01/NIL01

cp Platform/Diagnostics/Patches/Bug1ProbeRxPhases.cs /d/tmp/t37-rx.pristine.cs
cp Platform/Diagnostics/Patches/Bug1ProbePatch.cs    /d/tmp/t37-patch.pristine.cs
python D:/tmp/mut37.py M1        # 或 M2；M3 M3b 一起打（删生产接线）
# 每次：-t:Rebuild → cp DLL → 跑 exe → 记录 → python D:/tmp/mut37.py restore
```

> 陷阱：git-bash 的 `/d/tmp` 与 Windows Python 的路径不同，脚本内必须写 `D:/tmp/...`；还原只用 `cp`，不要 `git checkout --`（`core.autocrlf=true` 会改写 EOL）。

<!-- self-integrity: sha256(本行以上全部 12695 字节，不含本行) = b3bd1bbd3b0cd3a9a4fc0494ad999a51ae701e79c71d899b1493c10e7787de29 -->

