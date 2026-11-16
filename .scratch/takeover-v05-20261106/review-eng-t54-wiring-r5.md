# 工程轴复审 r5：t54（Bug1 仪表接线守卫补钉）

审查员：reviewer-eng。只读判定；变异全部在仓库外副本 `../_t56mut` 完成并已删除，主树字节开工/收尾两次取样一致
（`9fe6a0c8…` / `53a6c36b…`）。

## 0. 结论

**verdict = needs_revision（1 high + 1 medium + 3 low）。**

t54 宣称关闭的四项里，T49-F1 回滚（R10-02 部分）、R10-04（cfg 回写）、R10-01 的"保留调用但永不摘钩"都真的钉住了；
但**它要根治的那个老毛病在更高一层原样复现了**：

| 我构造的变异 | 结果 | 说明 |
| --- | --- | --- |
| **M6**：`EnablePreparedRun(...)` 调用保留、**返回值被丢弃** | **735/737，Bug1 52/52 全绿**（DLL `894319bb…`） | T52-F1 守卫只数调用次数；零 hook 时仍会打 `START_OK`、返回 true —— 即 R8-02 的原始伤害被静默放回 |
| **M7**：`ApplyRetryOutcome(outcome)` → `ApplyRetryOutcome(new Bug1RetryOutcome())` | **735/737，全绿**（DLL `8d8f138e…`） | T52-F2 的 IL 只数"调了没"，行为用例自己构造 outcome 直接调被抽出的方法；计算出来的结果可以被整体丢弃 |

这两条不是当下缺陷（现树行为正确），而是**守卫没覆盖它声称覆盖的回归面**——本队已经因此栽过三次，
这次出现在"补钉票"本身，必须打回。

## 1. 被审字节锁定（本人重新取样，与 t54 报告逐位一致）

| 文件 | 字节 | sha256 |
| --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1ProbeControl.cs` | 23724 | `53a6c36bb809f7fc22a748823fbd2864d4bdc909cef76d39d75fc69cc55d617f` |
| `Platform/Diagnostics/Patches/Bug1ProbeEntryLogic.cs` | 6565 | `04a4beeb058849b10788194eac19fd073268ad22375f44020d24183f5dad62fc`（未动） |
| `Platform/Diagnostics/Patches/Bug1ProbeRuntime.cs` | 51502 | `17ca472b6e86615b9653207c00c59cb20ef2fc934d559d5f8e2a146e5eb4e48d`（未动） |
| `Platform/Diagnostics/Patches/Bug1ProbePatch.cs` | 11707 | `22308d77197af4e58a34c8625f813e862783eebc06c3f5b129aaf29b28dd83d6`（未动） |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeEntryRepairTests.cs` | 45178 | `43f2d28dc4a203c71b8246d0c4198b75d3c8254d4ebb7893a756a0df752de013` |
| `WhitelistTests/Program.cs` | 140887 | `a66c3fb59e6869a7642593a883ccc3581615b861b92fc368c08bd9c9a3911e5d` |
| `SteamP2PFriends.csproj` | 27492 | `8b27ab5587b2656e0153cff2d2577c79d8d20bc2d5a5d544aadc572c732dd89e`（未动） |
| `bin/Release/SteamP2PFriends.dll` | 1482752 | `9fe6a0c8570d2e047a0ae96aa94a831363e3e04f4f5ea12f1093c9dbbcc327e7` |

Bug3 五文件前缀 `7a82de8e / a89f40b9 / 3d5044f7 / a4553385 / 5b0c4e9b` 与 t34/t44/t53 锁定值逐位一致。
`Build/Version.props` 20:01、`Release/0.2.4.11-preview2` 01:32、`publishi/0.2.4.11-preview` 23:56 —— **本票无新写入**。

## 2. 三命令复跑

| 命令 | 退出码 | 结果 |
| --- | --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 | 0 警告 / 0 错误；DLL 复现 `9fe6a0c8…` |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 0 | 0 错误 / 4 条既有警告（CS0219×1、CS0649×3，全部在既有文件） |
| `./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | 1 | **735/737（Failed: 2）**；失败仅 `GBA01`、`NIL01`（`TypeInitializationException SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`，控制台缺 Unity）；Bug1 段 52/52 |

exe 退出码 1 完全由 GBA01/NIL01 造成：`grep "FAIL \[Runtime\] Bug1"` 为 0 行。

## 3. ApplyRetryOutcome 抽提等价性（逐句对照 t53 的 `5f6738cf`）

原（`/d/tmp/t53-pristine.cs:392-397`，t53 绑定的 `5f6738cf` 版本）：

```
_startupRetries = outcome.AttemptsUsed;
if (outcome.GiveUp) { RoleLogger.Warn("[Shared]", Label + " RETRY_GIVEUP attempts=" + _startupRetries); }
if (outcome.Unhook) UnhookStartupRetry();
```

新（`53a6c36b:408-416`）：

```
internal static void ApplyRetryOutcome(Bug1RetryOutcome outcome)
{
    _startupRetries = outcome.AttemptsUsed;                    // ① 同一字段、同一来源
    if (outcome.GiveUp)                                        // ② 同一条件
    { RoleLogger.Warn("[Shared]", Label + " RETRY_GIVEUP attempts=" + _startupRetries); }   //   同一文案，读的是写回后的值
    if (outcome.Unhook) UnhookStartupRetry();                  // ③ 同一条件、同一位置
}
```

调用点（`53a6c36b:389-395`）仍在 `TryRetryStartup` 的同一个 `try` 块内、`AccountRetryAttempt` 之后、`catch (Exception ex)` 之前。
结论：**语句、顺序、条件、日志词逐字一致，等价成立**。唯一差异是多一层调用栈（异常时 `RETRY_FAILED` 日志文本不变，
因为文本只用 `trigger` 与异常类型），不构成 finding。生产调用点 grep：`ApplyRetryOutcome` 仅 `:395` 一处调用。

## 4. 变异表（副本 `../_t56mut`，基线重建 = `9fe6a0c8…`，已删除）

| 变异 | 改动点 | DLL sha256 | 结果 | 命中层 | 红名单 |
| --- | --- | --- | --- | --- | --- |
| **M1**（T52-F1） | `:185-186` 绕过 `EnablePreparedRun` 直连 `Bug1ProbeRuntime.Enable` | `1b79cf3c395a640b3f021e4fd644050f9090f4f1155c53d4d10905bcf295ebd8` | 734/737 | IL | **仅 Bug1-50**（"必须调用 EnablePreparedRun，实际=0"） |
| **M2**（MUT-5） | `:415` `if (false && outcome.Unhook)` | `c25ee3afce7c4ef8d740bc8c810116127f0400358d7acc05a3d81f42a2123499`（与 t54 同哈希） | 734/737 | **行为** | Bug1-51（"Unhook=true 时必须真的摘钩"） |
| **M3**（M-B3，t53 时全绿） | `:415` `if (outcome.Unhook && TickCount==int.MinValue)` | `f1b0c7c4ae1c54fb2b1b2a0d8256aa0f2ea03211e5aecfbbd9fece76a8ee8d67` | 734/737 | **行为** | Bug1-51（同上） |
| **M4**（MUT-6） | `:395` 改回 `_startupRetries = outcome.AttemptsUsed;`（删调用） | `42dede8e3febddf33d3e1f76796c4efb329047ba5b3bf5f3738e1f831d06f86f`（与 t54 同哈希） | 734/737 | **IL** | Bug1-51（"必须把记账结果交给 ApplyRetryOutcome，实际=0"） |
| **M5**（MUT-7） | `:302-303/:315-316` 去空判 + `Delete(path,true)` | `5685efb045f59a68abe6e67e5fcc87cf2b59d8a5022da986da68c79501965515` | 734/737 | 行为 | Bug1-45（"HOST 非空时不得删除 HOST"） |
| **M6**（本轮新发现） | `:185` 保留调用、丢弃返回值（`if (Environment.TickCount==int.MinValue) return Refuse(...)`） | `894319bb32623c9aa7163076f2fc89082aede20d6c9e356d9b8bb3434ecda60d` | **735/737** | **无** | **无 → R12-01** |
| **M7**（本轮新发现） | `:395` `ApplyRetryOutcome(new Bug1RetryOutcome())` | `8d8f138eea221ce45e581c769027c94cd8ed5ad7d4b504199e8b3366d0b15a28` | **735/737** | **无** | **无 → R12-02** |

还原：`cp` 回 t54 字节 → 副本 DLL 回 `9fe6a0c8…`、源回 `53a6c36b…`；副本 `rm -rf`，主树全程未写。
与 t54 自报的差异仅在两处哈希（M1 我的写法多一行 `enableError` 赋值、M3 条件文本不同），红名单完全一致。
t54 的 MUT-5 / MUT-6 哈希与我完全相同（确定性构建），说明对方确实真跑过——这条给它记上。

## 5. 两条新假绿（本轮的核心）

### R12-01（high）T52-F1 只钉"调了没"，钉不住"结果被采纳"——零 hook 的 `START_OK` 可以被静默放回

- 位置：`Platform/Diagnostics/Patches/Bug1ProbeControl.cs:185-186`、用例
  `WhitelistTests/Evidence/Runtime/Bug1ProbeEntryRepairTests.cs:624-636`。
- 变异 M6 保留 `EnablePreparedRun(...)` 调用、只让它的返回值不参与判定 → **735/737、Bug1 52/52 全绿**。
  后果链：`Install` 失败 → `EnablePreparedRun` 内部回滚目录并返回 false，但 `TryStartCore` 照旧打
  `START_OK`、`TryBindTarget`、`UnhookStartupRetry`、`return true`；配置里 `Enable=true`、`Enabled=false`、
  7 个 hook 一个没装 —— **正是 t45 时 R8-02 的原症状**，只是从"Enable 内部不看 Install"搬到了"TryStart 不看 Enable"。
- 为什么现有断言抓不到：Bug1-50 三条都是 `CountMethodCalls`（`prepared>=1`、`direct==0`、`PrepareRun>=1`），
  全部只看调用存在；Bug1-45 只测 `EnablePreparedRun` 自身，不测它的调用者是否尊重返回值。
- requiredFix（最小，二选一）：
  ①给 IL 断言加"返回值被消费"的形状检查——`EnablePreparedRun` 调用之后 3 条指令内必须出现 `brfalse/brtrue`
  （可在 `IlContractProbe` 新增 `CountCallsFollowedByBranch`），M6 即红；
  ②把 `TryStartCore` 的开启段抽成 `internal static bool ApplyStartOutcome(bool enabled, string error, ...)` 并加行为用例，
  由 `__TestInstallSeam = () => false` 驱动到"必须返回 false 且不打印 START_OK"（需要把 `RoleLogger.Info("START_OK")`
  也纳入被抽方法，否则仍只测半段）。

### R12-02（medium）T52-F2 的 IL 只数调用，计算出来的 outcome 可以整体丢弃

- 位置：`Bug1ProbeControl.cs:395`；用例 `Bug1ProbeEntryRepairTests.cs:643-687`。
- 变异 M7 把实参换成 `new Bug1RetryOutcome()`（全零）→ **735/737 全绿**。后果：次数永不增长、永不 `GiveUp`、
  永不摘钩，每次关卡加载都重试一次并打 `RETRY trigger` 日志。
- 为什么抓不到：Bug1-51 的 IL 断言只要求 `TryRetryStartup` 里出现对 `ApplyRetryOutcome` 的调用；
  行为断言直接 `ApplyRetryOutcome(自己构造的 outcome)`，与调用点传了什么无关。
- requiredFix（推荐）：让被抽方法自己吃原始输入——`ApplyRetryOutcome(int used, int max, bool started, string failureCode)`
  在方法内调 `AccountRetryAttempt` 并采纳；IL 断言改为"`TryRetryStartup` 调 `ApplyRetryOutcome` 且不直接调
  `AccountRetryAttempt`"，M7 这类"换实参"变异才会失去落点。

## 6. 测试缝审计（四个新增 internal）

- `__TestArmRetryHook` / `__TestRetryHookArmed` / `__TestStartupRetries` / `__TestResetRetryState`
  （`53a6c36b:419-428`）——生产路径 grep **0 调用**，仅 `Bug1ProbeEntryRepairTests.cs:655-722` 使用（含 finally 复位）。
- 可见性：`internal` + `InternalsVisibleTo("SteamP2PFriends.WhitelistTests")`，外部程序集（含其它插件）无法直接调用；
  **不改变类型的 public 面**（`Bug1ProbeControl` 本身仍是 public static，新增成员是 internal），无 ABI 风险。
- 唯一真实风险：`__TestArmRetryHook` 会把静态 `_retryHooked` 置真而**不订阅事件**；若某条用例在 finally 前抛
  （或未来新增用例忘了复位），下一条用例的 `EnsureStartupRetryHook` 会以为已挂载而跳过订阅。现状靠 `try/finally`
  与 `ResetForTests` 兜住，记为 **R12-04（low）**：建议 `__TestArmRetryHook` 同时记录"仅测试态"标志，
  或在 `Bug1ProbeRuntime.__TestDisable` 一并复位 `_startupRetries/_retryHooked`。

## 7. Bug1-52 的 Activator 构造稳定性

`Test_R10_04_AbortStartWritesBackConfig`（`:693-734`）用真实 `ConfigFile` + `Activator.CreateInstance(..., NonPublic, 4 参)`
构造 `ConfigEntry<bool>`，随后断言：`entry != null`、`Value` 属性存在、构造后为 true、`AbortStart` 后变 false，
`finally` 还原 `_enabledEntry` 并删临时目录。

- **不会静默失效**：BepInEx 若改签名，`Activator` 直接抛 `MissingMethodException`/`TargetInvocationException`，
  `RunTest` 的 catch 计 FAIL，用例变红；`Assert(entry != null)` 只是冗余保险。
- 残留风险（**R12-05 low**）：失败信息会是一条 `TargetInvocationException` 栈，读不出"是 BepInEx 升级导致"。
  建议在 `Activator.CreateInstance` 外包一层 `catch (Exception ex) { Assert(false, "BepInEx ConfigEntry 构造签名变化: " + ex.GetType().Name); }`，
  把环境变更翻译成可读的失败原因。

## 8. R10-02 五输入的判别力

- `Delete(path,true)` 变异（M5）**真红**（"HOST 非空时不得删除 HOST"），且我在两处都改成递归+去空判，仍只红 Bug1-45，无级联。
- 空目录误判为通过？**没有**：①先 `Directory.CreateDirectory(existingRoot)` 再断言存在；②③先建目录+写文件再断言，
  若实现"什么都不删"，③的 `!Directory.Exists(mixedRoot)` 会红。双向都有判别力。
- 覆盖缺口（**R12-03 low**）：合同要求"五输入"，实际覆盖"已存在"（①）、"非空"（②③）两类；**"被占用/只读"与"大小写"两类仍无断言**。
  只读/占用可用"打开独占句柄后调用 `DiscardNewRunDirectory`，断言不抛且目录仍在"补齐（离线可做）。

## 9. 合同 acceptance 逐条

| # | 条目 | 判定 | 依据 |
| --- | --- | --- | --- |
| 1 | 自跑 T52-F1 变异（直连 `Enable`）→ 红且只红 Bug1-50 | **pass** | M1 `1b79cf3c…`，734/737，仅 Bug1-50；还原回 `9fe6a0c8…` |
| 2 | 自跑 T52-F2 三条变异，各自红并说明 IL/行为层 | **pass** | M2 `c25ee3af…`→行为层；M3 `f1b0c7c4…`→行为层（t53 时全绿，现已红）；M4 `42dede8e…`→IL 层 |
| 3 | `ApplyRetryOutcome` 与 `5f6738cf` 原三句逐句对照，顺序/条件变化即 finding | **pass** | §3 三句逐字一致（字段、条件、文案、顺序），无 finding |
| 4 | 四个 internal 测试缝只被测试使用、无外部误用、无 ABI 风险 | **pass**（含 R12-04 low） | 生产 grep 0 调用；`internal` + IVT；不改变 public 面；残留静态污染风险记 R12-04 |
| 5 | Bug1-52 Activator 构造稳定性：升级后不得静默失效 | **pass**（含 R12-05 low） | 签名变化 → 抛异常 → RunTest 计 FAIL；仅失败信息可读性待改 |
| 6 | R10-02 五输入判别力：`Delete(path,true)` 真红、无"目录不存在"空过 | **pass**（含 R12-03 low） | M5 真红；三处均先建目录/文件再断言；占用/只读/大小写仍缺 |
| 7 | 复核三命令与 735/737、Bug1 52/52；exe exit=1 仅 GBA01/NIL01 | **pass** | §2；`FAIL [Runtime] Bug1` 计数 0 |
| 8 | 字节锁与 t54 一致；Bug3 未变；Build/Release/publishi 无新写入 | **pass** | §1 |
| 9 | 假绿反弹检查 | **FAIL** | **R12-01（high）+ R12-02（medium）**：M6/M7 两条"只断言调用存在不验结果"的回归全绿 |
| 10 | 说清能否支撑打包 preview3 | **pass** | §10 |

## 10. 交付判定：能不能打 preview3

**这批字节本身可以进构建产物**——三命令可复现、Bug3 五文件未变、构建确定性、无当下功能性缺陷；
但**不支持把"Bug1 仪表可采"写进 preview3 的交付说明**，理由有三层：

1. 本票 needs_revision：T52-F1/F2 的守卫在"保留调用但丢掉结果"这一层是空的（R12-01/R12-02）。
   修完这两条之前，任何后续重构都可能把"零 hook 却 START_OK"重新放回来而全绿。
2. t54 报告 §9 列的四项仍必须 **1H 实机先行**：Mono 下 BCL `Queue` detour 是否真触发、启动重试真实关卡时序、
   `isDismissed` 是否存在、`SettingChanged` 再入的真实行为。离线只能证明判定与接线，证不了 Unity 里真的走到。
3. 因此建议的交付口径是：**preview3 可打包**（构建与 Bug3 字节稳定），但 release notes 标
   「Bug1 仪表 = 实验性、待 1H 验证」，四项 UNKNOWN 逐条列出；**不得**据此宣称 Bug1 已诊断。

## 11. 复现命令

```bash
# 基线（主树，只读）
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild          # 0/0，9fe6a0c8…
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe     # 735/737

# 变异（副本）
cd .. && mkdir _t56mut && tar -C SteamP2PFriends-0.2.4-Experimental \
  --exclude=.git --exclude=.scratch --exclude=bin --exclude=obj --exclude=Release \
  --exclude=publishi --exclude=Assets --exclude=.agent-teams -cf - . | tar -C _t56mut -xf -
cd _t56mut && dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild \
  && dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release
python /d/tmp/mut56.py backup && python /d/tmp/mut56.py M1|M2|M3|M4|M5|M5b
# M6/M7 为内联脚本（见 §5），改完同样 build → cp DLL → 跑 exe
cp /d/tmp/t56-pristine.cs Platform/Diagnostics/Patches/Bug1ProbeControl.cs
```

## 12. 全量哈希

```
53a6c36bb809f7fc22a748823fbd2864d4bdc909cef76d39d75fc69cc55d617f  Bug1ProbeControl.cs          23724
43f2d28dc4a203c71b8246d0c4198b75d3c8254d4ebb7893a756a0df752de013  Bug1ProbeEntryRepairTests.cs 45178
a66c3fb59e6869a7642593a883ccc3581615b861b92fc368c08bd9c9a3911e5d  WhitelistTests/Program.cs    140887
9fe6a0c8570d2e047a0ae96aa94a831363e3e04f4f5ea12f1093c9dbbcc327e7  bin/Release/SteamP2PFriends.dll 1482752
```


<!-- self_sha256(body)=01f1c0c4c0e0cb107a4f4963879719afa03bc094e001e96c3d52e4a38f4b3ce3 -->
