# C-P2-Item-05 实施规格

## Problem / Solution

完整候选必须成为实际生产入口，避免新旧周期/资格双写。仅整合已完成路径，在会话边界切换并证明旧驱动退出。

## Fixed inputs

用户2026-10-08批准；依赖04及前序01–03实际产物/记录。研究插件 `57e3c4e4411b4f8831f0bf6d489b9fea9922c47c`，SDK `ea7b4973af5ba10f62baad2bfde36ab2e5b060eb`。读 [共同输入](../item-migration-slice/execution-inputs.md)、[主规格](../item-migration-slice/spec.md) 切换政策及ADR0019–0027。

## Implementation decisions

- 会话边界选择唯一插件侧Item Authority Writer；同一会话不分玩家切换，不双写，不在失败时回退旧Writer。
- 核对生产注册/项目编译/原生入口，退出旧M1/M2资格投影与P2P全图周期驱动；不能只删类型或声明新端口。
- 保留原生算法/RPC、对SP/Guest/U3DS passthrough及必要外部兼容边界；零仓内调用不自动授权删除公共/反射能力。
- 准备结果正确跨实际地图输入就绪与Core会话绑定衔接；断线/重连更换连接身份，换图/新会话隔离旧作业、计时、来源和失败资格。
- 保护Host本地音画/相机/拾取、既有审批及共享Resource/Collision路径，不全局改dedicated、不引入LMN依赖。
- 若07已启用，其距离门在生命周期Writer切换后保留；就绪与距离分别约束，原生拾取事务只执行一次。07未完成不阻塞本票。
- 本票只整合/退役已验证行为；发现前四票必要正确性漏项，记录并回到责任票修复验证，不能用05验收替代前置证据。

## Testing decisions

- 真实生产控制入口从注册到准备/需求/结算/恢复闭环验证，不以候选类单独单测证明正式接入。
- Red覆盖会话切换、迟到回调、旧连接恢复、重复准备、旧周期误驱动及失败双写；Green核对动作来源与唯一Writer。
- 核对真实StaticIL/注册与旧调用链退出；运行已有相关回归，包含Resource/Collision、SP/Guest/U3DS路径及Host本地形态边界。
- 建立完整候选构建/可复现指纹/审查记录，部署指引按现行流程交给用户执行；没有实际三端日志仅记implemented-pending-runtime。

## Out of scope / Deliverable

不补前四票漏项、不迁其他域、不新增20米逻辑、不覆盖稳定版或直接发布。交付正式单Writer候选及可追溯变更/验证/身份记录，供06进行人工三端验收。
