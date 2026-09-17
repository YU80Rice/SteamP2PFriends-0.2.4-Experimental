# 01: 僵尸重生 Listen-Host Dedicated Gate

**What to build:** 听主机在普通 PEI 上打死满足重生条件的僵尸后，原版重生窗口内该区域能再出现僵尸。只打开 `respawnZombies` 的听主机早退，不改生成算法。

**Blocked by:** None (can start immediately)

**Status:** completed（静态闭环 `audit/2026-09-12/Implementation-0.2.4.8-Ticket01-0027.md`；Runtime `audit/2026-09-18/RuntimeAcceptance-0.2.4.8-Ticket05-0047.md`，用户 2026-09-18 关单）

- [x] 将该早退所依赖的资格判断对齐到现有 `IsDedicatedOrP2PHost()`，听主机进入原版专用服重生分支。
- [x] 不修改生成表抽取、特化、Boss 上限、Beacon 剩余、Horde 波次、bound 轮转、tick 切片，也不再次修改已对齐的僵尸状态发送门控。
- [x] StaticIL：目标早退只替换一次，栈平衡保持。（ZG1/ZG2；ZG1 锁死现行 U3 构建中该调用点全方法恰 1 处）
- [x] PureMemory：专用服真、听主机真、普通单机/客机假；听主机在无信标、非 Horde 时不得仍走单机直接 return。
      （DG1-DG3 真值表直接执行生产资格函数；该子句由 DG2 资格恒真 + ZG1/ZG2 接线锁两证据域联合闭合，审计 §6-1 具名）
- [x] 诊断能表明是否走进对齐后的分支，以便 Runtime 区分「窗口未到」与「早退仍在」。
      （Prefix/Postfix 观测 respawnZombieIndex 轮转，按 bound 累计 passed/returned/ambiguous + elig；DG4 锁纯函数判定）
- [x] 双轴审查 CLEAN、Release 0/0、静态门禁绿。Runtime 不在本票宣称 PASS，归共享验收票。
      （round 1/2/3 双轴均 CLEAN；Runtime 验收归 .scratch/listen-host-join-routing-runtime-acceptance/issues/05）
