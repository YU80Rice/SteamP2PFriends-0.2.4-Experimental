# t154 工程轴复审：t153 诊断配置合并（round 2）

审查对象：t153（r10，attempt 2）交付的 `PluginConfig` 主 cfg 合并 + 启动自证行 + T145-05/06。
审查者：reviewer-eng（review-round-2），attempt `1fedcc71-122c-485a-81fb-fb8aa737994e`。
判定：**needs_revision**（0 blocker / 0 high / 3 medium / 2 low）。代码行为本身在「按 index 快照」上成立；打回的三条都属于**证据链与守护边界**，不是功能缺陷。

## 0. 一句话结论

配置放错路径导致静默关闭这一类故障，在**代码层**已被消除（三模块不再自建 cfg、启动行在 Bind 完成处无条件发出）；但「测过的字节 = 报告的字节 = 交付的字节」这条链在本轮**不成立**：声称的 DLL `d368fa2c` 无法由任何现存源状态重建，且自证行的**实参**没有被任何用例钉住（变异 MV7 全绿）。

## 1. 字节锁与快照（本轮的关键事实）

审查开始时主树处于「工作树 ≠ index」状态，且两者都不是 t153 声称测试的那份：

| 文件 | index（`git show :path`） | 工作树 | t153 报告 |
| --- | --- | --- | --- |
| `Core/Config/PluginConfig.cs` | `71f07e15…`（12903B/205 行） | 同 index | `71f07e15` ✓ |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs` | `0ace2d8a…`（9254B/199） | `f99095d7…`（01:47 改） | `0ace2d8a` ✓ |
| `WhitelistTests/Program.cs` | `75637220…`（163257B/1177） | `fb50a073…`（01:51 改） | `75637220` ✓ |
| `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs` | `6b49f543…` | `7197386c…`（01:58 改） | 未列 |
| `WhitelistTests/Evidence/Runtime/DiagnosticConfigConsolidationTests.cs` | `b3bd2409…`（19207B/372） | 同 index | `b3bd2409` ✓ |

- **按 index 快照**（仓库外副本 `_t154pin`，四文件用 `git show :path` 钉住）重建：生产 0 错 0 警，DLL = **`c45c34c1…`**（连续两次一致），测试 0 错 4 既有警告，exe **844/846**（仅 GBA01/NIL01），T145-01..06 全 PASS。
- **按工作树**重建：DLL = **`623b3204…`**（连续三次 + 全新副本一致），exe **845/847**（多出的一条来自 01:51 之后新增/改动的用例）。
- t153 声称的 **`d368fa2c…` 两者都不是**，且 `Release/*/SteamP2PFriends.dll`（gate-probe-1 `dd75d956`、preview2 `d9254a24`、preview3 `9fe6a0c8`、preview4 `7168ace4`、preview5 `7d625908`、testkit-1 `cc816b2e`）里也没有它。
- 时间线最可能的解释：t153 的构建发生在 `SteamP2PFriendsPlugin.cs`（01:15 越界改写，01:39:36 回退）**回退之前**，回退后未重建即写报告；因此 `d368fa2c` 对应一个已不存在的源状态。

> 因此本报告的**行为结论以 index 快照（c45c34c1 / 844/846）为准**；工作树快照只用于说明漂移。

## 2. 复跑（本人实测）

| 命令 | index 快照 | 工作树 |
| --- | --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | exit 0，0 警 0 错 | exit 0，0 警 0 错 |
| `dotnet build WhitelistTests/…csproj -c Release -t:Rebuild` | exit 0，0 错 4 既有警告（CS0649×3+CS0219） | exit 0（需 01:58 之后的测试文件；01:56 的快照编译失败 CS0104/CS1503） |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | exit 1，**844/846**，FAIL 仅 GBA01/NIL01 | exit 1，**845/847**，FAIL 仅 GBA01/NIL01 |
| T145 段 | T145-01..06 全 PASS | T145-01..06 全 PASS |

DLL：index `c45c34c12534e070…`，工作树 `623b32045685d81f…`，报告声称 `d368fa2ca58ce9e5…`。

## 3. 变异（仓库外副本，逐字节还原）

副本 `_t154pin`（index 快照）；每条：改源 → `-t:Rebuild` → `cp` DLL 到测试 bin → 跑 exe → 用备份字节还原。

| 变异 | 改动点 | 变异 DLL | 结果 | 新红 |
| --- | --- | --- | --- | --- |
| MV1 | `PluginConfig.cs` 纯函数 `BoolText(bug2HostGunshot)` → `BoolText(false)` | `9ab126cd…` | 841/846 | T145-04/05/06 |
| MV3 | `PluginConfig.cs` `"[Diagnostics] configPath="` → `cfgpath=` | `fffd5097…` | 842/846 | **T145-02 + T145-05** |
| MV4 | `GunshotHostAudioControl.cs` 在取 entry 前插 `new ConfigFile("…Bug2HostGunshot.cfg", false)` | `80304b0e…` | 843/846 | T145-03 |
| MV5 | `PluginConfig.cs` 删 `DiagnosticsLog.LogInfo(FormatDiagnosticsConfigLine())` | `97654b0c…` | 843/846 | T145-02 |
| MV6 | `PluginConfig.cs` 无参重载里 `Bug2HostGunshotEnable != null && …Value` → `false` | `67feef00…` | 842/846 | T145-04/06 |
| MV7 | 发射点改成 `FormatDiagnosticsConfigLine(BoundConfigFilePath, false, false, false)` | `09a329db…` | **844/846** | **无（全绿）** |
| MV2 | gate `OnEnabledChanged` 内插 `new ConfigFile("…Bug1Gate.cfg")` | 编译失败（未产出 DLL） | — | — |

- MV1/MV4/MV5/MV6 与 t153 自报方向一致；MV4 的红名单（只红 T145-03）与 Captain 的 `ad17a96e` 实测一致。
- **MV7 是本轮最重要的负结果**：把生产发射点的实参换成常量后全绿，说明 T145-02 只钉了「Bind 里调用了同名方法」，没有钉「用的是真实 entry 值」。
- MV3 同时红 T145-05，因为 T145-05 的期望串是精确匹配；这与 acceptance 写的「只红 T145-02」不符，但属于**更严格**的约束，不是缺陷（见 R32-04）。
- MV2 的编译失败是我插入的语句缺少 `Bep2Ex.Configuration` 可见性/命名空间上下文导致，属**变异构造失败**，不作为结论；「新增 ConfigFile ctor 必须红」已由 MV4 覆盖。

## 4. 逐条 acceptance

| # | 判据 | 结果 | 证据 |
| --- | --- | --- | --- |
| 1 | 恢复独立 ConfigFile → 红且只红 T145-03 | passed（以 MV4 代 MV2） | 843/846，只红 T145-03 |
| 2 | 删 configPath 段 → 红且只红 T145-02 | passed（更严） | 842/846，红 T145-02+05 |
| 3 | 全仓无 `new ConfigFile`/`ConfigFileName`/旧文件名读取 | passed | 生产 `Core`+`Platform` grep：`new ConfigFile` 0、`ConfigFileName` 0；旧 cfg 名只出现在注释 |
| 4 | 关闭态零动作 | passed（口径需澄清） | gunshot `Apply`：disabled → `Uninstall()` + `SetEnabled(false)`，不 Register；probe/gate 仅 `_enabledEntry.Value` 为真才 Start/Arm；三处仍 `SettingChanged +=`（热开关必需，见 R32-05） |
| 5 | 自证行来自真实注册路径、绕开 RoleLogger 有据 | passed | `PluginConfig.Bind` 末尾 `DiagnosticsLog.LogInfo(...)`；`RoleLogger.Info` 命中 DiagnosticMarkers → 走 `Diagnostic()`（受 `IsVerboseDiagnosticsEnabled` 门控），且 `NormalizeForOutput` 删 `[Diagnostics]` 前缀 |
| 6 | T145-01..04 不是形状断言 | **failed** | MV6 被抓（接线）、MV1 被抓（纯函数）、T145-01 断言具体默认值/绝对路径；但 **MV7 全绿** → 发射点实参未钉住 |
| 7 | 基线不掉、用例只增 | passed（index 快照） | 844/846 vs 上一轮 842/844；846 总数只增；工作树 845/847 亦只增 |
| 8 | 报告字节锁与实际一致 | **failed** | 声称 `d368fa2c`，index→`c45c34c1`、工作树→`623b3204`，且无任何 Release 包含 `d368fa2c` |

## 5. Findings

### R32-01（medium）交付字节锁不可复现
- 位置：`.scratch/takeover-v05-20261106/t153-diagnostic-config-r10.md` 的 DLL 行；`SteamP2PFriends.dll`。
- 事实：报告称 `d368fa2c…`；用 index 快照重建得 `c45c34c1…`，用工作树重建得 `623b3204…`，两者都稳定可重复；`Release/` 六个包内也没有 `d368fa2c`。
- 风险：复审与交付无法确认「跑过的 DLL」就是「要发布的 DLL」；若按报告哈希做发布比对会直接失败或放行错对象。
- requiredFix：在冻结快照上重跑生产/测试/exe 三命令，把**实际** DLL 哈希（及其来源快照 = index 或工作树）写入报告与 evidence_note；并在报告中说明 01:15 越界改写、01:39 回退与构建的先后关系。

### R32-02（medium）启动自证行的实参没有任何用例钉住
- 位置：`Core/Config/PluginConfig.cs:121`（发射点）、`WhitelistTests/Evidence/Runtime/DiagnosticConfigConsolidationTests.cs:81-83`（T145-02）。
- 事实：MV7 把发射点改成 `FormatDiagnosticsConfigLine(BoundConfigFilePath, false, false, false)` 后 **844/846 全绿**。T145-02 只用 `CountMethodCalls(..., "FormatDiagnosticsConfigLine") >= 1` 按名字计数，重载无关；T145-06 测的是无参重载本身，不覆盖 Bind 的调用点。
- 风险：三个开关全开、模块确实跑起来时，启动行仍可能打印 `bug1Probe=False bug1Gate=False bug2HostGunshot=False`（或反之），用户按启动行排障会被误导——恰是这条自证行存在的目的。
- requiredFix（二选一）：①在 T145-02 里断言 Bind 调用的是**零参数**重载（`IlContractProbe` 增加按参数个数匹配的重载计数）；②把「读三个 entry → 三个 bool」抽成 `internal static (bool,bool,bool) ReadDiagnosticsSwitches()`，再对该函数做 entry 真/假两组断言 + 对 Bind 做 IL 断言（调 `ReadDiagnosticsSwitches` 且其结果进入格式化调用）。

### R32-03（medium）审查期间主树被并发修改，「冻结」不成立
- 事实：`PatchRegistrationDiagnosticsModules.cs` 01:47、`WhitelistTests/Program.cs` 01:51、`GunshotHostAudioTests.cs` 01:58 均在 t153 报告（01:40）与我 claim（01:43）之后被改动；工作树与 index 不一致（`MM`×2、`AM`×1）；我 01:56 取的副本因测试文件处于中间态而编译失败（CS0104/CS1503），01:58 之后才恢复。
- 风险：任何以「主树已冻结」为前提的复审结论都会随时失效；本轮已出现「同一棵树两次构建得到不同测试总数」（846→847）。
- requiredFix：复审窗口内单写者；开工与收工各记录 index/工作树哈希；如需在复审期间修补，先在任务上写 evidence_note 声明字节变更并重新绑定，再让复审从新快照开始。

### R32-04（low）acceptance 文本与 T145-05 的精确匹配冲突
- 事实：acceptance 要求「删掉 configPath 段 → 只红 T145-02」，但 T145-05 的期望串精确包含 `configPath=`，因此必然同时红 02 与 05。
- requiredFix：把该条改为「至少红 T145-02；允许 T145-05 一并红」或「只要求 T145-02 红，其余红名单仅记录」。

### R32-05（low）「关闭态不订阅事件」的字面口径不成立
- 事实：三个模块在 disabled 时仍 `_enabledEntry.SettingChanged += …`（热开关必需），只是不装 patch、不启动采集。
- requiredFix：票面改为「关闭态不安装 patch、不订阅游戏事件、不产生周期写入；允许订阅配置变更回调」。

## 6. 边界与声明

- 本判定只覆盖 **index 快照**（`PluginConfig 71f07e15`、`DiagnosticsModules 0ace2d8a`、`Program 75637220`、`DiagnosticConfigConsolidationTests b3bd2409`、`GunshotHostAudioTests 6b49f543`）；工作树后续改动不在本次结论内。
- **主树 bin 被我重建覆盖**：为取基线我在主树跑过 `-t:Rebuild`，`bin/Release/SteamP2PFriends.dll` 现在是工作树快照的 `623b3204…`，原 `d368fa2c…` 已不可恢复（本就不可重建）。源文件未被本审查者修改；副本 `_t154pin` 已删除，其源哈希与 index 一致。
- 未声称：Bug1 已修复、枪声在实机可听到、G 门已验证。
- UNKNOWN（沿用 t153）：真实 BepInEx 在游戏内创建/落盘主 cfg 的行为；启动行在实机日志的可见性（需 1H+1G 诊断包）；Bug1Probe/Bug1Gate 真实开启的副作用。

## 7. 复现命令

```bash
# 1) index 快照
git show :Core/Config/PluginConfig.cs > /tmp/pin/Core/Config/PluginConfig.cs   # 其余三文件同理
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild          # → c45c34c1…
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/
WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe      # → 844/846

# 2) MV7（应为红，实际全绿）
# PluginConfig.cs:121
#   DiagnosticsLog.LogInfo(FormatDiagnosticsConfigLine());
# →
#   DiagnosticsLog.LogInfo(FormatDiagnosticsConfigLine(BoundConfigFilePath, false, false, false));
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild          # → 09a329db…
cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/
WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe      # → 844/846（全绿）
```

<!-- self_sha256(body)=13849b48ba57c2ef337bd5d809f388126e95745104cf1d92e0ffaa8a792a1da6 -->

### 8.1 t157 attempt 2 补记（R32-02 改为双层拆分）

- 原 §8 的 R32-02 记录的是「零参数重载」方案；t157 按 Captain 指出的更强口径改为“读 / 印”两层：
  - `ReadDiagnosticsSwitches()` 唯一读三个 `ConfigEntry.Value`；
  - `Bind` 调用 `FormatDiagnosticsConfigLine(BoundConfigFilePath, ReadDiagnosticsSwitches())`（双参重载）；
  - `T145-02` IL 契约：必须调用 `ReadDiagnosticsSwitches()`，且格式化调用参数个数 == 2；
  - 新增 `T145-07`：全 true 主 cfg 完整 `Bind` 后三值全 true / 启动行全 `True`，entry 置 false 后同步 `False`。
- 复跑：MA（发射点常量实参）DLL `62b10d95…` → 845/848，红 `T145-02`；MB（读取点写死 false）DLL `a89cc125…` → 843/848，红 `T145-07`（`True/True/False`）并连带红 `T145-06`。
- 最终快照三命令：生产 0/0、测试 0 错 4 警、exe **846/848**（仅 GBA01/NIL01）；DLL `01c8bb75…`；两次 Rebuild 哈希一致。详见 t153 报告 §11。
