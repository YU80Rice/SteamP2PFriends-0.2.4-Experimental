# C-P1 讨论与第三方审计背景接收记录

- 日期：2026-10-07；插件核对基线 HEAD=`57e3c4e4411b4f8831f0bf6d489b9fea9922c47c`。
- 状态：规划记录，独立双轴复核进度见 [背景接收审查链](review-scope-intake.md)；本记录不是冻结规格、完整复审或生产授权。
- 用户在 Q4 后补充 C-P1 讨论及 [第三方架构评审](../../ARCHITECTURE-REVIEW-0.2.4-Experimental.md)，用于恢复 C 阶段整体目标。

## 背景与当前事实分开

原评审日期为 2026-08-26、基线为 `master @ 2e6de00`；其开头另有 2026-09-11 里程碑注记。[2026-09-04 逐条复核](../../audit/2026-09-04/ReviewRecheck-0.2.4.8-0933.md) 也有自己的历史基线，不能把其中“当前”直接解释为本次 checkout。

用户本次提供的 P1 讨论强调三类职责分别处理：空间生命周期/进区基线、主机权威循环/周期复制、本地玩家表现与单机旁路。这作为规划输入，不把旧表里的完成状态直接搬成当前事实。

本次静态核对发现：玩家广播功能补丁存在，见 [PlayerManagerBroadcastPatch](../../Core/Patches/PlayerManagerBroadcastPatch.cs)；载具复制功能补丁存在，见 [VehicleManagerP0C1ReplicationPatch](../../Core/Patches/VehicleManagerP0C1ReplicationPatch.cs)。[注册模块](../../Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationLegacyDiagnostics.cs) 第 274/476 行分别调用其 RegisterManual。因此旧讨论“玩家/载具基本未修”不能作为当前缺失结论；仍须核对生产调用链、最终产物与运行验收，不能以文件/注册入口证明体验已验收。

## 规划职责矩阵（候选，待收束）

| 工作线 | 当前规划依据 | 下一步 |
|---|---|---|
| Resource / Collision | 用户已说明迁移及验收完成；本轮不重新授予 Runtime 身份 | 后续切片保留已有行为与单 Writer 契约 |
| Item 空间迁移 | 首轮研究确认 SPI 回调尚未驱动实际 Item 生命周期 | 明确首次生成、无人刷新、释放、基线/增量衔接，再形成 P2 规格 |
| Zombie 空间迁移 | 原生 Bound 与二维 Region 不同；旧生命周期补丁与共享驱动不同 | 保留后续候选，独立核对形状、需求及旧 Writer 退役 |
| Building / Object 状态 | 进区复制与 Collision Override 不是同一职责 | 单独细化状态基线与交互，不因碰撞完成而认定迁移完成 |
| 主机权威循环 / 周期复制 | 开闸与空间租约独立；玩家/载具已有补丁入口 | 区分已有实现待验收、真正缺失及需改造，避免重复实施 |
| 时间型进度 | Q1–Q4 已接受补算、关键事件、存档结算及原生规则精度方向 | 按对象确认资格/依赖/精度；不默认整个补算引擎属于 Item 首切片 |
| 本地形态 / 所有权旁路 | 既有顶层地图禁止全局伪造 dedicated；用户新增锁/音画/驾驶背景 | 保留本地形态回归约束；所有权旁路另查，不按空间域自动修复 |
| Animal / Vehicle / 性能 | 首轮研究显示 Pack、移动身份与 tick/物理职责各异 | 独立政策和成本评估；不从现有 SPI 类型推导已迁移或必须租约化 |

Q1–Q4 的方向继续有效；供电消费者、天气资格、运动/AI 和跨区效果的可补算性仍需逐域证明。全局权威数据、轻量事件/时钟、实体模拟、碰撞表现、网络接收人不能合并成一个“全图保活”布尔值。

## 后续收束顺序

先把 C 阶段剩余职责分成空间迁移、权威/复制、形态/权限三条清单，每项绑定当前实现与证据状态；再围绕 Item 完成问询、to-spec、to-tickets 和独立交接。Item 之后的领域顺序仍未冻结。已有 [Ticket 4: 命名通信通道 (LaunchMultiplayerNet) 与双端路由规范](../map-top-level-architecture-blueprint/ticket-04-channel-transport-routing/issue.md) 保持打开，但按用户后续澄清，不作为本 P2 强制 LMN 接入阻塞；见 [0021: SPF 原生世界同步迁移不自动成为 LMN 消费方](../../docs/adr/0021-spf-native-world-sync-and-gameplay-mod-channel-boundary.md)。

## 核验与限制

本次仅源码/文档读取与规划记录，没有生产源码、测试、构建、部署、发布或 Runtime 验收。独立审查代理此前报告 usage limit / agent thread limit reached；Q6 复核成功后，本增量另以两个全新上下文复核，结果见审查链，不沿用 Q6 的角色实例。
