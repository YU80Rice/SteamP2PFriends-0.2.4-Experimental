# R03 交付记录 — Guest 离开后的 Item 精确清理

- 分支：`codex/structure-baseline-0.2.4`
- 基线：`c081dfda29bdac2b703af9871b887b3a45ab5c05`
- 源码提交：`ebf4493`（`Fix exact observer exit cleanup for R03`），13 个文件，限本票生产/测试及其票、delivery、audit、index 记录。
- 状态：`implemented-pending-runtime` / `ready-for-human`。R5 双轴 CLEAN 后完成限定提交；原现场 NRE、原生退出清理和 Guest 离开后房间继续运行转 R04 Runtime，原 AC1/AC8 不全闭环。

## 变更

断线分发现在传递原始 `SteamPlayer`，协调器以共享 Core 连接记录中的 owner、transport 和 token 精确撤销旧 Item generation。旧回调不再通过 SteamID 清掉重连后的 adapter 绑定或 Core demand。退出链失败会输出有界的操作阶段、observer/token/region 与堆栈，并保留异常传播；有效结算遵循既有收尾语义。

## 验证

- Release 生产构建：既有 exit 0 证据复用；本次只修改 IT145 测试，生产源码未变。
- IT145–149、IT143/144、IT95/96/100 及 R01 committed-binding 定向回归：全部 exit 0。
- 本次 Whitelist 测试构建：`dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release --no-restore` exit 0；4 个既存 warning、0 error，日志 `verification/whitelist-build-propagation-assertion.log`。
- 本次定向 IT145：`run-single-test.ps1` 的 `Test_IT145_ObserverExitFailureReportsIdentityAndStack` exit 0，日志 `verification/IT145-propagation-assertion-corrected.log`。首次调用误传完整 namespace 导致类型未加载；按 helper 合同改用短 Type 名后通过，并非测试失败。
- Whitelist 全量：测试增量前既有候选 `619/619 PASS (Failed: 0)`；本次仅改 IT145 一个测试方法，生产和其他测试未变，复用该全量证据并另跑本次测试构建+IT145定向，未重跑全套。Runtime evidence 仍 PENDING。
- 测试与构建原始日志：`.scratch/matt-orchestrate/item-runtime-repair-20261025-vela/verification/`。
- 审查历史：第一轮源码/测试候选 Standards 与 Spec 均 CLEAN；第二轮 Spec 与 Standards 均指出交付/审计的首轮状态记录不一致。修正交付记录后，第二轮双方并非同一冻结候选，故不拼接其 verdict，按新鲜完整候选复审。

旧 Host 现场记录在断线后出现 SnapshotRemove/ObserverCompensation/ObserverRemove NRE，但无完整堆栈与失效对象快照。IT145 验证真实退出 seam 上的失败诊断，不将注入故障描述为旧现场根因。真实 Unity 销毁对象幂等边界、1 Host + 2 Guest 持续加载/交互与 SP/U3DS/P2P 行为仍须 Runtime 验收；不部署候选 DLL。

## 第 3 轮审查记录

完整冻结候选两轴均 NON-CLEAN，原始报告与澄清保留于 `messages/r03-round3-review.txt`、`messages/R03-round3-root-native-evidence.json`。Spec 澄清后接受历史 NRE 根因未知，但要求 IT145 断言真实异常的 stack frame 与 `action=guest-baseline-unbind`；两项现已加入并通过定向及全量测试。Standards 明确维持 AC1 NON-CLEAN：历史 NRE 堆栈/清理边界证据缺失，且其判断是 IT145/IT146 不复现历史 NRE。其关于票面调度状态陈旧是另一项意见，建议保留 `ready-for-agent` 并将 Status 更新为已领取、源码/测试首轮 CLEAN、交付未关闭、Runtime pending，现已更新。Spec 接受历史未知不等于 Standards 撤回；当前也没有历史完整堆栈或失效对象快照可补，不能伪造。IT145 新断言增量本身未独立观测 Red。R3 计为 1 次完整 NON-CLEAN；R1 双 CLEAN，R2 候选在审查中变更、未形成同候选完整轮。

## 源码阶段与 Runtime 阶段（用户批准，2026-10-25）

用户批准 R03 源码阶段先交付当前可证修复；原现场 NRE 根因、真实原生退出清理及 Guest 离开后房间继续运行并入 R04 同场 Runtime 验收。此决定改变原 AC1 证据时序/源码交付前置，不代表历史故障已复现或解决，也不表示原八条 AC 全满足。原 AC 文本与 R1–R4 报告/不同结论保留。

当前状态：源码阶段已完成双轴 CLEAN、限定提交及 ticket/delivery/audit/index/主会话交付，标记 `implemented-pending-runtime` / `ready-for-human`；Runtime PENDING。AC1 历史现场证据、原生退出清理和房间继续运行由 R04 同候选实机验证，玩家仅游戏内操作并导出 UMM 诊断包，由 Agent 分析日志。

本次唯一测试改动是在既有 IT145 方法记录 `propagated` 并加入最终断言；生产与其他测试不变，预计直接 Green，按补覆盖记录。复用变更前 619/619 全量结果，并另跑本次 Whitelist 测试构建与定向 IT145，不重跑无关全套。

## R4 审查与本次范围

R4 两轴为有效完整 NON-CLEAN。Standards 原文保存在 `messages/R03-round4-standards-original-report.md`，native `01a17258-878a-7652-b4d4-d0c60e1db14a`；Spec 原文转录保存在 `messages/R03-round4-spec-original-report.md`，native `01a17258-b0cd-7971-83da-d78642694153`。根核验两子代理的 Luna/high 身份，证据为 `messages/R03-round4-root-native-evidence.json`。Standards 的顺序事实 finding 与 Spec 的 AC1 历史证据 finding 各自保留；本次按用户批准的阶段划分改正审计事实并记录未闭合 Runtime 项，不改写历史结论。

R1 源码/测试候选双 CLEAN；R2 文档审查中候选被修改、无有效同候选完整轮；R3、R4 均为有效完整双 NON-CLEAN。R5 是累计第4个有效完整轮；不清零，不自动加开轮次。


## R5 双轴审查结果

全新独立 Standards/Spec 对冻结源码阶段候选均为 CLEAN；结论不代表原八条 AC 全部完成。Standards native 01a172cf-17f4-7590-8aa1-12f4e8592ef1（/root/r05_standards_reviewer），Spec native 01a172cf-54b0-7722-9fb9-3e3c2c0a5976（/root/r05_spec_reviewer）；父 SubAgentActivity 与子会话 session_meta/turn_context 已核为独立 subagent、gpt-6-luna/high。审查者未运行测试或构建。

仍转 R04 Runtime：AC1 历史现场 NRE 堆栈/失效对象证据、AC4 原生销毁幂等边界、AC6 Host/Guest 房间继续运行及完整 AC8 Runtime 闭环。未宣称历史 NRE 根因已知、历史复现或 Runtime PASS。
