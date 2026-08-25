# 0.2.4 Multi-Observer 实验区架构规范与全域规划

## 1. 核心架构范式：MC 局域网式区域租约模型 (Minecraft LAN-style Chunk Lease)

本架构为 Unturned 提供基于 **Client-Host（房主进程兼具服务端权威与本地渲染）** 的高性能动态世界模拟方案。

### 运行机制：
1. **观察者感知**：所有进入世界的玩家（房主本地玩家、已授权客机、Route B 待审核客机）共同构成 `WorldPresenceObserverSet`。
2. **需求租约分配**：任一观察者进入某区域 $\rightarrow$ 核心租约引擎 (`RegionLeaseCoordinator`) 检测到该区域需求 `0 -> 1` $\rightarrow$ 分配功能租约 (`Acquire`)，触发对应 `DomainAdapter` 执行原生权威逻辑（生成/物理/AI/碰撞），并通过原生可靠网络路径向相关客机入队初始快照。
3. **多观察者并集保活**：只要该区域需求数 $> 0$（有多名玩家在场），保持功能活跃 (`Hold`)，不发生重复生成或实体换代。
4. **有界滞回释放**：最后一名观察者离开该区域（需求归 0） $\rightarrow$ 核心引擎开启 **2 秒滞回缓冲期 (`Hysteresis Release`)**；超时且无新玩家进入，验证会话代与区域身份后安全释放 (`Release`/`destroy`)。
5. **表现与功能分离**：房主后台仅对远端客机区域租用必要的功能能力（AI Tick、物理碰撞查询、动画 Transform 提交、网络快照），不渲染远区网格、LOD、粒子和音效。

---

## 2. 状态代数与会话分轴不变量

严禁将多维系统状态压缩为单个布尔值。系统严格由 4 个单调递增的代数轴驱动：

1. **`SessionEpoch`（世界会话代）**：标识整个 listen-host 世界会话生命周期；仅在服务器启动、换图或返回主菜单时递增。
2. **`ConnectionGeneration`（逐观察者连接代）**：在当前会话内单调分配；客机断线重连只推进其自身的连接代，绝不推进全局 `SessionEpoch`，不污染其他在线玩家。
3. **`RegionGeneration`（权威区域代）**：标识物理区域或导航 Bound 真实重建的代数；普通拾取、开关门、掉落增量只更新状态版本，不递增区域代。
4. **`EntityGeneration`（实体代）**：单个实体槽位销毁重建时的代数。

---

## 3. 标准目录分层与模块化规划

```text
SteamP2PFriends-0.2.4-Experimental/
├── Core/                             # 【核心微内核】（纯逻辑，无具体业务）
│   ├── WorldPresenceObserverSet.cs   # 观察者追踪与坐标感知
│   ├── RegionLeaseCoordinator.cs     # 区域租约引擎与滞回释放调度器
│   ├── SessionEpochManager.cs        # SessionEpoch / ConnectionGeneration 代数管理
│   └── ILifecycleDomainAdapter.cs    # 统一领域适配器标准接口
│
├── Adapters/                          # 【业务领域适配器】（实现 ILifecycleDomainAdapter）
│   ├── Item/                          # 物品域：M1 生成 + M2 复制
│   ├── Zombie/                        # 僵尸域：M3 生命周期 + M4 状态快照
│   ├── Object/                        # 物件域：M5 场景物件与 Elver 权限门
│   └── Buildables/                    # 建筑与资源域：M5 路障/建筑/资源
│
├── Patches/                           # 【轻量 Hook 路由层】（只做转发，不写业务逻辑）
├── Host/                              # 【房主主机与准入管理】
├── Client/                            # 【客机连接与隔离表现】
├── Shared/ & UI/                      # 【通用工具、日志与前端】
└── WhitelistTests/                    # 【自动化单元与状态机测试套件】
```

---

## 4. M0 - M5 全域实施路线图

| 阶段 | 业务领域 | 核心职责 | 当前状态 | 旧补丁退役目标 |
|---|---|---|---|---|
| **M0** | 基础设施 | 观察者集合、影子账本、会话代隔离 | ✅ **PASS (已归档)** | 建立对照标准 |
| **M1** | 地图物品生成 | 远端观察者需求循环触发 `generateItems` | ✅ **PASS (已归档)** | 废除 `P0-B-3/6` 全图预刷 |
| **M2** | 物品快照复制 | 逐玩家 `ReliableEnqueueBaseline` 事务 | ✅ **PASS (已归档)** | 废除原生单布尔 `isItemsLoaded` 盲发 |
| **M3** | 僵尸生命周期 | `0->1` 按需生成 + 2s 滞回释放 + 隔离断路器 | 🟡 **代码/测试 PASS (待双机日志)** | 准备退役 `P0-D` 与 `P0-E` 简单拦截 |
| **M4** | 僵尸状态与外观 | 实体代 `EntityGeneration` + 服装/外观完整快照 | ⏳ **待开始 (M3 归档后)** | 彻底替换 `P0-C-1` 状态同步 |
| **M5** | 物件与建筑扩展 | Elver 权限门、家具远区碰撞、建筑/路障/资源 | ⏳ **待开始 (M4 归档后)** | 统一 `LevelObjectRemoteCollisionPatch` |

---

## 5. 阶段推进四步闭环流 (Standardized Workflow)

1. **Step 1 (编码与迁移)**：仅在对应 `Adapters/` 下实现业务逻辑，`Patches/` 仅作路由，严格遵守 `Apply -> Verify -> Commit` 事务与异常回滚；
2. **Step 2 (自动化与静态审计)**：执行 MSBuild 串行构建，运行 `WhitelistTests` 必须达到 100% PASS；
3. **Step 3 (双机运行日志验收)**：固定当前 Debug DLL SHA-256，部署双端/多端执行标准场景测试，采集完整诊断日志并核对流转签名；
4. **Step 4 (阶段归档与升版)**：在 `Develop-Stage/` 下建立冻结归档（含源码、DLL、完整日志与 `ACCEPTANCE-MANIFEST.md`）。归档经用户确认后，方可为下一阶段分配新版本号。

---

## 6. 不允许违反的铁规 (Hard Invariants)

1. **审批状态与观察者资格严格分离**：待审核玩家属于 `WorldPresenceObserverSet`，参与区域需求计算与初始快照接收，仅由服务端软隔离清洗其非法业务行为。
2. **单一 Writer 原则**：每个 Mutation Point 在会话期内恰有一个 Writer。禁止新旧写入逻辑双写，禁止在首个 Commit 后热切回 Legacy writer。
3. **严禁全局伪造 `Dedicator.IsDedicatedServer`**：严格保持 Client-Host 的 Listen Server 语义，禁止粗暴改写全局专用服布尔。
4. **旧哈希结论不可继承**：任何代码或 DLL 改动均会使旧运行 PASS 失效，必须重新以新哈希与成对日志建立证据。
