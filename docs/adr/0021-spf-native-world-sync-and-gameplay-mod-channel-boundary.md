# 0021: SPF 原生世界同步迁移不自动成为 LMN 消费方

用户在 2026-10-07 指出“为什么要接入LMN……这不是联机插件SPF吗？你是不是搞混了”。本轮纠正将上层功能性联机模组的命名频道规则自动套到 SPF 原生世界同步的推断：C-P2 Item 继续复用原生仓库、游戏协议和本地应用链，迁移的是观察者需求、执行资格与周期政策，不以接入 LMN 或重写原生复制为前提。

## 边界与依据

- 功能性联机模组通过 LMN 同步自身新增功能状态的规则继续有效；SPF 对原生世界联机基础设施的修复/迁移不因此自动增加 LMN 依赖。
- LMN README 将其定位为插件共享消息前置库，使用原生 ITransportConnection 且不接管原版协议包；NAMED_CHANNEL_STANDARD 将其定位为第三方插件的接入标准。当前 SPF 原生 Item 已有基线、增量和拾取事务，本仓也没有实际 LMN 依赖。
- 本次澄清不表示 SPF 永远禁止消费 LMN；若后续确需新增自定义消息，单独论证职责、顺序和成本，不能以一句功能模组规则替代必要性分析。

来源：`D:/Agent-工作目录/DevelopMyUNMultiplayerModAndModloader/LaunchMultiplayerNet/README.md:3`、`NAMED_CHANNEL_STANDARD.md:3–5`；本仓 `SteamP2PFriends.csproj:38–100`；[Item 基线、增量与恢复事实补查](../../.scratch/map-c-stage-world-state-migration/ticket-item-sync-preflight/findings-item-baseline.md)。

## 对当前规划的影响

撤回 `GAP-C-P2-LMN-CONTRACT` 作为 Item P2 阻塞项，LMN SDK 调查保留为背景，不安排其依赖接线、分片协议、通用路由器或应用 ACK 重建。已有跨图通信 HITL 票保持打开，不借此次澄清替其他功能模组裁定方案。

原生基线拒绝后恢复、返区先排除过期物资、旧连接状态隔离和原生增量衔接仍需规格化/验证；原生调用返回的证据限制也仍有效。Q8–Q10 推荐尚未获用户裁定，不将基于传输重写假设提出的新等待界面或整区拾取门控视为已批准需求。ADR 0019/0020 的 Item 周期政策不变。
