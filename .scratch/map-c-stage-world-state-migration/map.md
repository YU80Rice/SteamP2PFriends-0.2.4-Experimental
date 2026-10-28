# Wayfinder Map: C 阶段世界状态原生对照与逐域迁移路线

- **Label**: `wayfinder:map`
- **Owner**: YU80Rice
- **Status**: Active (Wayfinding)
- **Created**: 2026-10-07

## Destination

对照原生 U3DS 的世界模拟、空间激活、状态复制和持久化，明确哪些原生职责继续复用、哪些运行职责需要由 multi-observer 驱动，再形成逐域迁移规格与实施票。Item 是 Resource、Collision 之后的迁移起点。

## Notes

- 用户在本对话完成决策、问询、规格与拆票；另一位 agent 按自包含工单实现。
- 使用 ask-matt、research、grilling、domain-modeling；事实明确后收束为 to-spec → to-tickets → handoff。
- 源码对照基线：插件 `57e3c4e4411b4f8831f0bf6d489b9fea9922c47c`；U3-SDK `ea7b4973af5ba10f62baad2bfde36ab2e5b060eb`。
- 不预设 U3DS 的每个领域都全图持续激活；分别调查状态存储、计时、模拟、渲染/碰撞和网络相关性。
- 事实、迁移建议、待用户裁定项和 Runtime 未验证项分别列示。
- 本图只规划和研究，不修改生产源码、测试、DLL、稳定区或部署目录。
- 文档草稿在双轴独立审查前不作为正式交接规格；源码调查不能替代实机等价性验收。
- 跨图已有通信职责讨论票：[Ticket 4: 命名通信通道 (LaunchMultiplayerNet) 与双端路由规范](../map-top-level-architecture-blueprint/ticket-04-channel-transport-routing/issue.md)；保留该票，不建立重复决议，也不作为本 P2 的强制 LMN 接入阻塞。
- 用户已裁定无人进度补算默认方向与掉落物到期清理，见 [0015: 无人区域进度补算与掉落物到期清理](../../docs/adr/0015-dormant-progress-reconciliation-and-item-expiry.md)；持续交互、事件历史等后续边界仍在问询，不作为全域冻结规格。
- 用户允许必要的休眠关键事件判定/记录，见 [0016: 休眠期间允许必要的关键事件处理](../../docs/adr/0016-key-event-processing-during-dormancy.md)；具体事件集合与实现方式仍未冻结。
- 用户同意世界存档前结算原生可保存对象的无人进度，见 [0017: 世界存档前结算无人区域进度](../../docs/adr/0017-reconcile-dormant-progress-before-world-save.md)；不新增地面物品持久化或关房离线模拟。
- 用户确定按原生配置速率、状态条件和关键事件顺序补算，见 [0018: 无人进度按原生规则补算，不复刻帧调度偏差](../../docs/adr/0018-native-rule-based-reconciliation-accuracy.md)；Item 周期方向随后由 Q6 细化。
- 用户补充的 C-P1 讨论及原审计背景见 [C-P1 讨论与第三方审计背景接收记录](scope-intake-20261007.md)：分别规划空间迁移、权威/复制及本地形态/权限；旧表完成状态须对照当前源码与证据，不直接继承。独立文档复核已完成，不将该候选矩阵升级为冻结实施规格。
- Q4、背景接收、Q5/Q6 文档均已双轴 CLEAN，原代理配额/线程限制导致的审查缺口已关闭；记录分别见 [Q4 审查链](ticket-runtime-duty-boundary/review-q4.md)、[背景接收审查链](review-scope-intake.md) 和 [Q6 审查链](ticket-runtime-duty-boundary/review-q6.md)。`GAP-C-P2-IMPLEMENTATION-TDD` 继续保留。
- 用户在 Q6 接受全图准备自然数据、首次需求启动周期、无人停止实时周期而返区补算，见 [0019: Item 全图准备数据，首次需求启动自然周期，无人时返区补算](../../docs/adr/0019-item-preparation-and-demand-started-natural-cycle.md)；实际掉落物独立到期清理，随机补算语义由 Q7 细化，模型/渲染政策尚未冻结。文档复核状态见 [Q6 审查记录](ticket-runtime-duty-boundary/review-q6.md)。
- 用户在 Q7 选择自然物品返区结算当前结果、省略无人中间随机批次，见 [0020: 自然物品返区结算当前结果，省略无人期间中间刷新批次](../../docs/adr/0020-natural-item-return-result-reconciliation.md)；补货仍受原生资格与点位占用约束，未承诺与持续全图巡检历史完全相同或 Runtime 无重复。复核状态见 [Q7 审查链](ticket-runtime-duty-boundary/review-q7.md)。
- 用户指出 SPF 与上层功能模组的边界后，已撤回 Item P2 必须接入 LMN 的推断及消费契约阻塞，见 [0021: SPF 原生世界同步迁移不自动成为 LMN 消费方](../../docs/adr/0021-spf-native-world-sync-and-gameplay-mod-channel-boundary.md)；周期政策保留，原生仓库/协议/交互继续复用。纠正复核见 [SPF 与功能模组通信范围纠正审查链](review-spf-network-boundary.md)。
- 用户在 Q8/Q9 接受局部等待/有限恢复兜底，并要求正常体验沿用原生自动同步与呈现，见 [0022: Item 正常体验沿用原生，等待与重试作为局部兜底](../../docs/adr/0022-native-item-experience-with-local-failure-recovery.md)；Q10 原生准备流程已补查，SPF 开房等待策略尚未另行冻结。复核状态见 [Q8/Q9 审查链](ticket-runtime-duty-boundary/review-q8-q9.md)。
- 用户随后确认 Q10：先完成全图 Item 数据准备，再开放相应联机运行，见 [0023: 全图 Item 数据准备完成后开放相应联机运行](../../docs/adr/0023-item-preparation-before-world-availability.md)。上述旧未决语句保留当时顺序；本次补查确认房间发布/Host 活动不能代替数据就绪，模型仍按附近原生需求呈现。
- Item 决策开始通过 to-spec / to-tickets 收束，见 [Item Migration Slice 规格草稿](../item-migration-slice/issue.md)；只收束已明确的 Item 首切片，不关闭共同职责边界票或整张图。草稿尚待测试入口、工程参数及票图审阅，复核记录见 [本轮文档审查链](../item-migration-slice/review-chain.md)。
- 用户已选择异常恢复沿用原生提示/手动重试、必要离区返区，见 [0024: Item 异常恢复沿用原生提示与离区返区](../../docs/adr/0024-item-failure-recovery-uses-native-reentry.md)；当前不增加原地reset语义，正常进区不增操作。
- 2026-10-08用户要求本阶段除构建外无需双轴审查，额度用于核心步骤和决策；当前回到Item问询，六票保持草稿。Q11到期分类选择对齐U3DS，见 [0025: Item 到期分类对齐 U3DS](../../docs/adr/0025-item-expiry-classification-aligns-with-u3ds.md)；新沟通统一使用listen-host。
- Q12用户选择按区域共用一次返区权威结算，首个有效需求启动，后来者等同次结果；大量物资分帧推进，完成后才呈现/允许拾取/发清单，正常等待不属失败，见 [0026: Item 返区结算按区域共享并分帧完成](../../docs/adr/0026-item-return-reconciliation-shared-and-incremental.md)。
- Q13用户选择已启动结算在需求消失后继续分帧收尾，不撤销或重做已有结果，无等待者不发清单，完成即休眠；下次有效需求从完成后的状态重判，见 [0027: Item 已启动结算在需求消失后继续收尾](../../docs/adr/0027-item-reconciliation-finishes-after-demand-exits.md)。
- Q14用户批准本次恢复U3DS原生20米拾取距离校验，单列 [恢复 U3DS 原生20米拾取距离校验](../item-authoritative-pickup-distance/issue.md)，不改变正常操作距离、不由租约授权，与生命周期/同步迁移分别验收；见 [0028: U3DS 原生拾取距离校验作为独立 Item 权威交互任务](../../docs/adr/0028-item-pickup-distance-native-u3ds-separate-task.md)。

## Decisions so far

- [本阶段先做原生对照，再从 Item 开始逐域迁移](ticket-destination/ticket.md) — 用户已明确研究先行与迁移起点。
- [原生 U3DS 世界状态、激活与复制职责事实调查](ticket-u3ds-world-state-facts/ticket.md) — 首轮 manager/实体/代表组件事实地图及双轴复核完成；全局仓库、计时与空间运行须分开裁定，未查项仍保留。
- [Item 进区同步与 LMN 接入事实补查](ticket-item-sync-preflight/ticket.md) — 原生基线恢复/证据边界事实继续有效；LMN 调查留作背景，后续已纠正强制消费方的范围推断。

## Not yet specified

- 共同职责边界确定后，各领域的细化行为与独立验收矩阵。
- 跨域依赖及 Item 之后的迁移顺序。
- 全局状态、玩家状态及特殊事件的验收边界。
- 生产接线、旧 Writer 退役与原生行为等价性的具体实施切片。

## Out of scope

- 本规划窗口的生产实现、构建、部署、实机测试、发布和稳定版本覆盖。
- 未经用户决策擅自新增原生没有的持久化或玩法行为。
