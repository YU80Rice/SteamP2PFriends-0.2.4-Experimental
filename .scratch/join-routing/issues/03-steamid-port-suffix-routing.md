# 03: SteamID:port 分类

**What to build:** 客机在直连地址栏粘贴 `个人SteamID:27016` 时按 Steam P2P 连接，不再把整串交给原版而静默失败。端口只用于分类，不进入 P2P 连接参数。

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] 分类输入形如 `[可选空白] SteamID [可选单个 :port] [可选空白]`。仅当冒号最多一次、端口为 1–65535 十进制、去掉端口后仍是合法个人 SteamID 时返回 Steam P2P。
- [ ] 禁止「最后一个冒号」切割。IPv4:port、DNS:port、`:0`、`:65536`、非数字后缀、多冒号（含 IPv6）不得判为 Steam P2P。
- [ ] 分类成功后的 P2P 连接只使用 SteamID；该 UDP 端口被有意忽略。
- [ ] PureMemory 覆盖上列矩阵。不改变 Direct-IP / DNS 已有剥端口逻辑的职责边界。
- [ ] 不实现房间密码、不改 Route B。
- [ ] 双轴审查 CLEAN、Release 0/0、静态门禁绿。Runtime 归共享验收票。

操作顺序（非阻塞边）：本票建议先于会话密码落地，以降低与直连按钮补丁的同文件冲突。
