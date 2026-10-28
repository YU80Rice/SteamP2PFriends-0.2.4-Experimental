# 无人进度补算的原生规则补查

- 日期：2026-10-07；只读核对 SDK HEAD=`ea7b4973af5ba10f62baad2bfde36ab2e5b060eb`，未构建、未改 SDK 或生产实现。
- 以下是源码观察，不是新 Evidence Class，也没有 Runtime PASS。
- `SDK/` 指 `D:/Agent-工作目录/U3-SDK/Assets/Runtime/Assembly-CSharp/Unturned/`。
- 服务于 [原生职责复用与按需运行的边界是什么](ticket.md) 的后续问询；事实不替代用户决策。

## 发电机与供电依赖

1. 原生 Generator asset 的 `Burn` 是每单位燃油的秒数；Generator Update 仅在 elapsed > burn 时扣一次并重置 lastBurn；OnEnable 重置相位。直接用 floor(时长/burn) 不等于逐帧原生执行的所有细节。
   来源：`SDK/Bundles/ItemGeneratorAsset.cs:19-21,61`；`SDK/Interactable/InteractableGenerator.cs:254-283`。
2. 开关、余油、距离、车辆归属、全局供电和替代发电机影响 updateWired；油泵有电时按两秒阈值增加燃料，无电则重置 lastDrilled。只算发电机最终余油不能确定油泵实际有电时段。
   来源：`SDK/Interactable/InteractableGenerator.cs:165-246`；`SDK/Interactable/InteractableOil.cs:158-173`。
3. Safezone/Oxygenator 的供电变化注册或注销作用泡；这说明部分供电结果还有其他空间影响。是否允许推迟到重入，仍未裁定。
   来源：`SDK/Interactable/InteractableSafezone.cs:25-49`；`SDK/Interactable/InteractableOxygenator.cs:25-49`。

## 作物、天气与雨桶

4. Farm 保存 planted 时间戳，成熟条件是 Provider.time 与 planted 的差达到 growth；权威成熟无需另造每帧累计进度。
   来源：`SDK/Interactable/InteractableFarm.cs:15-25,50`。
5. Farm 在 POST_DRIZZLE 且资产允许雨水、向上 BLOCK_WIND 射线无遮挡时 updatePlanted(1)，不是按降雨分钟线性加速；禁用会退订事件。
   来源：`SDK/Interactable/InteractableFarm.cs:136-168`。
6. RainBarrel 只有空/满 bool，同一事件与无遮挡判定通过后直接置满；没有可按“降雨速率”累加的接水量。
   来源：`SDK/Interactable/InteractableRainBarrel.cs:11-21,37-64`。
7. POST_DRIZZLE 是雨开始渐出时的关键事件；LightingManager 发布原生及 ModHook 天气事件。可以研究记录少量事件历史，但这不证明历史遮挡可以只用返区时现状替代。
   来源：`SDK/Level/LegacyRainComponent.cs:9-32`；`SDK/Managers/LightingManager.cs:58-69`。

## 时钟与物品到期

8. Provider.time 是 _time 加 realtime 差的整秒转换，setter 还调整 offset；不能直接与现实日期或其他时间源相减。
   来源：`SDK/Provider/Provider.cs:6022-6031`；`SDK/Managers/LightingManager.cs:367`。
9. 原生 Item 消失比较 realtime-lastDropped 与 Dropped/Natural 配置，使用大于阈值，每次只删一个，区域轮询存在巡检延迟。用户已选无人期间到期移除，但尚未要求毫秒级瞬时删除；Arena 另有例外。
   来源：`SDK/Managers/ItemManager.cs:734-754,1201-1240`。

## 待问询与未查边界

- 中间事件顺序是否保留：例如离开 30 分钟，第 10 分钟燃油耗尽，则油泵供电不能按完整 30 分钟结算。
- 休眠期间是否允许少量关键事件的必要判定/记账，而不持续执行每帧区域模拟。
- 原生消耗速率与帧延迟/相位细节的精度边界；全供电消费者、动态遮挡历史、第三方回调、跨区依赖、会话结束结算仍未查清。
- 本补查未证明所有组件可以休眠，也未批准一般化补算引擎、特殊持续需求或新的同步协议。
