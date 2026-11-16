# T135 / 工程轴复审 t133（Bug2 r5：R26-01..05）

复审人：reviewer-eng。被审：t133（attempt `d38d6e5e-9a4c-4dfb-9a0e-c6f2c50b7ee1`）。任务：t135（attempt `958ca041-19e4-4bba-9fbf-ad86402d3142`）。

## 0. 结论

**needs_revision**（0 blocker / 0 high / 2 medium / 2 low）。

R26-03（死分支）、R26-04（try/finally）、R26-02 的**字段契约**都真闭合了；R26-01 的 `pop`/`brfalse` 两种形态也确实会红。但两条守护仍停在「形状对、语义可错」：

1. **Bug2-34 只钉 `call → brtrue` 的形状，不钉分支体里写了什么**：把 `ok = false;` 改成 `ok = true;`（M7）→ **834/836 全绿**。契约能区分「丢弃(pop)」「反转(brfalse)」，但区分不了「进分支后写错值」。
2. **R26-02 只钉了 getter，没钉 getter 的结果被使用**：`AudioSourcePresent = gun != null && GetGunshotAudioSource(gun) != null` 改成 `= gun != null`（M5）→ **834/836 全绿**。无音频源时门控照放行。

两条都是本模块被连打三轮的同一病灶（实现有、守护无），修复量各为一条断言。

## 1. 被审字节（实测，与票面逐项一致）

| 文件 | 字节 | 行 | sha256 |
|---|---|---|---|
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPatch.cs` | 16414 | 413 | `b90246e61ec015ead3dbc3a8235697e943a56726d4093ad01191123c4bf66191` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs` | 27126 | 695 | `368f391e459c691f5ba4133d97d6009493331190ac30993edf11faf2e8c3bc7e` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioControl.cs` | 5254 | 141 | `478d03c7caf876e7f8508566312f0ea49be1b88ba4be3f647c862e5386c57ec4` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPolicy.cs` | 12643 | 322 | `9e0fa20e685f5314dc4683361e4f2d3db67145fc5f034bb3095341375a71ad11` |
| `Core/Patches/GunshotHostAudio/GunshotIlScan.cs` | 9244 | 275 | `ef512ba0a8fea0540b248fdb1588e3c969b672df2e652edd4f1094c8f8dda1e9` |
| `Core/Registration/…PatchRegistrationDiagnosticsModules.cs` | 5000 | 132 | `22ca8a4977963d5cc2dab6fdf96bcc25091666a986600def2863cdcb3d8c64ef` |
| `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs` | 63207 | 1266 | `92f025a687fd9c1e54aae67adafd9296bc5b866f5d69247acbb3cea4b80dd7fe` |
| `bin/Release/SteamP2PFriends.dll`（两 bin 同） | — | — | `cc40787304dc624f8b7deb22b447ff985c97daa0e2f5888b39cbbd1545203c4a` |

`git status`：五个生产文件与测试 `A `，Diagnostics `M `；复审期间未见 `// mutated` 残留。收工时主树五个生产文件与 DLL 哈希与开工一致。

## 2. 独立复跑（主树）

```
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild                 # 0 警告 / 0 错误（1.21s），DLL cc407873…
dotnet build WhitelistTests/…WhitelistTests.csproj -c Release -t:Rebuild  # 0 错误 / 4 既有警告
WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe             # 834/836 PASS（Failed: 2）
```

Failed 仅 GBA01/NIL01（宿主缺 Unity：`TypeInitializationException SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`）。`PASS [Runtime] Bug2` 35 行、`FAIL [Runtime] Bug2` 0 行；`SPF_DLL_SHA256=cc407873…` 与磁盘一致；副本 `../_t135mut` 独立重建同样得 `cc407873`（确定性）。

## 3. 变异表（7 条，全部自跑；副本内 `-t:Rebuild`，逐条从 `.t135bak` 还原）

| # | 变异（old → new） | 变异 DLL | 计数 | 新增红 |
|---|---|---|---|---|
| CM1 | `if (!ApplyGunshotModuleOutcome(ok, _harmony)) { ok = false; }` → 裸调用（Captain M1） | `03de2910f6fc4834` | 833/836 | **Bug2-34**（`got pop at idx=60`） |
| CM2 | `GetGunshotAudioSource` 开头 `if (gun != null) { return null; }`（Captain M2） | `f6f13044d1e46a79` | 833/836 | **Bug2-35** |
| MV3 | 分支体清空：`{ ok = false; }` → `{ }` | `81f9d058d58b16f6` | 833/836 | **Bug2-34**（编译器折成 `call;pop`） |
| MV4 | 条件反转：`if (!Apply(...))` → `if (Apply(...))` | `055b768a71b07dae` | 833/836 | **Bug2-34**（`got brfalse.s`） |
| **MV5** | `AudioSourcePresent = gun != null && GetGunshotAudioSource(gun) != null,` → `= gun != null,` | `c34e69a62273c850` | **834/836** | **无（全绿）** |
| **MV7** | 分支体内 `ok = false;` → `ok = true;` | `ef7bd76fce55f52c` | **834/836** | **无（全绿）** |
| MV6 | 测试侧：Bug2-31 末尾注入 `Assert(false, "T135 deliberate failure…")`（只为验证 finally） | `cc407873`（生产未变） | 833/836 | 仅 Bug2-31；其余 **34 条 Bug2 全 PASS** |

每条还原后源文件回 §1 哈希、DLL 回 `cc407873…`。CM1/CM2 的红名单与 Captain 一致，DLL 哈希不同（见 R27-04）。

## 4. 五个重点逐条

### 4.1 Bug2-34 是否钉「采纳」而非「调用」—— 钉住了三种坏形态里的两种，漏了第三种

- 丢弃返回值（CM1）→ `pop`，红 ✓
- 条件反转（MV4）→ `brfalse.s`，红 ✓
- 分支体清空（MV3）→ C# 编译器把空 `if` 折成 `call;pop`，红 ✓（意外但有效）
- **分支体写错值（MV7）→ `call;brtrue;ldc.i4.1;stloc`，834/836 全绿** ✗

即契约要求「call 后紧跟 brtrue」，这能证明「返回值进了条件」，但不能证明「条件成立时把 ok 置成 false」。见 R27-01。

### 4.2 Bug2-35 契约 —— 字段契约真钉住，接线没钉

`Test_AudioSourceReflectionContract`（`GunshotHostAudioTests.cs:883-935`）确实断言了：字段存在（`GetField` + `AccessTools.Field` 同一 FieldInfo）、类型 `== typeof(AudioSource)`、测试侧 ldfld 读回同一实例、生产 `GetGunshotAudioSource` 反射调用读回**同一实例**（`ReferenceEquals`）。M2（恒 null）红 ✓。字段缺失时生产返回 null → `AudioSourcePresent=false` → `IsEligible` 不通过 → 不补偿，gate 行有 `decision=NotEligible`（fail-closed 可观测）✓。

但**消费端没有守护**：`Runtime.cs:385` 的 `AudioSourcePresent = gun != null && GetGunshotAudioSource(gun) != null` 改成 `= gun != null`（MV5）全绿——getter 再正确也没人检查它的结果是否被用上。见 R27-02。

### 4.3 R26-03 删除的插入数守卫确实不可达 —— 接受

不是只看注释：`Fire_Transpiler`（`Patch.cs:349-388`）在 `TryLocateAnchor` 失败时先 `TranspilerInsertions = -1` 再 **throw `InvalidOperationException`**；`RegisterManual` 的 `catch`（:150-156）统一 `Uninstall()` + `Fail(...)` + return false。`Test_RealHarmonyFailureLeavesNoPatch`（:449-471）正是构造这条异常路径并断言「返回 false / observer postfix 与 fire patch 都被摘掉 / FailClosed」。另一侧，「插入数确实等于 2」由 `Test_RealFireTranspilerInsertion`（:982-1035）在真实 `UseableGun.fire` IL 上断言（`TranspilerInsertions==2`、`patched.Count==codes.Count+2`、两条插入指令位置与操作数）。→ 守卫删除不会失去唯一保护，**关闭**。

### 4.4 Bug2-30/31 的 try/finally —— 实测有效

MV6 把 Bug2-31 末尾注成必然失败：该用例计 FAIL，**其余 34 条 Bug2 全部 PASS**（`PASS [Runtime] Bug2` 34 行），说明 finally 里的 `Uninstall()` + 三个 `ResetForTest()` 确实把 `PostfixVerifierForTest`/`SkipFirePatchForTest`/`Enabled`/已装 patch 都清了，无跨用例污染。**关闭**。

### 4.5 横向扫描「实现有、守护无」

| 路径 | 有无红线 | 说明 |
|---|---|---|
| `InitializeDiagnosticStage` 分支体内写入值（`ok = false`） | **无**（MV7 全绿） | 只钉 `call;brtrue` 形状 → R27-01 |
| `Collect` 的 `AudioSourcePresent` 接线 | **无**（MV5 全绿） | getter 有契约，消费端没有 → R27-02 |
| `SessionEpoch`/`BuildKey` 的 `catch { return 1UL; }` | 无 | 计算失败时把所有会话压成同一 epoch，跨会话去重键碰撞（可能吞掉新会话首发），且无告警 → R27-03 |
| `Collect` 的 `catch → CollectionError` | 部分 | 异常时 15 项多为 false → 门控 fail-closed；但无「异常路径」用例 |
| `CollectObserverForTest` seam 在生产 hot path | 有（null 检查） | 仅测试注入，开销一次判空 |
| 锚点定位/插入/前缀/终结器/账本/RNG/Aborted/热开启/缺省关闭 | 有 | 前几轮已钉，本轮 CM1..MV4 复核仍红 |

## 5. Findings

**R27-01（medium，契约强度）`Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs:86-89`**
Bug2-34 只验证「`ApplyGunshotModuleOutcome` 调用后紧跟 brtrue/brtrue.s」。分支体里的写入值没有被约束：`ok = false;` → `ok = true;`（MV7，DLL `ef7bd76f`）834/836 全绿。也就是说「模块失败 → 诊断阶段 ok=false」仍可被语义反转而无人发现，本轮 R26-01 只补了半格。
requiredFix（最小）：在 `Test_DiagnosticsStageAdoptsModuleResult` 里继续向后匹配分支目标处的 `ldc.i4.0` + `stloc`（Release 形态 `call;brtrue.s;ldc.i4.0;stloc.0`，Debug 多一次 `ldloc`），断言写进 ok 的常量是 0；或把该段抽成 internal 纯函数 `static bool FoldGunshotOutcome(bool ok, bool moduleOk)` 并直接对四组入参断言。

**R27-02（medium，接线无守护）`Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs:385`**
`AudioSourcePresent = gun != null && GetGunshotAudioSource(gun) != null` 改成 `= gun != null`（MV5，DLL `c34e69a6`）834/836 全绿。R26-02 修好了 getter 本身，但「getter 的结果被用作 AudioSourcePresent」这一跳没有任何用例。后果：无音频源时门控放行 → 补偿 leaf 打向空引用（被 `catch → FailClosed` 兜住，不是静默，但也无红）。
requiredFix：在 `Test_CollectWiringFromSdkSources` 里补一步——把 `gunshotAudioSource` 置 null，断言 `captured.AudioSourcePresent == false`（或将 `AudioSourceProbeForTest` 设成 `_ => null` 后走 Collect 断言同一事实）。

**R27-03（low，静默退化）`Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs:575-584`**
`SessionEpoch` 计算失败时 `catch { return 1UL; }`：所有会话的 `SessionEpoch` 都变成 1，去重键可能跨会话碰撞（新会话的首发被判为重复而不补偿），且 gate 行看不出异常。无用例（也就没有守护）。
requiredFix：失败时至少写 `LastGateLine` 的 `RngUnavailable`/`ClockUnavailable` 同款标记，或直接 `FailClosed`；补一条 seam 注入异常的用例。

**R27-04（low，证据可复现性）**
CM1/CM2 的红名单与 Captain 完全一致，但 DLL 哈希不同（我 `03de2910`/`f6f13044` vs `0ac32e5d`/`311e668e`）；副本对基线可确定性复现 `cc407873`。t133 报告已给 old→new 原文，本轮我按自己的原文重放——只要双方表格持续附原文，差异不影响判定，但「按哈希比对」仍不可用。

## 6. 判定边界

- 只对 §1 的字节有效。**不声称 Bug2 已修复、不声称主机可听见**。
- 仍为 UNKNOWN：真机听感（S1）、三端互听（S4）、Mod 兼容、`playGunshot` 观察器在真机的可触发性、真实 Harmony 对 `fire` 的 IL 重写。
- R26-03/04 与 R26-02 的字段契约本轮**确认关闭**；R26-01 只闭合一半（形状红、值不红）。

## 7. 复现命令

```bash
cd "/d/Agent-工作目录/DevelopMyUNMultiplayerModAndModloader"
tar -cf - --exclude=.git --exclude=.scratch --exclude=bin --exclude=obj --exclude=Release \
  --exclude=publishi --exclude=Assets --exclude=.agent-teams --exclude=Develop-Stage --exclude=publish \
  -C SteamP2PFriends-0.2.4-Experimental . | tar -xf - -C _t135mut
cd _t135mut && cp -a ../SteamP2PFriends-0.2.4-Experimental/Libs .
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe      # 834/836

# 关键变异（其余见 /d/tmp/mut135.py）
python /d/tmp/mut135.py MV7_oktrue && dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild \
  && cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/ \
  && ./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe  # 834/836（应红而未红）
python /d/tmp/mut135.py restore
```

<!-- self_sha256(body)=06f87cf1f360a999b593964398b3a25bf98738c59562d2fa12a1675af584c593 -->
