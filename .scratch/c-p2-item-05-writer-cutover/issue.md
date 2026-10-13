# C-P2-Item-05 — 会话切换与旧调用链退役

- **Label**: `ready-for-human`
- **Type**: Specification
- **Status**: `implemented-pending-runtime`
- **Version**: `0.2.4.10-Experimental`
- **Blocked by**: [04 局部失败的有限恢复](../c-p2-item-04-failure-recovery/issue.md)

## What to build

把前四票已完成路径在会话边界接入正式Item运行，退出旧插件资格/全图周期驱动，验证唯一Writer及会话切换。

## Acceptance criteria

- [x] 完整新路径真实注册消费，旧M1/M2资格和全图周期不再驱动同一会话。源码证据：IT88–IT90、IT92、IT94 与 StaticIL `Test_SessionBoundaryRetiresLegacyItemWriter`。
- [x] 断线/重连/换图不继承旧身份、工作或恢复资格，同轮准备不重复。源码证据：IT91、IT93。
- [x] SP/Guest/U3DS、Host本地形态、Resource/Collision无回归。源码证据：WhitelistTests 536/536，含既有 Resource/Collision 与 IT92/IT94 形态边界。
- [x] 前四票必要条件均已有证据；不在本票补漏或以切换包装未完成逻辑。01–04 交付仍为依赖证据，本票未改其恢复/结算语义。

以上勾选只覆盖源码与 WhitelistTests。Runtime 不在本票关闭，归 [06](../c-p2-item-06-runtime-acceptance/issue.md) 人工采证。

见 [本票规格](spec.md)、[共同输入](../item-migration-slice/execution-inputs.md)。尚无三端证据时不得当作06已通过。
