# 09: 共享 1 Host + 2 Guest Runtime 验收与 Collision Slice 关单

**What to build:** 用正式切换候选在 1 Host + 2 Guest 上证明资源迁入共享引擎无回归，Collision 租约生命周期正确，旧 Writer 调用为零。通过后 Collision Migration Slice 完成。影子 DLL 日志不得顶替本票。

**Blocked by:** 08 会话边界原子切换并退役旧 Collision Writer

**Status:** completed（2026-10-07，用户关单裁定：既有 1 Host + 2 Guest 功能与多观察者证据继续有效；09-R1 诊断修复后的 1 Host + 1 Guest 仅补播报探针，不替代 1H2G。用户确认碰撞、集装箱门/洗衣机门动画与碰撞正常，并接受“历史三端功能证据 + 本轮诊断修复证据”合并收口。）

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
