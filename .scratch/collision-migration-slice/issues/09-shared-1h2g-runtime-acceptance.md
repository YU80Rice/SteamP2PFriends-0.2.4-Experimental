# 09: 共享 1 Host + 2 Guest Runtime 验收与 Collision Slice 关单

**What to build:** 用正式切换候选在 1 Host + 2 Guest 上证明资源迁入共享引擎无回归，Collision 租约生命周期正确，旧 Writer 调用为零。通过后 Collision Migration Slice 完成。影子 DLL 日志不得顶替本票。

**Blocked by:** 08 会话边界原子切换并退役旧 Collision Writer

**Status:** in-progress（2026-09-27 取证定案：上一轮 S3 失败的直接原因不是维护拍缺失，而是 **Collision 租约从未成立**——4293 条 Acquire 全部 `acquire-identity-rejected`（`regionGeneration=0`，Ledger 代次源自举死锁）；下一刀落 06/08 执行路径（代次源自举语义），从当前 HEAD 继续改；维护拍保留但非本缺陷修复；09 不关单；见 `audit/2026-09-27/Forensics-0.2.4.9-Ticket09-2051.md`，其推翻 0938 报告 §2 根因叙事）

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
