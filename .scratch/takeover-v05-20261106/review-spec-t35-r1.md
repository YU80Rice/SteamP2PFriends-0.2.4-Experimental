# T36 规格轴审查：Bug1 仪表三相位（RX/AFTER_ENQUEUE、RX_REJECT、CONSUME/AFTER_DEQUEUE）

- 审查官：reviewer-spec（规格轴，只判"是否符合 spec 与取证是否诚实"，不评风格、不判 Bug1 根因）
- 被审任务：t35（ds-probe），attempt 交付；复核 attempt = `420735b1-18a9-4768-8f05-b23c8186cb27`
- 规格基准：`bug1-instrumentation-spec.md`（224 行，SHA-256 `502a52f7959033c34510704f34e2cbb4f3081ea1f978d094c69e9426fa620c90`，与 T4 冻结值一致）
- 审查时间：2026-11-07 02:08–02:20（Asia/Shanghai）
- **verdict：`needs_revision`**（1 条验收判据明确不成立 → F1；1 条判据只能部分成立 → F2；其余 6 条通过）

## 1. 结论摘要

三相位**确实接在原生接缝上**，没有用入口 Postfix 冒充入队成功；rx_id / inputseq / queue_depth 都取自原生对象；CONSUME 只接受本次真实出队包查表，查不到就 null，自跑变异可使其变红。这部分成立，本票的"测量能力已交付"是可判的。

不成立的是**一处字段诚实性**：CONSUME 记录的 `sim_call_id` 用的是"出队时刻最近一次已完成的 simulate"（`_lastSimCallId`），并以普通字符串写入，没有 null + unavailable 标记。这正是验收判据⑤明文禁止的"用最近 simulate 反推后当已知值使用"。t35 在任务描述里把这条列为 UNKNOWN，但**trace 字节里没有任何东西告诉下游"这个 id 是上一帧的"** —— 记述与产物不一致，属取证不诚实。

另有一处守卫语义与自述不符：深度守卫不通过时是**静默丢弃**（无计数、无 unavailable、无错误），且此时同一次 ReceiveInputs 调用仍会产出 RX_REJECT（"已入队却记为拒绝"）。验收判据②要求说明并标明这种情况，实现未标。

## 2. 被审字节绑定

本判定只对下列字节有效（2026-11-07 02:16 取样；`sha256` 全值）：

| 文件 | 字节 | SHA-256 |
|---|---|---|
| `Platform/Diagnostics/Patches/Bug1ProbeRxPhases.cs` | 12052 | `734e6022694efc80858d670098ff57cc24e2551b019a1163f004a5c00fc66193` |
| `Platform/Diagnostics/Patches/Bug1ProbePatch.cs` | 8872 | `12f8cbf0e1b9960bc6fc9512aeb4538d367f63db67da2d32930501d3ded031fd` |
| `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs` | 36671 | `66af670cb2d718713d680f070f092df18c612839d6bf7288e4cd237ff7f853a0` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeRxPhaseTests.cs` | 11685 | `91d9d01ed1c8c95f442d75613739728c50e5d9d8673d30174d501a1a0e1fea38` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeJudgmentTests.cs` | 14591 | `4ac418592146c27ca808bb40a0a1c200b7f6f6559991fa3e8f45d968d075df84` |
| `WhitelistTests/Program.cs` | 134202 | `684ae1a76beae71247433dbe83706df19e2082e7bbab1647e0eb8b67da0bdf5` |
| `bin/Release/SteamP2PFriends.dll`（-t:Rebuild 复现） | 1459200 | `0e2d52e1aeb7a388a2f69e5b4957598d9042b7a61a9c1cde9ab32ba81de5f3c7` |

- 派工给定的三个哈希（734e6022 / 4ac41859 / 684ae1a7）与磁盘逐一相符；DLL `0e2d52e1…` 在本机 `-t:Rebuild` 后**逐字节复现**。
- 派工的被审清单**漏了 `Bug1ProbeRxPhaseTests.cs`**（11685B），而 6 条新用例（Bug1-15..20）全在该文件里。本报告已补绑；F4 记录该清单缺口。
- **审查期间字节漂移（已恢复）**：02:14:15 该文件变为 12093B/`84b063c0…`，02:14:55 变为 12050B/`f8ca2ae8…`，02:15:43 恢复为 12052B/`734e6022…`；`Bug1ProbePatch.cs` 同刻 mtime 被触碰但哈希未变。恢复后哈希与派工一致，故本判定有效；但交付窗口内文件被并发改写本身违反"审查期间字节冻结"（F4）。

## 3. 三相位逐条判据

| 相位 | spec §2 要求 | 实现接线 | 判定 |
|---|---|---|---|
| `RX / AFTER_ENQUEUE` | 原生 ReceiveInputs 完成解析、序号资格检查且**真实 Enqueue 之后**；入口 Postfix 不算 | `Bug1ProbePatch.cs:79` + `:173-177` 打 `Queue<PlayerInputPacket>.Enqueue` Postfix（`__instance, item`）；`Bug1ProbeRuntime.cs:323` 用 `ReferenceEquals(queue, _serversidePackets)` 只认目标玩家的 serversidePackets；`:326-330` 深度取 `queue.Count`、序号取包自身 | **通过**（非入口冒充）；守卫弱点见 F2 |
| `RX_REJECT` | 原生提前拒绝分支可辨时；原因不可得为 null、不补读 reader | `Bug1ProbePatch.cs:78` ReceiveInputs Prefix/Postfix；`Bug1ProbeRuntime.cs:293` Prefix 只记深度、`:307-311` Postfix 结算；`Bug1ProbeRxPhases.cs:187-197` 本次调用"产出过 AFTER_ENQUEUE"才返回 null，否则产 REJECT；`:82-95` `reject_reason` 缺则 null + unavailable，rx_id/inputseq 恒 null + unavailable | **通过**；互斥实现有洞（F2） |
| `CONSUME / AFTER_DEQUEUE` | 原生选包出队后、simulate 之前；只读该包 clientSimulationFrameNumber，不拿最近包冒充 | `Bug1ProbePatch.cs:80` + `:179-183` 用 Postfix 参数 `__result`（真实出队包）；`Bug1ProbeRuntime.cs:343-345` 用该包序号查表；`Bug1ProbeRxPhases.cs:217-238` 查不到 `hasRx=false` → `rx_id:null` + unavailable；`:112` `queue_depth_before` 恒 null + unavailable（不用 after+1） | **通过**（rx_id/深度诚实）；`sim_call_id` 不诚实（F1） |

## 4. 八条验收判据逐条

1. **逐相位是否取真实原生接缝、"没有用入口 Postfix 冒充入队成功"** —— **通过**。证据：`Bug1ProbePatch.cs:122-123` 解析 `Queue<PlayerInputPacket>.Enqueue/Dequeue`，`:50-60` 任一 target 解析失败即 `FailClosed` 并报 ABI；`:173-183` 两个 Postfix 只读；`Bug1ProbeRuntime.cs:323` 队列身份校验。全仓 grep：AFTER_ENQUEUE 只能由 `OnEnqueue` 产出，`ReceiveInputs` Postfix 只产出 RX_REJECT（`:311`）。
2. **AFTER_ENQUEUE 深度守卫判别力 / 静默情况** —— **部分不成立**（F2）。守卫在 `Bug1ProbeRxPhases.cs:205`：`observedDepthBefore.HasValue && depthAfter != before+1 → return null`。两种失效形态：①未观察到 before（Enqueue 不在 ReceiveInputs 括号内）时守卫整个跳过，仍产出 AFTER_ENQUEUE（`queue_depth_before:null` + unavailable），即"未经解析路径的入队"会被记成 AFTER_ENQUEUE；②深度不连续时**静默丢弃**：无计数、无 unavailable、无 LastError，且不置 `_receiveEnqueued`，导致同一次调用随后产出 RX_REJECT。用例 Bug1-15 只断言"返回 null"，不检验丢弃是否被记录。
3. **RX_REJECT 与 AFTER_ENQUEUE 真互斥；isDismissed 取不到时 null+unavailable** —— **通过（正常路径）**。互斥依据：`Bug1ProbeRxPhases.cs:187-197`（`_receiveEnqueued`）；原因：`Bug1ProbeRuntime.cs:307` 只把 `IsDismissedRaw()==true` 映射为 `"dismissed"`，`:374-388` 字段缺失/读取异常返回 false → `:82-95` 写 `reject_reason:null` + unavailable。边界见 F2（守卫失败时互斥失效）。
4. **CONSUME 只用真实出队包查表、查不到 null** —— **通过，且经自跑变异证伪**。变异把 `Bug1ProbeRxPhases.cs:222` 的"未命中 → 0/null"改成回退 `rxId=1`，Bug1-19 立即变红（§5）。
5. **三字段诚实性；AFTER_DEQUEUE 的 queue_depth_before 与跨相位 sim_call_id 必须明确 null+unavailable** —— **不成立**（F1）。前半通过：inputseq 来自 `PlayerInputPacket.clientSimulationFrameNumber`（`Bug1ProbeRuntime.cs:354-363`），queue_depth 来自 `queue.Count`（`:326/:342`），rx_id 来自本地表；`queue_depth_before` 已 null + unavailable（`Bug1ProbeRxPhases.cs:112,114-115`）。**后半失败**：CONSUME 的 `sim_call_id` 由 `Bug1ProbeRuntime.cs:344` 传入 `_lastSimCallId`，该值只在 `OnSimAfter`（`:455`）赋值，即"上一个已完成的 simulate"，而消费本包的 simulate 是**下一次**；`Bug1ProbeRxPhases.cs:108` 把它当普通字符串写出，`unavailable` 里不含 `sim_call_id`。
6. **manifest 为运行时真值；703/705、Bug1 段 20 条、DLL 可复现** —— **通过**。`game_mvid` = `typeof(PlayerInput).Assembly.ManifestModule.ModuleVersionId`（`Bug1ProbeRuntime.cs:605`），`spf_assembly_sha256` = 自身 DLL 流式哈希（`:616` 起、`:581-598` 带 unavailable object）；用例 Bug1-20 断言 SHA 等于现算文件哈希。本机复跑：插件 `-t:Rebuild` 0 警 0 错、DLL `0e2d52e1…` 逐字节复现；用例集 703/705（唯一失败 GBA01/NIL01，`TypeInitializationException→ReflectionTypeLoadException` 环境缺失）；Bug1-14..20 全 PASS。
7. **spec §4 禁止项：缺样/截断/相位错误一律 UNKNOWN、无内插、无骨骼/bounds 顶替** —— **通过（就新增相位而言）**。新增三相位不含任何 Transform/骨骼/bounds 采样；`Record()`（`:512-535`）不做插值、不复制上帧；CONSUME 深度缺则 null。**但**记录级 `valid:true` 恒真、`invalid_reasons` 从不产出、RX/CONSUME 的 `unavailable` 是"字段名数组"而非 spec §3.1 的 `map<string,string>`（manifest 却是 object）——见 F3，属 schema 偏差而非伪造样本。
8. **"测量能力已交付" vs "Bug1 已诊断"** —— **通过（表述层）**。t35 交付说明明确"本票只产出测量能力，不构成 Bug1 根因结论；真实结论仍需 1H+1G"；代码里也没有任何修复/插值/重排行为。仍需 1H+1G 的条目见 §7。

## 5. 自跑变异（不采信 Builder 自述）

| 项 | 值 |
|---|---|
| 变异点 | `Bug1ProbeRxPhases.cs:222` `if (!hasRx) rxId = 0UL;` → `if (!hasRx) { rxId = 1UL; hasRx = true; }`（出队查表未命中时回退默认 rx_id） |
| 变异源码 SHA-256 | `f4d23bc553e8f48a4e2a398ab6d36f9e93fb4b9c8e8c55b2d981e149149353af` |
| 变异 DLL SHA-256 | `ec975137e50c41f99ab720b28241b60083d913c9c15c48d447a8304f2ccd9f23`（`-t:Rebuild`，0 警 0 错） |
| 运行结果 | `=== Final Result: 702/705 PASS (Failed: 3) ===`；新增唯一红 = `Bug1-19 Phase Consume Unknown Seq No Substitution (T35)`，报错正文显示 `"rx_id":"1"` 且 `unavailable:["queue_depth_before"]` |
| 还原 | `cp` 回 golden → 12052B / `734e6022…`；`-t:Rebuild` → DLL `0e2d52e1…`（与送审逐字节一致）；重跑 703/705、Bug1-19 PASS |

结论：判据④的"不得借用最近包/默认值"确实被用例咬住；判据①/②里更强的那条（守卫失败必须留痕）**没有**用例咬住。

## 6. Findings

**F1 — CONSUME 的 `sim_call_id` 把"上一次 simulate"当已知值写进 trace（high，阻塞判据⑤）**
- problem：`Bug1ProbeRuntime.cs:344` 用 `_lastSimCallId` 填 CONSUME 的 `sim_call_id`；该字段只在 `OnSimAfter`（`:455`）更新，语义上是"本包之前那次 simulate 的 id"，而消费本包的是紧随其后的下一次 simulate。`Bug1ProbeRxPhases.cs:108` 无条件下写普通字符串，`unavailable` 中也无此字段。下游按 `sim_call_id` 把 CONSUME 与 SIM 配对会稳定错位一帧；t35 文档承认它是近似值，但产物没有承认。
- requiredFix：出队时无法确定消费者就把 `sim_call_id` 写 `null` 并列入 `unavailable`（跨相位配对继续由 SIM 侧 `TakePending`/`consume_id` 完成）；若坚持保留该值，必须另起字段名（如 `last_completed_sim_call_id`）并在 §3.2 契约与用例中登记，不得占用 `sim_call_id`。验收方式：Bug1-18 增补断言"未提供消费者时 `sim_call_id:null` 且 `unavailable` 含 `sim_call_id`"。

**F2 — 深度守卫失败静默丢弃，且此时仍产出 RX_REJECT（medium，阻塞判据②）**
- problem：`Bug1ProbeRxPhases.cs:205` 守卫失败直接 `return null`；无计数/无错误/无 unavailable（`Bug1ProbeRuntime.cs:328-330` 也直接返回）。`_receiveEnqueued` 只在产出记录之后（`:208`）置位，因此同一次 ReceiveInputs 若真实入队但未通过守卫，Postfix 会产出 RX_REJECT（`Bug1ProbeRuntime.cs:309-311`）——把"入队"记成"拒绝"。若原生某次调用入队多包（第二包深度必然 != before+1），会同时丢一条 AFTER_ENQUEUE 并误记一条 REJECT。
- requiredFix：守卫失败时置显式标记（计数器 + `LastError`/`unavailable`，例如 `enqueue_guard_rejected`），并在同一次调用的 REJECT 判定里排除"本次已发生 Enqueue"的情况；RUN_END 或后续 RUN_BEGIN 呈报该计数。验收方式：新增用例"深度不连续时既无 AFTER_ENQUEUE 也无 RX_REJECT，且守卫拒绝计数=1"。

**F3 — 记录级 `valid` 恒真、`invalid_reasons` 缺失、`unavailable` 形状与 §3.1 不符（low）**
- problem：`Bug1ProbeRuntime.cs:531` 所有记录写 `"valid":true`，从不产出 `invalid_reasons`；`Bug1ProbeRxPhases.cs:46-61` 的 `unavailable` 是字段名数组（无原因），而 spec §3.1 定义为 `map<string,string>`（field→reason），manifest 又用 object（`Bug1ProbeRuntime.cs:596`）——同一 trace 文件同一键两种形状。
- requiredFix：统一为 object 并附原因；或由 Architect 修订 §3.1 并在 manifest/trace 两侧同步。非本轮阻塞项（判定器不读 JSONL，靠内存窗口重算）。

**F4 — 交付清单漏项与审查期字节漂移（low，流程）**
- problem：派工只给了 3 个文件哈希，6 条新用例所在的 `Bug1ProbeRxPhaseTests.cs`（11685B）未列入；审查窗口内 `Bug1ProbeRxPhases.cs` 在 02:14:15 / 02:14:55 两次被改写后 02:15:43 复原（§2）。
- requiredFix：交付清单补齐测试文件哈希；审查期间冻结字节（确认无残留写者，t35 已 completed、ds-probe 已 idle，需说明两次写入来源）。

**F5 — 重复 inputseq 覆盖旧 rx 映射（low，数据边界）**
- problem：`Bug1ProbeRxPhases.cs:254-258` 对已存在的 inputseq 直接覆盖为新的 rx_id，旧包的关联被顶掉；若原生 32 位序号环绕/重放未被拒绝，旧包出队时会链到新 rx。
- requiredFix：在 trace 中标注序号复用（或按会话拆段并记 `seq_reuse`），并在 1H trace 里核对是否出现；不要求本轮改结构。

## 7. 仍为 UNKNOWN、必须靠 1H+1G 才能判定

1. **相位是否真的会触发**：`Queue<PlayerInputPacket>.Enqueue/Dequeue` 是 BCL 泛型实例方法，Mono 下存在被内联而绕过 Harmony detour 的可能；离线用例只覆盖纯托管 ledger，`OnEnqueue/OnDequeue` 的真实触发无法在本机证明（这是本票最大的剩余风险，表现为"窗口缺样→UNKNOWN"，不会伪造样本）。
2. t35 自报四项：CONSUME 的 `queue_depth_before` 无独立接缝（已按 null+unavailable 处理，可接受）；`sim_call_id` 仅为最近 simulate（**不接受**，见 F1）；ReceiveInputs 提前拒绝分支在真实版本中是否留可辨 bracket；`isDismissed` 字段在发布版是否存在（取不到会退化为 `reject_reason:null`，诚实但信息减少）。
3. RUN_END 的 spec §2 要求（phase 覆盖、丢记录/缓冲溢出/写盘错误、完整性 hash）未在 trace 落字段：`Bug1ProbeRuntime.cs:514` 超上限静默丢行、`:541-557` RUN_END 只有 `end_reason`+`label`。此为 t28 既有范围，非 t35 引入，但会直接影响三相位"是否完整收到"的可判性，建议单独票补。
4. Bug1 根因/M1 判定：本票不涉及；M1/AUTH_STEP_LOCKED 仍需真实 1H（+1G 对照）trace 与视频。

## 8. 边界声明

- 本报告只判"仪表是否符合 spec、取证是否诚实"，**不构成 Bug1 已诊断、更不构成 Bug1 已修复**；也不对 t35 之外的仪表（帧/渲染相位）重新评级。
- 未评判命名、复杂度、注释密度等工程风格问题（属另一轴）。
- 审查为只读：除 §5 的一次性变异（已还原并复验字节一致）外，未修改任何生产/测试字节，未执行 git 操作。
- 复核 attempt：`420735b1-18a9-4768-8f05-b23c8186cb27`（attempt 1 `d472e869…` 已被平台改派作废）。

---
正文 SHA-256（本行之前全部字节）：`e978dc508aacf4ea1eef6441ed33936288568909808dae4282fef76fd9188209`；文件大小：15694B；复核 attempt：`420735b1-18a9-4768-8f05-b23c8186cb27`。
