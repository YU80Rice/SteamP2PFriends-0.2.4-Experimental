# SteamP2PFriends 0.2.4 Experimental

> Unturned listen-host 联机插件的实验线：无 U3DS 开房，并正在把世界状态从旧补丁切到 Multi-Observer 区域租约架构。

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](./LICENSE)
[![Version](https://img.shields.io/badge/version-0.2.4.8--experimental-blue.svg)](./Build/Version.props)
[![Status](https://img.shields.io/badge/status-experimental--no--release-orange.svg)](./ARCHITECTURE-REVIEW-0.2.4-Experimental.md)

> **版本来源**：徽章与「当前版本」表中的版本号以 [`Build/Version.props`](./Build/Version.props) 为唯一来源；展示值须与其一致。
>
> **不是稳定版，也没有 GitHub Release。** 本仓库默认分支是 `codex/structure-baseline-0.2.4`。玩家若只想用已归档的旧实现，请看 [YU80Rice/SteamP2PFriends](https://github.com/YU80Rice/SteamP2PFriends)（0.2.3，只读、已停止开发）。两个 DLL 不得同时部署。

## 这是什么

房主从原版单人地图开多人房间，客机用 SteamID 或 IPv4 加入，不需要安装或启动 U3DS。

两条联机路径并存：

- **SteamID P2P**：客机在原版直连地址栏输入房主个人 SteamID。
- **IPv4 直连**：真实局域网或 Radmin LAN 等虚拟局域网；客机输入房主 IPv4 和共享端口。

IP 只负责寻址。玩家身份、审批与白名单始终使用 Steam Networking Sockets 握手得到的 SteamID。

0.2.4 相对 0.2.3 的目标不是再堆一层补丁，而是把观察者需求并集、区域租约和单一权威写入真正接到生产路径上。2026-08-26 的 [架构评审](./ARCHITECTURE-REVIEW-0.2.4-Experimental.md) 判定当时的控制面是死代码空壳；Resource 域已按该评审第 8 项纲领走通 Acquire → Release，其余域仍在旧路径上。逐条进度见 [ReviewRecheck](./audit/2026-09-04/ReviewRecheck-0.2.4.8-0933.md)。

## 当前状态（请按这里判断能不能玩）

| 项目 | 值 |
|---|---|
| BepInPlugin / AssemblyVersion | `0.2.4.8` |
| 通道 | Experimental，无发布标识 |
| 已验证 | Resource 域 Multi-Observer 三端 Runtime（区域租约、采伐、滞回释放）；SteamID P2P 与 IPv4 直连开房/审批隔离 |
| 已知未修（玩家会碰到） | 僵尸被打死后不重生（ticket01 已实现，待 Runtime 验收）；地面掉落物不按专用服节奏消失/再生；SteamID 路线丢密码；`SteamID:端口` 粘贴会静默失败 |
| 分发 | **只从源码构建**；本仓库不提供 Release zip |
| 旧实现 | 0.2.3 已归档，只作来时路，不再开发 |

静态测试全绿不等于运行时可用。本仓库把证据分成 PureMemory / StaticIL / BuildArtifact / Runtime 四类，互不升级替代；没有真实多机日志，不得宣称 Runtime PASS。本轮 Listen-Host Dedicated Gate / Join Routing 的静态证据闭环后，其 Runtime 状态仍为 `PENDING`（共享验收票：`.scratch/listen-host-join-routing-runtime-acceptance/issues/05-shared-1h2g-runtime-acceptance.md`）；`Develop-Stage/` 与 `Archive/` 内的旧版本运行记录属历史运行证据（不属于 0.2.4.8 当前验收）。

## 安装（从源码）

1. 房主和所有客机安装**同一构建**的 BepInEx 与本插件。
2. 按下方「从源码构建」得到 `SteamP2PFriends.dll`。
3. 放到：

```text
Unturned/
└── BepInEx/
    └── plugins/
        └── SteamP2PFriends.dll
```

4. 用 `certutil -hashfile SteamP2PFriends.dll SHA256` 核对三端哈希一致后再进世界。

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
2. 在顶部地址栏粘贴房主个人 SteamID（不要带 `:端口`；当前分类器还剥不掉这个后缀）。
3. 点击连接。

插件只接管有效的 Steam 个人账户 ID；U3DS Server Code 仍交给原版。房间若设了密码，请先走 IPv4 直连，或等密码修复票——SteamID 路线目前会丢掉密码。

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
./WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe
```

产物：`bin/Release/SteamP2PFriends.dll`。

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
