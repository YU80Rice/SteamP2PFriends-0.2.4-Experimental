# 规格轴复审 r2：t155（R31-01 残余 / 生产分支五段执行守护）

- 审查对象：t155 交付的字节（t156 / attempt 2 = `be312df3-7034-43d9-8eba-d367cc80e05b`）
- 审查依据：t144 contract（R31-01 原文）、t148 复审报告（F1/F2/F3）、t155 修复报告
- 审查方式：只读主树；全部变异在仓库外副本 `_t156iso` 内完成，用精确字节还原（`RESTORED_matcher=True`），副本已删除
- 未修改主树任何文件；未把「自动化通过」写成「Bug2 可实机验收」

## §1 结论

**verdict = pass（可冻结这条测试守护链）。**

t148 的三条 finding 均已真关闭，且本轮用 10 条变异独立复核（其中 4 条为审查官加测，2 条专门尝试「骗过断言」）：

- 调用点被跳过（t143 形态）→ Bug2-39 红「got 0」；
- wrapper 条件化调用（M6）→ Bug2-39 红「branch-free … branches=1」；
- 自增与委托调用顺序反转 / 删自增 → 计数断言红「got 3 / got 0」；
- 空守卫被关（N1）→ Bug2-40 红「got 5」；
- 生产分支产 null（N3）→ Bug2-38 + Bug2-39 双红；
- 委托被换成 null / 空 lambda / 用 decoy lambda 满足 IL 而真实 wrapper 不执行 → 均被 **Bug2-38** 抓红（「all five sections must be invoked, got 0」）。

两条 low 仅供后续收敛（见 §5），不阻塞冻结。

## §2 被审字节绑定

| 文件 | 字节 | 行 | sha256 |
|---|---|---|---|
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs` | 9467 | 201 | `f99095d7d8e441ec6fdb082e71acd6cd484276265903f530c0cdddf95aef09e0` |
| `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs` | 83120 | 1635 | `7197386ce5dedf2c14331e6b4b101d93a3c4609e2b5bdf98892d36b763870122` |
| `WhitelistTests/Program.cs` | 162273 | 1178 | `fb50a07315affa49a86ae9649a3d158b2068cfa45fa35488b4a05fcbd6c98346` |
| `bin/Release/SteamP2PFriends.dll`（= `WhitelistTests/bin/Release/`） | 1593344 | — | `623b32045685d81f9c565cb15ed235b12395d7f4f1c14d0d612c237a36328db2` |

四项与 t155 报告 §6 **逐项一致**；用主树当前源码 `-t:Rebuild` 重编得到 **623b3204…（逐字节一致）**，故「源码 → DLL」可复现，字节锁成立。

两条与字节有关的事实必须记录（不影响 pass，但影响出包）：

1. **本轮复审窗口内主树 `bin/Release/SteamP2PFriends.dll` 曾短暂是 M6 变异体**（`41b9383c…`，mtime 02:18:58），而源码已在 02:22:33 恢复为 `f99095d7…`；我在 02:21 复制的副本因此带有 M6，构建后复现出 41b9383c 并跑出 Bug2-39 红——这恰好从反面证明 M6 会被抓，但也说明**工作树里可能残留变异构建的 DLL**。当前（02:35）DLL 已回到 `623b3204…`。
2. **DLL 哈希对源码行尾敏感**：同一份代码以 CRLF 保存构建得 `623b3204…`，以 LF 保存构建得 `6489b8fa…`（内容只见行尾差异）。出包/核验必须以「源码 CRLF + 该 DLL 哈希」成对绑定，否则会误判「字节不一致」。

## §3 六条验收逐条判定

| # | 判据 | 结果 | 证据 |
|---|---|---|---|
| 1 | R31-01 真关闭：调用点改 `if (seam != null) a();` 必须有红 | **passed** | M1 → DLL `03ffe6ce…`、844/847，唯一新增红 Bug2-39「production branch (seam == null) must enter all five sections, got 0」 |
| 2 | Bug2-39 断言「真实进入」而非计数自增；拆开自增与调用仍能红 | **passed** | M6 → `41b9383c…`、844/847，Bug2-39「branch-free … branches=1」；M3 → `79382370…`、红「got 3」；M7 → `3726aade…`、红「got 0」；M8（`counted = null`）→ Bug2-38 红；M9（`counted = () => {}`）→ Bug2-38 红「all five sections must be invoked, got 0」；M10（decoy lambda 满足 IL、真实 wrapper 只自增）→ Bug2-38 红同句。**「只计数不执行」这一整类已无处可逃** |
| 3 | 计数器不被测试以外写入污染 | **passed** | 全仓仅三处：声明 `:135`、`ResetDiagnosticsStageForTest` 清零 `:142`、包装体自增 `:160`；读仅测试 `:1007/:1009/:1097/:1099` 与 IL 断言 `:1045/:1061`。Reset 覆盖该字段；进程级累加与 `++` 非原子已在 `:132-134` 注释写明「只会误红」，方向安全 |
| 4 | N1（删空守卫）与 N3（生产分支产 null）现在会被抓 | **passed** | N1 等价形 M4（`if (false && a == null)`）→ `f10a4293…`、844/847，Bug2-40 红「null action must not be counted as entered, got 5」；N3 形 M5（`Action a = null;`）→ `362afa1d…`、843/847，Bug2-38 + Bug2-39 双红 |
| 5 | 基线不掉、Bug2 段全 PASS | **passed** | M0（CRLF，DLL `623b3204…`）→ **845/847**，失败仅 GBA01/NIL01；Bug2-01..40 全 PASS，无新增红 |
| 6 | 字节锁与实测一致；不得声称可实机验收 | **passed** | §2 四项哈希全对；报告与代码注释均写明「只证明进入执行点/委托被无条件调用，不证五段真实动作体效果」，未出现实机可用性结论 |

## §4 变异矩阵（副本 `_t156iso`，每条 `dotnet build -c Release -t:Rebuild` + 覆盖两处 DLL + 全量 exe）

| 变异 | 改动（DiagnosticsModules） | 变异 DLL | 结果 | 新增红 |
|---|---|---|---|---|
| M0 | 无（CRLF 基线） | `623b3204…` | 845/847 | — |
| M1 | `a();` → `if (seam != null) a();` | `03ffe6cef0e051645c332be3f434daa2578f522bcee3b6c60d976102d10d8ed5` | 844/847 | Bug2-39 got 0 |
| M6 | 包装体内 `counted();` → `if (seam != null) counted();` | `41b9383c2695bc51bdca8e4a3136e9374ecb7cee7ec273e3c9db184a27b6ca30` | 844/847 | Bug2-39 branches=1 |
| M3 | 先 `counted();` 后自增 | `793823708641a14d75499f078177e29e3f0684b6232742661917d04ce8d59198` | 844/847 | Bug2-39 got 3 |
| M7 | 删自增 | `3726aade3c368f775ca25814eaefb5fba1914e469794d9702a77e9bb60cb2273` | 844/847 | Bug2-39 got 0 |
| M4 | `if (a == null)` → `if (false && a == null)` | `f10a429349e6c2c41449d9b0bdfd19784df34a03ddd9ac91f80d865960de21fe` | 844/847 | Bug2-40 got 5 |
| M5 | 生产分支 `Action a = null;` | `362afa1d686ae086697b9d1cee51760b9e8f3a56aca701e0ab10093f951dac46` | 843/847 | Bug2-38 + Bug2-39 |
| M8 | `Action counted = null;` | `5aac63a630c86f103b0e3a6ed5bfb4077132a01b29069212b03cca0c2dc8ace6` | 844/847 | Bug2-38「production mode must return true when all five sections run」 |
| M9 | `Action counted = () => { };` | `763068368abf81294f768a8453cd23bf857133c46a9acde7656038f0185d1389` | 844/847 | Bug2-38「all five sections must be invoked, got 0」 |
| M10 | 增加契约形状 decoy lambda，真实 wrapper 只自增不执行 | `5edfb9ce720ea935d6b1eb75d3fdf824824a08f3e1df9a63cd5654e4fc1443fd` | 844/847 | Bug2-38 同句（got 0） |

M6/M3/M7/M4 的变异 DLL 与 t155 报告 §5 表格**逐字节相同**，说明其变异确为所声明的那一处。

## §5 findings（均 low，不阻塞冻结）

- **T156-F1（low）** `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs:1561-1583`：`FindDiagnosticWrapper` 以 `Name.StartsWith("<RunDiagnosticSectionForTest>b__")` 取「第一个」匹配，假设方法内只有一个 lambda。一旦该方法新增第二个 lambda，IL 断言可能指向错误目标（本轮 M10 证明该情形不会造成漏放行，因为 Bug2-38 独立验证真实调用；但 IL 层的通过/失败可能来自错误对象）。requiredFix：先统计候选数并 `Assert(candidates.Count == 1)`，或按「体内同时含 `stsfld DiagnosticSectionsEnteredForTest` 与 `Action.Invoke`」筛选。
- **T156-F2（low）** 出包流程：见 §2 两条（变异构建的 DLL 曾短暂留在主树；DLL 哈希对行尾敏感）。requiredFix：打包前从源码 `-t:Rebuild` 并核对 `623b3204…`；发布 manifest 同时记源码 sha256 与 DLL sha256。

## §6 冻结边界（仍为 UNKNOWN，不得据此对外声称）

1. 计数只证「唯一执行点被调用五次、且 wrapper 无条件调用委托」，**不证五段真实动作体的实际效果**（离线宿主里真实 action 多会抛异常，被既有 catch 折成 ok=false）。
2. 真实 Harmony 命中、`[Diagnostics]` 启动自证行在 BepInEx 下的可见性、实机 1H+2G 听感/次数：均未验证。
3. 报告口径已如实标注，「自动化 845/847」不得写成「Bug2 通过」或「可出包」。

## §7 自证

本报告只对 §2 所列四项字节有效。复审期间主树未被本审查官修改；变异副本 `_t156iso` 已删除，删除前其源码与主树逐字节一致（`RESTORED_matcher=True`）。

- 本文件自证：body 8468 B / sha256 `35af1e030c15e89e7208fbf7f6ae07314b6e4507ef1033f3302d50faefc83073`（末行追加后全文会变；以 body 值为准）。
