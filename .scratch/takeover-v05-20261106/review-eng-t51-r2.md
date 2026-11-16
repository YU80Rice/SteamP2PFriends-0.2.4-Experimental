# 工程轴复审 r2：t51（Bug1 仪表入口第 3 轮 — T49-F1 / R9-01 / R9-02）

审查员：reviewer-eng（工程轴）。只读判定：本票**没有修改主树任何字节**，变异全部在仓库外副本 `../_t53mut` 内完成，
副本已删除，主树 DLL 与源文件哈希在开工/收尾两次取样一致。

被审对象：t51（repair-round-3），承载 t50 的 R9-01/R9-02 与 t49 的 T49-F1。

## 0. 结论

**verdict = pass（有 2 条 medium、5 条 low，全部为守卫强度/覆盖问题，无当下功能性缺陷）。**

三项修复都真实落地，且都用"删掉就变红"验证过：

| 项 | 判定 | 关键证据 |
| --- | --- | --- |
| T49-F1 空目录清理 | **真关闭**，非递归、不误删 | 变异 M-A 删调用 → Bug1-45 红；反射探针实测"已有目录/非空/仅单级"三类输入均保留不删 |
| R9-01 重试记账 | **记账纯函数真关闭**；接线只钉到"调用存在" | 变异 M-B1 删调用 → Bug1-47 红；M-B3 保留调用但永不摘钩 → **732/734 全绿**（R10-01） |
| R9-02 异常 abort | **真关闭**（内存态一致性 + AbortStart 调用链） | 变异 M-C 去掉 `AbortStart` 调用 → Bug1-49 红；M-D 去掉 try/catch → Bug1-49 红 |

两处必须写进交付说明的边界：**本票 pass ≠ 仪表可交付，亦 ≠ Bug1 已诊断**。§10 的三项实机 UNKNOWN 一条都没有被
自动化绿灯覆盖，本复审也不把它们抹平。

## 1. 被审字节锁定（本人重新取样）

| 文件 | 字节 | sha256 |
| --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1ProbeControl.cs` | 22068 | `5f6738cfa02e32dd0378cbcc3ea59259ef0f95415ae1ebfc24791da018635400` |
| `Platform/Diagnostics/Patches/Bug1ProbeEntryLogic.cs` | 6565 | `04a4beeb058849b10788194eac19fd073268ad22375f44020d24183f5dad62fc` |
| `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs` | 51502 | `17ca472b6e86615b9653207c00c59cb20ef2fc934d559d5f8e2a146e5eb4e48d`（与 t50 同哈希，未动） |
| `Platform/Diagnostics/Patches/Bug1ProbePatch.cs` | 11707 | `22308d77197af4e58a34c8625f813e862783eebc06c3f5b129aaf29b28dd83d6`（未动） |
| `Platform/Diagnostics/Patches/Bug1ProbeTargetSelector.cs` | 6682 | `b716369fac0f4bace41f9a86297755b22d665fd7b64e7382e25fa9feb9d06da8`（未动） |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeEntryRepairTests.cs` | 34788 | `f6de7455ca93dae127018d24da453198750b305b52f9b5e0b069a903cf5d06aa` |
| `WhitelistTests/Program.cs` | 140251 | `eb1287f8a42d20acf82564cffdeefe1f17a3b8f6034db0e82c8e180a25d7d2b0` |
| `SteamP2PFriends.csproj` | 27492 | `8b27ab5587b2656e0153cff2d2577c79d8d20bc2d5a5d544aadc572c732dd89e`（与 t50 同，未动） |
| `bin/Release/SteamP2PFriends.dll` | 1482240 | `683c25e24825e073fdd2673bd5d70f77842920392dd1dfaa2c3e0dcab8232da5` |

Bug3 五个生产文件（前 16 位）：`GroupStateRecoveryCore.cs` `7a82de8e38fa0f6a`、`P2PGroupStateRecoveryPatch.cs`
`a89f40b949f841d0`、`GroupLeaseLedger.cs` `3d5044f7870fbb17`、`GroupLoadPlanner.cs` `a45533850f31bd96`、
`P2PGroupRegistration.cs` `5b0c4e9ba4c87cbe` —— 与 t34/t44 锁定的字节**逐位一致，本轮未触碰**。

写入窗口（mtime，均落在 t51 窗口内，其余文件停在更早的票）：
`Bug1ProbeControl.cs` 05:40:14、`Bug1ProbeEntryLogic.cs` 05:39:22、`Bug1ProbeEntryRepairTests.cs` 05:41:43、
`WhitelistTests/Program.cs` 05:41:56。`Release/0.2.4.11-preview2` 停在 01:32、`publishi/0.2.4.11-preview` 停在
23:56、`Build/Version.props` 停在 20:01 —— **本轮没有写任何发布/构建产物目录**。

## 2. 三命令复跑（本人执行，非采信自述）

| 命令 | 退出码 | 结果 |
| --- | --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 | 0 警告 / 0 错误（1.56s），DLL 复现 `683c25e2…`（确定性构建） |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release` | 0 | 0 错误 / 4 警告（CS0649 等，均既有） |
| `./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | 1 | **732/734 PASS（Failed: 2）**；失败仅 `GBA01`、`NIL01`（`TypeInitializationException: SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`，控制台缺 Unity）；Bug1 段 **49/49**；`[T14] SPF_DLL_SHA256=683c25e2…` 与磁盘一致 |

与 t51 自述（732/734、Bug1 49/49）一致。exe 退出码 1 属仓库既有约定（有失败即 1）。

## 3. T49-F1：删除安全性（逐行 + 实测探针）

### 3.1 逐行

- **探测在创建之前**：`PrepareRun` 先 `Directory.Exists(outputRoot)`（:266）、`Directory.Exists(runDirectory)`（:267），
  再 `Directory.CreateDirectory(outputRoot)`（:268）；两个 created 标志在创建成功后才赋值（:274-275），
  创建失败直接 return（:269-273）且标志保持 false。标志是**局部变量**（:177-178 → 传参），不落静态，不会被
  并发/重入污染；调用方 `TryStart`、重试回调、配置回调全部在主线程串行。
- **同名 runId 不误删**：`createdRunDirectory = !runExisted`，目录已存在 → 标志 false → 整段删除被 `if` 跳过（:310）。
  runId 是 UTC 秒级时间戳，同一秒重开由 `ALREADY_RUNNING` 挡住。
- **非递归**：全文只有两处 `Directory.Delete(x, false)`（:303、:316）；`Directory.Delete(..., true)` grep **0 命中**。
- **非空/占用/只读/大小写**：
  - 非空：`GetFileSystemEntries(...).Length == 0` 才删（:302、:315），且删除是**非递归**的——即使 check 与 delete
    之间有新文件落盘，`Delete(path,false)` 会抛 `IOException` 被 catch → 打 `RUN_DIR_CLEANUP_SKIPPED`，不会递归清空。
  - 占用/只读：`Delete(false)` 抛 `IOException`/`UnauthorizedAccessException`，两处各有独立 `catch`（:305-308、:318-321），
    只打 `RUN_DIR_CLEANUP_SKIPPED host|run <异常类型>`，**向上不抛**。
  - 大小写差异：Windows 上 `Directory.Exists` 大小写不敏感 → 用户已有的 `BUG1-XXX` 会被判为已存在 → 标志 false → 不删。
- **两级语义正确**：先删 HOST（:298-309），再删 runId（:310-321）；若 runId 里还有别的文件（例如用户放了东西），
  第二次删除自然被非空判定挡住。

### 3.2 实测（反射直接调生产方法，主树 DLL `683c25e2…`）

| 场景 | 调用 | 结果 |
| --- | --- | --- |
| S1 目录已存在、标志 false | `(HOST, false, false)` | no-throw，两级**都保留** |
| S2 HOST 非空、标志 true,true | `(HOST, true, true)` | no-throw，两级**都保留**（不误删） |
| S3 run 有文件、HOST 空 | `(HOST, true, true)` | no-throw，HOST 删、run **保留** |
| S4 两级空、标志 true,true | `(HOST, true, true)` | no-throw，两级都删（正常回滚） |
| S5 新建空 HOST、仅 host 标志 | `(ro, true, false)` | no-throw，已删 |
| S6 null / 空串路径 | `(null, true, true)`、`("", true, true)` | no-throw |

结论：**"只删本次新建、且为空"被真实执行，不是注释承诺**；非递归成立。只读属性目录未单独实测（Windows 上目录只读
不阻止删除；即便删掉也是本次自建的空目录，无数据风险）。

## 4. R9-01：重试记账

- **纯函数**：`Bug1ProbeEntryLogic.AccountRetryAttempt(int attemptsUsed, int maxAttempts, bool started, string failureCode)`
  （`Bug1ProbeEntryLogic.cs:106-128`）只做整数运算，不碰 Unity/Provider/日志/静态状态。四态齐备：
  ①`started` → 次数不变、`Unhook=true`、`GiveUp=false`、Reason=`RETRY`；
  ②未到上限 → `used+1`、`Unhook=false`、`GiveUp=false`、Reason 透传失败码；
  ③到上限（`used >= max` 或 `max <= 0`）→ `Unhook=GiveUp=true`、Reason=`RETRY_GIVEUP`；
  ④上限 ≤0 → 第一次就放弃。
  `>=` 语义配合 `MaxStartupRetries = 3`（`Bug1ProbeControl.cs:27`）恰好执行 3 次；Bug1-46 用 10 轮循环断言
  `starts==3 / used==3 / !hooked`，覆盖①②③④。
- **接线**：`TryRetryStartup`（:365-403）→ `DecideRetry`（:406-412）→ `TryStart` → `AccountRetryAttempt`（:390-391）→
  `_startupRetries = outcome.AttemptsUsed` → `if (outcome.Unhook) UnhookStartupRetry()`。语义与纯函数一致。
- **Bug1-47 钉的是接线，不是自证**：IL 计数发生在**生产方法 `TryRetryStartup` 的方法体**上
  （`CountMethodCalls(retry, "…Bug1ProbeEntryLogic", "AccountRetryAttempt") >= 1`，并同时要求 `DecideRetry`、`UnhookStartupRetry` 各 ≥1），
  不是"另写一份逻辑再自测"。
- **变异**：
  - M-B1（把记账换回内联表达式、**删掉调用**）→ DLL `ab124158…`，731/734，红 **Bug1-47**（`实际=0`）。
  - M-B2（保留调用但忽略返回值，改回 `_startupRetries + 1`）→ DLL `1015e05e…`，732/734 **全绿**。
    该变异在语义上接近等价（`started` 时分母只用于日志），判"可接受"。
  - M-B3（保留两个调用，但把摘钩改成**永不执行**）→ DLL `1c2d9a19…`，732/734 **全绿** —— 见 R10-01。

## 5. R9-02：异常 abort 的失败可见性

- `AbortStart(Exception)`（`Bug1ProbeControl.cs:114-135`）顺序：`Enabled` 为真才 `Disable("ENTRY_EXCEPTION")`（包 try/catch，
  失败打 `ENTRY_INIT_FAILED disable_failed <类型>`）→ 若 `_enabledEntry.Value` 为真则回写 `false`（包 try/catch，
  失败打 `ENTRY_CFG_RESET_FAILED`）→ `Refuse(ENTRY_INIT_FAILED, …)`。`Refuse`（:202-206）= `RoleLogger.Error("REFUSED "+code)` + `return false`，
  **用的是既有词表**，没有新造错误码。
- **"cfg=false 但 hook 仍挂"是否可达**：`Disable`（`Bug1ProbeRuntime.cs:207-237`）先 `EndStep`（内层 try/catch）→
  `Enabled=false`（:218）→ `BoundCamera(false)`（内部自带 catch）→ `Uninstall()`（`:222`，自身 try/catch，`UnpatchOne` 逐条幂等）。
  从 `Enabled=false` 到 `Uninstall` 之间只有无条件赋值与自带 catch 的调用，**不存在"抛在两者之间"的路径**；
  即使 `Uninstall` 抛，也已 `Enabled=false` 且 hook 状态被记录为 `UNINSTALL_FAILED`（可观测）。
- **"cfg=true 残留"**：`AbortStart` 只要 cfg 为真就回写 false（:128），且 `TryStart`/`OnEnabledChanged` 的 catch 都走它。
- **Bug1-49 钉的不是"有没有 try/catch"**：它同时断言 `CountExceptionHandlers(TryStart) >= 1` **与**
  `CountMethodCalls(TryStart, "AbortStart") >= 1`，外加 `OnEnabledChanged` 的 handler ≥1。两条变异分别打掉这两个点：
  - M-C（保留 handler，删掉 `return AbortStart(ex)` 改成 `return false`）→ DLL `7eee50db…`，731/734，红 **Bug1-49**
    （"TryStart 的异常分支必须交给 AbortStart"）；
  - M-D（整段 try/catch 换成直调）→ DLL `c02f7cae…`，731/734，红 **Bug1-49**（`handler=0`）。
- **未覆盖**：cfg 回写没有用例（Bug1-48 里 `_enabledEntry` 为 null，只断言内存 `Enabled`）→ R10-04；
  `AbortStart` 在 `SettingChanged` 回调内回写 cfg 会同步再入一次 `OnEnabledChangedCore`（深度 2，当前无害）→ R10-05。

## 6. 回归面与扩张面

- **计数变化**：t48 基线 727/729、Bug1 44/44 → 本轮 732/734、Bug1 49/49。差额 **+5 条**与新增 **Bug1-45…49 五条**
  完全对上；`grep RunTest("Bug1-` 共 **49** 条，ID 无重复（`sort | uniq -c` 全为 1）。
- **无删除/跳过**：`git diff HEAD --numstat` 对 `WhitelistTests/Program.cs` 为 **103/0**、`SteamP2PFriends.csproj` 为 **14/0**
  （HEAD 早于全部 Bug1 注册），即相对 HEAD **零删除**；本轮相较 t48 只多 5 行注册 + 5 个测试方法
  （测试文件 24670 → 34788B）。没有把旧判据放宽或标注 Skip。
- **Bug1-44 不是新增**：它（F4 走/跑两档）在 t48 已注册；本轮新增严格是 45…49。
- **Bug3 五文件**：哈希与 t34/t44 锁定值逐位一致（见 §1），本轮未触碰。
- **扩张面**：`SteamP2PFriends.csproj` 哈希 `8b27ab55…` 与 t50 相同（本轮没改）；`Release/`、`publishi/`、`Build/`
  的 mtime 停在 01:32 / 23:56 / 20:01，**本轮无写入**。改动落在 `Bug1ProbeControl.cs`、`Bug1ProbeEntryLogic.cs`、
  测试文件、`Program.cs` 四个文件（加一个窗口外的旧 `Build/Version.props` 发布票改动）。

## 7. 变异（仓库外副本 `../_t53mut`，已删除；副本首次重建复现 `683c25e2…`）

| 变异 | 改动点 | 变异后 DLL sha256 | 结果 | 红名单 |
| --- | --- | --- | --- | --- |
| **M-A** | 删 `EnablePreparedRun` 内 `DiscardNewRunDirectory(...)`（`Control.cs:336`） | `45e3e7f9442c49bc138df6b064ed7b67c1a23df433eb45b95f8cfdeddbccd02d` | 731/734 | **Bug1-45**（`<runId>/HOST 不得残留`） |
| **M-B1** | 记账改回内联、删掉 `AccountRetryAttempt` 调用（`:390-391`） | `ab1241582cd57e480ae4812e861ac29574e25ce44fa61aad137e6b7aad3231cb` | 731/734 | **Bug1-47**（`实际=0`） |
| **M-B2** | 保留调用但忽略返回值（`_startupRetries = _startupRetries + 1`） | `1015e05eabcee01012f69ff8a5271eaf8a140472e4604f1936a21a3056217123` | 732/734 | 无（语义近似等价，可接受） |
| **M-B3** | 保留两个调用，摘钩改成永不执行（`if (outcome.Unhook && TickCount==int.MinValue)`） | `1c2d9a197a0160093f0702eca98790c6e13c06427352e2792badd053dfb00246` | 732/734 | **无 → R10-01** |
| **M-C** | 保留 handler、`return AbortStart(ex)` 改 `return false` | `7eee50db3beb7f739ede4e36f6ebdf8c805b9dbdcf76fa3653e229c5479fe0d5` | 731/734 | **Bug1-49**（AbortStart 链路） |
| **M-D** | `TryStart` 整段 try/catch 换成直调 `TryStartCore` | `c02f7caea0832ea23570c9c79839e47e3cb8a9c323be6b25477be2d0d2e0e8f2` | 731/734 | **Bug1-49**（`handler=0`） |

还原：`cp` 回主树原始 `Bug1ProbeControl.cs` → 副本重建 DLL = `683c25e24825e073fdd2673bd5d70f77842920392dd1dfaa2c3e0dcab8232da5`
（与主树逐字节一致），源文件回到 `5f6738cf…`；副本已 `rm -rf`。变异只在副本内跑，主树全程未被写。

与 t51 自报比对：M-F（内联）红 Bug1-47、M-G（去 try/catch）红 Bug1-49，与我实测一致；M-E 我的实现是"注释掉调用"
（哈希 `45e3e7f9…`，与自报 `4a967fa2…` 不同属预期：改动文本不同导致 IL 不同），红名单一致（Bug1-45）。
**未采信、也不采信**自述：本轮所有哈希与红名单都是我重跑得出的。

## 8. Findings

### R10-01（medium）R9-01 的接线守卫只钉"调用存在"，钉不住"结果被采纳"

- 位置：`Platform/Diagnostics/Patches/Bug1ProbeControl.cs:390-397`（应用点）、
  `WhitelistTests/Evidence/Runtime/Bug1ProbeEntryRepairTests.cs:493-506`（守门用例）。
- 证据：**M-B3** 保留 `AccountRetryAttempt` 与 `UnhookStartupRetry` 两个调用，只把应用条件改成永不成立
  （`if (outcome.Unhook && Environment.TickCount == int.MinValue)`），重建 DLL `1c2d9a19…` →
  **732/734、Bug1 49/49 全绿**。语义后果是"3 次后不摘钩，之后每次关卡加载/玩家进出都再试一次"，
  正是 R9-01 当初要消灭的那类回归，却没有任何用例会红。
- 影响面：不是当下缺陷（现树的行为正确），而是**守卫强度不足**。t51 报告 §8 第 2 条把"钩子已摘"写成
  "已自动化可证"，这句过头了——自动化只证明了纯函数规则与调用存在，没证明应用。
- requiredFix（最小）：把"应用"也做成可钉的形状二选一——
  ①把 `if (outcome.Unhook) UnhookStartupRetry(); _startupRetries = outcome.AttemptsUsed;` 抽成
  `internal static void ApplyRetryOutcome(ref int used, ref bool hooked, Bug1RetryOutcome outcome)`，用
  一条纯行为用例断言 `Unhook=true → hooked==false`、`used` 被采纳；
  ②或给 `TryRetryStartup` 加 `__TestRetrySeam`，注入"返回 Unhook=true 的 outcome"并断言 `_retryHooked==false`。
  同时把 §8 第 2 条的表述降到"纯函数规则 + 调用接线可证；应用语义靠代码审阅"。

### R10-02（medium）T49-F1 只覆盖 2/5 输入，误删防护没有回归守门

- 位置：`WhitelistTests/Evidence/Runtime/Bug1ProbeEntryRepairTests.cs:375-417`（Bug1-45）。
- 事实：用例只覆盖"新建两级目录 + Enable 失败 → 都删"与"成功 → 都保留"。契约点名的另外三种输入
  ——**已存在同名 runId**、**目录非空**、**被占用/只读**、以及大小写差异——没有一条断言。
  我用反射探针在真实 DLL 上验证了这几种行为**是对的**（§3.2 S1/S2/S3），但这是本次人工结论，
  下次改坏不会被测试抓住（例如把 `GetFileSystemEntries.Length == 0` 去掉，只留 `Directory.Delete(path,false)`，
  非空目录会抛异常被吞——行为仍"安全"，但若改成 `true` 递归删除则**没有任何用例会红**，只能靠 grep）。
- requiredFix：给 Bug1-45 追加三段：①预置 `<runId>` 及文件、`created=true,true` 调用 → 断言目录仍在；
  ②把 HOST 目录设为只读（或打开独占句柄）→ 断言不抛且保留；③用大小写变体路径 + `created=false` → 断言不删。
  另加一条 grep 级别的静态断言（`Directory.Delete(..., true)` 零命中）更省事。

### R10-03（low）Enable 抛异常（而非返回 false）时目录不回滚

- 位置：`Platform/Diagnostics/Patches/Bug1ProbeControl.cs:328-339`。
- 事实：`EnablePreparedRun` 只在 `Bug1ProbeRuntime.Enable(config)` **返回 false** 时调 `DiscardNewRunDirectory`；
  若 `Enable` 抛异常（`_window = new Bug1StepWindow{...}`、未来新增代码），异常穿到 `TryStart` 的 catch →
  `AbortStart`（不带 outputRoot 参数）→ **本次新建的两级空目录留在磁盘上**，T49-F1 的回滚失效。
- requiredFix：`EnablePreparedRun` 内 `try { ... } catch { DiscardNewRunDirectory(...); throw; }`，或 catch 内 Discard 后
  转成 `error=INSTALL_FAILED:<Type>; return false`（与现有词表一致）。

### R10-04（low）AbortStart 的 cfg 回写没有用例

- 位置：`Bug1ProbeControl.cs:126-133`；用例 `Bug1ProbeEntryRepairTests.cs:511-544`。
- 事实：Bug1-48 走 `__TestOffline(true)`，`_enabledEntry` 为 null，`AbortStart` 的 cfg 分支被短路，
  用例只断言内存 `Enabled==false`。"cfg 的 Enable 真回写 false"目前只有代码审阅支撑。
- requiredFix：把 cfg 回写抽成 `internal static void ResetEnabledEntry(Func<bool> get, Action<bool> set)`（或注入桩），
  用一条用例断言"调用后 set 收到 false；原本为 false 时不再写"。

### R10-05（low）AbortStart 在 SettingChanged 回调内回写 cfg 会同步再入

- 位置：`Bug1ProbeControl.cs:63-74`（catch）与 `:128`（写 Value）。
- 事实：BepInEx 在赋值时同步触发 `SettingChanged` → 嵌套进入 `OnEnabledChangedCore`（深度 2，走"关闭"分支，
  因 `Enabled` 已被 `Disable` 置 false 而不重复动作）。当前无害，但依赖 BepInEx 的事件语义；若将来在
  `OnEnabledChangedCore` 里加"打开"副作用，这个再入会变成真实的双执行。
- requiredFix：加 `static bool _suppressHandler`，`AbortStart` 写 cfg 前后置位/复位。

### R10-06（low）`Bug1RetryOutcome.Reason` 是装饰字段

- 位置：`Bug1ProbeEntryLogic.cs:23`、`:126`；消费点 `Bug1ProbeControl.cs:392-396`。
- 事实：`TryRetryStartup` 只读 `AttemptsUsed/Unhook/GiveUp`，`Reason` 从未被记录；且 `failureCode` 为 null 时
  它被赋成空串，与"原因码"约定不符。字段存在但无消费者，容易被误读为"日志里会带原因"。
- requiredFix：要么在 `RETRY_GIVEUP` 日志里带上 `outcome.Reason`，要么删掉字段。

### R10-07（low）`Path.GetDirectoryName` 推断 run 目录对尾分隔符不健壮

- 位置：`Bug1ProbeControl.cs:311`。
- 事实：`outputRoot` 若带尾分隔符（`...\HOST\`），`GetDirectoryName` 返回 `...\HOST` 而非 `...\<runId>`，
  第二次删除会作用在 HOST 上、`createdRunDirectory` 语义错配。生产路径是 `Path.Combine(..., "HOST")` 无尾分隔符，
  当前不可达；但该方法是 internal、测试可直接调。
- requiredFix：`string runDirectory = Path.GetDirectoryName(outputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));`
  或直接由调用方把 runDirectory 传进来。

## 9. 诚实边界：本票 pass 的含义

**pass ≠ 仪表可交付；pass ≠ Bug1 已诊断。** 本票只证明"开启入口的三处修复在离线可复现且删掉会红"。
以下三项仍是实机 UNKNOWN，t51 报告 §8 已如实列出，本复审确认**没有被抹平**：

1. **Mono 下 BCL `Queue<PlayerInputPacket>` 的 Enqueue/Dequeue detour 是否真触发**——离线能装（`patch=ok verify=True`），
   不代表 Unturned 的 `serversidePackets` 实例真的走到；要看实机 `trace.jsonl` 有没有 `AFTER_ENQUEUE/AFTER_DEQUEUE`。
2. **启动重试的真实时序**——`Level.onLevelLoaded` / `Player.onPlayerCreated` 的先后、`Provider.isServer` 的就绪时刻，
   离线只能证明判定与记账正确、接线存在。
3. **`isDismissed` 是否存在**——反射读取，实机需确认成员存在；缺失时走 unknown 分支。

另：离线进程 5 条 Assembly-CSharp 原生 patch 一律 `IL Compile Error`，属测试环境限制（拿不到原生 IL），
生产侧是否全部装上必须 1H 实机核对（`spf_hook_errors`）。

报告 §8 的过头表述只有一处（R10-01 已列）："钩子已摘"被写成已自动化可证，实际只到"调用存在"。

## 10. 合同 acceptance 逐条判定

| # | 条目 | 判定 | 依据 |
| --- | --- | --- | --- |
| 1 | T49-F1 逐行验证非递归 + 误删防护（五种输入） | **pass** | §3.1 逐行 + §3.2 探针实测 S1/S2/S3；`Delete(...,true)` 0 命中；覆盖缺口记 R10-02 |
| 2 | R9-01 `AccountRetryAttempt` 纯函数且四态完备；自跑内联变异使 Bug1-47 红 | **pass** | §4；纯函数无静态依赖；M-B1 红 Bug1-47；应用语义缺口记 R10-01 |
| 3 | R9-02 异常路径 cfg 与 Enabled 一致、hook 已卸；Bug1-49 钉 AbortStart 而非仅 try/catch | **pass** | §5；M-C 单独打掉 AbortStart 调用即红；cfg 回写无测试记 R10-04 |
| 4 | 回归面：732/734 与 49/49 是新增而非放宽；Bug3 未变；Build/Release/publishi 未写 | **pass** | §6；+5 与 Bug1-45…49 对齐；diff 103/0、14/0；mtime 全部停在窗口前 |
| 5 | 至少三条变异、各给 DLL sha256 与红名单、副本内逐字节还原 | **pass** | §7 六条变异全给全量 sha256；还原回 `683c25e2…`；副本已删 |
| 6 | 明确"pass ≠ 可交付 ≠ 已诊断"，三项 UNKNOWN 不被抹平 | **pass** | §9 |

## 11. 复现命令

```bash
# 基线（主树，只读）
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe        # 732/734

# 变异（仓库外副本；tar 复制后先重建，确认 683c25e2…）
cd .. && mkdir _t53mut && tar -C SteamP2PFriends-0.2.4-Experimental \
  --exclude=.git --exclude=.scratch --exclude=bin --exclude=obj --exclude=Release \
  --exclude=publishi --exclude=Assets --exclude=.agent-teams -cf - . | tar -C _t53mut -xf -
cd _t53mut && dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild \
  && dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release
python /d/tmp/mut53.py A|B1|B2      # 或 mut53b3.py B3
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe
cp /d/tmp/t53-pristine.cs Platform/Diagnostics/Patches/Bug1ProbeControl.cs   # 逐字节还原
```

探针（§3.2）用 PowerShell 反射调 `Bug1ProbeControl.DiscardNewRunDirectory(string,bool,bool)`，
脚本在 `bin/Release/probe53c.ps1`（审查后已删除，逻辑见 §3.2 表格）。

## 12. 全量哈希（本报告引用值）

```
5f6738cfa02e32dd0378cbcc3ea59259ef0f95415ae1ebfc24791da018635400  Bug1ProbeControl.cs        22068
04a4beeb058849b10788194eac19fd073268ad22375f44020d24183f5dad62fc  Bug1ProbeEntryLogic.cs      6565
17ca472b6e86615b9653207c00c59cb20ef2fc934d559d5f8e2a146e5eb4e48d  Bug1ProbeRuntime.cs        51502
22308d77197af4e58a34c8625f813e862783eebc06c3f5b129aaf29b28dd83d6  Bug1ProbePatch.cs          11707
b716369fac0f4bace41f9a86297755b22d665fd7b64e7382e25fa9feb9d06da8  Bug1ProbeTargetSelector.cs  6682
f6de7455ca93dae127018d24da453198750b305b52f9b5e0b069a903cf5d06aa  Bug1ProbeEntryRepairTests.cs 34788
eb1287f8a42d20acf82564cffdeefe1f17a3b8f6034db0e82c8e180a25d7d2b0  WhitelistTests/Program.cs  140251
8b27ab5587b2656e0153cff2d2577c79d8d20bc2d5a5d544aadc572c732dd89e  SteamP2PFriends.csproj      27492
683c25e24825e073fdd2673bd5d70f77842920392dd1dfaa2c3e0dcab8232da5  bin/Release/SteamP2PFriends.dll 1482240
```

<!-- self_sha256(body)=7056a06b14ee4aeb5492beadb98663af8771d5808b7179e46970f4ef5a4c0e06 -->
