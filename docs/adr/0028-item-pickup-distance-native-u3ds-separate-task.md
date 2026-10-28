# 0028: U3DS 原生拾取距离校验作为独立 Item 权威交互任务

用户在2026-10-08选择Q14的A：本次恢复U3DS的20米拾取距离校验，单独列为Item权威交互任务，复用原生限制；不改变正常拾取操作距离，不由区域租约授权。生命周期与同步迁移保持原范围，两者分别验收。

校验玩家到权威ItemData位置的距离平方，原生拒绝条件为大于400；通过该项只表示未被距离拒绝，其他原生身份/状态/实例/背包条件仍须满足。应在背包写入之前恢复这条校验，不全局伪造dedicated，不重写整个Item协议。

依据SDK `ItemManager.cs:294–381`，尤其 `325–331`。源根为 `D:/Agent-工作目录/U3-SDK/Assets/Runtime/Assembly-CSharp/Unturned/Managers/`。用户原文见 [原生职责复用与按需运行的边界是什么](../../.scratch/map-c-stage-world-state-migration/ticket-runtime-duty-boundary/ticket.md) 的Q14；任务草稿见 [恢复 U3DS 原生20米拾取距离校验](../../.scratch/item-authoritative-pickup-distance/issue.md)。
