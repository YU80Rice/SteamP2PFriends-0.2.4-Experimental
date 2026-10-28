# Item-SessionReadinessAndFrameProgress 实施审计

## 当前状态

- 候选状态：已限定提交 `6526b7e`；提交后Release构建成功，单DLL身份门PASS。源码状态 `implemented-pending-runtime`。
- 范围：Resource 先启动共享 engine 后 Item 的本地 session/preparation/admission 初始化；返区结算的逐帧有界消费；不含 C/Resource 观测 NRE、僵尸/掉落概率、ACK/LMN、故障清除或部署。
- 基线：`5c496c0b3c8aa61ba825348bec637071759223c0`，分支 `codex/structure-baseline-0.2.4`。
- 构建 owner：Vela `426e9bf4-3888-4a06-b04c-3ed8fac35264`，native `01a17e32-c56c-7de2-85b9-33010a59d147`，`gpt-6-luna/high`；领取记录在 `.scratch/matt-orchestrate/item-session-readiness-and-frame-progress-20261028/builder-claim.md`。

## 变更摘要

1. `ItemProductionControlSeam` 单独记录本地初始化 epoch；同 epoch幂等，不把共享 engine active 当作 Item 已初始化。退出旧 Item writer 时，仅当其本地 epoch 仍是 engine 当前 epoch 才结束共享 engine；避免结束刚由 Resource 启动的新会话。
2. 当前准备明确由 `(epoch, worldIdentity)` 认定。Coordinator 先绑定当前身份及本地 Item session，再准备当前世界并保留有效当前 Admission。
3. Coordinator 将原采样/共享 reconcile callback 接入 `RunActiveFrame`。健康帧只推进共享 engine 时钟一次；reconcile仍受原一秒门控制，共享 flush 只在 due reconcile 一次；Item settlement每个有效帧最多消费原动作预算一次，需求刷新与自然周期仍仅 due 执行。
4. 保留原 capture batch fail-closed、identity/backoff/fault/挂起/未准备门、单 writer、自然 job 无人时收尾、R01–03/NIL/GBA/距离及共享恢复合同。

## Red → Green

- A：`Test_SFP01_ResourceStartedEngineStillInitializesItemPerWorld` 从 Resource 先 `BeginSession` 的同一 shared engine 到真实 coordinator Item begin / `ItemStepFiveAdmission.Admit`。基线目标准备/Admission断言返回 `false`（日志：`.scratch/matt-orchestrate/item-session-readiness-and-frame-progress-20261028/sfp01-red.log.txt`，exit：`sfp01-red.exit.txt`）；修复后同方法通过。覆盖新 world/epoch、重新准备、旧 Admission 拒绝、当前 Admission 接受，以及不结束已启动的新 Resource session。
- B 旧来源与当前严格fixture分别记录：父native物理行1003（2026-10-27T16:52:08.801Z，`exec-13f4354c-0067-4f9b-abe8-ade8be2ed651`）和1046（16:53:08.688Z，`exec-667b28d8-13fc-498d-9976-3f7e479fe8fb`）是真实旧SFP03 Red/Green，但旧callback含额外`seam.Flush`，不能证明当前严格fixture首due预算。此前误引ordinal1394/1395为严格fixture Red已撤回；该错误引用及17:10错误fixture失败保留在轮次历史，不作业务证据。
- SFP-R2-G1在固定基线+当前A修复+保行为旧门提取的隔离RED，对**同一当前严格fixture**实测目标`false`/exit1：12个过期Natural、latePrepared/duplicateBegin均true，callback1、首due后11项、0交付、held/bounded/no-partial/fault均满足。当前GREEN同源同测试21×1/60秒内完成：0项、1次完整交付、目标true/exit0。两侧production与test-copy MVID、真实加载路径及逐命令UTC/stdout/stderr/exit见本run `experiments/b-final-fixture/g1-experiment-result.md`；源映射、HintPath与完整输出见相邻材料。RED额外CS0414 warning透明保留为warning，未放宽其他warning。
- Round 1 Standards指出旧SFP03 callback额外调用 `seam.Flush` 且未检查首个due帧；当前严格测试已改为仅真实共享 Observe/Flush、首次due也纳入每帧≤1动作断言。
- SFP05：共享 Resource 与 Item 注册于同一 lifecycle engine；经 frame seam推进 delta，并以真实 Resource hysteresis release timing 观察每帧唯一 engine clock。目标通过。

## 自动化验证

- 本轮最终 Release 构建与正式 Main：详细命令、独立 host、生产/测试 MVID、ledger、stdout/stderr 位于 `.scratch/matt-orchestrate/item-session-readiness-and-frame-progress-20261028/`。
- Round 1 `full-suite-final-2` 为 `635/635 PASS`，但该轮SFP03测试有额外 consumer 调用，不能单独证明预算。当前 Round 2 候选正式 Main `full-suite-round2`：`635/635 PASS`、exit `0`；635 行同一 runId、ordinal 连续，633 Framework-local + 2 CoreCLR-x86。NIL01 与 GBA01 各 1 条真实 child 路由；Runtime 类仍为 PENDING。
- 第一轮全量曾为 `632/635 PASS`：SFP05 因协调器静态节拍未隔离；旧 Readiness StaticIL 仍要求被提取前的 Tick→恢复闭环直接接线。已分别在 SFP 测试 finally 恢复静态状态，并把旧 StaticIL 门改为核对实际 reconcile callback，随后同候选完整 Main 635/635 通过。首轮证据保留在 `full-suite-final/`。
- 当前 StaticIL 证明生产 Tick→RunActiveFrame、实际 Reconcile callback、单次时钟、单次 shared Flush、无 Item旧 Flush alias；SFP01/03 为独立业务控制流 Red/Green，不以静态IL代替。

## 运行时与下一步

- 本票没有新的实机证据；旧 1H1G 的旧症状本场未复现仍只代表该场。进入物资区延迟的用户体验结果待 R04；1H2G、原06/07、僵尸项保持 OPEN。
- 运行时：本票没有新的实机证据；旧1H1G本场未复现仍只代表该场。视觉延迟由R04继续验证；1H2G、原06/07、僵尸项保持OPEN。DLL未安装/部署/发布；不生成ZIP。

## 审查轮次 1 记录

- Standards（native thread `01a17e52-f165-74c3-874e-24be0aabc6e5`，`gpt-6-luna/high`）：NOT CLEAN。SFP03 callback的 `seam.Flush(0f)` 会额外消费一次；首个due帧未计入预算断言。已改callback与完整首帧动作断言，并重建、定向及完整Main验证。
- Spec（native thread `01a17e53-114b-7ee3-9d6e-ba4389b4d587`，`gpt-6-luna/high`）：NOT CLEAN。原始裁定指出SFP03 Red只有父执行回显、未持久化；当时把父ordinal1394/1395错误定位为11项Red并回填。Round 2已证明该定位错误：1768/1769 exit1输出不是目标Red，1394/1395也不是源；上述Red引用已撤回，原误记作为审查历史保留，不再作业务证据。
- 本轮完整非CLEAN次数：1；累计源码审查窗口：1/2。两个报告及来源保存在本run；本票累计复审仍保留所有历史，不因G1补证归零。

## 审查轮次 2 记录与后续澄清

- Standards（native thread `01a17e59-d7f1-7481-94f6-c52a76f56fcd`，`gpt-6-luna/high`）：CLEAN。详见本run `root-round2-standards-native-final.md` 及原生来源证据。
- Spec（native thread `01a17e59-f222-7571-af14-8631da33d936`，`gpt-6-luna/high`）：NOT CLEAN。原完整轮明确1394/1395无关且1768/1769的`items=12/deliveries=1/held=False`不是目标Red；17:10错误fixture失败不是生产Red。随后同轮澄清核出真实旧门pair：物理行1003 `exec-13f4354c-0067-4f9b-abe8-ade8be2ed651`（2026-10-27T16:52:08.801Z）输出11项/0交付/held=True/targetFalse/child exit1；物理行1046 `exec-667b28d8-13fc-498d-9976-3f7e479fe8fb`（16:53:08.688Z）输出0项/1交付/held=True/targetTrue/child exit0。但旧callback额外执行`seam.Flush`，不能证明严格fixture首due预算，Spec维持NOT CLEAN并要求隔离同测Red。
- 根据专家同run只读匹配及根登记的SFP-R2-G1，当前严格fixture已完成隔离同测Red/Green；完整输入、runner身份门、逐命令日志及目标输出见`experiments/b-final-fixture/`。此新证据只补AC3本次严格对照，不修改round1/2结论、历史候选或635/635适用范围。
- Round 2结束时累计完整非CLEAN轮2、窗口2/2；根登记一次G1恢复窗口后，当前候选待唯一第3/3轮完整双轴复审（该恢复窗口预留1轮）。

## 审查轮次 3 记录

- Standards：CLEAN；native `01a17fe9-a8c7-70a1-86f0-fe83277fadaa`，`gpt-6-luna/high`。核验epoch/world局部幂等、共享Resource会话不被旧Item writer误结束、生产帧链及shared flush、逐帧预算和真实测试接缝；无阻断。
- Spec：CLEAN；native `01a17fe9-c114-7f33-9a45-8a59614680b3`，`gpt-6-luna/high`。AC1–7由当前生产/测试、严格SFP-R2-G1对照和完整Main支持；AC8为双轴后续限定提交/产物动作；AC9 Runtime/R04仍待测，不宣称通过。
- 两名审查者均为父native `SubAgentActivity.started`实际启动的并行独立原生实例。完整session_meta、turn_context、final及来源证据见本run `root-round3-native-provenance.json`、`review-round-3-results.md`。
- 本票累计第3/3完整轮，两轴同一候选均CLEAN；不拼接前轮裁定。隔离对照和完整Main证据均引用原日志，本轮未改生产或测试代码。

## 提交与单DLL

- 限定提交：`6526b7e`（`Fix item session readiness and frame progress`），仅包含本票生产/测试、issue/spec/audit、审查与本票必要实验/Main证据；五个预存tracked保护文件未纳入。
- 提交后Release：2026-10-28T00:35:26Z起使用仓库约定的Release命令构建成功，exit 0，输出 `bin/Release/SteamP2PFriends.dll`；完整实际日志在本run `postcommit-production-release.log`。代码与已跑635/635候选相同，提交/记录只改变工件交付状态，不改变生产/测试输入。
- 独立交付：`artifacts/Item-SessionReadinessAndFrameProgress/6526b7e-20261028-0035/SteamP2PFriends.dll`。文件非空1,387,008 bytes；Mono.Cecil元数据为net472 CLR v4.0.30319、I386/ILOnly AnyCPU、Assembly/File version `0.2.4.10`。独立 `Verify-BuildFingerprintArtifact.ps1` Cutover门PASS；Case `SPF-0.2.4.10-Experimental-ItemSlice-Cutover`，MVID `58277318-8897-4147-b9a2-9bb6fd3e9f49`，SHA-256 `02FA2AFD442D074FF584B01503AE8A1AC6FB33DE9CD55CC698CB8916924AC6C7`。详细身份在DLL同目录。
- 本票`delivery.md`与DLL同目录README给出各端关闭游戏、备份旧DLL、只替换本插件、UMM新会话及正常退出回滚步骤；R04测试路线保持开放，不宣称玩家可见延迟已消除。
