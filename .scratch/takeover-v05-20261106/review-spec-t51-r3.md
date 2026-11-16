# T52 规格轴复审 r3 — t51 Bug1 仪表入口修复轮（T49-F1 + R9-01/R9-02）

- 票：t52（被审 t51，attempt 1；审查官 attempt `14c25681-292e-48dc-b0eb-b23e3e172346`）
- 判定口径：只判「是否按需求/规格真关闭、且没有引入新的假绿」，不评实现风格；只读，未改主树任何字节。
- **verdict：needs_revision**。三条修复的**行为**都真修了，且各自的接缝可失败；但两条新增的**生产接线**（`TryStartCore → EnablePreparedRun`、`outcome.Unhook → UnhookStartupRetry`）删掉后全量仍 732/734 全绿，属本队反复禁止的「假绿」类别。见 T52-F1 / T52-F2。
- 本判定**不构成**「Bug1 已诊断」或「仪表可发布」；三项实机 UNKNOWN 与 T49-F2/T49-F3 仍开。

## 1. 被审字节绑定（本机实测，2026-11-07 05:56 取样）

| 文件 | 字节 | sha256 | 与 t51 自报 |
| --- | --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1ProbeControl.cs`（431 行） | 22068 | `5f6738cfa02e32dd0378cbcc3ea59259ef0f95415ae1ebfc24791da018635400` | 一致 |
| `Platform/Diagnostics/Patches/Bug1ProbeEntryLogic.cs`（130 行） | 6565 | `04a4beeb058849b10788194eac19fd073268ad22375f44020d24183f5dad62fc` | 一致 |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeEntryRepairTests.cs`（566 行） | 34788 | `f6de7455ca93dae127018d24da453198750b305b52f9b5e0b069a903cf5d06aa` | 一致 |
| `WhitelistTests/Program.cs` | 140251 | `eb1287f8a42d20acf82564cffdeefe1f17a3b8f6034db0e82c8e180a25d7d2b0` | 一致 |
| `SteamP2PFriends.csproj` | 27492 | `8b27ab5587b2656e0153cff2d2577c79d8d20bc2d5a5d544aadc572c732dd89e` | 与 t48 相同（本轮未改） |
| `bin/Release/SteamP2PFriends.dll` | 1482240 | `683c25e24825e073fdd2673bd5d70f77842920392dd1dfaa2c3e0dcab8232da5` | 一致，且 `WhitelistTests/bin/Release/` 同名同哈希 |
| `Bug1ProbeRuntime.cs`（未改） | 51502 | `17ca472b6e86615b9653207c00c59cb20ef2fc934d559d5f8e2a146e5eb4e48d` | 与 t48 相同 |
| `Bug1ProbeTargetSelector.cs`（未改） | 6682 | `b716369fac0f4bace41f9a86297755b22d665fd7b64e7382e25fa9feb9d06da8` | 与 t48 相同 |
| `Bug1ProbePatch.cs`（未改） | 11707 | `22308d77197af4e58a34c8625f813e862783eebc06c3f5b129aaf29b28dd83d6` | 与 t48 相同 |
| `Bug1ProbeJudge.cs`（未改） | 22193 | `5d863441a4376cf2fb5b6ce06a29bc4baf92f8cab4e3a29d6929df79988fbb9f` | 与 t48 相同 |
| `Bug1ProbeNativeSpeed.cs`（未改） | 7614 | `109e283e5a37323c3c3838b62e595a290d84a95547f62a340e5581cd2d24dfae` | 与 t48 相同 |
| `Bug1ProbeRxPhases.cs` / `Bug1ProbeModel.cs`（未改） | — | `8d0d36fc…` / `eb40f806…` | 与 t48 相同 |
| Bug3 五文件（`Core/Patches/P2PGroupStateRecovery/*.cs`） | — | `3d5044f7…`/`a4553385…`/`7a82de8e…`/`5b0c4e9b…`/`a89f40b9…` | 与 t48 收尾树一致，未动 |

- 主树基线复跑（未重建，直接用 t51 05:40 的产物）：`732/734 PASS (Failed: 2)`，失败仅 GBA01/NIL01（`TypeInitializationException → SDN.Unturned.SteamPlayer/NetReflection → ReflectionTypeLoadException`），`Bug1-45…49` 全 PASS，打印 `[T14] SPF_DLL_SHA256=683c25e2…`。
- 本判定只对这些字节有效。

## 2. 三条修复的关闭判定

### 2.1 T49-F1（medium，Enable 失败留空 run 目录）— **行为关闭**

- `PrepareRun`（`Bug1ProbeControl.cs:252-289`）在建目录前探测两级是否已存在（`:266-267`），建后回填 `createdHostDirectory/createdRunDirectory`（`:274-275`），失败分支不建目录（`:261-263` Validate 在前）。
- `DiscardNewRunDirectory`（`:295-322`）：只删「本次新建」的两级（`:298`、`:310`），**非递归 + 判空**（`:302`、`:315`），两段各自 try/catch 只 Warn `RUN_DIR_CLEANUP_SKIPPED`（`:307`、`:320`）——fail-safe 方向正确，不会误删用户目录。
- `EnablePreparedRun`（`:328-339`）：`Enable` 失败 → `Discard`（`:336`）→ 回传 `LastError`（`:337`）→ false；调用点在 `TryStartCore` `:185`，失败即 `Refuse`（`:186`）。
- 用例 Bug1-45（`Bug1ProbeEntryRepairTests.cs:375-417`）：Install seam=false → 断言 `!Enabled`、`INSTALL_FAILED` 前缀、`<runId>/HOST` 与 `<runId>` 都不存在（`:400-401`）；随后 seam=true 走成功路径，断言目录保留（`:410`）——正反两侧都有。
- **我自跑变异 MUT-3**（把 `:298` 的 `if (createdHostDirectory)` 改成 `false &&`）→ DLL `c542349b27cd222aca4b1c31364918542be03533ccb590d585f9bb08d886b611`、731/734，**唯一新增红 Bug1-45**（报错正文即「Enable 失败后 `<runId>/HOST` 不得残留」）。⇒ 清理逻辑是承重的。
- 残留（不阻塞本条）：`EnablePreparedRun` 的**调用点**未被任何用例绑定，见 T52-F1。

### 2.2 R9-01（medium，重试计数与摘钩零覆盖）— **关闭（纯函数 + 调用存在性）**

- 纯函数 `Bug1ProbeEntryLogic.AccountRetryAttempt(attemptsUsed,maxAttempts,started,failureCode)`（`Bug1ProbeEntryLogic.cs:106-128`）：成功 → `Unhook=true/GiveUp=false/Reason="RETRY"`（`:109-118`）；失败 → `used=attemptsUsed+1`、`exhausted = maxAttempts<=0 || used>=maxAttempts`、`Unhook=GiveUp=exhausted`、原因为 `RETRY_GIVEUP` 或透传失败码（`:119-127`）。码值 `RETRY`/`RETRY_GIVEUP`（`:44-45`）与日志词表一致，未新增对外错误码。
- 接线：`TryRetryStartup`（`Bug1ProbeControl.cs:365-403`）先 `DecideRetry`（`:376`），GiveUp 分支 `UnhookStartupRetry`（`:383`），再 `TryStart`（`:389`）→ `AccountRetryAttempt`（`:390-391`）→ 写回 `_startupRetries`（`:392`）→ 打印 `RETRY_GIVEUP`（`:393-396`）→ `if (outcome.Unhook) UnhookStartupRetry()`（`:397`）。
- 上限语义自洽：`EvaluateStartupRetry`（`Bug1ProbeEntryLogic.cs:92-103`）在 `attemptsUsed >= maxAttempts` 时 GiveUp，故 `MaxStartupRetries=3`（`Control.cs:27`）下 `TryStart` 最多 3 次；第 3 次失败后 `used=3` → 摘钩。
- 用例：Bug1-46（`:451-488`）用纯函数穷举「3 次后停、第 4 次不再 Start、钩子已摘」「成功摘钩不放弃」「未到上限保持钩子并透传失败码」「上限 0 立即放弃」；Bug1-47（`:493-506`）IL 断言 `TryRetryStartup` 真的调用 `AccountRetryAttempt`/`DecideRetry`/`UnhookStartupRetry` 各 ≥1。
- 未闭合处（不阻塞本条，但记 T52-F2）：`outcome.Unhook/GiveUp` 的**使用**没有绑定——MUT-5 把 `:397` 改成 `false &&` 后仍 732/734 全绿。

### 2.3 R9-02（medium，开启异常留下「已开启」假象）— **关闭**

- `TryStart`（`Control.cs:96-108`）包住 `TryStartCore`，异常统一 `AbortStart`（`:106`）。
- `AbortStart`（`:114-135`）：`Enabled` 为真时 `Bug1ProbeRuntime.Disable("ENTRY_EXCEPTION")`（`:117-125`），回写 cfg `Enable=false`（`:126-133`，仅当确为 true），最后 `Refuse(ENTRY_INIT_FAILED, …)`（`:134`）。
- 热切换回调同样有守卫：`OnEnabledChanged` 内 `AbortStart(ex)`（`:72`）。
- 用例：Bug1-48（`:511-544`）先正常开启（`Enabled=true`），再 `AbortStart` → 断言返回 false 且 `!Enabled`，并验证未开启时幂等；Bug1-49（`:549-564`）IL 断言 `TryStart` 有 try/catch 且异常分支调用 `AbortStart`，`OnEnabledChanged` 有 try/catch。
- 语义核对：`AbortStart` 在 `Enabled=false` 时不调 `Disable`（避免无意义卸载），只写 cfg + 日志，符合「不留假象」且不误伤。

## 3. t49 已关闭六项的回归复核（本轮只改了 Control/EntryLogic，需确认未回退）

| 判据 | 本轮证据 | 结论 |
| --- | --- | --- |
| R8-01 判空 | `TargetSelector.cs` 哈希与 t48 相同（`b716369f…`）；MUT-2 实测仍由 Bug1-36 抓（见 §4） | 未回退 |
| F1 相位计数同源 | `Runtime.cs` 哈希与 t48 相同；`:721-722` 同步 → `:723-724` 取值 → `:725` Judge → `:738` `WriteArtifacts(verdict,…)` 仍是同一份；`Judge.cs:187-200` 判据未变 | 未回退 |
| R8-02 Install 失败 | `Runtime.cs` 未改（`Enabled=true` 仍在 `:152`，失败 `RollbackEnable` `:148`）；`Control.cs:185-186` 仍只在 `EnablePreparedRun` 返回 true 后才打 `START_OK`（`:188`） | 未回退 |
| R8-04 卸载 | `Runtime.cs` 未改（`Disable:207` → `Bug1ProbePatch.Uninstall()`）；`Patch.cs` 未改 | 未回退 |
| R8-03 启动重试 | 本轮重写 `TryRetryStartup`，但 `DecideRetry`（`:406-412`）与 `UnhookStartupRetry` 仍被调用（Bug1-47 IL 断言），`MaxStartupRetries=3` 未变 | 未回退（新增 T52-F2） |
| F4 两档上限 | `NativeSpeed.cs` 哈希与 t48 相同（`109e283e…`） | 未回退 |

## 4. 自跑变异记录（仓库外副本 `../spf-t52-iso`，主树未动）

副本用 `tar` 排除 `.git/bin/obj` 生成（`..\Libs` 相对引用成立）。每条变异均为：改源码 → `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` → 重建测试项目 → 跑 `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe`。

| 步骤 | 改动 | 变异源码 sha256 | 产物 DLL sha256 | 全量 | 新增红 |
| --- | --- | --- | --- | --- | --- |
| M0 | 无（副本基线） | 见 §1 | `683c25e24825e073fdd2673bd5d70f77842920392dd1dfaa2c3e0dcab8232da5` | 732/734 | —（GBA01/NIL01 既有环境失败） |
| MUT-1 | 删 `Bug1ProbeRuntime.cs:721-722` 两行 window 计数同步 | `fc4521a5a9146332c7b63287cc7a2c0b6d0f55b58657bb7d3eb59c8dfaf505dd` | `bcba3744d814a85b70becdfce8bcbab61b67df9be1eec82e7fce610096f2d60a` | 731/734 | **Bug1-38**（唯一）：`RUN_END 入队计数必须是 1，实际={…"rx_after_enqueue":0,"label":"Unknown","missing_fields":["AFTER_ENQUEUE","AFTER_DEQUEUE"]}` |
| MUT-2 | `Bug1ProbeTargetSelector.cs:65` `ReferenceEquals(id, null)` → `id == null` | `aac777ddc674b34e35bc4391cf254d4be1c6e52f89420c59244c47d50a6e4d26` | `0137919c611a3bad3468da2d5d7265a3bb678f18450b2c99450acb1538390451` | 731/734 | **Bug1-36**（唯一）：`System.NullReferenceException` |
| MUT-3 | `Bug1ProbeControl.cs:298` `if (createdHostDirectory)` → `if (false && createdHostDirectory)`（清理不删 host 目录） | `9301ef02c42317e66ac6c9a870c9c046f57490d34eac4995a5d71ff7e81874dd` | `c542349b27cd222aca4b1c31364918542be03533ccb590d585f9bb08d886b611` | 731/734 | **Bug1-45**（唯一）：`T49-F1：Enable 失败后 <runId>/HOST 不得残留` |
| MUT-4 | `Bug1ProbeControl.cs:185` 把 `EnablePreparedRun(...)` 换成直接 `Bug1ProbeRuntime.Enable(config)`（绕过清理入口） | `225bc39e3c10dfd0d8e6b3f4d3bdd8410f5a03a6680bfe8c5c4f8780c3c96bc1` | `1601daf19cc74e6ad6223189198125e08604a3f0d0bf81e9bf0a5f40b15e972c` | **732/734** | **无（全绿）** → T52-F1 |
| MUT-5 | `Bug1ProbeControl.cs:397` `if (outcome.Unhook) UnhookStartupRetry();` → `if (false && outcome.Unhook) …`（记账结果不被使用） | `b7ccd5965ef0010377fecb3846aafc6ca42144d1d463800453dfa7c73418bca7` | `8185ae84d8f5d0f87858bd827bb10f1d6e2bd866977931cf55ea643ddcd11f7b` | **732/734** | **无（全绿）** → T52-F2 |
| 还原 | `cp` 回送审三文件 + `-t:Rebuild` | Control `5f6738cf…`、Runtime `17ca472b…`、Selector `b716369f…` | `683c25e2…`（逐字节回送审值） | 732/734 | — |

- MUT-1/MUT-2 是本票契约点名要求的两条，均复现「唯一新增红」，说明 t49 关掉的两条判据在本轮字节上仍然承重。
- MUT-3 证明 T49-F1 的清理逻辑本身可失败；MUT-4 证明**它的调用点不可失败**——这两句必须同时成立，才叫「没有引入新的假绿」。
- 主树在整个审查期间未改：§1 的 15 个文件与 DLL 在抽样前后完全一致，副本 `../spf-t52-iso` 保留供复核。

## 5. RUN_END 计数与 result.json 判定的同源复核

本轮 `Bug1ProbeRuntime.cs` 未被修改（哈希与 t48 相同），链路仍为唯一来源：

1. 归零：`Enable` → `:124-125`（`_rxEnqueueRecords = 0`、`_rxDequeueRecords = 0`）；
2. 递增：`OnEnqueue` `:429`、`OnDequeue` `:447`（仅此两处 `++`）；
3. 判读前同步：`EndStep` `:721-722` 把静态值写进 `window.RxEnqueueRecords/RxDequeueRecords`；
4. 取值与判定：`:723-724` 读回同一对 int，`:725` `Bug1ProbeJudge.Judge(_window)`；
5. 判据：`Bug1ProbeJudge.cs:187-200` `ValidatePhaseCoverage`，`<= 0` → `MarkUnknown(..., "PHASE_COVERAGE_MISSING")`；
6. 落盘：`RUN_END` 行 `:732-733` 与 `result.json` `:738` `WriteArtifacts(verdict, reason)` 共用同一个 `verdict`。

⇒ 结构上不存在「RUN_END 打 N、result.json 判 UNKNOWN」的第二条计数路径；MUT-1 再次证明这条同步是承重的（删掉即 `rx_after_enqueue:0` + `PHASE_COVERAGE_MISSING`，并被 Bug1-38 抓住）。

## 6. cfg 语义与文档核对（本轮 `Enable` 默认值未被改动）

| 项 | 契约/文档 | 实现 | 判定 |
| --- | --- | --- | --- |
| 配置文件 | `BepInEx/config/SteamP2PFriends.Bug1Probe.cfg` | `Control.cs:24` `ConfigFileName`；`:41` `new ConfigFile(path, true)` | 一致 |
| `Enable` 默认 | **false**（默认关闭，防生产误采） | `:44-45` `_config.Bind("Bug1Probe","Enable", false, …)` | 一致 |
| `TargetPlayer` 默认 | 空（留空且唯一客机时自动选中） | `:46-47` 默认 `""`，说明文字与行为一致 | 一致 |
| `SpeedTier` 默认 | `auto` | `:48-50` 默认 `"auto"` | 一致 |
| auto 推导 | 走/跑两档，按开启瞬间姿态 | `NativeSpeed.cs:131-138`：空/auto → `sprinting ? sprint : walk`；`walk`/`sprint` 强制；其它 null → `Control.cs:169-170` 以 `NATIVE_SPEED_CAP_UNRESOLVED` 拒绝 | 一致 |
| 两档公式 | `SPEED_STAND/SPEED_SPRINT × exercise × totalSpeedMultiplier` | `NativeSpeed.ComputeTiers`；SDK `PlayerMovement.cs:93-96` 常量、`:303` exercise、`:309-316` 分支 | 一致 |
| 拒绝码词表 | 9 个稳定码 | `EntryLogic.cs:33-41` 未变；`Refuse` 统一打 `REFUSED <code>`（`Control.cs:202-206`） | 一致 |
| 新增原因码 | 不得新增对外错误码 | `RETRY`/`RETRY_GIVEUP`（`EntryLogic.cs:44-45`）只用于记账/日志原因，未进入 `Validate` 的错误码集合 | 一致 |

## 7. Findings

| id | 级别 | 问题 | 证据 | requiredFix |
| --- | --- | --- | --- | --- |
| T52-F1 | medium | T49-F1 的生产接线未被绑定：`TryStartCore` 若退回直接 `Bug1ProbeRuntime.Enable(config)`（不再经 `EnablePreparedRun`），清理永不执行，但全部 734 项仍绿——正是 t42 主验收「删生产接线必须变红」禁止的那类假绿。当前行为本身正确，缺的是让接线可失败。 | MUT-4：`Control.cs:185` 绕过 `EnablePreparedRun` 后 DLL `1601daf1…`、**732/734 全绿**；`WhitelistTests` 中无任何 `TryStartCore` 断言，`EnablePreparedRun` 只在测试里被直接调用（`Bug1ProbeEntryRepairTests.cs:395/408/527`）。 | 加一条 IL 契约：`IlContractProbe.CountMethodCalls(typeof(Bug1ProbeControl).GetMethod("TryStartCore", NonPublic), control, "EnablePreparedRun") >= 1`（与 Bug1-47 同型）；或给 `TryStartCore` 的 Enable 段做 seam 用例，断言失败路径确实删目录。 |
| T52-F2 | low | R9-01 的记账**结果**未被绑定：`outcome.Unhook/GiveUp` 若被忽略（只写回 `_startupRetries`），钩子永不摘、会无限被事件唤醒，但 734 项仍绿。Bug1-46 是「复刻生产循环」的纯函数用例，不驱动 `TryRetryStartup`；Bug1-47 只断言调用存在，不断言结果被使用。 | MUT-5：`Control.cs:397` 改为 `false &&` 后 DLL `8185ae84…`、**732/734 全绿**。 | 二选一：把 `TryRetryStartup` 的 `DecideRetry`/`TryStart` 抽出可注入 seam，做一条「第 3 次失败后钩子已摘、第 4 次事件不再 Start」的状态机用例；或 IL 断言 `UnhookStartupRetry` 的调用位于 `outcome` 消费之后（弱一些，至少加「读数取自 `Bug1RetryOutcome`」的结构断言）。 |
| T52-F3 | info | 契约 `inScope` 与实际改动不符：t51 必须改 `Bug1ProbeEntryLogic.cs` 与两个测试文件，但自动生成的 inScope 未列它们（t51 已在 changedPaths 里注明）。这不是实现缺陷，是派工契约文本问题。 | t51 output 的「本票实际改动的全部文件」注记；本票 inScope 列表含 `Adapters/Zombie/ZombieSnapshotAdapter.cs` 等无关项。 | 由 Captain 在后续票把 `Platform/Diagnostics/Patches/Bug1ProbeEntryLogic.cs` 与 `WhitelistTests/Evidence/Runtime/Bug1ProbeEntryRepairTests.cs`、`WhitelistTests/Program.cs` 补进 inScope，避免下轮再出现「改了但未声明」。 |
| T52-F4 | info | t49 的 F2/F3 本轮被明确排除，**仍未关闭**，不得因为本轮 732/734 而视为已收口：F2（auto 遇 CROUCH/PRONE 仍按 walk 上限，SDK 原生 2.5/1.5 更低）；F3（探针外 6 处同类 `playerID == null`，见 t49 报告）。 | t51 报告开头「范围裁决」写明两者不在本票；本轮文件哈希显示 `NativeSpeed.cs` 未改，`Adapters/*` 未动。 | 保持开票：F2 归后续轮或 spec 追认；F3 归独立低优先级票。 |

## 8. 诚实边界与仍未验证项

- t51 报告没有把「仪表能开」写成「Bug1 已诊断」：开头即写明只做 T49-F1 + R9-01/R9-02，§8 区分「已自动化可证」与「仍需 1H+1G 实机」，三项 UNKNOWN 如实保留：
  1. Mono 下 BCL `Queue<PlayerInputPacket>.Enqueue/Dequeue` detour 是否真的触发（离线只证明装上，不证明被调用）；
  2. 启动重试在真实关卡时序下是否命中（`Level.onLevelLoaded`/`Player.onPlayerCreated` 与 `Provider.isServer`、首个客机进入的先后）；
  3. `isDismissed` 是否存在于发布版。
- 环境限制同样如实：离线进程里 5 条 Assembly-CSharp 原生 target 报 `HarmonyException: IL Compile Error`，只有 BCL 两条能装；生产侧是否全部装上仍需 1H 实机核对 `spf_hook_errors`/`FailClosed`。
- 本轮 732/734 的两条失败（GBA01/NIL01）是控制台缺 Unity 运行时的既有环境失败，既不算通过也不算回归。

| 项 | 归属 | 关闭条件 |
| --- | --- | --- |
| T52-F1（接线不可失败） | **下一修复轮**（可并入 t53） | 新增 IL 断言或 seam 用例后，MUT-4 必须变红 |
| T52-F2（记账结果未绑定） | 下一修复轮（可与 F1 同批） | MUT-5 必须变红 |
| T52-F3（inScope 缺项） | Captain 派工文本 | 后续票 inScope 补三文件 |
| T49-F2（crouch/prone 上限） | 后续轮或 spec 追认 | 建模三档 / 拒绝 / 书面登记限制 |
| T49-F3（探针外同类判空） | 独立低优先级票 | 统一改 `ReferenceEquals` 并有用例 |
| 三项实机 UNKNOWN + 5 条原生 patch | **实机 1H+1G** | `trace.jsonl` 出现真实相位记录、`RETRY_ARMED→RETRY_OK` 链路、`spf_hook_errors` 为空 |

## 9. 复审边界

- 只对 §1 的字节有效；任何文件再变（含 `Program.cs`、`csproj`）须重判。
- 只判规格/需求与「是否引入假绿」；命名、注释密度、复杂度等工程风格问题由另一轴负责。
- 未修改主树任何生产/测试/构建产物；五条变异全部在 `../spf-t52-iso` 完成并已还原（副本保留供 Captain 复核）。
- needs_revision 的范围仅限 T52-F1（medium）与 T52-F2（low）两条**覆盖缺口**；三条修复的**行为**已判通过，T49 六项判据未回退。本结论不代表可以出包，也不代表 Bug1 已诊断。

## 10. 自证

- 正文（§1–§9）在追加本节前的字节数与哈希：**19168 B / `4ecbb3573ba9c1f56587a4c82439bb30783b453cbdc01ba12f4ea4c2ce34a8ae`**（截断本节即可复核）。
- 主树复核：`Control 5f6738cf` / `EntryLogic 04a4beeb` / `Runtime 17ca472b` / `Selector b716369f` / `DLL 683c25e2` 在本轮结束再次取样，与 §1 完全一致。
