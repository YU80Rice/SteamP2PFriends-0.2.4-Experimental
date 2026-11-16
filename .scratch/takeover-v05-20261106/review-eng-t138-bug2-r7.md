# t138 工程轴复审（r7）：前序失败丢弃是否已可检出、开关改形是否到位

审查官：reviewer-eng（工程轴）· 任务 t139 · 被审 t138 · attempt ccc6947d-b385-44ba-977a-d7e24d120fb4
日期：2026-11-08

## §0 结论

**verdict = needs_revision**（0 blocker / 0 high / 1 medium / 2 low）。

t138 的两条目标——「t137 判为全绿的两条变异现已变红」与「测试参数改内部开关」——**都已达成并被我独立复现**：

- M3 `ok = FoldGunshotOutcome(true, gunshotModuleOk);` → `044f6a2d…`，835/838，唯一新增红 **Bug2-34**；
- M6 `ok = gunshotModuleOk;` → `6094dac3…`，835/838，唯一新增红 **Bug2-34**；
- Bug2-34 覆盖四组真值（prior × module 全组合），且我自造的 B1（只在 `moduleOk=false` 时回填 prior 失败）也被抓到 → 说明不是「只加一条断言盖住」。

**但"前序失败可检出"只证明了测试旗标这一条路，没证明真实前序失败能传播。** 删掉第一个 catch 里的 `ok = false;`（UnityLogBridge 失败不再计入）→ `7db024ac…`，**836/838 全绿**；同理在 fold 前插 `if (!GunshotStageOnlyForTest) ok = true;` → `5317fe04…`，全绿。原因：隔离模式（`GunshotStageOnlyForTest=true`）根本不执行那四段 try/catch，而生产模式（false）从未在任何用例中跑过。故判 needs_revision，修法见 R29-01。

## §1 被审字节（实测，与 Captain 锁逐项一致）

| 文件 | 字节 | 行 | sha256（前16） |
|---|---|---|---|
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs` | 6406 | 160 | `b3c953cd40873624` |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationOrchestration.cs` | 5478 | — | `e9759abe166f4b20` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioRuntime.cs` | 28493 | 718 | `cf1efab2cc1f2f32` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioPatch.cs` | 16414 | 417 | `b90246e61ec015ea` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioControl.cs` | 5254 | 141 | `478d03c7caf876e7` |
| `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs` | 69686 | 1379 | `2e5e129cf894289e` |
| `bin/Release/SteamP2PFriends.dll` | 1588224 | — | `594701a949f3ba48` |

## §2 独立复跑

| 命令 | 退出码 | 结果 |
|---|---|---|
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 | 0 警告 0 错误，DLL 复现 `594701a9`（确定性） |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 0 | 0 错误，4 条既有警告 |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | 1 | **836/838 PASS**，FAIL 仅 `GBA01`/`NIL01`（缺 Unity 运行时），`FAIL [Runtime] Bug2` = 0 |

## §3 变异表

均在仓库外副本 `../_t139mut`（独立重建先复现 `594701a4`），逐条还原后回 `594701a4`，副本已删除。

| # | 文件:行 | 改前原文 | 改后原文 | 变异 DLL | 结果 |
|---|---|---|---|---|---|
| M3 | `DiagnosticsModules.cs:94` | `ok = FoldGunshotOutcome(ok, gunshotModuleOk);` | `ok = FoldGunshotOutcome(true, gunshotModuleOk);` | `044f6a2d73f11896` | 835/838 红 **Bug2-34** |
| M6 | 同上 | 同上 | `ok = gunshotModuleOk;` | `6094dac363b7a50e` | 835/838 红 **Bug2-34** |
| B1 | 同上 | 同上 | `ok = FoldGunshotOutcome(ok, gunshotModuleOk) \|\| (PriorStageFailedForTest && !gunshotModuleOk);` | `422001ecec437304` | 835/838 红 **Bug2-34** |
| **B4** | `:93` 前 | `bool gunshotModuleOk = …` | `if (!GunshotStageOnlyForTest) ok = true;` + 原行 | `5317fe049ad2cb09` | **836/838 全绿** |
| **B5** | `:52` | `                ok = false;`（UnityLogBridge catch 内） | 删除该行 | `7db024accb7de3b1` | **836/838 全绿** |

- B1 证明四组真值里 (priorFail, moduleFail) 这一组确实被断言到（否则 B1 会绿）。
- B4/B5 是同一类：**生产模式下的前序失败传播没有任何用例**，任何让 `ok` 在四个 catch 之后被重置、或删掉某个 `ok = false;` 的改动都不会变红。

## §4 四问逐条

**问 1：M3/M6 是否真变红，且不是靠加一条断言盖住？——是，成立。**
自跑 M3 `044f6a2d…`、M6 `6094dac3…`，均为 835/838、唯一新增红 Bug2-34（`stage must keep prior failure`）。Bug2-34（`GunshotHostAudioTests.cs:879-907`）确有四组：`(prior=false,module=true)→true`、`(prior=true,module=true)→false`、`(prior=true,module=false)→false`、`(prior=false,module=false)→false`；我自造 B1（只在 `moduleOk=false` 时补 prior 失败）仍 835/838 红 Bug2-34，说明 `(prior=true,module=false)` 组也被钉住，不是单断言遮盖。

**问 2：是否仍有绕过路径？——有，且是全绿。**
- **B4**：在 `:93` 前插入 `if (!GunshotStageOnlyForTest) ok = true;` → `5317fe04…`，**836/838 全绿**。即生产模式下把四个前序块的失败结果整体丢弃，测试毫无察觉。
- **B5**：删掉 `:52` UnityLogBridge catch 里的 `ok = false;` → `7db024ac…`，**836/838 全绿**。
根因：隔离模式把 `if (!GunshotStageOnlyForTest)` 干了，四段 try/catch 与 GateProbe 段一次都不执行；而生产模式（走 Orchestration → 真初始化 Unity 桥/探针）在测试里从未运行。于是「前序失败可检出」只覆盖了 `PriorStageFailedForTest` 这条**测试旗标**路径，`ok = false;` 这五处赋值全是无守护语句。这与 t137 的 R28-01 是同一类问题的下一层：从「stageOk 未参与」变成「stageOk 的来源块未被执行」。

**问 3：`GunshotStageOnlyForTest` 改形是否彻底？——是。**
生产签名 `private bool InitializeDiagnosticStage()` 无参（`:37`）；唯一生产调用点 `PatchRegistrationOrchestration.cs:44-46` 以方法组传入（不传参）；`ResetDiagnosticsStageForTest()`（`:119-123`）把 `PriorStageFailedForTest` 与 `GunshotStageOnlyForTest` 双清零；测试用 `AccessTools.Method(..., Type.EmptyTypes)`，不再存在「用 `new[]{typeof(bool)}` 调生产方法」的旧形态。R28-02 关闭。

**问 4：报告是否按「文件:行 + 改前/改后原文 + 变异 DLL sha256」记录？——是。**
`t138-bug2-r7.md` §4 为五列表格（编号 / 文件:行 / 改前原文 / 改后原文 / 变异 DLL sha256 / 结果），§7 列文件 sha256，格式合规、可依文复现。唯一残留：DLL 哈希与 Captain 对不上（见 R29-02），不是格式问题而是替换文本/字节边界未逐字公开。

## §5 findings

| ID | 严重度 | 位置 | 问题 | 修法 |
|---|---|---|---|---|
| **R29-01** | medium | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs:43-106` | 生产模式下四段前序 try/catch 与 GateProbe 段零用例执行；`ok = false;` 五处赋值无任何守护。B4/B5 两个「丢弃前序失败」的变异全绿（836/838）→ 真实的 Unity 桥/探针初始化失败既不会让 stage 失败，也不会被测试发现。t138 声称的「前序失败确定可检出」只在测试旗标路径成立。 | 二选一：①加 `internal static bool UnityLogBridgeThrowForTest;`，在首段 try 内 `if (UnityLogBridgeThrowForTest) throw new InvalidOperationException("test");`，并用**非隔离**模式（`GunshotStageOnlyForTest=false`）跑一次断言 `stage==false`+`_registrationStageFailed==true`；②把「是否曾失败」改成显式累积值并在隔离模式下也能注入（如 `StageResultAccumulator` 纯函数 + 四段各自 `Accumulate(block, ref failed)`），使 B5 这类删除能被单测抓住。 |
| **R29-02** | low | 变异证据 | 我复现的 M3/M6 DLL（`044f6a2d…`/`6094dac3…`）与 Captain 的（`2a7540a0…`/`6818f5ff…`）不同，红名单一致；根因之一是本文件为 **CRLF**、其余 Gunshot 文件为 LF，文本级补丁在 EOL 处理上易漂移（我第一轮用 LF 回写就把源改成了 `1dee0942…`）。 | 报告补充「替换字节范围/是否含行尾」；或统一该文件 EOL 到 LF。 |
| **R29-03** | low | `DiagnosticsModules.cs` 全文件 | 160 行全部 CRLF，与同目录/同模块其余源文件（LF）不一致；会导致按行 patch、diff、逐字节复现三方不一致。 | 统一为 LF（本票或清理票），并在提交说明注明纯 EOL 变更，避免被误读为逻辑改动。 |

## §6 结论边界

- 本轮判「t138 的两条声明已达成、开关改形到位」，**不等于** Bug2 已修复；主机侧枪声补偿的真实听感、1H+2G 三端互听、Mod 自定义音效兼容仍是 UNKNOWN，需实机。
- 本轮未触碰生产代码；所有变异在 `../_t139mut` 外跑，逐条还原并复核 `594701a4`，副本已删除。
- 主树收工哈希与开工一致（`b3c953cd…`/`594701a9…`），无 `*.exact`/`*.orig` 残留。

## §7 复现命令

```bash
# 基线
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe   # 836/838，仅 GBA01/NIL01

# M3（:94）→ 835/838 红 Bug2-34
sed -i 's/ok = FoldGunshotOutcome(ok, gunshotModuleOk);/ok = FoldGunshotOutcome(true, gunshotModuleOk);/' \
  Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs
# M6 → ok = gunshotModuleOk;   /  B4 → 前置 if (!GunshotStageOnlyForTest) ok = true;  /  B5 → 删 :52 的 ok = false;
# 每次：重建生产 → cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/ → 跑 exe → 还原并核 sha256
```

<!-- self_sha256(body)=6b510a1b51edfe8f08847d4f70a3d20171f90fb26e3a0bf5b7ecea2042e5ffc8 -->
