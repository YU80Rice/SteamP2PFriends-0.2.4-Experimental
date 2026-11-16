# t140 工程轴复审（r8）：生产模式阶段覆盖是否真实、注入缝是否安全

审查官：reviewer-eng（工程轴）· 任务 t141 · 被审 t140 · attempt ef4d5cf7-da8d-478d-a390-6f412dddf6b9
日期：2026-11-08

## §0 结论

**verdict = needs_revision**（0 blocker / 0 high / 1 medium / 3 low）。

t140 的三个声明里，两个成立、一个只成立一半：

- **B5/B4 确已变红**（我自跑）：B5（删 unity catch 的 `ok = false;`）DLL `bf336538…`，836/839，唯一新增红 **Bug2-38**——与 Captain 逐位一致；B4（fold 前 `if (!GunshotStageOnlyForTest) ok = true;`）DLL `5e916ba0…`，836/839，只红 Bug2-38（与 t140 报告一致；Captain 的 `cd9d1e85…` 因加了注释）。S3（注入分支吞掉 throw 不抛）也被抓到（`52192657…`，836/839 红 Bug2-38），说明「注入但不抛＝成功」这条语义没有被滥用。
- **注入缝生产不可达**：`DiagnosticStageSectionForTest` / `DiagnosticStageSectionThrowForTest` 全仓库仅在本文件声明（`:122-123`）、Reset（`:129-130`）与读取（`:137`/`:147`），没有任何生产赋值路径，也没有配置/外部输入入口。✓
- **但「生产模式首次被用例执行」只对了一半**：Bug2-38 虽然保持 `GunshotStageOnlyForTest=false`，却**始终设置 SectionForTest**，于是 `RunDiagnosticSectionForTest` 走注入分支——真实动作 `action()` 在任何用例里都**一次没有执行**。后果是「生产默认路径」本身无覆盖：
  - **S1**（`if (injected == null) { return; }`，即生产模式静默跳过全部五段）→ DLL `d62691a4…`，**837/839 全绿**；
  - **S2**（`gate` 段的生产动作换成 `() => { }`，即该段永不初始化）→ DLL `62dbf309…`，**837/839 全绿**。
  这正是验收第 3 条点名要试的「让生产模式跳过该段」。t140 把「catch 里的 ok=false」钉住了，但没钉住「段到底有没有被调用」。

## §1 被审字节（实测，与 Captain 锁逐项一致）

| 文件 | 字节 | 行 | sha256（前16） |
|---|---|---|---|
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs` | 8160 | 187 | `a76c3d4c442ab443` |
| `WhitelistTests/Program.cs` | — | — | `802fea816ef1a224` |
| `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs` | 71635 | 1416 | `900ddc659f484679` |
| `bin/Release/SteamP2PFriends.dll` | — | — | `96f4ff36aa24e38b` |

EOL：DiagnosticsModules.cs 全 CRLF（187/187），与 t140 §R29-02 的说明一致；测试与其余 Gunshot 文件为 LF。

## §2 独立复跑

| 命令 | 退出码 | 结果 |
|---|---|---|
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 | 0 错误 0 警告，DLL 复现 `96f4ff36`（确定性） |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 0 | 0 错误，4 条既有警告（CS0649×3 + CS0219） |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | 1 | **837/839 PASS**，FAIL 仅 `GBA01`/`NIL01`（缺 Unity 运行时），`FAIL [Runtime] Bug2` = 0 |

## §3 变异表

仓库外副本 `../_t141mut`（独立重建先复现 `96f4ff36`），逐条按 `.orig` 字节还原并复核 DLL，副本已删除。替换均为 CRLF 精确子串。

| # | 文件:行 | 改前 → 改后 | 变异 DLL | 结果 |
|---|---|---|---|---|
| B5 | `DiagnosticsModules.cs:52` | `ok = false;`（unity catch）→ 删除 | `bf336538fce90772` | 836/839 红 **Bug2-38** |
| B4 | `:93` 前 | 插入 `if (!GunshotStageOnlyForTest) ok = true;` | `5e916ba0ea3f8f0b` | 836/839 红 **Bug2-38** |
| **S1** | `:140` | `action();` → 删除（生产分支直接 return） | `d62691a4fbff9d69` | **837/839 全绿** |
| **S2** | `:101` | `Bug1GateProbe.Initialize` → `() => { }` | `62dbf30978d698f4` | **837/839 全绿** |
| S3 | `:147-148` | `throw …` → 空块（注入但不抛） | `521926575177cfa8` | 836/839 红 **Bug2-38** |

## §4 四问逐条

**问 1：B5/B4 确已变红？——是。** 见 §3；B5 与 Captain 哈希逐位一致，红名单仅 Bug2-38。

**问 2：注入模式「其余段跳过」是否引入新的假绿？——是，且比预期更宽。**
- 注入模式下 `RunDiagnosticSectionForTest` 对不匹配的段直接 `return`，对匹配段只抛异常或什么都不做——**五段的真实动作 `action()` 在整轮测试里从未执行**。因此「段是否被调用」没有任何守护：S1（生产分支不调 action）、S2（gate 段传空动作）都全绿。
- 按段清点（当前实现的覆盖）：
  | 段 | catch 的 `ok=false` 是否被执行 | 真实动作是否被执行 | 段内返回值失败是否可见 |
  |---|---|---|---|
  | unity | 是（B5 反证） | **否** | n/a（void） |
  | nativeSns | 是（注入循环） | **否** | n/a（lambda 表达式体） |
  | bug1Probe | 是（注入循环） | **否** | **否**——`RegisterManual` 返回 bool，lambda 丢弃（`:68`） |
  | bug1ProbeControl | 是（注入循环） | **否** | n/a（void） |
  | gate | 是（注入循环） | **否** | n/a（void） |
- 因此「真实初始化体在测试宿主不可执行」这一既有 UNKNOWN 仍然成立（合理，宿主无 Unity/BepInEx），**但必须如实登记**：目前没有任何用例证明生产路径确实调用了这五个初始化入口。

**问 3：注入 seam 是否生产不可达？——是。**
`grep -rn "DiagnosticStageSectionForTest|DiagnosticStageSectionThrowForTest"` 在本文件外 0 命中；文件内只有声明（`:122-123`）、Reset 清零（`:129-130`）、读取（`:137`/`:147`）。没有配置文件、环境变量或外部输入能到达这两个字段（它们不是 BepInEx ConfigEntry，也不在反射名单里）。Reset 在测试 finally 中调用，两个字段都清（`null` / `false`）。唯一残余面：字段是 `internal static`，程序集内任意未来代码都可写——属契约风险而非当前缺陷（R30-03）。

**问 4：是否仍有绕过路径？——有，见 S1/S2；「注入不抛＝成功」本身没被滥用（S3 变红）。**
S3 证明：若把注入分支改成吞掉异常/不抛，Bug2-38 立刻红——所以该语义是有效的。真正漏的是「调用点」而非「异常语义」。

## §5 findings

| ID | 严重度 | 位置 | 问题 | 修法 |
|---|---|---|---|---|
| **R30-01** | medium | `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs:135-150` | 注入 seam 以「跳过其余段 + 不执行真实动作」实现，生产默认分支（`injected == null → action()`）无任何用例覆盖。S1 `d62691a4…`（生产分支不调 action）与 S2 `62dbf309…`（gate 段空动作）均 837/839 全绿 → 删除/替换任一诊断初始化调用都不会被发现。t140 声称的「生产模式首次被用例执行」只覆盖了 catch 结构与注入语义，未覆盖调用点。 | 把 seam 从「跳过」改为「替换动作」：`internal static Func<string, Action> DiagnosticStageActionForTest;`，`Run` 中 `Action a = DiagnosticStageActionForTest?.Invoke(section) ?? action; a();`。用例注入 `s => () => { entered.Add(s); if (throwFor) throw …; }`，断言 `entered.Count == 5` 且五段名齐全、逐段 throw→stage=false、无 throw→stage=true。这样删掉调用点或换成空动作会因 `entered` 缺项而变红。 |
| **R30-02** | low | `DiagnosticsModules.cs:68` | `Bug1ProbePatch.RegisterManual` 返回 bool，lambda 表达式体把它丢弃；probe 安装失败（FailClosed=true）既不让 stage=false，也不进入任何断言。属 t139 R29-01 的同类残留（异常之外的失败信号）。 | 在 seam 重构时一并显式接收返回值并记录（如 `failedSections.Add("bug1Probe")` + 日志），**不要**因此让 stage=false（诊断不应阻断注册）；用例断言失败段名出现在记录中。 |
| **R30-03** | low | `DiagnosticsModules.cs:122-123` | 两个注入字段为 `internal static`，程序集内任意代码可写；当前无生产赋值，但无任何编译期/运行期约束。 | 在字段注释写明「仅测试写、生产只读」契约，或改用 `#if DEBUG`/`InternalsVisibleTo` 之外的显式测试钩子类集中管理。 |
| **R30-04** | low | `GunshotHostAudioTests.cs:934-937` | 「成功侧」只验证 `SectionForTest="unity"` 一种注入；其余四段在注入模式被跳过，五段「成功路径」未逐段断言（当前被跳过所以恒真）。 | 随 R30-01 的 seam 重构，逐段断言 `entered` 含该段且 stage=true。 |

## §6 结论边界

- 本轮判「t140 让 catch 层失败可检出、注入缝生产不可达」——**不等于**诊断阶段在真实环境可运行：五个初始化体的真实执行仍是 UNKNOWN（宿主无 Unity/BepInEx），需实机。
- 不声称 Bug2 已修复或主机可听见；实机听感、1H+2G 三端互听、Mod 音效兼容仍 UNKNOWN。
- 主树未改动；变异全在 `../_t141mut`，逐字节还原、副本已删。

## §7 复现命令

```bash
# 基线（应为 837/839，仅 GBA01/NIL01；DLL 96f4ff36…）
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe

# S1：生产分支不执行真实动作 → 837/839 全绿（d62691a4…）
#   把 RunDiagnosticSectionForTest 中 `injected == null` 分支的 `action();` 删掉
# S2：gate 段换成空动作 → 837/839 全绿（62dbf309…）
#   把 :101 的 Bug1GateProbe.Initialize 改成 () => { }
# 每次：重建生产 → cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/ → 跑 exe → 按 .orig 还原并核 sha256
```

<!-- self_sha256(body)=d84a281ec83952bd016ac36c49bfcc160516fd34433a6b6a9557f1ea247c63a4 -->
