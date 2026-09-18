# 0013: Collision 正式切换准入门与影子证据边界

Collision 正式切换必须满足完整 Control-Plane Readiness Gate：坏观察者样本不得冻结其它需求，也不得被当成离开而 Release；故障隔离到领域、区域和转换；Session 身份可恢复或显式熔断；retry 按事务粒度，禁止区域单槽；Execution Port 在提交边界拒绝陈旧身份。准入未闭合时允许无副作用影子运行，但影子不得写原生状态，也不得当作正式切换证据。Resource M6P31 不是本门已过；被容忍的单槽互吞仍阻塞正式切换。

## Considered Options

- 只把 Resource 接缝债当阻塞、Coordinator 债带到 Collision Runtime：否决。同一 capture/Tick 会把故障传到第二域。
- 用影子跑通替代正式准入：否决。影子 DLL 不得证明切换 PASS。
- 把坏记录直接跳过并当零需求：否决。会把整批 fail-closed 修成误 Release。

## Consequences

- 阻塞债毕业为 `/to-spec` 前置约束，不开「重写 coordinator」地图。
- `EndSession` 不再是共享 Update catch 的默认反应。
- 正式候选与影子候选必须有独立指纹。
