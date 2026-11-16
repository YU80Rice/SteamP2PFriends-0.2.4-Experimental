# T128 / 工程轴复审 t127（Bug2 repair-round-3）

复审人：reviewer-eng（工程轴）。被审：t127（attempt `aca368ca-14ad-4671-b8ac-f1bd26b4a125`）。任务：t128（attempt `91e2c887-5d24-457b-81ea-3a84599a5aa4`）。

## 0. 结论

**needs_revision**（0 blocker / 0 high / 3 medium / 3 low）。

生产代码本身逐行读过，结论是**正确**的：R24-01 的死分支确实删除且换成实测字段、R24-03 的失败/异常路径确实两个 patch 一起撤、R24-07 的队列确实重建、T126-F1 的回查确实恢复、T126-F2 的守卫确实绑定。R23-01/R23-02 两条 high 我独立复跑后确认关闭（见 §3 MA/MB）。

打回的原因是**「半覆盖」**：三处本轮声称修好的东西，没有任何一条用例在实现回退时变红（M2/M3/M4 全绿，§3 实测）。这与我在 t125 打回 R24-04/R24-05 是同一类假绿，只是形态从「分支零覆盖」变成「纯函数有覆盖、适配层/传播线零覆盖」。

合同注记：t128 派工消息里的 Acceptance 文本是从 t125 复制的（仍写 822/824、DLL `a714d055`、Bug2-01..23）。本次按 t127 实际声称关闭的 R24-01..09 + T126-F1/F2 与它的终态字节判定，并把合同里仍适用的项逐条映射在 §4。

## 1. 被审字节（终态）

| 文件 | 字节 | 行 | sha256 |
|---|---|---|---|
| `Core/Patches/GunshotHostAudio/GunshotHostAudioControl.cs` | 5254 | 141 | `478d03c7caf876e7f8508566312f0ea49be1b88ba4be3f647c862e5386c57ec4` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPatch.cs` | 15754 | 409 | `25d7d1dcacd15f7847cce914dd4afbfcf2e9d989c21c30ee29cc20dc9a7f24e6` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPolicy.cs` | 12643 | 322 | `9e0fa20e685f5314dc4683361e4f2d3db67145fc5f034bb3095341375a71ad11` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs` | 23763 | 623 | `65bebc221e6c900b17adf08bb3bf9c3369483ccc03c579ed6269f9dca3ae4309` |
| `Core/Patches/GunshotHostAudio/GunshotIlScan.cs` | 9244 | 275 | `ef512ba0a8fea0540b248fdb1588e3c969b672df2e652edd4f1094c8f8dda1e9` |
| `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs` | 48211 | 984 | `d3db92b6844bfb1942508a2de92b6fb489a9edea49a4a0813e6df587e5525c49` |
| `WhitelistTests/Program.cs` | 159429 | 1151 | `211b20eef84fad9cd562119dccfce89abbb2970470226815bc640dcd0a418373` |
| `SteamP2PFriends.csproj` | 28133 | 416 | `e327865c031521fd…`（与 t124 相同，未改） |
| `Core/Registration/…PatchRegistrationDiagnosticsModules.cs` | 3921 | 110 | `6ecda675d5a456f5ddf714a82bd870afb9e95bb43cd7149d0872fce8c8959b56` |
| `bin/Release/SteamP2PFriends.dll` | 1586176 | — | `9c8ff2f9b6fb688a7a80ad10cc28cd71f252488295d96c72cafd3f6cdab8c326` |

九项与 t127 报告 §8 一致。**可复现性验证**：把 `git show :<path>` 的 staged 字节取到仓库外副本（`../_t128mut`）独立重建，得到完全相同的 `9c8ff2f9…` 与 829/831 —— 交付 DLL 确实由这批源码生成。

**工作区漂移（时间窗内）**：我开工时（16:38）取到的是上表字节；16:49 复核时 `GunshotHostAudioPatch.cs` 变成 `3bf3943b…`（catch 路径 `Uninstall();` 被替换成 `// mutated: Uninstall removed`）、`bin/Release` 的 DLL 变成 `922bd84f…`；17:10 复核又回到 `25d7d1dc…` / `9c8ff2f9…`。说明在我复审期间有另一执行体在同一工作区跑变异。见 R25-04。

## 2. 三命令复跑（隔离副本，staged 字节）

```
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild                              # 0 警告 / 0 错误，DLL 9c8ff2f9…（1.79s）
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild  # 0 错误 / 4 既有警告
WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe                           # 829/831 PASS（Failed: 2）
```

Failed 仅 `[PureMemory] GBA01 Native First Observe Binding`、`NIL01 Real Native Lookup Contract`（`TypeInitializationException → SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`，测试控制台缺 Unity 运行时）。`PASS [Runtime] Bug2` 30 行（Bug2-01..30 全绿），`FAIL [Runtime] Bug2` 0 行。`[T14] SPF_DLL_SHA256=9c8ff2f9…` 与磁盘一致。与 t127 自报的 829/831、DLL `9c8ff2f9…` 一致。

## 3. 变异表（全部自跑；副本 `../_t128mut`，逐字节还原）

基线重建先复现 `9c8ff2f9…`；每条变异后 `dotnet build -c Release`，把 DLL 复制进 `WhitelistTests/bin/Release` 再跑 exe；跑完从 `D:/tmp/t128p/` 的原始副本还原，最终五个源文件哈希与 §1 一致。

| # | 变异（file:line） | 变异 DLL | 计数 | 新增红 |
|---|---|---|---|---|
| MA | `Runtime.cs:195` `= !frame.NativeLeafEntered` → `= true` | `f64645c8e5c69db7` | 828/831 | **仅 Bug2-18** |
| MB | `Runtime.cs:564` `"playGunshot"` → `"playGunshotX"` | `72ee21943017d16f` | 828/831 | **仅 Bug2-19** |
| M1 | `Patch.cs:150` 删 catch 内 `Uninstall();`（R24-03） | `2ff8341d341b2f0c` | 828/831 | **仅 Bug2-24** |
| M2 | `Patch.cs:112` 回查守卫 `if (!IsOurPostfixInstalled(…))` → `… && false` | `9a72da60e995efd3` | 829/831 | **无（全绿）** |
| M3 | `PatchRegistrationDiagnosticsModules.cs:84` 传播线 `…Initialize(_harmony) && false` | `7e5947cfee9a9496` | 829/831 | **无（全绿）** |
| M4 | `Runtime.cs:349` Collect 适配 `levelLoaded: Level.isLoaded` → `Provider.isConnected` | `d8bb1d85dcc8c612` | 829/831 | **无（全绿）** |
| M5 | `Policy.cs:175` PurgeExcept 内删 `RebuildInsertionOrder()`（R24-07） | `a4c8c9c7cf7d7207` | 828/831 | **仅 Bug2-27** |
| M6 | `Runtime.cs:106` `_supplementalLeafDepth > 0` → `… && false`（T126-F2） | `5458e9b43b3bc8af` | 828/831 | **仅 Bug2-29** |
| M7 | `Patch.cs:109-111` 删 `harmony.Patch(playGunshot, postfix…)`（T126-F1 安装行） | `bb849744312ddca9` | 828/831 | **仅 Bug2-20** |

红名单与 t127 报告 §5 一致（它有 R24-03/F1/R24-02/R24-04/R24-07/F2 六条，我这边的 M1/M2/M5/M6/M7 与之对应）；**DLL 哈希逐条不同**（例如 R24-03 我 `2ff8341d` / 它 `feafa691`），属替换文本不完全一致，见 R25-05。

关键判断由 M2/M3/M4 给出：**把这三个修复点逐一"停用"，829/831 依然全绿** —— 即它们没有可失败的自动化证据。

## 4. 逐条核查

| 项 | 判定 | 依据 |
|---|---|---|
| R24-01 死分支 | **通过** | `Runtime.cs:188-195` 分支已删，三项分别取 `frame.AnchorReached/AssociationChecked/!NativeLeafEntered`；MA 只红 Bug2-18；不可达论证已进 `requirements-v2.md:67` UNKNOWN |
| R24-02 leaf 解析失败 | **通过** | `Runtime.cs:210-218` 三级选择 + null→`Fail`+`SetFailClosed`+`LeafUnresolved`；Bug2-25 直调生产路径断言 `leafCalls==0`、`FailClosed`、gate 行 |
| R24-03 失败零 patch | **通过** | `Patch.cs:132-138`（插入数不符）与 `:147-153`（异常）均先 `Uninstall()`；Bug2-24 用真实 Harmony + FakeGun 断言两个目标 `GetPatchInfo` 均无我方记录；M1 只红 Bug2-24 |
| R24-04 门控映射 | **半通过** | `BuildFactsFromState` 15 项被 Bug2-26 逐项翻转覆盖；但 `Collect` 只负责读 SDK 再喂纯函数，**没有任何用例经过它**（M4 全绿）→ R25-02 |
| R24-05 注册失败传播 | **半通过** | `Control.Initialize` 返回 bool + `DiagnosticsModules.cs:84-92` 置 `ok=false`，代码正确；Bug2-30 只断言 `Initialize` 的返回类型，传播线无用例（M3 全绿）→ R25-03 |
| R24-06 真实 Harmony | **通过** | Bug2-20/Bug2-24 走真实 `Harmony.Patch/Unpatch/GetPatchInfo`（目标替换为 FakeGun 无害方法，因测试宿主缺 UnityEx 无法真装 fire/playGunshot） |
| R24-07 账本有界/世代 | **通过** | `Policy.cs:136-145` 淘汰跳过已删键；`:156-207` 两个 Purge 后 `RebuildInsertionOrder()`；Bug2-27 断言队列==字典且 `Count<=1024`；M5 只红 Bug2-27 |
| R24-08 分支边界 | **通过** | Bug2-28 在真实 fire IL 上遍历 branch 与 switch，断言 `anchor+1` 的 offset 不是任何目标 |
| R24-09 无关 M 文件 | **通过** | `issues/03-…md` 仍为 ` M`，本轮未触碰（t127 已声明归属他票） |
| T126-F1 观察器回查 | **通过（带缺口）** | `Patch.cs:109-117` 先装 postfix 再 `IsOurPostfixInstalled`（owner+PatchMethod 精确匹配），失败即 `Uninstall+Fail`；M7 删安装行→Bug2-20 红，证明该顺序承重；但**停用回查本身全绿**（M2）→ R25-01 |
| T126-F2 补叶守卫 | **通过** | `Runtime.cs:106-109` `_supplementalLeafDepth>0` 直接 return；Bug2-29 让补偿叶内部回调观察器，断言 `count==0`、gate `leafNotEntered=1`；M6 只红 Bug2-29 |
| 合同：R23-01/R23-02 关闭 | **通过** | MA/MB 独立复现，各自只红 Bug2-18/Bug2-19，无连带 |
| 合同：三命令 822/824 → 实际 829/831 | **通过（口径订正）** | 合同数字是 t125 旧值；r3 终态是 829/831（+5 用例：Bug2-24..30 中的 5 条净增，2 条复用旧例） |
| 合同：字节锁 | **通过** | §1 九项一致，且隔离副本可重建同一 DLL |
| 合同：不声称已修复 | **通过** | 报告 §7 保留 S1/S2/S3/S4 与观察器 UNKNOWN；`requirements-v2.md:65/67` 已登记 |
| 合同：边界 | **通过** | `Release/`、`publishi/`、`Develop-Stage/` 无 16:00 后写入；`git status` 五个生产文件与测试均 `A `（已暂存未提交） |

## 5. Findings

**R25-01（medium，测试有效性）`Core/Patches/GunshotHostAudio/GunshotHostAudioPatch.cs:112`**
`IsOurPostfixInstalled` 是这个补丁唯一的"观察器真的挂上了"的证明，但没有用例在其被停用时变红（M2 全绿）。它与 R24-01 的"删除观察器缺失分支"叠加后有个理论缺口：若未来 Harmony 版本让 `Patch()` 静默失败，`SetNativeObserverInstalled(true)` 仍会被置位，`NativeLeafNotEntered` 恒 true，补偿与原生叶同时发声（双声），而自动化全绿。
requiredFix：加一条用例，用 seam 让 `harmony.Patch` 成功但 `GetPatchInfo` 查不到我方 postfix（或直接把 `IsOurPostfixInstalled` 提为可注入判据），断言 `RegisterManual==false` 且 `Installed==false`。

**R25-02（medium，测试有效性）`Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs:329-360`**
R24-04 只闭合了"纯函数半边"：`BuildFactsFromState` 有 15 项逐项断言，但 `Collect`（读 `gun.player`/`equippedGunAsset`/`HostManager`/`Provider` 再喂纯函数）零覆盖。M4 把 `levelLoaded` 换成 `Provider.isConnected` 全绿，说明 15 个实参的接线错误在自动化层不可见。
requiredFix：给 `Collect` 加一条"用替身 gun + seam 的 HostManager/Provider 值 → 断言 15 项 facts"的用例（或把 15 个读取抽成 `CollectStateForTest` 结构体，再由一条用例逐项断言）。

**R25-03（medium，测试有效性）`Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs:84-92`**
R24-05 的"向上传播"只验了 `Initialize` 的返回类型（Bug2-30 用反射断言 `ReturnType == typeof(bool)`），没有任何用例经过"返回 false → `ok=false`"这条线；M3 停用传播后 829/831 全绿。t124 的 R24-05 原意（注册失败不能被日志吞掉）在自动化层仍未被钉住。
requiredFix：把该 stage 抽成可注入 `Initialize` 结果的小函数（或在测试里直接调 `CreatePatchRegistrationPlan()` 的相关 stage），断言失败时返回 `false`。

**R25-04（low，交付/流程）工作区非静止**
复审期间（16:38→16:49）`GunshotHostAudioPatch.cs` 被改成 `3bf3943b…`（catch 路径带 `// mutated:` 注释）、`bin/Release` DLL 变成 `922bd84f…`，17:10 又恢复。任何在这段时间"从工作区取字节/打包"的人都会拿到变异体。
requiredFix：t127 收口后锁树（或明确声明"交付字节以 `git show :path` 为准"），复审/打包脚本一律从索引取字节。

**R25-05（low，证据可复现性）变异表缺改动文本**
我的七条变异红名单与 t127 报告完全一致，但 DLL 哈希逐条不同（MA/MB/M1/M5/M6/M7）。说明报告只给了"做法"的自然语言，没有给逐字替换文本，第三方无法复现同一哈希。
requiredFix：变异表附 `old → new` 原文（或补丁 diff），或直接给变异后的源文件哈希。

**R25-06（low，命名/断言强度）`GunshotHostAudioTests.cs:645-653`**
`Test_InitializeReturnsBool` 名字与断言都只到"返回类型"，没有覆盖"失败时返回 false"。与 R25-03 同源，建议改名并补断言。

## 6. 判定边界

- 本判定只针对 §1 的字节。生产代码**按读到的实现**是正确的；三条 medium 全部是"缺少能变红的用例"，不是"实现有错"。若团队把 R25-01/02/03 作为下一轮的最小修复，三条都是单文件、可离线完成的用例补充。
- **不声称 Bug2 已修复或可听性通过**。仍为 UNKNOWN：真机听感（S1）、Host→Guest 与 Guest↔Guest 互听（S4）、Mod 自定义音效叠加、`playGunshot` 观察器在真机的可触发性（`requirements-v2.md:67` 已登记）、真实 Harmony 对 `fire` 的 IL 重写（测试宿主缺 UnityEx，离线只验证插入点与分支边界）。
- `Fire_Transpiler` 的 `ILGenerator` 仍传 null（t127 §7 自述），真实应用由宿主完成——与 t124 一致，属已知 UNKNOWN 而非回归。

## 7. 复现命令

```bash
# 隔离副本（staged 字节）
tar -cf - --exclude=.git --exclude=.scratch --exclude=bin --exclude=obj \
  --exclude=Release --exclude=publishi --exclude=Assets --exclude=.agent-teams . \
  | tar -xf - -C ../_t128mut
for f in $(git diff --cached --name-only -- 'Core/Patches/GunshotHostAudio/*.cs' 'WhitelistTests/*'); do
  git show ":$f" > "../_t128mut/$f"; done
cd ../_t128mut
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe    # 829/831

# 变异（示例：M2）
python /d/tmp/mut128b.py M2 && dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild \
  && cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/ \
  && ./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe   # 829/831（应红而未红）
python /d/tmp/mut128b.py restore
```

<!-- self_sha256(body)=36f7ba2b6a9e707355e8d44a2bdbbfc17c1c62f9f4603fde81a2c6081c694a48 -->
