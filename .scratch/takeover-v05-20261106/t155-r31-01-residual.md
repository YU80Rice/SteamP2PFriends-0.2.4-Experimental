# t155 / r10 残余收口：生产分支 wrapper 的「无条件调用」契约

## §1 摘要
t148（规格轴复审）指出 R31-01 仍是「计数自报」：M6 把 wrapper 内的 `counted();`
改成 `if (seam != null) counted();` 后，生产分支（seam == null）五段一段都不执行，
计数仍为 5，842/844 全绿。本票在编译期生成的 wrapper 上补 IL 契约，钉住
「wrapper 无条件调用委托」这一语义，并用四条变异复验。

## §2 契约设计：为什么票面两条断言单独不够
票面推荐：
- `CountFieldLoads(wrapper, typeof(SteamP2PFriendsPlugin), "DiagnosticStageActionForTest") == 0`
- `CountMethodCalls(wrapper, typeof(Action).FullName, "Invoke") == 1`

实测 M6 后这两条仍然成立：M6 在 lambda 内引用的是外层**局部变量** `seam`，
C# 编译器把 `seam` 提升为 display-class 字段（`SteamP2PFriendsPlugin+<>c__DisplayClass…`），
wrapper 读的是该 display-class 字段，而不是静态字段 `DiagnosticStageActionForTest`
（`CountFieldLoads` 按 DeclaringType.FullName 精确匹配，故计数仍为 0）；
`Action.Invoke` 仍是 1 次，且仍在自增之后。也就是说票面两条断言无法单独让 M6 变红。

因此实际契约（`WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs`，
`Test_DiagnosticStageProductionBranchEntersAllSections` 内）为：
1. 票面两条断言（保留，防「wrapper 直接读 seam 字段」的写法）；
2. **wrapper 零分支**：`OpCode.FlowControl` 为 `Branch` / `Cond_Branch` 的指令数 == 0
   —— 这是「无条件调用」的语义等价物，M6 的 `if` 必然产生 1 条分支；
3. **顺序**：`stsfld DiagnosticSectionsEnteredForTest` 的下标 < `Action.Invoke` 的下标，
   防「先调后计数」；
4. 既有 Bug2-39 行为断言（计数 == 5）继续保留。

这几条合起来，使「在包装里再加一个使用 seam 的条件」无法既通过编译又通过断言。

## §3 改动清单（file:line）
- `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs:129-133`
  字段注释补「进程内累计；任何断言前必须先 ResetDiagnosticsStageForTest，否则会误红
  （只会误红，不会把未执行段算成已执行）」。（生产逻辑未变，仅注释。）
- `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs`
  - `Test_DiagnosticStageProductionBranchEntersAllSections` 追加 IL 契约（票面两条 +
    零分支 + 顺序）；
  - 新增 `Test_DiagnosticStageNullSeamGuard`（Bug2-40）：注入
    `DiagnosticStageActionForTest = (s, real) => null`，断言 stage 返回 false、
    `DiagnosticSectionsEnteredForTest == 0`、`RunDiagnosticSectionForTest` IL 含
    `"diagnostic stage action is null for section: "`；
  - 新增助手 `FindDiagnosticWrapper` / `CountBranches` / `MethodContainsString`。
- `WhitelistTests/Program.cs:1073` 注册
  `Bug2-40 Diagnostic Stage Null Seam Guard (T155)`。

> 票面 inScope 只列了 DiagnosticsModules；测试与 Program.cs 是验收项本身的必要载体，
> 已在回执 output 中如实声明。`Release/` 未动（testkit-2 由 Captain 重打）。

## §4 变异表（仓库外副本 SPF-t155mut，逐字节还原后删除）
基线：845/847（仅 GBA01/NIL01 环境失败），DLL `623b3204…`。

| 变异 | old → new（DiagnosticsModules.cs） | 变异 DLL sha256 | 结果 | 新增红 |
|---|---|---|---|---|
| M6 条件化调用 | `DiagnosticSectionsEnteredForTest++;\n counted();` → `…;\n if (seam != null) counted();` | `41b9383c2695bc51bdca8e4a3136e9374ecb7cee7ec273e3c9db184a27b6ca30` | 844/847 | Bug2-39「wrapper must be branch-free…branches=1」 |
| M3 顺序反转 | `DiagnosticSectionsEnteredForTest++;\n counted();` → `counted();\n DiagnosticSectionsEnteredForTest++;` | `793823708641a14d75499f078177e29e3f0684b6232742661917d04ce8d59198` | 844/847 | Bug2-39「got 3」 |
| M7 删自增 | 同上 → `counted();` | `3726aade3c368f775ca25814eaefb5fba1914e469794d9702a77e9bb60cb2273` | 844/847 | Bug2-39「got 0」 |
| M4 关空守卫 | `if (a == null)` → `if (false && a == null)` | `f10a429349e6c2c41449d9b0bdfd19784df34a03ddd9ac91f80d865960de21fe` | 844/847 | Bug2-40「null action must not be counted as entered, got 5」 |

## §5 三命令（-t:Rebuild）
1. `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` → 0 警告 0 错误。
2. `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild`
   → 0 错误，4 条既有警告（NativePendingBatchLeafProbe.cs:715/41、R01E02SliceATests.cs:39/2021）。
3. `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe`
   → **845/847 PASS**，失败仅 GBA01、NIL01（既有环境失败），T145-01..06、Bug2-01..40 全 PASS。

## §6 sha256（本轮终态）
- DiagnosticsModules.cs `f99095d7d8e441ec6fdb082e71acd6cd484276265903f530c0cdddf95aef09e0`（201 行 / 9467 B）
- GunshotHostAudioTests.cs `7197386ce5dedf2c14331e6b4b101d93a3c4609e2b5bdf98892d36b763870122`
- Program.cs `fb50a07315affa49a86ae9649a3d158b2068cfa45fa35488b4a05fcbd6c98346`
- bin/Release/SteamP2PFriends.dll `623b32045685d81f9c565cb15ed235b12395d7f4f1c14d0d612c237a36328db2`

## §7 UNKNOWN / 残余
- 只证明「生产分支进入唯一执行点 5 次，且 wrapper 无条件调用委托」；
  五段真实动作体在测试宿主可能抛异常，其实际效果、Harmony 命中、实机听感仍 UNKNOWN。
- 计数仍是进程内累计，必须由 `ResetDiagnosticsStageForTest()` 复位；本轮未加生产侧重置
  （会改变「进程内累计」语义），仅在字段注释与本报告写明。
- 顺序断言（increment 在 invoke 之前）目前没有单独的点名变异：M3 先命中计数断言
  （got 3）即已红；顺序断言作为第二道防线保留。
