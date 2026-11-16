# T47 工程轴复审（r6）：Bug1 仪表开启入口 — 线程安全 / 反射健壮性 / 回滚

- 审查对象：t45「Bug1 仪表开启入口」（Reviewed task = t45）
- 审查官：reviewer-eng（工程轴；不评是否满足 Bug1 测量需求，只看正确性、边界、回归与可维护性）
- 日期：2026-11-07
- **verdict = needs_revision**（存在 blocker：开启入口在真实主机上必抛 NRE，探针无法被打开）

## 0. 被审字节锁定（本人实测，与送审前缀一致）

| 文件 | 字节 | sha256（前 24） |
| --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1ProbeControl.cs` | 8159 | `e0a1885f143bde72288bdf4c` |
| `Platform/Diagnostics/Patches/Bug1ProbeEntryLogic.cs` | 3125 | `5bf4f391d5b3d7660f6ede3f` |
| `Platform/Diagnostics/Patches/Bug1ProbeNativeSpeed.cs` | 4004 | `235943b8c78104edf62bd4eb` |
| `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs` | 42953 | `9b4397312f8e0af28ebf3a3c` |
| `Platform/Diagnostics/Patches/Bug1ProbeJudge.cs` | 22193 | `5d863441a4376cf2fb5b6ce0` |
| `Platform/Diagnostics/Patches/Bug1ProbeModel.cs` | 3684 | `eb40f806`（11-06 23:17，未动） |
| `Platform/Diagnostics/Patches/Bug1ProbePatch.cs` | 8872 | `12f8cbf0e1b9960bc6fc9512`（t42 字节，未变） |
| `Platform/Diagnostics/Patches/Bug1ProbeRxPhases.cs` | 16693 | `8d0d36fc94cb1d6b8ba98fb9`（未变） |
| `Core/Registration/...PatchRegistrationDiagnosticsModules.cs` | — | `ba5b7a73…` |
| `SteamP2PFriends.csproj` | — | `4b563108…` |
| `WhitelistTests/Program.cs` | — | `eecd2ac0…` |
| `bin/Release/SteamP2PFriends.dll` | 1462784 | `9625a13945ea08c56ae6bc07` |

上述文件在审查结束时重新 sha256 校验，**六个 probe 源文件与上表逐字节一致**（本人未在主树改任何源码；变异全部在仓库外副本 `../_t47mut` 完成）。

## 1. 独立复跑（本人实测，与 Builder/Captain 自报一致）

| 命令 | exit | 结果 |
| --- | --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 | 0 警告 0 错误；DLL 仍 `9625a139`（确定性构建） |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 0 | 0 错误 4 警告（既有 CS0649） |
| `./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | 1 | **718/720 PASS (Failed: 2)**，Bug1 段 **35/35** |

两条失败均为既有环境缺失：`GBA01 Native First Observe Binding`、`NIL01 Real Native Lookup Contract`（`TypeInitializationException → SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`，控制台无 Unity 运行时）。与 t45 自报的 718/720、Bug1 35/35、`9625a139` 完全一致，**无自述失真**。

## 2. 决定性实证：`playerID == null` 必抛 NRE（不是推断）

在 `WhitelistTests/bin/Release` 下用 csc 编译独立探针（`D:\tmp\probe_sdk_eq.cs`），以 `FormatterServices.GetUninitializedObject(typeof(SteamPlayerID))` 造非同名对象，直接执行三种比较，输出：

```
nonNullObject=True
THREW(nonnull==null): NullReferenceException :: 未将对象引用设置到对象的实例。
THREW(null!=null): NullReferenceException
THREW(null==null): NullReferenceException
```

根因：SDK 的 `SDG.Unturned.SteamPlayerID` 重载了 `==`/`!=`，实现直接解引用 `playerID_0.steamID`（`.scratch/bug2-sdk-longpaths-20261030/.../SteamPlayerID.cs:136-144`：`return playerID_0.steamID == playerID_1.steamID;`），**任一侧为 null 必抛，连 `null == null` 也不例外**。t30 在 Bug3 侧已因同一原因改用 `ReferenceEquals`，本票的三个新判空点没有跟上。

命中点（全仓库 grep `playerID == null` / `!= null`）：

| 位置 | 语义 |
| --- | --- |
| `Bug1ProbeControl.cs:138` | `ResolveTarget` 遍历 `Provider.clients` 时的 `client.playerID == null` |
| `Bug1ProbeControl.cs:162` | `FindPlayer` 的同类判空 |
| `Bug1ProbeRuntime.cs:178` | `TryBindTarget` 的同类判空 |

（另有 `UseableBarricadeDiagnosticPatch.cs:851`、`SteamPersonaDisplay.cs:271` 同类写法，属既有代码，不计入本票。）

## 3. 变异证据（全部在仓库外副本 `../_t47mut` 完成，主树未改）

副本构建可复现送审字节：`_t47mut` 全量重建 → `9625a139`，跑测 718/720，与主树一致。每条变异后把新 DLL 拷入副本测试目录运行，结束后从主树 `cp` 还原并校验源码/DLL 哈希。

| 变异 | 改动点 | 变异后 DLL（前 16） | 结果 | 新红用例 |
| --- | --- | --- | --- | --- |
| A：删出队侧过滤 | `Bug1ProbeRuntime.cs:366` `if (_serversidePackets == null \|\| !ReferenceEquals(queue, _serversidePackets)) return;`（仅 `OnDequeue`） | `9de135f7cf5ff1e1` | 716/720（Failed 4） | **Bug1-22、Bug1-25** |
| B：相位覆盖短路 | `Bug1ProbeJudge.cs:189` 首行插入 `if (w != null \|\| w == null) return true;` | `bb1dc27e5c5a4cce` | 716/720（Failed 4） | **Bug1-26、Bug1-27** |
| C：删 alreadyEnabled 守卫 | `Bug1ProbeEntryLogic.cs:48` 删 `if (alreadyEnabled) return ErrorAlreadyRunning;` | `0fef7b9936ceaccf` | 717/720（Failed 3） | **Bug1-30** |
| 还原 | 从主树 `cp` 三个文件 | `9625a13945ea08c5` | 718/720（Failed 2，仅既有环境失败） | — |

三条变异都**精确命中票面预期的红名单**（A→25/22、B→26/27、C→30），说明 t45 新增的用例不是自证：它们真的守卫了出队侧过滤、相位覆盖硬判、重复开启守卫。还原后源码 sha256 回到 `9b439731` / `5d863441` / `5bf4f391`，DLL 回到 `9625a139`，逐字节还原成立。

## 4. 逐条 acceptance 判定

| # | 判据 | 判定 | 依据 |
| --- | --- | --- | --- |
| 1 | 独立复跑 718/720、Bug1 35/35、DLL 9625a139 | **pass** | 第 1 节 |
| 2 | `SettingChanged` 线程安全 | **partial（medium）** | 本仓 `BepInEx.dll` 无 FileSystemWatcher/ConfigReloader（Cecil 核对：`ConfigFile.Reload` 仅被构造函数调用），当前热切换只来自 ConfigurationManager 的 IMGUI（主线程）；但 `OnEnabledChanged` 无 try/catch、无主线程断言或 marshalling，`Disable` 直接在回调线程里 `_traceLines.Clear()`/`_rx.Reset()`，任何非主线程 setter 都会与 hook 撕裂 → R8-05 |
| 3 | 反射健壮性 fail-closed | **pass** | `Bug1ProbeNativeSpeed.TryResolve`（:38-70）字段缺失/类型不符 → `SPEED_SPRINT_member_not_found` 返回 false；整个解析包在 try/catch，异常转 `resolve_failed:<Type>`，**不抛到插件加载链**；`TryStart` 见 false 即 Refuse。`totalSpeedMultiplier` 在 toggle 时读取，届时玩家已初始化 |
| 4 | 「不会部分开启」/ 回滚 | **fail（high）** | `Enable` 在 `Install()` 失败或抛异常时仍 `return true`（`Bug1ProbeRuntime.cs:122-124`），`TryStart` 照打 `START_OK`；`Enabled=true`、相机已订阅、7 个 hook 一个没装，`FailClosed` 只写不读、无回滚 → R8-02。Config 先写、`Enabled` 后置的次序本身正确（:97-114），不存在「Enabled=true 但窗口未就绪」 |
| 5 | 三条变异 + 逐字节还原 | **pass** | 第 3 节 |
| 6 | 新用例是否自证 | **partial** | Bug1-26..29 调生产 `Bug1ProbeJudge.Judge`、Bug1-30 调生产 `Bug1ProbeEntryLogic.Validate`，**不是复制逻辑**；但入口胶水 `TryStart/ResolveTarget/FindPlayer` 与 `TryResolve` 反射段零用例 —— R8-01 正是从这个盲区漏出去的 → R8-06 |
| 7 | 副作用 / 性能 / 关闭后卸载 | **fail（medium）** | trace 目录创建失败 → Refuse（安全）；但 `Disable` 只清状态与相机订阅，**不 Unpatch**，`Bug1ProbePatch` 根本没有 Uninstall → 7 个 detour 常驻（含 BCL `Queue.Enqueue/Dequeue`）→ R8-04 |
| 8 | 扩张面 | **pass（附注）** | csproj `+13/-0`、Program.cs `+89/-0` 纯追加；Bug3 五文件哈希仍 `7a82de8e/a89f40b9/3d5044f7/a4553385/5b0c4e9b`；probe 默认关闭（`Bind("Bug1Probe","Enable", false)`）；`Release/`、`publishi/` 未被本票改写；`M Build/Version.props` 属发布票（mtime 20:01 早于交付窗口） |
| 9 | 能否作为「带 Bug1 仪表的实机采集版」交付 | **fail（blocker）** | R8-01：真实主机上开启入口必抛 NRE；R8-02：即使不抛，Install 失败也会报 START_OK 而零采集；R8-07：现存发布包全部不是 `9625a139` |
| 10 | 审查只读 | **pass** | 主树六个 probe 源 sha256 与送审一致；变异只在 `../_t47mut` |

## 5. Findings

**R8-01（blocker）开启入口在真实主机上必抛 NRE，探针无法被打开**
`Bug1ProbeControl.cs:138`（`ResolveTarget`）、`:162`（`FindPlayer`）、`Bug1ProbeRuntime.cs:178`（`TryBindTarget`）用 `playerID == null` / `!= null` 判空，而 SDK 的 `SteamPlayerID` 运算符在任一侧为 null 时必抛 NRE（第 2 节实证）。正常主机 `Provider.clients` 至少含本机连接，`TryStart` 必经 `:138`，异常在 `Enable` 之前抛出，`START_OK` 永远不可达。
requiredFix：三处一律改 `ReferenceEquals(x.playerID, null)`（与 Bug3 t30 的修法一致）；并把 `ResolveTarget` 的 null/选择器逻辑拆成吃 `IEnumerable<(ulong id, string name)>` 的纯函数，补一条离线用例（不需要 Unity）。

**R8-02（high）Enable 在 hook 安装失败时仍返回 true，产生「开着但零采集」**
`Bug1ProbeRuntime.cs:122-124`：`Enabled = true`（:114）与 `BoundCamera(true)`（:121）都已完成，`try { Bug1ProbePatch.Install(); } catch { LastError = ... }` 之后无条件 `return true`；`Install()` 自身返回 false（ABI 未解析 / identity verify 失败）也被忽略。`TryStart` 据此打印 `START_OK` 并返回 true，用户/操作者看到成功，实际 7 个 hook 一个都没装；`FailClosed` 无消费者，且 `Enabled` 留在 true，再次开启会被 `Validate` 判 `ALREADY_RUNNING` 卡死。
requiredFix：`bool ok = Bug1ProbePatch.Install();`（放进 try，catch 时 ok=false）；`if (!ok) { Disable("install_failed"); LastError = ...; return false; }` —— 先回滚相机订阅与 Enabled，再返回 false，让 `TryStart` 走 Refuse 分支。

**R8-03（high）启动期开启永远不生效，且没有重试点**
`Bug1ProbeControl.Initialize()` 由注册计划 stage 6 在插件加载期调用（`PatchRegistrationDiagnosticsModules.cs:73`，`PatchRegistrationOrchestration.cs:46`），此时 `Provider.isServer == false`，`TryStart("startup")` 直接 Refuse `ROLE_NOT_HOST`；`TryStart` 的唯一调用点是 Initialize 的 startup 分支与 `OnEnabledChanged`，**没有任何 level-loaded / player-connected / host-started 重试**。运营者按最自然的方式「改 cfg 的 Enable=true 再启动游戏」→ 配置写着开、日志一行 REFUSED、永不采集。
requiredFix：把启动期请求挂起为 pending，在 `Level.onLevelLoaded`（或 P2P host 就绪）后重试一次；或在 startup 分支直接拒绝并**把配置项回写为 false**，让「开没开」与配置文件一致。

**R8-04（medium）关闭后不卸载 hook，detour 常驻热路径**
`Bug1ProbeRuntime.Disable`（:138-165）只清状态、`BoundCamera(false)`；`Bug1ProbePatch` 全文件无 Unpatch/Uninstall，7 个 Harmony patch 在 `Disable`、`OnSessionReset`（:857-862 → `Disable("session_reset")`）之后仍然挂在 `PlayerMovement.simulate/Update`、`PlayerInput.ReceiveInputs` 与 BCL `Queue<PlayerInputPacket>.Enqueue/Dequeue` 上。每次调用付一次委托跳转 + `Enabled` 读，与「默认关闭零副作用」「关闭后彻底卸载」的声明不符——这正是票面点名要确认的一项。
requiredFix：`Bug1ProbePatch` 增加 `Uninstall()`：`_harmony.Unpatch(SimulateTarget, simBefore)` 等 7 条（或 `UnpatchSelf`），并在 `Disable` 末尾调用、把 `Registered`/`FailClosed` 复位。

**R8-05（medium）配置热切换无线程亲和声明**
`OnEnabledChanged`（`Bug1ProbeControl.cs:53-66`）没有 try/catch，`TryStart`/`Disable` 直接在该回调线程执行 `Provider.clients` 遍历、`Directory.CreateDirectory`、`Harmony.Patch`、`_traceLines.Clear()`、`_rx.Reset()`。本仓 BepInEx 版本没有文件监视器（Cecil 核对），当前唯一现实路径是 ConfigurationManager 的主线程 IMGUI，所以**不是 blocker**；但 API 未作任何约束，任何插件/控制台在主线程外写这个 `ConfigEntry` 都会与 hook 并发撕裂 trace/ledger。
requiredFix：入口处 `if (!MainThreadGuard.IsMainThread()) { 记录并投递到主线程队列; return; }`，或至少加 try/catch 并在日志写明线程约束。

**R8-06（medium）入口胶水零覆盖，NRE 因此未被任何用例拦截**
35 条 Bug1 用例里，`TryStart`/`ResolveTarget`/`FindPlayer`/`TryResolve`（反射段）没有任何调用；能测到的只有 `Judge`/`Validate`/`Compute` 这些纯逻辑。R8-01 就是从这条缝里漏出去的（与上一轮 R6-01「用例不穿透接线」同型，只是这次的缝在 Unity 侧）。
requiredFix：同 R8-01 的纯函数抽取 + 一条离线用例（列表含 null playerID 的条目，断言不抛且返回 TARGET_MISSING）。

**R8-07（medium，交付层）现存发布包都不是送审字节**
`Release/0.2.4.11-preview2/SteamP2PFriends.dll` = `d9254a24`（1457152B，sha256.txt/manifest/zip 四者自洽但都陈旧），`preview1` 更旧；没有任何包是 `9625a139`。用 preview2 上机，Bug1 开启入口代码根本不在二进制里，配置项不会产生任何效果，也不会报错——这是交付层的「开着但采不到」。
requiredFix：从 `9625a139` 重新打包（建议 preview3），同步 dll/sha256.txt/manifest/zip，并在测试说明写明 `SPF_DLL_SHA256=9625a139…` 为上机前置核对项。

**R8-08（low）开启失败后配置项与运行态不一致**
`_enabledEntry` 已是 true；`TryStart` 任意 Refuse 分支只写日志，不回写配置、不在 `ENTRY_READY` 之后暴露状态。建议 Refuse 时把 entry 回写 false（或把拒绝原因写进 `Bug1ProbeRuntime.LastError` 并让 RUN_END/日志首行可见）。

**R8-09（low）`TryStart` 每次都会 `Directory.CreateDirectory` 与全量 `Provider.clients` 遍历**
只在 toggle 时发生（非每帧），成本可忽略；但 `FindPlayer` 在 `Validate` 之前执行，语义上目标未就绪就抛/拒，建议把「参数校验」前置到「玩家解析」之前，让拒绝原因更准确（当前 target 缺失会先撞 NRE）。

## 6. 通过项（明确记录，避免下一轮重复审查）

- 反射失败一律 fail-closed：字段缺失/改名/类型变 → 拒绝开启；解析异常被 `TryResolve` 兜住，不会打断插件加载（对照票面第 2 问：合格）。
- `Config` 赋值、`_window` 构造、`_traceLines`/`_rx` 复位都在 `Enabled = true` **之前**（`Bug1ProbeRuntime.cs:97-114`），不存在「Enabled=true 但窗口未就绪」。
- `Install()` 内部先解析 5 目标、再装 7 hook、再逐个 `Verify(owner+original+PatchMethod)`，失败置 `FailClosed` 并 Error —— 解析与身份校验本身是 fail-closed 的（问题只在 `Enable` 不消费它的返回值，见 R8-02）。
- 相位覆盖硬判有效：`Judge` 先 `ValidatePhaseCoverage` 再 `ValidateContext/Frames`，缺相位直接 `Unknown(PHASE_COVERAGE_MISSING)`，不会落成 `NO_THRESHOLD_EVENT`；三条变异证明它可失败。
- 相机订阅是幂等的（`-=` 后 `+=`），关闭时确实解除；`Camera.onPreCull` 只在开启时挂。
- probe 默认关闭（`Bind(..., false)`），未开启时不装任何 hook（`RegisterManual` 先判 `Enabled`）。
- 扩张面干净：csproj/Program.cs 纯追加；Bug3 五文件字节未变；`Release/`、`publishi/` 未在本票被改写。

## 7. UNKNOWN（自动化无法闭合，须实机或环境确认）

1. 真实游戏里插件加载顺序：`Provider.isServer` 在 `Initialize()` 时是否为 false（本审查按源码与 BepInEx 加载时序推断，未在实机验证）——若实机加载在 host 建立之后，R8-03 的「启动期必拒绝」不成立，但 R8-01 仍然阻断运行中开启。
2. 部署环境是否带 ConfigurationManager（决定热切换是否可达）。
3. 是否存在第三方插件以非主线程写入本 `ConfigEntry`（决定 R8-05 是否会真的撕裂）。
4. 1H+1G 实机上，修复 R8-01/R8-02 后相位行是否稳定产出（本票只判入口工程性，不构成 Bug1 根因结论）。

## 8. 结论与最小修复顺序

`needs_revision`。按影响排序：**R8-01（改 3 处判空 + 纯函数抽取与用例）→ R8-02（Install 失败即回滚并返回 false）→ R8-03（启动期 pending 重试或回写配置）→ R8-04（Uninstall）→ R8-07（重打包）**。R8-05/R8-06/R8-08/R8-09 可同轮带上，不单独阻塞。

## 9. 复现命令

```bash
# 基线
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe    # 718/720，Bug1 35/35

# NRE 实证（在 WhitelistTests/bin/Release 下）
csc -out:D:\tmp\probe_sdk_eq.exe -r:Assembly-CSharp.dll D:\tmp\probe_sdk_eq.cs && ./probe_sdk_eq.exe

# 变异（仓库外副本）
tar -C SteamP2PFriends-0.2.4-Experimental --exclude=./.scratch --exclude=./.git --exclude=./bin \
    --exclude=./obj --exclude=./Release --exclude=./publishi -cf - . | tar -C ../_t47mut -xf -
python D:/tmp/mut47.py MA|MB|MC|restore
```

self_sha256=12b6cf9ecf33453decf7a4bdfd0934669ba2efd7db2fe97a5b1dde82f5050649
