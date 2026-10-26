# Item-GuestBindingAdmission：合法Guest首次交付与发送前分类

- Label: ready-for-human
- Type: AFK
- Status: implemented-pending-runtime
- Version: SteamP2PFriends 0.2.4.10 Experimental
- Baseline: `2aa91e1c9f6053d9998dd957d103470ccbcfebc2` / `codex/structure-baseline-0.2.4`
- Review gate: GBA-R2-G1 completed; round 3 fresh native Standards and Spec both CLEAN after same-round identity clarification. Native provenance and original finals are archived under `.scratch/matt-orchestrate/item-guest-binding-admission-20261027/review-round-03/`.
- 主调度：Vela `0249e965-1143-4b6b-96f9-67484a145008`
- 技术规格/指导：Vela `6c55c1a2-e8b8-4f78-90d8-f3f63ca713b7`，Codex gpt-6.1-sol/high
- 实施负责人：Vela `924e4179-a42e-471a-a8b3-8d527d3afa6c` / native `01a17922-6540-7901-afe9-9d8d7bfb019b`，gpt-6-luna/high。
- Created: 2026-10-27

## 问题与证据

Host初区、Guest加入后与Guest退出后的物资，玩家均观察正常且无幽灵；但两次世界首次Guest连接各出现九区长“可能半包”提示。同世界重连不再提示。截图可见九个不同格，不证明包实际不完整、同区重报或九区已完整交付。僵尸本场未确认。

Host日志18次首错均为`BindGuestBaseline`发送前的stale身份拒绝，随后普通异常被默认分类为possible-half-packet，逐观察者/连接/区域隔离并通知。首次领域Observe先回调binder、后提交领域token；已提交的协调器物理身份与领域表不同。该时序机制尚待本票真实Red，不把源码推导写成已运行测试。

复用[诊断与源码因果](../matt-orchestrate/item-runtime-diagnosis-20261027-halfpacket/expert/diagnosis.md)及[根会话/包索引](../matt-orchestrate/item-runtime-diagnosis-20261027-halfpacket/evidence-index.json)，不重复调查包/哈希。

## 单一交付

一次完整修复：正确准入首次合法物理连接；发送前等待/拒绝不误判半包，真实未发送与发送后未知沿原恢复合同处理。交付当前全量自动化及双轴通过的独立可实机测试DLL与ZIP，不停在源码阶段。

## 验收与禁止项

- [ ] 真实首次Observe→binder生产准入Red→同测Green；物理owner/transport/token及会话校验不弱化。
- [ ] 等待、过期拒绝、确认未发送、发送后未知在即时/结算后路径正确处理；旧退出回调不修改新绑定。
- [ ] 同token新区、重连、Host/另一Guest及原恢复/呈现/引用判空/距离门回归保持。
- [ ] 当前所有正式登记实际执行、完整结果/父子身份/exit闭合；必要runner维护纳入同候选。
- [ ] 全新原生Luna/high独立双轴CLEAN、限定提交、有效net472 Release DLL和独立ZIP/指纹/说明齐备。
- [ ] 实机状态保持pending，由根安排R04；不自动部署、发布或给僵尸PASS。

## 实施候选证据

- Seam-preparation真实Red与本票同测Green详见`audit/2026-10-27/Implementation-0.2.4.10-Item-GuestBindingAdmission.md`及本票builder记录。
- 首轮审查发现及身份来源限制见`.scratch/matt-orchestrate/item-guest-binding-admission-20261027/review-round-01/`；已补真实GBA01 owner/transport/token/epoch拒绝和旧Unbind零副作用、GBA08结算等待后续送达，以及模块精确路由/runId ledger。
- 修复后当前正式Main `632/632 PASS`；Framework本地630、CoreCLR 10.0.9 x86 NIL01/GBA01各1；runId `1db2a9cc-b33a-4b11-8a13-6570293e6303`，完整ledger及双流位于`.scratch/matt-orchestrate/item-guest-binding-admission-20261027/integrated-run-review-fixes-02/`。
- GBA-R2-G1当前Main `632/632 PASS`；runId `e61c06a2-8d95-4a81-a053-4bc45d016559`，Framework本地630、CoreCLR 10.0.9 x86 NIL01/GBA01各1。production/test MVID分别为`2ec64d10-dc14-4c9c-afe2-6c22276e6d14` / `70033a4d-4063-4072-92da-e450a521a30c`，完整ledger及双流在`.scratch/matt-orchestrate/item-guest-binding-admission-20261027/integrated-run-gba-r2-g1/`。
- GBA-R2-G1定向证据：GBA01同测试先以真实confirmed-not-sent→Waiting→原10秒到期复现等待时耗尽通知Red，生产只移除Waiting通知后同测Green；等待保留原故障起点/次数/AttemptId，Accepted后不自动续送且只通知一次，再由`TryManualRecovery`成功恢复。真实Core committed身份暂缺/原引用恢复的reader Waiting→Accepted、A/TA→B/TB正向投影、旧Unbind/旧disconnect保护、Rejected engine不排旧retry一并收于GBA01。
- 上述候选证据记录的是当时审查前冻结状态；现Round 3双轴CLEAN、限定提交和测试包已完成，最终身份见下方交付记录。R04运行时、僵尸掉落与其它Runtime仍pending，不自动部署/发布。

## 已交付测试包

- 实施提交：`0f94e1333b755bc2fe07d81e1cc2dbe32c74a0ba`。
- Release DLL：`artifacts/Item-GuestBindingAdmission/0f94e13-20261027-0259/SteamP2PFriends.dll`；唯一包：`artifacts/Item-GuestBindingAdmission/Item-GuestBindingAdmission-0f94e13-final.zip`。
- 版本/Case/Cutover：`0.2.4.10` / `SPF-0.2.4.10-Experimental-ItemSlice-Cutover` / `Cutover`；MVID `2ec64d10-dc14-4c9c-afe2-6c22276e6d14`；SHA-256 `133BBC899B4B0A032769715E2231F3F7940D8F4DE8C19ED29D81CB6C243C8826`。
- 632/632当前Main及双轴CLEAN已完成；R04实机、僵尸掉落及其它Runtime仍待人工验证。未部署或发布。

保留唯一Writer、原生仓库/协议、R01 pending与呈现、R02有限恢复、R03精确撤销、NativeIdentityLookup与07距离门。不提前提交共享engine token、不建身份表、不清fault、不改概率/掉落/LMN/ACK/reset。具体落点、拟议接口、测试及交付合同见[spec.md](spec.md)。
