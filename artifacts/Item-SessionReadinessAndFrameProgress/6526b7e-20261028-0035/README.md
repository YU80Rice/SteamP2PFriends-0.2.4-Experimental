# SteamP2PFriends 0.2.4.10 — Item session/frame test build

交付文件：`SteamP2PFriends.dll`（Cutover；net472；IL-only I386/AnyCPU）。

## 安装与回滚

1. Host 与 Guest 全部正常关闭游戏。
2. 在每台需要测试的客户端备份现有 `SteamP2PFriends.dll`。
3. 只替换此插件 DLL；不要将 WhitelistTests、native host 或 Libs 测试副本放进游戏目录。
4. 用 UMM 启动新会话/新世界并按 R04 步骤测试。文件身份见同目录 `fingerprint-verification.txt`。
5. 若异常，正常退出游戏后恢复各端备份的 DLL。

这是待实机验证的独立插件，不包含ZIP或自动部署。R04视觉延迟仍待记录。
