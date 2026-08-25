# SteamP2PFriends v0.2.4.4 (M4) 僵尸状态与外观同步测试对照清单

## 一、测试目标与架构范围
在 M3 阶段解决远端按需生成与滞回释放生命周期（Lifecycle）的基础上，**M4 阶段聚焦于快照（Snapshot）、外观（Appearance）与增量同步（Delta States）的完整一致性**：
1. **初始全量快照与外观绑定**：远端观察者首次进区或重连时，必须原子接收僵尸初始状态与完整服装外貌数据（`askZombies` + `sendZombieClothes`），杜绝“白模/裸模/默认光头”；
2. **周期性运动与攻击状态同步**：房主后台计算的僵尸 AI 寻路、转向、移动与攻击挥击必须通过 `SendZombieStates` 准实时投递给远端客机，杜绝“原地踏步/挠空气/隔空扣血”；
3. **单实体击杀与死亡布娃娃同步**：客机击杀僵尸立刻转入 `tellZombieDead` 物理布娃娃，多端状态实时一致；
4. **特殊变异种与 Mega 外观**：特种僵尸（爬行、冲刺、自爆、巨型 Mega）模型、缩放与技能表现完整无损。

---

## 二、测试二进制指纹与部署环境

| 项目 | 关键信息 |
|---|---|
| **待测版本** | `v0.2.4.4-experimental` (M4 `ZombieSnapshotAdapter`) |
| **测试构建配置** | **Debug**（包含 `[MultiObserver/M4-ZombieSnapshot]` 完整状态机事件） |
| **待测 DLL 源码路径** | `D:\Agent-工作目录\DevelopMyUNMultiplayerModAndModloader\SteamP2PFriends-0.2.4-Experimental\bin\Debug\SteamP2PFriends.dll` |
| **唯一有效 SHA-256** | **`AF6F887F6764BF4C57497D9D72FFA11ED6F23A914B88255DAB282D1F55148457`** |
| **文件大小** | `1,005,568 字节 (bytes)` |
| **自动化测试基线** | **`114/114 PASS`** |

### 🚀 部署核对：
1. 复制上述 `bin/Debug/SteamP2PFriends.dll` 覆盖到 **本机房主**、**虚拟机客机**、**用户测试客机** 的 `Unturned/BepInEx/plugins/SteamP2PFriends.dll`；
2. 在各机器 PowerShell 执行 `Get-FileHash <Unturned路径>\BepInEx\plugins\SteamP2PFriends.dll -Algorithm SHA256`；
3. **必须核对三端哈希 100% 为 `AF6F887F6764BF4C57497D9D72FFA11ED6F23A914B88255DAB282D1F55148457`**。

---

## 三、M4 核心验证场景清单 (Case ID: `M4-ZombieSnapshot-Acceptance`)

### 📋 场景 1：远区首次进入与外观完整性 (Initial Appearance & Clothes)
* **操作步骤**：
  1. 房主在城镇 A（如 Charlottetown）；
  2. 客机前往远端未探索城镇 B（如 Alberton / Stratford）；
  3. 客机进入城镇 B 视野范围。
* **🎯 核心观察点**：
  * 客机视野内生成的僵尸是否**全部穿着对应职业的衣服、裤子、帽子**（如警察、消防员、厨师、军人等）？
  * 绝不能出现大面积“默认裸体光头/紫色丢失材质”现象。
* **日志核对**：房主端出现 `[MultiObserver/M4-ZombieSnapshot] ... capability=ReliableEnqueueBaseline`。

---

### 📋 场景 2：僵尸运动与攻击动作平滑度 (Periodic Delta Movement Sync)
* **操作步骤**：
  1. 客机在远端城镇 B 引怪（让 2-3 只僵尸追逐客机）；
  2. 客机边后退边观察僵尸的跑动与攻击动作；
  3. 让僵尸击中客机 1 次。
* **🎯 核心观察点**：
  * 僵尸是否**平滑面向客机并朝客机奔跑**？（绝不能在原地滑步或踏步）；
  * 僵尸攻击时是否有明显的抬手挥击动画？客机掉血时僵尸位置是否确实就在客机近战范围内？（绝无“隔空被无形僵尸扣血”）。

---

### 📋 场景 3：击杀反馈与死亡布娃娃 (Kill & Ragdoll Sync)
* **操作步骤**：
  1. 客机使用近战武器或枪械击杀远端城镇 B 的僵尸；
  2. 观察僵尸死亡瞬间的表现与掉落物。
* **🎯 核心观察点**：
  * 僵尸被击杀后是否**立即倒地变成布娃娃 (Ragdoll)**，并在几秒后正常溶解消失？
  * 僵尸死亡掉落的物资（经验、物品包）是否正常刷在地上并可拾取？
  * 房主端日志正常记录死亡广播，无任何 RPC 崩溃。

---

### 📋 场景 4：多客机同区视角外观一致性 (Multi-Observer Appearance Uniformity)
* **操作步骤**：
  1. 客机 1 与 客机 2 同时进入城镇 B；
  2. 找到同一只僵尸（例如一只戴军帽穿迷彩服的特种僵尸）；
  3. 两人同时在各自屏幕上比对该僵尸的外观。
* **🎯 核心观察点**：
  * 客机 1 与客机 2 屏幕上看到的**该僵尸服装、帽子颜色、头部模型完全一致**；
  * 两名客机在场期间，僵尸数量与位置不发生瞬间闪烁或跳变。

---

### 📋 场景 5：特殊变异种与 Mega 巨型僵尸 (Special Boss / Mega Zombie)
* **操作步骤**：
  1. 客机前往有巨型僵尸的区域（如军营、雷达站、死区或大型城镇中的 Mega 刷新点）；
  2. 观察特种僵尸（爬行 Crawler、狂暴冲刺 Sprinter、自爆 Flanker / Burner）与 Mega 巨型僵尸。
* **🎯 核心观察点**：
  * 爬行僵尸是否呈现爬行动画、冲刺僵尸是否四肢着地奔跑、Mega 是否呈现巨大体型；
  * Mega 僵尸咆哮、投掷发光巨石技能在客机端同步正常，无模型撕裂。

---

### 📋 场景 6：离开区域后重入的外观重建 (Re-entry Full Resync)
* **操作步骤**：
  1. 客机离开城镇 B，移动至城镇外 200 米以上（等待至少 3 秒以触发 M3 滞回释放）；
  2. 客机再次返回城镇 B。
* **🎯 核心观察点**：
  * 城镇 B 重新按需生成新一批僵尸后，客机**重新获得全新的全套服装与外观数据**；
  * 不发生“第二次进入变成全员裸体”的退化缺陷。

---

### 📋 场景 7：客机断线重连外观自愈 (Reconnect Snapshot Resync)
* **操作步骤**：
  1. 客机 1 在城镇 B 与僵尸对峙时，直接 Alt+F4 强退或断开连接；
  2. 重新启动客户端并重新连接加入房间；
  3. 重连进入世界后观察城镇 B 的僵尸。
* **🎯 核心观察点**：
  * 重连后，客机 1 的旧连接代作废，系统为其重新分发 `ZombieSnapshot`；
  * 客机 1 重新看清所有僵尸的外貌与动作，不卡黑屏，不出现幽灵僵尸。

---

### 📋 场景 8：房主单人与本地分支非回归 (Host Local Baseline)
* **操作步骤**：
  1. 房主在本地出生地游玩、单机模式游玩；
  2. 击杀本地僵尸、观察本地变异种。
* **🎯 核心观察点**：
  * 房主本地渲染与原版 Unturned 单机体验 100% 无差异，帧率与击杀手感完全正常。

---

## 四、关键日志签名特征对照表

在测试过程中与测试完成后，各端日志中应重点确认以下特征：

| 日志签名 | 预期出现端 | 代表的正常含义 |
|---|---|---|
| `[MultiObserver/M4-ZombieSnapshot] registration verified capability=ReliableEnqueueBaseline` | 全端 | M4 快照适配器成功加载并完成能力绑定 |
| `[MultiObserver/M4-ZombieSnapshot] session-begin epoch=1` | 房主端 | 世界会话启动，快照账本清空并就绪 |
| `[MultiObserver/M3-Zombie] acquire-commit bound=... generation=...` | 房主端 | M3 生命周期按需生成成功 |
| `[Host] [P0-C-1/Zombie] OK Transpiler replacement=1/1` | 房主端 | 周期性状态同步底层 hook 正常生效 |
| `[Client] ... ReceiveZombieStates ...` | 客机端 | 客机正常接收周期性位置与动作同步包 |

⚠️ **严禁出现的异常日志**：
* `DIAGNOSTIC BUILD INVALID`
* `NullReferenceException`（尤其在 `sendZombieClothes` 或 `askZombies` 处）
* `stale delta received`

---

## 五、测试后收集与交付

测试完成后，请通过 UMM 启动器分别导出：
1. **房主主机 UMM 诊断包**（`UMM-诊断包_YYYYMMDD_HHMMSS`）
2. **虚拟机客机 UMM 诊断包**
3. **用户测试客机 UMM 诊断包**

将诊断包路径提供给我，我将立即为你出具不可篡改的 M4 阶段全量运行验收审计报告！祝 M4 测试顺利！
