# Ticket 05 Runtime 验收审计:1H+1G 补测闭合密码三局与门控诊断;票 05 机制断言成立

- **日期**:2026-09-18 00:47
- **基线**:候选 DLL 仍绑定 HEAD `01dc27b`(票 04 最终构建);执行包 `f84958d`;上轮 `68e546a` / `audit/2026-09-17/RuntimeAcceptance-0.2.4.8-Ticket05-2320.md`(不构成 PASS)
- **票据**:`.scratch/listen-host-join-routing-runtime-acceptance/issues/05-shared-1h2g-runtime-acceptance.md`
- **证据来源**:2026-09-18 两端 UMM 诊断包(1 Host + 1 Guest,用户一人分饰两角;上轮已实证 1H+2G 无密码同房):
  - 主机:`…\UMM-诊断包_20260918_004720\LogOutput.log`(17426 行)
  - 客机:`…\UMM-诊断包_20260918_004700\LogOutput.log`(3721 行)
- **DLL 指纹关联**:两端 L15 `[BuildFingerprint]` 与候选表逐字节一致——`mvid=2ea4dbc6-be2e-4f72-95d0-dfd438ad0f62`、`dllSha256=A69B78AFDA3185865FF79AC54708DFCEF625DBF9EE622B6492033D9E76877217`、`caseId=SPF-0.2.4.8-Experimental-StructureBaseline`、`caseIdSource=build-metadata`。

---

## 1. 结论

**本轮机制断言闭合;用户 2026-09-18 关单票 05。** 不改 RELEASES。01/02/04 随关单转 completed。票 03 **不转**:用户确认的操作是「端口栏先填 27016,再在地址栏填裸 SteamID」。识别为 SteamID 后 `MenuPlayConnectP2PIndicatorPatch` 将 `portField.IsVisible=false`,P2P 分支只 `Classify(hostField.Text)`、不读端口栏——测到的是裸 SteamID 路由,不是地址栏整串 `SteamID:27016`。

上轮缺口本轮均有日志行:主机 Verbose 已开(`[Startup] … internal monitor=True`,L18;探针非 0);三局 `StartP2PServer` 为 False → True → False;客机无密码进入、`PASSWORD(6)` 拒绝、填对进入、第三局无密码再进。

形态说明:本轮 1H+1G,不重复上轮已成立的双客同房。票面「1 Host + 2 Guest」的同房并发已在 09-17 轮 `clients=3` 取证;本轮补的是诊断与密码切片。

## 2. Case 矩阵

| Case | 裁决 | 证据 |
|---|---|---|
| C05-01 指纹 | **PASS**(跨两轮) | 09-17 三端 + 本轮两端同一 SHA/MVID/Case ID |
| C05-02 无密码 SteamP2P | **PASS**(裸 SteamID 路由) | 第一局主机 L790/L810 `hasPassword=False`;客机 L148 `[UnifiedConnect] route=SteamP2P target=76561199030780228 hasPassword=False started=True`,L156 `ServerAccepted` / L413 `Connected` `failureInfo=NONE(0)`;主机 L2136 `HOST_ACCEPT` `76561199721762479` `clients=2`。用户关单说明:端口栏先填 27016、地址栏填裸 SteamID;识别后插件隐藏端口栏,P2P 不读该栏。**不是**地址栏整串 `SteamID:27016`(票 03) |
| C05-03 僵尸 | **PASS** | 主机 `RespawnGateDiag/Zombie` 53 行。第一局 `elig=True`;空 bound 的 `passed=0 returned=N` 是原版「Count<=0 早退」(探针 `ObservePass`),**不是** dedicated 门没开。走进对齐分支:`bound=12 calls=91 passed=91 returned=0`(L1932);同局 `allPassed` 14→477(L1609→L4833,#20/20 配额用尽)。`ambiguous=0`。用户口述可见刷新 |
| C05-04 物品 | **PASS** | `generateItems` 15 / `despawnItems` 53 / `respawnItems` 53。`respawnItems` `elig=True` 且 `calls` 持续增长(第一局 L3879 `calls=4802`)。周期移除:L1610 `removed=0` → L3878 邻域 `removed=13`(L 见下表)。再生:L3879 `region=(30,32) spawned=7 cooldown=4788 idle=7`(窗口未到为主、已有 spawned,非 `calls==0`)。第三局仍有 `spawned=1/3`。`anomaly=0`;`spawned` 个位数相对数千 `calls` 收敛,无同区无界累加。`generateItems` 进区账本不构成本票判据 |
| C05-05 密码生命周期 | **PASS** | 第二局非空(L11657/L11677 `hasPassword=True`) → Stop(L14399) → 第三局开局 L14437/L14457 `hasPassword=False`;客机 L3228 无密码 `started=True`,L3236 `ServerAccepted`。结束路径静默,可观测点在下一局开局,与执行包一致 |
| C05-06 有密码可连 | **PASS** | 客机 L2735 `[UnifiedConnect] … hasPassword=True started=True`;L2743 `ServerAccepted` / L3214 `Connected` `NONE(0)`。主机 L13056 `HOST_ACCEPT` 同客机。本轮客机无 `ServerConnectParameters constructed` 字面行(Verbose 自报被归一化;该行非 C05-06 常开锚) |
| C05-07 不填 → PASSWORD | **PASS** | 客机 L2720 无密码 `started=True` 后 L2721 `Rejected by server (WRONG_PASSWORD)` `failureInfo=PASSWORD(6)`;L2723 `Failed`;L2724 `!!! 连接失败 !!! info=PASSWORD`;L2725 `SafeAlert` 跳过自建弹窗(`MenuUI.window 未就绪`——未新增专用密码弹窗)。主机 L12727–12730 `rejection=WRONG_PASSWORD(4)`。原版枚举,无明文 |

## 3. 物品锚行摘录(防「calls 增长但无 removed/spawned」误读)

第一局 `despawnItems` 从空转(L1453 `removed=0 empty=1`)到有过期移除(后续 `removed=13/3/14/18` 等,配额内多次)。`respawnItems` 多数行是空图 `nospawn` 或 `cooldown`(窗口未到),这是轮转扫过无生成点/未到 `Respawn_Time` 的形态,执行包判读口径明确不判 FAIL。有生成点且过窗的区域出现 `spawned>=1`,与用户肉眼「会再长」一致。

## 4. 边界

- **Horde/Beacon**:本轮 0 命中;三局均 PEI/EASY/cheats=True。
- **密码纪律**:两端 `password` 命中为 `hasPassword` / 失败枚举 `PASSWORD` / `WRONG_PASSWORD`;无长度、无明文。
- **退出**:客机三局均为 pause-menu exit 或 application quitting,先于主机对应 `StopP2PServer`;两端摘要退出码 0。
- **1H+2G 形态**:同房双客本轮未重复;09-17 轮已绑定同一 DLL 并 `clients=3`。本轮不回退该证据。
- **`:port` 粘贴**:用户关单时说明实际操作=端口栏 27016 + 地址栏裸 SteamID(因识别 SteamID 后插件挡住端口栏)。**不是**票 03 目标输入 `hostField="SteamID:27016"`。四次连接均为 `route=SteamP2P`,与裸 ID 分类一致。票 03 Runtime 仍 pending。

## 5. 状态建议(本报告不关单)

| 项 | 建议 |
|---|---|
| 票 05 | completed(用户关单) |
| 票 01 | completed |
| 票 02 | completed |
| 票 03 | 仍 `implemented-pending-runtime`(缺地址栏整串 `SteamID:port` 输入) |
| 票 04 | completed |
| RELEASES | 不改 |

## 6. 审计纪律

- 只读两端日志与工作树。分析会话未改生产代码。
- 本报告为归档新建文件。
