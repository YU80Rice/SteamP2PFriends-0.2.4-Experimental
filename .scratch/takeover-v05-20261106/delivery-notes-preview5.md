# 0.2.4.11-preview5：这份数据说明了什么

状态：草稿（等待 t74/t75 双轴复审结论后定稿，DLL 哈希与复审结论在打包时回填）
证据来源：主机 `UMM-诊断包_20261107_191410`、客机 `UMM-诊断包_20261107_191157`、Bug1 产物 `bug1-20261107T111121Z`

---

## 一、先说结论边界

这份数据能证明三件事，不能证明其余任何事。请按这个边界读下面的每一节。

| 结论 | 证据强度 | 依据 |
| --- | --- | --- |
| Bug3（队伍跨局丢失）已修复，主机开局即可恢复 OWNER | **强：生产日志直接记录** | 主机 `LogOutput.log` 的 `Gate` / `deferred native Quests.load executed once` / `native group state published after restore` 三段串联，两局各一次 |
| 主机侧看到的远端玩家，位置**没有任何第二路平滑**，模型与权威逐帧同拍 | **强：数据 + SDK 读码互证** | 渲染帧 `v_pos == a_pos` 5737/5737；`lerp_factor` 中位 774.64 且钳位恒 1；`remote_input_tick` 140.26 秒内变化 0 次；SDK 中该分支的 Lerp 被 `IsLocalPlayer` 包住 |
| 权威位置按原生 12.5 Hz 步进（不是丢包、不是延迟） | **强：独立脚本复算** | 相邻位移间隔中位 80.202 ms；12.5 Hz 恰等于原生 `count % SAMPLES(4)` 节流（50 TPS ÷ 4） |
| 用户看到的"顿挫"由上述机制造成 | **中：推断，非 A/B 实验** | 机制、数值、双向不对称三者自洽，但没有做过"补上平滑后对比"的实验 |
| 主机侧存在真实的 M1 停段回补事件 | **未证** | 本次 trace 经修订后判 `AUTH_STEP_LOCKED`，`strict_m1_events=0` |
| 客机侧（客机看主机）是否有同类问题 | **未测** | 本轮采集只在主机侧，客机无仪表 |
| Bug2（开枪声） | **未动** | 本轮无任何相关改动 |

一句话：**Bug3 修好了；Bug1 的机制被数据指向了，但修复本身还没有做。**

---

## 二、Bug3：队伍跨局丢失

### 机制

Unturned 的队伍归属存在**角色存档**（`PlayerQuests.save`），`Groups.dat` 只存队伍 ID / 名字 / 成员数。

P2P listen host 的本地玩家在 `GroupManager` 组表加载完成**之前**就执行了 `PlayerQuests.load()`。此时组表为空，`loadMainGroup()` 查不到组，把 `groupID` 清成 none、`groupRank` 降为 MEMBER。组表随后才由 `Level.onLevelLoaded` 的回调加载——但已经被清掉的字段不会回填。

单人 / 专用服务器不踩这条路径，因为世界先加载完组表、再生成玩家。

### 修法

插件侧把这一次原生 `Quests.load()` 延后到组表就绪后执行**恰好一次**，并配套写安全门：若该次 load 没有成功完成，就**拒绝本局保存**，避免把"被清空的状态"写回存档。

- 没有替换原生入口；
- 没有手写存档解析；
- 没有用 `ServerAssignToGroup` 冒充恢复（那会 members++）。

### 日志证据

```
[P2P-GroupRecovery] gate ... eligible=true | token=1 epoch=2 map=PEI | decision=Deferred
[P2P-GroupRecovery] deferred PlayerQuests.load
[P2P-GroupRecovery] deferred native Quests.load executed once
[P2P-GroupRecovery] native group state published after restore (group=...9548 rank=OWNER)
```

第二局（`token=3 epoch=4`）同序列，恢复 `group=...9697 rank=OWNER`。

### 如何自己复核

主机的 `LogOutput.log` 里搜 `P2P-GroupRecovery`，应能看到 `eligible=true` → `Deferred` → `executed once` → `group state published` 四步；若只看到 `decision=NotEligible`，说明该局没有进入恢复路径。

---

## 三、Bug1：主机看客机为什么顿挫

### 数据

同一份 trace（18417 行）：

| 字段 | 值 | 含义 |
| --- | --- | --- |
| `v_pos == a_pos` | 5737/5737（渲染帧）；可比口径 5643/11379 | 可见载体与权威 transform 逐帧完全相同，**没有任何插值偏移** |
| `lerp_factor` | 中位 774.64，钳位恒 1 | 原生插值因子早已饱和，即便执行也等于瞬间贴合 |
| `remote_input_tick` | 首末同值 164.093，140.26 秒变化 0 次 | 远端时钟不刷新，插值没有时间基准 |
| 相邻位移间隔 | p50 80.202 ms / p90 123.106 ms | 12.46 Hz —— 原生 12.5 Hz 步进 |
| 单步位移 | 3D 0.3600 / 0.5600 U；XZ 0.3592 / 0.5584 U | 与 `native_speed_cap × RATE(0.08)` 一致 |

### 解释

同一份 12.5 Hz 的位置样本：

- **客机看主机**：走客户端路径，`NetworkSnapshotBuffer` 把样本插值成逐帧连续 → 平滑；
- **主机看客机**：走服务端路径，没有 snapshot buffer，远端模型每帧 `localPosition = 0` 直接继承权威 root → 12.5 Hz 的阶梯。

所以这不是"主机收到的客机数据更少"，而是**同一份数据一边插值、一边不插值**。帧率与延迟不是变量——这解释了为什么 75 FPS、0 ms 延迟下依然可见。

### 为什么还没修

补平滑不是加一行 LateUpdate：

- 远端 `Third` 下挂的 `Skeleton` 自带 Collider，搬 `Third` 或骨骼可能改到命中体；
- `Model_0/1` 的 `SkinnedMeshRenderer` 引用同一套骨骼，只挪 renderer 不能保证骨骼跟随；
- 复制整个 `Third` 再删脚本不可行（`Awake` 已执行）。

架构师给的候选是**独立 view proxy**（只含 Transform / Renderer / 视觉骨架的新根，原骨骼、碰撞、权威坐标全部保留），代价量级为 2 客机 75 FPS 下约 4800–9600 次骨骼 pose 复制/秒。这属于新增呈现层，按裁决不进 0.2.4.11，作为后续默认关闭的实验候选。

---

## 四、判定器曾经误报（已修订）

preview4 的 `result.json` 曾判 `M1StallCatchup`。经复核，该候选窗（11562–12215）内：

- 654 个渲染帧位置跨度**全为 0**；
- 那个 0.00999 的"跨度"全部来自 SIM 样本（首条 `BEFORE` 的纯 Y 微跳），渲染侧贡献为 0；
- release 帧 12216 的水平位移 0.3416 U，已超过该帧速度边界 B≈0.1188 U。

即：**该窗口没有任何"渲染落后于权威"的证据**，判 M1 属于阈值假阳性。

修订后规则 `M1-XZ-RELATIVE-v2`：

1. M1 必须在同一 `RENDER_READY` 相位内观测到真实方向性落后，并在释放时朝权威方向收敛——A/V 恒重合的窗口**数学上不可能**判 M1；
2. 走/跑候选改用水平 XZ 投影，2 cm 绝对事件尺度门（纯 Y 位移不参与，Y 数据仍保留给跳跃/落地）；
3. `eps`、原速度边界 B、完整性门一律不动；SIM 三维 span 降级为旁证。

用同一份 trace 回放，原候选现判 `AuthStepLocked`，`strict_m1_events=0`；原始 `result.json` 未被覆盖，保留当时的判定以作审计。

**注意：修判定器不等于修顿挫。** 零 M1 只说明"这份数据里没有停段回补"，不说明"主机看客机已经平滑"。

---

## 五、仍然缺的证据

| 缺口 | 需要什么 |
| --- | --- |
| 真实 M1 是否存在 | 一次 1H+1G 采集，走到会导致停段回补的场景（高丢包/卡顿/传送） |
| 客机侧是否同类问题 | 客机侧仪表（当前没有），或 1H+2G 拓扑 |
| 补平滑是否真的改善观感 | 默认关闭的实验候选 + A/B 对比 |
| 丢包时的行为取舍 | 冻结 vs 吸附，需要实机主观对比 |

---

## 六、复现与校验

- 静态分析脚本：`.scratch/takeover-v05-20261106/t72_trace_analysis.py`（630 行，纯 stdlib）
- 固定输出：`.scratch/takeover-v05-20261106/t72_trace_analysis.out.txt`
- 判定规则裁决：`.scratch/takeover-v05-20261106/architecture-decision-t71-m1-relative-motion.md`
- 判定器实现报告：`.scratch/takeover-v05-20261106/bug1-m1-xz-relative-v2.md`

DLL 与压缩包哈希见同目录 `sha256.txt`。
