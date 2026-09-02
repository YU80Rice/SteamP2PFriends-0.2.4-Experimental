# 0.2.4.8 仓库与插件整体结构整理

- **Label**: `ready-for-agent`
- **Type**: Specification
- **Status**: Ready for Agent
- **Version**: `0.2.4.8`
- **Branch**: `codex/structure-baseline-0.2.4`
- **Release boundary**: 当前 `0.2.4-Experimental` 标签与历史归档版本保持不变

## Objective

在不改变当前生产行为的前提下，建立可审计、可测试、可迁移的仓库与插件结构基线；完成基线后，以 Resource 作为首个 Production Control Seam 行为迁移样板。

## Spec

[查看完整需求规格说明书](./spec.md)

## Blocking order

1. U3-SDK 原生注册顺序追踪与注册编排拆分；
2. Domain Ownership、目录与 namespace 迁移；
3. Evidence Class 测试结构与 Build Fingerprint 门禁；
4. Resource Production Control Seam 迁移；
5. 旧 Authority Writer 退出与 Host/Guest Runtime 验证。

后续步骤必须按阻塞关系执行；在前一阶段的 Evidence Gate 未通过前，不得进入下一阶段。
