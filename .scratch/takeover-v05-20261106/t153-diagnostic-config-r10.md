# t153 — 诊断配置 r10 收口（R31-01..04）

任务：t153（Fixes t146 findings），attempt 2 = `d2bccb55-77df-4898-a165-93fee7ccaf3f`。
日期：2026-11-09。基线：`842/844`（Captain 口径）；本轮新增 2 条用例后 `844/846`。

## 1. 摘要

- R31-01：启动自证行格式化抽成纯函数 + 八组真值表 + ConfigEntry 接线用例；写死任一项必红。
- R31-02：契约从「Initialize 方法体」升级为「三个类型的全部方法（含编译器生成 lambda/嵌套类型）」+ Core/Platform 源码级扫描；旁路 `new ConfigFile` 必红。
- R31-03：自证行从诊断 stage 内部移到 `PluginConfig.Bind` 完成处，配置绑定后无条件输出；任何注册/诊断 stage 尚未执行，前序失败仍可见。**不修改根 `SteamP2PFriendsPlugin.cs`（票面 inScope 之外）**。
- R31-04：空/未绑定路径输出 `configPath=<unbound>`，并在 Bind 末尾告警一次。
- 新增用例 T145-05（真值表）、T145-06（接线）；T145-02/03/04 加固。
- 全量：`844/846 PASS`，仅 GBA01/NIL01 既有环境失败；T145-01..06、Bug2-01..39 全 PASS。

## 2. R31-01 —— 纯函数 + 真值表 + 接线

文件：`Core/Config/PluginConfig.cs`

改前（单函数直接读 ConfigEntry）：

```csharp
internal static string FormatDiagnosticsConfigLine()
{
    return "[Diagnostics] configPath=" + BoundConfigFilePath
        + " bug1Probe=" + BoolText(Bug1ProbeEnable?.Value == true)
        + " bug1Gate=" + BoolText(Bug1GateEnable?.Value == true)
        + " bug2HostGunshot=" + BoolText(Bug2HostGunshotEnable?.Value == true);
}
```

改后（包装器只负责取路径，拼接交给纯函数，纯函数无静态状态）：

```csharp
internal static string FormatDiagnosticsConfigLine()
{
    string path = string.IsNullOrEmpty(BoundConfigFilePath) ? "" : Path.GetFullPath(BoundConfigFilePath);
    return FormatDiagnosticsConfigLine(path,
        Bug1ProbeEnable?.Value == true,
        Bug1GateEnable?.Value == true,
        Bug2HostGunshotEnable?.Value == true);
}

internal static string FormatDiagnosticsConfigLine(
    string absolutePath, bool bug1Probe, bool bug1Gate, bool bug2HostGunshot)
{
    string path = string.IsNullOrEmpty(absolutePath) ? "<unbound>" : absolutePath;
    return "[Diagnostics] configPath=" + path
        + " bug1Probe=" + BoolText(bug1Probe)
        + " bug1Gate=" + BoolText(bug1Gate)
        + " bug2HostGunshot=" + BoolText(bug2HostGunshot);
}
```

用例：

- `T145-05 Startup Line Truth Table (T153)`：三布尔八组逐一断言完整输出串；空串与 null 断言 `configPath=<unbound>`。
- `T145-06 Startup Line Entry Wiring (T153)`：`bug1Gate`/`bug2HostGunshot` 的 ConfigEntry 改 true → 行内 `True`；改 false → `False`（finally 还原）。
- `T145-04` 在热开启 true 窗口内追加 `bug2HostGunshot=True`、关闭后 `False`。

判定「为什么能防同类回归」：输出不再依赖调用点的常量或某个 ConfigEntry 的读取顺序——纯函数把四个输入显式化，真值表要求任一输入变化必须反映到对应字段；接线用例证明生产包装器确实把 entry 值传进去。把任一项写死成常量，八组中至少一组必不匹配。

## 3. R31-02 —— 全类型全方法 + 源码扫描

文件：`WhitelistTests/Evidence/Runtime/DiagnosticConfigConsolidationTests.cs`（`Test_ModulesDoNotCreateIndependentConfigFiles`）

改前：只对每个控制类的 `Initialize` 做 IL 扫描。

改后：

- 对 `GunshotHostAudioControl` / `Bug1ProbeControl` / `Bug1GateProbe` 递归枚举自身与全部嵌套类型（含 `<>c`、`<>c__DisplayClass`），对每个 `DeclaredOnly` 方法与构造器统计 `BepInEx.Configuration.ConfigFile::.ctor`，总数必须为 0。
- 源码级兜底：遍历仓库 `Core/` 与 `Platform/` 全部 `.cs`，`StripComments` 去注释后统计 `new ConfigFile`、`new BepInEx.Configuration.ConfigFile`、以及三个旧 cfg 文件名，全部必须为 0（旧名只允许出现在注释里）。
- 仍断言三类不保留 `ConfigFileName` 静态字段。

判定：任何在任意方法（含 lambda/局部函数）里重新建独立 cfg 的改动都会同时触发 IL 计数与源码计数；字符串中出现旧文件名也会命中。

## 4. R31-03 —— 自证行前移到 PluginConfig.Bind

文件：`Core/Config/PluginConfig.cs` 与 `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs`

改前：在 `InitializeDiagnosticStage()` 内输出（stage 序列前段失败时这行消失）。

改后：

- `PluginConfig.Bind` 绑定完全部 entry、记录 `BoundConfigFilePath` 后无条件：
  `DiagnosticsLog.LogInfo(FormatDiagnosticsConfigLine());`
- `DiagnosticsLog` 是 `Logger.CreateLogSource("SteamP2PFriends")`（`BepInEx.Logging`），不依赖插件实例，也不走 RoleLogger（RoleLogger 会把含 `Diagnostic` 的消息降级为 Verbose 并改写前缀）。
- `PatchRegistrationDiagnosticsModules.InitializeDiagnosticStage()` 中原有发射删除。

覆盖论证：`Bind` 在 `Awake` 里第一个业务调用，早于所有诊断 stage 与 patch 注册；因此无论后续哪个 stage 失败，这行都已经进入日志。故不修改根 `SteamP2PFriendsPlugin.cs`。

`T145-02` 改为断言 `PluginConfig.Bind(ConfigFile)` 的 IL 调用 `FormatDiagnosticsConfigLine` ≥ 1。

## 5. R31-04 —— 未绑定路径

`PluginConfig.Bind` 末尾：路径为空时 `RoleLogger.Warn("[Shared]", "... configPath=<unbound>")` 一次；格式化函数把空/null 渲染为 `configPath=<unbound>`；T145-05 覆盖。

## 6. 变异表

副本：`D:/Agent-工作目录/DevelopMyUNMultiplayerModAndModloader/SPF-t153mut`（robocopy 复制，已删除）。每个变异在副本内 `-t:Rebuild` 后跑全量 exe；主树未触碰，还原后主树 sha 与第 8 节一致。

### M1 — R31-01 写死 `bug2HostGunshot`

- 文件：`Core/Config/PluginConfig.cs`
- 改前：`+ " bug2HostGunshot=" + BoolText(bug2HostGunshot);`
- 改后：`+ " bug2HostGunshot=" + BoolText(false);`
- 变异 DLL sha256：`dcdb0a0e0910caf5101449abfdfda015e2d18e7ca421a5c63a9cfba39322a471`
- 结果：`841/846`，红 `T145-04`（true 窗口行内为 False）、`T145-05`（truth-table mismatch，gun=True 期望 True 实际 False）、`T145-06`（wiring）。

### M2 — R31-02 旁路旧 cfg（新增私有方法，从 SettingChanged 回调调用）

- 文件：`Platform/Diagnostics/Patches/Bug1ProbeControl.cs`
- 改前：`OnEnabledChanged` 方法体首行为空（直接 `OnEnabledChangedCore()`）。
- 改后：在 `OnEnabledChanged` 开头插入 `__LoadLegacyCfg();`，并新增：

```csharp
private static void __LoadLegacyCfg()
{
    var legacy = new BepInEx.Configuration.ConfigFile(
        System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "SteamP2PFriends.Bug1Probe.cfg"), true);
    _ = legacy;
}
```

- 变异 DLL sha256：`0d5704201bd19a0642a684d42d02633e40ded1f5891e27e43ccc9610f77d340a`
- 结果：`843/846`，红 `T145-03`（`Bug1ProbeControl (all methods incl. compiler-generated lambdas) must not construct ConfigFile (found 1)`）。

## 7. 三命令（主树，均 `-t:Rebuild`）

| 命令 | 结果 |
| --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | 0 警告 / 0 错误 |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | 0 错误 / 4 条既有警告 |
| `./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `844/846`，仅 GBA01/NIL01，exit 1 |

## 8. 收工哈希（主树）

| 文件 | 行 | 字节 | sha256 |
| --- | --- | --- | --- |
| `Core/Config/PluginConfig.cs` | 205 | 12903 | `71f07e15f78128401777f55d305b7cf6ce1ed3f68f3272a0a8af86ab6de378ec` |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs` | 199 | 9254 | `0ace2d8a53afc050208a92f8652a124b2d63349225671f077687422766922b82` |
| `WhitelistTests/Evidence/Runtime/DiagnosticConfigConsolidationTests.cs` | 372 | 19207 | `b3bd2409b9cd7b3e5864d32ca3cd5c141ee9339c8d243ebb0b5eaa3271c18499` |
| `WhitelistTests/Program.cs` | 1177 | 163257 | `756372208be9e222ed58b7cd2fd26db0311c314c67d08e663f6e3948ecba3fa0` |
| `bin/Release/SteamP2PFriends.dll` | — | — | `d368fa2ca58ce9e5b1aba56f7d83d029bc007d872c8073d386fc83177824c49d` |

根 `SteamP2PFriendsPlugin.cs` 未改（工作区字节与 HEAD 一致，`git diff` 为空）。

说明：因 R31-03 把发射点移到 `PluginConfig.Bind`，生产字节从 t145 的 `43068b2f…` 变为 `d368fa2c…`；`Release/` 未写入，testkit-2 由 Captain 统一重打。

## 9. UNKNOWN / 兼容性

- 旧三个独立 cfg（`SteamP2PFriends.Bug1Probe.cfg`、`SteamP2PFriends.Bug1Gate.cfg`、`SteamP2PFriends.Bug2HostGunshot.cfg`）自本版本起不再读取，旧值不迁移；源码只允许在注释中提及（T145-03 守护）。
- 自证行验证的是「配置已绑定、三项取值」，不证明三个模块的真实动作已在实机执行；热开启路径由 T145-04（真实 Harmony + FakeGun）覆盖。
- 实机听感、1H+2G 三端互听、Mod 自定义音效兼容仍为 UNKNOWN（上游票遗留）。
- `Release/0.2.4.11-testkit-1` 未改动；testkit-2 由 Captain 在复审后重打。

## 10. t157 更正（R32-01 / R32-02 / R32-03 / R32-04 / R32-05）

> 本节由 t157 追加。§7/§8 的数字（`844/846`、`d368fa2c…`）保留为 t153 当时的历史记录，但**已被本节取代**，不得再作为当前交付哈希引用。

### 10.1 R32-01：实际快照、DLL 哈希与时间线

- 冻结快照来源：**工作树 + index**（t155 收口后的字节；不含 01:15 那次根 `SteamP2PFriendsPlugin.cs` 改写）。
- 三命令（`-t:Rebuild`，主树）：生产 `0 警告 / 0 错误`；测试 `0 错误 / 4 条既有警告`；exe **845/847**（仅 GBA01/NIL01），T145-01..06 与 Bug2-01..40 全 PASS。
- 实际 DLL sha256：`623b32045685d81f9c565cb15ed235b12395d7f4f1c14d0d612c237a36328db2`（1584640 B 量级与 t155 一致；本轮仅测试侧改动，生产 DLL 不变）。
- 时间线（关键）：
  1. 01:15:35 —— 越界改写根 `SteamP2PFriendsPlugin.cs`（把自证行移入 Awake）。
  2. 01:18:33 —— t153 在该改写仍存在的状态下构建出 `d368fa2c…`（因此它不是任何可复现源码快照的产物）。
  3. 01:39:36 —— 根文件按 HEAD 字节回退，`git diff` 为空；发射点最终留在 `PluginConfig.Bind`。
  4. t157 —— 在回退后的工作树上重跑三命令，得到 `623b3204…`；`d368fa2c…` 作废。
- 结论：`d368fa2c…` 不是交付哈希；可复现交付哈希是 `623b3204…`。

### 10.2 R32-02：T145-02 钉住零参数重载

- `DiagnosticConfigConsolidationTests.Test_StartupConfigLineFormat` 在原有「按名字计数 >= 1」之后，新增对 `PluginConfig.Bind` IL 的检查：找到 `FormatDiagnosticsConfigLine` 的调用点，断言其 `MethodInfo.GetParameters().Length == 0`。
- 变异 MV7（`DiagnosticsLog.LogInfo(FormatDiagnosticsConfigLine());` → `FormatDiagnosticsConfigLine(BoundConfigFilePath, false, false, false)`）：
  - 变异 DLL sha256：`87967c20b3eb5122c2dec778299c88e29c7dbe03a9299ee8fb46adfd9d47493b`
  - 结果：`844/847`，红 `T145-02`，消息 `PluginConfig.Bind must call the zero-argument FormatDiagnosticsConfigLine overload, got 4 parameter(s)`。
- 变异在仓库外副本 `SPF-t157mut` 完成并逐字节还原，副本已删除；主树未被变异触碰。

### 10.3 R32-03：单写者与开工/收工哈希

- 本窗口内 t157 为唯一写者；未再有并发改写。
- 开工快照（= t155 收口态）：exe 845/847，DLL `623b3204…`；文件：`DiagnosticsModules`（t155 注释版）、`GunshotHostAudioTests`（Bug2-39/40 版）、`Program.cs` `fb50a073…`、`PluginConfig.cs` `71f07e15…`、`GunshotHostAudioControl.cs` `fd765c83…`。
- 收工快照：exe 845/847，DLL 仍 `623b3204…`（本轮仅 `DiagnosticConfigConsolidationTests.cs` 变更）；该文件收工 sha256 `f084c63bca3bedfba5ad69011496991dfbefb53088b0b15dacb3be0e39d795b1`。
- 复审期间如需再修补：先在任务写 `evidence_note` 声明字节变更并重新绑定 attempt，再让复审从新快照开始。

### 10.4 R32-04：红名单口径更正

- 原口径「删 `configPath` 段只红 T145-02」与 T145-05 的精确匹配冲突。
- 更正为：**至少红 T145-02；允许 T145-05 一并红**；其余红名单仅记录，不作为通过条件。

### 10.5 R32-05：关闭态口径更正

- 原「关闭时零订阅」与热开关所需的 `SettingChanged` 回调冲突。
- 更正为：**关闭态不安装 patch、不订阅游戏事件、不产生周期写入；允许订阅配置变更回调**（该回调仅用于当场热开关）。

## 11. t157 attempt 2 最终快照（取代 §10.2 的零参数方案与 §8 的旧 DLL）

> 本节是 t157 的最终交付记录；§8 的 `d368fa2c…` 与 §10.2 的「零参数重载」方案均已被本节取代，仅供追溯。

### 11.1 快照来源与三命令（最终源码状态）

- 快照来源：**工作树 = index**（t157 收工前已 `git add`；本报告写入后同一批 stage）。不含任何未声明的中间态。
- 三命令（均 `-t:Rebuild`，主树）：
  - 生产：`0 警告 / 0 错误`
  - 测试：`0 错误 / 4 条既有警告`
  - exe：**846/848 PASS**，仅 `GBA01`、`NIL01` 两条既有环境失败（exit 1），T145-01..07 与 Bug2-01..40 全 PASS。
- 实测 DLL sha256：`01c8bb75cc4200f7750f68a1151e05ceaf1eff1bf8b5777e25073d78636bb5ce`
- 同一源码连续两次 `-t:Rebuild` 得到**相同**哈希（两次均为 `01c8bb75…`），本仓库本轮未观察到偶发不一致；以该值为准。

### 11.2 源码 sha256（与 DLL 成对）

| 文件 | sha256 |
| --- | --- |
| `Core/Config/PluginConfig.cs` | `2c4374d608320f6a62a0e93fc6377740a08da7ff1a886b64afa84481e7119bba` |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs` | `f99095d7d8e441ec6fdb082e71acd6cd484276265903f530c0cdddf95aef09e0` |
| `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs` | `7197386ce5dedf2c14331e6b4b101d93a3c4609e2b5bdf98892d36b763870122` |
| `WhitelistTests/Program.cs` | `5fb596d8f692b52a4ca593121efdbab3e04cb77ca31b6c759e4490d651be54bf` |
| `WhitelistTests/Evidence/Runtime/DiagnosticConfigConsolidationTests.cs` | `73b4a76cc79d9cbbb353f618f451e3b606801daeef6564eaf613587febcbcf10` |

- EOL：上述 `PluginConfig.cs` 与 `DiagnosticConfigConsolidationTests.cs` 均为 **LF**（`grep -c $'\r'` = 0），本轮新增/修改行沿用 LF；成对记录即「LF 源码 sha256 + DLL `01c8bb75…`」。审查官报告的 LF `6489b8fa…` 对应的是另一份中间树（含当时的测试修复），非本快照。

### 11.3 R32-02：读开关与打印分层的最终形态

- `PluginConfig.ReadDiagnosticsSwitches()` 只读三个 `ConfigEntry.Value`，返回 `(bool bug1Probe, bool bug1Gate, bool bug2HostGunshot)`。
- `PluginConfig.Bind` 发射点：`DiagnosticsLog.LogInfo(FormatDiagnosticsConfigLine(BoundConfigFilePath, ReadDiagnosticsSwitches()))`。
- 新增双参重载 `FormatDiagnosticsConfigLine(string, (bool,bool,bool))`，内部转调四参纯函数；零参数 wrapper 仍保留给测试。
- `T145-02` 的 IL 契约：`Bind` 必须调用 `ReadDiagnosticsSwitches()`，且 `FormatDiagnosticsConfigLine` 调用点的参数个数必须为 **2**（旧断言「零参数」已废除）。
- 新增 `T145-07`：用一份三项 `Enable=true` 的主 cfg 完整 `Bind`，断言 `ReadDiagnosticsSwitches()` 三值全 true、启动行三项全 `True`；再把三个 entry 置 false，断言两处同步变 `False`；finally 重新绑定默认主 cfg。
- 变异（仓库外副本 `SPF-t157mut2`，逐字节还原，副本已删除）：
  - MA（发射点改常量实参）：`FormatDiagnosticsConfigLine(BoundConfigFilePath, false, false, false)` → DLL `62b10d953821d550588e4374f21d9573ab96249b8578d3e18a68ac14b7ceb462`，845/848，红 `T145-02`（`PluginConfig.Bind must call ReadDiagnosticsSwitches() before formatting`）。
  - MB（读取点写死）：`ReadDiagnosticsSwitches()` 第三项 → `false` → DLL `a89cc1254e089e4af302b6ac6ba158c4c05248edf2fb10c4b5b693d165cb0f9d`，843/848，红 `T145-07`（`got True/True/False`），并连带红 `T145-06`（允许的更严结果）。

### 11.4 R32-03：单写者开工/收工哈希

| 文件 | 开工（t155 收口 / index） | 收工（本轮交付 / index=worktree） |
| --- | --- | --- |
| `Core/Config/PluginConfig.cs` | `71f07e15f78128401777f55d305b7cf6ce1ed3f68f3272a0a8af86ab6de378ec` | `2c4374d6…` |
| `DiagnosticsModules.cs` | `f99095d7…` | `f99095d7…`（未改） |
| `DiagnosticConfigConsolidationTests.cs` | `b3bd2409…` | `73b4a76c…` |
| `GunshotHostAudioTests.cs` | `7197386c…` | `7197386c…`（未改） |
| `WhitelistTests/Program.cs` | `fb50a073…` | `5fb596d8…` |
| 生产 DLL | `623b3204…`（845/847） | `01c8bb75…`（846/848） |

本轮窗口内 t157 为唯一写者，无并发改写；若复审期需修补，先写 `evidence_note` 声明字节变更并重新绑定 attempt。

### 11.5 R32-04 / R32-05：口径更正（作为报告正式口径）

- **R32-04（红名单）**：删 `configPath` 段 / 把发射点改成常量实参时，**至少红 T145-02；允许 T145-05、T145-06、T145-07 等一并红**；其余红名单仅记录，不作为「唯一红」的通过条件。
- **R32-05（关闭态）**：关闭态**不安装 patch、不订阅运行时事件、不产生周期写入**；**允许保留配置变更订阅**（`SettingChanged` 是热开关的必要条件）。不得表述为「关闭态零订阅」。

### 11.6 时间线（R32-01 结论）

1. 01:15:35 越界改写根 `SteamP2PFriendsPlugin.cs`（自证行移入 Awake）。
2. 01:18:33 在该改写存在时构建出 `d368fa2c…`——它不是任何可复现源码快照的产物。
3. 01:39:36 根文件按 HEAD 字节回退（`git diff` 为空），发射点最终留在 `PluginConfig.Bind`。
4. t157 在回退后的工作树上重跑三命令，得 `01c8bb75…`；`d368fa2c…` 作废，不得作为交付哈希。
