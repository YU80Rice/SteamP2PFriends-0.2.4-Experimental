# t148 规格轴复审：R31-01 生产分支计数是否真承重、守护链能否冻结

- 审查官：reviewer-spec（规格轴，round 1）
- 任务：t148（attempt 2 `93029194-f2fd-4fe7-b9ad-aef3242c7589`），被审 t144（Bug2 测试守护链 r10）
- 判定：**needs_revision**——R31-01 的**原始形态**（唯一调用点改成 `if (seam != null) a();`）已被抓住，但计数仍是**自报**：把 wrapper 改成「生产分支只自增、不执行」后 **842/844 全绿**。这条链能掩盖真实运行行为，按票面第 4 条不能冻结。
- 边界：本轮只读审查；变异全部在仓库外副本 `_t148iso` 执行并已删除；主树未被我写入（唯一改动是副本内文件）。未声称 Bug2 可实机听。

## 1. 字节绑定

### 1.1 本轮实际测试的字节（副本冻结于 2026-11-09 01:08）

| 文件 | 字节 | sha256 |
|---|---|---|
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs` | 211 行 / 9254B | `d1d24f23490d6de8cec6c130ae65f148bdf519d9d9e41f48094e36db00d0e19e` |
| `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs` | 1487 行 | `6b49f5436514f9a2e8b503df139add624012f8a2242fab28377419ab2b4f71cd` |
| `WhitelistTests/Program.cs` | — | `f9324bdb6b8d67b1d6d80d653ced56e21971b7a76325aa37e63a8a6cf70a63e3` |
| `bin/Release/SteamP2PFriends.dll`（副本重建） | 1593344B | `43068b2f795a788b9cc8579ba80b22a4c317e2b2139a5b46aa94ffa37040cbf0` |

### 1.2 与 t144 报告 §6 的差异（说明，不计 finding）

t144 §6 记录的是 `2e59cd59…` / `86d369c1…` / `5172d325…` / `8ccfeb27…`。这些与当前树不一致的原因是 **t145 在 00:23 之后修改了同三个文件并重建**（配置合并），属正常的后续批次覆盖，不是 t144 虚报。由于 t144 交付字节不可回溯，本轮以**实测副本**为绑定物；t144 §2.3 引用的 R31-01 代码块与副本中的 `:145-176` 逐字一致。

### 1.3 复审期间主树被并发改动（记录）

- 01:15:35 `PatchRegistrationDiagnosticsModules.cs` 被第三方改写为 199 行 / `0ace2d8a53afc050208a92f8652a124b2d63349225671f077687422766922b82`（diff 显示**整段移除 t145 的启动自证行**，与 R31-01 无关）；01:16:42 `Program.cs` 亦被改写；01:18:33 `bin/Release/SteamP2PFriends.dll` 被重建为 `57065f47…`。
- 已核对：当前 199 行版本仍含 R31-01 全部要素——`:133` 计数声明、`:140` Reset 清零、`:151` null 守卫、`:155-160` `Action counted = a; a = () => { DiagnosticSectionsEnteredForTest++; counted(); }; a();`，与本轮测试版本**逐字相同**。故本轮结论对新字节同样成立；若 Captain 需严格锁定当前字节，重跑 M0/M6 即可（两条命令，约 1 分钟）。

## 2. 逐条验收判定

| # | 验收条 | 判定 | 依据 |
|---|---|---|---|
| 1 | R31-01 真关闭：`if (seam != null) a();` 必须红 | **不通过（部分关闭）** | M1 复现 t143 原变异 → 841/844，**唯一新增红 Bug2-39**，报错「production branch (seam == null) must enter all five sections, got 0」。原始形态确实被抓；但 M6（§3）证明同一类失败仍可全绿，见 F1 |
| 2 | Bug2-39 断言的是「真实生产分支进入过五段」而非计数器自增 | **不通过** | M6 把 wrapper 改成 `count++; if (seam != null) counted();` → **842/844 全绿**，Bug2-38/39 均 PASS。计数器证明的是「唯一调用点被执行」，不是「委托被执行」 |
| 3 | 计数器不被测试以外写入污染；Reset 是否清、并发/重复初始化是否累加 | **通过（附 F3）** | 只读引用：`:1007/:1009` 测试断言；写入仅 `:158` 生产自增与 `:140` Reset；无测试写入。`ResetDiagnosticsStageForTest` 清零 ✓。重复初始化会在进程生命周期内累加（Bug2-39 先 Reset 故不受影响）；`++` 非原子（并发会丢计数→只会误红不会错放行） |
| 4 | N1（删 `if (a == null) throw`）与 N3（生产分支产 null）是否被抓 | **N3 通过 / N1 不通过** | M5（`... : null;`）→ 841/844 红 Bug2-39（计数保持 0）；M4（守卫条件恒假，等价删守卫）→ **842/844 全绿**，无用例覆盖（F2） |
| 5 | 基线不掉、Bug2 段全 PASS | **通过** | 副本基线 **842/844**（失败仅 GBA01/NIL01 既有环境），`grep -c "PASS \[Runtime\] Bug2-"` = **39**，Bug2-01..39 全 PASS |
| 6 | 不得声称 Bug2 已可实机验收 | **通过（本报告口径）** | 本报告只判自动化守护链，结论边界见 §6 |

## 3. 变异矩阵（副本 `_t148iso`，每条 `-t:Rebuild` 生产 + 覆盖两处 DLL + 跑全量 exe）

| 编号 | 变异（文件:行） | 变体 DLL sha256 | 结果 | 红名单 |
|---|---|---|---|---|
| M0 | 基线 | `43068b2f795a…40cbf0` | 842/844 | 无（仅 GBA01/NIL01） |
| M1 | `:176` `a();` → `if (seam != null) a();`（t143 原形态） | `c8897d871077bb3513d1b5aca53f9fa9da390a4744bdeb9ee729b2c1e843a7ca` | 841/844 | **Bug2-39**（got 0） |
| M2 | 删 wrapper 内 `counted();`（完全不调用） | `b0944f9cc1d17471848ee66279647f029825486db531ff6d2d849bcc0db70168` | 841/844 | **Bug2-38**（seam 路记录为空） |
| M3 | 顺序反转：`counted(); count++;` | `96f6057da9aef1b41976d861cf76015693ed32b8d10155c60d95485d4e4ab683` | 841/844 | **Bug2-39**（真实动作在测试宿主抛异常，后置自增丢计数） |
| M4 | `:151` `if (a == null)` → `if (false && a == null)`（等价删守卫） | `6d10cbb3aad84632e1fa17d92ced7092259235a813fe3e41875adefe0627f412` | **842/844** | **无** |
| M5 | `:150` 生产分支 `: action` → `: null`（N3） | `ddf8b4a50353d1182667b358b8fb28a4c863925795cc85c7792f734e461c2002` | 841/844 | **Bug2-39**（got 0） |
| **M6** | **wrapper 内 `counted();` → `if (seam != null) counted();`（生产只自增不执行）** | `b29948d0ab14ce27b570c06305d5c18ddf2031e1e7d9611191f67f2b56a65511` | **842/844** | **无（Bug2-38/39 均 PASS）** |
| R0 | 还原副本文件 | `d1d24f23…`（与 1.1 一致） | 842/844 | 无 |

旁证：M3 说明「先自增后执行」不是风格选择而是**承重**——生产动作在测试宿主必然抛异常，只有把自增放在调用前，计数才等于「进入调用点次数」。这也说明计数器度量的是**入口**，不是**完成**；t144 §2.2 与 §7 的措辞（「证明五段都真的进入」）比实现能支撑的强一档。

## 4. Findings

### T148-F1（medium）计数是自报：生产分支可「只自增、不执行」而全绿

- 事实：M6 在 wrapper 内把无条件调用改为 `if (seam != null) counted();`，生产（seam==null）五段一段不执行，`DiagnosticSectionsEnteredForTest` 仍为 5 → Bug2-39 PASS、Bug2-38 PASS、总结果 **842/844 全绿**（变体 DLL `b29948d0…`）。
- 这与 t143 的 R31-01 是**同一类失败**（生产分支零执行而全绿），只是从「跳过调用点」下移了一层到「调用点内跳过委托」：t144 把计数的*位置*钉在了调用点内，但没有钉住「调用点内必须无条件调用委托」这一前提。t144 §2.2 的结论「只有 `a()` 真的被调用才计数」正确，但**没有任何断言阻止 wrapper 自身条件化**；注释（`:154`）与报告（§2.3/§7）据此宣称「生产分支五段都真的进入」，超出实现可支撑的强度。
- 为何是 medium 而非 low：它能**掩盖真实运行行为**（五段全不跑）且全部自动化绿——正是本票第 4 条定义的 medium。
- requiredFix（推荐 IL 契约，最小改动、可失败）：
  1. 在 Bug2-39 内（或新增 Bug2-40）取编译器生成的 wrapper：`typeof(SteamP2PFriendsPlugin).GetMethods(BindingFlags.NonPublic|BindingFlags.Static)` 中 `Name.StartsWith("<RunDiagnosticSectionForTest>b__")` 的方法；
  2. 断言 `IlContractProbe.CountFieldLoads(lambda, typeof(SteamP2PFriendsPlugin), "DiagnosticStageActionForTest") == 0`（wrapper 内不得再读 seam → M6 必红）；
  3. 断言 `IlContractProbe.CountMethodCalls(lambda, typeof(Action).FullName, "Invoke") == 1`（委托恰好被调用一次）。
  备选（若 Captain 判定 M6 属不现实变异）：把 t144 §2.2/§7 与 requirements 的措辞降级为「证明唯一调用点被执行；wrapper 的无条件调用由代码审查保证、未由断言钉住」，并在 requirements 登记为已知残余 low。二者择一，不得维持现有「证明五段真的进入」的表述而不加断言。

### T148-F2（low）N1（null 守卫）无任何用例覆盖

- 事实：M4 把守卫条件改成恒假 → 842/844 全绿。现有用例从不注入返回 null 的 seam（`grep DiagnosticStageActionForTest` 仅有 `:933` 记录器与 `:998` 复位）。
- 影响有限：守卫防的是「测试自己注入 null 后静默跳过」，生产不可达；删掉它只会把 `InvalidOperationException("... is null for section: X")` 降级为 `NullReferenceException`，不会让生产行为变错 → 可作为残余 low 接受。
- requiredFix（若不愿留残余）：新增一条用例注入 `DiagnosticStageActionForTest = (s, real) => null`，断言 `InitializeDiagnosticStage()` 返回 false 且日志含 `diagnostic stage action is null for section: `；或 IL 断言 `:151` 的 `a == null` 分支存在。

### T148-F3（low）计数器卫生：非原子、进程级累加、依赖测试自行 Reset

- 事实：`:158` `DiagnosticSectionsEnteredForTest++` 为普通 `++`；唯一清零点是 `ResetDiagnosticsStageForTest`（`:140`），生产不重置；重复调用 `InitializeDiagnosticStage`（插件重载/热重载）会继续累加。
- 影响方向安全：累加只可能让「==5」断言误红，不会让未执行的段被算成已执行；并发丢计数同样只会误红。Bug2-39 在 invoke 前 Reset，故当前不受影响。
- requiredFix：在字段注释与 t144 报告写明「断言前必须 Reset」；或改 `Interlocked.Increment`。不建议加生产侧重置（会破坏「进程内累计」语义）。

## 5. 判通过项（不因 needs_revision 而抹去）

- 生产分支确实被计数覆盖：M1（t143 原形态）与 M5（N3）都只红 Bug2-39，失败信息精确可定位。
- seam 注入分支仍被独立覆盖：M2 只红 Bug2-38，说明「完全不调用」在注入路被记录器抓住。
- `ResetDiagnosticsStageForTest` 正确清零，无测试写入该计数器，Bug2-39 在 finally 复位，不跨用例泄漏。
- 基线未退化（842/844，Bug2 39/39），构建 0 错误。
- t144 未把「守护链绿」写成「Bug2 已实机可用」，§7 明列三条 UNKNOWN。

## 6. 冻结结论

**不可冻结。** 理由：仍存在一条能**掩盖真实运行行为**的 medium（T148-F1，M6 复现），与 R31-01 同类。补齐 F1 的两条 IL 断言（并可选补 F2 的一条用例）后，本链可冻结，其余仅剩 F3 卫生项。

冻结后的能力边界（供 Captain 写进 requirements/交付说明）：本链只证明「生产分支进入唯一调用点 5 次，且（加断言后）wrapper 无条件调用该次委托」；**不证明**五段真实动作体的效果、不证明 Harmony 命中、不证明听感，实机 1H+2G 仍为 UNKNOWN。

## 7. 复审边界

- 只审 t144 交付的**测试守护链语义**与 R31-01 关闭度；不评 `Core/Patches/GunshotHostAudio/` 实现（out of scope），不评工程风格。
- 变异只在仓库外副本执行，副本已删除；主树我未写入。复审期间主树被第三方改动（§1.3）已记录，R31-01 代码区未受影响。

---
self_sha256(body)=07528a48eb216a49cd1cf31bf3e1a460b95e6f6849f36cad5412b2e97d59f40a
