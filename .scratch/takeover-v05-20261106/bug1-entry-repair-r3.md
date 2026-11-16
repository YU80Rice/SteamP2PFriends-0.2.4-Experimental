# Bug1 仪表入口修复轮 r3（t51 / repair-round-3）

- 票：t51，attempt 1，attempt_id `0e4d33cb-149f-4daa-9dbf-fc681f870ef5`
- 来源：t49（规格轴 review）+ t50（工程轴 review）的 findings
- 范围裁决：captain 消息（m00497 / m00524）收紧——只做 T49-F1（必做）+ 本轮并入的 R9-01、R9-02；
  T49-F2、T49-F3 明确不在本票。
- 基线：t48 收尾树，DLL `f28c211a9178818d81d2d58721a4e5b9db101371ce0a6be836ff37561301e2e8`，727/729。
- 本轮终态：DLL `683c25e24825e073fdd2673bd5d70f77842920392dd1dfaa2c3e0dcab8232da5`，732/734（仅 GBA01、NIL01 两条既有环境失败）。

---

## 1. T49-F1（medium，必做）：Enable 失败会在磁盘上留一个空 run 目录

### 缺陷
`Bug1ProbeControl.TryStart` 的顺序是「`PrepareRun`（内部 `Directory.CreateDirectory(outputRoot)`）→ `Bug1ProbeRuntime.Enable(config)`」。
Enable 返回 false（典型：`INSTALL_FAILED`）时旧代码直接 `return Refuse(...)`，把刚建出来的
`<BepInExRoot>/diagnostics/spf-bug1/<runId>/HOST` 留在磁盘上。于是「拒绝路径不留空 run 目录」这条
F3 契约只对 ALREADY_RUNNING / 参数缺失成立，对 INSTALL_FAILED 不成立。

### 修法：方案 2（Enable 失败时只删本次新建的空目录）
captain 给了两个选项，选 **方案 2**，理由：

1. 保留 `CreateDirectory` 在 `Enable` 之前的既有顺序，`Validate` 语义、`START_OK` 日志内容与
   `dir=` 字段含义、既有用例 Bug1-43 全部不动（方案 1 会把目录创建挪到开启成功之后，
   `START_OK` 打印的 `dir` 在打印瞬间尚不存在，且 Bug1-43 的「通过后才创建」断言要重写）。
2. 删除是**非递归 + 只删本次记录为「新建」的两级目录 + 先判空**：`Directory.GetFileSystemEntries(dir).Length == 0`
   才 `Directory.Delete(dir, false)`，任何异常都 catch 住并只打 `RUN_DIR_CLEANUP_SKIPPED`，
   **宁可留空目录也不误删**。因此不存在 captain 担心的「递归清理误删用户目录」风险，
   与并发/重入的冲突面只剩「判空后被别人写入 → Delete 抛 → 被 catch → 保留」，是 fail-safe 方向。
3. 方案 1 需要引入「先开启成功、再建目录失败」的新回滚分支（Enable 已成功但目录建不出来时要 Disable），
   反而扩大了改动面与冻结项。

### 改动（`Platform/Diagnostics/Patches/Bug1ProbeControl.cs`）
- `PrepareRun(...)` 新增两个 out：`out bool createdHostDirectory, out bool createdRunDirectory`。
  创建前先探测两级目录是否已存在，创建后回填「本次是否新建」；`Validate` 拒绝时两个 out 均为 false 且不建目录。
- 新增 `internal static void DiscardNewRunDirectory(string outputRoot, bool createdHostDirectory, bool createdRunDirectory)`：
  仅当对应级别是「本次新建」且目录为空时非递归删除；两段各自 try/catch，失败只 Warn `RUN_DIR_CLEANUP_SKIPPED host|run`。
- 新增 `internal static bool EnablePreparedRun(Bug1ProbeConfig config, string outputRoot, bool createdHostDirectory, bool createdRunDirectory, out string error)`：
  `Enable` 成功 → 返回 true；失败 → `DiscardNewRunDirectory(...)` → 返回 false 并回传 `LastError`。
  抽成静态方法是让离线用例走**同一条生产分支**（不碰 Provider/Unity）。
- `TryStart` 改为经 `EnablePreparedRun`；失败仍走原来的 `Refuse(enableError, "运行时拒绝开启")`，
  日志词表、START_OK 行、后续 `TryBindTarget` 全部未变。

### 对比例子
- 修复前：`PrepareRun` 建 `<runId>/HOST` → `Enable` 失败 → `REFUSED INSTALL_FAILED:patch_install_returned_false`
  → 磁盘上留下空的 `<runId>/HOST`（t49 报告复现的就是这个）。
- 修复后：同一路径下 `Enable` 失败 → 先删空 `HOST`，再删空的 `<runId>` → `REFUSED ...` 且
  `!Directory.Exists(<runId>/HOST) && !Directory.Exists(<runId>)`（用例 Bug1-45 断言）。
  成功路径目录保留（同一用例的后半段证明「只回滚失败路径」）。

---

## 2. R9-01（medium，本轮并入）：启动重试的计数与摘钩零覆盖

### 缺陷
t50 的对照变异 E（把「启动重试钩子永不挂载」改出来）**727/729 全绿**——`Control.cs` 里
`TryRetryStartup` 的「累计次数 / 到上限放弃 / 摘钩」逻辑只靠读码，没有任何用例钉住。

### 修法
- `Bug1ProbeEntryLogic` 新增纯函数 `AccountRetryAttempt(int attemptsUsed, int maxAttempts, bool started, string failureCode)`
  返回 `Bug1RetryOutcome { AttemptsUsed, Unhook, GiveUp, Reason }`：
  成功 → 摘钩不放弃（Reason=`RETRY`）；累计次数达到上限（或上限 ≤0）→ 摘钩并放弃（Reason=`RETRY_GIVEUP`）；
  否则保持钩子、Reason 透传失败码。新增常量 `RetryReasonStarted="RETRY"`、`RetryReasonExhausted="RETRY_GIVEUP"`（复用既有日志词，不新增对外词）。
- `Control.TryRetryStartup` 不再自己做 `_startupRetries++` / `>= MaxStartupRetries` 判断，
  改为调用 `AccountRetryAttempt` 后按 `outcome.AttemptsUsed / GiveUp / Unhook` 执行；`DecideRetry` 纯判定仍在前面。

### 用例（Bug1-46 / Bug1-47）
- Bug1-46 用 `EvaluateStartupRetry` + `AccountRetryAttempt` 组合跑完整序列：
  上限 3 → **只 Start 3 次**、累计停在 3、**第 4 次不再 Start 且钩子已摘**；另覆盖「成功立即摘钩」「未到上限保持钩子」
  「上限 ≤0 立即放弃」「Reason 透传失败码」。
- Bug1-47 是接线用例（IL 探针）：断言 `TryRetryStartup` 的 IL 里确实 `call AccountRetryAttempt`、
  `call DecideRetry`、`call UnhookStartupRetry`。这条是「删掉抽取的调用点必须变红」的落点（见 §5 M-F）。

---

## 3. R9-02（medium，本轮并入）：开启失败会留一个骗人的「已开启」状态

### 缺陷
`OnEnabledChanged` / `TryStart` 没有 try/catch。异常逃逸时 cfg 里 `Enable=true`、内存里没跑起来，
用户看到的是「开了但没数据」。

### 修法
- `TryStart` 变成薄壳：`try { return TryStartCore(origin); } catch (Exception ex) { return AbortStart(ex); }`；
  原函数体整体改名 `TryStartCore`，逻辑一字未改。
- `OnEnabledChanged` 同样变成薄壳 + `OnEnabledChangedCore`，异常走同一个 `AbortStart`。
- 新增 `internal static bool AbortStart(Exception ex)`：运行时若 `Enabled` 则 `Disable("ENTRY_EXCEPTION")`
  真关闭（不留 `Enabled=true` 假象）→ cfg 的 `Enable` 若为 true 则回写 false（失败只 Warn `ENTRY_CFG_RESET_FAILED`）
  → `Refuse(ErrorEntryInitFailed, "开启过程异常 " + ex.GetType().Name)`（日志仍是 REFUSED / ENTRY_INIT_FAILED 词表）。

### 用例（Bug1-48 / Bug1-49）
- Bug1-48 行为：离线 seam 正常装 → `EnablePreparedRun` 成功、`Enabled=true` → `AbortStart(new InvalidOperationException("boom"))`
  必须返回 false、**`Enabled` 必须为 false**；再对未开启状态调一次 `AbortStart` 必须仍返回 false（幂等）且不得抛。
- Bug1-49 接线（IL 探针）：`TryStart` 必须有 ≥1 个异常处理器且异常分支 `call AbortStart`；
  `OnEnabledChanged` 同样必须有异常处理器。

---

## 4. 三命令（`-t:Rebuild`）

| 命令 | 结果 |
| --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 已成功生成。**0 个警告 / 0 个错误** |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 已成功生成。**4 个警告（既有）/ 0 个错误** |
| `./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `=== Final Result: 732/734 PASS (Failed: 2) ===` |

失败两条仍是既有环境失败，与本次改动无关：
- `FAIL [PureMemory] GBA01 Native First Observe Binding`
- `FAIL [PureMemory] NIL01 Real Native Lookup Contract`（`TypeInitializationException: SDG.Unturned.SteamPlayer` 链）
exe 退出码 1 属仓库既有约定（有失败即 1）。

Bug1 段：Bug1-01…Bug1-49 共 **49/49 PASS**（新增 Bug1-45…49 五条全绿）。

---

## 5. 变异证据（同一 DLL 哈希链，全部在仓库外副本 `../_t51mut`）

基线：主树最终 DLL `683c25e24825e073fdd2673bd5d70f77842920392dd1dfaa2c3e0dcab8232da5`（732/734，Bug1 49/49）。
副本做法：`cp -a` 整仓到 `../_t51mut` → 删 `bin/obj/Release/publishi/.scratch/.git` →
首次构建报 `error MSB4019: 找不到导入的项目 "..._t51mut\Build\Version.props"`，补 `cp -a <原仓>/Build _t51mut/Build`
（`Build/` 是只读输入，主树内未被写）后构建通过。**未在树内跑任何变异**（今晚已有两例同树变异互相打断）。

| 变异 | 改法 | 变异后 DLL sha256 | 红用例 | 全量 |
| --- | --- | --- | --- | --- |
| **M-E**（T49-F1） | 删掉 `EnablePreparedRun` 内的 `DiscardNewRunDirectory(...)` 调用 | `4a967fa21b3e3c25677da238e7d6739412f5f76fb16e253336ae58376c428a13` | `Bug1-45 … T49-F1：Enable 失败后 <runId>/HOST 不得残留` | 731/734 |
| **M-F**（R9-01） | `TryRetryStartup` 换回内联 `_startupRetries++ / >= MaxStartupRetries`（删掉 `AccountRetryAttempt` 调用点） | `57c5cca8a20e3ccf84b232b4d8e61423b49ea6c8696b1740dadb7d1ebcc3040d` | `Bug1-47 … R9-01：TryRetryStartup 必须调用 AccountRetryAttempt 记账，实际=0` | 731/734 |
| **M-G**（R9-02） | `TryStart` 的 try/catch 换成 `return TryStartCore(origin);` | `c02f7caea0832ea23570c9c79839e47e3cb8a9c323be6b25477be2d0d2e0e8f2` | `Bug1-49 … R9-02：TryStart 必须有 try/catch，实际 handler=0` | 731/734 |

还原核对：
- M-E、M-F 还原：`cp` 主树 `Bug1ProbeControl.cs` 回副本，源码哈希回到 `5f6738cf…`。
- M-G 还原：同样 `cp` 回主树文件后**重建副本 DLL**，得到 `683c25e2…`——与主树 DLL 逐字节一致，
  再跑一次全量回到 732/734、Bug1 49/49，随后 `rm -rf ../_t51mut` 删除副本。
- 每条变异都只红**对应的那一条**用例，其余 Bug1 用例保持绿，说明红是「修复被删」而非「环境抖动」。

---

## 6. 逐文件 sha256 与未触碰复核

本轮改动（4 个文件，均在 in-scope 内）：

| 文件 | 行数 | sha256 |
| --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1ProbeControl.cs` | 431 | `5f6738cfa02e32dd0378cbcc3ea59259ef0f95415ae1ebfc24791da018635400` |
| `Platform/Diagnostics/Patches/Bug1ProbeEntryLogic.cs` | 130 | `04a4beeb058849b10788194eac19fd073268ad22375f44020d24183f5dad62fc` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeEntryRepairTests.cs` | 566 | `f6de7455ca93dae127018d24da453198750b305b52f9b5e0b069a903cf5d06aa` |
| `WhitelistTests/Program.cs` | — | `eb1287f8a42d20acf82564cffdeefe1f17a3b8f6034db0e82c8e180a25d7d2b0` |
| `bin/Release/SteamP2PFriends.dll` | — | `683c25e24825e073fdd2673bd5d70f77842920392dd1dfaa2c3e0dcab8232da5` |

本轮**未改**的 Bug1 文件（哈希前 16 位，供下一轮比对）：
`Bug1ProbeRuntime.cs 17ca472b6e86615b`、`Bug1ProbePatch.cs 22308d77197af4e5`、
`Bug1ProbeNativeSpeed.cs 109e283e5a37323c`、`Bug1ProbeTargetSelector.cs b716369fac0f4bac`。

Bug3 五文件哈希（前 12 位，**均未变**）：`P2PGroupStateRecoveryPatch.cs a89f40b949f8`、
`GroupStateRecoveryCore.cs 7a82de8e38fa`、`GroupLeaseLedger.cs 3d5044f7870f`、
`GroupLoadPlanner.cs a45533850f31`、`P2PGroupRegistration.cs 5b0c4e9ba4c8`。

交付目录未被写：`find Build Release publishi -newermt "2026-11-07 05:30" -type f` → 无输出；
`Release/` 最新条目 `0.2.4.11-preview2`（mtime 2026-11-07T01:32）、`publishi/` 最新 `0.2.4.11-preview`
（2026-11-06T23:56），都早于本轮；`Build/Version.props` mtime 2026-11-06T20:01，是既有改动。

`grep -rn "playerID == null\|playerID != null" Platform/Diagnostics/Patches/` → **0 命中**（exit 1），
t48 的 R8-01 修复未被本轮回退。

---

## 7. 未改项与理由（按 captain 口径）

- **T49-F2（auto 档遇 CROUCH/PRONE 套用 walk 上限）**：不在本票。captain 另问架构裁决，
  本轮**未动** `Bug1ProbeNativeSpeed.ResolveTier` 的任何分支，cfg 键 `Bug1Probe/SpeedTier` 字面量未变。
- **T49-F3（探针外六处同类 `playerID == null`）**：不在本票。建议另开低优先级票；
  本轮只保证 `Platform/Diagnostics/Patches/` 下 0 命中（见 §6）。
- **`spf_hook_errors` / `FailClosed`**：无需代码修复。归入实机项——1H 上 Enable 后核对
  `spf_hook_errors` 是否为空、`FailClosed` 是否保持 false（离线进程装不上 5 条原生 patch，见 §8）。
- 生产判定逻辑、`Bug1ProbeJudge`、其余日志词表、cfg 键名：全部冻结未动；
  唯一与 cfg 相关的写操作是 R9-02 的「异常时把 `Enable` 回写 false」，这是 captain 明确允许的那一项。

---

## 8. 「已自动化可证」vs「仍需 1H+1G 实机」

### 已自动化可证（离线可复现，跑在上面三条命令里）
1. **T49-F1**：`Enable` 失败时本次新建的 `<runId>/HOST` 与 `<runId>` 都被删掉；成功路径目录保留。
   删掉修复 → Bug1-45 必红（M-E 实测）。
2. **R9-01**：重试记账到上限后**第 4 次不再 Start 且钩子已摘**；`TryRetryStartup` 的 IL 里确实调用
   `AccountRetryAttempt`/`DecideRetry`/`UnhookStartupRetry`。删掉调用点 → Bug1-47 必红（M-F 实测）。
3. **R9-02**：开启过程抛异常后 `Enabled=false`（不再有「开了但没数据」的假象），并留下
   `REFUSED ENTRY_INIT_FAILED 开启过程异常 <异常类型>` 痕迹；`TryStart`/`OnEnabledChanged` 的 IL 里确有异常处理器。
   删掉 try/catch → Bug1-49 必红（M-G 实测）。
4. t48 的 R8-01（三处判空改 `ReferenceEquals` + 纯函数选择器）与 F1（相位计数同步）仍由
   Bug1-36…Bug1-42 钉住，本轮未回退（t49 已自跑变异确认，本轮不重做）。

### 仍需 1H+1G 实机（自动化绿灯**不能**替代）
1. **Mono 下 BCL `Queue<PlayerInputPacket>` 的 Enqueue/Dequeue detour 是否真触发**：
   离线进程里这两条 patch 能装（`patch=ok verify=True`），但「装了」不等于「Unturned 的
   `PlayerInput.serversidePackets` 实例真的走到它们」。需要 1H 实机看 `trace.jsonl` 是否出现
   `AFTER_ENQUEUE`/`AFTER_DEQUEUE` 行。
2. **启动期重试在真实关卡时序下是否命中**：`Level.onLevelLoaded` / `Player.onPlayerCreated` 的
   触发顺序与 `Provider.isServer` 就绪时刻只能在真实 listen-host 下确认；离线只能证明
   「该重试的判定与计数正确」和「钩子挂/摘接线存在」，不能证明「真实时序里第 1 次重试就成功」。
3. **`isDismissed` 是否存在**：`PlayerInput` 上的 dismissed 语义靠反射读取，
   实机需确认该成员存在且取值符合预期（缺失时 `IsDismissedRaw` 走 unknown 分支）。

另：离线进程里 5 条 Assembly-CSharp 原生 patch（`simulate` Prefix/Postfix、`update` Prefix、
`receiveInputs` Prefix/Postfix）一律报 `HarmonyException: IL Compile Error (unknown location)`，
这是离线测试环境的限制（拿不到原生 IL），不是生产缺陷；生产侧是否全部装上，同样要 1H 实机核对
（对应 `spf_hook_errors` 项）。
