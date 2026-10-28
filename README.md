# SteamP2PFriends 0.2.4 Experimental

> Unturned listen-host 联机插件的实验线：无 U3DS 开房，并正在把世界状态从旧补丁切到 Multi-Observer 区域租约架构。

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](./LICENSE)
[![Version](https://img.shields.io/badge/version-0.2.4.10--experimental-blue.svg)](./Build/Version.props)
[![Status](https://img.shields.io/badge/status-experimental--prerelease-orange.svg)](https://github.com/YU80Rice/SteamP2PFriends-0.2.4-Experimental/releases)

> **版本来源**：徽章与「当前版本」表中的版本号以 [`Build/Version.props`](./Build/Version.props) 为唯一来源；展示值须与其一致。
>
> **当前提供实验预发布包。** 默认分支为 `codex/structure-baseline-0.2.4`；下载见[GitHub Releases](https://github.com/YU80Rice/SteamP2PFriends-0.2.4-Experimental/releases)。[0.2.3旧实现](https://github.com/YU80Rice/SteamP2PFriends)已归档，两个版本的DLL不得同时部署。

## 这是什么

房主从原版单人地图开多人房间，客机用 SteamID 或 IPv4 加入，不需要安装或启动 U3DS。

两条联机路径并存：

- **SteamID P2P**：客机在原版直连地址栏输入房主个人 SteamID。
- **IPv4 直连**：真实局域网或 Radmin LAN 等虚拟局域网；客机输入房主 IPv4 和共享端口。

IP 只负责寻址。玩家身份、审批与白名单始终使用 Steam Networking Sockets 握手得到的 SteamID。

0.2.4将观察者需求并集、区域租约和单一权威写入接到生产路径上。Resource与Collision已有共享控制面；0.2.4.10进一步接入Item，并修复物资呈现、连接身份、世界准备与逐帧结算。当前目标已完成1 Host＋2 Guest人工验收，见[运行验收记录](./audit/2026-10-29/RuntimeAcceptance-0.2.4.10-R04.md)及[更新日志](./CHANGELOG.md)。

## 当前状态（请按这里判断能不能玩）

| 项目 | 值 |
|---|---|
| BepInPlugin / AssemblyVersion | `0.2.4.10` |
| 通道 | Experimental，GitHub实验预发布 |
| 当前阶段 | 本阶段物品修复与DLL交付完成，2026-10-29用户通过1H＋2G目标体验验收 |
| 本轮已确认 | 物品刷新、僵尸AI及掉落正常，无幽灵或不刷新；日志支持双Guest在线、退出重连和同进程再开世界 |
| 后续独立事项 | 原06未覆盖边界与07受控超距安全专项另行留档；残余Resource观测与Zombie需求保护记录见运行审计 |
| 分发 | Release附件`SteamP2PFriends-0.2.4.10.zip`；仓库归档在`publishi/0.2.4.10/` |
| 旧实现 | 0.2.3 已归档，只作来时路，不再开发 |

当前候选自动化635/635通过，实现与交付已完成独立双轴审查；本次P2P listen-host实机结论单独记录。实验预发布范围及文件身份见[0.2.4.10发布说明](./publishi/0.2.4.10/RELEASE_NOTES.zh-CN.md)，历史证据见[audit索引](./audit/README.md)。

## 安装（发布包）

1. 所有端退出游戏，备份原插件；确认已安装适用的BepInEx。
2. 从[Releases](https://github.com/YU80Rice/SteamP2PFriends-0.2.4-Experimental/releases)下载`SteamP2PFriends-0.2.4.10.zip`，解压到Unturned根目录，合并`BepInEx`目录。
3. 最终文件位置如下；本包只含插件，房主与所有客机必须使用同一份DLL：

```text
Unturned/
└── BepInEx/
    └── plugins/
        └── SteamP2PFriends.dll
```

4. 用`Get-FileHash .\BepInEx\plugins\SteamP2PFriends.dll -Algorithm SHA256`核对各端一致，并与发布说明比对。不要让多个版本同时加载；回滚时退出游戏后恢复备份。

## 使用方法

### 房主开房

1. 进入原版单人地图选择界面。
2. 点击插件的「多人联机」。
3. 设置玩家数、难度、PVP、作弊与死亡保留规则。
4. 把房主 SteamID，或局域网 / Radmin IPv4 发给客机。
5. 进入世界后按 `P`（原版默认，可重绑），在玩家列表里审批新玩家。

新玩家进世界后先进入约 30 秒审批隔离：不能移动、交互、指令和造成伤害，并保持受保护。客机聊天栏会按间隔显示剩余时间。

### 客机通过 SteamID 加入

1. 打开「开始游戏 → 直连」。
2. 在顶部地址栏粘贴房主个人 SteamID（可带单个 `:端口`；端口只用于分类，不进入 P2P 连接参数）。
3. 有密码房间在下方密码框填写房主给的密码（无密码房间留空）。
4. 点击连接。

插件只接管有效的 Steam 个人账户 ID；U3DS Server Code 仍交给原版。房主在插件开房菜单为当前这局设置的可选密码，客机经直连页密码框传入 SteamID 路线；错密码由原版按 `PASSWORD` 失败提示。密码只在当前会话有效，不保存、下一局自动清空。

### 客机通过 IPv4 加入

1. 打开「开始游戏 → 直连」。
2. 输入房主的局域网或 Radmin IPv4。
3. 端口填房主实际监听的 UDP 端口（局域网 / Radmin 默认 `27016`）。
4. 插件会跳过 U3DS A2S 查询，直接连该 UDP 端口。

Windows 防火墙必须允许 Unturned 在相应网络上使用 UDP `27016`。

### SakuraFRP / 公网穿透（不保证可达）

1. 房主建**一条** UDP 隧道：本地 `127.0.0.1:27016` → 远端 UDP 端口 `R`。
2. 客机直连页填 Sakura 节点域名或 IPv4，端口填 `R`。
3. 若只有随机域名，勾选「插件域名直连（FRP）」。
4. 插件不会把端口改成 `27016` 或计算 `R±1`。

可用性取决于第三方节点、NAT 和运营商，测试通过不构成公网保证。

## 端口说明

| 端口 | 原版用途 | 当前插件用途 |
|---|---|---|
| UDP 27015 | U3DS A2S 查询 | 客机不必再填（无 A2S 应答器） |
| UDP 27016 | 游戏连接 | Steam Networking Sockets 实际数据；局域网 / Radmin 客机填此端口 |

单端口语义：客机输入的端口既是 query 也是 connection。SakuraFRP 可以把任意远端 UDP 端口 `R` 映射到房主本地 `27016`。

## 从源码构建

需要 Windows、.NET SDK / MSBuild，以及项目 `Libs` 目录中的 Unturned / BepInEx / Harmony 引用。Git Bash 下不要写 `/t:Rebuild`（会被 MSYS 当成路径），用 `-t:Rebuild`。

```powershell
dotnet msbuild SteamP2PFriends.csproj -t:Rebuild -p:Configuration=Release
dotnet msbuild WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -t:Rebuild -p:Configuration=Release
```

产物：`bin/Release/SteamP2PFriends.dll`。正式全量验证请按[贡献指南](./CONTRIBUTING.md#构建与静态门禁)配置隔离宿主并使用`run-integrated.ps1`；原生类型初始化失败不能跳过或记PASS。

## 文档

- [ARCHITECTURE-REVIEW-0.2.4-Experimental.md](./ARCHITECTURE-REVIEW-0.2.4-Experimental.md)：2026-08-26 架构评审（里程碑，原样公开）。
- [audit/2026-09-04/ReviewRecheck-0.2.4.8-0933.md](./audit/2026-09-04/ReviewRecheck-0.2.4.8-0933.md)：评审逐条对照与可升级清单。
- [EXPERIMENTAL-ARCHITECTURE.md](./EXPERIMENTAL-ARCHITECTURE.md)：实验区架构说明（其中部分目录名已随结构基线迁移，以仓库实际布局为准）。
- [CONTRIBUTING.md](./CONTRIBUTING.md)：如何构建、报告问题和提交改动。
- [CHANGELOG.md](./CHANGELOG.md)：变更记录。
- [CONTRIBUTORS.md](./CONTRIBUTORS.md)：贡献者与工具声明。
- [VIBECODING.md](./VIBECODING.md)：人机协作与审计原则。

## 许可证

[MIT License](./LICENSE) © 2026 YU80Rice
