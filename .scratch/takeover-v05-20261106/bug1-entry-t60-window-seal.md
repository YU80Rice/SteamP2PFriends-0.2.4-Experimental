# t60 — Bug1 仪表缺陷修复：窗口收口 / 缺相位归因 / 输入关联可解释 / 插值观测

- 任务：t60（implementation），attempt 2，attempt_id `e48bef2a-82b6-483f-b031-bece87b64484`
- 基线：0.2.4.10 → 0.2.4.11 实验分支；上一轮 t58 交付态（exe 737/739，Bug1 段 54/54）
- 本票只产**测量能力**：不产出 Bug1 根因结论，不改任何判定语义与阈值。

## 1. 结论摘要

| 项 | 修复前（真实 trace `bug1-20261107T044218Z`） | 修复后（同一 trace 离线回放） |
| --- | --- | --- |
| 窗口范围 | 记录帧 7078..13415，但 `RUN_END.frame_count = 14459`（含 1044 帧从未采到任何记录的空白帧） | 收口到最后一个齐全帧，`tail_excluded = 1043`、`head_excluded = 1` |
| 判定 | `Unknown / MISSING_PHASE`，missing_fields 4173 条（其中 1043 帧×4 项来自 tail） | `NoThresholdEvent / NO_CANDIDATE_RUN`，tail 段不再产生任何 `phase_BEFORE_UPDATE@13xxx` |
| 缺相位归因 | 全部混为一个 `MISSING_PHASE`，无法区分「真没采到」与「越界算进来的」 | `phase_missing_genuine = 0`、`phase_missing_out_of_window = 0`，越界帧在进入判定前已被裁掉并单独计数 |
| 输入关联 | 113/6338 条 RENDER_READY 因 `input_association` 失配被判 invalid，只有笼统 `last_consume_record_incomplete` | 每条 invalid 都带规则名 + 具体原因码 + 候选 `rx_id`/`inputseq`；暖机段（首个 consume 之前）被明确解释为 `no_consume_yet` |
| 插值观测 | 无 | 每帧 RENDER_READY 记录 `remote_input_tick` / `sample_time_s` / `input_rate` / `lerp_factor` / `lerp_factor_clamped` / `last_update_pos` / `authority_pos` / `model_pos`，缺字段进 `interpolation_unavailable`，不填默认值 |

## 2. 根因（真实 trace 复盘，已定位）

trace：`<repo 父目录>/启动器/UnturnedModManager/publish/UMM-v2.2.1-win-x64/spf-bug1/bug1-20261107T044218Z/HOST/trace.jsonl`
（19474 行 / 11.4 MB；result.json 100 KB）

记录分布：

- `FRAME/RENDER_READY` 6338 条，frame 7078..13415
- `FRAME/BEFORE_UPDATE` 6337 条，frame 7079..13415
- `RX/AFTER_ENQUEUE` 1699（7190..13415）、`CONSUME/AFTER_DEQUEUE` 1699（7191..13416）
- `SIM/BEFORE` 1699、`SIM/AFTER` 1699（7191..13416）
- `RUN_BEGIN` 1（7078）、`CONTEXT` 1 与 `RUN_END` 1（均 14459）
- epoch：19472 条为 1，仅 CONTEXT/RUN_END 为 2（session_reset 时 epoch 已自增）

故障链：

1. 目标玩家在 frame 13416 之后被销毁/离场，但 `Camera.onPreCull` 仍每帧回调。
2. `OnCameraPreCull` 每帧 `GetOrCreateFrame(Time.frameCount)` 建帧并置 `HasRenderReady = true`。
3. 同一回调里 `_third` / `_thirdRenderer` 已销毁，`IsDescendantOf(...)` 抛异常被 catch 吞掉 → 该帧**没有写 trace**（所以 trace 里看不到 13416..14458），但帧对象已留在 `_window.Frames`。
4. `OnBeforeUpdate` 因 `!Bound` 提前 return，这些帧永远缺 `BEFORE_UPDATE` 与 third_carrier / renderer_chain / model_endpoint。
5. 窗口直到 14459 session_reset 才 `EndStep`，`StepLastFrame` 被空白帧推到 14458；`Judge` 把 1043 帧全部判成缺相位 → `UNKNOWN(MISSING_PHASE)`，`n_expected = 7381`。

113 条 `input_association` 失配全部落在 frame 7078..7190（首个 `AFTER_DEQUEUE` 在 7191 之前），`inputseq/rx_id/queue_depth` 三字段全 null —— 是「还没开始消费」的暖机段，不是采集故障。

## 3. 修复内容

### 3.1 窗口在边界收口（acceptance ①②）

新文件 `Platform/Diagnostics/Patches/Bug1ProbeWindowBoundary.cs`：

- `Bug1WindowBounds`：`HeadExcluded / TailExcluded / InteriorPartial / FirstFrame / LastFrame / RemainingFrames / ExcludedReason`
- `Bug1ProbeWindowBoundary.Finalize(window)`：定位首个与末个 `HasBeforeUpdate && HasRenderReady` 的帧；裁掉头部之前与尾部之后的不完整帧，`InteriorPartial` 只统计夹在中间的半帧；同步 `StepFirstFrame/StepLastFrame`。
- **一个齐全帧都没有时不裁剪**：防止系统性 detour 全灭被「裁成空窗」掩盖，这种情况仍由判定层报 `MISSING_PHASE`。

`Bug1ProbeRuntime.EndStep`：在写 `CONTEXT` 与调 `Judge` 之前先 `Finalize(_window)`，并把 4 个统计写回窗口；`RUN_END` 与 `result.json` 追加同源 6 字段：`window_head_excluded / window_tail_excluded / window_interior_partial / phase_missing_genuine / phase_missing_out_of_window / window_excluded_reason`。

`Bug1ProbeRuntime.Enable/Disable/RollbackEnable` 新增边界事件：`Player.onPlayerDestroyed`（目标本体销毁 → `Disable("target_left")`）与 `Level.onLevelLoaded`（关卡切换 → `Disable("level_changed")`），均只在非离线模式订阅、各自 try/catch、失败记 `BOUNDARY_HOOK_FAILED:*`。SDK 无 `onLevelUnloaded`，故用下一次 `onLevelLoaded` 收口上一窗。

### 3.2 缺相位原因可区分（acceptance ②）

`Bug1ProbeJudge`：`Bug1Verdict` 增 `PhaseMissingGenuine / PhaseMissingOutOfWindow / WindowHeadExcluded / WindowTailExcluded / WindowInteriorPartial / WindowExcludedReason`；`ValidateFrames` 的每处缺相位改为 `PhaseMissingGenuine++` 后再 `MarkUnknown`。

`PhaseMissingOutOfWindow` 恒为 0 —— 越界帧在进入判定前已被裁掉，只可能出现在窗口统计里。因此 `MISSING_PHASE` 不再由窗口越界触发，只能来自「窗口内真的缺」。内部半帧（夹在中间的半帧）仍计入 `PhaseMissingGenuine`，不会被静默放过。

### 3.3 输入关联失配可解释（acceptance ③）

`Bug1InputAssociation`（同文件）：

- 规则名 `last_consume_before_render`：RENDER_READY 关联「最近一次出队」的 `rx_id/inputseq/queue_depth`。
- `Classify` 返回 `null`（关联成立）或原因码：`no_consume_yet` / `last_consume_missing_inputseq` / `last_consume_missing_rx_id` / `last_consume_missing_queue_depth`。
- `JsonFragment` 把 `input_association_rule` / `input_association_reason` / `candidate_rx_ids` / `candidate_inputseqs` 写进 trace。

`OnCameraPreCull` 的 `invalid_reasons` 由笼统串改为 `input_association:<原因码>`，不再静默判 invalid。

### 3.4 只读插值观测字段（acceptance ④）

`Bug1InterpolationSample.Compute` + `SampleInterpolation()`：

- 远端 `PlayerInput.tick`（反射属性）、`PlayerInput.RATE`（反射静态字段 `0.08f`）、`Time.realtimeSinceStartup`（`[MethodImpl(NoInlining)]` + try/catch）、`PlayerMovement.lastUpdatePos`（反射私有字段）、权威 `movement.transform.position`、模型 `player.first.position`。
- `lerp_factor = (sample_time - tick) / rate`，另给 `lerp_factor_clamped`（钳到 [0,1]，对应 SDK `PlayerMovement.cs:1746` 的 `Vector3.Lerp(lastUpdatePos, transform.position, (realtimeSinceStartup - input.tick) / PlayerInput.RATE)`）。
- 任何字段读不到就进 `interpolation_unavailable` 数组并置对应 `Has* = false`，**不填默认值**。

### 3.5 判定语义与阈值

零改动：`ValidateContext` / `ValidateFrames` 的阈值、`FindLargestStallCandidate`、`EpsilonU`、`MinStallDisplacements`、标签定义、`NO_THRESHOLD_EVENT` 判据全部保持原样。本票只增加「窗口裁剪 + 归因计数 + 观测字段」。

## 4. 真实 trace 离线回放（acceptance ⑧）

用例 `Bug1-61 Real Trace Replay Seals Tail` 直接解析真实 trace：按 `kind=FRAME` 行重建 6338 帧的相位图，再从 `RUN_END.frame_count = 14459` 得知「窗口里还有 13416..14458 这 1043 帧从未 Record」，据此复现修复前的窗口。

实测打印（主树，2026-11-07 13:2x）：

```
[T60] replay trace=...\spf-bug1\bug1-20261107T044218Z\HOST\trace.jsonl recorded=6338 run_end_frame=14459
      raw=Unknown/MISSING_PHASE raw_tail_missing=200
      sealed=NoThresholdEvent/NO_CANDIDATE_RUN head_excluded=1 tail_excluded=1043 genuine=0
```

- `raw`（不裁剪直接 Judge）：`Unknown / MISSING_PHASE`，tail 段缺相位 200 条（按 `phase_BEFORE_UPDATE@frame > 13415` 计数）
- `sealed`（先 `Finalize` 再 Judge）：`NoThresholdEvent / NO_CANDIDATE_RUN`，tail 段缺相位 0 条，`phase_missing_genuine = 0`

即：修复前那一整轮 UNKNOWN 完全是窗口越界造成的假象；收口后同一份数据给出真实标签 `NO_THRESHOLD_EVENT`，不再有 `phase_BEFORE_UPDATE@13xxx`。

trace 路径解析：优先环境变量 `SPF_BUG1_TRACE_JSONL`；否则从测试 exe 目录逐级上溯找 `启动器/.../spf-bug1/bug1-*/HOST/trace.jsonl` 最新一份；找不到则用例跳过（不伪装成通过）。

## 5. 新增用例（Bug1-55..Bug1-61，Bug1 段 54 → 61）

| 编号 | 用例 | 钉住什么 |
| --- | --- | --- |
| Bug1-55 | Window Boundary Trims Head And Tail | head 2 / tail 3 被裁，剩 3 帧，`StepFirst/LastFrame` 同步 |
| Bug1-56 | No Complete Frame Means No Trim | 无齐全帧不裁剪，仍 `UNKNOWN/MISSING_PHASE`，`PhaseMissingGenuine == 4`（精确） |
| Bug1-57 | Interior Partial Stays Genuine | 内部半帧不裁、`InteriorPartial == 2`、`PhaseMissingGenuine == 2`、`OutOfWindow == 0` |
| Bug1-58 | Interpolation Fields And Unavailable | lerp 计算正确；缺 tick/lastUpdatePos/rate 时置 unavailable 且不硬算 |
| Bug1-59 | Input Association Reasons | 四个原因码分支 + `JsonFragment` 规则名/候选字段 |
| Bug1-60 | EndStep Seals Window In Production Path | 生产路径 `Enable → 帧 → EndStep`：RUN_END 与 result.json 同源 6 字段，且 EndStep 真调 `Finalize`（IL 守卫） |
| Bug1-61 | Real Trace Replay Seals Tail | 真实 trace 前后判定对比（见 §4） |

## 6. 变异证据（仓库外副本 `../_t60mut`，已 `rm -rf` 删除）

副本由 `tar` 复制主树（排除 bin/obj/.git/.scratch/Release/publishi，保留 `Build/Version.props`），驱动脚本 `mut.py`：唯一替换 → 先重建生产再重建测试 → 跑 exe → 打印变异后 DLL sha256 → 从主树 `cp` 还原并打印源码 sha256。

| 变异 | 改法 | 变异后 DLL sha256 | 红名单 | 总数 |
| --- | --- | --- | --- | --- |
| M-A | `EndStep` 的 `Finalize(_window)` → `new Bug1WindowBounds()`（等于不裁剪） | `9faa7690236dc9d3c3bfe0c8507ac593e37e273841c334e5d1c8313c2e704951` | Bug1-60 | 743/746 |
| M-B | `Judge` 删掉 BU 分支的 `PhaseMissingGenuine++` | `926584bfeb621b60701d1e25b2e105b4466c1083f3c2e7f7ac12fb56eddac71c` | Bug1-56、Bug1-57 | 742/746 |
| M-C | `Classify` 的 `no_consume_yet` 分支改为 `return null`（回到静默） | `97a04b136d3819dae6e0eea973f612b2357eb8f918e7d283da25f8b705e2091f` | Bug1-59 | 743/746 |
| M-D | 插值 `if (HasTick && HasSampleTime && HasRate)` → `if (true)`（缺字段硬算 lerp） | `9540ac41c6f9053ae94ccba564d71422c597582e35b38db04c108b678d945657` | Bug1-58 | 743/746 |

每条变异只红 1–2 条新用例 + 2 条既有环境失败。还原后源码哈希回到：

- `Bug1ProbeRuntime.cs` `e6d3bc265c7422a4152f62054fad58264f94957d5de6bd6c0ebacd70e177b2f4`
- `Bug1ProbeJudge.cs` `7228213064b9272383c403e28dd99d507c8d075154825bc7d68609126081028a`
- `Bug1ProbeWindowBoundary.cs` `4217568ad47feca65a1aa582aa09d6d733cba110cf95da46b0872b160f87102f`

记录一条教训：M-B 第一版（只删 BU 计数）**没有变红**，因为 Bug1-56/57 当时只断言 `PhaseMissingGenuine > 0`，RR 分支的计数仍在。改为精确断言（4 / 2）并把 Bug1-57 的内部帧改成「缺 BU」「缺 RR」各一之后才红；测试收紧后主树仍 744/746。

## 7. 三命令（主树收工实测）

| 命令 | 结果 |
| --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 警告 0 错误 |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 4 条既有警告 0 错误 |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `=== Final Result: 744/746 PASS (Failed: 2) ===` |

两条失败均为既有环境失败（与本次改动无关）：
`FAIL [PureMemory] GBA01 Native First Observe Binding`、`FAIL [PureMemory] NIL01 Real Native Lookup Contract`（`TypeInitializationException: SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`）。
exe 退出码 1 属既有约定（有失败即 1）。Bug1 段 61/61 PASS。

## 8. 逐文件 sha256（主树终态）

| 文件 | 字节 | 行 | sha256 |
| --- | --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1ProbeWindowBoundary.cs`（新） | 8932 | 195 | `4217568ad47feca65a1aa582aa09d6d733cba110cf95da46b0872b160f87102f` |
| `Platform/Diagnostics/Patches/Bug1ProbeJudge.cs` | 23582 | 514 | `7228213064b9272383c403e28dd99d507c8d075154825bc7d68609126081028a` |
| `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs` | 62778 | 1204 | `e6d3bc265c7422a4152f62054fad58264f94957d5de6bd6c0ebacd70e177b2f4` |
| `SteamP2PFriends.csproj` | 27575 | 409 | `ed629ae7064015d55d2e1bee15b01ba1eb6a7a565dcdb80ac36892499350b948` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeT60Tests.cs`（新） | 19923 | 346 | `8df9ff4d29d345cb3ee59b99671eb4407164731df814e74de5516f35c88f7229` |
| `WhitelistTests/Program.cs` | 142682 | 1061 | `b9933c0b3ae784f02bda1b6b383aced017b9e0d476aad793fc868f36a1fd7018` |
| `bin/Release/SteamP2PFriends.dll` | 1491456 | — | `d6da583532deaf45019af455c51a3b9b144dde962beff3a4726736fc0780d7dc` |

边界核验：

- Bug3 五文件哈希未变：`a89f40b949f841d0`（P2PGroupStateRecoveryPatch.cs）、`7a82de8e38fa0f6a`（GroupStateRecoveryCore.cs）、`3d5044f7870fbb17`（GroupLeaseLedger.cs）、`a45533850f31bd96`（GroupLoadPlanner.cs）、`5b0c4e9ba4c87cbe`（P2PGroupRegistration.cs）
- `find Release publishi -newermt "2026-11-07 12:50" -type f` 无输出（未写发布目录）
- `grep -rn "playerID == null\|playerID != null" Platform/Diagnostics/Patches/` → 0 命中
- 未动 `Core/Patches/P2PGroupStateRecovery/`、`Build/`、`Platform/Host/`、`Platform/Transport/`

## 9. 未改项与 UNKNOWN 边界

未改（本票不做）：

- 判定阈值、`FindLargestStallCandidate`、标签集合、`NO_THRESHOLD_EVENT` 判据
- `Core/Registration/...PatchRegistrationDiagnosticsModules.cs`（边界事件订阅放在 `Bug1ProbeRuntime` 内部，无需改注册处）
- T49-F2（auto 档遇 CROUCH/PRONE 套用 walk 上限）：由 captain 另问架构裁决
- T49-F3（探针外六处同类 `playerID == null`）：另开低优先级票

仍需 1H+1G 实机验证（自动化不能证明）：

1. Mono 运行时下 `Queue<PlayerInputPacket>` 的 Enqueue/Dequeue detour 是否真触发（离线进程只能装 BCL 两条 patch，5 条 Assembly-CSharp 原生 patch 报 `HarmonyException: IL Compile Error (unknown location)`）。
2. `Player.onPlayerDestroyed` / `Level.onLevelLoaded` 在真实关卡时序下是否在窗口内命中（离线用例只验证了接线与判定函数）。
3. `PlayerInput.tick` / `PlayerInput.RATE` / `PlayerMovement.lastUpdatePos` 在实机上是否都可反射读到；读不到时会进 `interpolation_unavailable`（本票只保证「不填默认值」）。
4. 输入关联在真实暖机段是否给出 `no_consume_yet`（离线只覆盖分类函数与 JSON 形态）。

声明边界：本票**不产出 Bug1 根因结论**。真实 trace 的 `NO_THRESHOLD_EVENT` 只说明「收口后这一轮没有出现超过阈值的停顿候选」，不等于「Bug1 不存在」，也不等于「仪表可采」——`n_expected`、插值字段、关联规则都还需要实机再采一轮才能确认测量面完整。
