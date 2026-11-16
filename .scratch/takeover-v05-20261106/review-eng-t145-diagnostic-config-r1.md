# t147 工程轴复审 t145（诊断配置合并）— r1

## §0 结论

**verdict = needs_revision**（0 blocker / 0 high / 2 medium / 3 low）。

t145 的主线目标**成立**：三个诊断模块确实不再创建/读取独立 cfg，开关统一走插件主 cfg，
热开热关仍然有效，启动自证行确实由真实注册路径发出。但我自跑出两条「停用仍然全绿」
的缺口，都落在这张票的核心命题上：

- **R31-01（medium）**：自证行里的三个布尔只被「和自己比较」的方式断言，
  把 `FormatDiagnosticsConfigLine()` 的三个值**写死成 False** 后 842/844 全绿
  （M3）。也就是说，这行本来用于「用户一眼确认配置有没有被读到」，
  却可以在测试全绿的情况下长期说假话——正好回到本票要消灭的那类静默失败。
- **R31-02（medium）**：T145-03 的 IL 契约只扫三个类的 `Initialize` 方法体；
  在同一个类里**另加一个方法** new ConfigFile（从 `OnEnabledChanged` 调用）842/844 全绿（M4）。
  「全仓无 `new ConfigFile`」目前只是源码事实，不是被守护的约束。

另有 3 条 low：启动行在注册流程第 6 阶段才发（前 5 阶段任一失败即整行消失）、
`BoundConfigFilePath` 为空时的静默、以及 testkit-1 交付包仍带旧独立 cfg 样本 + 旧 DLL。

Captain 的两条对照变异我独立复现：M2 逐位相同（`67ca5cb1`），M1 红名单相同（哈希 `2964fee2`）。

**pass 边界**：本判定不涉及 Bug1 是否修复、不涉及枪声能否在实机听到；
本轮只回答「配置合并是否消除『放错路径即静默关闭』这一类故障、且未引入新的静默退化」。

## §1 字节锁（实测 = 送审）

| 文件 | 字节 | 行 | sha256 |
| --- | --- | --- | --- |
| `Core/Config/PluginConfig.cs` | 10858 | 170 | `aa8a22e98fd0efd5f38b02b975a89d2fe866003a6595983842cdc820a2d51765` |
| `Core/Patches/GunshotHostAudio/GunshotHostAudioControl.cs` | 4926 | 137 | `fd765c83f99dd77988711d19e3d17799c3f36900ca01f012c415bcf7ae5541c9` |
| `Platform/Diagnostics/Patches/Bug1ProbeControl.cs` | 23430 | 457 | `ff38ff53f7befd77ab17612fdc48dfe17acccfd2b76d55b108a5755269ec597c` |
| `Platform/Diagnostics/Patches/Bug1GateProbe.cs` | 81069 | 1763 | `9f7b2f97599ceb1f516b28d4227975b1f630d9f0d61d7b786be80a78ffb39e7a` |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs` | 9912 | 211 | `d1d24f23490d6de8cec6c130ae65f148bdf519d9d9e41f48094e36db00d0e19e` |
| `WhitelistTests/Program.cs` | 162884 | 1175 | `f9324bdb6b8d67b1d6d80d653ced56e21971b7a76325aa37e63a8a6cf70a63e3` |
| `WhitelistTests/Evidence/Runtime/DiagnosticConfigConsolidationTests.cs` | 9332 | 163 | `a469c4f2d8dd0782a8fb49c72f539c6f95b4539061ed43e442c31934dd95cb6e` |
| `SteamP2PFriends.csproj` | 28133 | 416 | `e327865c031521fde970c139dc31c15eb02fcb648ebf9bd3ce75f46f0b4a68e5` |
| `bin/Release/SteamP2PFriends.dll` | 1593344 | — | `43068b2f795a788b9cc8579ba80b22a4c317e2b2139a5b46aa94ffa37040cbf0` |

Bug3 五文件未变：`a89f40b9` / `7a82de8e` / `3d5044f7` / `a4553385` / `5b0c4e9b`（前缀）。

## §2 三命令复跑（本人实测）

| 命令 | 结果 |
| --- | --- |
| `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` | exit 0，0 警告 0 错误，1.59s，DLL 复现 `43068b2f`（确定性构建） |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` | exit 0，0 错误（4 条既有警告） |
| `./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | exit 1，**842/844 PASS (Failed: 2)**，仅 `GBA01` / `NIL01`（控制台缺 Unity 运行时，`TypeInitializationException → SDG.Unturned.SteamPlayer → NetReflection → ReflectionTypeLoadException`）；T145-01..04 全 PASS |

副本 `../_t146mut`（`tar` 排除 .git/.scratch/bin/obj/Release/publishi/Assets/.agent-teams/Develop-Stage，并 `cp -a Libs` 到同级）独立重建同样得 `43068b2f` / 842/844。

## §3 变异表（自跑，仓库外副本，逐字节还原）

| 变异 | 改动点（文件:行，改前 → 改后） | DLL sha256 前16 | 结果 | 新增红用例 |
| --- | --- | --- | --- | --- |
| **M1** 模块重新自建 cfg | `GunshotHostAudioControl.cs:46` 后插入 `_ = new BepInEx.Configuration.ConfigFile("SteamP2PFriends.Bug2HostGunshot.cfg", false);` | `2964fee206285780` | 841/844 | **T145-03**（`must not construct ConfigFile (found 1)`） |
| **M2** 自证行键名漂移 | `PluginConfig.cs:159` `"[Diagnostics] configPath="` → `"[Diagnostics] cfgpath="` | `67ca5cb16bcfd24a` | 841/844 | **T145-02**（前缀断言） |
| **M3** 三个布尔写死 | `PluginConfig.cs:160-162` `+ " bug1Probe=" + BoolText(Bug1ProbeEnable)` 等三行 → `+ " bug1Probe=False"` 等 | `1f4edf0f1c236fbe` | **842/844 全绿** | 无 ← **R31-01 证据** |
| **M4** 另一个方法自建 cfg | `Bug1GateProbe.cs:130` 前加私有方法 `__LoadLegacyCfg()`（内含 `new ConfigFile("SteamP2PFriends.Bug1Gate.cfg", false)`），并在 `OnEnabledChanged` 首行调用 | `3ee44f4e8b56dbed` | **842/844 全绿** | 无 ← **R31-02 证据** |
| **M5** 热开失效 | `GunshotHostAudioControl.cs:51` 删 `_enabledEntry.SettingChanged += OnSettingChanged;` | `7a6bab3c9780ce89` | 841/844 | **T145-04**（`hot enable from main cfg must install patches`） |

还原：五条变异逐条 restore 后重建 DLL 均回 `43068b2f`，源文件九个哈希回 §1；副本已删除。

对照 Captain：
- M2 **逐位相同**（`67ca5cb1`，841/844，只红 T145-02）；
- M1 红名单相同、哈希不同（我 `2964fee2` / Captain `ad17a96e`）——插入文本不完全一致所致，红名单与计数一致。

## §4 五个重点的逐条判定

**1. 「旧 cfg 不再被读」——成立，且不是「只删了调用」。**
- 全仓 `Core/` + `Platform/` 中 `new ConfigFile` / `ConfigFileName` **0 命中**（grep 两次，含 `.ctor` 形式）；
- 三个 `.cfg` 文件名只出现在 4 处**注释**（`PluginConfig.cs:30`、`GunshotHostAudioControl.cs:43`、`Bug1ProbeControl.cs:40`、`Bug1GateProbe.cs:68`），无路径拼接、无 `File.Exists`、无目录扫描
  （全仓 `File.Exists` 仅 `BuildFingerprint.cs` 与 `HarmonyCompatibilityAudit.cs`，与 cfg 无关）；
- 三个模块的入口都直接取 `Core.Config.PluginConfig.*`（`Gunshot:46`、`Probe:41-43`、`Gate:71-78`），并在 entry 为 null 时显式报 `main config not bound`；
- 旧 cfg 里的 true 不会被任何代码路径读取——因此**旧 cfg 的 true 不会「残留在旧文件里假装生效」**，但也**不会迁移**（`PluginConfig.cs:30-31` 注释已声明），用户必须在新主 cfg 里重新打开。

**2. 热开热关仍有效——成立。**
三处都是 `_enabledEntry.SettingChanged += OnSettingChanged/OnEnabledChanged`（`Gunshot:51`、`Probe:51`、`Gate:85`），
回调里重新 `Apply/TryStart`（`Gunshot:91-101`、`Probe:62-92`、`Gate:130+`），不是启动读一次的快照。
M5（删订阅）→ T145-04 变红，说明这条被测住；T145-04 走的是真实 `Initialize(entry) → entry.Value=true → SettingChanged → Install` 链路，不是直调 `Apply`。

**3. 启动自证行的位置与「绕开 RoleLogger」的判断——成立。**
- 发出点在 `InitializeDiagnosticStage()` 内（`PatchRegistrationDiagnosticsModules.cs:44-52`），该函数是注册计划 **stage 6**（`PatchRegistrationOrchestration.cs:43-46`）的真实执行体；
  T145-02 用 IL 契约（`CountMethodCalls(stage, "PluginConfig", "FormatDiagnosticsConfigLine") >= 1`）钉住「真的从 stage 发」，不是只在测试里调格式化函数；
- 绕开 RoleLogger **有据**：`RoleLogger.Info` 对含 `Diagnostic` 的消息走 `Diagnostic()`，而 `RoleLogger.cs:82-84` 在 `PluginLogPolicy.IsVerboseDiagnosticsEnabled == false` 时直接 return；
  且 `NormalizeForOutput` 的 `InternalDiagnosticBracketTag`（`RoleLogger.cs:47-50`）会把 `[Diagnostics]` 整个删掉。两条都成立 → 这行若走 RoleLogger，默认日志里**既看不到值、也看不到前缀**。注释（`:41-43`）与实现一致。
- 直接 `Logger.LogInfo`（BaseUnityPlugin 的 ManualLogSource）落在 BepInEx 标准日志里，前提是 `Logger != null`（`:46`），异常被 catch 成 RoleLogger.Error（`:49-52`），不会打断注册。

**4. T145-01..04 的形状 vs 语义——部分不成立，见 §6 R31-01/R31-02。**
- T145-01（entry 非空 + 默认值 + 路径绝对）是**语义**断言，把默认值改掉会红；
- T145-02 的前缀/片段断言是**形状**断言，但配合「值取自 entry」本应等价于语义——问题在三个默认值全是 False，于是「写死 False」与「真读 entry」不可区分（M3）；
- T145-03 是 IL 契约（强于形状），但**作用域只有 `Initialize` 一个方法**（M4）；
- T145-04 是**语义**断言（真实 Harmony 安装/卸载），M5 变红，可信。

**5. 新增静默退化——两处，均 low。**
- `BoundConfigFilePath` 为空（`PluginConfig.cs:105` `config.ConfigFilePath ?? string.Empty`）时，自证行输出 `configPath=` 空值，不报警；测试里 `Length > 0` 会先红，但生产没有兜底提示（R31-04）；
- 自证行只在 stage 6 发出，而 orchestrator 在任一 stage 返回 false 时**立即 return false**（`PatchRegistrationOrchestrator.cs` 的 `foreach` + `if (!stage.Execute()) return false`）→ 前 5 个 stage 任一失败，用户拿不到这行（R31-03）。

## §5 其余核查

**关闭态零动作——成立。**
`GunshotHostAudioControl.Apply`（`:110-130`）：`enabled=false → GunshotHostAudioPatch.Uninstall()`，不装 patch；
`Bug1ProbeControl.Initialize`（`:55`）只有 `if (_enabledEntry.Value) TryStart("startup")`；
`Bug1GateProbe.Initialize`（`:86`）只有 `if (_enabledEntry.Value) Arm("startup")`。
三个模块**唯一无条件动作**是 `_enabledEntry.SettingChanged += handler`（配置事件，热开所需），
不订阅游戏事件、不建目录、不写 trace、不装 Harmony patch；T145-04 的「disabled ⇒ !Installed」也被实测断言。
t145 新增的 `BoundConfigFilePath` 只是字符串赋值（`PluginConfig.cs:105`），无 IO。

**用例总数只增。** 844（上一轮 839），`Program.cs:1073-1076` 新增 T145-01..04；
grep `RunTest(` 共 845 处（一条为注释/条件式），无既有登记被删除。

**边界。** Bug3 五文件哈希未变（见 §1 脚注）；`Core/` 中除 `PluginConfig.cs`、
`GunshotHostAudioControl.cs`、`PatchRegistrationDiagnosticsModules.cs` 外无其它改动；
`Platform/Diagnostics/Patches/Bug1GateProbeLogic.cs`（out of scope）未被触碰（未出现在本轮变更中）。

**交付包（low，见 R31-05）。** `Release/0.2.4.11-testkit-1/` 是**上一轮**（`builtAtUtc 2026-11-08T14:50Z`）的产品：
DLL `cc816b2e…`（≠ 送审 `43068b2f`），`cfg样本/` 仍放**旧的两个独立 cfg**，且两者都写着 `Enable = true`。
用这个包做下一轮实机，会复现「配置看起来开了、实际没开」——与本票要修的现象同形。

## §6 Findings

**R31-01（medium，语义未钉住）** — `WhitelistTests/Evidence/Runtime/DiagnosticConfigConsolidationTests.cs:69-74` + `Core/Config/PluginConfig.cs:159-162`
三个布尔在自证行里只与「同一个 entry 的当前值」比较，而三个 entry 默认全是 False，
于是把返回值写死成 `False` 也能通过（M3：`1f4edf0f`，842/844 全绿）。
影响：这行是用户判断「配置有没有被读到」的唯一自证，可以被无声改成常量输出；
一旦将来某个 entry 绑定丢失或值取错，用户仍会看到 `bug2HostGunshot=False`——与本票要消灭的静默失败同类。
最小修法（任一）：
① 在 T145-04 已把 `Bug2HostGunshot.Enable` 置 true 的窗口内，追加 `Assert(FormatDiagnosticsConfigLine().Contains("bug2HostGunshot=True"))`；
② 给 Bug1Probe/Bug1Gate 各加一条「entry=true 时行内为 True」的用例（设 true → 断言 → finally 还原）；
③ 把 `FormatDiagnosticsConfigLine` 重载为 `FormatDiagnosticsConfigLine(bool a, bool b, bool c)` 纯函数，对 8 组真值表断言，生产侧只做一次取值的转发。

**R31-02（medium，守护作用域不足）** — `...DiagnosticConfigConsolidationTests.cs:93-105`
T145-03 只对 `Control.Initialize` 这一个方法体计 `ConfigFile::.ctor`、只按字段名查 `ConfigFileName`。
在同类里另加一个方法 `__LoadLegacyCfg()`（内含 `new ConfigFile("…Bug1Gate.cfg", false)`）并从 `OnEnabledChanged` 调用，
842/844 **全绿**（M4：`3ee44f4e`）。即「全仓无 new ConfigFile」是当前源码事实，但不是被测试守护的不变量；
下一次有人为了「兼容旧配置」加一条读取路径，不会有任何红灯。
最小修法：对三个类型的**全部方法**（`GetMethods(Public|NonPublic|Static|Instance|DeclaredOnly)`，含编译器生成的 lambda 方法）逐一计 `ConfigFile::.ctor`，总数必须为 0；
并补一条源码级断言（或 CI 脚本）扫 `Core/`+`Platform/` 的 `new ConfigFile` 文本。

**R31-03（low，故障时恰好看不到自证行）** — `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs:44-52`
自证行在注册计划 **stage 6** 内发出；`PatchRegistrationOrchestrator.Execute` 对任一 stage 返回 false **立即 return**，
因此 stage 1-5 任一失败时这行完全不出现——而「配置放错/插件装坏」正是用户最需要这行的时候。
最小修法：在 `Awake` 里 `PluginConfig.Bind(Config)` 之后（`SteamP2PFriendsPlugin.cs:79` 后）无条件发一次同一行；
或在 orchestrator 的失败分支补发（`failure` 已带 stage traceId）。

**R31-04（low，空路径静默）** — `Core/Config/PluginConfig.cs:105,147-157`
`config.ConfigFilePath ?? string.Empty`：若 BepInEx 传 null，自证行输出 `configPath=`（空），
`FormatDiagnosticsConfigLine` 的 catch 只兜 `Path.GetFullPath`，不预警。
最小修法：空路径时输出 `configPath=<unbound>`，并在 `Bind` 末尾 `RoleLogger.Warn` 一次。

**R31-05（low，交付包与语义冲突）** — `Release/0.2.4.11-testkit-1/`（含 `cfg样本/SteamP2PFriends.Bug1Gate.cfg`、`…Bug2HostGunshot.cfg`，两者 `Enable = true`）
包内 DLL 是 `cc816b2e…`（t145 之前），DLL 与 cfg 样本都指向旧语义。用它上机，等于把本票刚修掉的现象再演一遍。
最小修法：用 `43068b2f` 重打 testkit-2；`cfg样本/` 删除两个旧文件，改放主 cfg 模板
（`com.yu80rice.steamp2pfriends.cfg`，含 `[Bug1Probe]`/`[Bug1Gate]`/`[Bug2HostGunshot]` 三节与默认 false），
并在「测试说明.md」首段写明「旧的三个独立 cfg 自本版起不再读取」。

## §7 结论边界

- 本判定只覆盖「配置合并」这一改动面；**不声称 Bug1 已修复**、**不声称足枪声可在实机被听到**、不声称 G 门成立。
- 「旧 cfg 不再读取」是**行为变更**而非缺陷：依赖旧 cfg 的既有测试机在升级后必须手动迁移开关；
  这一条需要写进交付说明（可与 R31-05 一起做）。
- 实机未参与本轮：`[Diagnostics]` 行在真实 BepInEx 日志里的落盘位置/编码、以及 ConfigurationManager 热改的实际线程，仍属 UNKNOWN（沿用前轮结论：本仓 BepInEx 无文件监视重载，热改只来自 IMGUI 主线程）。

## §8 复现命令

```bash
# 基线
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe   # 842/844，仅 GBA01/NIL01

# 副本（必须与主仓同级，HintPath ..\Libs 才解析）
tar -C <repo> --exclude=./.git --exclude=./.scratch --exclude=./bin --exclude=./obj \
    --exclude=./WhitelistTests/bin --exclude=./WhitelistTests/obj --exclude=./Release \
    --exclude=./publishi --exclude=./Assets --exclude=./.agent-teams --exclude=./Develop-Stage -cf - . \
  | tar -C ../_t146mut -xf - && cp -a <repo>/Libs ../_t146mut/

# 每条变异：apply → build prod → cp dll 到 WhitelistTests/bin/Release → build tests → 跑 exe → restore
python D:/tmp/mut146.py ../_t146mut M1   # 841/844 红 T145-03，dll 2964fee2
python D:/tmp/mut146.py ../_t146mut M2   # 841/844 红 T145-02，dll 67ca5cb1（= Captain）
python D:/tmp/mut146.py ../_t146mut M3   # 842/844 全绿 ← R31-01
python D:/tmp/mut146.py ../_t146mut M4   # 842/844 全绿 ← R31-02
python D:/tmp/mut146.py ../_t146mut M5   # 841/844 红 T145-04
python D:/tmp/mut146.py ../_t146mut restore
```

<!-- self_sha256(body)=7a54ead8255cf6100e07037be5cd779e1a3d40f3d9574e305aae1f58c5b10fc5 -->
