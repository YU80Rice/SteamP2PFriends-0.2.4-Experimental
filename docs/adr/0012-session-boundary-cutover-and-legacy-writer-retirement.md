# 0012: 会话边界原子切换与旧 Writer 退役

Collision 迁移允许只读影子计算，禁止双写。旧 `RemoteCoverage` 在影子期仍是唯一生产 Writer；新路径无副作用。正式候选构建在插件启动或新会话建立时确定唯一 Authority Writer，不做运行中切流，也不在同一会话切回旧 Writer。影子比较解释差异，不把旧 remote-only 集合当金标。完成条件是旧 Writer 生产调用为零，且新 Writer 有效；回滚是部署级回滚。

## Considered Options

- 两个 Writer 并行并逐步切流：否决。顺序相关抖动，故障后无法判断最终状态由谁写成。
- 直接删除旧 Writer、不做影子验证：否决。无法区分有意修复 Host 缺失与新投影遗漏 Guest。
- 以新旧区域集合相等或相似度阈值为通过标准：否决。会把必须修掉的 remote-only 错误锁成回归金标。

## Consequences

- 影子比较器、旧扫描器和旧集合必须有退役条件，不能成为第三套永久架构。
- Resource 对 `IsRegionCovered` 的 fallback 随切换退出。
- 影子候选 DLL 与正式切换候选 DLL 不得混用验收日志。
