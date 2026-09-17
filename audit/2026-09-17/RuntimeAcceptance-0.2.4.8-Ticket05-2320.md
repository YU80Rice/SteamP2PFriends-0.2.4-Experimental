# Ticket 05 Runtime 验收审计:指纹绑定成立,无密码 SteamP2P 可连;僵尸/物品仅肉眼,密码三局未跑

- **日期**:2026-09-17 23:20
- **基线**:候选 DLL 绑定 HEAD `01dc27b`(票 04 最终构建);执行包提交 `f84958d`
- **票据**:`.scratch/listen-host-join-routing-runtime-acceptance/issues/05-shared-1h2g-runtime-acceptance.md`(`ready-for-human`)
- **证据来源**:2026-09-17 三端 UMM 诊断包(1 Host + 2 Guest):
  - 主机:`…\UMM-诊断包_20260917_232013\LogOutput.log`(19602 行)
  - 客机1:`…\UMM-诊断包_20260917_231909\LogOutput.log`(1013 行)
  - 客机2:`…\UMM-诊断包_20260917_231940.zip`(解压至系统临时目录核验,5926 行)
- **用户口述**(权威肉眼口径,不替代诊断):可见僵尸与物品刷新,无问题。
- **DLL 指纹关联**:三端 `[BuildFingerprint]` 逐字节一致——`mvid=2ea4dbc6-be2e-4f72-95d0-dfd438ad0f62`、`dllSha256=A69B78AFDA3185865FF79AC54708DFCEF625DBF9EE622B6492033D9E76877217`、`caseId=SPF-0.2.4.8-Experimental-StructureBaseline`、`caseIdSource=build-metadata`。与票 04 审计 §5 / 执行包候选表一致。

---

## 1. 结论

**本轮不构成票 05 PASS。** C05-01 成立;C05-02 无密码 SteamP2P 进入成立(两客机 `started=True` 且 `failureInfo=NONE` 进世界);C05-03/04 仅有用户肉眼、**无** `[RespawnGateDiag/Zombie]` / `[ItemGateDiag/Item]` 锚行(Verbose 未开,探针零输出);C05-05/06/07 **未跑**(全程单局无密码,`hasPassword=True` 三端 0 次,无原版 `PASSWORD`)。

按执行包与 SOP:肉眼刷新不得单独把实施票 01/02 转 PASS;密码切片(票 04)与 `SteamID:27016` 分类(票 03)本轮无独立 Runtime 证据。不关实施票、不改 RELEASES。

## 2. Case 矩阵(按执行包逐项,引用日志行)

| Case | 裁决 | 证据 |
|---|---|---|
| C05-01 三端指纹 | **PASS** | 三端 L15 同一指纹行,`caseIdSource=build-metadata`(非 environment) |
| C05-02 无密码进入 | **部分 PASS** | 主机 `StartP2PServer … hasPassword=False`(L145)+`[SessionPassword] hasPassword=False`(L157)。Guest1 L148 `[UnifiedConnect] route=SteamP2P target=76561199030780228 hasPassword=False started=True`,L156 `ServerAccepted` / L625 `Connected` `failureInfo=NONE(0)`。Guest2 同锚 L148,L436 重连后 `ServerAccepted`。主机 `HOST_ACCEPT` Guest1=`76561199130814523` t=52.347s `clients=2`;Guest2=`76561199721762479` t=1194.322s `clients=3`。**日志无法区分裸 SteamID 与 `SteamID:27016`**(端口不进连接参数、不打日志) |
| C05-03 僵尸重生 | **证据不足** | 主机 `RespawnGateDiag/Zombie` **0 行**。用户口述可见刷新。间接:`[MultiObserver/M3-Zombie] acquire-commit bound=0 … zombies=0->38`(L1037)属进区租约,不是 `respawnZombies` 对齐分支。无法用 `passed`/`calls==0` 区分早退仍在 vs 窗口未到 |
| C05-04 物品周期 | **证据不足** | 主机 `ItemGateDiag/Item` **0 行**。用户口述可见刷新。`[ItemAuthorityGate] commit` 16 次 / `skip` 48 次(`already-generated-or-preparing`)是进区预生成账本,规格明确不构成本票周期判据。无 `removed`/`spawned`/`calls` 区域计数,不能证周期 despawn/respawn,也不能证无界累加 |
| C05-05 密码生命周期 | **未跑** | `StartP2PServer` 仅 1 次; `StopP2PServer` 1 次(L19579,`reason=application quitting`);无第二/三局开局 `[SessionPassword]` |
| C05-06 有密码+SteamID 路线 | **未跑** | 三端 `hasPassword=True` 计数 0;无 `ServerConnectParameters constructed` |
| C05-07 有密码不填 → PASSWORD | **未跑** | 无 `failureInfo`/`PASSWORD` 枚举。Guest2 一次 `TIMED_OUT(40)`(L414–416,约 30s 无服务器消息后重连成功)属会话超时,不是密码失败 |

## 3. 根因:Verbose 门控关闭

执行包部署步骤 3 要求三端将 `Debug.VerboseDiagnostics=true` 后重启。本轮:

- `[RespawnGateDiag/Zombie]`、`[ItemGateDiag/Item]`、客机 `ServerConnectParameters constructed` 均为 0。
- 插件 `[Startup] … verboseDiagnostics=` 自报行三端均缺失。该行文本含 `Diagnostic`,命中 `RoleLogger.DiagnosticMarkers`,走 Verbose 门控的 `Diagnostic()`——**Verbose 关闭时连自报也被吞**,故不能从该行反证开关,只能由探针 0 行推定未开。
- `[BuildFingerprint]`/`[SessionPassword]`/`[UnifiedConnect]` 常开锚行存在,与门控设计一致。

因此 C05-03/04 的诊断确认条款本轮不可判定,不是「窗口未到」。

## 4. 其余边界

- **Horde/Beacon**:主机日志 0 命中;开房 `map=PEI mode=EASY cheats=True`。无语义被改证据(本轮也无专项场景)。
- **密码明文/长度**:三端 `password` 命中均只经 `hasPassword=`;无长度、无明文。纪律在已产生的密码相关行上成立。
- **退出顺序**:Guest1 先 `application quitting`(t=252s);Guest2 先 TIMED_OUT 再重连,后 `application quitting`(t=1851s);Host 后停(t=1453s,`StopP2PServer`)。主机摘要 `UncleanExit` 退出码 `-1073741569`,与票 05 判据无直接对应,不升格为本票缺陷。
- **G2 TIMED_OUT**:约进世界 48s 后 `without a message from the server`;随后 `join-request` 成功。不纳入 Join Routing 失败矩阵。

## 5. 对票 05 与 01–04 的状态

| 项 | 本轮 |
|---|---|
| 票 05 | 仍 `ready-for-human`;清单仅 C05-01 可勾。不改 RELEASES |
| 票 01/02 | 仍 `implemented-pending-runtime`(肉眼刷新记录在案,缺诊断闭合) |
| 票 03 | 仍 pending(`SteamID:27016` 无独立锚) |
| 票 04 | 仍 pending(有密码三态+生命周期未跑) |

**复测最小集**(同一 DLL;三端先开 Verbose,主机日志须先出现 `RespawnGateDiag`/`ItemGateDiag` 再开局;不扩大规格):

1. **第一局无密码**(可重跑或与本轮合并补证):Guest1 裸 SteamID、Guest2 地址栏粘贴 `SteamID:27016`(回传时注明谁用了哪种),闭合 C05-02 端口分类。同一局:记录某区域僵尸初始数量 → 打死满足重生条件的僵尸 → 等窗口 → 该区域出现新僵尸,主机对应 bound `passed` 随时间增长(C05-03);物品 `respawnItems` `calls` 持续增长、`removed`/`spawned` 带区域且 `spawned` 收敛无同区无界累加(C05-04)。
2. **第二局有密码**:Guest1 `SteamID:27016`+密码 → C05-06;Guest2 不填 → 原版 `PASSWORD`(C05-07)。
3. **第三局**:ESC 后重开、密码框默认空、开局 `hasPassword=False`、Guest 无密码可连 → C05-05。

## 6. 审计纪律披露

- 只读三端日志与工作树;客机2 zip 解压至 `/tmp`,未入仓库。
- 本报告为归档新建文件。分析会话未改生产代码。
