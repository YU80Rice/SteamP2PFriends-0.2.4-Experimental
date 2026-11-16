# T125 工程轴复审：t124（Bug2 主机侧枪声补偿 r2）

审查对象：t124 交付的源码字节与构建产物（DLL `a714d055…`）。
结论：**needs_revision**（0 blocker / 0 high / 4 medium / 5 low）。
判据口径：本报告只判「这批字节在工程上是否可进入实机待测」，不判 Bug2 是否满足用户需求、不声称可听性通过。

已复核 Captain 指定两条变异：MUT-A 与 MUT-B 均在仓库外副本独立复现，DLL 哈希与红名单逐条一致。
新增独立证据：三条 fail-closed/采集分支的变异**全部全绿**（见 §3 C/D/F），是本轮打回的主因。

---

## 1 字节锁定（实测，2026-11-08）

| 文件 | 字节 | sha256 |
| --- | --- | --- |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioControl.cs` | 5007 | `f65e995c5e9ef1383542445f93b66dd34ce9f6c33295f7972aa303c2d1cae671` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPatch.cs` | 13525 | `a74633f314bc2a772adb4dda8a483fda6e946fc6424bab841b00d33161adc905` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPolicy.cs` | 11618 | `064e67450fb59e61a67ac8fba13e38a951b08907581b3ba8be18b4b1c291df6d` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs` | 20267 | `4bc62d3af706199ebb0b15956b28222f2cb248d1ddae9a8c97022dc91d3f9ac2` |
| `Core/Patches/GunshotHostAudio/GunshotIlScan.cs` | 9244 | `ef512ba0a8fea0540b248fdb1588e3c969b672df2e652edd4f1094c8f8dda1e9` |
| `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs` | 31949 | `9fa0bac3eba4a4e03f4bd589e6ace078d91c93606bf9055a420e7f67bcebfbb5` |
| `WhitelistTests/Program.cs` | 158108 | `276f09a7e8e4f2a0c127d78e4409e2a1828dae0cb406d9a07f61a6d194208d77` |
| `SteamP2PFriends.csproj` | 28133 | `e327865c031521fde970c139dc31c15eb02fcb648ebf9bd3ce75f46f0b4a68e5` |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDomainModules.cs` | 3913 | `6ad2099835000cd99efbfb16d2e8c4198ca832ed42d22d91a8379d007dff1bcf` |

| 产物 | 字节 | sha256 |
| --- | --- | --- |
| `bin/Release/SteamP2PFriends.dll` | 1584640 | `a714d055657322bb21889518ac77bf188d1d4fa5311d25c46c31a29e7b71b141` |
| `WhitelistTests/bin/Release/SteamP2PFriends.dll` | 1584640 | 同上 |

九项与 Captain 表逐项一致（Control/Patch/Policy/Runtime/IlScan/Tests/Program/csproj/DomainModules 的 16 位前缀均匹配）。接线实点在 `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs:84`（`GunshotHostAudioControl.Initialize(_harmony)`，外层 try/catch → `ok=false`），`git status` 显示该文件在列表内。

---

## 2 独立复跑三命令

| 命令 | 退出 | 结果 |
| --- | --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 | 0 警告 0 错误，1.27s，DLL 复现 `a714d055…`（确定性构建） |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 0 | 0 错误 4 警告（CS0649×3 + 既有 CS0219 类噪音） |
| `./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | 1 | **822/824 PASS (Failed: 2)**；FAIL 仅 `GBA01`、`NIL01`（`TypeInitializationException SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`，控制台缺 Unity）；Bug2 段 23/23；`SPF_DLL_SHA256=a714d055…` |

副本 `../_t125mut`（tar 排除 `.git/.scratch/bin/obj/Release/publishi/Assets/.agent-teams`，`cp -a Libs` 到同级）独立重建同样得到 `a714d055` 与 822/824，确认基线可复现。

---

## 3 变异（副本内，逐字节还原）

方法：`D:/tmp/mut125.py`（字节级替换 + `MATCH_COUNT` 断言 + `.t125bak` 备份/还原）+ `D:/tmp/drive125.sh`（apply → 重建生产 → `cp` 到 `WhitelistTests/bin/Release` → 跑 exe → 抓 `Final Result` 与 Bug2 FAIL → restore）。每条**独立应用、独立还原**，还原后重建均回 `a714d055`、源回 `4bc62d3a`/`a74633f3`。

| # | 改动（送审字节行号） | 变异 DLL | 结果 | 新增红 |
| --- | --- | --- | --- | --- |
| A | `Runtime.cs:185` `NativeLeafNotEntered = !frame.NativeLeafEntered` → `= true` | `0e35a5be9d1f4015…` | 821/824 | **Bug2-18**（`native leaf already entered must not compensate, calls=1`） |
| B | `Runtime.cs:502` 反射名 `playGunshot` → `playGunshotX` | `101e6656dd457b75…` | 821/824 | **Bug2-19**（`ResolveDefaultLeaf must resolve UseableGun.playGunshot`） |
| C | `Runtime.cs:190` 观察器缺失分支 `NativeLeafNotEntered = false` → `= true` | `cac2762f22483f6d…` | **822/824** | 无 —— **全绿** |
| D | `Runtime.cs:337` `facts.ServerActive = Provider.isServer` → `= false`（只影响 `Collect`） | `68a1b02ffc59eca5…` | **822/824** | 无 —— **全绿** |
| E | `Patch.cs:152` `Uninstall` 内 `_attempted = false` → `= true` | `3bd3ee2e0f62a8b3…` | 821/824 | **Bug2-20**（`re-enable must install again`） |
| F | `Runtime.cs:210` `if (leaf == null)` → `if (leaf == null && Environment.TickCount == int.MinValue)` | `7a42486ecac9f006…` | **822/824** | 无 —— **全绿** |

A/B 与 Captain 对照：计数与红名单一致；DLL 哈希 A=`0e35a5be`（与 t124 报告一致）、B=`101e6656`（与 Captain 完全一致）。C/D/F 三条全绿是本轮的判决证据：

- **C 全绿**：观察器缺失时的 fail-closed 分支（`Runtime.cs:187-195`）没有任何用例；把它改成 fail-open（仍允许补偿）整套 824 条一条不红。
- **D 全绿**：生产事实采集 `Collect`（`Runtime.cs:328-359`）没有任何用例；把 `ServerActive` 恒置 false 也不红 → 生产门控输入整体只由 `FactsProviderForTest` 缝覆盖。
- **F 全绿**：`leaf == null → FailClosed` 分支（`Runtime.cs:210-216`）没有用例，删掉守卫也不红。
- **E 红**：`Uninstall` 清闩锁是真承重的（Bug2-20 钉住），这条 t124 的修复成立。

诚实记录：首轮驱动脚本漏了「修改前备份」，导致 A–E 累计叠加、结果串味（E 后 819/824 含 18/19/20）。已修复脚本并为每条重新建立干净基线重跑，上表为隔离后的结果；首轮数据不作为证据。

---

## 4 四个重点判定

### 4.1 观察器是否真能被「原生已播过」触发（Q1）

写入路径正确且是实测来源：`Fire_Prefix` → `OnFireEnter` 压帧（`Runtime.cs:127-138`）；`playGunshot` 的 `NativeLeaf_Postfix`（`Patch.cs:285-289`）→ `OnNativeLeafObserved`（`Runtime.cs:96-125`）在 `_supplementalLeafDepth == 0` 且 `ReferenceEquals(frame.Gun, gun)` 时置 `frame.NativeLeafEntered = true`；补偿路径在 `Runtime.cs:181-186` 读取 `!frame.NativeLeafEntered`，无硬置。自己补的那一次用 `_supplementalLeafDepth` 排除（`Runtime.cs:243-251`），不会被误记成原生。

**但真实可达性要打问号**：补偿门的 `ShooterNotLocal` 要求射手不是本机玩家，而 SDK 里 `playGunshot` 只从 `shoot()` 进入、`shoot()` 只在 `channel.IsLocalPlayer` 时调用（`UseableGun.cs:780-785/880-941`）。也就是说：**在本功能唯一允许补偿的路径上，原生叶原则上不会先响**，观察器只在「别的代码/Mod 先调了 playGunshot」时才有意义。它不是错误，但它守护的场景在原生流程下不可达，因此「真机时序下观察器命中」仍是 UNKNOWN（t124 报告 §5 已把真机 Harmony 命中列 UNKNOWN，但没点出这一层）。

### 4.2 观察器缺失的 fail-closed 分支（Q2）

代码语义正确：`Runtime.cs:187-195` 在 `_nativeObserverInstalled == false` 时把 `NativeLeafNotEntered` 置 false 并写 `CollectionError = "native-leaf observer not installed"`，随后 `IsEligible` 必然 false → 不补声。

**问题一：零用例**（变异 C 全绿）。
**问题二：该分支当前不可达**。`TryCompensateAfterSend` 首行 `if (!Enabled || FailClosed) return;`（`Runtime.cs:146`）；注册失败时 `Fail()` 会 `SetFailClosed`（`Patch.cs:353-359`），成功时 `SetNativeObserverInstalled(true)`（`Patch.cs:124`）。`Uninstall()` 虽置 false，但调用它的 `Apply(false)` 先 `SetEnabled(false)`。因此 `Enabled && !FailClosed && !observer` 这个组合在生产里走不到 —— 它现在是**死代码**，且没有用例。
结论：不是当前缺陷，但是「看起来有保护、实际没有测试也没有路径」的假安全；要么补一条直接构造（`SetEnabled(true)` + `SetNativeObserverInstalled(false)`）的用例并断言不补声，要么删除该分支并在注释里写明由 FailClosed 兜底。

### 4.3 热开启是否「看起来能开」（Q3）

控制流确实修好了：`Control.Initialize` 保存 `_harmony`（`Control.cs:42`）；`OnSettingChanged` 用 `_harmony` 调 `Apply`（`:90-105`）；`Apply` 返回 `bool` 并在失败时打 `ENTRY_FAILED`（`:107-134`）；`RegisterManual` 在 `harmony == null` 时直接 Fail 且**不落 `_attempted` 闩锁**（`Patch.cs:46-51`）；成功才置 `_attempted`（`:123`）；`Uninstall` 清闩锁（`:152`）；`Apply` 在 enable 前 `ClearFailClosed`（`Control.cs:115`）。变异 E（不清闩锁）→ Bug2-20 红，说明「关→开→关」用例是真承重的。

三点保留：

1. `Bug2-20` 用 `InstallerForTest` 缝（`GunshotHostAudioTests.cs:400/418`），**真实的 `harmony.Patch` / `Unpatch` / 再 `Patch` 从未被执行**；「不会重复 patch 叠加」在离线只有控制流证据。
2. `Patch.cs:116-121` 的 transpiler 插入数校验失败时只 `Unpatch(fire)`，**没有撤销同一 try 里已经挂上的 `playGunshot` postfix**；异常路径（`:130-134` `catch`）更是两个 patch 都不撤。此时 `FailClosed=true` 会挡住补偿，但「fail-closed 零 patch」的说法不成立，且重试 `RegisterManual` 会把 postfix 再挂一次。
3. 注册失败的传播止于日志：`Control.Initialize` 是 `void`，`DiagnosticsModules.cs:82-90` 只在抛异常时置 `ok=false`；`Apply` 返回 false 时注册计划仍视为成功（只有 `RoleLogger.Error`）。R23-03 的「向上传播」只完成到 `Control` 边界。

### 4.4 Bug2-23 是否真实 IL（Q4）

是真实 IL，不是合成指令：用例 `GunshotHostAudioTests.cs:471-524` 用 `GunshotIlScan.Read(AccessTools.Method(typeof(UseableGun), "fire"))` 取真实方法体，把全部指令转成 `CodeInstruction`，先校验 `TryLocateAnchor` 命中、再对原始指令逐分支断言目标落在指令边界，最后调用**生产** `Fire_Transpiler`（`:516`），断言 `TranspilerInsertions == 2`、`patched.Count == codes.Count + 2`、`patched[anchor+1]` 是 `Ldarg_0`、`patched[anchor+2].operand` 是 `TryCompensateAfterSend`。这一条判为**通过**。

两点限定（不足以推翻结论）：分支边界检查是在**插入前**的原始 IL 上做的，没有断言「没有分支目标落在 `anchor+1`」；插入语义依赖 Harmony/MonoMod 的 label 跟随指令对象，因此真机重写能否编译/命中仍是 UNKNOWN（报告 §5 已列）。另外 `Fire_Transpiler` 传入的是 `null` ILGenerator，真实运行时会拿到非 null，这一点没用例覆盖（不影响当前实现，因为实现未使用 generator）。

---

## 5 其余核查

- **F1 三项事实来源**：`Runtime.cs:181-186` 三行均来自 `frame.AnchorReached` / `frame.AssociationChecked` / `!frame.NativeLeafEntered`，无常量硬置（与 t124 声明一致）。
- **R23-02 生产 leaf**：`ResolveDefaultLeaf` 为 `internal static`（`:493`），`Bug2-19` 直接调用并断言 `leaf.Method == UseableGun.playGunshot`；变异 B 唯一红 19 → 成功路径真承重。失败路径（leaf==null）无用例（见 §3 F）。
- **RNG**：保存/恢复在 `finally`（`Runtime.cs:265-278`），捕获失败单独走 `SetFailClosed("rng state unavailable: …")`；`LogGate` 自带 try/catch（`:522-537`）不会在 `LeafCallCount++` 之后抛出，故不会漏恢复。`SetFailClosed` 失败路径都不重试（`Ledger.Fail`），符合「不双声」。
- **账本**：`TryClaim` 只从 `Unclaimed` 迁出，`Transition` 只允许 `ClaimedBeforeCall → Completed/Failed`（`Policy.cs:120-141/212-222`）；字典上限 1024 有界（`Test_LedgerPurgeByGeneration` 覆盖）。**但 `PurgeExcept`/`PurgeWeapon` 只删字典、不清理 `_insertionOrder` 队列**（`Policy.cs:147-188`），队列随发数单调增长；字典有界、进程内泄漏很小但无界。
- **默认关闭**：`RegisterManual` 在 `!Enabled` 时直接返回 true 且不 patch（`Patch.cs:41-44`）；`Initialize` 在默认 false 时走 `Apply(false)` → `SetEnabled(false)` + `Uninstall()`（无 patch 可撤）。`Bug2-07/17` 已覆盖关闭态。`ResetForTest` 同时复位 `_attempted`/`Installed`/observer/`FailClosed`，与 `Uninstall` 配对。
- **越界**：五个生产文件 grep `ReceivePlayShoot|ClientMethodHandle|SendAndLoopback|InvokeAndLoopback|new ClientInstanceMethod` 均 0；`shoot()/project()/playGunshot()` 只出现在注释；`csproj` 仅 5 条 `<Compile>` 追加；`Release/`、`publishi/` 无 12:00 后写入。
- **交付**：五个生产文件与测试文件均为 `A `（已 git add、未 commit），`Program.cs`/`csproj`/`DiagnosticsModules.cs` 为 `M `；工作区另有与本票无关的 `M .scratch/reported-bugs-2026-09-17/issues/03-gunshot-sfx-not-heard-by-host-and-guest.md`（未暂存），不属 t124 变更清单。
- **日志隐私**：gate 行只含 15 布尔 + epoch/connGen/weaponGen/netId/ordinal/map/decision，无 SteamID；`ENTRY_READY` 只打锚点摘要 `idx=/off=`（R23-07 已修）。

---

## 6 Findings

| id | severity | 位置 | 问题 | requiredFix |
| --- | --- | --- | --- | --- |
| R24-01 | medium | `GunshotHostAudioRuntime.cs:187-195` | 观察器缺失的 fail-closed 分支零用例（变异 C 全绿），且当前被 `FailClosed` 短路成不可达死代码；「看起来有保护、实际既不测也不走」 | 补一条直接构造 `Enabled=true` + `SetNativeObserverInstalled(false)` 的用例，断言 `leafCalls==0` 且 gate 含 `collectError=native-leaf observer not installed`；否则删除该分支并在注释说明由 FailClosed 兜底 |
| R24-02 | medium | `GunshotHostAudioRuntime.cs:210-216` | leaf 解析失败的 fail-closed 分支零用例（变异 F 全绿）；SDK 漂移时这条路径会静默退化 | 增加用例：注入 `ResolveDefaultLeaf` 返回 null 的路径（可用 seam 或改成可注入 resolver），断言 `FailClosed`、`LeafUnresolved`、且不调用 leaf |
| R24-03 | medium | `GunshotHostAudioPatch.cs:116-121`、`:130-134` | transpiler 插入数不符或安装异常时只撤 `fire` 的 patch，`playGunshot` postfix 留在运行期；「fail-closed 零 patch」不成立，重试会重复挂 postfix | 失败/异常路径统一 `Uninstall()`（或 `Unpatch(fire)` + `Unpatch(playGunshot)`）再 `Fail`；补一条用真实 Harmony 安装后强制失败的用例 |
| R24-04 | medium | `GunshotHostAudioRuntime.cs:328-359` | `Collect` 全零覆盖（变异 D 全绿）：生产门控输入仅由 `FactsProviderForTest` 缝验证，F1「生产路径实测」未证 | 抽 `BuildFactsFromState(...)` 纯函数（吃普通值 + 可注入的 AudioSource 探测），对 15 项各一条断言；或至少在 `Bug2-03` 系列补一条走 `Collect` 的用例 |
| R24-05 | low | `GunshotHostAudioControl.cs:54-58` → `DiagnosticsModules.cs:82-90` | 注册失败只到 `RoleLogger.Error`；`Initialize` 返回 void，注册计划 `ok` 仍为 true | `Initialize` 改返回 bool 或暴露 `LastFailure`，由 `DiagnosticsModules` 置 `ok=false`（与 Bug3 注册链一致） |
| R24-06 | low | `GunshotHostAudioTests.cs:395-424` | `Bug2-20` 用 `InstallerForTest` 缝，真实 `harmony.Patch`/`Unpatch`/再 `Patch` 从未执行；叠加风险只有控制流证据 | 报告 §5 增加该 UNKNOWN 条目，或补一个真 Harmony 的 idempotent 安装/卸载用例（不需要 Unity 运行时） |
| R24-07 | low | `GunshotHostAudioPolicy.cs:147-188` | `PurgeExcept`/`PurgeWeapon` 不裁剪 `_insertionOrder`，队列随发数无界增长（字典有界） | 清理时同步重建队列（`new Queue(_states.Keys)`），或在淘汰循环里跳过已不存在的键并周期性 trim |
| R24-08 | low | `GunshotHostAudioTests.cs:488-512` | 分支边界检查只针对插入前 IL，未断言「没有分支目标落在 `anchor+1`」；`Fire_Transpiler` 传 `null` ILGenerator，真实调用形态未覆盖 | 断言 `anchor+1` 不是任何分支目标（若有则改插在安全边界），并说明 label 跟随指令对象的前提 |
| R24-09 | low | 工作区 | `.scratch/reported-bugs-2026-09-17/issues/03-…md` 未暂存修改，不属 t124 变更清单 | 归属说明或在提交前还原/单独提交 |

---

## 7 结论边界与 UNKNOWN

- 本报告判定的是「t124 这批字节是否可进入实机待测」。**不声称 Bug2 已修复，不声称 Host 可听性通过。**
- R23-01（热开启）、R23-02（生产 leaf 解析）、R23-05（真实 fire IL 证据）三条 high 在「离线证据」意义上**真关闭**（Captain 两条变异与我的 A/B/E 一致，Bug2-23 是真实 IL）。
- 仍需实机/后续：真实 Harmony 重写后的命中（MonoMod 编译）、实机听感（S1）、三端互听（S2/S4）、Mod 兼容、帧级 RNG 对比、S3 原生副作用对照 —— 全部 UNKNOWN，沿用 `requirements-v2.md` 的登记。
- 观察器在真实时序中的命中（原生先响而仍需补偿）在原生 SDK 流程下不可达，见 §4.1；不得据此推断「双声保护已被实机验证」。

---

## 8 复现命令

```bash
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe

# 变异（仓库外副本 ../_t125mut，与主仓同级以便 ..\Libs 解析）
python D:/tmp/mut125.py A      # 或 B/C/D/E
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe
python D:/tmp/mut125.py restore
```
<!-- self_sha256(body)=5baf172213def8f32852fb2593dd12aacd1e1c25d79a84d3cea90117ccf500d7 -->
