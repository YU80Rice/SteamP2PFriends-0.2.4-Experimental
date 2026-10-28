# 0025: Item 到期分类对齐 U3DS

用户在2026-10-08回答Q11：“这个问题我选择对齐U3DS”。本条明确实际掉落采用哪套消失时间，不改变已接受的首次需求启计、无人自然周期休眠及返区当前结果结算。

## 已接受的行为

- 地图首次生成/周期刷新产生的自然物品使用 `Despawn_Natural_Time`。
- 玩家丢弃，以及已核实的僵尸、动物、资源产生的实际掉落，按U3DS对应分类使用 `Despawn_Dropped_Time`；年龄从实际掉落产生时开始，清理不依赖区域访问或自然周期。
- 不保留listen-host因dedicated标志为false而将上述战利品套用自然期限的差异。生成来源与到期配置须分别明确，不能只靠既有isDropped推断来源，也不能把全部地面物品统一改成实际掉落。
- 对已核实原生路径作对应处理；未知第三方来源不被擅自归为自然物品或重置年龄。具体字段/旁挂元数据接线由实施确定，本决策不要求全局伪造dedicated。

## 原生依据与取舍

SDK `ItemManager.cs:745` 按isDropped选择两种期限；`ZombieManager.cs:806,819`、`AnimalManager.cs:478,489,498`、`ResourceManager.cs:214` 将 `Dedicator.IsDedicatedServer` 传入掉落分类参数。因此这些路径在U3DS与listen-host可使用不同期限。SDK根为 `D:/Agent-工作目录/U3-SDK/Assets/Runtime/Assembly-CSharp/Unturned/Managers/`。

选择U3DS分类与目标联机体验一致；当两种配置不同，上述listen-host战利品的存留时间会随之改变。没有改变配置数值、原生模式例外、自然补货资格或新增地面物品存档。

本条细化ADR0019当时未冻结的来源/配置问题；此前“尚未批准统一改旗”的历史记录保留。问询与用户原文见 [原生职责复用与按需运行的边界是什么](../../.scratch/map-c-stage-world-state-migration/ticket-runtime-duty-boundary/ticket.md) 的Q11。
