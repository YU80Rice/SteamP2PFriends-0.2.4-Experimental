# Item 进区同步与 LMN 接入事实补查

- **Label**: `wayfinder:research`
- **Type**: AFK
- **Parent**: `map-c-stage-world-state-migration`
- **Owner**: YU80Rice
- **Assignee**: YU80Rice (Codex 主持只读补查)
- **Status**: Closed (源码事实复核完成；后续已纠正将 LMN 接入作为 P2 前提的范围推断)
- **Blocked by**: [原生 U3DS 世界状态、激活与复制职责事实调查](../ticket-u3ds-world-state-facts/ticket.md) 的首轮事实复核（已满足）

## Question

在 Item 周期政策已接受后，当前进区基线/增量、拾取事务、失败恢复与 LMN 命名频道接入有哪些可复用事实、缺口和用户可见取舍？

## Scope

- 核对当前 Item 生成/复制 ledger、native askItems/ReceiveItems/ReceiveItem/Destroy/Take 请求与实际注册链；入队承诺不等同客机应用确认。
- 只读核对 LaunchMultiplayerNet 的命名频道 API、可靠性、身份/分片/大小、线程与双端生命周期能力及本仓依赖/接入现状。
- 功能模组的 LMN 规则继续有效；SPF 原生世界同步迁移不据此自动成为 LMN 消费方，见本票后续范围纠正。
- 跨图通信决策复用 [Ticket 4: 命名通信通道 (LaunchMultiplayerNet) 与双端路由规范](../../map-top-level-architecture-blueprint/ticket-04-channel-transport-routing/issue.md)；本票只提供事实输入，不关闭或复制该 HITL 决议。
- 不实现通用路由器、Item 或其他域，不构建、不部署，不产生 Runtime PASS。

## Outputs

[Item 规格前同步与通信事实索引](findings.md)，链接两条调查事实和尚需用户裁定的行为入口；独立复核状态见 [审查链](review-chain.md)，完成 CLEAN 前为草稿。

## Comments

### Claim — 2026-10-07

用户以 ask-matt 询问下一步。已先领取本补查票，当前承接收束问询 → to-spec → to-tickets → handoff；构建继续交给另一位 agent。本票不预设已可发布 ready-for-agent 规格。

### Research — 2026-10-07

两条只读补查已返回并落为 Findings：Item 同步的重试/发送完成/接收顺序边界，以及 LMN 工作树的命名 API/代次/ACK/生命周期边界。尚待独立双轴复核，用户可见行为与跨图通信职责仍待 live exchange，不以事实调查替代。

### Resolution — 2026-10-07

两条事实线经两个全新独立审查上下文返回 Standards CLEAN / Spec CLEAN，本研究票关闭。资料见 [事实索引](findings.md) 与 [审查链](review-chain.md)。已确认规格前仍需问询等待、持续失败与开局准备体验；现有 M2 不证明入队/应用，原生 RPC 与新增 LMN 顺序/代次/完成契约需要明确。

本次关闭不裁定跨图通信 HITL，不冻结 LMN 脏树 SDK 消费契约；`GAP-C-P2-LMN-CONTRACT` 与实施 TDD 缺口保留。没有生产实现、构建、部署或 Runtime PASS。

### Scope correction — 2026-10-07

用户原文：“为什么要接入LMN……这不是联机插件SPF吗？你是不是搞混了”。已确认此前把功能模组规则自动用于 SPF 原生世界同步，是主 agent 的范围推断错误；撤回 Item P2 必须接入 LMN 及 `GAP-C-P2-LMN-CONTRACT` 阻塞项。SDK 调查留作背景，原生基线拒绝/恢复和完成证据的事实继续有效。

当前 P2 复用原生协议、仓库和交互逻辑，实施目标仍为 Item 共享控制面迁移及已接受周期政策；Q8–Q10 未获裁定，不因此新增等待界面/整区交互门控。依据见 [0021: SPF 原生世界同步迁移不自动成为 LMN 消费方](../../../docs/adr/0021-spf-native-world-sync-and-gameplay-mod-channel-boundary.md)，纠正复核链另记 R2。
