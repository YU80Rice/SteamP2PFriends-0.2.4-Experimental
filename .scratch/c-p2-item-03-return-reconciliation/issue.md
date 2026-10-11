# C-P2-Item-03 — 返区结算后呈现

- **Label**: `ready-for-human`
- **Type**: Specification
- **Status**: READY FOR HUMAN — dual-axis CLEAN；Runtime pending
- **Version**: `0.2.4.10-Experimental`
- **Blocked by**: [02 按需求运行与掉落到期](../c-p2-item-02-demand-and-expiry/issue.md)

## What to build

返区由首个有效需求启动一次区域共用结算；分帧完成前所有进入者等待，不看见或拾取半成品，也不收到旧清单。

## Acceptance criteria

- [x] 先排除全部过期自然项，再按原生资格补当前缺量；存活旧年龄保持，新物从实际生成计龄。
- [x] 后来者等待同次作业，完成后自动交付；正常分帧不触发失败预算。
- [x] 最后需求离开仍完成本轮，不回滚/重做，无等待者不发清单，完成立即休眠。
- [x] 实际掉落独立运行，所有区域共享有限帧预算，完成门及原生事务Red→Green成立。

见 [本票规格](spec.md)、[共同输入](../item-migration-slice/execution-inputs.md)。结算与呈现保持一张垂直票；不实现异常重试或旧Writer退役。
