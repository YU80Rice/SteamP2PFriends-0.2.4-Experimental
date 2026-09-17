# 05: 共享 1 Host + 2 Guest Runtime 验收

**What to build:** 不写生产代码。用前四张票审查通过后的同一 Release DLL，做一次三端实机，证明僵尸能再刷、物品能周期消失并再长、无密码/有密码/`SteamID:27016` 路由成立。

**Blocked by:** 01 僵尸重生 Listen-Host Dedicated Gate；02 物品周期生命周期 Listen-Host Dedicated Gate；03 SteamID:port 分类；04 Listen-Host Session Password。四张均须双轴 CLEAN、Release 构建与静态门禁通过。

**Status:** ready-for-human

- [x] 统一 Case ID；三端 `SteamP2PFriends.dll` SHA-256 逐字节一致。（2026-09-17 轮 C05-01 PASS，见 `audit/2026-09-17/RuntimeAcceptance-0.2.4.8-Ticket05-2320.md`）
- [ ] 普通 PEI：记录已知区域僵尸初始数量，打死满足重生条件的僵尸，等待原版窗口后该区域出现新僵尸；诊断确认走进对齐后的早退分支。（本轮肉眼可见刷新；`RespawnGateDiag` 0 行，Verbose 未开，证据不足）
- [ ] 物品：诊断记录 `generateItems` / `despawnItems` / 周期 respawn 的次数与区域；地面物品能被周期移除也能再生成；无持续刷满或无界累加。（本轮肉眼可见刷新；`ItemGateDiag` 0 行，证据不足）
- [ ] 时间窗内未自然刷出不得直接判代码失败，须结合诊断计数与可重复条件区分「窗口未到」与「早退仍在」。（本轮因探针 0 行不可判定）
- [ ] 预生成与周期处理若造成重复刷物品：本验收 FAIL，另开收缩票，不在本票重构预生成。（本轮无周期计数，未触发）
- [ ] Join Routing：无密码房间 SteamID 不填密码可连；有密码房间不填密码由原版产生 `PASSWORD`；填对密码可连；`SteamID:27016` 走 Steam P2P 连上。（仅无密码进入部分 PASS；有密码三态与 `:port` 独立锚未跑）
- [ ] 日志只证明 `hasPassword`、路由类型和失败枚举，无密码明文或长度。（已产生的无密码行成立；有密码行未产生）
- [x] 本轮不专项 Horde/Beacon，也不得改变它们既有语义。（PEI/EASY，Horde/Beacon 0 命中）
- [x] 回传 UMM 诊断包；按 Evidence Class 落盘 Runtime 审计。本票不关实施票、不改 RELEASES 批准态。（本轮审计已落盘；结论=不构成 PASS）

## Runtime 验收执行包（2026-09-12 构建，主工作树会话）

### 候选身份（唯一有效，三端一致绑定）

| 项 | 值 |
|---|---|
| 候选 DLL | 仓库 `bin/Release/SteamP2PFriends.dll`（三端部署同一文件） |
| SHA-256 | `A69B78AFDA3185865FF79AC54708DFCEF625DBF9EE622B6492033D9E76877217` |
| MVID | `2ea4dbc6-be2e-4f72-95d0-dfd438ad0f62` |
| Case ID | `SPF-0.2.4.8-Experimental-StructureBaseline`（程序集烘焙默认） |
| 基线 | HEAD `01dc27b`（含票 01–04 全部变更；静态闭环见 `audit/2026-09-12/` 下 Ticket01–04 四份 Implementation 审计） |

- 2026-09-12 HEAD 复核：全套测试 280/280 PASS（Runtime 证据类恰为 PENDING：SP/listen-host/U3DS/P2P，即本票）；`Tools/Verify-BuildFingerprintArtifact.ps1` PASS，与 Ticket04 审计 §5 指纹逐字节一致。
- Case ID 决定：沿用程序集烘焙默认值。三端部署同一 DLL，`build-metadata` 来源天然逐字节统一；改 `Build/Version.props` 属生产代码变更（本票禁止），`STEAMP2PFRIENDS_CASE_ID` 环境变量覆盖徒增三端配置失败面。三端 `[BuildFingerprint]` 行 `caseIdSource` 应为 `build-metadata`；出现 `environment` 即视为环境污染。

### 三端部署与核对（人工，不可跳步；SOP：`docs/agents/auto-rm-test-sop.md`）

1. 三端各备份旧 `Unturned\BepInEx\plugins\SteamP2PFriends.dll`，再用仓库 `bin/Release/SteamP2PFriends.dll` 覆盖。
2. 三端各执行一次并核对输出与上表 SHA-256 逐字节一致（不一致即停）：
   `Get-FileHash <Unturned>\BepInEx\plugins\SteamP2PFriends.dll -Algorithm SHA256`
3. 三端开测前编辑 `Unturned\bepInEx\config\com.yu80rice.steamp2pfriends.cfg`，将 `Debug` 节 `VerboseDiagnostics` 设为 `true`（重启生效，必须在启动前改）：`[RespawnGateDiag/Zombie]`、`[ItemGateDiag/Item]`（C05-03/04 判据）与客机 `ServerConnectParameters constructed` 行（输出时 `[Diag]` 内部前缀被归一化剥除）受此门控；`[BuildFingerprint]`、`[SessionPassword]`、`[UnifiedConnect]` 行常开不受限。
4. 三端经 UMM 启动器启动；房主创建普通 PEI 世界并开 P2P 房间，两名客机待命。

### 会话编排（三局；局内不重启游戏）

- **第一局（无密码房间）**：C05-01 → C05-02 → C05-03 → C05-04。
- **第二局（有密码房间）**：第一局 ESC 结束回菜单后，房主重新开房并在插件开房菜单密码框填非空密码；两客机在直连页待命 → C05-06（Guest1）→ C05-07（Guest2）。
- **第三局（密码清空回归局）**：第二局 ESC 结束回菜单后，房主再次开房（开房菜单每次打开密码框默认空，勿重新输入）→ C05-05；两客机随后确认可无密码连入。
- 僵尸/物品观察窗内可并行推进连接类场景，不必串行空等。

### Case 矩阵

| Case | 场景 | 通过判据（日志锚行） |
|---|---|---|
| C05-01 | 三端指纹绑定 | 三端 `[BuildFingerprint]` 行 `dllSha256=`/`mvid=`/`caseId=` 与上表逐字节一致，`caseIdSource=build-metadata` |
| C05-02 | 无密码房间进入 | Guest1 裸 SteamID、Guest2 `SteamID:27016`（端口只用于分类）不填密码均连入；房主 `[SessionPassword] hasPassword=False`；客机 `[UnifiedConnect] route=SteamP2P target=<房主ID> hasPassword=… started=True` |
| C05-03 | 僵尸重生 | 普通 PEI 已知区域记录初始僵尸数 → 打死满足重生条件的僵尸 → 等原版重生窗口 → 该区域出现新僵尸；`[RespawnGateDiag/Zombie] respawnZombies #k/20 elig=True bound=… calls=… passed=… returned=… ambiguous=… allPassed=…` 对应 bound 的 `passed` 随时间增长；`passed>0` 但数量未回升 ⇒ 结合 `ambiguous` 计数与窗口计时复核（窗口未到，不判 FAIL） |
| C05-04 | 物品周期 | `[ItemGateDiag/Item] respawnItems … elig=True region=(x,y) calls=…` 存在且 `calls` 持续增长（轮转每帧推进）；`generateItems`/`despawnItems`（`removed`）/周期 respawn（`spawned`）三路计数均带区域；地面物品被周期移除且随 `Respawn_Time` 再生成；`spawned` 收敛，无同区域无界累加 |
| C05-05 | 会话密码生命周期 | 第二局（非空密码）ESC 结束回菜单（清空路径静默无日志）→ 第三局开房：菜单密码框默认空，开局日志 `[SessionPassword] hasPassword=False`（常开锚行），Guest 不填密码可连入 ⇒ 第二局非空密码未沿用、运行时已清 |
| C05-06 | 有密码房间填对可连（SteamID 路线，Guest1） | Guest1 直连页粘贴 `SteamID:27016` 并在密码框填同一密码连入；`[UnifiedConnect] route=SteamP2P target=<房主ID> hasPassword=True started=True`（常开锚行）；Verbose 开启时客机另见 `ServerConnectParameters constructed: hostSteamId=<房主ID> hasPassword=True`（`[Diag]` 内部前缀被归一化剥除，非字面可见）；任何行不得出现密码明文或长度 |
| C05-07 | 有密码房间不填密码（Guest2） | Guest2 直连页粘贴 SteamID、密码框留空点连接 → 原版产生 `PASSWORD` 失败呈现；无插件第二块提示、无专用弹窗；日志仅失败枚举 |

### 判读口径（防误判，出自票 01–04 审计 §8）

- **「窗口未到」≠「早退仍在」**：僵尸 `elig=True` 且对应 bound `passed` 恒 0、物品 `elig=True` 且 `calls==0`，贯穿整个测试窗 ⇒ 早退仍在（FAIL）；`passed>0`/`calls>0` 但数量未回升 ⇒ 仅等待原版重生/`Respawn_Time` 窗口（不判 FAIL，以 `ambiguous` 计数、`cooldown`/`idle`/`nospawn` 计数与可重复条件复核）。
- **物品刷满/无界累加**：同一区域 `spawned` 持续无界增长 ⇒ 本验收 FAIL，另开收缩票；禁止在本票内重构预生成。
- **密码日志纪律（仅限密码相关日志）**：密码相关日志只允许 `hasPassword=True/False`、路由类型、失败枚举；出现密码明文或长度即 FAIL。其余诊断锚行（`[RespawnGateDiag/Zombie]`/`[ItemGateDiag/Item]`/`[BuildFingerprint]` 等）不受此限。
- **Horde/Beacon**：不专项开场景；日志中不得出现二者既有语义被改变的证据。

### 证据回传与边界

- 三端正常退出（先 Guest 后 Host）→ UMM 各导出诊断包 → 三端路径（或 zip）回传主工作树会话分析。
- 本票不关实施票 01–04（其转态随 Runtime 证据与用户裁决）、不改 RELEASES 批准态、不写生产代码；分析结论按 Evidence Class 落盘 `audit/<date>/RuntimeAcceptance-0.2.4.8-Ticket05-*.md`。
