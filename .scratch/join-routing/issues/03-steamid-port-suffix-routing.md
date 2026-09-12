# 03: SteamID:port 分类

**What to build:** 客机在直连地址栏粘贴 `个人SteamID:27016` 时按 Steam P2P 连接，不再把整串交给原版而静默失败。端口只用于分类，不进入 P2P 连接参数。

**Blocked by:** None (can start immediately)

**Status:** implemented-pending-runtime（静态闭环见 `audit/2026-09-12/Implementation-0.2.4.8-Ticket03-1048.md`；Runtime 归共享验收票 05）

- [x] 分类输入形如 `[可选空白] SteamID [可选单个 :port] [可选空白]`。仅当冒号最多一次、端口为 1–65535 十进制、去掉端口后仍是合法个人 SteamID 时返回 Steam P2P。
      （JR1 正例 + `CSteamID.IsValid/BIndividualAccount` 既有判定；端部空白走 Trim、内部空白守卫拒绝——round 2 Spec F2 修复）
- [x] 禁止「最后一个冒号」切割。IPv4:port、DNS:port、`:0`、`:65536`、非数字后缀、多冒号（含 IPv6）不得判为 Steam P2P。
      （JR2 负例矩阵全覆盖，含字面量 `:0`/`:65536`、冒号前空白、超长端口串 `:4294967297`——round 1 F1 / round 3 F3 覆盖修复）
- [x] 分类成功后的 P2P 连接只使用 SteamID；该 UDP 端口被有意忽略。
      （连接侧未改：`MenuPlayConnectP2PRoutePatch` 只消费 out `steamId`；JR1 断言 `steamId==假号`）
- [x] PureMemory 覆盖上列矩阵。不改变 Direct-IP / DNS 已有剥端口逻辑的职责边界。
      （JR1/JR2/JR3；`TryBuildDirectIpEndpoint`/`TryBuildExplicitDnsEndpoint` 未改，JR3 锁边界）
- [x] 不实现房间密码、不改 Route B。
      （diff 未触及密码与 Route B；README 保留「SteamID 路线丢密码」警示归票 04）
- [x] 双轴审查 CLEAN、Release 0/0、静态门禁绿。Runtime 归共享验收票。
      （round 1/2/3 Spec 各一 Finding 修复后，round 4 双轴 CLEAN；277/277、双 Release 0/0、三门禁 PASS）

操作顺序（非阻塞边）：本票建议先于会话密码落地，以降低与直连按钮补丁的同文件冲突。
