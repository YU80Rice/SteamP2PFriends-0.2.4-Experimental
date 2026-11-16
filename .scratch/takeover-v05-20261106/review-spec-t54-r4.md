# 规格轴复审 r4（t55 / 被审 t54）：Bug1 仪表接线守卫补钉

- 审查者：reviewer-spec（规格轴，独立实例）
- 被审：t54（T52-F1、T52-F2、R10-01、R10-02、R10-04 合并修复；ds-probe2）
- 审查时间：2026-11-07 06:10–08:15
- 判定：**pass**（1 条 low finding 不阻塞，见 §6）
- 边界：本判定只对 §1 列出的字节有效；pass ≠ Bug1 端到端可用 ≠ 可出包（见 §8）

---

## 0. 结论摘要

| # | 验收判据 | 判定 | 承重证据 |
|---|---|---|---|
| a | `TryStartCore` 必须经 `EnablePreparedRun`，且该接线可被 IL 断言证伪 | **pass** | `Bug1ProbeControl.cs:185`；Bug1-50；MUT-F1 → 734/737 唯一红 Bug1-50 |
| b | 记账结果必须被采纳（Unhook/GiveUp/次数），且可被 IL + 行为断言证伪 | **pass** | `:395` → `ApplyRetryOutcome:408-416`；Bug1-51；MUT-F2a/b/c 均唯一红 Bug1-51 |
| c | R10-02：只删「本次新建且为空」的目录，非递归，认不出的目录保留 | **pass** | `PrepareRun:266-275`、`DiscardNewRunDirectory:295-322`；Bug1-45 三条输入；MUT-R10-02 唯一红 Bug1-45 |
| d | Bug1-46 注释降级为「只覆盖纯函数、不证明生产接线」 | **pass** | `Bug1ProbeEntryRepairTests.cs:500-503` 原文 |
| e | 无 Uninstall/PhaseCoverage/任何 26000B 可执行产物改动 | **pass** | §1 未动项哈希；产物目录 mtime 全部早于 06:00 |
| f | 第一阶段 UNKNOWN 四项保留并写明归属 | **pass** | t54 报告 §9 四项；Runtime/Judge/NativeSpeed 哈希未变 |
| g | 判定绑定每个被审文件 sha256 | **pass** | §1 |
| h | 残留项归属（本轮/后续/真机） | **pass** | §7；§6 给出 T55-F1 归属 |

关键事实：t54 只改 **3 个文件**（Control.cs、Bug1ProbeEntryRepairTests.cs、Program.cs），
`R10-01` 是**逐字搬移**（§4 有 diff 证据），不是语义改写。

---

## 1. 被审字节绑定

主树实测（`sha256sum`，2026-11-07 06:10 取样；改动文件中途未再变动）：

| 文件 | 字节 | 行 | sha256 |
|---|---:|---:|---|
| `Platform/Diagnostics/Patches/Bug1ProbeControl.cs` | 23724 | 456 | `53a6c36bb809f7fc22a748823fbd2864d4bdc909cef76d39d75fc69cc55d617f` |
| `WhitelistTests/Evidence/Runtime/Bug1ProbeEntryRepairTests.cs` | 45178 | 736 | `43f2d28dc4a203c71b8246d0c4198b75d3c8254d4ebb7893a756a0df752de013` |
| `WhitelistTests/Program.cs` | 140887 | 1052 | `a66c3fb59e6869a7642593a883ccc3581615b861b92fc368c08bd9c9a3911e5d` |

未动项（与 t53 轮完全一致）：

| 文件 | 字节/行 | sha256（前 24） |
|---|---|---|
| `Bug1ProbeEntryLogic.cs` | 6565/130 | `04a4beeb058849b10788194e` |
| `Bug1ProbeRuntime.cs` | 51502 | `17ca472b6e86615b9653207c` |
| `Bug1ProbeJudge.cs` | 22193 | `5d863441a4376cf2fb5b6ce0` |
| `Bug1ProbeNativeSpeed.cs` | 7614 | `109e283e5a37323c3c3838b6` |
| `Bug1ProbePatch.cs` | 11707 | `22308d77197af4e58a34c862` |
| `Bug1ProbeRxPhases.cs` | 16693 | `8d0d36fc94cb1d6b8ba98fb9` |
| `SteamP2PFriends.csproj` | 27492 | `8b27ab55…` |
| Bug3 五文件 | — | `a89f40b9…` / `7a82de8e…` / `3d5044f7…` / `a4553385…` / `5b0c4e9b…` |

交付 DLL：`bin/Release/SteamP2PFriends.dll` = `WhitelistTests/bin/Release/SteamP2PFriends.dll`
= 1482752B / `9fe6a0c8570d2e047a0ae96aa94a831363e3e04f4f5ea12f1093c9dbbcc327e7`（mtime 06:22:52），
与 t54 自报一致；本审查官在隔离副本全量重建后可逐字节复现同一哈希（§3 的 M0）。

---

## 2. 逐条 acceptance

### (a) `TryStartCore` 经 `EnablePreparedRun`，可由 IL 断言证伪 —— pass

- 生产调用点：`Bug1ProbeControl.cs:185`
  `if (!EnablePreparedRun(config, outputRoot, createdHostDirectory, createdRunDirectory, out enableError))`；
  失败分支 `:186` 直接 `Refuse(enableError, "运行时拒绝开启")`，成功后才 `:188-194` 写 `START_OK`。
- `EnablePreparedRun:328-339` 内部先 `Bug1ProbeRuntime.Enable(config)`，失败时 `:336` 调
  `DiscardNewRunDirectory(...)`（把 (c) 与 (a) 串成一条路径），`:337` 回传 `Runtime.LastError`。
- 用例 `Test_T52_F1_StartCoreRoutesThroughEnablePreparedRun`（`Bug1ProbeEntryRepairTests.cs:624-636`）
  用真实 IL 解析（`IlContractProbe.CountMethodCalls`，非桩）断言三件事：
  `EnablePreparedRun >= 1`、`Bug1ProbeRuntime.Enable == 0`、`PrepareRun >= 1`。
- 证伪实验 MUT-F1：把 `:185` 换回 t51 的直连 `Bug1ProbeRuntime.Enable(config)` + 自行取 `LastError`
  → 734/737，**唯一新增红 Bug1-50**（`T52-F1：TryStartCore 必须调用 EnablePreparedRun，实际=0`）。
- 结论：判据成立，且失败信息可定位到具体边。

### (b) 记账结果被采纳 —— pass

- 生产路径：`TryRetryStartup:389` 取 `started`，`:390-391` 调
  `Bug1ProbeEntryLogic.AccountRetryAttempt(...)`，`:395` 把结果交给 `ApplyRetryOutcome(outcome)`。
- 采纳实现 `ApplyRetryOutcome:408-416`：`:410` 写回 `_startupRetries`；`:411-414` `GiveUp` 时打
  `RETRY_GIVEUP attempts=`；`:415` `outcome.Unhook` 时真的 `UnhookStartupRetry()`。
- 用例 `Test_T52_F2_RetryOutcomeIsAdopted`（`:643-687`）：IL 断言 `ApplyRetryOutcome >= 1`；
  行为侧直接驱动生产方法两个分支（`AttemptsUsed=2/GiveUp/Unhook` → 次数=2 且钩子已摘；
  `1/false/false` → 次数=1 且仍挂载），finally 复位测试态。
- 证伪实验（三条独立变异，全部唯一红 Bug1-51）：
  - MUT-F2a `if (false && outcome.Unhook) UnhookStartupRetry();`（t53 时全绿的那条）
  - MUT-F2b 删除摘钩语句、保留方法调用
  - MUT-F2c `TryRetryStartup` 不调 `ApplyRetryOutcome`
- 结论：判据成立。**但存在一条更窄的变体仍全绿，见 §6 T55-F1。**

### (c) R10-02：只删本次新建且为空的目录 —— pass

- `PrepareRun:261` 先 `Validate`（`ALREADY_RUNNING` 在建目录前就被拦）；
  `:266-267` 记录两级 `Directory.Exists`，`:268` 建目录，`:274-275` 回填 `createdHostDirectory/createdRunDirectory`。
- `DiscardNewRunDirectory:295-322`：HOST 段 `:302` 同时要求「存在」且「`GetFileSystemEntries().Length == 0`」，
  `:303` `Directory.Delete(outputRoot, false)`（**非递归**），`:307` 任何异常只 `Warn RUN_DIR_CLEANUP_SKIPPED`；
  run 段 `:315-316` 同样的空判 + 非递归删，`:320` 同样 warn；
  `:298`/`:310` 先看 `created*` 标志，不是本次新建的一律不碰。
- 用例 `Test_T49_F1_EnableFailureRemovesNewRunDirectory:375-468` 覆盖失败路径（`INSTALL_FAILED` 前缀、
  两级目录都不存在）与成功路径（目录保留），并补 R10-02 三条输入：
  - ① 已有两级目录、flags 全 false → 一个都不删（`:415-427`）
  - ② HOST 非空、flags 全 true → HOST/文件/run 全部保留（`:429-444`）
  - ③ run 非空、HOST 空 → 空 HOST 被删、run 与用户文件保留（`:446-461`）
- 证伪实验 MUT-R10-02（去掉两处空判 + `Delete(..., false)`→`true` 递归）
  → 734/737，**唯一新增红 Bug1-45**，报错正文 `R10-02：HOST 非空时不得删除 HOST`。
- 结论：判据成立，且「非递归」在失败路径上是被断言守住的，不只是注释。

### (d) Bug1-46 注释降级 —— pass

`Bug1ProbeEntryRepairTests.cs:500-503` 原文写明：

> R9-01：**只覆盖纯函数**（EvaluateStartupRetry + AccountRetryAttempt 的穷举），
> 不证明生产接线真的采纳了结果——生产侧由 Bug1-47（IL 调用存在）与 Bug1-51
> （ApplyRetryOutcome 行为 + IL 绑定）负责。t52 复审指出本用例曾被误读为「接线已验证」，故在此写明边界。

口径与 t52 复审要求一致，未把纯函数用例冒充接线验证。

### (e) 无越界改动 —— pass

- 未动 `Bug1ProbePatch.cs`（Uninstall 幂等实现所在）、`Bug1ProbeRuntime.cs`（PhaseCoverage/计数真值所在）、
  `Bug1ProbeJudge.cs`、`Bug1ProbeNativeSpeed.cs`、`Bug1ProbeRxPhases.cs`、`SteamP2PFriends.csproj`：
  哈希见 §1，与本票开工前一致。
- 未产出任何发布物：`Build/Version.props` mtime 2026-11-06 20:01；`Release/0.2.4.11-preview1` 11-06 23:56、
  `preview2` 11-07 01:32；`publishi/0.2.4.11-preview` 11-06 23:56、`publishi/0.2.4.10` 11-06 17:28 —— 全部早于本轮 06:00。
- Bug3 五文件哈希与 t53 轮一致，未被本轮顺带改动。

### (f) 四项实机 UNKNOWN 保留 —— pass

t54 报告 §9 保留四项（1H+1G 采集才可证）：真实主机上 cfg 热切换、启动重试真实时序、
Mono 下 BCL/原生 detour 是否真触发、R10-05 回写在 ConfigurationManager 下的真实再入行为；
`Runtime/Judge/NativeSpeed` 哈希未变，说明没有靠改判据把这些项「做绿」。

### (g)(h) 绑定与归属 —— pass

见 §1、§7。

---

## 3. 变异矩阵（本审查官实跑）

环境：仓库外副本 `_t55iso`（与主树同字节），脚本 `.scratch/takeover-v05-20261106/_t55_mut.sh`，
日志 `/tmp/t55-mut.log`。每条变异都 `-t:Rebuild` 全量重建 SPF，再重建测试项目、覆盖两处 DLL、跑 exe。

| # | 变异 | 变异后 DLL sha256 | 结果 | 变化 |
|---|---|---|---|---|
| M0 | 无（基线） | `9fe6a0c8570d2e047a0ae96aa94a831363e3e04f4f5ea12f1093c9dbbcc327e7` | 735/737 | 仅 GBA01、NIL01（环境） |
| F1 | `TryStartCore` 直连 `Runtime.Enable` | `6272ebb9deb4fdc02c455bd75647cda269e43c0648dab88c5fdc78344f4c91b9` | 734/737 | +Bug1-50 |
| F2a | `if (false && outcome.Unhook)` | `c25ee3afce7c4ef8d740bc8c810116127f0400358d7acc05a3d81f42a2123499` | 734/737 | +Bug1-51 |
| F2b | 删摘钩语句、保留调用 | `15bff1eacb4ba9522b3fb3aa2403e3fbfac3acff0d513271114c6038cf62ca35` | 734/737 | +Bug1-51 |
| F2c | 不调 `ApplyRetryOutcome` | `51452d4db8ae87176a214dda246059d3911219178707484f27512325469a47d3` | 734/737 | +Bug1-51 |
| **F2d** | **保留调用、传空结果** | `8d8f138eea221ce45e581c769027c94cd8ed5ad7d4b504199e8b3366d0b15a28` | **735/737** | **无（见 §6）** |
| R10-02 | 去空判 + 递归删 | `7f1bc5ad85daea05259e384746aabda5ae272734060ed3a06f60701bae7469cc` | 734/737 | +Bug1-45 |

还原核验：源码回 `53a6c36b…`、DLL 回 `9fe6a0c8…`、735/737、Bug1 段 52 条全 PASS。
交叉核对：F2a 与 t54 自报 MUT-5、R10-02 与 t54 自报 MUT-7 的 DLL 哈希**逐字节相同**，
说明双方变异落在同一处；F1 因我的改写方式与 t54 的 MUT-4 文本不同，DLL 哈希不同属正常，
但红点一致（Bug1-50）。

---

## 4. R10-01 是「原样搬移」的逐字证据

用仍持有 t51 字节（`5f6738cf…`）的 `../spf-t52-iso` 作对照，`diff -u` 结果：

- 唯一的生产改动：把原三句
  `_startupRetries = outcome.AttemptsUsed;`
  `if (outcome.GiveUp) { RoleLogger.Warn("[Shared]", Label + " RETRY_GIVEUP attempts=" + _startupRetries); }`
  `if (outcome.Unhook) UnhookStartupRetry();`
  替换为一行 `ApplyRetryOutcome(outcome);`。
- 新方法 `ApplyRetryOutcome:408-416` 内部三句与原文**逐字相同**（含花括号位置与日志词
  `RETRY_GIVEUP attempts=`），未新增条件分支、未改日志词。
- 其余差异仅为注释与四个 `internal` 测试缝（`:419-428`），测试缝全仓只被
  `Bug1ProbeEntryRepairTests.cs:655-680` 引用，生产 `Platform/`、`Core/` 无调用点。

结论：R10-01 满足「原样搬移、不借机改语义」的裁决要求。

---

## 5. R10-05 登记核验

t54 报告 §1 范围表与 §10 均把 R10-05（`AbortStart` 在 `SettingChanged` 内回写 `_enabledEntry`
造成同步再入，深度 2）标为「登记不改」，并给出当前无害论证（第二次进入时 `Enable=false` 走关闭分支、
`Runtime.Enabled` 已 false）与修法代价（需引入收尾重入标志、会改热切换语义）。
`Bug1ProbeControl.cs:128` 实现与之一致：仅在 `_enabledEntry.Value` 为 true 时回写。
判定：登记符合「不许默默放过」的要求，归属见 §7。

---

## 6. Findings

### T55-F1（low）：`ApplyRetryOutcome` 的**实参**未被断言，传空结果仍全绿

- 位置：`WhitelistTests/Evidence/Runtime/Bug1ProbeEntryRepairTests.cs:643-687`（Bug1-51）；
  生产侧 `Bug1ProbeControl.cs:395`。
- 现象：把 `:395` 改成 `ApplyRetryOutcome(new Bug1RetryOutcome())`（调用保留、记账结果丢弃）
  → DLL `8d8f138e…` → **735/737 全绿**。
- 原因：IL 断言只数「是否调用 `ApplyRetryOutcome`」，行为分支是测试直接调该方法
  （自带正确 outcome），因此「调用点传错参数」这一类回归没有判据。
- 影响：低——需要有人特意把正确实参换成构造空值才会命中，且与验收 (b) 列出的三种变异
  （删调用/删摘钩/短路摘钩）不同类；但本票主题正是「接线回归必须变红」，故记录。
- requiredFix（择一）：① `IlContractProbe` 增加「call 前一条指令是 `ldloc` 同一局部索引」的断言，
  锁死实参来源；② 或给 `TryRetryStartup` 增加可注入的 `TryStart` 委托 seam，
  让用例经生产路径驱动一次 `Unhook=true` 分支并断言钩子被摘（而非直接调 `ApplyRetryOutcome`）。

---

## 7. 残留项归属

| 项 | 归属 |
|---|---|
| T55-F1（实参未断言） | **后续轮可选加固**；不阻塞本轮，因 (b) 的三条必需变异均已变红 |
| R10-05（cfg 回写同步再入） | **需实机**核对日志是否出现成对 `STOP`/`REFUSED`；代码侧已登记 |
| 真实主机 cfg 热切换、启动重试真实时序 | **需真机 1H+1G** |
| Mono 下 BCL/原生 detour 是否真触发、`isDismissed` 是否存在 | **需真机 1H+1G**（沿用 t48/t51 的 UNKNOWN 清单） |
| T49-F2（crouch/prone 上限）、T49-F3（探针外同类判空） | 沿用 t52 判定，仍为后续项，不得因 735/737 视为收口 |

---

## 8. 边界声明

1. 本次审查**只读**：未修改主树任何生产或测试文件；全部变异在仓库外副本 `_t55iso` 完成并已逐字节还原
   （源码回 `53a6c36b…`，DLL 回 `9fe6a0c8…`，735/737）。
2. 本判定**只对 §1 的字节**有效；任一文件字节变化后需重取哈希并重跑 §3 的 M0 与至少一条接线变异。
3. `pass` 的含义是「t54 满足本票 acceptance 与契约」；**不等于** Bug1 仪表在真实主机可用，
   也**不等于** Bug1 根因已定位，更**不等于** 0.2.4.11 可出包——出包仍需 1H+1G 采集与 Captain 核验。
4. Bug3 相关结论不受本票影响：Bug3 五文件本轮未动，其 T1a/T1b 等 UNKNOWN 仍按各自票据追踪。
