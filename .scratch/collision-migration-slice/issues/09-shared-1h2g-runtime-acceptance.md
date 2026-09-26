# 09: 共享 1 Host + 2 Guest Runtime 验收与 Collision Slice 关单

**What to build:** 用正式切换候选在 1 Host + 2 Guest 上证明资源迁入共享引擎无回归，Collision 租约生命周期正确，旧 Writer 调用为零。通过后 Collision Migration Slice 完成。影子 DLL 日志不得顶替本票。

**Blocked by:** 08 会话边界原子切换并退役旧 Collision Writer

**Status:** in-progress（2026-09-26 修复轮：票 08 候选实机启动失败已修复并双轴 CLEAN，新候选指纹 `F055B34CF40FF3D73E4C946CD11BBBF0CBB64E2F98CCE69903ABDEEB92C95CFE`，待三端复测；见 `audit/2026-09-26/RuntimeFix-0.2.4.9-Ticket09-2341.md`）

- [ ] 使用正式切换候选 DLL；三端 SHA-256 / MVID / 版本 / Case-ID 一致。
- [ ] Resource 迁入共享引擎后的砍树、资源碰撞与租约行为无回归。
- [ ] Collision：Host 独在、Guest 独在远区开关门、双 Guest 同区/跨区通过。
- [ ] Guest 离开不释放 Host 或另一仍在场 Guest 的需求；滞回重入无抖动。
- [ ] 门动画、Collider 行为正确；Receipt Release 不覆盖 Acquire 后的合法原版状态。
- [ ] Collision 不改变任何可采集树；远区砍树仍由资源域执行。
- [ ] 单坏样本、单区域失败、单领域失败不跨域传播；retry 无单槽互吞。
- [ ] 会话恢复 / 重置正确；陈旧 receipt 无提交资格。
- [ ] 旧 Writer 调用为零；无运行时回退旧路径；Release 日志指向具体 receipt。
- [ ] 生产代码若再变，必须重算指纹并重跑相关 Runtime Gate。
- [ ] Migration Manifest、审计索引与地图状态更新；双轴独立审核 CLEAN。
- [ ] 用户关单后 Collision Migration Slice 标记完成。
