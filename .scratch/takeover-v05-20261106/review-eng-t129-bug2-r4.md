# T130 / 工程轴复审 t129（Bug2 r4：R25-01..03 守护补齐）

复审人：reviewer-eng（工程轴）。被审：t129（attempt `aca368ca-14ad-4671-b8ac-f1bd26b4a125`）。任务：t130（attempt `ed50ec24-10d9-4dda-b26b-457d2046091f`）。

## 0. 结论

**needs_revision**（0 blocker / 0 high / 2 medium / 3 low）。

三条被点名的半覆盖确实各补上了一条真能红的用例（CM1 → Bug2-30+31，CM2 → Bug2-32，R25-03 的 IL 契约在），测试缝默认 null 且 `ResetForTest` 全清。但有两条 medium 是同一病灶的残余，且都正是本合同第 4 条要的「实现有、守护无」：

1. **`ok = ApplyGunshotModuleOutcome(ok, _harmony)` 的赋值**没有守护：把结果丢掉（MV4）832/834 全绿——R25-03 只钉了「函数语义」和「调用存在」，没钉「结果被采纳」。
2. **`GetGunshotAudioSource` 的真实反射路径**没有守护：让它恒 null（MV3）832/834 全绿——SDK 字段改名会让 `AudioSourcePresent` 恒 false、模块永不补偿，且无任何红。

另有 3 条 low：插入数守卫不可达（MV5 全绿）、两条 r4 用例缺 try/finally、变异哈希不可复现。

## 1. 被审字节（实测，与 Captain 表一致）

| 文件 | 字节 | 行 | sha256 |
|---|---|---|---|
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPatch.cs` | 16301 | 417 | `6426e77f801d32f300641cb4f644d3af7137ed261054de62cdc87936b119b1c5` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs` | 25941 | 670 | `6fb7888316bcb28180c668cd478112c2c3dad65611d18da27c72cb97a75ed4c3` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioControl.cs` | 5254 | 141 | `478d03c7caf876e7f8508566312f0ea49be1b88ba4be3f647c862e5386c57ec4` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPolicy.cs` | 12643 | 322 | `9e0fa20e685f5314dc4683361e4f2d3db67145fc5f034bb3095341375a71ad11` |
| `Core/Patches/GunshotHostAudio/GunshotIlScan.cs` | 9244 | 275 | `ef512ba0a8fea0540b248fdb1588e3c969b672df2e652edd4f1094c8f8dda1e9` |
| `Core/Registration/…PatchRegistrationDiagnosticsModules.cs` | 4832 | 128 | `de6b54972bddb4116583f89c18ee6aebd68f2ad3489c7022e3e5550e0ed04bf6` |
| `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs` | 56531 | 1139 | `78699cf2b87a9637a5f2eb635ccdded9902b0560f42e31b47b85979be3336016` |
| `WhitelistTests/Program.cs` | 130746 | — | `14e825faf598249fb0cb1ddfe2082fa4cf96cc59db2c08d527cf06f8668fcee8` |
| `bin/Release/SteamP2PFriends.dll` | — | — | `44bc3d74ab2365e9ce78bdeafc85f2b7a1d5b9af72d78725a0886cbb27167662` |

`git status`：五个生产文件与测试均 `A `（已暂存），`PatchRegistrationDiagnosticsModules.cs` 为 `M `；工作区在复审期间未见新的 `// mutated` 残留（与 t128 那轮不同）。

## 2. 独立复跑（主树）

```
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild            # 0 警告 / 0 错误（1.29s），DLL 44bc3d74…
dotnet build WhitelistTests/…WhitelistTests.csproj -c Release -t:Rebuild  # 0 错误 / 4 既有警告
WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe         # 832/834 PASS（Failed: 2）
```

Failed 仅 GBA01/NIL01（`TypeInitializationException → SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`，测试宿主缺 Unity）。`PASS [Runtime] Bug2` 33 行、`FAIL [Runtime] Bug2` 0 行；`[T14] SPF_DLL_SHA256=44bc3d74…` 与磁盘一致；重建可复现同一哈希。

## 3. 变异表（5 条，全部自跑；副本 `../_t130mut`，独立重建先复现 44bc3d74…）

| # | 变异 | 变异 DLL | 计数 | 新增红 |
|---|---|---|---|---|
| CM1 | `Patch.cs:116-118` `bool postfixInstalled = <ternary>` → `= true`（Captain M1） | `650bd0de18eb886b` | 830/834 | **Bug2-30、Bug2-31** |
| CM2 | `Runtime.cs:371` `LevelLoaded = Level.isLoaded` → `= Provider.isConnected`（Captain M2） | `397f9d63b803e8ed` | 831/834 | **Bug2-32** |
| MV3 | `Runtime.cs:581` `GetGunshotAudioSource` 首行插入 `if (gun == null \|\| gun != null) return null;` | `bb1af4f495a6f6d3` | 832/834 | **无（全绿）** |
| MV4 | `DiagnosticsModules.cs:85` `ok = ApplyGunshotModuleOutcome(ok, _harmony);` → 丢弃返回值 | `8e3237424ddbf70f` | 832/834 | **无（全绿）** |
| MV5 | `Patch.cs:140` `if (TranspilerInsertions != 2)` → `… && false` | `ca2fe4d3d71a3568` | 832/834 | **无（全绿）** |

红名单与 Captain 的 CM1/CM2 一致；DLL 哈希不同（我 `650bd0de`/`397f9d63` vs `d5c236a6`/`38ab52fa`），同 R25-05 的可复现性问题，见 R26-05。每条跑完从 `D:/tmp/t130p/` 原始副本还原，五个源文件回 §1 哈希、DLL 回 `44bc3d74…`。

## 4. 四个重点的判定

**4.1 三条新用例是否真承重 —— 两条承重、一条只承重一半**

- Bug2-31（R25-01）**承重**：`PostfixVerifierForTest=false` 且 `SkipFirePatchForTest=true`，真实 Harmony 先装 postfix，回查返回 false → `Uninstall()` → `Fail("native leaf postfix not installed")`；断言了拒绝启用、`Installed==false`、`NativeObserverInstalled==false`、原因可读、两个目标上零我方 patch、`FailClosed`。CM1 只让它和 Bug2-30 变红，说明它钉住的是"回查结果被采纳"这条线。**与"Harmony 静默失败"的等价性成立**：真实 `harmony.Patch` 已执行，只是回查看不到——正是"返回成功但没挂上"的形态（不同 Harmony 版本包一层、或 target 被其它补丁替换时会出现）。
- Bug2-32（R25-02）**承重但有边界**：它只断言 `Connected / LevelLoaded / NotDedicated` 三项来自 `Provider.isConnected / Level.isLoaded / Dedicator`，其余 12 个源（`ServerActive`、`P2PHostMode`、`ShooterChannelIsLocal` 等）仍是"改了也不红"。
- Bug2-33（R25-03）**承重一半**：`ApplyGunshotModuleOutcome` 的三态（true→false、true→true、false→false）都断言了，IL 也钉了 `InitializeDiagnosticStage` 调用它——但**没钉调用结果被赋回 `ok`**（MV4 全绿）。见 R26-01。

**4.2 测试缝泄漏 —— 默认 null、Reset 全清，但两条新用例无 try/finally**

- 8 个缝（`FactsProviderForTest/LeafInvokerForTest/LeafResolverForTest/AudioSourceProbeForTest/RandomStateSave|RestoreForTest/CollectObserverForTest/PostfixVerifierForTest` + `Fire/PlayGunshotTargetForTest/Skip*ForTest`）全部是无初始化器的 `static` 字段 → 默认 null；`Runtime.ResetForTest`（:483-519）逐项置 null 并把 `_supplementalLeafDepth/_nativeObserverInstalled/Enabled` 复位；`Patch.ResetForTest`（:207-225）清 `PostfixVerifierForTest` 与两个 target。
- 但 Bug2-30 与 Bug2-31 **没有 try/finally**：Bug2-31 设了 `PostfixVerifierForTest=false`、`SetEnabled(true)`，末尾只 `Uninstall()`；一旦中途断言失败，缝与 Enabled 会带进后续用例。当前用例顺序（32/33 每条开头都 Reset）掩盖了它，这正是 R22-02 的同款形态。→ R26-04

**4.3 传播链 —— 成对，但有"结果丢弃"旁路**

`InitializeDiagnosticStage`（:37-98）里 `ok = ApplyGunshotModuleOutcome(ok, _harmony)`；后者在 `initializer(harmony)==false` 或抛异常时 return false，成功时 return `ok`（保留入参的 false）；stage 6 返回该 `ok`，`PatchRegistrationOrchestrator.Execute`（:141-148）对 false 直接 `return false` 并附 TraceId。链路本身是成对的、无顺序假设（同步调用、无异步/线程切换）。唯一旁路就是赋值被丢（MV4）。

**4.4 横向扫描「实现有、守护无」**

| 路径 | 有无红线 | 备注 |
|---|---|---|
| `GetGunshotAudioSource` 真实反射 | **无**（MV3 全绿） | 字段缺失 → `AudioSourcePresent=false` → `IsEligible=false` → 永不补偿，静默 |
| `ok = ApplyGunshotModuleOutcome(...)` 赋值 | **无**（MV4 全绿） | 只钉了函数与调用 |
| `TranspilerInsertions != 2` 分支 | **无**（MV5 全绿） | 该分支在现有流程下不可达（transpiler 失败走抛异常，已被 Bug2-24 覆盖） |
| `Collect` 的 catch → `CollectionError` | 无 | 低风险，异常即 fail-closed |
| `BuildKey` 的 FNV/会话世代 | 部分 | Bug2-22 覆盖账本，非键算法 |
| 其余（锚点定位/插入/前缀/终结器/账本/RNG/Aborted/热开启/缺省关闭） | 有 | 前几轮已钉 |

## 5. Findings

**R26-01（medium，传播线）`Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs:85`**
R25-03 只闭合到"函数语义 + 调用存在"：把 `ok = ApplyGunshotModuleOutcome(ok, _harmony);` 改成丢弃返回值（MV4），832/834 全绿。也就是说"模块初始化失败 → 诊断阶段 ok=false"这条真正的传播，在实现回退时不会红。
requiredFix：改成 `if (!ApplyGunshotModuleOutcome(ok, _harmony)) { ok = false; ... }` 之类的分支形态，让现有 `IlContractProbe.CountCallsWithBranchOnResult` 能钉住；或把 stage 返回值写成 `ok &= …` 并在 Bug2-33 里加一条"seam 注入 false → InitializeDiagnosticStage 返回 false"的直接断言（需先把该 stage 变 internal 可调）。

**R26-02（medium，fail-silent）`Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs:581-597`**
`GetGunshotAudioSource` 的反射路径无任何断言：让它在运行时恒 null（MV3），全部用例仍绿。真实后果是 SDK 字段改名/类型变化后 `AudioSourcePresent` 恒 false → 门控恒不通过 → 模块永不补偿，且日志里只有 gate 行，没有一次性告警。
requiredFix：加一条离线契约用例（`AccessTools.Field(typeof(UseableGun),"gunshotAudioSource") != null` 且 `FieldType == typeof(AudioSource)`，风格同 Bug2-19 对 `playGunshot` 的处理）；或在 `Install` 里解析该字段，缺失即 `Fail("audio source field missing")` 并 fail-closed——后者更符合本模块"装不上就报"的既有口径。

**R26-03（low，死分支/无守护）`Core/Patches/GunshotHostAudio/GunshotHostAudioPatch.cs:140-146`**
`if (TranspilerInsertions != 2)` 的失败分支在现有实现下不可达（transpiler 要么置 2，要么抛异常走 catch），MV5 停用它全绿。要么删掉并注明"由 catch 兜底"，要么补一条让 transpiler 返回 0/1 条插入的 seam 用例。

**R26-04（low，测试卫生）`WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs:640-655 / 675-710`**
Bug2-30 与 Bug2-31 无 try/finally：失败路径会留下 `PostfixVerifierForTest`、`SkipFirePatchForTest`、`Enabled=true`。当前用例顺序掩盖（32/33 各自开头 Reset），但任何"加一条中间用例"的改动都可能踩到 R22-02 的老毛病。

**R26-05（low，证据可复现性）**
CM1/CM2 的红名单与 Captain 完全一致，但 DLL 哈希逐条不同（`650bd0de`/`397f9d63` vs `d5c236a6`/`38ab52fa`）；我的副本对基线可确定性复现 `44bc3d74`，所以差异只能来自两边替换文本不是逐字相同。延续 R25-05：变异表应附 old→new 原文。

## 6. 判定边界

- 只对 §1 的字节有效。**不声称 Bug2 已修复、不声称主机可听见**。
- 仍为 UNKNOWN：真机听感（S1）、三端互听（S4）、Mod 兼容、`playGunshot` 观察器在真机的可触发性、真实 Harmony 对 `fire` 的 IL 重写（测试宿主缺 UnityEx）。
- R25-01/02/03 的**代码**都已正确落地；本轮两条 medium 都是"再退一步就静默"的守护缺口，修复量各为一条用例或一处 Fail-closed，不含架构改动。

## 7. 复现命令

```bash
# 副本
tar -cf - --exclude=.git --exclude=.scratch --exclude=bin --exclude=obj \
  --exclude=Release --exclude=publishi --exclude=Assets --exclude=.agent-teams --exclude=Develop-Stage . \
  | tar -xf - -C ../_t130mut
cd ../_t130mut && cp -a ../SteamP2PFriends-0.2.4-Experimental/Libs .
dotnet build SteamP2PFriends.csproj -c Release
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release
cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe        # 832/834

# 变异
python /d/tmp/mut130.py MV4 && dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild \
  && cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/ \
  && ./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe    # 832/834（应红而未红）
python /d/tmp/mut130.py restore
```

<!-- self_sha256(body)=ed73aef1ea9b285c7c2761f6d1bddb560902a07ed3c9a0e6a62a399ddb6ce02c -->
