# 05: 共享 1 Host + 2 Guest Runtime 验收

**What to build:** 不写生产代码。用前四张票审查通过后的同一 Release DLL，做一次三端实机，证明僵尸能再刷、物品能周期消失并再长、无密码/有密码/`SteamID:27016` 路由成立。

**Blocked by:** 01 僵尸重生 Listen-Host Dedicated Gate；02 物品周期生命周期 Listen-Host Dedicated Gate；03 SteamID:port 分类；04 Listen-Host Session Password。四张均须双轴 CLEAN、Release 构建与静态门禁通过。

**Status:** ready-for-human

- [ ] 统一 Case ID；三端 `SteamP2PFriends.dll` SHA-256 逐字节一致。
- [ ] 普通 PEI：记录已知区域僵尸初始数量，打死满足重生条件的僵尸，等待原版窗口后该区域出现新僵尸；诊断确认走进对齐后的早退分支。
- [ ] 物品：诊断记录 `generateItems` / `despawnItems` / 周期 respawn 的次数与区域；地面物品能被周期移除也能再生成；无持续刷满或无界累加。
- [ ] 时间窗内未自然刷出不得直接判代码失败，须结合诊断计数与可重复条件区分「窗口未到」与「早退仍在」。
- [ ] 预生成与周期处理若造成重复刷物品：本验收 FAIL，另开收缩票，不在本票重构预生成。
- [ ] Join Routing：无密码房间 SteamID 不填密码可连；有密码房间不填密码由原版产生 `PASSWORD`；填对密码可连；`SteamID:27016` 走 Steam P2P 连上。
- [ ] 日志只证明 `hasPassword`、路由类型和失败枚举，无密码明文或长度。
- [ ] 本轮不专项 Horde/Beacon，也不得改变它们既有语义。
- [ ] 回传 UMM 诊断包；按 Evidence Class 落盘 Runtime 审计。本票不关实施票、不改 RELEASES 批准态。
