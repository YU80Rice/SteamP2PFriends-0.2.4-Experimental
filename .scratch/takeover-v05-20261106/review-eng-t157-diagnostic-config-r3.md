# t158 工程轴复审：t157 诊断配置合并 r3（round 3）

审查对象：t157（repair-round-3）对 R32-01..05 的收口，源码快照 = 工作树 = index。
审查者：reviewer-eng，attempt `f8c99df5-ec88-4dc0-9b89-6254728bb684`。
判定：**pass**（0 blocker / 0 high / 1 medium / 2 low）。

## 0. 一句话结论

R32-01（字节锁不可复现）与 R32-02（自证行实参无守护）在**这一批字节上真闭合**：五个源码哈希与报告 §11.2 逐项一致，主树与副本都能把 DLL 重建为 `01c8bb75…`，`T145-02` 现在同时钉「Bind 调 `ReadDiagnosticsSwitches()`」与「格式化调用参数数 == 2」，把发射点换成常量实参（MA）或把读取点写死（MB）都会红。打回只剩一条 medium：**复审期间主树仍被第三方临时改写**（R33-01），它污染了我第一次取的副本，属于流程风险而不是这批代码的缺陷。

## 1. 字节锁（与 t153 报告 §11.2 逐项对照）

| 文件 | 实测 sha256（前 16） | 报告 §11.2 | 结果 |
| --- | --- | --- | --- |
| `Core/Config/PluginConfig.cs` | `2c4374d608320f6a`（14016B） | `2c4374d6…` | ✓ |
| `…PatchRegistrationDiagnosticsModules.cs` | `f99095d7d8e441ec`（9467B） | `f99095d7…` | ✓ |
| `WhitelistTests/Evidence/Runtime/GunshotHostAudioTests.cs` | `7197386ce5dedf2c`（83120B） | `7197386c…` | ✓ |
| `WhitelistTests/Program.cs` | `5fb596d8f692b52a`（162483B） | `5fb596d8…` | ✓ |
| `WhitelistTests/Evidence/Runtime/DiagnosticConfigConsolidationTests.cs` | `73b4a76cc79d9cbb`（23749B） | `73b4a76c…` | ✓ |
| `bin/Release/SteamP2PFriends.dll` | `01c8bb75cc4200f7` | `01c8bb75…` | ✓ |

- index 与工作树一致（`git show :path` 与磁盘同哈希，三文件逐一核对），`git status` 中 `M  Core/Config/PluginConfig.cs` 为已 staged 的 t157 修改，无 `MM`。
- 主树 `-t:Rebuild` 重建后 DLL 仍为 `01c8bb75…`；在仓库外副本（`_t158mut`，另一路径）重建亦为 `01c8bb75…` → 该哈希可复现，且与构建路径无关。
- 结论：R32-01 **关闭**（报告 §11 的时间线解释与哈希自洽，§8 的 `d368fa2c` 已被明确作废）。

## 2. 三命令（主树 + 副本）

| 命令 | 主树 | 副本 `_t158mut` |
| --- | --- | --- |
| 生产 `-t:Rebuild` | exit 0，0 警 0 错 | exit 0，0 警 0 错 |
| 测试 `-t:Rebuild` | exit 0，0 错 4 既有警告（CS0649×3+CS0219） | 同 |
| exe | exit 1，**846/848**，FAIL 仅 GBA01/NIL01 | 同 |

用例总数 848（上轮 846），T145-01..07 与 Bug2 段全 PASS。

## 3. 变异（仓库外副本，逐字节还原）

副本 `_t158mut`；每条：改源 → `-t:Rebuild` → `cp` DLL 到测试 bin → 跑 exe → 备份字节还原 → 重建校验回到 `01c8bb75…`。

| 变异 | 改动点 | 变异 DLL | 结果 | 新红 |
| --- | --- | --- | --- | --- |
| MA | `PluginConfig.cs:123` 发射点 → `FormatDiagnosticsConfigLine(BoundConfigFilePath, false, false, false)` | `62b10d95…` | 845/848 | T145-02 |
| MA2 | 发射点 → `(BoundConfigFilePath, (false,false,false))`（参数数仍为 2） | `1a3d5265…` | 845/848 | T145-02 |
| MB | `ReadDiagnosticsSwitches()` 三项 → `false,false,false` | `f9b2db52…` | 843/848 | T145-04/06/07 |
| M1 | gunshot 模块取 entry 前插 `new ConfigFile("…Bug2HostGunshot.cfg", false)` | `81000214…` | 845/848 | **只红 T145-03** |
| M2 | `"[Diagnostics] configPath="` → `cfgpath=` | `28d25814…` | 844/848 | T145-02 + T145-05 |
| M3 | 删 `Bind` 内 `DiagnosticsLog.LogInfo(...)` 整行 | `43468946…` | 845/848 | T145-02 |

- **MA 与 t157 自报逐位一致**（`62b10d95…`，845/848，只红 T145-02）→ 上一轮「自述不可复现」的问题在本轮消失。
- **MA2 是新增的关键负控**：参数个数仍为 2，只把 `ReadDiagnosticsSwitches()` 换成常量元组，仍红 T145-02 → 说明守护不是只数参数个数，而是按方法名/声明类型精确匹配调用边（`DiagnosticConfigConsolidationTests.cs:89-107`）。
- **MB 比 t157 自报多红 T145-04**：T145-04 的热开关窗口断言启动行含 `bug2HostGunshot=True/False`，读取点写死后自然同时红；属更严结果，不算缺陷。
- M1 满足 acceptance「改回独立 ConfigFile → 只红 T145-03」。

## 4. 逐条 acceptance

| # | 判据 | 结果 | 证据 |
| --- | --- | --- | --- |
| 1 | 改回独立 ConfigFile → 红且只红 T145-03 | passed | M1 `81000214`，845/848，只红 T145-03 |
| 2 | 删 configPath 段 → 红且只红 T145-02 | passed（更严） | M2 `28d25814`，844/848，红 T145-02+T145-05；M3 删整行 `43468946` 只红 T145-02 |
| 3 | 全仓无 `new ConfigFile`/`ConfigFileName`/旧文件名读取 | passed | `grep -rn "new ConfigFile\|ConfigFileName" Core Platform` 0 命中；旧 cfg 名仅 `GunshotHostAudioControl.cs:43`、`PluginConfig.cs:32` 两处注释 |
| 4 | 关闭态零动作 | passed（口径已改） | `GunshotHostAudioControl.Apply`：disabled → `GunshotHostAudioPatch.Uninstall()` + `SetEnabled(false)`，不 Register；probe/gate 仅 `_enabledEntry.Value` 为真才 `TryStart`/`Arm`；保留 `SettingChanged` 订阅（t153 §11.5 已把口径改为「不装 patch、不订阅运行时事件、不写周期」） |
| 5 | 自证行来自真实注册路径、绕开 RoleLogger 有据 | passed | `PluginConfig.Bind` 末尾 `DiagnosticsLog.LogInfo(FormatDiagnosticsConfigLine(BoundConfigFilePath, ReadDiagnosticsSwitches()))`（`PluginConfig.cs:123`），早于所有 stage；`RoleLogger.Info` 对 DiagnosticMarkers 走 `Diagnostic()`（受 verbose 门控）且 `NormalizeForOutput` 删前缀 |
| 6 | T145-01..04 不是形状断言 | passed | MA/MA2（发射点）、MB（读取点）、M1（模块自建 cfg）、M3（删发射行）全部真红；T145-01 断言具体默认值与绝对路径 |
| 7 | 基线不掉、用例只增 | passed | 846/848（上轮 846/848）且总数 848 > 846；仅 GBA01/NIL01 环境失败 |
| 8 | 报告字节锁与实际一致 | passed | §11.2 五个源码哈希与 DLL `01c8bb75` 实测逐项相同 |
| 9 | 主树收工哈希与开工一致 | passed（源码） | 我未改任何源文件；主树重建后 DLL 仍 `01c8bb75`。但见 R33-01：开工与审查中段出现过第三方临时改写 |

## 5. Findings

### R33-01（medium）复审期间主树被第三方临时改写，污染了一次副本
- 事实链：03:03:17 前后，主树 `Core/Config/PluginConfig.cs` 一度变成 MA 变体（发射点 `FormatDiagnosticsConfigLine(BoundConfigFilePath, false, false, false)`，sha256 `83ff48f3…`，14010B）；我恰在此时 `tar` 取副本，副本因此构建出 `62b10d95…` 并在基线跑出 845/848、T145-02 红；03:06:01 主树恢复为交付字节 `2c4374d6…`（14016B），我复原副本后重建得 `01c8bb75…`、846/848。
- 风险：若复审官不核对源哈希与报告 §11.2，会把「变异态」当成交付态，得出「T145-02 在基线上就红」的错误结论；反过来也可能把变异态下的红灯当作修复证据。
- requiredFix：①复审窗口内单写者（t153 §11.4 已承诺，但本轮仍有第三方写入）；②复审官取副本后，先对报告 §11.2 做逐文件哈希核对再跑基线；③变异只在副本内做，副本从 `git show :path` 建（避免抓到工作树瞬时态）。

### R33-02（low）acceptance 文本未同步到 t153 §11.5 的新口径
- 事实：acceptance 仍写「删掉 configPath 段 → 只红 T145-02」，但 T145-05 的期望串精确包含 `configPath=`，实测 M2 同时红 02+05。t153 §11.5 已把口径改为「至少红 T145-02、允许连带」，本票 acceptance 未改。
- requiredFix：把 acceptance 第 2 条改为「至少红 T145-02；允许 T145-05 一并红」。

### R33-03（low）零参 wrapper 成为仅测试使用的死代码
- 事实：`PluginConfig.FormatDiagnosticsConfigLine()`（`:165-180`）在生产无调用点（发射点已改用双参重载），仅测试/历史用例使用；T145-02 自 t157 起要求「参数个数 == 2」，已不再依赖零参重载。
- 风险：后续维护者可能误以为生产走零参包装，或在包装里加逻辑而无人使用。
- requiredFix：加 `// 仅测试使用` 注释，或在下一轮删除并把相关用例改为直接调双参重载。

## 6. 边界与声明

- 本判定只对上述五个源码哈希 + DLL `01c8bb75…` 有效；R33-01 所述瞬时态不在此列。
- 未声称：Bug1 已修复、枪声在实机可听到、G 门已验证。
- UNKNOWN（沿用）：真实 BepInEx 在游戏内创建/落盘主 cfg 的行为；启动行在实机日志的可见性（需 1H+1G 诊断包）；Bug1Probe/Bug1Gate 真实开启副作用。
- 我未修改任何源文件；副本 `_t158mut` 已删除；主树 `bin/Release` 由我重建，哈希仍为 `01c8bb75…`（与 t157 交付一致）。

## 7. 复现命令

```bash
# 0) 取副本前先钉字节
sha256sum Core/Config/PluginConfig.cs          # 期望 2c4374d6…
sha256sum bin/Release/SteamP2PFriends.dll      # 期望 01c8bb75…
# 1) 基线
dotnet build SteamP2PFriends.csproj -c Release -t:Rebuild
dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release -t:Rebuild
cp bin/Release/SteamP2PFriends.dll WhitelistTests/bin/Release/
WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe   # → 846/848

# 2) MA2（参数个数不变、只换常量元组，必须红 T145-02）
# PluginConfig.cs:123
#   FormatDiagnosticsConfigLine(BoundConfigFilePath, ReadDiagnosticsSwitches())
# →
#   FormatDiagnosticsConfigLine(BoundConfigFilePath, (false, false, false))
# 重建 → 1a3d5265…，845/848，T145-02 红

# 3) M1（模块自建独立 cfg，必须只红 T145-03）
# GunshotHostAudioControl.cs:44 前插
#   _ = new BepInEx.Configuration.ConfigFile(Path.Combine("BepInEx","config","SteamP2PFriends.Bug2HostGunshot.cfg"), false);
# 重建 → 81000214…，845/848，只红 T145-03
```

<!-- self_sha256(body)=bd3c00aa33e5f2eb399d51460426479c7d5e43c5386b77f616af57ea03231acb -->
