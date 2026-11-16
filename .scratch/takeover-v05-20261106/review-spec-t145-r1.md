# T147 规格轴复审：t145 诊断配置合并的兼容性登记

- 审查官：reviewer-spec（规格轴，round 1）
- 任务：t147（attempt 2 `7a58b6fa-fa94-4ce0-ba06-2bbb5eaef348`），被审 t145（诊断配置合并）
- 判定：**needs_revision**——配置键与默认值全部与既有裁决一致、口径无越界，但**兼容性变更尚未在任何可判文档中登记**，且与 `architect-ruling-t88-release-diagnostics.md §3` 的既有条款**直接冲突**，同时仓库内仍在流通的测试说明仍在教用户编辑旧 cfg。
- 边界：本轮只读。未修改 `requirements-v2.md`（按票面要求把补条写在本报告 §5）、未改源码/测试/发布目录。

## 1. 取证对象

| 对象 | 事实 |
|---|---|
| `.scratch/takeover-v05-20261106/t145-diagnostic-config-consolidation.md` | 8516B，§2 键表 / §6 兼容性变更 / §8 三命令 / §9 逐文件 sha / §10 UNKNOWN |
| `Core/Config/PluginConfig.cs:70-141` | `Bind` 与 `BindBug1Gate` 是三个模块键的唯一绑定源；`BoundConfigFilePath` 于 `:157` 记录 |
| `Platform/Diagnostics/Patches/Bug1ProbeControl.cs:36-55`、`Bug1GateProbe.cs:66-84`、`Core/Patches/GunshotHostAudio/GunshotHostAudioControl.cs:44-48` | 三模块改从 `PluginConfig` 取 entry；entry 为空 → `INIT_FAILED/ENTRY_INIT_FAILED ... main config not bound` 并返回 |
| `Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDiagnosticsModules.cs:40-52` | 启动行 `Logger.LogInfo(PluginConfig.FormatDiagnosticsConfigLine())`，try/catch 不阻断注册 |
| 残留旧 cfg 引用（全仓 `.cs` grep） | 生产代码 0 处构造 `ConfigFile`；仅 `WhitelistTests/Evidence/Runtime/Bug1GateProbeTests.cs:30` 用临时目录 cfg 做离线绑定，属既有测试夹具 |

## 2. 逐条验收判定

| # | 验收条 | 判定 | 依据 |
|---|---|---|---|
| 1 | requirements / 测试说明是否已有「旧 cfg 不再读取、旧值不迁移」登记；若只在代码里，是否必须补 | **不通过** | `requirements-v2.md` 全文无此条（`grep 配置/cfg/迁移` 仅命中 `:44` 速度上限、`:75-77` §4.1 测试缝）；`grep -rn "不迁移\|旧 cfg"` 仅命中 t145 报告自身；测试说明不仅没登记，仍在教用户编辑旧文件（见 F2） |
| 2 | 三节键名/默认值与既有 Spec/裁决一致，无 t76/t88/t115 冲突 | **通过（键值面）** | 见 §3 核对表：与 t85（`[Bug1Gate]` Enable/TargetPlayer/OffsetU/Samples）、t90（`MaxDeviationU=0`→`EPSILON_UNSPECIFIED`）、t95/t104（`SampleIntervalSeconds=1.0`、`MinSamplesPerCell=3`、`MaxSamples=240`）、t48/t51（`[Bug1Probe]` Enable/TargetPlayer/SpeedTier=auto）逐项一致；t76 只要求 0.10U 与「epsilon 由决策者给定」，未冲突；t115 不含 cfg 条款 |
| 3 | 影响面如实：旧 Enable=true 失效，但不会被读成相反值（不得静默开启） | **部分不通过** | t145 §6 文字如实（不再读取、不迁移、须在主 cfg 重开），代码路径也不存在「读旧文件」分支；但「不得静默开启」只有**默认值断言 + IL 不构造 ConfigFile**，缺一条「旧 cfg 存在且 Enable=true → 模块仍 disabled」的行为级反证（F3） |
| 4 | 未把配置合并写成 Bug1 已修 / 枪声实机可用 | **通过** | 正则扫描 `已修复|闸门成立|实机可用|能听见|可出包|可发布|验收通过` 在 t145 报告 **0 命中**；§1 只写「开启行为不变」，§10 明列两条 UNKNOWN（真实 BepInEx 建 cfg、实机日志可见性） |
| 5 | 越界口径扫描：不得把 842/844 当作闸门成立或 Bug2 通过 | **通过** | §8 第 3 条原文「842/844 PASS，仅 GBA01/NIL01 失败（既有环境失败，与本票无关）」，未与任何闸门/听感结论挂钩；Bug2-01..39 全 PASS 属回归陈述，不是听感通过 |
| 6 | 需补文档时给最小条目清单 | 见 §5 | 已给可判条款 + 覆盖点 + 反证 + 不授予 PASS 的口径 |

## 3. 键名与默认值核对（实现 ↔ 既有裁决）

| 节 / 键 | 实现值（`PluginConfig.cs`） | 既有裁决锚点 | 一致 |
|---|---|---|---|
| `[Bug1Probe] Enable` | `false`（:75） | t48/t51：`[Bug1Probe] Enable` 默认 false | ✅ |
| `[Bug1Probe] TargetPlayer` | `""`（:77） | 同上；空且唯一客机时自动选中（t46 已接受） | ✅ |
| `[Bug1Probe] SpeedTier` | `"auto"`（:79） | 仪器规格 §3.4 走/跑两档；`auto` 只决定本次窗口取哪一档（t51 轮确立） | ✅ |
| `[Bug1Gate] Enable` / `TargetPlayer` | `false` / `""`（:124/:126） | t85 §3「独立 cfg 节 `[Bug1Gate]`、默认关闭」 | ✅ |
| `[Bug1Gate] OffsetU` | `0.10f`（:128） | t76 §2.2「分别对 X 与 Z 施加 0.10U 并还原」；t85 `DefaultOffsetU=0.10` | ✅ |
| `[Bug1Gate] MaxDeviationU` | `0f`（:132） | t90「epsilon 未指定：默认 0 → `UNKNOWN/EPSILON_UNSPECIFIED`，不自选阈值」 | ✅ |
| `[Bug1Gate] Samples` | `64`（:130） | t85/Spec `DefaultSampleCount=64` | ✅ |
| `[Bug1Gate] SampleIntervalSeconds` | `1.0f`（:135） | t95/t104「默认 1.0s；连续 2 次 = 2×间隔，不是 2 帧」 | ✅ |
| `[Bug1Gate] MinSamplesPerCell` / `MaxSamples` | `3` / `240`（:137/:139） | t95 §F6「每格最少 3 个有效样本」+ `MaxSamples=240` 上限；t98 R17-04 只改计数规则不改上限 | ✅ |
| `[Bug2HostGunshot] Enable` | `false`（:102） | t115 §3 补偿默认关闭、按门控开启 | ✅ |

结论：本次合并**没有改默认值、没有改节名、没有改键名**，只是把绑定源从三个独立文件搬到主 cfg；键值面与 t76/t88/t95/t104/t115 **无冲突**。

## 4. Findings

### T147-F1（high）兼容性变更与 t88 §3 既有条款直接冲突，且 requirements 无任何登记

- 事实：`architect-ruling-t88-release-diagnostics.md:44` 现行条文为「**默认值不会覆盖用户旧配置。** 两个入口发现已有 `Enable=true` 会启动/尝试启动…升级部署前须检查 `SteamP2PFriends.Bug1Probe.cfg` 及 `SteamP2PFriends.Bug1Gate.cfg`；发现 true，部署者先停止发布动作…」；`:103` 又要求「必须同时解释旧配置不会自动重置」。t145 让这两个文件**不再被读取**，于是：
  - 「发现 true 会启动」不再成立（旧 true 一律失效）；
  - 「部署前检查旧 cfg」变成检查无效文件，流程空转；
  - requirements-v2.md 未新增任何条款覆盖此变更（§4 只有 `:75-77` 测试缝边界，与 cfg 无关）。
- 为何不是「报告里写了就行」：t145 §6 是**实现报告**，不是验收契约；t88 §3 是**架构裁决**，两者冲突时必须有裁决层改判，否则下一轮打包/部署会照旧条文执行。
- requiredFix（两件，缺一不可）：
  1. Architect 书面追认：t88 §3「默认值不会覆盖用户旧配置 / 部署前检查旧 cfg」自 T145 起被替代为「旧 cfg 退役：不读取、不迁移；核对方式改为主 cfg 节 + 启动自证行」；
  2. 在 `requirements-v2.md` §4 增 `### 4.2 诊断配置合并与旧 cfg 退役（T145）`，采用 §5 措辞。

### T147-F2（medium）测试说明仍在教用户编辑旧 cfg——按 t145 字节打包会造成「以为开着其实关着」

- 事实（仓库内现存文件）：
  - `Release/0.2.4.11-testkit-1/测试说明.md:20-22`「`SteamP2PFriends.Bug1Gate.cfg` → `BepInEx/config/`；`SteamP2PFriends.Bug2HostGunshot.cfg` → …；打开 `Bug1Gate.cfg`，把 `TargetPlayer` 填成客机玩家名」；`:28`「把旧的 `SteamP2PFriends.Bug1Probe.cfg` 里的 `Enable` 改成 `false`」；
  - `Release/0.2.4.11-preview5/测试说明.md:29`「编辑 `BepInEx\config\SteamP2PFriends.Bug1Probe.cfg`」；
  - `Release/0.2.4.11-gate-probe-1/测试说明.md:33`「首次启动会生成 `…Bug1Gate.cfg`，改成：」。
- 这些说明对**旧包**属实（包内 DLL 早于 t145），但仓库没有任何「换 DLL 必须换说明」的判据。一旦用 t145 字节重打包并沿用旧说明，操作者会：编辑不被读取的文件 → 模块保持 false → 启动行 `bug1Gate=false` → **用户以为开着、实际关着**，且诊断包会显示「未开启」而无法归因。
- requiredFix：在 requirements §4.2 写入「测试说明必须与 DLL 同源」条款，并把下一次打包的说明模板改为——① 编辑主 cfg `com.yu80rice.steamp2pfriends.cfg` 的 `[Bug1Probe]/[Bug1Gate]/[Bug2HostGunshot]` 节；② 强制一步「核对启动日志 `[Diagnostics] configPath=… bug1Probe=… bug1Gate=… bug2HostGunshot=…`，三布尔与预期不符即视为未启用，先停下排查」；③ 明写旧三文件已退役、旧值不迁移。旧包说明不追改，但 manifest 必须标明包内 DLL 与说明代次，禁止「旧说明 + 新 DLL」组合。

### T147-F3（low）「不会被读成相反值」缺行为级反证

- 事实：T145-01 断言主 cfg 默认值全为默认；T145-03 以 IL 断言三个 `Initialize` 不构造 `ConfigFile`、不留 `ConfigFileName` 字段。二者合起来证明「不读旧文件」的结构性成立，但没有任何用例**放一个 `Enable=true` 的旧文件**再看模块是否仍 disabled。t145 报告 §6 的说法正确，只是证据层级偏低。
- requiredFix（二选一，写清即可）：① 新增 T145-05：临时目录放 `SteamP2PFriends.Bug1Gate.cfg`（`[Bug1Gate] Enable=true`），初始化后断言 `Bug1GateProbe` 未启用、`FormatDiagnosticsConfigLine()` 中 `bug1Gate=false`；② 若判定「IL 契约即充分」，须在 t145 报告 §6 与 requirements §4.2 中写明该结论的证据层级为「结构（IL）而非行为」，避免下游把它当行为已验证。

## 5. 建议补入 requirements 的最小条目（交 Captain 派需求角色，勿整篇重写）

位置：`requirements-v2.md` 第 4 节，`### 4.1` 之后新增 `### 4.2 诊断配置合并与旧 cfg 退役（T145）`，正文四句：

1. **唯一配置源**：`Bug1Probe` / `Bug1Gate` / `Bug2HostGunshot` 三节的键只从插件主 cfg `com.yu80rice.steamp2pfriends.cfg` 读取；三个模块的 `Initialize` 不得构造 `ConfigFile`、不得保留独立配置路径字段。覆盖点 **T145-01/03**；反证：把任一模块改回独立 cfg 或删除键绑定，对应用例必须红。
2. **旧文件退役**：`SteamP2PFriends.Bug1Probe.cfg` / `Bug1Gate.cfg` / `Bug2HostGunshot.cfg` 自本版本起**不再读取、不做值迁移**；升级用户须在主 cfg 对应节重新开启。覆盖点 **T145-01**（默认值）+ **T145-05**（旧文件 `Enable=true` 仍 disabled，建议补）；反证：出现任何「读旧文件即启用」路径即为违反。
3. **启动自证与说明同源**：每次启动须输出一行 `[Diagnostics] configPath=<绝对路径> bug1Probe=<bool> bug1Gate=<bool> bug2HostGunshot=<bool>`（经 `Logger.LogInfo`，不得被 RoleLogger 的 Diagnostic 降级吞掉）；测试说明必须含「核对本行」步骤且与包内 DLL 同代。覆盖点 **T145-02** + 打包清单核对。
4. **不授予 PASS**：本条仅为配置面兼容性验收，**不授予** S1–S4、G 门成立、Bug1 已修复、Bug2 听感或「0.2.4.11 可发布」任何结论；旧 cfg 不迁移带来的「静默关闭」只能由本条第 3 句的启动行核对暴露，不得据默认 false 推断用户未开启诊断。

同条附注一句：**本条取代 `architect-ruling-t88-release-diagnostics.md §3` 中「默认值不会覆盖用户旧配置 / 部署前检查旧 cfg」的操作口径；其余（默认 OFF、不捆绑配置、必要入口日志）不变。**

## 6. 越界口径扫描

- `t145-diagnostic-config-consolidation.md`：`已修复|闸门成立|实机可用|能听见|可出包|可发布|验收通过|全部通过` → **0 命中**。
- 842/844 仅出现在 §8 基线，且同句注明两条失败为既有环境失败；未出现「闸门成立」「Bug2 通过」「可出包」。
- §10 两条 UNKNOWN 如实（真实 BepInEx 建 cfg 路径、实机日志可见性），未把自动化绿写成人机可用。
- 其余仓库文档未出现把本次合并当作修复结论的表述。

## 7. 判定边界

- 本判定只针对**规格与文档面**：配置键一致性、兼容性登记、影响面表述、越界口径。不评工程实现（由 t146/工程轴负责）、不评测试强度本身（仅评「有没有可失败的覆盖点」）、不评打包与版本号。
- 「needs_revision」不等于配置合并实现有误——键值面与行为面均未发现冲突；阻塞点只有 F1 的裁决冲突与文档缺位、F2 的说明错配风险。
- F2 涉及 `Release/` 目录（本票 out of scope 写入），故只给 requiredFix 与模板要求，不由本票修改任何发布物。

---
self_sha256(body)=9d10f0d72e7c9e033f0d0ddf50ff617bc6f8e7067a8e6d23796d058962ebfc9e
