# 0018: 无人进度按原生规则补算，不复刻帧调度偏差

用户在 2026-10-07 的 Q4 选择“A，按原生规则”：按原生配置速率、状态条件和关键事件顺序结算无人进度，允许与逐帧检查产生计时差异。来源见 [原生职责复用与按需运行的边界是什么](../../.scratch/map-c-stage-world-state-migration/ticket-runtime-duty-boundary/ticket.md) 的 Q4。本条细化 [0015: 无人区域进度补算与掉落物到期清理](0015-dormant-progress-reconciliation-and-item-expiry.md)，不改写既有决策。

原生发电机每次 Update 最多扣一单位燃油，跨过多个周期也不一次补扣全部；减少调用或运行卡顿会影响实际消耗。选择规则与速率作为补算依据，避免为了重现这类帧调度结果而维持逐帧执行；因此休眠补算不承诺与某次卡顿运行的实际耗油完全相等。事实见 [无人进度补算的原生规则补查](../../.scratch/map-c-stage-world-state-migration/ticket-runtime-duty-boundary/native-progress-facts.md) 的第 1 条。

天气资格、供电条件与关键事件顺序仍须遵循原生规则；本决策不允许将所有进度简单按总时长线性计算。时钟、计时相位、舍入/阈值、调度延迟及各领域验收容差仍需明确；没有批准改变配置速率，也没有冻结实施接口或非时间型对象政策。
