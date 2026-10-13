# 0029: Item 异常恢复实施边界

## 状态

已接受（0.2.4.10 Experimental，票 04）。本 ADR 将 ADR0024 中的待审工程提案
落为候选路径的实施边界；不授予 Runtime 验收，也不改变 05 Writer 切换范围。

## 决策

1. `/spfitemretry <x> <y>` 是候选 Item 路径的定向恢复入口。它只能在当前有效
   Listen-Host 会话、已登记回调、已批准观察者、当前 Core Connection Generation、
   当前需求和该区域已耗尽恢复资格同时成立时生效；同观察者/区域冷却为 10 秒。
2. 共享 Lifecycle Orchestration Engine 仍拥有 Acquire 的 FailedRetry 预算。Item
   复制接缝只声明处置：原生 `askItems` 已明确未执行时可回到共享区域 retry；调用已执行
   或 loaded 状态提交不确定时转为观察者+连接代次+区域的半包隔离，不盲目重发。
3. 复制发送失败不再回滚其它观察者、区域或领域。未声明该能力的 Resource/Collision
   继续使用既有 Observe 事务回滚。Item 通过领域端口能力接缝接入，不在协调器按 DomainId
   分支路由恢复通知。
4. 半包隔离在完整 Item 区域离区后释放；返区使用原生正常基线/结算路径，不重放已完成结算。
   断线、连接代次失效和会话结束撤销旧资格。

## 范围

本决策不新增 LMN、管理员权限、全区 ACK、原地 reset 或新传输协议；不退役旧 Writer，
正式切换留给票 05。PureMemory/StaticIL/BuildArtifact 证据不能代替 1 Host + 2 Guest
的人工 Runtime 验收。
