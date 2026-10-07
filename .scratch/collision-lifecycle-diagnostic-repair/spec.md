# Collision 生命周期诊断修复规格

## 目标与范围

修复共享生命周期事件到生产日志的身份丢失，形成可审计的 Collision Reentry 与 receipt 证据。本票是独立返修，不实施第二套生命周期或改变原生碰撞行为。关联父票：`.scratch/collision-migration-slice/issues/09-shared-1h2g-runtime-acceptance.md`。

## 输出契约

1. 事件领域来自实际执行端口产生的 LifecycleDiagnostic.Domain，不由资源出口默认值、日志前缀或 authority 文本反推。正常转换、各 severity、TransitionOnce 去重路径均须保留领域。
2. Collision Reentry 成功行含 event=LeaseReentry、domain=Collision、authority=Collision、outcome=success、hysteresisCancelled=true，以及原有 region/sessionEpoch/regionGeneration/observer/connectionGeneration；Resource Reentry 保持 domain=Resource，不能被改成 Collision。
3. 如兼容保留 [ResourceObs] 前缀，字段身份必须真实且契约测试覆盖该生产格式化路径；不靠追加第二个 domain= 字段修补，不允许一行出现相互冲突的身份。
4. Release/拒绝日志关联当前命令及 receipt 身份：领域、区域、命令 SessionEpoch/RegionGeneration、receipt SessionEpoch/RegionGeneration/AcquireGeneration。receipt 缺失须显式标记，不虚构零值为有效身份。
5. 不改变日志安全去重、有界配额和级别语义；不得让新增字段改变执行结果、保活状态或 Release 权限。

## 红先行验证

- 实际诊断桥/格式化出口的 Collision 与 Resource 转换身份测试；旧实现必须因 Collision 被格式化为 Resource 而红。
- 从最高生命周期接缝驱动 Acquire、需求归零、滞回内重入，消费真实格式化输出；断言取消 pending、没有最终 Release、无额外 Acquire，且日志满足上述 Reentry 契约。
- 同区双领域及多观察者保持隔离，Resource 输出兼容；覆盖去重和不同 severity 的身份传递。
- receipt 日志契约测试包括成功释放、无 receipt、旧会话 receipt 与相同数字代次的新会话 receipt，防止靠 AcquireGeneration 数字混同身份。
- 旧 receipt 拒绝测试调用真实生产身份门的可执行接缝，断言明确拒绝且 Store 无原生写入。若纯宿主受到 Unity JIT/icall 限制，具名记录接缝，禁止用旁路测试假称真实边界已执行。

## 人工证据与 Runtime 边界

- 既有用户观察：多次滞回内返回，碰撞正常，集装箱门/洗衣机门动画、开关门碰撞正常。作为功能证据保存，不因日志错标撤销；它不能单独证明内部 receipt 身份门。
- 旧日志中的 Reentry 不能仅因 domain=Resource 被解释成 Collision 从未重入；以错标缺陷和关联证据陈述，不将推断写成显式日志事实。
- 实施方先完成自动化与诊断修复，再制定最小相关 Runtime 补证矩阵。1H1G 可做新增诊断冒烟；正式多观察者项沿用已有证据的范围必须经审查，不能冒称新 DLL 已全流程验收。
- 不要求用户靠重复开关门触发陈旧 receipt。首先查明真实 Runtime 可达路径；若正常会话清理令旧 receipt 不再可提交，则记录“未自然触发”，提出隔离环境受控探针或具名规格裁定，不自行降低父规格 Runtime 门。
- 若需受控探针，不得默认启用、不得新增网络指令绕过身份门，必须限制到隔离实验测试候选并单独审查；本票不预先授权部署或外部发送。

## 交付门

- 测试遵守生产插件先重编、测试宿主再重编的 HintPath 顺序；红、绿、全套验证逐项留证。
- 源码冻结后双次 Rebuild，独立重采 SHA-256/MVID/Case-ID；不能复用旧候选身份。
- 新实例 Standards/Spec 双轴并行审核，修复发现后重新双轴审查至 CLEAN。
- 交付实现审计、证据矩阵及候选路径，不自动覆盖稳定版本、不自动部署。
- 仅在后续票 09 验收审计收口时更新其正式状态；本票完成不等于父票自动关单。
