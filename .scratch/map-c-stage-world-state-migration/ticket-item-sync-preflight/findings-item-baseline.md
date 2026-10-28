# Item 基线、增量与恢复事实补查

- 日期：2026-10-07；插件 HEAD=`57e3c4e4411b4f8831f0bf6d489b9fea9922c47c`，SDK HEAD=`ea7b4973af5ba10f62baad2bfde36ab2e5b060eb`；均由 research agent 实际核对。
- 只读源码观察，不是新增 Evidence Class，无构建/测试或 Runtime PASS；复核状态见 [审查链](review-chain.md)。
- `P/` 为本仓根；`S/` 为 `D:/Agent-工作目录/U3-SDK/Assets/Runtime/Assembly-CSharp/`。

## 现有生产与代次语义

1. Item SPI 的 Acquire/Release、观察者进出与 Tick 为空，M1/M2 实际由原生 Harmony 入口驱动。M1 的一次生成事务不等于每次掉落/拾取/周期变化的世界 revision；当前 authoritativeGeneration 直接取 epoch。
   来源：`P/Adapters/Item/ItemDomainAdapter.cs:29–62`；`P/Adapters/Item/Patches/AuthoritativeItemGenerationGatePatch.cs:62–92,310–323`。
2. M2 按观察者、连接代次、区域和 M1 generation 去重基线；明确不拥有已有原生拾取删除、玩家/僵尸掉落增量。
   来源：`P/Adapters/Item/ItemObserverReplicationAdapter.cs:176–222,267–272`。

## 拒绝、异常与重试不同

3. 原生 step 5 先写 loaded=true，再调用 askItems。M2 注册未就绪/解析失败可直接拒绝且不回滚该 bit；另一些拒绝分支回滚 bit，但 Prefix false 只抑制 askItems，未修改 canIncrementIndex。正常返回后 PlayerMovement 推进 step，因此回滚 loaded 不等于自动重试；未见 Item SPI 独立恢复调度。
   来源：`S/Unturned/Managers/ItemManager.cs:1026–1038`；`P/Adapters/Item/ItemObserverReplicationAdapter.cs:368–388,413–415`；`P/Adapters/Item/Patches/ItemManagerRegionSyncPatch.cs:540–551`；`S/Unturned/Player/PlayerMovement.cs:1866–1875`。
4. 异常分支 Abort 后重抛，控制流可能在 increment 前退出，下次 simulation 再进同一步；这是源码推论，未实机复现拒绝/异常恢复。
   来源：`P/Adapters/Item/Patches/ItemManagerRegionSyncPatch.cs:597–605`；`P/Adapters/Item/ItemGenerationAuthorityAdapter.cs:83–94`。

## 原生发送返回不证明成功入队或应用

5. 原生 askItems 可靠分包，空区域也发第0包；协议未见总包数/结束提交包/应用 ACK。M2 在原生调用返回后 Commit。SDK transport 的 void Send 对 Steam 非 OK 仅 DebugLog，上层不获失败；P2P redirect 只是换底层 API，诊断也在 void 返回后记 send-success。
   来源：`S/Unturned/Managers/ItemManager.cs:642–684`；`P/Adapters/Item/ItemObserverReplicationAdapter.cs:418–442`；`S/NetTransport_SteamNetworkingSockets/TransportConnection_SteamNetworkingSockets.cs:126–148`；`P/Core/Patches/SteamUserP2PRedirectPatch.cs:259–268`；`P/Core/Patches/ClientMethodLoopbackPatch.cs:366–372`。
6. 因此 ReliableEnqueueBaseline 是现有能力名称；本次进一步收窄旧事实描述：Commit 可证原生调用完成且采用 Reliable 标志，不能证明每包 Steam 已接受入队，更不能证明完整客机应用。

## 接收与排序边界

7. 第0包置 isNetworked 后只排队模型，后续 Update 分预算实例化。未 networked 的新增/删除增量会被丢弃，但若随后基线包含正确现状，这不必然丢物。盲目重发已 networked 区域的原生基线也不是可靠恢复：第0包会被忽略，后续包可能追加。
   来源：`S/Unturned/Managers/ItemManager.cs:253–282,535–537,584–634,1150–1165`；`S/Unturned/Regions/Regions.cs:263–275`。
8. 常规主线程 askItems 同步分包、drop/take 同步改仓库；同可靠 lane 有序。不能把源码窗口直接说成已复现乱序。其他补丁重入、实际 transport、发送失败、跨代次和混合原生/LMN消息顺序仍需验证。
   来源：`P/Adapters/Item/ItemObserverReplicationAdapter.cs:369,421`；`S/Unturned/Managers/ItemManager.cs:209–212,371–381,642–684`；`S/Unturned/Provider/Provider.cs:3545–3559,3579–3591,6291–6315`；`D:/Agent-工作目录/U3-SDK/Assets/com.rlabrecque.steamworks.net/Runtime/autogen/isteamnetworkingsockets.cs:533–537`。

原生拒绝恢复、单 Writer、主线程提交、旧状态隔离和增量衔接需要明确，但这些证据限制不自动要求重建 Item 协议或新增应用 ACK。同步完成定义按所复用原生链的真实语义与验收目标制定；仅对迁移实际改变的玩家体验提问，不能先引入传输替换再制造等待需求。范围纠正见 [0021: SPF 原生世界同步迁移不自动成为 LMN 消费方](../../../docs/adr/0021-spf-native-world-sync-and-gameplay-mod-channel-boundary.md)。
