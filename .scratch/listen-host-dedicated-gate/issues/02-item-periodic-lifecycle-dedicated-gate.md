# 02: 物品周期生命周期 Listen-Host Dedicated Gate

**What to build:** 听主机地面物品按服务器节奏消失并再生成。打开更新循环里整段 despawn/respawn 的 dedicated 早退；进世界一次性预生成保留。

**Blocked by:** None (can start immediately)

**Status:** completed（静态闭环 `audit/2026-09-12/Implementation-0.2.4.8-Ticket02-1043.md`；Runtime `audit/2026-09-18/RuntimeAcceptance-0.2.4.8-Ticket05-0047.md`，用户 2026-09-18 关单）

- [x] 听主机继续执行周期 `despawnItems` 与周期 respawn；普通单机/客机早退不变；专用服行为不变。
      （Update 尾部守卫单点 Transpiler 对齐现有 `IsDedicatedOrP2PHost()`；IGP3 资格回归锁 + IG1/IG2 接线锁联合闭合）
- [x] 不关闭、缩小或重写 `onLevelLoaded` 一次性预生成。
      （diff 确未触碰；听主机进区 generateItems 仍受 Authoritative 账本约束）
- [x] StaticIL：该早退只替换一次，栈平衡保持，目标为现有资格函数。
      （IG1/IG2；IG1 锁死现行 U3 构建中 Update 方法体恰 1 处调用点；IG2 含签名等价栈平衡断言）
- [x] 诊断按区域记录 `generateItems`、`despawnItems`、周期 respawn 的调用次数；诊断不得成为第二套生成器。
      （三路 Prefix/Postfix 区域计数 + 独立限频日志；纯函数 ObserveDespawn/ObserveRespawn 封闭枚举判定；round1 F1 修复闭合）
- [x] 同一区域持续刷满或无界累加 → 本票 Runtime 阶段 FAIL，另开收缩票，禁止在本票内临时重构预生成。
      （未做越权"预防"；Runtime 判读口径见审计 §8-5）
- [x] 双轴审查 CLEAN、Release 0/0、静态门禁绿。Runtime 不在本票宣称 PASS，归共享验收票。
      （round 1 Standards CLEAN / Spec NOT CLEAN(F1、F2)→ 修复 → round 2 双轴均 CLEAN；Runtime 验收归 .scratch/listen-host-join-routing-runtime-acceptance/issues/05）
