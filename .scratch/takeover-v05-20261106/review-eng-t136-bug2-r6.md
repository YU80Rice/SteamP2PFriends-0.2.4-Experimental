# t136 工程轴复审（r6）：语义纯函数是否封住规避形态

审查官：reviewer-eng（工程轴）· 任务 t137 · 被审 t136 · attempt ae9ae6fb-1f62-48db-acaa-7cfc2180b269
日期：2026-11-08

## §0 结论

**verdict = needs_revision**（0 blocker / 0 high / 1 medium / 2 low）。

t136 把「结果是否被采纳」从 IL 形状断言改成语义真值表 + stage 级隔离执行，方向正确，且确实封住了上一轮的两条规避形态：

- 把 `ok = Fold(...)` 整体写成 `ok = true;`（本轮 M1）→ 835/838，只红 Bug2-34；
- 把 `AudioSourcePresent = HasGunshotAudioSource(gun)` 放宽成 `gun != null`（本轮 M2）→ 835/838，只红 Bug2-36；
- Fold 体改成 `stageOk || moduleOk`（M5）→ 835/838，只红 Bug2-34；
- `HasGunshotAudioSource` 改成只判 `gun != null`（M4）→ 835/838，只红 Bug2-36；
- 删除 `SessionEpochFallbackCount++`（M7）→ 835/838，只红 Bug2-37。

**但语义化只覆盖了 `moduleOk` 一个操作数。** 把 Fold 的第一个实参钉成 `true`（丢弃先前 stage 失败）或在接线处直接 `ok = gunshotModuleOk`，两条都是 **836/838 全绿**（M3 / M6）。也就是说「stage 已失败时不得被模块成功翻回 true」这条语义，在接线层没有任何用例；真值表用例只证明了 Fold 函数本身。这正是本轮要收口的那一类回归面，故判 needs_revision，修法很小（见 R28-01）。

## §1 被审字节（实测，与 Captain 锁逐项一致）

| 文件 | 字节 | 行 | sha256（前16） |
|---|---|---|---|
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPatch.cs` | 16414 | 417 | `b90246e61ec015ea` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs` | 28493 | 718 | `cf1efab2cc1f2f32` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioControl.cs` | 5254 | 141 | `478d03c7caf876e7` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPolicy.cs` | 12643 | 322 | `9e0fa20e685f5314` |
| `Core/Patches/GunshotHostAudio/GunshotIlScan.cs` | 9244 | 275 | `ef512ba0a8fea054` |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs` | 5910 | 149 | `fef8ff38664b032a` |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationOrchestration.cs` | 5486 | — | `21bf98efb62c7f7e` |
| `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs` | 69404 | 1378 | `662a990640a107c3` |
| `bin/Release/SteamP2PFriends.dll` | 1588224 | — | `e897481cb8a9b164` |

## §2 独立复跑

| 命令 | 退出码 | 结果 |
|---|---|---|
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 | 0 警告 0 错误，DLL 复现 `e897481c`（确定性） |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 0 | 0 错误，4 条既有警告 |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | 1 | **836/838 PASS**，FAIL 仅 `GBA01`/`NIL01`（控制台缺 Unity 运行时），`FAIL [Runtime] Bug2` = 0（37/37） |

`[T14] SPF_DLL_SHA256=e897481c…` 与磁盘一致。

## §3 变异表（独立副本 `../_t137mut`，逐条还原后均回到 `e897481c`）

| # | 改法（文件:行 → 语义） | 变异 DLL | 结果 | 新增红名单 |
|---|---|---|---|---|
| M1 | `DiagnosticsModules.cs:93` `ok = Fold(ok, gunshotModuleOk)` → `ok = true;` | `afc9ebdd3d2787f4` | 835/838 | **Bug2-34** |
| M2 | `Runtime.cs:391` `AudioSourcePresent = HasGunshotAudioSource(gun)` → `= gun != null` | `9fd34e892bbfe653` | 835/838 | **Bug2-36** |
| **M3** | 同上 M1，但只丢第一个实参：`ok = Fold(true, gunshotModuleOk);` | `31f791792c67adb3` | **836/838** | **无（全绿）** |
| M4 | `Runtime.cs:604-607` `HasGunshotAudioSource` 体 → `return gun != null;` | `d351b19aaa529b5b` | 835/838 | **Bug2-36** |
| M5 | `DiagnosticsModules.cs:146` Fold 体 → `return stageOk \|\| moduleOk;` | `186afec3105d4945` | 835/838 | **Bug2-34** |
| **M6** | 同上 M1，整体绕过 Fold：`ok = gunshotModuleOk;` | `badf3ce300f85970` | **836/838** | **无（全绿）** |
| M7 | `Runtime.cs:594` 删 `SessionEpochFallbackCount++;` | `6298c90d6240f76c` | 835/838 | **Bug2-37** |
| M8 | 删 `SessionEpochFallbackReason = ex.GetType().Name;` | 构建失败 | — | 不可构造（`ex` 变未使用 → CS0168 → 警告即错误） |

说明：

- M1/M2 与 Captain 自跑的**红名单一致**（830/834 与 835/838 的差异来自 Captain 基线不同轮次），DLL 哈希不同（我 `afc9ebdd` / Captain `9ad9d16d`），属 R25-05/R27-04 同一类"证据不可逐字节复现"缺口（R28-03）。
- M3/M6 **必须记为 finding**：它们不是"等价改写"，而是把 `stageOk` 真的丢掉——只要先前诊断阶段（UnityLogBridge / NativeSnsLogProbe / Bug1Probe / Bug1ProbeControl）任一失败，stage 仍会返回 true。
- M8 无法单独构造，说明"原因"字段与 `ex` 变量被编译器绑在一起；这不算缺陷，只是记录该点不可用删除法证伪。

## §4 五问逐条

**问 1：纯函数化是否终止了"形状 vs 语义"这一类？**
部分终止。`ok = Fold(...) && true;` / `|| false;` 这类等价改写仍是绿——这是应该的（语义未变）。真正的问题是**弱化改写**：M3（`Fold(true, …)`）与 M6（`ok = moduleOk`）全绿，说明"丢弃 stageOk"没有任何用例能发现。结论：`moduleOk` 方向已封住，`stageOk` 方向未封住。

**问 2：Bug2-36 是否覆盖消费端全链？**
是。`GunshotHostAudioTests.cs:991-999` 用 `AudioSourceProbeForTest = _ => null` 走真实 `Collect`，断言 `AudioSourcePresent == false` 且 `IsEligible(facts) == false`；`Runtime.cs:197` 的 `!IsEligible` 早退由既有用例（`:373`/`:1216` 的 `decision=NotEligible`）覆盖到"不调 leaf"。不是只断言函数返回值。

**问 3：生产签名上的 `gunshotStageOnlyForTest` 是否可接受？**
**建议改形（不阻塞本轮）**。理由：①默认 `false`，唯一生产调用点 `PatchRegistrationOrchestration.cs:46` 是 `() => InitializeDiagnosticStage()`，不传参 → 当前无误用路径；②但它是 `private` 方法签名上的测试开关，与仓库既有 seam 风格（`internal static Func<...> XxxForTest` + `ResetForTest` 清零）不一致，且用例用 `AccessTools.Method(..., new[]{typeof(bool)})` 绑定，未来再加参数会让用例以"方法找不到"失败而非语义失败。建议改为 `internal static bool GunshotStageOnlyForTest;`（Reset 清零），生产签名保持无参。

**问 4：`SessionEpoch` 的降级现在可观测吗？**
是。`Runtime.cs:594-596` 计数 + 原因；`SessionEpochFallbackCount/Reason` 为 internal 只读属性（`:79-80`），`ResetForTest` 清零（`:516-517`）；Bug2-37 同时断言正常路径不计数、异常路径计数=1 且原因=`InvalidOperationException`。M7 证明删计数即红。

**问 5：横向再扫，还有"实现存在但停用不红"的路径吗？**
有，但只剩 §0 的 stageOk 方向（M3/M6）。其余关键 fail-closed 分支本轮均有红能力：音频源判定（M2/M4）、Fold 语义（M1/M5）、Fallback 观测（M7）、transpiler 锚点失配（Bug2-24，t135 已验）、观察器缺失（Bug2-20，t135 已验）、leaf==null（R24-02，t135 已验）。

## §5 Findings

### R28-01（medium）stage 既有失败在接线层无覆盖——`stageOk` 可被结构性地丢弃
- 位置：`Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs:93`（`ok = FoldGunshotOutcome(ok, gunshotModuleOk);`）
- 证据：M3 `ok = FoldGunshotOutcome(true, gunshotModuleOk);` → `31f791792c67adb3`，836/838 全绿；M6 `ok = gunshotModuleOk;` → `badf3ce300f85970`，836/838 全绿。
- 影响：诊断阶段前面四个子模块（UnityLogBridge / NativeSnsLogProbe / Bug1ProbePatch / Bug1ProbeControl）任一失败时，`ok` 已为 false；上述改写会让 stage 仍返回 true，注册编排据此继续，失败被吞（与 t130 R26-01 同类，只是换了操作数）。
- 为什么现有用例抓不到：`InitializeDiagnosticStage(true)` 隔离模式**跳过**了前面四段，`ok` 起点恒为 true；Bug2-34 只覆盖 `moduleOk` 方向。
- requiredFix（最小）：加一个与既有 seam 同风格的开关注入，例如
  - `internal static bool PriorStageFailedForTest;`，把 `bool ok = true;` 改为 `bool ok = !PriorStageFailedForTest;`；
  - Bug2-34 追加两组：`PriorStageFailedForTest=true,moduleOk=true` → stage 必须 false；`PriorStageFailedForTest=false,moduleOk=true` → true。
  - 验收判据：M3/M6 两条改写在修复后必须变红（只红 Bug2-34 即可）。

### R28-02（low）生产方法签名携带测试参数
- 位置：`DiagnosticsModules.cs:37`（`private bool InitializeDiagnosticStage(bool gunshotStageOnlyForTest = false)`）
- 现状可接受：默认 false、唯一生产调用点不传参（`PatchRegistrationOrchestration.cs:46`），无当前误用路径。
- 建议：改为 `internal static bool GunshotStageOnlyForTest;`（`ResetForTest` 清零），生产签名恢复无参，与 `GunshotModuleInitializerForTest` 等既有 seam 同构；同时避免用例用 `new[]{typeof(bool)}` 反射绑参。

### R28-03（low）变异哈希与 Captain 不一致
- M1 我 `afc9ebdd3d2787f4` / Captain `9ad9d16d…`；M2 我 `9fd34e892bbfe653` / Captain `407549f5…`。红名单一致、构建确定性已验（同副本两次重建同哈希），差异只能来自替换文本未逐字公开。延续 R25-05/R27-04。

## §6 结论边界（不得越界声称）

- 本判定只针对本报告 §1 的字节；任何源码变更后需重新绑定。
- **不等同于 Bug2 已修复**，也不等同于"主机可听见枪声"。以下仍为 UNKNOWN，需实机 1H+2G：真实 Harmony transpiler 在游戏内是否被 JIT 接受、远端听感、三端互听顺序、Mod 兼容、帧级 RNG 恢复。
- 自动化侧只证明：门控事实、折叠语义、fallback 可观测、transpiler 插入形状在**控制台无 Unity 运行时**下的静态/合成证据。

## §7 复现

```bash
# 基线
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe   # 836/838

# 变异（仓库外副本，逐条还原）
python D:/tmp/mut137.py M3   # ok = Fold(true, gunshotModuleOk);
dotnet build SteamP2PFriends.csproj -c Release
cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe   # 836/838 → 全绿
python D:/tmp/mut137.py restore
```

副本 `../_t137mut` 已删除；主树收工哈希：Patch `b90246e6`、Runtime `cf1efab2`、Diagnostics `fef8ff38`、DLL `e897481c`、836/838。

<!-- self_sha256(body)=785d13bdbed490876cc76d9fbe6eb49e83d5ee3e0f70e175c95259cad4340f95 -->
