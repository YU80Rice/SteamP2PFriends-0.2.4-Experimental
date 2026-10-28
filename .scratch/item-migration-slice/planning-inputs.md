# Item Migration Slice 规格编制输入

- 日期：2026-10-07；插件 HEAD=`57e3c4e4411b4f8831f0bf6d489b9fea9922c47c`，SDK HEAD=`ea7b4973af5ba10f62baad2bfde36ab2e5b060eb`。
- 两条 research context 在额度恢复后只读补查；主 agent 只写规划文档，没有源码、测试、构建、部署或 Runtime PASS。
- `P/` 为本仓根；`S/` 为 `D:/Agent-工作目录/U3-SDK/Assets/Runtime/Assembly-CSharp/Unturned/`。

## 开房、准备与共享入口

1. HostManager 设置模式/会话后调用 Provider.host，随后启动地图加载、创建房间；活动标志不是 Item 就绪证明。原生 Provider.host 先建立网络，数组与物资点要到地图加载才出现，不能将全图准备提前到输入存在前。
   来源：`P/Platform/Host/HostManager.cs:143–157,239–245,350–363,467–484`；`S/Provider/Provider.cs:2518–2528`。
2. 当前运行门检查 Level.isLoaded，未包含全图 Item 准备；当前实际 Item 生成策略是 step 5 按需。旧 P0B3/P0B6 文件在项目编译项，但未找到当前手工注册/调用，不能盲目重新启用；“不编译”注释也不能替代项目清单事实。
   来源：`P/Platform/Host/HostManager.cs:470–471,1606–1613`；`P/Core/ControlPlane/MultiObserverShadowCoordinator.cs:212–217,254–264`；`P/SteamP2PFriends.csproj:130–131`；`P/Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationCriticalVerification.cs:1113`。
3. 生产共享引擎已注册 Resource/Collision；Item 应注册自身 Demand Policy 与 Domain Execution Port，复用投影/编排。M1 epoch 与 M2 transport 自分配 token 不能假定等于 Core Session Epoch/Connection Generation。
   来源：`P/Core/ControlPlane/MultiObserverShadowCoordinator.cs:126–165,686–713`；`P/Core/ControlPlane/Lifecycle/IDomainExecutionPort.cs:25–64`；`P/Adapters/Item/Patches/AuthoritativeItemGenerationGatePatch.cs:310–323`；`P/Adapters/Item/ItemObserverReplicationAdapter.cs:524–538`。
4. RetrySchedule.QuietAttempts 仅静默日志，AcquireRetry 无自动总次数上限；满足用户有限恢复须在共享政策/编排扩展中明确上限、失败稳态与恢复资格，保留旧领域默认行为，不复制 Item 私有通用重试机。
   来源：`P/Core/ControlPlane/Lifecycle/LifecyclePolicy.cs:5–38`；`P/Core/ControlPlane/Lifecycle/LifecycleOrchestrationEngine.cs:950–983,1487–1496`。

## 原生计时与动作结果

5. ItemData.lastDropped 公开只读/私有后备字段；ItemRegion.lastRespawn 可写，创建时均写 realtime。首次启计只调整预生成自然物品的有效基准，实际掉落保持自身时间，存活旧自然物品返区不归零。
   来源：`S/Managers/ItemData.cs:20–33`；`S/Managers/ItemRegion.cs:23,55`。
6. 来源须按创建路径区分；真实掉落在listen-host可能isDropped=false。扩展回调嵌套dropItem时，单一“当前生成中”标志或简单列表差分不足以可靠识别。Q11用户随后选择到期分类对齐U3DS：已核实实际掉落用掉落期限，不保留listen-host的上述期限差异；未知来源不能自动当可休眠自然物资。
   来源：`S/Managers/ItemManager.cs:202–209,816–823,885–907`；`S/Managers/ZombieManager.cs:806,819`；`S/Managers/AnimalManager.cs:478,489,498`；`S/Managers/ResourceManager.cs:214`；`S/Player/PlayerInventory.cs:1711,1802`；`S/Player/PlayerClothing.cs:1680`。
7. 原生 despawn 每次最多删一件，true 表示区域有物品而非过期已清完；respawn 的数量/距离检查含所有 ItemData，返回 true 也不必然创建成功。薄边界须报告实际删除/创建/无变化及计时效果，不能用 bool 猜准备/结算完成。
   来源：`S/Managers/ItemManager.cs:741–757,778–839`。
8. 空区须能准备完成并发合法空基线；Arena 的自然生成/周期函数早退及特殊 drop 路径保留。普通单机、Guest、U3DS 不启用新 SPF 运行政策。
   来源：`S/Managers/ItemManager.cs:736,769,849`；`S/Managers/LevelManager.cs:470–486`。

## 测试入口与工程缺口

用户已确认生命周期/同步高层入口：真实共享空间事实/投影/编排＋Item 执行端口，注入时间、原生状态/动作和发送薄边界；从准备、观察者进入/退出验证结算、基线、掉落到期和恢复顺序。07另用独立拾取请求入口，两者分别验收。

参考现有 `LifecycleOrchestrationEngineTests`、`ResourceProductionControlSeamTests` 和 `ItemObserverReplicationAdapterTests`；原生 IL/模型/网络效果仍须 StaticIL 与 1 Host + 2 Guest Runtime，不能由 fake port PASS 升级。

`GAP-C-P2-SOURCE-TIME`、`GAP-C-P2-NATIVE-RESULT`、`GAP-C-P2-REENTRANT-CREATE`、`GAP-C-P2-PUREMEMORY-INTEGRATION` 为实施必须解决的工程项，由各切片验收承担，不是批准延期。准备/首次启计必要条件归首票，不能等后继票修补；2026-10-08恢复数值/测试入口/七票图已获用户批准，见 [执行输入](execution-inputs.md)。

恢复载体补查及用户选定A见 [Item 局部恢复载体补查](recovery-carrier-facts.md)：原生定向提示/用户命令到主机，可能半包时离区返区；不新增原地reset。2026-10-08用户另批准命令名 `/spfitemretry` 及主规格默认值。

Q11–Q13之后的必要接线核对见 [Item 分帧结算与原生创建/删除/同步衔接](native-integration-flow.md)：新增/删除内含立即复制、首生成列表替换、背包写入前就绪门、取消待实例化、共享tick与覆盖补偿边界。Q14已批准恢复U3DS原生20米拾取距离校验，单列 [Item权威交互任务](../item-authoritative-pickup-distance/issue.md)，不将它作为租约能力。
