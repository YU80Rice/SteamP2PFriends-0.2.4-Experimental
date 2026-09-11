# 04: Listen-Host Session Password

**What to build:** 房主能在开房菜单为当前听主机会话设可选密码；客机 SteamID 路线把直连页密码传入连接参数。无密码房间行为与现在一致；错密码走原版 `PASSWORD`。

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] 插件开房菜单新增密码字段：默认空、遮罩、不设最大长度、不 trim。空串 = 无密码房间。输入规则只约束该新字段，不改变原版直连页密码框。
- [ ] 非空值只写入当前会话的 `Provider.serverPassword`。返回菜单、结束会话或销毁房间时清空。不写入上次房间设置、审计、普通诊断或仓库文件。
- [ ] 日志与诊断只记录 `hasPassword=true/false`，不记录明文或长度，不转储连接参数中的密码。
- [ ] 客机 SteamID 路线从原版直连页密码框读取并传入 `ServerConnectParameters`。IPv4/DNS 已有密码传递保持。
- [ ] 不改写 `connectionFailureInfo`，不把 `PASSWORD` 转成自定义失败类型，不拦截原版失败面板，不新增专用密码弹窗。
- [ ] PureMemory：无密码/有密码参数组装（只断言 hasPassword）；会话结束后运行时密码为空。
- [ ] 双轴审查 CLEAN、Release 0/0、静态门禁绿。Runtime 归共享验收票。

无语义阻塞于分类票。建议执行顺序为分类票先合并，再做本票，仅为降低同文件冲突。
