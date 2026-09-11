# 02: 物品周期生命周期 Listen-Host Dedicated Gate

**What to build:** 听主机地面物品按服务器节奏消失并再生成。打开更新循环里整段 despawn/respawn 的 dedicated 早退；进世界一次性预生成保留。

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] 听主机继续执行周期 `despawnItems` 与周期 respawn；普通单机/客机早退不变；专用服行为不变。
- [ ] 不关闭、缩小或重写 `onLevelLoaded` 一次性预生成。
- [ ] StaticIL：该早退只替换一次，栈平衡保持，目标为现有资格函数。
- [ ] 诊断按区域记录 `generateItems`、`despawnItems`、周期 respawn 的调用次数；诊断不得成为第二套生成器。
- [ ] 同一区域持续刷满或无界累加 → 本票 Runtime 阶段 FAIL，另开收缩票，禁止在本票内临时重构预生成。
- [ ] 双轴审查 CLEAN、Release 0/0、静态门禁绿。Runtime 不在本票宣称 PASS，归共享验收票。
