# 07: 闭合 Collision 正式切换准入证据

**What to build:** 汇总静态准入实现、影子 Runtime 与 Collision 执行端口证据，给出正式切换的 Go / No-Go 审计结论。不切换 Authority Writer，不退役旧 Writer，不宣称最终 Collision Runtime PASS。

**Blocked by:** 04 修复共享控制面的正式切换准入阻塞项；05 Collision 声明式 Demand Policy 与只读影子验证；06 Collision Execution Port 与 Acquisition Receipt

**Status:** ready-for-agent

- [ ] 04 的静态准入不变量仍成立，并吸收 05 回写的任何新阻塞。
- [ ] 影子 Runtime 验证 canonical samples、Host demand、差异稳定性、会话恢复，且无错误 Release。
- [ ] Collision Execution Port 的身份拒绝、receipt 与局部故障隔离有证据。
- [ ] 形成正式切换 Go / No-Go 审计结论；No-Go 时不得进入 08。
- [ ] 不切换 Authority Writer；不退役旧 Writer。
- [ ] 不宣称最终 Collision Runtime PASS。
- [ ] 双轴独立审核针对本票证据包。
