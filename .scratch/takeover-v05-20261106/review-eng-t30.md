# T31 工程轴复审 r5：Bug3 NRE 根因修复 + gate 日志 + 三用例

- 复审任务：t31（工程轴，第 5 轮）｜被审任务：t30｜审查官：reviewer-eng
- 结论：**needs_revision**（生产字节行为正确；缺陷在"假绿"：把生产判空改回原样仍全绿）
- 审查范围：只读。为跑变异临时改过 `P2PGroupStateRecoveryPatch.cs`，全部用 `cp` 还原并核 sha256（见 §3），未执行任何 git 写操作。

## 0. 结论摘要

t30 对 0.2.4.11-preview1 现场 NRE 的根因判断**成立且已修对**：`SDG.Unturned.SteamPlayerID`
重载了 `==`/`!=`，运算符实现直接取两侧的 `steamID`（`SteamPlayerID.cs:136-143`），
因此 `owner.playerID != null` 这种"判空惯用法"在任何情况下都会抛 NRE（右侧 `null` 被解引用）——
不是"playerID 恰好为 null"。改成 `ReferenceEquals` 后判定路径不再抛异常，合格/不合格两条分支
都留下了 gate 证据行。生产面无第二个 `playerID != null` 命中。

但本轮有一条 high 级假绿：**把生产的判空改回 `owner.playerID != null`（即把 preview1 的现场崩溃原样装回去），
全量 695/697、Bug3 段 49 条全绿**。原因是 Bug3-47/48 只测 `HasBoundPlayerId`/`BoundSteamIdOf`
两个 helper 与纯数据 `Evaluate`，**没有任何用例经过生产 `BuildEligibility`**（t30 自述的
"M2 让 BuildEligibility 恒 false 应红 Bug3-48"实测也不成立，见 §3/§4）。修复只需补测试侧 IL 契约，
不动生产字节。

## 1. 被审字节锁定

| 文件 | 字节 | sha256（前 16） | mtime |
| --- | --- | --- | --- |
| `Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs` | 59744 | a89f40b949f841d0 | 11-07 00:15 |
| `Core/Patches/P2PGroupStateRecovery/GroupStateRecoveryCore.cs` | 21614 | 7a82de8e38fa0f6a | 11-06 19:45 |
| `Core/Patches/P2PGroupStateRecovery/GroupLeaseLedger.cs` | 5861 | 3d5044f7870fbb17 | 11-06 19:46 |
| `WhitelistTests/Evidence/Runtime/P2PGroupStateRecoveryRuntimeTests.cs` | 81438 | ebfe893d9022a24e | 11-07 00:19 |
| `WhitelistTests/Program.cs` | 132593 | 1d29ea6cde524088 | 11-07 00:16 |
| `bin/Release/SteamP2PFriends.dll` | 1457152 | d9254a2461009dad | — |
| `Release/0.2.4.11-preview2/SteamP2PFriends.dll` | 1457152 | d9254a2461009dad | 11-07 00:21 |

- 与 t30 自述一致（含 GroupLoadPlanner 1344B/a4553385…、P2PGroupRegistration 1790B/5b0c4e9b… 未变）。
- `Release/0.2.4.11-preview2/sha256.txt` 与上表 DLL 哈希一致。
- 扩张面（mtime 归属，非本票）：`Platform/Diagnostics/Patches/Bug1Probe*.cs` 23:17–23:26（Bug1 票）、
  `Build/Version.props` 20:01、`Release/0.2.4.11-preview1/` 23:56。本票窗口（00:15 起）内
  生产源码只改了 `P2PGroupStateRecoveryPatch.cs` 一个文件。

## 2. 独立复跑（不采信自述）

| # | 命令 | exit | 实测结果 |
| --- | --- | --- | --- |
| 1 | `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 | 0 警告 0 错误；产物仍 `d9254a24…`（确定性构建） |
| 2 | `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 0 | 0 错误 4 警告（2 条 `R01E02SliceATests` CS0649 + 既有 2 条，均非本票） |
| 3 | `./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | 1 | **695/697 PASS (Failed: 2)**，Bug3 段 49 条全绿 |

- 失败两条为环境缺失，非本票回归：`[PureMemory] GBA01 Native First Observe Binding`、
  `NIL01 Real Native Lookup Contract`，原因 `TargetInvocationException → TypeInitializationException:
  SDG.Unturned.SteamPlayer → SDG.Unturned.NetReflection → ReflectionTypeLoadException`（控制台无 Unity 程序集）。
- 测试进程日志打印 `[T14] SPF_DLL_SHA256=d9254a24…  path=…\WhitelistTests\bin\Release\SteamP2PFriends.dll`，
  确认跑的是送审 DLL 字节，不是源码链接。
- gate 行不出现在 exe stdout（`RoleLogger.Info` 走游戏日志通道），`grep -c "gate p2pHost"` = 0；
  验证 gate 行只能靠单测（Bug3-48）与实机日志文件，故实机交付必须附带"取 Client.log"的说明。

## 3. 自跑变异（每条：改字节 → 重建 → 同步 DLL → 跑全量 → 记录 → `cp` 还原）

变异脚本 `D:/tmp/mut31.py`（备份 `D:/tmp/p31.pristine.cs`，每次替换前断言命中次数 = 1）。
**仓库 `core.autocrlf=true`，还原只能用 `cp` 回拷备份，绝不能用 `git checkout --`（会改成 CRLF）。**

| 变异 | 改动点 | DLL sha256（前 16） | 结果 | 新红用例 |
| --- | --- | --- | --- | --- |
| 基线 | 无 | d9254a2461009dad | 695/697（Failed 2=环境） | — |
| M1a | `Patch.cs:1006` helper 内 `!ReferenceEquals(owner.playerID, null)` → `owner.playerID != null` | 7c4024be9e65cede | 693/697 | **Bug3-47**、**Bug3-48** |
| M1b | `Patch.cs:962` 生产调用点 `HasBoundPlayerId(owner)` → `owner != null && owner.playerID != null`（= preview1 现场崩溃原样回归） | 7a3de5a1b58586dc | **695/697（全绿）** | **无** |
| M2 | `Patch.cs:966` `Provider.user.m_SteamID` → `+ 1UL`（让 BuildEligibility 必然产出"不合格"事实） | 9623690d915b41f3 | **695/697（全绿）** | **无** |
| M3 | 删 `Patch.cs:538` 与 `:545-546` 两处 `LogGate(...)` 调用 | 3dc43c5d9465238f | 694/697 | **Bug3-48** |

- M1a 红 47 的原因正确：`Assert(false, "playerID=null 的判定路径不得抛异常…")`；
  红 48 说明该运算符**对任意一侧为 null 都解引用**——`owner.playerID` 非空时依然 NRE。
- **M1b 是本轮的核心发现**：生产路径的判空退回运算符后，整套 49 条 Bug3 用例一条都不红，
  实机将重现 preview1 的"每局 `Prefix_Load failed; saving blocked … NullReferenceException`"。
  IL 层面的差异是 `BuildEligibility` 内多了一次 `callvirt SteamPlayer::get_playerID`，现有用例没有断言它。
- M2 证明 t30 自述"让 BuildEligibility 恒 false 应红 Bug3-48"不成立：Bug3-48 用 `Eligible()` 造纯数据，
  根本不经过 `BuildEligibility`。
- 还原核验：`P2PGroupStateRecoveryPatch.cs` 回到 `a89f40b9…`，重建 DLL 回到 `d9254a24…`，
  复跑 695/697（仅环境 2 条）——逐字节还原成立。

## 4. Acceptance 逐条

| # | 判据 | 判定 | 依据 |
| --- | --- | --- | --- |
| 1 | 独立复跑三命令，不采信自述 | **passed** | §2；三项 exit/计数与 t30 自述一致，DLL 哈希可复现 |
| 2 | 逐文件绑 sha256 且与 t30 交付一致 | **passed** | §1 全表一致，含 preview2 产物与 sha256.txt |
| 3 | NRE 修复是真根因而非绕过 | **passed** | 全模块 `playerID != null`/`== null` 0 命中；判空全走 `ReferenceEquals`（`:1006`、`:963-964`、`:1009-1010`）；`BuildEligibility` 内无 try/catch 吞异常、无提前 return 假绿；grep `playerID` 仅 `:993`（已被 helper 保护）、`:1006`、`:1010` |
| 4 | 自跑 ≥3 变异且对应必红 | **failed** | M1a/M3 真红；**M1b（生产判空回退）与 M2（生产恒不合格）全绿**，t30 对 M2 的红名单声明不成立 |
| 5 | Bug3-49 是否合法 characterization | **passed** | 断言对象是 SDK 运算符行为（`absent != null` 抛 NRE），非我方实现；标注"不修改 SDK"，SDK 升级后变红属有意信号 |
| 6 | gate 行只读、12 项语义与门控一致 | **passed** | `FormatGateLine:1017-1038` 12 项逐一对应 `Evaluate` 输入；无 SteamID 明文（仅 `ownerSteamIdNonZero` 布尔）；唯一副作用是 `RoleLogger.Info` 且被 try/catch 包住、不参与判定；每连接 1 次（load 前缀），非逐帧 |
| 7 | 未扩张改动面 | **passed** | 本票窗口生产源码仅 `P2PGroupStateRecoveryPatch.cs`（mtime 00:15）；测试与 `Program.cs` 可接受；`Build/`、`publishi/`、`Platform/` 无本票变更（`Bug1Probe*` 23:17–23:26 属 Bug1 票，preview1 23:56） |
| 8 | 可否作为实机待测交付 | **conditionally** | 行为上可以（根因已消除、失败模式显式），但按 ④ 未满足 + §5 R5-01 high，本轮不能以"修复收口"放行 |

## 5. Findings

### R5-01（high）生产 `BuildEligibility` 的判空无任何行为/IL 守卫，preview1 崩溃可原样回归而全绿

- 位置：`Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs:962`（生产调用点）；
  `WhitelistTests/Evidence/Runtime/P2PGroupStateRecoveryRuntimeTests.cs:1305-1332`（Bug3-47）与
  `:1334-1372`（Bug3-48）。
- 现象：Bug3-47 只调 `HasBoundPlayerId`/`BoundSteamIdOf`，Bug3-48 用 `Eligible()` 纯数据 + `FormatGateLine` + IL 计数，
  **两条都不执行 `BuildEligibility`**。实测 M1b（把 `:962` 改回 `owner.playerID != null`）→ 695/697 全绿、Bug3 49 条无红；
  该改动正是 preview1 现场每局 NRE 的原样回归。
- requiredFix（只动测试，不动生产）：在 Bug3-47 内补两条 IL 契约，并用 M1b 复验必红：
  ```csharp
  MethodInfo build = AccessTools.Method(typeof(P2PGroupStateRecoveryPatch), "BuildEligibility");
  Assert(IlContractProbe.CountMethodCalls(build, "SDG.Unturned.SteamPlayer", "get_playerID") == 0,
      "BuildEligibility 不得直接触碰 SteamPlayer.playerID（必须经 HasBoundPlayerId）");
  Assert(IlContractProbe.CountMethodCalls(build,
      "SteamP2PFriends.Core.Patches.P2PGroupStateRecovery.P2PGroupStateRecoveryPatch", "HasBoundPlayerId") >= 1,
      "BuildEligibility 的 owner 判定必须走 helper");
  ```

### R5-02（medium）t30 的变异证据含一条不成立的红名单

- 位置：t30 交付说明"M2 BuildEligibility 恒 false → 应红 Bug3-48"。
- 现象：Bug3-48 不经 `BuildEligibility`；实测 M2（`ProviderUserId + 1UL`，等价于生产恒不合格）→ 695/697 全绿。
  也就是说，"门控映射被改坏"这件事当前没有任何用例能发现。
- requiredFix：订正 evidence_note/报告表述；把 M2 换成真红的等价变异（例如删 `GroupStateRecoveryCore.Evaluate`
  里 `OwnerSteamId == ProviderUserId` 比较 → 应红 Bug3-34/35），或先落 R5-01 的 IL 守卫再重新取值。

### R5-03（low）`Provider.user` 无判空，dedicated/未初始化上下文会抛 NRE 并被外层 catch 转成 fallback 或阻塞

- 位置：`P2PGroupStateRecoveryPatch.cs:966` `facts.ProviderUserId = Provider.user.m_SteamID;`。
- 现象：`BuildEligibility` 不做 P2P/模式短路，先映射全部事实；dedicated（无本地 client）或 Steam 未初始化时
  `Provider.user` 为 null → NRE → `:569` catch。dedicated 下 `hostContext=false` → 每连接打一条 Error 后回退原生 load（噪音）；
  host 上下文下 `hostContext=true` → `MarkInstanceBlocked` + fail-closed（本局不 load 不 save）。
- 该风险非本票引入（旧字节同位置），但与本票修的 NRE 属同一家族，且新 catch 语义把它的后果从"崩溃"变成"静默阻塞"。
- requiredFix：`ReferenceEquals(Provider.user, null)` 判空取 0，或在方法首行 `if (!HostManager.IsP2PHostMode) return default;`
  短路（同时省掉全量映射）。

### R5-04（low）根因描述不精确，会误导后来者

- 位置：`P2PGroupStateRecoveryPatch.cs:1001-1002` 注释、测试 `:1302-1304` 文档注释、实机说明。
- 现象：写成"`playerID` 为 null 时运算符抛 NRE"。实际 `SteamPlayerID.cs:136-143` 的 `operator ==` 直接取
  `playerID_0.steamID` 与 `playerID_1.steamID`，**任一侧为 null 都抛**；因此 `playerID != null` 在 `playerID` 非空时同样必抛
  （M1a 让 Bug3-48 也红，正是这个原因）。"判空惯用法本身失效"比"值为 null"更接近事实。
- requiredFix：订正注释与实机说明措辞（一行）。

### R5-05（low）gate 行的 `owner=` 把"owner 缺失"与"playerID 未绑定"合并，现场定位会歧义

- 位置：`P2PGroupStateRecoveryPatch.cs:1026`（`owner=` → `HasOwningSteamPlayer`）。
- 现象：`HasOwningSteamPlayer = owner != null && owner.playerID != null`；实机上看到 `owner=false` 无法区分
  是 `channel.owner` 为 null 还是 `playerID` 未绑定——而这恰好是本票要区分的两种现场。
- requiredFix（择一）：拆成 `owner=`（仅 `!ReferenceEquals(owner,null)`）与 `ownerPlayerId=`（`!ReferenceEquals(owner.playerID,null)`），
  或在交付说明的 12 项对照表里写明该布尔是合取。

### R5-06（info）交付面观察

- `Release/0.2.4.11-preview2/`（DLL 与送审字节同哈希）属本票打包产物；`proof` 需在交付清单中标注"打包产物，不属生产源码面"。
- 新增三条用例无静态单例/seam 泄漏（不碰 `Core` 单例、不设 `EligibilityResolverForTests`），不需要 `ResetForTests`，顺序无关。

## 6. 实机待测判断与 UNKNOWN

**行为侧可以进实机**：根因（运算符解引用）已消除，判定路径不再抛异常；失败方向是显式 fail-closed + Error 日志，
不会写坏存档；gate 行不含 SteamID 明文，无隐私问题。**但按合同 ④ 与 R5-01，本轮不以"修复收口"放行**——
缺陷在守卫（测试），不在生产字节；修 R5-01/R5-02 不需要改生产代码，下一轮只需复验基线不变 + M1b/M2 必红。

实机必须验证（缺一项则"修复有效"不成立）：

1. 主机首次进 GAME 后，日志出现 `gate … eligible=true … decision=Deferred`，随后出现 deferred load 成功行；
2. 不再出现 `Prefix_Load failed; saving blocked … NullReferenceException at BuildEligibility`；
3. 原症状消失（队伍/成员列表在延迟加载后正确，而非丢失）；
4. 退服重进、换图第二局仍走 defer（旧租约不复活）；
5. 正常保存后重进，任务与组状态保留（写安全标记未误伤正常 save）。

UNKNOWN（自动化不可证，不得用绿灯替代）：

| # | 未知项 | 影响 | 判定方式 |
| --- | --- | --- | --- |
| U1 | 真实 Unity 下 `AccessTools.Field(typeof(GroupManager),"knownGroups")` 能否解析 | 解析失败 → 注册失败、特性静默不启用（有 Error） | 实机日志看注册成功行 |
| U2 | 1H+1G 端到端（Guest 不在本补丁作用面） | 决定 Bug3 是否真的修好 | 真机三端 |
| U3 | dedicated/无本地 client 下 `Provider.user` 是否 null（R5-03） | 每连接一条 Error，行为仍是 fallback | 正式服日志抽样 |
| U4 | gate 行实际落到哪个日志文件、是否被日志级别过滤 | 影响现场取证 | 实机取 Client.log |

## 7. 复现命令

```bash
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/SteamP2PFriends.dll
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe   # 695/697，Failed=GBA01/NIL01

cp Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs /d/tmp/p31.pristine.cs
python /d/tmp/mut31.py M1a   # 然后 M1b、M2、M3；脚本自带 cp 还原与 sha256 校验
sha256sum Core/Patches/P2PGroupStateRecovery/P2PGroupStateRecoveryPatch.cs  # 必须回到 a89f40b9…
dotnet build SteamP2PFriends.csproj -c Release && sha256sum bin/Release/SteamP2PFriends.dll  # d9254a24…
```

> 注意：`core.autocrlf=true`，不要用 `git checkout --` 还原被变异的工作区文件（会改写为 CRLF 破坏字节锁定）。

<!-- self-integrity: sha256(本行以上全部 15314 字节，不含本行) = ee47140b8e1735f8cc491478d1bfdb28786ace33b32599ac1dfe7db3d95b5725 -->



