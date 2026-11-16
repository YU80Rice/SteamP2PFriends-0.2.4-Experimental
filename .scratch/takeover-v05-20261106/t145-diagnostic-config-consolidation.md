# t145/t146 合并诊断配置：三个开关并入插件主 cfg + 启动自证行

## §1 摘要

把 `Bug1Probe` / `Bug1Gate` / `Bug2HostGunshot` 三个诊断模块的开关与参数从各自独立的 cfg
（`SteamP2PFriends.Bug1Probe.cfg`、`SteamP2PFriends.Bug1Gate.cfg`、`SteamP2PFriends.Bug2HostGunshot.cfg`）
统一并入插件主配置 `com.yu80rice.steamp2pfriends.cfg`（`BaseUnityPlugin.Config`，由 BepInEx 自动创建）。

启动时每个端各打一行可自证日志：

```
[Diagnostics] configPath=<绝对路径> bug1Probe=<bool> bug1Gate=<bool> bug2HostGunshot=<bool>
```

三个模块不再 `new ConfigFile(...)`、不再拼接独立路径；关闭时仍零 patch，开启行为不变，热切换仍当场生效。

## §2 配置键与默认值（唯一定义：`Core/Config/PluginConfig.cs`）

| 节 | 键 | 类型 | 默认值 | 位置 |
|---|---|---|---|---|
| Bug1Probe | Enable | bool | false | PluginConfig.cs:75（属性 :32） |
| Bug1Probe | TargetPlayer | string | "" | PluginConfig.cs:77（属性 :33） |
| Bug1Probe | SpeedTier | string | "auto" | PluginConfig.cs:79（属性 :34） |
| Bug1Gate | Enable | bool | false | PluginConfig.cs:124 |
| Bug1Gate | TargetPlayer | string | "" | PluginConfig.cs:126 |
| Bug1Gate | OffsetU | float | 0.10 | PluginConfig.cs:128 |
| Bug1Gate | MaxDeviationU | float | 0 | PluginConfig.cs:132 |
| Bug1Gate | Samples | int | 64 | PluginConfig.cs:130 |
| Bug1Gate | SampleIntervalSeconds | float | 1.0 | PluginConfig.cs:135 |
| Bug1Gate | MinSamplesPerCell | int | 3 | PluginConfig.cs:137 |
| Bug1Gate | MaxSamples | int | 240 | PluginConfig.cs:139 |
| Bug2HostGunshot | Enable | bool | false | PluginConfig.cs:102 |

- `BindBug1Gate(...)`（PluginConfig.cs:112-141）是 Bug1Gate 八项的唯一定义；`Bind` 与
  `Bug1GateProbe.BindConfig(...)` 都委托到它，避免生产默认值与离线用例漂移。
- 主 cfg 绝对路径在 `Bind` 时记录到 `BoundConfigFilePath`（PluginConfig.cs:46）。

## §3 模块改动（file:line）

- `Core/Patches/GunshotHostAudio/GunshotHostAudioControl.cs:44-48`
  从 `PluginConfig.Bug2HostGunshotEnable` 取 entry；entry 为 null（主配置尚未 Bind）时
  `ENTRY_INIT_FAILED main config not bound (PluginConfig.Bind must run first)` 并返回 false。
  删除 `ConfigFileName`/`ConfigPathForTest`/`_config`/`using System.IO`，不再创建独立文件。
- `Platform/Diagnostics/Patches/Bug1ProbeControl.cs:36-55`
  Initialize 守卫改为 `_enabledEntry != null`；三个 entry 从 PluginConfig 取，任一缺失则
  `ErrorEntryInitFailed + " main config not bound (PluginConfig.Bind must run first)"` 并返回；
  ENTRY_READY 的 `config=` 改用 `PluginConfig.BoundConfigFilePath`。
  重试路径 `:370` 的初始化判据由 `_config == null` 改为 `_enabledEntry == null`。
- `Platform/Diagnostics/Patches/Bug1GateProbe.cs:66-84`
  八个 entry 从 PluginConfig 取；任一缺失则 `INIT_FAILED main config not bound (...)` 并返回。
  `BindConfig(...)`（:119-132）委托 `PluginConfig.BindBug1Gate(...)`，5 参重载继续委托 8 参，
  既有 `Bug1GateProbeTests` 的临时 ConfigFile 用法不变。
- `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs:40-51`
  在诊断阶段入口输出启动自证行。

## §4 为什么启动行必须走 `Logger.LogInfo`

`Platform/Diagnostics/RoleLogger.cs` 的 `Info()` 会把含 `"Diagnostic"` 的文本转交 `Diagnostic()`，
默认（`VerboseDiagnostics=false`）不输出；`NormalizeForOutput` 还会删除 `[Diagnostics]` 标记并把
Diagnostic 相关 token 改写成 `internal monitor`。若该行走 `RoleLogger.Info`，用户在主日志里根本看不到，
本票要解决的「一眼判断配置是否被读到」就失效。因此
`PatchRegistrationDiagnosticsModules.cs:44-50` 直接调用 BaseUnityPlugin 的 `Logger.LogInfo(...)`，
并用 try/catch 保证日志失败不阻断注册阶段。

## §5 行为不变证据

- 关闭时零 patch：`GunshotHostAudioControl.Initialize` 只在 entry.Value=true 时 `Apply`；
  `Bug1ProbePatch/Bug1GateProbe` 的既有门控未改。T145-04 断言 `Initialize` 后 `Patch.Installed == false`。
- 开启行为不变：Bug1-*/Bug2-* 既有用例全 PASS（见 §8）。
- 热开启：`SettingChanged` 绑定在 PluginConfig 的同一个 ConfigEntry 上；
  T145-04 通过真实 `entry.Value = true/false` 断言安装/卸载，Bug2-20 继续覆盖真实 Harmony 的
  安装→卸载→重装幂等。

## §6 兼容性变更（必须向用户写明）

- 旧文件 `SteamP2PFriends.Bug1Probe.cfg` / `SteamP2PFriends.Bug1Gate.cfg` /
  `SteamP2PFriends.Bug2HostGunshot.cfg` **不再被读取**。
- 旧文件中设为 `true` 的值**不会迁移**；需要在主 cfg 的 `[Bug1Probe]` / `[Bug1Gate]` /
  `[Bug2HostGunshot]` 节重新开启。
- 这样也消除了「cfg 放错路径/未创建 → 静默关闭」的故障：主 cfg 由 BepInEx 自动创建。

## §7 变异表（仓库外副本 `SPF-t145mut`，已逐字节还原并删除副本）

| 变异 | 文件:行 | 改前（原文） | 改后（原文） | 变异 DLL sha256 | 结果 |
|---|---|---|---|---|---|
| M1（恢复独立 cfg） | GunshotHostAudioControl.cs:44 | `                _enabledEntry = Core.Config.PluginConfig.Bug2HostGunshotEnable;` | `                var legacyConfig = new ConfigFile(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "SteamP2PFriends.Bug2HostGunshot.cfg"), false);`<br>`                _enabledEntry = legacyConfig.Bind(Section, "Enable", DefaultEnable, "legacy");` | `3f1fe0f116ccca4cad91f791a1deb13144699c8f6c4e784137cdbf58752662e7` | 840/844；T145-03 红（`must not construct ConfigFile (found 1)`）+ T145-04 红 |
| M2（去掉 configPath） | PluginConfig.cs:159 | `            return "[Diagnostics] configPath=" + path` | `            return "[Diagnostics] path=" + path` | `130d728c158b3e345c39afe6bd437a6d9d3ac20435662d205f4514b19f4e5255` | 841/844；T145-02 红（`must start with [Diagnostics] configPath=`） |

主树未被变异触碰；副本构建后主树文件 sha 与 §9 一致。

## §8 三命令（`-t:Rebuild`）

1. `dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild` → **0 警告 0 错误**
2. `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild` → **0 错误**
   （4 条既有警告：NativePendingBatchLeafProbe.cs:715 CS0219、:41 CS0649、R01E02SliceATests.cs:39/2021 CS0649）
3. `./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` → **842/844 PASS**，仅
   `GBA01 Native First Observe Binding` 与 `NIL01 Real Native Lookup Contract` 失败（既有环境失败，与本票无关）。
   T145-01..04 全 PASS；Bug2-01..39 全 PASS。

生产 DLL：`43068b2f795a788b9cc8579ba80b22a4c317e2b2139a5b46aa94ffa37040cbf0`（bin/Release）。

## §9 逐文件 sha256（终态）

| 文件 | sha256 |
|---|---|
| Core/Config/PluginConfig.cs | `aa8a22e98fd0efd5f38b02b975a89d2fe866003a6595983842cdc820a2d51765` |
| Core/Patches/GunshotHostAudio/GunshotHostAudioControl.cs | `fd765c83f99dd77988711d19e3d17799c3f36900ca01f012c415bcf7ae5541c9` |
| Platform/Diagnostics/Patches/Bug1ProbeControl.cs | `ff38ff53f7befd77ab17612fdc48dfe17acccfd2b76d55b108a5755269ec597c` |
| Platform/Diagnostics/Patches/Bug1GateProbe.cs | `9f7b2f97599ceb1f516b28d4227975b1f630d9f0d61d7b786be80a78ffb39e7a` |
| Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs | `d1d24f23490d6de8cec6c130ae65f148bdf519d9d9e41f48094e36db00d0e19e` |
| WhitelistTests/Evidence/Runtime/DiagnosticConfigConsolidationTests.cs（新增） | `a469c4f2d8dd0782a8fb49c72f539c6f95b4539061ed43e442c31934dd95cb6e` |
| WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs | `6b49f5436514f9a2e8b503df139add624012f8a2242fab28377419ab2b4f71cd` |
| WhitelistTests/Program.cs | `f9324bdb6b8d67b1d6d80d653ced56e21971b7a76325aa37e63a8a6cf70a63e3` |
| bin/Release/SteamP2PFriends.dll | `43068b2f795a788b9cc8579ba80b22a4c317e2b2139a5b46aa94ffa37040cbf0` |

## §10 UNKNOWN（未闭环）

- 真实 BepInEx 在游戏内创建/读取 `com.yu80rice.steamp2pfriends.cfg` 的路径与落盘行为未在测试宿主执行；
  自动化只证明「键存在、默认值正确、模块只读主配置、启动行内容正确」。
- 启动行在实机日志中的实际可见性需下一轮 1H+1G 诊断包核对（本票不能替代实机证据）。
- Bug1Probe/Bug1Gate 的真实开启副作用（Unity 依赖）仍未在自动化中执行，沿用既有 UNKNOWN。
