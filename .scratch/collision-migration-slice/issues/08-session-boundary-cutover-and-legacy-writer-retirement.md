# 08: 会话边界原子切换并退役旧 Collision Writer

**What to build:** 正式候选在启动或新会话建立时选定唯一 Collision Authority Writer；共享引擎经 Collision 执行端口写入；旧覆盖扫描、旧刷新 Writer、对树的越权写入、资源对旧覆盖查询以及生产影子比较全部退出。切换与退役同票闭合，不允许可部署候选双写。本票完成后 implemented-pending-runtime。

**Blocked by:** 07 闭合 Collision 正式切换准入证据

**Status:** implemented-pending-runtime

- [x] 正式候选在插件启动或新 Session Epoch 一次性确定唯一 Writer；不做运行中按区域、玩家或比例切流，不保留旧 Writer 开关。
- [x] 共享生命周期引擎经 Collision Execution Port 成为唯一插件侧 Collision Writer。
- [x] 旧覆盖集合、观察者扫描、reconcile 和刷新写入退出生产调用图。
- [x] Collision 不再写可采集树；Resource 不再读取旧覆盖谓词。
- [x] 正式生产不再运行影子比较。
- [x] 新 Writer 失败或 retry 耗尽不调用旧 Writer；回滚只允许结束会话并部署上一份已验收构建。
- [x] StaticIL / 调用图证明旧 Writer 生产调用为零，且新 Writer 是唯一生产入口。
- [x] BuildArtifact 为正式切换候选指纹，与影子候选可区分。
- [x] 双轴审查 CLEAN；Runtime 仍 Pending，正式运行验收归票 09。
