# Implementation-0.2.4.10-Item04

**工单**：`.scratch/c-p2-item-04-failure-recovery/`
**状态**：`implemented-pending-runtime`
**提交**：`44c37f7`，父基线 `e4cf746`
**Runtime**：不在本票关闭，归票 06。本记录不是 Runtime PASS。

## 源码验收

- 共享 `FailedRetryController` 持有失败预算复位；首次失败加最多 3 次，间隔 0.5/1/2 秒，整链 10 秒。
- 命令 10 秒冷却只在 `ItemProductionControlSeam._manualCooldown`，键为 Session+observer+Core 连接代次+region，只在 Started 后写入。
- 同一结算 Job 挂起后续做。未确认结果与可能半包不盲重发。
- 耗尽和不可确认结果向当前有效等待者定向通知。

## 审查链

较早窗口的非 CLEAN 与求救保留在 `.scratch/c-p2-item-04-failure-recovery/rescue-message-round7.txt` 与 `rescue-message-round8.txt`。这里不重写那些正文。

E01 前窗口：

- 第 1 轮 Standards `ses_ecc559019ffelmYdaLbhcPK4mJ` CLEAN；Spec `ses_ecc559000ffe3czT8KBRwJRMl6` NON-CLEAN。阻断是作业级 `LastManualAt` 会让 A 限流 B。
- 第 2 轮 Standards `ses_ecc47ff6cffe9143fDbqJMkvZs` NON-CLEAN；Spec `ses_ecc47ff58ffe2eIfRWF4XKEyWt` CLEAN。阻断是入口冷却与引擎时间戳重复写入。
- 专家方案：`.scratch/c-p2-item-04-failure-recovery/expert-rescue-window9.txt`。确认消息 `msg-d4e8a1c0-7b32-4f19-8e55-1a6c9d0f34ab`。

E01 第 1 轮，未再改生产语义后的最终审查：

- Standards `ses_ecc346203ffeqYLUZUwPB2cTE5`，haowei-ai/grok-4.7，assistant variant low，CLEAN。
- Spec `ses_ecc345f60ffe0KYSgx2KuuMjBB`，haowei-ai/grok-4.7，assistant variant medium，CLEAN。

## 证据

双项目 Rebuild 成功。WhitelistTests `529/529 PASS`。日志：`.scratch/c-p2-item-04-failure-recovery/test-run-e01-pre-review.txt`。本关单只改票面、交付记录和审计索引，不改变该证据所覆盖的生产源码与测试。
