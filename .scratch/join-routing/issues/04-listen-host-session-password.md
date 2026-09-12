# 04: Listen-Host Session Password

**What to build:** 房主能在开房菜单为当前听主机会话设可选密码；客机 SteamID 路线把直连页密码传入连接参数。无密码房间行为与现在一致；错密码走原版 `PASSWORD`。

**Blocked by:** None (can start immediately)

**Status:** implemented-pending-runtime（静态闭环见 `audit/2026-09-12/Implementation-0.2.4.8-Ticket04-1123.md`；Runtime 归共享验收票 05）

- [x] 插件开房菜单新增密码字段：默认空、遮罩、不设最大长度、不 trim。空串 = 无密码房间。输入规则只约束该新字段，不改变原版直连页密码框。
      （`P2PNativeMenuUI.BuildRoleMenu` 新增密码框：`IsPasswordField=true`、`MaxLength=0`、`OnPasswordChanged` 不 trim；`OpenRoleMenu` 每次打开默认空，不从配置恢复；原版直连页 `passwordField` 行为未改）
- [x] 非空值只写入当前会话的 `Provider.serverPassword`。返回菜单、结束会话或销毁房间时清空。不写入上次房间设置、审计、普通诊断或仓库文件。
      （`ConfigureCommonServerSettings` 经 `SessionPassword.ResolveSessionServerPassword` 写入并只记 hasPassword；清空= `StopP2PServer`/`AbortHostStart`/`OnClickedBackFromRole` 三处 `SessionPassword.ClearRuntime()`；`Destroy` 有意不清——U3DS 每 tick 调用，零侵入铁规；`PersistLastRoomSettings` 不含密码）
- [x] 日志与诊断只记录 `hasPassword=true/false`，不记录明文或长度，不转储连接参数中的密码。
      （四处日志 `StartP2PServer`/`[SessionPassword]`/`[UnifiedConnect]`/`[Diag] ServerConnectParameters` 均只输出布尔，唯一出口 `SessionPassword.HasPassword`）
- [x] 客机 SteamID 路线从原版直连页密码框读取并传入 `ServerConnectParameters`。IPv4/DNS 已有密码传递保持。
      （`MenuPlayConnectP2PRoutePatch` SteamID 分支读 `passwordField.Text` → `P2PJoinManager.TryConnectToHost(targetId, password)` → `SessionPassword.BuildSteamP2PConnectParameters`；IPv4/DNS 路径未改；`TryConnectFromLobby`/旧 SteamID 菜单路径仍空密码）
- [x] 不改写 `connectionFailureInfo`，不把 `PASSWORD` 转成自定义失败类型，不拦截原版失败面板，不新增专用密码弹窗。
      （diff 未触及失败枚举与失败 UI；无新 Harmony patch，仅既有 Prefix 内部扩展）
- [x] PureMemory：无密码/有密码参数组装（只断言 hasPassword）；会话结束后运行时密码为空。
      （JR4 参数组装、JR5 房主输入语义、JR6 运行时清空+幂等，断言全部经 `HasPassword` 布尔出口；注册数 277→280）
- [x] 双轴审查 CLEAN、Release 0/0、静态门禁绿。Runtime 归共享验收票。
      （round 1 Standards CLEAN / Spec NOT CLEAN F1 → 修复后 round 2 双轴 CLEAN；280/280、双 Release 0/0、三门禁 PASS）

无语义阻塞于分类票。建议执行顺序为分类票先合并，再做本票，仅为降低同文件冲突。
