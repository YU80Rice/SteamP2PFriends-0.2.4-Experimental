---
title: "Collision 生命周期诊断修复：真实领域归属、Reentry 与 receipt 取证"
status: "completed"
labels:
  - "ready-for-human"
created_at: "2026-10-07T00:00:00+08:00"
updated_at: "2026-10-07T11:30:00+08:00"
---

# Collision 生命周期诊断修复（票 09 的独立返修票）

**What to build:** 让已经正常工作的 Collision 生命周期准确报告真实领域、滞回重入和 receipt 身份，使维护者无需要求用户反复完成相同操作才能判读证据。

**Blocked by:** 无待完成的实现依赖；消费 Collision Slice 06/08/10 的现有实现与票 09 日志。不以票 09 关单为前置，避免循环依赖。

**Status:** completed（2026-10-07，用户关单裁定）。实现、红绿测试、双轴三轮 CLEAN、双 Rebuild 身份一致均已完成；09-R1 候选已部署。用户确认既有 1H+2G 功能体验证据继续有效，本次 1H+1G 仅作为诊断播报探针补证，不替代上一轮多观察者覆盖。

## 问题与已核实根因

- 用户已多次完成 1H2G 远区进入、需求归零、滞回内返回，确认碰撞正常；集装箱门、洗衣机门的动画和碰撞正常。人工观察是功能验收证据，不得因缺诊断字段改判用户操作失败。
- 共享编排器构造转换事件时携带实际执行端口 DomainId；生产使用的资源诊断桥转交日志出口时未传递该身份，日志被标记为 Resource。Reentry detail 又只有 hysteresisCancelled=true，不能从单行识别 Collision。
- 已确认的是诊断身份丢失，不是 Collision 状态被 Resource 覆盖；没有证据证明第二 Writer、碰撞执行失败或跨域状态污染。
- Release receipt 诊断缺少完整会话绑定；正常关房、重开不保证产生旧 receipt 拒绝。不得将没有自然产生拒绝日志推断为用户没有完成测试。

## 输入证据

- 票 09 及其 2026-10-05 2313 审计保留，不回写为功能失败。
- 2026-10-06 UMM：Guest-A 233651、Host 233721、Guest-B 233818.zip，位于启动器 publish/UMM-v2.2.1-win-x64。
- Host 三段：Elver epoch=1（校验失败，不计通过）；Washington epoch=3（Reentry 专测）；Washington epoch=5（后续房间恢复）。
- 关联样例：Host 行 28755 Collision ReleaseScheduled；28949 同区域/同代次 Reentry。该关联保留为历史间接证据，不伪造为显式 domain=Collision。

## 验收与交付

- [ ] 按 spec.md 红先行，覆盖真实生产诊断格式化出口；仅检查引擎内事件或 Fake sink 不足以证明修复。
- [ ] 转换和去重诊断准确保留实际领域，Reentry 明确播报 authority；Resource 行为和日志兼容性有回归验证。
- [ ] receipt 释放/拒绝证据可关联领域、区域、会话、区域代次及 Acquire 代次；诊断不成为第二 Writer。
- [ ] 对旧 receipt 使用受控自动化接缝验证拒绝及零原生写入；记录 Runtime 接缝能否执行，不把自动化 PASS 冒称 Runtime PASS。
- [ ] 不增加默认运行的破坏性测试注入，不修改需求半径、滞回、Writer 或门玩法状态。
- [ ] 全套测试、双次可复现构建、独立身份核验及全新 Standards/Spec 双轴 CLEAN 后交付候选；不自动部署。
- [ ] 将历史人工功能证据和新诊断修复证据分开归档；源码变化所需相关 Runtime Gate 不可免除，也不默认重做整套 1H2G。

## 边界

- 不在票 09 或票 10 继续堆生产修复；不回退 06/08，不改 01–05，不恢复旧 Writer。
- 1H1G 可用于诊断最小冒烟，但不自动替代正式 1H2G 多观察者覆盖。
- 票 09 仍未关单；父规格的 Runtime 陈旧 receipt 门不在本票中静默删改。若原候选无法自然触发，实施方须提出受控取证方式并审查，不要求用户重复普通游玩。
