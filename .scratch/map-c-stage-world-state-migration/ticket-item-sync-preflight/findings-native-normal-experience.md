# U3DS Item 正常加载与异常兜底的区别

- 日期：2026-10-07；SDK HEAD=`ea7b4973af5ba10f62baad2bfde36ab2e5b060eb`，research agent 只读核对；无构建/实机验证或 Runtime PASS。
- `S/` 为 `D:/Agent-工作目录/U3-SDK/Assets/Runtime/Assembly-CSharp/Unturned/`；复核进度见 [Q8/Q9 审查链](../ticket-runtime-duty-boundary/review-q8-q9.md)。

## 服务器开图先准备数据

1. ItemManager.onLevelLoaded 同步建立全图数组，Dedicated 同步逐区 generateItems；准备的是 ItemData，不是全图模型。
   来源：`S/Managers/ItemManager.cs:847–907,920–950`。
2. Level 同步调用 onLevelLoaded，经过后续加载步骤才标记 isLoaded=true；Dedicated Provider.listen 在未加载时返回，不处理玩家网络包。因此正常服务器网络处理前，已注册 Item 初始化回调和全图数据生成完成。
   来源：`S/Level/Level.cs:1989–1995,2041–2042`；`S/Provider/Provider.cs:3797–3811`。

## 玩家入服与日常进区是另一条流程

3. 客机完成内容/地图加载后发 ReadyToConnect；accept 创建玩家、发送 Accepted/global state 并初始化。global state 列表没有 Item 全图快照，Item 由后续玩家区域回调发送附近状态。
   来源：`S/Provider/Provider.cs:1589–1621,4749–4757,4876–4957`；`S/Player/PlayerMovement.cs:1966–1985`；`S/Player/PlayerQuests.cs:2613–2626`；`S/Managers/ItemManager.cs:1076–1078,1015–1038`。
4. ITEM_REGIONS=1，基线覆盖玩家所在区域及周边，非等玩家脚踏进每个物资点才启动下载。askItems 同步可靠分包；接收进入模型队列，由 Update 按预算逐步实例化，未承诺固定网络/模型延迟。
   来源：`S/Managers/ItemManager.cs:44,642–684,584–634,1022–1026,1122,1150–1165`。

## 原生不设每区完整 Item ACK 等待界面

5. InteractableItem 的 hint 显示物品名，use 直接调用 takeItem；交互基于实际模型射线，未见整个区域 Item 基线 ACK 门。take 请求是 Unreliable，成功后的权威背包更新与 Destroy 另有可靠路径，不能将所有原生拾取消息称为 Reliable。
   来源：`S/Interactable/InteractableItem.cs:18–43`；`S/Interactable/Interactable.cs:18–26`；`S/Player/PlayerInteract.cs:170–176,301–305,482–486`；`S/Managers/ItemManager.cs:137–151,371–381`。
6. 全局加载画面等待 Assets/Provider/Level/Player loading 状态，Player loading 是 life/inventory/clothing，不包含地面物资完整基线或全图模型显示。
   来源：`S/Loading/LoadingUI.cs:923–930`；`S/Player/Player.cs:219–222`。

## 解释边界

源码所表达的正常流程是：服务器已有数据，区域变化自动发送，客机逐步显示合法物品并按原生交互；没有每次进区手动确认同步或等全区域物品模型齐全的门。尚未逐帧追踪各 Start/simulate 的实机顺序，不能承诺进区零延迟或固定耗时。

插件基线 Prefix 拒绝后的恢复缺口属于迁移需处理的异常路径，不是 U3DS 日常体验政策。用户 Q8/Q9 的等待/提示/重试只作为未就绪、异常或超时兜底，不据此重建原生传输或统一拦截正常物资交互。Q10 对照的是服务器数据初始化阶段，具体 SPF 开房时机仍需核对。
