# Item-NativeIdentityLookup：修复原生玩家身份的危险判空

- Label: ready-for-human
- Type: Specification
- Status: in-progress
- Version: SteamP2PFriends 0.2.4.10 Experimental
- Baseline: `fb2acbe31ceb786f3dd9e2dbebce185cc700ac9b`，`codex/structure-baseline-0.2.4`
- Created: 2026-10-26
- 技术作者：常驻指导 Vela `6c55c1a2-e8b8-4f78-90d8-f3f63ca713b7`
- 实施负责人：Vela `08ddf56c-a3ee-41f1-a275-4ed3e0e06ad7` / native `01a173f7-c7f6-7031-a4ae-9a99bbe74afe`；用户已恢复实施、回归、审查、限定提交及独立可测包交付授权；不含部署、发布或人工R04验收。
- 调度入口：主调度 Vela `0249e965-1143-4b6b-96f9-67484a145008`

## 问题

`Adapters/Item/ItemPreparationAuthority.cs:515` 的 `sp?.playerID != null` 调用原生 `SteamPlayerID` 重载。该重载解引用比较两端，右侧null会抛出NRE；`sp` 本身为null也会将null传入重载，`?.`不保护整个比较。

本场Host日志4894捕获 `FindClient → UnbindGuestBaseline → OnObserverReplicationExited` 的真实补偿异常，继而Item repair-required/共享写入暂停，Guest退出后仍持续。第一次SnapshotEnqueue首抛点缺stack，推断与已确认补偿栈分开记录。完整证据见[诊断报告](../matt-orchestrate/item-runtime-diagnosis-20261025-2329/expert/diagnosis.md)，不在本票复制诊断全文。

## 要交付的改变

仅修复ItemPreparationAuthority内原生ID引用判空及直接查找、首次Guest绑定、补偿解绑/退出路径的必要测试。保留steamID值匹配、owner/transport/Core token、生命周期、原异常传播及一致性保护。实现合同、真实类型Red设计、接缝前置条件与四层验收见[spec.md](spec.md)。

## 不包含

不改生成器、僵尸/dropLoot/掉落概率，不清fault/repair-required，不放宽重试、绕门、吞异常，不改SDK运算符，不新增仓库/Writer/协议/身份表。`ItemRecoveryCommandPatch`同类判空及无生产调用旧helper均范围外，不自动附带修复。

## 完成边界与依赖

1. 原规格阶段状态为“仅完成规格/未获实施授权”；用户于2026-10-26明确恢复本票实施。当前实现、回归与审查仍进行中，不代表已获CLEAN。
2. 后续源码交付须真实定向Red→同测Green、当前修后候选完整自动化、全新独立原生双轴CLEAN；历史619不能代替。
3. Runtime由根在新候选交付后统一并入R04安排，玩家只操作游戏和提供UMM包；源码交付与Runtime验收分别记录。
4. R01/R02/R03历史CLEAN保留，本票不唤醒旧构建者；新证据不倒推旧退出NRE根因。僵尸来源/Guest包归属/会话结束后再挂起仍为独立证据留项。
5. 旧[根草案](../matt-orchestrate/item-runtime-diagnosis-20261025-2329/drafts/item-native-identity-lookup.md)保留，由根在收齐最终票后衔接引用。

## 当前验证与审计

- 当前阶段：实现与自动化验证完成；第3轮Standards/Spec均CLEAN。审查后仅补齐本票审计记录；phase15生产/测试候选未变。待限定提交及Release测试DLL身份核验/独立打包；Runtime仍待R04实机验收。
- [实施审计/NIL-D01处置](../../audit/2026-10-26/Implementation-0.2.4.10-Item-NativeIdentityLookup.md)
- [phase15 Red/Green、Release、完整Main与候选身份记录](evidence/candidate-validation-phase15.md)
- [双轴轮次映射与原始final](evidence/review-round-1.md)、[第2轮](evidence/review-round-2.md)、[第3轮](evidence/review-round-3.md)、[原始报告](evidence/review-raw-finals.md)
