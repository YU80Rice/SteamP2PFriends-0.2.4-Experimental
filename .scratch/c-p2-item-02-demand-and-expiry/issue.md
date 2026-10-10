# C-P2-Item-02 — 按需求运行与掉落到期

- **Label**: `ready-for-agent`
- **Type**: Specification
- **Status**: `implemented-pending-runtime`
- **Version**: `0.2.4.10-Experimental`
- **Blocked by**: [01 全图准备与首次到访](../c-p2-item-01-preparation/issue.md)

## What to build

区域有有效需求时运行原生自然周期，最后需求离开后停止实时自然周期；实际掉落在无人及未访问区域仍独立到期。

## Acceptance criteria

- [x] 两观察者同区，一人离开不停止另一人的需求；零需求即停止实时自然周期。
- [x] 2秒释放滞回不重置自然年龄、不冒充有效需求。
- [x] 已核实实际掉落按U3DS掉落期限到期，后续及嵌套来源正确。
- [x] 同一原生仓库保持原生规则，真实生产入口Red→Green与回归通过。

固定输入与演示条件见 [本票规格](spec.md)、[共同执行输入](../item-migration-slice/execution-inputs.md)。不做返区结算或异常恢复。
