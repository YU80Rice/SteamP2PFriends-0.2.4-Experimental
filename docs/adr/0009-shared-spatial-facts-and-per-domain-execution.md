# 0009: 共享空间事实与分域执行

Collision Migration Slice 不把远区碰撞收成单一 Collision Writer，也不维持 `RemoteCoverage` 交叉读取。观察者位置与进入/退出差异由 Control Plane 统一提供；Resource 与 Collision 各自投影 typed Demand / Region Lease，并只执行本域插件侧能力。`RegionKey` 只是区域身份，不授予执行资格。因此 Collision 必须退出对 `ResourceSpawnpoint` 的写入，Resource 必须停止读取 Collision 私有覆盖集。

## Considered Options

- Collision 统一拥有所有远区碰撞：否决。会拆开已完成的 Resource Authority Writer，并继续与 Resource 争夺同一原生状态。
- 保持 `RemoteCoverage` 交叉依赖：否决。第二套 Demand Authority 与第二个资源 Writer 会留下，私有轮询无法退役。
- RegionKey 活跃即所有相关领域自动激活：否决。会把区域身份、观察者存在和领域能力压成全局布尔值。

## Consequences

- 后续域（Item / Zombie）必须复用「共享空间事实 + 本域 Demand Policy」，不得再扫描 `Provider.clients`。
- 迁移期只允许只读影子比较，禁止双写和静默回退旧 Writer。
- Collision 域内部是否再拆门动画与静态 Collider，不由本 ADR 决定。
