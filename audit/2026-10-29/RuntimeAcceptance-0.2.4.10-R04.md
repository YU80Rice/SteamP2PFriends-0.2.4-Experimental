# R04：1 Host + 2 Guest 目标体验验收通过

## 结论与候选

用户于2026-10-29明确裁定本轮人工验收通过：物品刷新、僵尸AI及掉落正常，无幽灵物品、不刷新或异常体感。三端日志支持两世界、双Guest在线及退出重连期间Item链持续工作。本轮返修目标验收完成，不追加玩家复测。

- 实现提交：`6526b7e`；交付记录：`027e3bc`。本轮仅更新验收记录，不改代码或DLL。
- 三端当前`LogOutput.log:15`均为版本`0.2.4.10`、MVID `58277318-8897-4147-b9a2-9bb6fd3e9f49`。
- SHA-256均为`02FA2AFD442D074FF584B01503AE8A1AC6FB33DE9CD55CC698CB8916924AC6C7`；Case `SPF-0.2.4.10-Experimental-ItemSlice-Cutover`，Cutover。
- 原635/635及源码第3轮双CLEAN属于交付证据；本轮运行结论来自用户确认与三端实机日志。

## 原始证据与事件顺序

原包共同位于`D:/Agent-工作目录/DevelopMyUNMultiplayerModAndModloader/启动器/UnturnedModManager/publish/UMM-v2.2.1-win-x64/`：

| 角色 | 原包 | 依据 |
|---|---|---|
| Host | `UMM-诊断包_20261029_001733` | Host peer尾号0228；当前LogOutput 26225行 |
| Guest1 | `UMM-诊断包_20261029_001855` | peer尾号2479；当前LogOutput 13074行 |
| Guest2 | `UMM-诊断包_20261029_002041.zip` | zip内同名目录，peer尾号2841；当前LogOutput 18419行 |

以下HL/HC表示Host当前LogOutput/Client，G1/G2表示对应Guest当前文件。只按peer、世界身份及单端顺序关联；Guest时标约快113秒，不强行对齐绝对钟。Prev与旧渲染日志未混入。

1. HL1091、17619分别记录同进程两个Elver会话，Core epoch1/3；HL1061、17589有各自当期生成提交，两世界均有本地Acquire/presented及Guest非空清单。
2. HC786、1017为两Guest先后进入；HL6093为observers=3。HL7011联集9区，HL8922/9359/11269及11504支持三人分散后联集27区。
3. G1退出时HL13332 exact-cleanup，HL13790仍有两观察者；G2继续移动。HC1605及HL16561支持G1同世界重连，随后再次精确清理。
4. 世界2双方再入（HC2989/3013），HL22493仍为三观察者、27区；最终两Guest分别精确清理（HL25295/25846），Host随后结束世界。
5. Host共131条清单发送采样；56条waiting-settlement中可核同peer同区等待后继续发送。发送/bound不是模型完成ACK，不从这些行推算毫秒级延迟或所有包完整性。

## 已通过范围与保留事实

| 项目 | 本轮结论 |
|---|---|
| 物资刷新、无幽灵/不刷新、正常僵尸AI/掉落 | 用户明确验收通过；不再沿用“僵尸未获玩家确认”的历史状态 |
| 新世界与双Guest退出/重连 | 日志支持实际两世界、同时在线、移动及精确清理；没有复现旧Item stale/半包/补偿异常/repair-required/共享停写标记 |
| 原06完整专项 | 本轮三端体验和身份有效；争拾/背包满、掉落到期、无人job收尾、多包完整性及受控故障恢复等未独立证明，原票不整票关闭 |
| 原07超距请求 | 保留20米保护与既有自动化；受控超距实机测试延期为独立安全专项，不阻塞本轮体验验收，不记为已通过 |

日志并非零错误：三端共59条Resource身份观测NRE，现有诊断Prefix局部处理；Host有22条Zombie demand-mismatch保护隔离及2条release-cancel。它们未呈现本轮Item故障升级或玩家异常体验，保留独立线索，不据此自动开修复。G2入服前6次workshop超时，世界1结束附近有5010/CUSTOM断开，随后世界2恢复连接；不将关联顺序写成已确认根因。

完整核证及逐行依据：`.scratch/matt-orchestrate/item-runtime-acceptance-20261029/expert/findings.md`、`line-evidence.jsonl`、`scan-summary.json`；三包库存/身份见同run `evidence-index.json`。本次不新增构建、测试或游戏操作。

本轮结论适用于当前候选的P2P listen-host目标体验；纯SP、U3DS和原06/07独立专项没有被代验。没有发布或覆盖稳定版授权。

## 记录审查

本轮仅验收文档，原源码三轮审查与635结果不重跑、不改判。第1轮Standards/Spec均NON-CLEAN：R04整票Completed与原清单未闭合混淆。已将状态限定为本轮体验完成、保留未闭合清单，并勾选有证据支持的基础项；没有因此要求玩家补测。

修订后第2轮全新独立Standards/Spec均CLEAN。Standards native `01a18364-986a-7762-9c16-5d9369057b24`；Spec native `01a18364-d1e6-7d93-9137-e860303462e9`；父活动与各自session_meta/turn_context核为`gpt-6-luna/high`。两轮原报告及来源分别保存在本次run `review-round-1-finals.json`、`review-round-2-native-finals.json`。审后只追加本段实际审查链，无代码、DLL或验收结论变化。
