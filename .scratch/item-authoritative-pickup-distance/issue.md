# C-P2-Item-07 — 恢复 U3DS 原生20米拾取距离校验

- **Label**: `ready-for-human`
- **Type**: Specification
- **Status**: `implemented-pending-runtime`
- **Version**: `0.2.4.10-Experimental`
- **Blocked by**: 无空间迁移票前置依赖；沿用现有有效P2P会话与原生Item仓库

## What to build

在SPF P2P listen-host的原生Item拾取权威入口恢复U3DS已有的20米距离校验；超出范围的请求在背包写入前被拒绝，地面物资不因拒绝而删除。正常操作距离与其他原生条件保持。

## Scope and acceptance

详见 [独立实施规格](spec.md) 与 [共同输入](../item-migration-slice/execution-inputs.md)。依据 [0028: U3DS 原生拾取距离校验作为独立 Item 权威交互任务](../../docs/adr/0028-item-pickup-distance-native-u3ds-separate-task.md)，与 [Item生命周期及同步迁移](../item-migration-slice/issue.md) 分别验收；租约存在不能绕过校验。

- [x] 权威距离平方大于400时拒绝，背包/地面无写入或删除；平方400不被本项拒绝。源码证据：IT95、IT96。
- [x] Host/Guest请求均绑定实际调用玩家及权威物品位置，不能使用客户端坐标绕过。源码证据：IT99、IT101 与 StaticIL `GetPlayer`/`instanceID`/`point`。
- [x] 原生身份/存活/回调/容量条件及成功后删除事务保持，拾取事务只执行一次。源码证据：IT96、IT100；前缀不调用 `tryAddItem`/`RemoveAt`。
- [x] 独立生产请求入口 Red→Green 与注册/绑定已记录。源码证据：IT95–IT101、`Test_PickupDistanceConsumesReceiveTakeItemRequest`。
- [ ] Runtime：正常拾取与受控超距必须到达真实权威入口。未采证，保持 OPEN。普通客户端选不中远物品不算通过。

用户2026-10-08批准第07张独立票，无空间迁移前置边，只增加原生20米拒绝，不改变拾取事务。当前会话只发布规划；下一构建会话按本票执行。非构建规划不安排双轴审查，发布不代表TDD或Runtime通过。

## 2026-10-29 验收安排

本轮R04的1H+2G正常玩法已获用户通过。20米是服务端请求校验上限，不是普通客户端交互范围；不能要求玩家站远按键来验证未发出的请求。保留本票保护代码、已有自动化和上方未勾选的真实超距请求要求。主调度将该实机项延期为独立安全专项，不作为本轮R04体验验收的阻塞；不记为已测PASS、不更改距离或原规格。无需玩家现在补测，后续确需证明真实超距请求拒绝时再准备受控工具。
