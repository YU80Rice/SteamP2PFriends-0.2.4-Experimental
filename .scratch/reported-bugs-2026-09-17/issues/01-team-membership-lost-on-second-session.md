# 01: 队伍不跨局 —— 第二局主机掉出第一局创建的队伍

**Label:** needs-triage

**Type:** Bug（用户口述，未经日志核对）

**Status:** Open —— 待取证

## 现象（原文照录）

> 第一次游戏，主机创建队伍并拉队友进入，主客机处于同一队伍，箱子可正常交互；第二次游戏，主机作为队长会掉出第一次创建的队伍，导致客机失去队友，需要重新创建队伍并重新邀请

## 影响

同队共享的资源权限（组内箱子交互）失效；每次开新局都要重建队伍并重新邀请，等同队伍功能跨局不可用。

## 取证指引

原版 `GroupManager` 的存档路径由探针字段 `logicalPath=/Groups.dat` 标出。当前已有只读探针 [`P2PGroupStateProbe`](../../../Core/Patches/P2PGroupStateProbe.cs)，在下列点打 `[P2P-GroupProbe]` 行：`GroupManager.load`（before/after）、`GroupManager.save`（before/after）、`PlayerQuests.ReceiveGroupState`（incoming/before/after）、`ReceiveCreateGroupRequest`、`ReceiveAcceptGroupInvitationRequest`、`SendInitialPlayerState`。

因此**不需要新增探针**即可判定掉队发生在哪一段，只需第二局的包按下列锚行读：

- 第二局 `event=GroupManager.load phase=after` 若已 `group=none` / `groupKnown=false` / `member=False` → 掉队发生在**读档阶段**。
- 若 `load/after` 仍持有原 group，而 `event=SendInitialPlayerState` 或 `ReceiveGroupState phase=after` 之后才变 `none` → 掉队发生在**入场/状态下发阶段**。
- 比对 `event=ReceiveGroupState phase=incoming` 的 `incomingGroup` 是否为 `none`：可区分「服务端从未下发组」与「下发了但被覆盖」。

主机与客机**两侧都要包**：用于区分「主机自己掉出」与「主机仍在队但客机看不到他」。

## 待澄清

- [ ] 「第二次游戏」是退回主菜单重开一局，还是重启游戏进程？
- [ ] 是否同一存档、同一主机、同一台机器？
- [ ] 客机第二局是重新加入还是延续上一局连接？
- [ ] 掉队是否必现；若第一局正常结束（不杀进程直接退房）是否也复现？
- [ ] 第二局里客机侧是否也掉队，还是只有主机掉？

## 备注

- 已知插件不写队伍状态（见批次 [issue.md](../issue.md) 的事实段），因此本条**不能**用「插件动了 group」作默认假设，须以日志落点为准。
