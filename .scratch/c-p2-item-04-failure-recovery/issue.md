# C-P2-Item-04 — 局部失败的有限恢复

- **Label**: `ready-for-agent`
- **Type**: Specification
- **Status**: Open — 未实现
- **Version**: `0.2.4.10-Experimental`
- **Blocked by**: [03 返区结算后呈现](../c-p2-item-03-return-reconciliation/issue.md)

## What to build

只对实际异常进行有限自动恢复；耗尽后提示受影响玩家，允许验证身份的手动恢复。可能半包时沿用原生离区返区。

## Acceptance criteria

- [ ] 首次尝试加最多3次重试，间隔0.5/1/2秒，整链最多10秒；重复回调不重置预算。
- [ ] 正常准备/结算/模型加载不触发失败提示；恢复不重做已提交删除或补货。
- [ ] `/spfitemretry` 主机验证资格，同观察者/区域间隔至少10秒，旧身份无效。
- [ ] 可能半包不盲目重发，局部故障隔离；Resource/Collision默认政策不回归。

见 [本票规格](spec.md)、[共同输入](../item-migration-slice/execution-inputs.md)。不提前正式切换或退役旧Writer。
