# T58 收口接线守卫：让「返回值被丢弃」「outcome 被整体丢弃」两类回归必红

- 票号：t58（repair，attempt 2 / `37a970a9-369b-449d-bb0d-857b8a7bd1a9`）
- 被修：t56 工程轴 `review-eng-t54-wiring-r5.md` 的 **R12-01（high）/ R12-02（medium）**
- 范围：**仅测试侧**（`WhitelistTests/Evidence/StaticIL/IlContractProbe.cs`、`WhitelistTests/Evidence/Runtime/Bug1ProbeEntryRepairTests.cs`、`WhitelistTests/Program.cs`）
- `spec_delta=not_rerun`：本票不新增/不修改任何需求口径；规格结论继承 `review-spec-t54-r4.md`（t57 §3 的 `TEST_STRENGTHEN_ONLY` 口径：只加强测试，不改生产行为与需求）。
- 生产零字节：`Core/` 与 `Platform/` 全部 `.cs` 聚合哈希开工=收工=`11935e2b…`；`Bug1ProbeControl.cs` 仍 23724B / `53a6c36b…`；`SteamP2PFriends.csproj` 仍 `8b27ab55…`；`bin/Release/SteamP2PFriends.dll` 仍 `9fe6a0c8…`。

## 1. 问题复述（t56 判的两条假绿）

| 缺口 | 位置 | 旧守卫 | 变异 | t56 时结果 |
| --- | --- | --- | --- | --- |
| R12-01 | `Bug1ProbeControl.cs:185-186` | Bug1-50 只断言 `TryStartCore` 调过 `EnablePreparedRun` | M6：保留调用、返回值不参与判定 | 735/737 全绿 |
| R12-02 | `Bug1ProbeControl.cs:395` | Bug1-51 只断言 `TryRetryStartup` 调过 `ApplyRetryOutcome`（行为断言自带 outcome） | M7：`ApplyRetryOutcome(new Bug1RetryOutcome())` | 735/737 全绿 |

本票把判据从「调用存在」升级为「**结果被消费**」，并保留旧守卫不动（分工见 §4）。

## 2. R12-01：`EnablePreparedRun` 的返回值必须参与条件判定

新增探针（`IlContractProbe.cs`）：

```csharp
internal static int CountCallsWithBranchOnResult(
    MethodBase method, string declaringTypeName, string methodName, int lookahead)
```

判据（结构判据，不做字段计数）：

1. 找到目标 `call` 指令；
2. 从其后逐条跟踪该返回值（跳过 `nop`）：
   - 直接遇到**条件分支**（`OpCode.FlowControl == Cond_Branch`，即 `brtrue/brfalse` 等）→ 采纳；
   - 先 `stloc L` 暂存、随后 `ldloc L` 再条件分支 → 采纳（容纳编译器临时变量形态）；
   - 先遇到 `pop`（丢弃）、或任何其它形态 → 判未采纳（fail-closed）。

**`lookahead=8` 的取值理由**：规范写法 `if (!Call(...))` 在 `call` 之后**第 1 条**指令即分支；
允许 8 条只为容纳 `stloc/ldloc` 暂存与 `nop` 填充，再远就不再是「该调用结果的即时控制决策」。
**漏判情形（如实登记）**：若编译器把结果先写入字段、经其它方法中转后再判定，本判据会漏判——
但方向是**判红**（fail-closed），不会放过回归。

**探针自检（防空转）**：用例内用两个本地样本证明判据真的有判别力，而不是恒真：

- `ProbeDiscardResult()`（`ProbeCallee(true); return true;` → `call; pop`）必须数出 **0**；
- `ProbeConsumeResult()`（`if (!ProbeCallee(true)) return false;`）必须数出 **1**。

## 3. R12-02：`ApplyRetryOutcome` 的实参必须来自记账结果

新增探针：

```csharp
internal static int CountCallsWithArgumentFromProducer(
    MethodBase method, string calleeType, string calleeName, string producerType, string producerName)
```

判据：目标 `call` 之前**第一条非 `nop`** 指令必须是

- 对 producer 的调用（`call AccountRetryAttempt` 结果直接留在栈上），或
- 对某局部变量的 `ldloc`，且该变量最近一次 `stloc` 的前一条非 `nop` 指令是对 producer 的调用。

其余形态（`newobj` / `initobj` / 字面量 / 其它调用）一律不计入（fail-closed）。
因此 `ApplyRetryOutcome(new Bug1RetryOutcome())`（全零 outcome）必红。

**探针自检**：`ProbeFed()`（`int v = ProbeProduce(); ProbeConsume(v);`）必须数出 **1**；
`ProbeDefaultFed()`（`ProbeConsume(0);`）必须数出 **0**。

## 4. 新增用例与分工（`WhitelistTests/Program.cs` 注册为 Bug1-53 / Bug1-54）

| 用例 | 断言 | 负责的回归 |
| --- | --- | --- |
| Bug1-53 `Test_R12_01_EnablePreparedRunResultIsConsumed` | `CountCallsWithBranchOnResult(TryStartCore, Control, "EnablePreparedRun", 8) >= 1` + 两个探针自检 | M6（保留调用、丢弃返回值） |
| Bug1-54 `Test_R12_02_RetryOutcomeArgumentComesFromAccounting` | `CountCallsWithArgumentFromProducer(TryRetryStartup, Control, "ApplyRetryOutcome", EntryLogic, "AccountRetryAttempt") >= 1` + 两个探针自检 | M7（outcome 换成全零默认值） |

**分工说明（避免红名单互相串味）**：两个新守卫只在「调用点存在」时判结果是否被采纳；
调用点整体缺失（M1 直连 `Runtime.Enable`、M4 不调 `ApplyRetryOutcome`）由既有 Bug1-50 / Bug1-51 负责。
于是每条变异只红它对应的那条用例（§5 表实测：七条变异各红 1 条 + 2 条既有环境失败 = 3 条 FAIL）。

## 5. 变异证据（仓库外副本 `../_t58mut`、`../_t58rev`，结束后均已 `rm -rf`）

基线（副本首建，与主树逐字节一致）：`bin/Release/SteamP2PFriends.dll` = `9fe6a0c8…`，
`Bug1ProbeControl.cs` = `53a6c36b…`，exe = `737/739 PASS`。

| 变异 | 改法（`Bug1ProbeControl.cs`） | 变异后 DLL sha256 | 红名单 | 结果 |
| --- | --- | --- | --- | --- |
| **M6**（R12-01） | `:185` 保留调用、返回值只用于日志（`call; stloc`，无分支消费） | `f1447cebf5959a275f400eec3d519f6365d65cd7f0651399da056450bd9a760e` | **Bug1-53** | 736/739（Bug1-50 仍绿） |
| **M6′**（复现审查官写法） | `EnablePreparedRun(...);` 语句调用 + `if (Environment.TickCount == int.MinValue) return Refuse(...)` | `43267b3648acfdabf13c4bf1b01bdb471f0a8b4e06e8c90529c97dac680ad54d` | **Bug1-53** | 736/739（Bug1-50 仍绿） |
| **M7**（R12-02） | `:395` `ApplyRetryOutcome(new Bug1RetryOutcome())`（保留 `outcome` 使用以免 CS0219） | `5bbca5b72aa90b3662b84bff4794eac1e4ed05bc19be7339aba43f20f9cbb193` | **Bug1-54** | 736/739（Bug1-51 仍绿） |
| **M7′**（逐字节复现审查官 M7） | 同上，`outcome` 不再使用 | `8d8f138eea221ce45e581c769027c94cd8ed5ad7d4b504199e8b3366d0b15a28` | **Bug1-54** | 736/739 |
| M1（回归） | `TryStartCore` 直连 `Bug1ProbeRuntime.Enable`，绕过 `EnablePreparedRun` | `bd9485b3085e66d302c414ecfb5a501dbfdf40cf37c52038df1840fad14b2cee` | Bug1-50 | 736/739 |
| M2（回归） | `if (false && outcome.Unhook) UnhookStartupRetry();` | `c25ee3afce7c4ef8d740bc8c810116127f0400358d7acc05a3d81f42a2123499` | Bug1-51 | 736/739 |
| M3（回归） | `if (outcome.Unhook) { }`（保留调用但永不摘钩） | `767a13b79f132b45512f488dd3092f7fa09e7406d05ae5fa4a9bc23223411e03` | Bug1-51 | 736/739 |
| M4（回归） | 删 `ApplyRetryOutcome(outcome);` 改 `_startupRetries = outcome.AttemptsUsed;` | `42dede8e3febddf33d3e1f76796c4efb329047ba5b3bf5f3738e1f831d06f86f` | Bug1-51 | 736/739 |
| M5（回归） | 去判空 + `Directory.Delete(path, true)` 递归（HOST 与 run 两级） | `7f1bc5ad85daea05259e384746aabda5ae272734060ed3a06f60701bae7469cc` | Bug1-45 | 736/739 |

失败信息原文（控制台按 GBK 输出，管道里中文显示为 `?`，此处按源码字符串还原）：

- M6 / M6′：`Bug1ProbeRepair: R12-01：EnablePreparedRun 的返回值必须参与条件判定（call 后 8 条内未见 brtrue/brfalse），实际=0`
- M7 / M7′：`Bug1ProbeRepair: R12-02：ApplyRetryOutcome 的实参必须来自 AccountRetryAttempt 的返回值（不得传新建默认值），实际=0`

**跨审查官交叉校验**：M7′ 的 DLL 哈希 `8d8f138e…` 与 `review-eng-t54-wiring-r5.md:83` 记录的审查官 M7 哈希**逐字节相同**，
说明双方变异同源、且本票新守卫确实能抓住它（t56 时该 DLL 为 735/737 全绿）。
M6′ 与审查官 M6（`894319bb…`）行为等价（语句调用 + 与结果无关的分支），DLL 不同只因那条无关分支的表达式文本不同，
无法在不掌握对方原文的情况下逐字节复现——此处如实标注，不冒充。

**还原**：每条变异后从主树 `cp` 回 `Bug1ProbeControl.cs`；七轮结束后副本重建
`bin/Release/SteamP2PFriends.dll` 回到 `9fe6a0c8…`、`Bug1ProbeControl.cs` 回到 `53a6c36b…`，
`diff -r`（排除 `bin/obj/.git/.scratch/Release/publishi`）与主树**零差异**，随后 `rm -rf` 删除两个副本。

## 6. 三命令（主树收工实测）

| # | 命令 | 结果 |
| --- | --- | --- |
| 1 | `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 已成功生成，**0 警告 0 错误**（1.32s） |
| 2 | `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 已成功生成，**4 条既有警告 0 错误** |
| 3 | `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `=== Final Result: 737/739 PASS (Failed: 2) ===` |

- FAIL 仅两条既有环境失败：`[PureMemory] GBA01 Native First Observe Binding`、`[PureMemory] NIL01 Real Native Lookup Contract`（`TypeInitializationException: SDG.Unturned.SteamPlayer` 链，与 t48 起完全一致）。
- Bug1 段 **54/54 PASS**（t54 收工时 52/52，本票 +2）。
- exe 退出码 1 属既有约定（有任意 FAIL 即 1），非本票引入。

## 7. 逐文件 sha256（收工实测）与生产零字节对照

| 文件 | 字节 | sha256 |
| --- | --- | --- |
| `WhitelistTests/Evidence/StaticIL/IlContractProbe.cs` | 21820 | `1e4f94d5f6c7654cfca54bd79740beedf0bf4c27bfd30b85608910b0e5c992fc` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeEntryRepairTests.cs` | 50803 | `028dab2d27b92b33b7cd6e9aa4ec8194b39c039da5fe298b7941d96510a45a69` |
| `WhitelistTests/Program.cs` | 141327 | `989e29d145df932a59564737ddbbe5b0b1906d8d523c810a4a2a940f21f4f987` |

生产侧对照（开工 08:24:30 → 收工 08:43）：

| 对象 | 开工 | 收工 | 结论 |
| --- | --- | --- | --- |
| `Platform/Diagnostics/Patches/Bug1ProbeControl.cs` | 23724B `53a6c36b…` | 23724B `53a6c36b…` | 未变 |
| `SteamP2PFriends.csproj` | `8b27ab55…` | `8b27ab55…` | 未变 |
| `Core/` + `Platform/` 全部 `.cs`（177 个，排序后聚合） | `11935e2b…` | `11935e2b…` | 未变 |
| `bin/Release/SteamP2PFriends.dll` | `9fe6a0c8…` | `9fe6a0c8…` | 未变 |

## 8. 边界核验

- **Bug3 五文件哈希未变**：`a89f40b949f841d0…`（`P2PGroupStateRecoveryPatch.cs`）、`7a82de8e38fa0f6a…`（`GroupStateRecoveryCore.cs`）、`3d5044f7870fbb17…`（`GroupLeaseLedger.cs`）、`a45533850f31bd96…`（`GroupLoadPlanner.cs`）、`5b0c4e9ba4c87cbe…`（`P2PGroupRegistration.cs`）。
- `find Release publishi -newermt "2026-11-07 08:20" -type f` → 无输出（两目录未被写）。
- 主树根目录无新增临时文件；变异副本 `../_t58mut`、`../_t58rev` 已删除。
- `git status --porcelain` 中本票只涉及上述 3 个测试文件（其余改动为 t48/t51/t54 的既有交付）。
- 测试工程用 `<Compile Include="Evidence\**\*.cs" />` 通配，未新增文件、未改 csproj。

## 9. 已自动化可证 vs 仍需 1H+1G 实机（不得因本票 pass 被写成已证）

**本票已自动化可证（限静态结构层面）**：

1. 生产代码里 `EnablePreparedRun` 的返回值参与条件判定（M6/M6′ 必红，且只有 Bug1-53 红）。
2. 生产代码里 `ApplyRetryOutcome` 的实参来自 `AccountRetryAttempt` 返回值（M7/M7′ 必红，M7′ 与审查官 DLL 逐字节一致）。
3. t54 五条既有变异（M1–M5）仍各自只红对应用例——守卫之间没有互相顶替，也没有退化成「调用存在即通过」。

**仍需 1H+1G 实机（四项 UNKNOWN，逐条保留，不得因本票 pass 被写成已证）**：

1. Mono 运行时下 BCL `Queue<PlayerInputPacket>` 的 `Enqueue`/`Dequeue` detour 是否真触发（离线进程只能装 BCL 两条，Assembly-CSharp 五条报 `HarmonyException: IL Compile Error (unknown location)`）。
2. 启动期重试（`level-loaded` / `player-created`）在真实关卡时序下是否命中。
3. `isDismissed` 成员是否存在于目标 MVID。
4. `SettingChanged` 内 `AbortStart` 回写 cfg 造成的同步再入（R10-05）真实行为。

**结论口径**：本票只加强测试，**不构成**「Bug1 仪表可采」或「Bug1 已诊断」的证据；上述四项在实机采集前一律标 UNKNOWN。

## 10. 未改项与口径声明

- `spec_delta=not_rerun`：未新增/修改需求；规格结论继承 `review-spec-t54-r4.md`（t57 §3 `TEST_STRENGTHEN_ONLY`）。
- 生产行为、判定逻辑、日志词表、cfg 键名、`Bug1ProbeControl.cs` 一律未动（§7 已给哈希对照）。
- R10-05（`AbortStart` 在 `SettingChanged` 内回写 cfg 造成同步再入，深度 2，当前无害）按裁决**登记不改**，列入 §9 实机项。
- T49-F2（auto 档遇 CROUCH/PRONE 套用 walk 上限）、T49-F3（探针外六处同类 `playerID == null`）不在本票范围，仍未处理。
- 判据强度说明（对应契约第 5 条）：未使用 `CountFieldLoads` 之类易被常量折叠骗过的弱判据；采用「调用后即时条件分支」与「实参来自 producer 返回值」两种结构判据，`lookahead=8` 的理由与漏判情形见 §2/§3，两处漏判方向均为**判红**（fail-closed）。


