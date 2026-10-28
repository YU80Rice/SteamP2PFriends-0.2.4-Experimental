# LMN 命名频道接入事实补查

- 日期：2026-10-07；只读源码调查，无构建/安装/部署或 Runtime PASS；复核状态见 [审查链](review-chain.md)。
- `P/` 为本仓根；`L/` 为 `D:/Agent-工作目录/DevelopMyUNMultiplayerModAndModloader/LaunchMultiplayerNet/`；`S/` 为 `D:/Agent-工作目录/U3-SDK/Assets/Runtime/Assembly-CSharp/`。
- 插件/SDK HEAD 与本研究票基线一致。LMN HEAD=`e50082f983ac7cd33e008cca1a1a1dc3d9498d3f`，当前脏树迁移根目录类至未跟踪分层目录；HEAD/工作树均声明 v5，新增主线程/强类型/RPC/Session能力不能视为已冻结发布契约。未核验部署 DLL 与双端版本。
- 适用范围纠正：本调查保留为 SDK 背景资料，不作为 SPF Item P2 必须接入 LMN 的依据；见 [0021: SPF 原生世界同步迁移不自动成为 LMN 消费方](../../../docs/adr/0021-spf-native-world-sync-and-gameplay-mod-channel-boundary.md)。

## 依赖与可复用 API

1. 本仓完整 csproj Reference 段及生产 Core/Platform/Adapters 未检出实际 LMN 接入；入口仅见 LIT SoftDependency。
   来源：`P/SteamP2PFriends.csproj:38–100`；`P/SteamP2PFriendsPlugin.cs:26–27`；限定当前生产路径的 rg 检索（排除 Develop-Stage）。
2. 当前 LMN 双端提供 RegisterNamedServerHandler/ClientHandler，BuildNamedMessage，SendNamedToServer/Client 与 BroadcastNamedToAllClients；发送默认可靠且返回 void。注销需保留实际委托作 compare-and-remove；同方向重复注册抛异常。
   来源：`L/Routing/ModTransport.cs:246–266,579–593,832–835`；`L/Routing/NamespacedTransport.cs:95–155`；`L/NAMED_CHANNEL_STANDARD.md:110–172,192–196`。
3. 命名字符串归一为 trim/lowercase、ASCII [a-z0-9._-]、最长96字节；payload为1..61440字节，框架未提供分片/重组。广播无 Item 空间收件人筛选，Host向自身可本地回环。
   来源：`L/Routing/NamespacedTransport.cs:16–17,171–180,224–236,262–303,374–388,415–420,468–477`；`L/Routing/ModRouter.cs:53–68`。
4. 脏树普通命名 handler 复制 payload 进入主线程 ConcurrentQueue；默认每轮最多500回调，队列无总容量上限。强类型 INetworkPacket 接口可复用字段编解码形式，但这是当前脏树 API。
   来源：`L/Routing/NamespacedTransport.cs:424–465`；`L/Core/MainThreadDispatcher.cs:16–42`；`L/Routing/ModTransport.cs:270–310,597–620`。

## 身份、完成与晚到边界

5. sender来自 transport SteamID，解析失败可为Nil；没有自动世界成员、GameplayReady、区域、距离/库存/权限校验。脏树连接/频道 token-bucket 是限流，不是业务授权；旧规范的“无限流”描述落后于此工作树。
   来源：`L/Routing/ModRouter.cs:109–126,174–185`；`L/Sessions/ConnectionRateLimiter.cs:22–34,44–77`；`L/NAMED_CHANNEL_STANDARD.md:196`。
6. 命名帧没有 SessionEpoch/ConnectionGeneration/RegionGeneration；RpcEnvelope 仅有 correlationId、请求标志和payload。Session对象可挂自定义状态，但不自带世界代次。Hello/ACK实现存在，未检出生产 BuildHelloPacket调用，命名收发也未用握手完成/频道支持作门控。
   来源：`L/Routing/ModRouter.cs:18–24,53–68`；`L/Rpc/RpcEnvelope.cs:11–26`；`L/Sessions/ConnectionSession.cs:20–34,64–103`；`L/Protocol/HandshakeProtocol.cs:24,136–182`；`L/Routing/NamespacedTransport.cs:42–86,159–236,322–371`。
7. 缺transport、超限和异常可只记录后返回；命名Send不调用EnsureOperational。可靠标志及void返回都不是业务应用ACK，SDK底层失败也可能只记日志。
   来源：`L/Routing/NamespacedTransport.cs:159–236,402–411`；`L/Routing/ModTransport.cs:45–58`；`S/NetTransport_SteamNetworkingSockets/TransportConnection_SteamNetworkingSockets.cs:126–147`。
8. 脏树RPC有请求关联/超时/取消，未决表仅按correlationId匹配，未绑定频道/世界代次，也无业务幂等。RPC响应可在普通主线程队列前完成Task，不能视作全部Unity线程安全。
   来源：`L/Routing/ModTransport.cs:624–688`；`L/Routing/NamespacedTransport.cs:338–359`；`L/Rpc/RpcManager.cs:29–45,103–148`。
9. 注销只移字典，已排队闭包保留旧handler；断线清Session却不清dispatcher/RPC，只有全局Shutdown清全部。领域须拒绝旧代次的晚到包和异步结果。
   来源：`L/Routing/NamespacedTransport.cs:131–155,322–371,424–465`；`L/Sessions/ConnectionSessionManager.cs:179–210`；`L/Routing/ModTransport.cs:108–129`。

## 背景候选（仅在未来另行决定接入时适用）

若未来另行决定消费 LMN，消费方仍需负责空间收件人、代次、快照/增量衔接、有界组装、应用确认与恢复。原生字段/本地应用逻辑可作为适配研究对象，但私有 NetPak serializer 与依赖 ClientInvocationContext 的接收方法不是现成 LMN 接口（`S/Unturned/Managers/ItemManager.cs:528–716`）；这不构成当前 SPF Item P2 改换传输的要求。

`GAP-C-P2-LMN-CONTRACT` 已因不属于当前 Item 原生同步迁移范围而撤回，不是通过 SDK 验收关闭。若未来另行决定消费 LMN，才需锁定对应 API/产物与接入验收。已有 [Ticket 4: 命名通信通道 (LaunchMultiplayerNet) 与双端路由规范](../../map-top-level-architecture-blueprint/ticket-04-channel-transport-routing/issue.md) 仍保持 HITL，不作为本 P2 的强制 LMN 接入阻塞。
