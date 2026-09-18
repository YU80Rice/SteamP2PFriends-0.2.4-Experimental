# 0014: 租约型 Collision Override 与 Acquisition Receipt

确认的 Collision Release 不是对 Acquire 做机械反向调用，也不是区域归零后的整区 disable。Acquire 必须产生绑定身份轴的 Acquisition Receipt，记录实际取得的插件侧 Override。Release 只撤销当前有效 receipt 仍持有的状态维度，并把最终状态交还 Native State Authority。Host 仍贡献需求时编排层不得发出最终 Release；Execution Port 不重新扫描观察者。Collision 操作集合不含 ResourceSpawnpoint。无法证明所有权时采用非破坏性 fail-safe，而不是永远只 enable。

## Considered Options

- Collision Demand 归零就关闭区域内全部对象：否决。把插件需求当成全局唯一需求。
- 永远只 enable、确认的 N→0 也不撤销：否决。可作为不确定状态的临时安全策略，不能作为生命周期终态。
- 成功 Acquire 过 RegionKey 即有权 disable：否决。vanilla 与游戏事件可能已改写状态；Acquire 调用过 enable 不等于以后有权 disable。

## Consequences

- 执行层必须按实际变更粒度记录所有权，即使公开 Capability Lease 尚未拆分。
- 聚合 native API 在无法证明不会覆盖非本域状态时，不得作为 destructive Release。
- 「只 enable 不 disable」保留为身份不确定时的 fail-safe，不是正常 Release。
