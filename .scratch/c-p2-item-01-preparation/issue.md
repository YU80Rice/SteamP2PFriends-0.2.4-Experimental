# C-P2-Item-01 — 全图准备与首次到访

- **Label**: `ready-for-agent`
- **Type**: Specification
- **Status**: `implemented-pending-runtime`
- **Version**: `0.2.4.10-Experimental`
- **Blocked by**: 无

## What to build

地图输入就绪后准备全图物品数据；首次有效需求只启动该区域自然计时，各观察者沿原生路径获得同一权威仓库的清单。

## Acceptance criteria

- [x] 全图含合法空区只准备一次；对应Item联机操作不消费未准备数据。
- [x] 首次启计不改实际掉落年龄，第二观察者不重生成/重启计时。
- [x] 准备中的来源识别、嵌套实际掉落保留及真实动作提交均正确。
- [x] 真实共享入口和原生呈现/基线绑定成立，完成本票Red→Green及相应回归。

固定输入及范围见 [本票规格](spec.md)、[共同执行输入](../item-migration-slice/execution-inputs.md)。不做返区结算；后继02不能补救本票必要正确性。
