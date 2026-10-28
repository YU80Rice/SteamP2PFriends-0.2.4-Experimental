# 0016: 休眠期间允许必要的关键事件处理

用户在 2026-10-07 的 Q2 接受：不持续逐帧运行无人区域组件，但允许在天气变化、燃油耗尽等必要时刻进行少量判定或记录，其余进度返区补算。选择来源是 [原生职责复用与按需运行的边界是什么](../../.scratch/map-c-stage-world-state-migration/ticket-runtime-duty-boundary/ticket.md) 的 Q2 提问及用户“接受”。本条细化 [0015: 无人区域进度补算与掉落物到期清理](0015-dormant-progress-reconciliation-and-item-expiry.md)，保留其历史记录。

## 取舍

仅凭离开时长或返回时现状，不能确定此前的合格接雨事件，也不能确定发电机耗尽前后设备的实际供电时段。保留必要的关键事件依据用于维持原生玩法结果，采用事件判定/记录与进度补算作为方向，避免区域每帧组件保活。事实依据见 [无人进度补算的原生规则补查](../../.scratch/map-c-stage-world-state-migration/ticket-runtime-duty-boundary/native-progress-facts.md)。

## 未冻结的内容

具体事件集合、遮挡与供电历史表示、判定方法、调度机制/预算、时间精度、存档前结算及不可时间补算对象仍需问询。本决策没有批准通用补算引擎、全域模型/碰撞常驻或持续逐帧仿真，不解除 Item 的到期清理要求。
