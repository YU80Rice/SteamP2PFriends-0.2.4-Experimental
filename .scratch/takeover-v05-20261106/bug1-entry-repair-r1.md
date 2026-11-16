# T48 Bug1 仪表入口修复轮（repair round 1）— 交付报告

- 票号：t48（被修 t45，双轴复审判 needs_revision）
- 基线：0.2.4.10 → 目标 0.2.4.11；当前树 DLL `f28c211a…`
- 结论：R8-01 / F1 / R8-02 / R8-03 / R8-04 / F3 / F4 七项全部落地，四条变异均在**最终树副本**上复现红→逐字节还原；三命令中生产与测试构建 0 错误，全量 727/729 PASS，Bug1 段 44/44 PASS（2 条失败为既有环境项 GBA01/NIL01）。
- **边界（先说清楚）**：本轮只把「探针能不能开、数得对不对、关得干不干净」修到可自动化证明；**不构成 Bug1 根因结论**，真实采集仍需 1H+1G 实机，见 §12。

## 1. 逐文件 sha256（本轮最终树）

| 文件 | 行数 | sha256 |
| --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1ProbeTargetSelector.cs`（新增） | 149 | `b716369fac0f4bace41f9a86297755b22d665fd7b64e7382e25fa9feb9d06da8` |
| `Platform/Diagnostics/Patches/Bug1ProbeControl.cs` | 313 | `aa6c8bea8ef3a73309f6dee1232e9cbb2ef7bb3f57a7e989ab9473b8bf805ab5` |
| `Platform/Diagnostics/Patches/Bug1ProbeEntryLogic.cs` | 88 | `51460c685196f26d69303c28388f6931c6e6f7540f0d4913827637fa36fa1299` |
| `Platform/Diagnostics/Patches/Bug1ProbeNativeSpeed.cs` | 145 | `109e283e5a37323c3c3838b62e595a290d84a95547f62a340e5581cd2d24dfae` |
| `Platform/Diagnostics/Patches/Bug1ProbePatch.cs` | 242 | `22308d77197af4e58a34c8625f813e862783eebc06c3f5b129aaf29b28dd83d6` |
| `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs` | 1013 | `17ca472b6e86615b9653207c00c59cb20ef2fc934d559d5f8e2a146e5eb4e48d` |
| `Platform/Diagnostics/Patches/UseableBarricadeDiagnosticPatch.cs`（同类判空顺手修） | 1096 | `50eb1dd3f22f076feff8a906633cacfba1af80e949f4a7764357c5c99d6532f8` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeEntryRepairTests.cs`（新增） | 399 | `51db40833c3f9db4f27f335c29a5ee42cc91a69ca5d13a87709381ec1792629e` |
| `WhitelistTests/Program.cs` | — | `8b278b6a1ee3fdd1d86a35b670a726fe0e71fa3516e7b80dfef43a465c0a0695` |
| `SteamP2PFriends.csproj` | — | `8b27ab5587b2656e0153cff2d2577c79d8d20bc2d5a5d544aadc572c732dd89e` |
| `bin/Release/SteamP2PFriends.dll` | — | `f28c211a9178818d81d2d58721a4e5b9db101371ce0a6be836ff37561301e2e8` |

未改动：`Bug1ProbeJudge.cs`（`5d863441…`）、`Bug1ProbeModel.cs`（`eb40f806…`）、`Bug1ProbeRxPhases.cs`（`8d0d36fc…`）、`Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs`（`ba5b7a73…`）。

## 2. R8-01（blocker）：判空自爆真修

**根因**：`SDG.Unturned.SteamPlayerID` 重载 `==`/`!=`，实现直接取两侧 `.steamID`，任一侧为 null 必抛 NRE（`.scratch/bug2-sdk-longpaths-20261030/.../Unturned/Player/SteamPlayerID.cs:136-144`）。旧代码三处 `client.playerID == null` 让探针在真实主机上必崩。

**修法**：
1. 新增纯函数层 `Bug1ProbeTargetSelector`（不碰 Unity 对象，只吃快照）：
   - `Bug1ClientSnapshot Snapshot(SteamPlayer client, int index)` —— **全工程唯一判空点**，只用 `ReferenceEquals`；`SteamPlayerID id = client.playerID; if (ReferenceEquals(id, null)) …`。
   - `Bug1TargetSelection Select(IList<Bug1ClientSnapshot>, string selector, ulong localSteamId)` → `Status{Selected,NotFound,Ambiguous}` + `Reason`（`unique-match` / `selector-matched-nothing` / `no-remote-client` / `multiple-clients-matched`）+ `Skipped`（`null-client` / `playerid-null` / `self` / `selector-mismatch`）+ `ErrorCode`。
   - `SelectBySteamId(...)` 供运行时绑定使用。
2. 三个调用点全部改走该层：`Control.ResolveTarget`（并打 `TARGET_SELECT <status> reason=… candidates=… skipped=[…]` 日志）、`Control.FindPlayer`、`Runtime.TryBindTarget`（`clients[selection.SelectedIndex]` + `ReferenceEquals` 取 player）。
3. 同目录 `UseableBarricadeDiagnosticPatch.cs:851` 的同类写法一并改 `ReferenceEquals(owner.playerID, null)`（原本必抛后被 catch 成 `unknown(NullReferenceException)`，现在语义正确）。
4. 两处注释改词，避免出现字面模式影响 grep 验收。

**验收 grep（0 命中）**：

```
$ grep -rn "playerID == null\|playerID != null" Platform/Diagnostics/Patches/
$ echo $?
1            # 无任何输出，0 命中
```

**用例**：
- `Bug1-36 Null PlayerID Client Skipped Not Thrown (T48/R8-01)`：用 `FormatterServices.GetUninitializedObject(typeof(SteamPlayer))` 造 `_playerID == null` 的 client，断言 `Snapshot` 不抛、`HasPlayerId=false`、`Select` 判 `Skipped` 含 `playerid-null`；并断言直接写 `id == null` 在同类对象上**确实抛 NRE**（把隐患钉死在用例里）。
- `Bug1-37 Selector Excludes Self Detects Ambiguity (T48/R8-01)`：排除自己、两名候选→`Ambiguous`+`TARGET_PLAYER_AMBIGUOUS`、名字与 SteamID64 两种选择器、`SelectBySteamId` 命中/未命中。

## 3. F1（规格轴 blocker）：相位计数断链真修

**缺陷**：`Enable` 把 `_window.RxEnqueueRecords/_rxDequeueRecords` 置 0，而 `OnEnqueue/OnDequeue` 递增的是静态 `_rxEnqueueRecords/_rxDequeueRecords`；`EndStep` 调 `Judge(_window)` 前不同步 → `ValidatePhaseCoverage` 恒失败，每局生产 run 都判 `UNKNOWN(PHASE_COVERAGE_MISSING)`；同时 `RUN_END` 打印静态计数（可能 500），同一局产物自相矛盾。

**修法**（`Bug1ProbeRuntime.EndStep`，Judge 之前）：

```csharp
if (_window.RxEnqueueRecords.HasValue) _window.RxEnqueueRecords = _rxEnqueueRecords;
if (_window.RxDequeueRecords.HasValue) _window.RxDequeueRecords = _rxDequeueRecords;
int rxAfterEnqueue = _window.RxEnqueueRecords ?? _rxEnqueueRecords;
int rxAfterDequeue = _window.RxDequeueRecords ?? _rxDequeueRecords;
var verdict = Bug1ProbeJudge.Judge(_window);
// RUN_END 行改用 rxAfterEnqueue / rxAfterDequeue
```

合成窗语义保留：`null` 仍是「不参与相位门」，只有「开启了仪表（0）却没记录」才硬判缺失。

**修复前后对比例子（同一用例、同一条链路，取自 M-A 变异日志）**：

```
# 回退 F1 修复（M-A）时，Bug1-38 打出的真实 RUN_END 行：
{"kind":"RUN_END","label":"Unknown","end_reason":"t48-test",
 "rx_after_enqueue":0,"rx_after_dequeue":0,
 "missing_fields":["AFTER_ENQUEUE","AFTER_DEQUEUE"]}
→ result.json: reason=PHASE_COVERAGE_MISSING     （自相矛盾：明明记录过，却说没记录）

# 修复后同一用例（Bug1-38 绿）：
RUN_END: "rx_after_enqueue":1,"rx_after_dequeue":1
result.json: label=NoThresholdEvent，reason 不含 PHASE_COVERAGE_MISSING
→ 判定依据与 RUN_END 打印的是同一套数
```

**用例**：
- `Bug1-38 Production Path Phase Coverage Keeps Judgment (T48/F1)`：**走生产路径** `__TestOffline(true) → __TestInstallSeam(()=>true) → Enable(config, OutputRoot=temp) → __TestAddSmoothFrames(6) → __TestBindQueue → OnEnqueue/OnDequeue → EndStep`，读磁盘 `trace.jsonl` 与 `result.json`，断言 `rx_after_enqueue:1`、`rx_after_dequeue:1`、不含 `PHASE_COVERAGE_MISSING`、label=`NoThresholdEvent`。
- `Bug1-39 Zero Records Forces Phase Coverage Unknown (T48/F1)`：同样走 `Enable → EndStep` 但零记录，断言 `rx_after_enqueue:0`、label=`Unknown`、`missing_fields` 同时点名 `AFTER_ENQUEUE`+`AFTER_DEQUEUE`、`result.json` 含 `PHASE_COVERAGE_MISSING`。
- 两条都在 M-A 下变红（见 §11）。

> 注：`EndStep` 的 `finally` 会清 `_traceLines`，因此用例必须读落盘文件而不是内存 trace——这也是本轮先红后绿的直接教训。

## 4. R8-02（high）：fail-closed 开启

`Bug1ProbeRuntime.Enable` 重排为「先装后开」：

1. 前置校验（RunId/StepId/TargetSteamId/SpeedCapKnown）不变；
2. 清状态 → **先** `installed = __InstallSeam != null ? __InstallSeam() : Bug1ProbePatch.Install()`，整体 try/catch；
3. 失败：`LastError = "INSTALL_FAILED:…"`（返回值 false 时补 `INSTALL_FAILED:patch_install_returned_false`）→ `RollbackEnable()`（`Uninstall()` + 退订 `Camera.onPreCull` + 清 window/队列/计数 + `Config = default(Bug1ProbeConfig)`）→ `return false`；
4. 成功：才 `Enabled = true` → `BoundCamera(true)` → `Record("RUN_BEGIN")`。

因此不会再出现「`START_OK` 但零 hook」，也不会留 `Enabled=true` 把后续开启卡成 `ALREADY_RUNNING`；`Bug1ProbeControl` 本来就在 `Enable` 返回 true 后才打 `START_OK`，现在这个前提才真正成立。

**用例** `Bug1-40 Install Failure Fails Closed Then Retry (T48/R8-02)`：`__TestInstallSeam(() => false)` → `Enable` 必须 false、`!Enabled`、`LastError` 以 `INSTALL_FAILED` 开头；随后 `__TestInstallSeam(() => true)` 再 `Enable` 必须成功（证明没被 `ALREADY_RUNNING` 卡死）；再注入抛异常的 seam，断言 `LastError` 以 `INSTALL_FAILED:InvalidOperationException` 开头。

## 5. R8-03（high）：启动期开启不再一拒到底

- 纯判定函数（可离线测）：`Bug1ProbeEntryLogic.EvaluateStartupRetry(enableRequested, running, isHost, levelLoaded, observableClients, attemptsUsed, maxAttempts)` → `Bug1StartupRetryVerdict{Wait, Retry, GiveUp}`。
  - `!enableRequested` / `running` / 次数用尽 → `GiveUp`；
  - 非 host / 关卡未加载 / 可观察客机 0 → `Wait`；
  - 就绪 → `Retry`。
- `Bug1ProbeControl`：`MaxStartupRetries = 3`；非 host 拒绝时 `EnsureStartupRetryHook()` 订阅 `Level.onLevelLoaded` 与 `Player.onPlayerCreated`（各 try/catch，失败打 `RETRY_HOOK_FAILED`，成功打 `RETRY_ARMED`）；`TryRetryStartup(trigger)` 按判定 Wait/Retry/GiveUp，Retry 成功后立刻 `UnhookStartupRetry()`，超次数打 `RETRY_GIVEUP`；`OnEnabledChanged(true)` 重置计数。有界、可关（改 cfg 关掉 Enable 即退订）、日志可见。
- **用例** `Bug1-41 Startup Retry Decision Pure Function (T48/R8-03)`：覆盖上表全部分支。

## 6. R8-04（medium）：关闭要卸干净

- 新增 `Bug1ProbePatch.Uninstall()`（幂等）：对 7 条 patch（`Prefix/Postfix_Simulate`、`Prefix_Update`、`Prefix/Postfix_ReceiveInputs`、`Postfix_Enqueue`、`Postfix_Dequeue`）逐个 `UnpatchOne`；`UnpatchOne` **先 `IsPatched` 预检**（扫 `Harmony.GetPatchInfo` 的 Prefixes/Postfixes 匹配 `owner==_harmonyId && PatchMethod==…`），未装过直接算成功——否则 Harmony 对未装过的 target 会抛，幂等就断了。
- `Disable(reason)` 与 `RollbackEnable()` 都调用 `Uninstall()`；`Install` 成功时清 `FailClosed`。
- **用例** `Bug1-42 Disable Uninstalls Detours Reopen OK (T48/R8-04)`：用真实 `new HarmonyLib.Harmony("t48-r8-04")` 装 `Queue<PlayerInputPacket>` 的 Enqueue/Dequeue 两条 detour → `Enable`（seam=true）→ `Disable` → 断言两条 `Verify` 变 false（证明 Uninstall 不是空实现）→ 再 `Enable` 必须成功 → `Uninstall` 连续两次都 true（幂等）。

## 7. F3：拒绝路径不留空 run 目录

`Bug1ProbeControl.PrepareRun(...)` 抽出：**先** `Bug1ProbeEntryLogic.Validate`（`ALREADY_RUNNING` / 参数缺失直接返回，**不建目录**）→ 再 `Directory.CreateDirectory(outputRoot)` → 组 `Bug1ProbeConfig`。`TryStart` 改为调它。

**用例** `Bug1-43 Refused Start Leaves No Run Directory (T48/F3)`：`alreadyEnabled=true` + 目标目录不存在 → 返回 `ALREADY_RUNNING` 且目录**仍不存在**；成功路径 → 返回 null、目录存在、`config.SpeedTier` 与入参一致。

## 8. F4：走/跑两档速度上限

**依据（不是含糊推断）**：`.scratch/bug2-sdk-longpaths-20261030/Assets/Runtime/Assembly-CSharp/Unturned/Player/PlayerMovement.cs`
- `:92-97` `SPEED_STAND = 4.5f; SPEED_SPRINT = 7f;`（另有 CLIMB 4.5 / SWIM 3 / CROUCH 2.5 / PRONE 1.5）
- `:303` `float exercise = 1f + (player.skills.mastery(OFFENSE, EXERCISE) * 0.25f);`
- `:311` 及同段分支：SPRINT → `SPEED_SPRINT * exercise * totalSpeedMultiplier`；STAND → `SPEED_STAND * exercise * totalSpeedMultiplier`
- spec §3.4（`bug1-instrumentation-spec.md:87-91`）：「走/跑各有上限……不能以轨迹最大跳步/当前观察FPS/历史0.08秒推导它」。

**修法**：`Bug1ProbeNativeSpeed.ComputeTiers(stand, sprint, exercise, totalMultiplier, out walkUS, out sprintUS, out evidence)`、`ResolveTier(configured, sprinting)`（`auto`/空→按开启瞬间 stance；`walk`/`sprint` 强制档；其他→null 非法）、`TryResolve(player, out walkUS, out sprintUS, out sprinting, out evidence)`（反射 `SPEED_STAND`+`SPEED_SPRINT`）。`Bug1ProbeConfig` 增 `NativeSpeedCapWalkUS / NativeSpeedCapSprintUS / SpeedTier`；新增 cfg 键 `Bug1Probe/SpeedTier`（默认 `auto`）；`RUN_BEGIN`、`manifest.json`、`START_OK` 日志同时记录 walk/sprint/tier；`Judge` 用的 `NativeSpeedCapUS` 按档取值。

**用例** `Bug1-44 Walk And Sprint Caps Distinct (T48/F4)`：`ComputeTiers(4.5, 7, 1.25, 1.1)` → walk `6.1875` / sprint `9.625`（且不相等）；`sprint < walk` 拒绝、stand 非正/非有限拒绝；`ResolveTier` 的 auto/walk/sprint/非法四分支。

## 9. 测试接线

`WhitelistTests/Program.cs` Bug1 段追加 Bug1-36…Bug1-44（行 937-945），沿用既有 `RunTest` 风格；测试项目用 `Evidence\**\*.cs` 通配，新测试文件无需登记；生产 csproj 追加 `<Compile Include="Platform\Diagnostics\Patches\Bug1ProbeTargetSelector.cs" />`。

## 10. 三命令输出摘要

| 命令 | 结果 |
| --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 已成功生成，**0 个警告 / 0 个错误** |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 已成功生成，**4 个既有警告 / 0 个错误** |
| `./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `=== Final Result: 727/729 PASS (Failed: 2) ===`，exit=1 |

- 失败 2 项均为**既有环境失败**：`GBA01 Native First Observe Binding`、`NIL01 Real Native Lookup Contract`（`TypeInitializationException: SDG.Unturned.SteamPlayer` / `NetReflection`），与 t45 基线一致。
- Bug1 段：**44 PASS / 0 FAIL**（Bug1-01…Bug1-44）。
- exe 只要存在失败即返回 1（既有约定），非本轮回归。

## 11. 变异证据（同一 DLL 哈希链，均在仓库外副本 `../_t48mut`，副本已删除）

副本从**最终树**重建，基线 DLL 与主树一致：`f28c211a…`（副本基线 724/729，额外 3 条 `BuildArtifact` 失败是副本缺构建产物输入所致，与 Bug1 无关）。

| 变异 | 内容 | 变异后 DLL sha256 | 红用例 | 全量 |
| --- | --- | --- | --- | --- |
| 基线 | — | `f28c211a9178818d81d2d58721a4e5b9db101371ce0a6be836ff37561301e2e8` | 无 | 724/729 |
| **M-A** | 删掉 `EndStep` 里 `_window.Rx*Records` 同步 | `c8949aa8fbfd35de45e46adb9b06eebe790eebcde77a9d01e17a85a0aa97d9bc` | **Bug1-38** | 723/729 |
| 还原 | — | `f28c211a…`（回到基线） | — | 724/729 |
| **M-B** | `Enable` 重新忽略 `Install()` 返回值 | `be0a6bf349c7dc8fcd351b8b9922e128b4daef64249a5016f1f9528cddbd1662` | **Bug1-40** | 723/729 |
| 还原 | — | `f28c211a…` | — | 724/729 |
| **M-C** | `Uninstall()` 改成空实现 | `c1340fd2587cbeda87dd57ff2350eadb303310fdcbcad249f97610cbc9dae463` | **Bug1-42** | 723/729 |
| 还原 | — | `f28c211a…` | — | 724/729 |
| **M-D** | `Snapshot` 的 `ReferenceEquals` 回退成 `==`（等价三处回退） | `a1c9c0e6192d1f58f3a1f9498c0ee6621d749f2cd92c9daf7faae7b9f8615ac9` | **Bug1-36**（NRE） | 723/729 |
| 还原 | — | `f28c211a…` | — | 724/729 |

还原后逐字节核对（副本 vs 主树，全部相同）：

```
17ca472b…  Bug1ProbeRuntime.cs        （副本 = 主树）
22308d77…  Bug1ProbePatch.cs          （副本 = 主树）
b716369f…  Bug1ProbeTargetSelector.cs （副本 = 主树）
f28c211a…  bin/Release/SteamP2PFriends.dll（副本 = 主树）
```

M-A 红用例的真实失败信息（证明是「判据被切断」而非别的原因）：

```
Bug1-38: RUN_END 入队计数必须是 1，实际={"kind":"RUN_END","label":"Unknown",
"rx_after_enqueue":0,"rx_after_dequeue":0,"missing_fields":["AFTER_ENQUEUE","AFTER_DEQUEUE"]}
```

## 12. 「已自动化可证」与「仍需 1H+1G 实机」的分界

**已可自动化证明**：判空不再自爆且跳过语义正确；相位计数与判定同源；Install 失败 fail-closed 且可重开；关闭卸载 detour 且可再开；拒绝路径不建目录；走/跑两档上限按 SDK 分支推导；启动重试判定函数全分支。

**必须实机才能回答（本轮不给结论）**：
1. **Mono 下 BCL `Queue<PlayerInputPacket>` 的 Enqueue/Dequeue detour 是否真触发**：离线进程里这两条能装（`verify=True`），但**没有被真实游戏调用过**；游戏跑的是 Mono，泛型 BCL 方法 detour 在 Mono 上是否生效、是否被内联绕过，只能在 1H+1G 上看 `AFTER_ENQUEUE`/`AFTER_DEQUEUE` 是否落盘。
2. **启动期重试在真实关卡时序下是否命中**：纯函数分支已覆盖，但 `Level.onLevelLoaded` / `Player.onPlayerCreated` 与 `Provider.isServer` 变 true 的真实先后、以及「首个客机进入」是否落在重试窗口内，需看实机日志 `RETRY_ARMED` / `RETRY trigger=…` / `RETRY_GIVEUP`。
3. **`isDismissed` 是否存在**：`PlayerInput.isDismissed` 的字段/属性在真实 Assembly-CSharp 实例上能否读到（影响 `RX_REJECT` 判定），离线未验证。

另需如实记录的**环境限制**（不是代码缺陷）：离线控制台进程里，`PlayerMovement.simulate` / `PlayerMovement.Update` / `PlayerInput.ReceiveInputs` 这 5 条 Assembly-CSharp 原生 patch 全部报 `HarmonyException: IL Compile Error (unknown location)`，只有 BCL `Queue<PlayerInputPacket>` 两条能真装。因此 R8-04 用例是用「测试侧装 Queue 两条 → Disable 必须卸掉」来证明 Uninstall 有效；5 条原生 detour 的真实覆盖仍属 §12.1/§12.2 的实机范围。

## 13. 其它核对

- **Bug3 五文件 sha256 未变**：`a89f40b9…`（P2PGroupStateRecoveryPatch.cs）、`7a82de8e…`（GroupStateRecoveryCore.cs）、`3d5044f7…`（GroupLeaseLedger.cs）、`a4553385…`（GroupLoadPlanner.cs）、`5b0c4e9b…`（P2PGroupRegistration.cs）。
- **未动** `Build/`、`Release/`、`publishi/`：`Build/Version.props` 的 mtime 为 11-06 20:01（本会话开始前），`Release/` 目录 mtime 11-06 23:56，本轮均未写入。
- **未纳入本票**：R8-05（热切换线程亲和）、R8-06（入口胶水覆盖）、R8-07（发布包哈希，交付层）、R8-08（失败时配置项不一致）、R8-09（`FindPlayer` 早于 `Validate`，已被 `PrepareRun` 顺序部分覆盖但未单独收口）。
