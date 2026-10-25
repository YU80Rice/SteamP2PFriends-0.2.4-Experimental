# 实施审计 — SPF 0.2.4.10 R03

- 票据：R03，Guest 离开后精确撤销旧 Item 资格，房间继续运行。
- 固定基线：`codex/structure-baseline-0.2.4` / `c081dfda29bdac2b703af9871b887b3a45ab5c05`。
- 源码提交：`ebf4493`（实现）；交付记录闭环提交：`f74301a`（当前 HEAD）。
- 状态：`implemented-pending-runtime` / `ready-for-human`。历史 NRE、原生 Guest 退出清理及 Guest 离开后房间继续运行转 R04 Runtime；原八条 AC 未全完成。

## 故障证据与退出顺序

旧现场 Host 日志记录断线后 `SnapshotRemove`、`ObserverCompensation`、`ObserverRemove` 的 `NullReferenceException` 及 `repair-required failClosed`，但缺少完整堆栈和失效对象快照，具体根因未知。旧候选 `01692ecc` 先移除 Item replication adapter，Core Item observer 清理由后续快照差异推进；这是旧链路，不能据此确认历史 NRE 根因。

当前链路从 `SessionDisconnectDispatcher.OnEnemyDisconnected(SteamPlayer)` 进入 `ItemManagerRegionSyncPatch` 与 `MultiObserverShadowCoordinator.OnItemObserverDisconnected`：精确匹配 owner/transport/committed token 后，`ItemProductionControlSeam.RemoveObserver(observer, token)` 撤销旧 settlement 资格；engine 分区完成 Guest baseline unbind，成功后移除 observer/token；coordinator 清理 `ItemObservers` 和对应 `Connections`；最后按 owner/transport 精确移除 adapter。前序失败时不得声称后续 adapter 清理完成；异常经现有补偿路径传播。

## 原票八条 AC 证据边界

原八条 AC 原文保留于修复票；以下只记录证据，不改写或将八项全部勾选。

| AC | 当前证据与未完成项 |
|---|---|
| 1 | IT145 在真实 seam 注入 Guest baseline unbind 异常，断言 action、identity、stack frame 与传播；IT146 是独立迟到断线 Red→Green。两者均非历史 Host NRE 复现；历史堆栈/失效对象证据待 R04。 |
| 2 | IT145/146/149 覆盖真实退出 seam、失败补偿及迟到/重复回调；Unity 原生对象销毁时序留 Runtime。 |
| 3 | IT146–149 覆盖 owner+transport、Core token 精确撤销及新连接/另一 observer 保留。 |
| 4 | IT145 确认异常不被吞并保留诊断；原生对象销毁且确认清理后的幂等边界待 Runtime。 |
| 5 | IT143/144、IT148 覆盖 delivery/recovery 精确撤销；有效 settlement 按既有生命周期继续收尾。 |
| 6 | 修复不改 Resource/Collision；多人房间继续运行与交互待 R04。 |
| 7 | 未发现真实前票接口依赖，未新增调度阻塞边。 |
| 8 | R5 双轴与限定源码提交完成；全票 Runtime/原生边界仍未闭环。 |

## 验证

- Whitelist 测试构建 exit 0，4 个既有 warning、0 error；`verification/whitelist-build-propagation-assertion.log`。
- IT145 `Test_IT145_ObserverExitFailureReportsIdentityAndStack` 定向 exit 0；`verification/IT145-propagation-assertion-corrected.log`。首次 helper 参数为完整 namespace 导致类型未发现，按 helper 合同改用短 Type 名后通过，非测试失败。
- IT145 只新增 `propagated` bool 与最终断言；生产代码已抛出异常，属于补覆盖、直接 Green，不伪造 Red。
- 既有全量 `619/619 PASS` 在本次单一测试方法增量前完成；生产与其他测试未变，依授权复用。适用结论为“既有全量 + 本次 Whitelist build + IT145 定向”；未重跑无关全套或 Release 构建。
- Runtime 尚未验证；未部署、发布或要求玩家测试未审 DLL。

## 审查历史与阶段授权

R1 当时九个源码/测试文件仅双 CLEAN。R2 交付记录在审查期间变化，不构成同冻结候选有效完整轮；保留两轴 NON-CLEAN。R3 同候选有效双 NON-CLEAN：Standards 维持 AC1 历史堆栈/清理边界缺证，Spec 要求 IT145 精确 action/stack-frame 断言。R4 同候选有效双 NON-CLEAN：Standards 指出审计误述旧 adapter-first 顺序；Spec 指出历史 stack/object 证据及 AC1/AC8 未闭合。各轮原报告与不同结论保存在本次 run `messages/`；R5 是累计第4个有效完整轮。

用户于 2026-10-25 批准先交付当前证据支持的源码阶段修复，将历史 Host NRE 根因、真实原生退出清理与 Guest 离开后房间继续运行并入 R04 同场 Runtime 验收。该决定调整 AC1 证据时序/源码交付前置，不改变原八条 AC 文本，不表示历史故障已复现或解决。

## R5 双轴审查结果

本轮全新独立 Standards/Spec 在同一冻结源码阶段候选上均为 CLEAN；结论仅覆盖用户批准的源码阶段，不表示原八条 AC 全部完成。Standards native `01a172cf-17f4-7590-8aa1-12f4e8592ef1`（`/root/r05_standards_reviewer`），Spec native `01a172cf-54b0-7722-9fb9-3e3c2c0a5976`（`/root/r05_spec_reviewer`）；父 SubAgentActivity 与子会话 session_meta/turn_context 核实均为独立 subagent、`gpt-6-luna/high`。

Spec 保留 AC1 历史 NRE 证据、AC6 房间继续运行、原生销毁边界与 AC8 全票闭环未完成；本轮没有声称历史故障复现或 Runtime PASS。审查者未运行测试/构建；采用本记录中当前 Whitelist build、定向 IT145 与未变生产/其余测试既有 619/619 结果。
R5 报告正文及核验来源存于 `.scratch/matt-orchestrate/item-runtime-repair-20261025-vela/messages/r05-standards-report.txt`、`r05-spec-report.txt`；交付正文和 VelaTerm message-id/回执随后另存于同目录。
