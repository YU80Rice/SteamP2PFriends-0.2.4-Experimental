# C-P2-Item-06 — 三端验收与冻结归档

- **Label**: `ready-for-human`
- **Type**: Specification
- **Status**: Open — 等待完整候选与人工运行证据
- **Version**: `0.2.4.10-Experimental`
- **Blocked by**: [05 会话切换与旧调用链退役](../c-p2-item-05-writer-cutover/issue.md)

## What to verify

用户运行1 Host + 2 Guest完整候选；Agent核对指纹并按剧本分析UMM诊断包，形成生命周期/同步独立验收记录。

## Acceptance criteria

- [ ] 三端部署SHA-256/MVID与候选记录一致，证据绑定实际构建及Case-ID。
- [ ] 首访/返区、同区两Guest、无人收尾、掉落到期及正常Host本地行为可核对。
- [ ] 多包清单、争拾/背包满、局部故障恢复、重连/换图均有结果和证据。
- [ ] 未执行项保持OPEN；07即使同轮采证也有独立结论，最终关单由用户裁决。

见 [本票规格](spec.md)。没有07前置依赖；本票通过不代表全部Item工作或总C-P2完成。
