# T127 / Bug2 repair-round-3 实现报告（R24-01..09 + T126-F1/F2）

任务：t127（attempt `aca368ca-14ad-4671-b8ac-f1bd26b4a125`）
范围：`Core/Patches/GunshotHostAudio/`、`WhitelistTests/`、`Core/Registration/`、`.scratch/takeover-v05-20261106/`
基线：822/824（仅 GBA01/NIL01 环境失败），DLL `a714d055…`；终态：829/831（仅 GBA01/NIL01），DLL `9c8ff2f9…`

## 1. 摘要

本轮把 t125 工程轴 4 medium / 5 low 与 t126 规格轴 F1/F2 合并成同一批字节修完：

- **R24-01**：删除「观察器缺失」死分支，改为 `facts.NativeLeafNotEntered = !frame.NativeLeafEntered`；观察器安装由 T126-F1 的 `Harmony.GetPatchInfo` 回查兜底。原生流程不可达的论证已登记到 `requirements-v2.md` UNKNOWN 区（补偿门要求 `ShooterNotLocal=true`，SDK 只在本地 channel 调 `playGunshot`；观察器是防 Mod/第三方重复播放的安全网）。
- **R24-02**：新增 `LeafResolverForTest` 注入缝；resolver 返回 null 时 `FailClosed + decision=LeafUnresolved` 且不调用 leaf（Bug2-25）。
- **R24-03**：失败/异常路径统一 `Uninstall()`（同时 `Unpatch(fire)` + `Unpatch(playGunshot)`）再 `Fail`；用真实 Harmony + FakeGun 目标强制 transpiler 失败，断言两个 patch 均不残留（Bug2-24）。
- **R24-04**：15 项门控抽成纯函数 `GunshotHostAudioRuntime.BuildFactsFromState(...)`；`Collect` 只读 SDK 原始值后调用它；逐项断言（Bug2-26）。
- **R24-05**：`GunshotHostAudioControl.Initialize` 改返回 `bool`；`PatchRegistrationDiagnosticsModules` 在返回 false 时置 `ok=false`；结构断言（Bug2-30）。
- **R24-06**：Bug2-20 改为真实 Harmony 安装/卸载/重装（目标换成 FakeGun 无害方法；真实 `fire/playGunshot` 的 IL 复制在测试宿主缺 UnityEx，无法真装），并用 `GetPatchInfo` 回查。
- **R24-07**：`PurgeExcept/PurgeWeapon` 清理后 `RebuildInsertionOrder()`；淘汰循环跳过已删除键；`InsertionQueueCount` 暴露给测试（Bug2-27）。
- **R24-08**：新增断言「anchor+1 不是任何分支/switch 目标」（Bug2-28）；真实 fire IL 插入点仍由 Bug2-23 覆盖。
- **R24-09**：`.scratch/reported-bugs-2026-09-17/issues/03-…md` 的既有未暂存修改未触碰（见 §7）。
- **T126-F1**：先 `harmony.Patch(playGunshot, postfix)`，再 `IsOurPostfixInstalled`（`Harmony.GetPatchInfo` 遍历 `Postfixes`，owner + PatchMethod 精确匹配）；查不到立即 `Uninstall + Fail`。删掉安装行时 Bug2-20 必红（见 §5）。
- **T126-F2**：`_supplementalLeafDepth` 守卫绑定用例 Bug2-29——补偿叶内部回调观察器时 `NativeLeafObservationCount==0`、`leafNotEntered=1`。

## 2. IL 锚点证据（真实 `UseableGun.fire`）

用 `GunshotIlScan.Read`（自带解码器，不依赖 Harmony/MonoMod）离线读取 `Assembly-CSharp` 的 `UseableGun.fire`：

- 共 **1496** 条 IL；唯一锚点 **index 82 / offset 0x010E**：
  `callvirt Void Invoke(SDG.Unturned.NetId, SDG.NetTransport.ENetReliability, List<ITransportConnection>)`
- 锚点前 index 71 / 0x00E7：`ldsfld ClientInstanceMethod SendPlayShoot`；72–81 为 `ldarg.0` / `GetNetId` / `ldc.i4.1` / `channel` / `transform.position` / `INSANE` / `GatherRemote...`。
- 锚点后：0x0113 `ldarg.0`、0x0114 `get_realtimeSinceStartup`、0x0119 `stfld lastShot`；耐久 `set_quality` 在 index 179/0x0265 与 185/0x0278、`sendUpdateQuality` 在 189/0x0288 —— 插入点位于 `Invoke` 返回后、barrel 耐久修改前。
- 扫描器把 `ShortInlineBrTarget/InlineBrTarget` 的 Operand 规范化为**绝对目标 offset**，switch 为绝对目标数组；Bug2-23/28 据此校验分支边界。

## 3. 改动清单（file:line）

| 文件 | 改动 |
|---|---|
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPolicy.cs` | `TryClaim` 淘汰跳过已删键；`InsertionQueueCount`；`PurgeExcept/PurgeWeapon` → `RebuildInsertionOrder()`；`RebuildInsertionOrder()`；`FormatGateLine` 去掉重复 `token=` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs` | `LeafResolverForTest`/`AudioSourceProbeForTest` 缝；删 observer-missing 死分支（R24-01 注释）；leaf 解析走 resolver；`Collect` → `BuildFactsFromState`；`BuildFactsFromState` 纯函数；`GetGunshotAudioSource` 支持探测缝；`ResetForTest` 清理新缝 |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPatch.cs` | 测试缝 `FireTargetForTest/PlayGunshotTargetForTest/SkipAnchorVerifyForTest/SkipFirePatchForTest` + `_fireMethod/_playGunshotMethod`；RegisterManual 重写（先装观察器 → `GetPatchInfo` 回查 → 再装 fire）；`IsOurPostfixInstalled`；失败/异常统一 `Uninstall()`；`Uninstall` 用记录的方法且清空；`ResetForTest` 同步 |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioControl.cs` | `Initialize` 返回 bool；启动失败记 `ENTRY_INIT_FAILED` 并返回 false；`Apply` 失败返回 false（热开启用 `_harmony`） |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs` | `Initialize` 返回 false → `ok=false` |
| `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs` | Bug2-20 改真实 Harmony 周期；新增 Bug2-24..30 与 `FakeGun`、`Build/CountOurPostfixes/CountPatches` 助手 |
| `WhitelistTests/Program.cs` | 注册 Bug2-24..30 |
| `.scratch/takeover-v05-20261106/requirements-v2.md` | UNKNOWN 区新增「原生 playGunshot 观察器不可实机验证」条目 |

## 4. 验收对应（A–H）

| 项 | 证据 |
|---|---|
| R24-01 死分支 | 分支已删；`NativeLeafNotEntered` 由 `frame.NativeLeafEntered` 决定；不可达论证见 §1 与 requirements UNKNOWN；删除后 Bug2-29（守卫）与 Bug2-20（安装回查）仍能红。 |
| R24-02 resolver | Bug2-25：resolver=null → `FailClosed`、`decision=LeafUnresolved`、leafCalls=0；变异 R24-02 红。 |
| R24-03 零 patch | Bug2-24：真实 Harmony 强制 transpiler 失败 → 返回 false、`Installed=false`、`GetPatchInfo(fakeLeaf)` 无我方 Postfix、`GetPatchInfo(fakeFire)` 无 patch；变异 R24-03 红。 |
| R24-04 纯函数 | Bug2-26：15 项逐项翻转断言 + 全 true 可 eligible；变异 R24-04 红。 |
| R24-05 返回值 | `Initialize` 返回 bool；DiagnosticsModules 置 ok=false；Bug2-30 断言返回类型。 |
| R24-06 真实 Harmony | Bug2-20：真实 `Harmony.Patch/Unpatch/GetPatchInfo`，开→关→再开，观察器 Postfix 计数 1→0→1。 |
| R24-07 队列 | Bug2-27：purge 后队列=0 且与字典同步；插入 MaxEntries+16 后 `Count<=MaxEntries` 且队列==字典；变异 R24-07 红。 |
| R24-08 分支目标 | Bug2-28：真实 fire IL 中 anchor+1 不被任何 branch/switch 指向。 |
| R24-09 无关 M 文件 | 未触碰 `.scratch/reported-bugs-2026-09-17/issues/03-…md`（git status 仍为既有的 ` M`）。 |
| T126-F1 | 安装后 `IsOurPostfixInstalled` 回查；删安装行 → Bug2-20 红（§5）。 |
| T126-F2 | Bug2-29：补偿叶内回调观察器 → count=0、`leafNotEntered=1`；变异 T126-F2 红。 |

## 5. 变异表（均自跑，逐字节还原）

| 变异 | 做法 | 变异 DLL sha256 | 新增红用例 | 还原 |
|---|---|---|---|---|
| R24-03 | catch 路径去掉 `Uninstall()` | `feafa6911fe48f0a6c8ac33276f9c5268463707c0f483023e6f25721f33e35f3` | Bug2-24「observer postfix must be removed」 | 源码 sha 回 `25d7d1dc…` |
| T126-F1 | 删 `harmony.Patch(playGunshot, postfix…)` | `3c9fa7284d485e96dd93cf74abd025d334cd54474aa718c4ddbc546ce1b97351` | Bug2-20「hot enable must install」 | 同上 |
| R24-02 | resolver 缝改为直接 `ResolveDefaultLeaf()` | `14e84655dedc2c52ec4184afaf7193fd60ecd4a2b2b16b88ed90c66490eb1e2c` | Bug2-25「unresolved leaf must fail closed」 | Runtime sha 回 `65bebc22…` |
| R24-04 | `NotDedicated = true` 写死 | `6b2bc8e836d42adc016b36421403edfab0bc5342527ec859e2b8bf0ab9a26523` | Bug2-26「notDedicated」 | 同上 |
| R24-07 | 清理时不 `RebuildInsertionOrder()` | `ccb9a9e5979dc3457c103a6a89eed0c373faa4c0997cda562bda09fedb8ff4b9` | Bug2-27「queue rebuilt empty」 | Policy sha 回 `9e0fa20e…` |
| T126-F2 | 守卫条件改为永不成立 | `af63e2a329056bac63e6e5fa3d5122c2e45759047721e1bdc6703ee1c8fd8182` | Bug2-29「supplemental leaf must not be observed as native, count=1」 | Runtime sha 回 `65bebc22…` |

还原后：生产 0 警 0 错、测试 0 错 4 既有警告、exe **829/831**（仅 GBA01/NIL01）、Bug2-01..30 全 PASS、DLL `9c8ff2f9…`。

## 5.1 变异 old→new 原文摘录（R25-05）

> 本节按 t127 当时源码文本复原替换点，便于第三方复现；t129 报告 `t129-bug2-r4-coverage.md` §3 给出 t129 三条变异的逐字节文本。
> 同一语义变异若替换文本不同（整行/片段、空白/分号），编译出的 DLL 哈希会不同——审查官复跑哈希差异即源于此。

| 变异 | old（原文） | new |
|---|---|---|
| R24-03 | `                Uninstall();`（catch 内，`Fail("register exception: ...")` 之前） | 删除该行 |
| T126-F1 | `                harmony.Patch(\n                    playGunshot,\n                    postfix: new HarmonyMethod(nativeLeafPostfix));` | 删除这三行（随后 `IsOurPostfixInstalled` 回查失败） |
| R24-02 | `                Action<UseableGun> leaf = LeafInvokerForTest ?? (LeafResolverForTest != null ? LeafResolverForTest() : ResolveDefaultLeaf());` | `                Action<UseableGun> leaf = LeafInvokerForTest ?? ResolveDefaultLeaf();` |
| R24-04 | `                NotDedicated = !dedicated,` | `                NotDedicated = true,` |
| R24-07 | `                RebuildInsertionOrder();`（`PurgeExcept`/`PurgeWeapon` 两处） | 删除这两行 |
| T126-F2 | `            if (_supplementalLeafDepth > 0)` | `            if (_supplementalLeafDepth > 1000000)` |

## 6. 三命令

```
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild                 # 0 warning / 0 error
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild  # 0 error / 4 pre-existing warnings
WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe             # 829/831 PASS（仅 GBA01/NIL01）
```

## 7. 未闭环 / UNKNOWN

- **真机听感**：S1（Guest→Host 10/10 单发）仍待 [真机 1H+1G]；本报告不声称 Bug2 已修复或可听性通过。
- **三端互听**：Guest A↔Guest B 仍 UNKNOWN（本轮拓扑不可验证）。
- **原生观察器**：见 §1 与 requirements-v2.md UNKNOWN；自动化只验证安装/回查/守卫，不代表真机命中。
- **Mod 自定义音效**：出现本地叶时停补并记录的行为未在真实 Mod 环境验证，UNKNOWN。
- **R24-09**：`issues/03-…md` 的既有未暂存修改非本票产物，未还原、未提交；归属原票/后续整理。
- `Fire_Transpiler` 的 `ILGenerator` 仍传 null（离线无法构造真实生成器）；分支目标有效性由 Bug2-23/28 的偏移校验覆盖，真实 Harmony 应用由宿主完成。

## 8. 文件 sha256（终态）

| 文件 | sha256 |
|---|---|
| GunshotHostAudioControl.cs | `478d03c7caf876e7f8508566312f0ea49be1b88ba4be3f647c862e5386c57ec4` |
| GunshotHostAudioPatch.cs | `25d7d1dcacd15f7847cce914dd4afbfcf2e9d989c21c30ee29cc20dc9a7f24e6` |
| GunshotHostAudioPolicy.cs | `9e0fa20e685f5314dc4683361e4f2d3db67145fc5f034bb3095341375a71ad11` |
| GunshotHostAudioRuntime.cs | `65bebc221e6c900b17adf08bb3bf9c3369483ccc03c579ed6269f9dca3ae4309` |
| GunshotIlScan.cs | `ef512ba0a8fea0540b248fdb1588e3c969b672df2e652edd4f1094c8f8dda1e9` |
| GunshotHostAudioTests.cs | `d3db92b6844bfb1942508a2de92b6fb489a9edea49a4a0813e6df587e5525c49` |
| Program.cs | `211b20eef84fad9cd562119dccfce89abbb2970470226815bc640dcd0a418373` |
| PatchRegistrationDiagnosticsModules.cs | `6ecda675d5a456f5ddf714a82bd870afb9e95bb43cd7149d0872fce8c8959b56` |
| bin/Release/SteamP2PFriends.dll | `9c8ff2f9b6fb688a7a80ad10cc28cd71f252488295d96c72cafd3f6cdab8c326` |

本轮未写 `Release/`、`publishi/`、`Develop-Stage/`、`publish/`；未改 `Platform/Diagnostics/`；未改 `Core/Patches/P2PGroupStateRecovery/`。
