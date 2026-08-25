# Ticket 3: 历史 P0/P1 系列补丁清洗、归并与退役矩阵

- **Label**: `wayfinder:research`
- **Type**: AFK
- **Parent**: `map-top-level-architecture-blueprint`
- **Status**: Open (Frontier)

---

## Question

当前 `Patches/` 目录下散落了数十个历史补丁（P0-B、P0-C-1、P0-D、P0-E、P1-S* 等）。
哪些补丁已经被 M1~M4 领域适配器完全接管可以正式退役？
哪些补丁是核心挂载接缝（Hook Seams）需要保留并归入领域适配器内部？
哪些补丁是通用诊断/防御补丁应归入 `Diagnostics/` 或 `Security/`？
需产出不可篡改的补丁生命周期矩阵与退役清理清单。
