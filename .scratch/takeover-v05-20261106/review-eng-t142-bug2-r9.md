# t143 工程轴复审 t142（Bug2 r9：seam 改替换动作 + 五段调用可证）

## §0 结论

**verdict = needs_revision**（0 blocker / 0 high / **1 medium** / 3 low）。

t142 把 seam 从「跳过」改成「替换动作」，并让 Bug2-38 记录每段真实 action 的身份——这一步方向正确，S1（删唯一调用点）与 S2（gate 换成空 lambda）现在都真红，t141 的两条全绿已被堵住。

但「生产分支（`seam == null`）」仍然**没有任何用例执行**：本轮自造 N2 把唯一调用点改成 `if (seam != null) a();` 后，`837/839` **全绿**——即「生产模式下五段一段都不跑」在测试里完全不可见。这与 R30-01 是同一类缺口，只是从「注入分支不执行真实动作」搬到了「生产分支不执行任何动作」。要闭合，需要一个在 seam==null 下也成立的生产侧可观测计数（见 R31-01）。

不声称 Bug2 已修复，也不声称主机可听见。

## §1 字节锁（实测，与 Captain 逐项一致）

| 文件 | 字节 | 行 | sha256 |
|---|---|---|---|
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs` | 8292 | 184（CRLF 184/184） | `2aed924e8a0a8b59402364e98d07e787e61ae3f7f2531a68f7bdf622bd84b291` |
| `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs` | 73460 | 1445 | `3ae6bcdb1e0be1c7a9d4665c7daafb83bca44ca33f480710d2366e160aacd323` |
| `WhitelistTests/Program.cs` | — | — | `802fea816ef1a224…`（未改） |
| `Core/Patches/GunshotHostAudio/*.cs` | 未变 | — | Control `478d03c7`、Patch `b90246e6`、Policy `9e0fa20e`、Runtime `cf1efab2`、IlScan `ef512ba0` |
| `bin/Release/SteamP2PFriends.dll` = `WhitelistTests/bin/Release/…` | 1588224 | — | `cc816b2e7d01a492d69568766a7890d6bc919e7a31290ca90c242900a0f2ab38` |

## §2 独立复跑

| 命令 | 退出码 | 结果 |
|---|---|---|
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 | 0 警 0 错；DLL 复现 `cc816b2e…`（确定性） |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 0 | 0 错 4 既有警告（CS0649×3 + CS0219） |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | 1 | **837/839**；FAIL 仅 `GBA01` / `NIL01`（控制台缺 Unity：`TypeInitializationException SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`）；Bug2 38/38 PASS |

## §3 变异（仓库外副本 `../_t143mut`，独立重建复现 `cc816b2e`；每条按 `.orig` 字节还原后重建）

| # | 改法（文件:行） | 改前 → 改后（摘要） | 变异 DLL(前16) | 结果 | 新增红 |
|---|---|---|---|---|---|
| S1 | `DiagnosticsModules.cs:146` | `a();` → `//a();` | `33036de959b28f35` | 836/839 | Bug2-38「all five sections must be invoked, got 0」 |
| S2 | `:108` | `…("gate", Bug1GateProbe.Initialize);` → `…("gate", () => { });` | `a60decea9600e607` | 836/839 | Bug2-38「gate action identity changed: gate\|<>c.<InitializeDiagnosticStage>b__92_2」 |
| N1 | `:144-145` | 删除 `if (a == null) throw …` | `220662d682f7d1ef` | **837/839 全绿** | — |
| **N2** | `:146` | `a();` → `if (seam != null) a();` | `70b5942bb5f679e9` | **837/839 全绿** | — |
| N3 | `:143` | `… : action;` → `… : null;` | `151a3a2a674e3b81` | **837/839 全绿** | — |

还原后：源 `2aed924e…`、DLL `cc816b2e…`、复跑 837/839。

- S2 哈希与 t142 自报**逐位相同**（`a60decea…`）；S1 哈希不同（我 `//a();` = `33036de9`，Captain `ed0fb8fd`，t142 `daa97fe7`），红名单一致，属注释文本差异（同 R29-02/R30 类可复现性问题，low）。
- **N2 是本轮决定性的**：生产（`seam == null`）时五段一段都不执行、`ok` 保持 true → stage 返回 true、注册继续，而整套用例 837/839 全绿、Bug2-38 PASS（38/38）。这说明 Bug2-38 只能观察注入分支，观察不到生产分支。

## §4 四问逐条

**① S1/S2 是否真红** —— 是。S1 因 `entered.Count==0` 红，S2 因身份 `<>c.<InitializeDiagnosticStage>b__92_2` 与期望 `Bug1GateProbe.Initialize` 不符而红；两者都只新增 Bug2-38 一条 FAIL。

**② 是否仍有绕过（记录但未执行真实动作）** —— 有两层：
- 测试侧：Bug2-38 的包装器 `(section, real) => () => { entered.Add(…real.Method…); }` **从不调用 `real()`**。它证明的是「用正确的 delegate 对象进入了 seam」，**不证明真实动作体执行**（这一点 t142 §9 已声明为 UNKNOWN，口径正确；但报告措辞「五段真实调用可证」偏强，见 R31-03）。
- 生产侧（实质缺口）：N2 证明「生产分支跳过全部五段」不会被任何断言发现——因为所有断言都只在 `seam != null` 的分支里跑。N3 同理（生产分支返回 null，五段各抛 `InvalidOperationException` → 全被 catch 折成 `ok=false`，注册整体 fail-closed，是「响的失败」，但测试仍全绿）。

**③ 删掉 `if (a == null) throw` 是否变红** —— 不变（N1，837/839 全绿）。该守卫只对「测试注入返回 null」生效；生产 `seam==null` 时 `a` 恒非 null，因此这条守卫是纯测试契约防御，无生产语义。建议要么加一条注入 `(s, r) => null` 的用例钉住它，要么在注释里写明「仅防御测试注入」。

**④ `RegisterManual` 的 false 语义与折算** —— 正确。`Platform/Diagnostics/Patches/Bug1ProbePatch.cs:30-40`：`harmony==null → false`；`Attempted && 同 Id → true`；`!Bug1ProbeRuntime.Enabled → true`（默认关闭不算失败）；否则 `return Install()`。故 lambda 里 `if (!RegisterManual(_harmony)) throw …` 不会把「未启用」误判为失败，只会把「harmony 缺失 / 安装或校验失败」折进 `ok=false`，且异常被既有 `catch`（`:77-81`）记日志——语义与 fail-closed 口径一致。已逐行核对，无异议。

## §5 seam 生产可达性与清零

- 全仓 `DiagnosticStageActionForTest` 仅三处：声明 `:129`、Reset 置 null `:135`、读取 `:142`；`WhitelistTests` 仅 `GunshotHostAudioTests.cs:929` 赋值。**生产无可达写入路径**。
- `ResetDiagnosticsStageForTest()`（`:131-136`）三清（`PriorStageFailedForTest` / `GunshotStageOnlyForTest` / seam）；Bug2-38 `finally` 调用它并清 `GunshotModuleInitializerForTest`（tests:968-972），无跨用例泄漏。
- 生产路径（`seam == null`）代码为 `Action a = action; a();`——读代码层面与 t141 等价（零行为变化）；但见 §4②，**这一支没有测试执行**。

## §6 findings

| id | 严重度 | 位置 | 问题 | requiredFix |
|---|---|---|---|---|
| **R31-01** | **medium** | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs:140-147` | 全部断言只在 `seam != null` 时执行；N2（`if (seam != null) a();`）使生产模式五段**全不执行**仍 837/839 全绿。R30-01 的「删/换初始化调用不被发现」在生产分支上仍成立。 | 在 `RunDiagnosticSectionForTest` 里加生产侧可观测计数（如 `internal static int DiagnosticSectionsEnteredForTest;`，在 `a()` 前自增，Reset 清零），并新增一条 **seam==null** 的用例：直接调 `InitializeDiagnosticStage()`，断言计数 ≥5（真实动作体允许抛异常并被 catch 折成 false，但「进入过五段」必须被证）。加后 N2 必红。 |
| R31-02 | low | 同文件 `:144-145` | null 守卫可被删除而不红（N1）。 | 加一条注入 `(s, r) => null` 的用例断言抛 `InvalidOperationException`；或在注释注明「仅测试契约防御、生产不可达」。 |
| R31-03 | low | `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs:929-934`；t142 报告 §2 | 包装器记录 `real.Method` 但从不调用 `real()`；「五段真实调用可证」应改述为「五段以正确的 delegate 进入唯一执行点可证」，动作体仍 UNKNOWN。 | 订正报告措辞；如要证真实体，需要 Unity/BepInEx 宿主，作为实机/宿主测试项登记。 |
| R31-04 | low | 变异证据 | S1 的变异 DLL 哈希三方不同（33036de9 / ed0fb8fd / daa97fe7），红名单一致；注释文本未逐字公开。 | 变异表补「改前/改后原文 + 完整 DLL sha256」，与本轮 §3 同格式。 |

## §7 边界与 UNKNOWN

- 未触碰 `Release/`、`publishi/`、`Develop-Stage/`、`publish/`；`git status` 与开工一致；副本 `../_t143mut` 已删除，主树源 `2aed924e…` / DLL `cc816b2e…` 与开工一致。
- 五段真实动作体（UnityLogBridge / NativeSns / Bug1Probe / Bug1ProbeControl / Gate 的初始化）**仍未在无 Unity/BepInEx 环境执行**，只能证调用点、顺序、delegate 身份。
- 实机听感、1H+2G 三端互听、Mod 自定义音效兼容：UNKNOWN，未测。
- 本报告不评估 Bug2 是否已被修复，也不评估主机是否能听见。

## §8 复现命令

```bash
# 基线
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe   # 837/839, DLL cc816b2e

# 变异（仓库外副本，src 为 CRLF，必须按 \r\n 精确匹配）
python /d/tmp/mut143.py S1|S2|N1|N2|N3
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/
WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe
python /d/tmp/mut143.py restore && dotnet build … -t:Rebuild && sha256sum …  # 回 cc816b2e
```

<!-- self_sha256(body)=8a527a224db28e111b9c7e67ac19e591c5d1e95ea5dd2d4889694c72d74856cb -->
