# T73 交付报告：M1 判定器修正（M1-XZ-RELATIVE-v2）

- 任务：t73（repair），attempt 2，attempt_id `72572cb3-8ab5-43ef-b226-0b9c5187f35e`
- 裁决依据：`.scratch/takeover-v05-20261106/architecture-decision-t71-m1-relative-motion.md`
  （112 行，sha256 `6a91855b1badfc468a9406faa435fa7f5d3151b4817153f2630389f29d46dcee`）
- 结论口径（先写在最前，避免误读）：**本次只修判定器，不修角色顿挫。**
  零 M1 不等于 Bug1 已修复；AUTH_STEP_LOCKED / M1_STALL_CATCHUP / NO_THRESHOLD_EVENT
  三种结论的含义固定，不得互相代替。原始 `result.json` 与历史结论未被覆盖或删除。

---

## 1. 裁决条款 → 实现位置映射

| 裁决条款 | 要求 | 实现位置 |
| --- | --- | --- |
| §3.1 轴定义 | `H(p)=(p.x,p.z)`；只比较同一行 `a_pos`/`v_pos`；禁止跨相位拼见证 | `Bug1ProbeModel.cs` `Bug1Vec3.Horizontal()`；`Bug1ProbeJudge.ClassifyHorizontalMotion` 只读 `Bug1FrameSample.APos/VPos`；`ComputeSimRootSpan3D` 只读 `Sim.ABefore/AAfter` 且仅作旁证输出 |
| §3.2 C_H 停段 | ≥4 点、逐帧 `‖V_i−V_{i−1}‖≤eps`、释放帧 `r=b+1`、`B_r=cap×dt_r+eps`、`‖V_r−V_b‖>B_r` 且 `≥D`、右截断记 RIGHT_CENSORED | `ClassifyHorizontalMotion`（段枚举 + `IsHorizontalSmallStep` + `SpeedBoundary` 同式 `cap*dt+eps` + `HorizontalAtLeastGrids` + `MarkUnknown("right_censored@…","RIGHT_CENSORED")`） |
| §3.3 M1 两组必要条件 | 落后见证 `T_i=A_i−A_a`：`‖T_i‖≥D`、`‖A_i−V_i‖>eps`、`dot(A_i−V_i,T_i)/‖T_i‖>eps`；回补 `L_b−L_r>eps` 且 `dot(V_r−V_b,A_b−V_b)>0` | `FindLagWitness`；`ClassifyHorizontalMotion` 内 `converged` 表达式（两条件与） |
| §3.4 AUTH_STEP_LOCKED | 段内 `max‖A_i−A_j‖≤eps` 且 `‖(A_r−A_b)−(V_r−V_b)‖≤eps`；其余记 NON_M1_HORIZONTAL_PATTERN 并继续搜索 | `IsAuthStepLocked`；`NonM1Segments++` + `CandidateRejections` |
| §3.4 搜索完整性 | 遍历**全部**最大连续水平停段，不只取最长段 | `ClassifyHorizontalMotion` 的 `step = e + 1` 前进（而非跳出循环） |
| §4 事件尺度 | `D=0.020U` 按 ≥20 个 1mm 网格实现；`B_r` 严格 `>` | `Bug1Vec3.HorizontalAtLeastGrids(...,20)`（整数格平方和 ≥400）；释放门 `!(releaseHorizontal > boundary)` 即拒绝 |
| §5 接缝 | 先水平投影 + C_H，再分型；数据完整性/相位验证保持优先 | `Judge` 仍先跑 t65 尾部门 → `ValidatePhaseCoverage/Context/Frames`，最后才 `ClassifyHorizontalMotion` |
| §6 取代关系 | 取代 T4 §5.1–5.3 三维 RootSpan 分类；不删原始 XYZ；旧结果按 revision 重判 | `Bug1Verdict.RootSpan` 已删除，改为 `RenderRootSpanXZ`/`SimRootSpan3D`；`m1_rule_revision` 随 RUN_END/result.json/manifest 导出 |

换算说明（§4）：`q = 0.001U`（`PositionQuantumU`），`20 × q = 0.020U`；
`0.020 / eps = 0.020 / 0.0017320508075688772 ≈ 11.55`，即 2cm 门约为 11.55 个 `eps`，
远小于 `4.5U/s × 0.08s = 0.36U` 与 75FPS 走档单帧 `0.06U`，因此不会误吞正常移动，
也不会被单帧抖动触发。实现用整数格（`Math.Round(dx/q)`）平方和比较，避免 `19.999` 格被
浮点误差误判为 20 格。

## 2. 关键行为变化（相对旧判定器）

1. **A/V 始终重合的窗口数学上不可能判 M1**：全部 `A_i=V_i` ⇒ 每帧 `L_i=0` ⇒
   `FindLagWitness` 必然返回 -1（要求 `‖A_i−V_i‖>eps`）。本次 trace 的 5737/5737 个
   RENDER_READY 帧 `a_pos` 与 `v_pos` 逐坐标完全相同，因此旧结论 `M1StallCatchup` 是误报。
2. **SIM 三维 span 只作旁证**：`SimRootSpan3D` 仍计算并导出，但不再参与任何布尔判断
   （M3 变异把它接回布尔门后 R03/R09 立刻变红，见 §5）。
3. **候选分型而不是「无候选」**：每个满足 C_H 的段都会落到 M1 / AUTH / NON_M1 之一，
   拒绝原因逐条写入 `candidate_rejections`（`RELEASE_NOT_EXCEEDED@帧`、
   `RELEASE_BELOW_2CM@帧`、`LAG_WITNESS_MISSING@帧`、`ERROR_NOT_CONVERGED@帧`）。
4. **事件计数真实**：`strict_m1_events` 统计所有合格段，代表段只用于展示
   （`CandidateA/B/ReleaseFrame` 取最长合格 M1 段；无 M1 时取最长 AUTH 段）。
5. **右截断不记零事件**：段一直延伸到窗口末帧时判 `UNKNOWN(RIGHT_CENSORED)`，
   不会把「看不到释放」写成 `NO_THRESHOLD_EVENT`。

## 3. 真实 trace 回放（R03）

- trace：`启动器/UnturnedModManager/publish/UMM-v2.2.1-win-x64/bug1-20261107T111121Z/HOST/trace.jsonl`
  （18417 行，sha256 `f0627abd6e1da009bf30fe49ff91fa0128fa577614e89491afb5db37eb0580f8`）
- 回放方式：`ReplayCandidateSegment` 解析 RR/BU 帧与 SIM BEFORE/AFTER，**字段全部来自 trace**，
  无任何硬编码相位（这是对 t63-F2「回放伪造 HasRenderReady」的正面修复）。
- 回放结果（Bug1-72 断言）：

| 项目 | 旧判定器（历史 result.json） | 新判定器（M1-XZ-RELATIVE-v2） |
| --- | --- | --- |
| label | `M1StallCatchup` | `AuthStepLocked` |
| reason | `M1_STALL_CATCHUP` | `AUTH_STEP_LOCKED` |
| strict_m1_events | 1 | 0 |
| auth_segments | 不存在该字段 | 1 |
| 候选区间 | 11562–12215（release 12216） | 11562–12215（release 12216） |
| render_root_span_xz_u | 旧 `root_span`（三维） | ≤ eps（停段内水平根确实没动） |
| 原 result.json | — | 未被覆盖，仍保留 `M1StallCatchup` 供审计 |

## 4. 逐文件 sha256（交付态）

| 文件 | 行数 | sha256 |
| --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1ProbeJudge.cs` | 692 | `615080f4f132ea964acd30592203d4db5d98ca3717e6b400ba062e7b744723f0` |
| `Platform/Diagnostics/Patches/Bug1ProbeModel.cs` | 135 | `daf08c53309795d28139d6bd4d1933bc5d6f95c0b5042a0cae6f9f4628f1437f` |
| `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs` | 1355 | `a66aee9a934190131278bae84557c78a03a2436445114785634b9d1e79876c69` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeM1RuleTests.cs` | 584 | `0b4b019179bbfc0ec8177eb75288ed8602e98dc031117d3a7107a78439870749` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeJudgmentTests.cs` | 319 | `6ff42723251858d0f9c7164e6828e35667909a2893277cacdd369fd49c805bed` |
| `WhitelistTests/Program.cs` | 1085 | `71d5268ad401ae842ee97c71ba368f590a1554ba86b0f7dae8087c9949095c8e` |
| `SteamP2PFriends.csproj`（未改） | — | `ed629ae7064015d55d2e1bee15b01ba1eb6a7a565dcdb80ac36892499350b948` |
| `bin/Release/SteamP2PFriends.dll` | — | `81da407a7bec87e88e0a642a5b1885e37541c28d7418259fdb897f2212341f27` |

依赖文件旧/新哈希对照：

| 文件 | 旧（t65 交付态） | 新（本票） |
| --- | --- | --- |
| `Bug1ProbeJudge.cs` | `f41613c0194dc36716d2b17458956e0bc025d44e0323db40ccc834083de8f314` | `615080f4…` |
| `Bug1ProbeRuntime.cs` | `114742df5e12f647964842f3df8d8078ac363ee51e08d3b94b393eeba0048a5c` | `a66aee9a…` |
| `Bug1ProbeModel.cs` | `eb40f806…`（t48 交付后 t60/t65 未改动） | `daf08c53…` |
| `WhitelistTests/Program.cs` | `f61df0d2…`（t65 交付态） | `71d5268a…` |

## 5. 变异证据（逐条自跑，逐字节还原）

副本 `../_t73mut`（`tar` 复制仓库，排除 `bin/obj/.git/.scratch/Release/publishi`，
带上 `Build/Version.props`）。每轮：唯一文本替换 → 先 build 生产 → 再 build 测试 → 跑 exe →
记录变异后 DLL sha256 与 FAIL 名单 → 从主树 `cp` 回被改文件并打印还原后源码 sha256。
副本最终 `diff` 与主树零差异，已 `rm -rf`。

| # | 契约要求 | 实际改法 | 变异后 DLL sha256 | 实际红例 | 总数 |
| --- | --- | --- | --- | --- | --- |
| M1 | ①只删落后见证 → R05-a 红 | `if (witness >= 0 && converged)` → `if (converged)` | `a564f2d2268b22564eed9ffc972848915bc634f339f350fcd1dd0a188459e1ff` | Bug1-74（R05-a） | 765/768 |
| M2 | ②只删回补收敛/方向门 → R05-b 红 | `bool converged = (lagBefore - lagAfter) > eps && HorizontalDot(carrierDelta, gapBefore) > 0d;` → `bool converged = true;` | `49c15589ce436e39eb82d8691e02f6c442c04ef3edf5ff60eda39b0cb5d4b22d` | Bug1-75（R05-b）+ Bug1-11 | 764/768 |
| M3 | ③恢复「SIM 三维 span>eps 即 M1」→ R03/R09 红 | 判定条件追加 `\|\| ComputeSimRootSpan3D(w, frames[s - 1].Frame, frames[e].Frame) > eps` | `ba9aa5d3381ec6385e5415e7282382f697d2074efd81cca4f42508697efc2c53` | Bug1-72（R03）+ Bug1-79（R09） | 764/768 |
| M4a | ④恢复三维候选 → R04 的 0.08U 垂直例红 | `releaseHorizontal = HorizontalDistance(vRelease, vBefore)` → `Distance(release.VPos.Quantized(), frames[e].VPos.Quantized())` | `0d0db9037689f04f70a5f5be8a7c89f8abe37dedd6a35e3d9b41646c53e26988` | Bug1-73（R04） | 765/768 |
| M4b | ④同上（完整三维候选：距离 + 2cm 门都三维） | M4a + `if (!HorizontalAtLeastGrids(vRelease, vBefore, 20))` → `if (releaseHorizontal < Bug1ProbeConstants.HorizontalMotionMinU)` | `dcc0c8a7e44b772ba6d61e0c4eb05b2def76ce3b1795137e3192ea2682e3cc51` | Bug1-73（R04） | 765/768 |
| M5 | ⑤放宽 B 或删完整性门 → 原边界/缺样用例红 | `if (!ValidateFrames(window, verdict))` → `if (false && !ValidateFrames(window, verdict))` | `7ae503e3d718201ef36f427be03f86b31eaca1b7e91a96e5c12cf0564443497b` | Bug1-04/05/07/08/56/57/61/66/80/81 | 756/768 |
| M6 | ⑥只检查最长原始段 → R08 红 | `step = e + 1;` → `step = frames.Count;` | `5183ef11c2fdda6824cc0c83394a20aebc9583a164a3a9065252ab7f1bd2736c` | Bug1-78（R08） | 765/768 |

每条变异都至少红 1 条用例（M4a/M4b 是对同一条 R04 的两种写法，都实测变红；
不存在「被其它条件遮住」的变异被记为通过）。每轮还原后 `Bug1ProbeJudge.cs`
回到 `615080f4f132ea964acd30592203d4db5d98ca3717e6b400ba062e7b744723f0`。

## 6. 三命令输出

1. `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild`
   → `0 个警告` / `0 个错误`（1.22s，已成功生成）
2. `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild`
   → 已成功生成，0 错误（4 条既有警告，属测试工程既有条目）
3. `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe`
   → `=== Final Result: 766/768 PASS (Failed: 2) ===`
   - 失败仅 `[PureMemory] GBA01 Native First Observe Binding` 与
     `[PureMemory] NIL01 Real Native Lookup Contract`（既有环境失败，未配置原生运行时路由）
   - Bug1 段 81 条全 PASS（既有 69 条 + 本票新增 12 条），exit code 1 属既有约定（有失败即 1）

## 7. 行级改动清单

### 7.1 `Bug1ProbeModel.cs`（96 → 135 行）

- `Bug1Vec3` 新增 `Horizontal()`（返回 `(X, 0, Z)`，轴定义唯一来源）、`Subtract(a,b)`、
  `HorizontalDistance(a,b)`（只算 `dx/dz`）、`HorizontalDot(a,b)`（`a.X*b.X + a.Z*b.Z`）、
  `HorizontalAtLeastGrids(a,b,grids)`（`gx=(long)Math.Round((a.X-b.X)/q)`、`gz` 同理，
  `gx*gx + gz*gz >= grids*grids`）。
- `Bug1ProbeConstants` 新增 `M1RuleRevision="M1-XZ-RELATIVE-v2"`、`MotionAxes="XZ"`、
  `HorizontalMotionMinU=0.020`、`HorizontalMotionMinGrids=20`。

### 7.2 `Bug1ProbeJudge.cs`（557 → 692 行）

- `Bug1Verdict`：删除 `RootSpan`；新增 `RenderRootSpanXZ`、`SimRootSpan3D`、
  `M1RuleRevision`、`MotionAxes`、`HorizontalMotionMinU`、`LagWitnessFrame`（-1 表示无）、
  `LagWitnessAuthority`、`LagWitnessCarrier`、`LagBeforeReleaseU`、`LagAfterReleaseU`、
  `HorizontalStallSegments`、`HorizontalCandidates`、`AuthSegments`、`NonM1Segments`、
  `CandidateRejections`。
- `Judge`：分类段改为 `ClassifyHorizontalMotion(window, verdict)`；顺序保持
  「t65 尾部门 → 相位覆盖 → 上下文 → 帧完整性 → 水平分类」；`Valid=false` 先返回 Unknown，
  `CandidateA<0` 返回 NoThresholdEvent，再做 `ValidateCandidateAssociations`，
  最后按 `StrictM1Events>0` 落 `M1StallCatchup` 否则 `AuthStepLocked`。
- 删除 `FindLargestStallCandidate`、`ComputeRootSpan`（三维 RootSpan 分类整体退场）。
- 新增 `ClassifyHorizontalMotion`（段枚举 + 释放门 + 分型 + 代表段选择）、
  `IsHorizontalSmallStep`、`FindLagWitness`、`IsAuthStepLocked`、
  `ComputeRenderRootSpanXZ`（含 `ABeforeUpdate`）、`ComputeSimRootSpan3D`（旁证，仅 SIM 相位）。

### 7.3 `Bug1ProbeRuntime.cs`（1317 → 1355 行）

- `EndStep` 的 RUN_END 行新增 16 个字段：`m1_rule_revision`、`motion_axes`、
  `horizontal_motion_min_u`、`render_root_span_xz_u`、`sim_root_span_3d_u`、
  `lag_witness_frame`、`lag_witness_authority`、`lag_witness_carrier`、
  `lag_before_release_u`、`lag_after_release_u`、`strict_m1_events`、
  `horizontal_stall_segments`、`horizontal_candidates`、`auth_segments`、
  `non_m1_segments`、`candidate_rejections`。
- `WriteArtifacts` 的 `result.json`：删除旧 `"root_span"`，写入上述新字段
  （`lag_witness_authority_pos` / `lag_witness_carrier_pos` 用既有 `VecJsonInline`）。
- `manifest.json` 新增 `m1_rule_revision`、`motion_axes`、`horizontal_motion_min_u`。

### 7.4 测试

- 新增 `WhitelistTests/Evidence/Runtime/Bug1ProbeM1RuleTests.cs`（584 行，R01–R11 共 12 个用例，
  全部调用真实 `Bug1ProbeJudge.Judge`，未另写判定器）。
- 改 `Bug1ProbeJudgmentTests.cs`（319 行）：Bug1-01 断言改用 `RenderRootSpanXZ` 并补见证帧/收敛/
  修订号；`Test_AuthStepLocked_SameStallDifferentAuthority` 夹具改为 A=V 同步步进；
  `Test_AuthorityMovesAndReturns_StillM1` → `Test_AuthorityMovesAndReturns_IsNotM1`
  （旧断言把「权威先走、再同拍回来」当成 M1，按 v2 该几何不满足回补方向，应落 NoThresholdEvent）。
- `WhitelistTests/Program.cs`（1085 行）：Bug1-11 标题改为
  `Bug1-11 Authority Moves And Returns Is Not M1 (T71/v2)`；Bug1-70…Bug1-81 注册 12 条新用例。

### 7.5 未改动

- `SteamP2PFriends.csproj`（无新增生产文件）、`Bug1ProbeControl.cs`、
  `Bug1ProbePatch.cs`、`Bug1ProbeWindowBoundary.cs`、`Bug1ProbeNativeSpeed.cs`、
  `Bug1ProbeTargetSelector.cs`、`Bug1ProbeEntryLogic.cs`、`Bug1ProbeRxPhases.cs`。
- `Core/`、`Build/`、`Release/`、`publishi/`、`Develop-Stage/`、`docs/` 未写
  （`find Release publishi -newermt "2026-11-07 19:00" -type f` 无输出）。
- Bug3 五文件哈希未变：`a89f40b949f841d0`（P2PGroupStateRecoveryPatch.cs）、
  `7a82de8e38fa0f6a`（GroupStateRecoveryCore.cs）、`3d5044f7870fbb17`（GroupLeaseLedger.cs）、
  `a45533850f31bd96`（GroupLoadPlanner.cs）、`5b0c4e9ba4c87cbe`（P2PGroupRegistration.cs）。

## 8. 复审与边界声明

1. **本票改变了 Judge 生产字节**，因此不适用 `TEST_STRENGTHEN_ONLY` 单轴例外，
   必须走双轴复审（标准轴 + 规格轴）。
2. **只修判定器，不修角色顿挫**：本次没有改动任何补丁注入点、输入队列处理、
   位置同步或插值逻辑；`strict_m1_events=0` 只说明「这一段几何不构成 M1 证据」，
   不说明 Bug1 已修复，也不说明角色顿挫不存在。
3. **三种结论含义固定**：
   - `M1StallCatchup`：同一 RENDER_READY 相位内可见载体确实落后于权威，且释放时向权威回补；
   - `AuthStepLocked`：停段内权威根与载体同拍未动、释放时同拍步进（根被锁住），不是 M1；
   - `NoThresholdEvent`：窗口内有完整 C_H 段，但没有任何段同时满足落后见证与回补收敛。
4. **历史结论保留**：`bug1-20261107T111121Z/HOST/result.json` 未被覆盖，仍记录
   `M1StallCatchup`；新口径以 `m1_rule_revision = M1-XZ-RELATIVE-v2` 标注，重判结果见 §3。
5. **自动化可证的边界**：R01–R11 与 R03 回放可证明判定规则本身按裁决实现；
   但「真机 trace 上 M1 是否存在」仍取决于后续 1H+1G 采集，本票不产出根因结论。

