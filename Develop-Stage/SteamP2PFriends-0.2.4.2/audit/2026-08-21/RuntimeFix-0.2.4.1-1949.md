# M0 Zombie Bound 返修报告 - 0.2.4.1

## 一、问题定位与修复策略

### 根因

M0 补测的 Host 日志在 `UMM-诊断包_20260821_193743/LogOutput.log:333-334` 记录：

```text
transition=RelevanceChanged ... zombieBound=255
mismatch bound=255 observerDemand=1 nativePlayerCount=0
```

U3-SDK 证实 `LevelNavigation.tryGetBounds` 在没有任何 navigation bound 包含当前位置时先将
`bound=255`，`PlayerMovement.InitializePlayer` 也以 `255` 初始化 `_bound`。原生
`ZombieManager.onBoundUpdated` 只在 `LevelNavigation.checkSafe(bound)` 成立时改变
`PlayerCountInRegion`、发送或生成僵尸。因此旧 M0 将无效 bound 写入 Zombie demand，造成影子账本与
原生语义不一致。

补测同时确认审核语义正确：Host 在 `LogOutput.log:372` 记录客机
`authorized=False`，`:380` 记录 `pendingObservers=1`；`:438` 记录审核后的
`AuthorizationChanged ... observer presence unchanged`。

### 修复策略

1. 在 M0 capture 使用 U3 原生 `LevelNavigation.checkSafe(movement.bound)` 计算
   `HasFunctionalZombieBound`，不硬编码 `255`。
2. 无有效 navigation bound 时，observer、Item relevance、审核状态均保持不变，只跳过 Zombie demand。
3. 有效 bound 与待审核状态完全正交：`GameplayAuthorized` 不参与 functional-demand 判断。
4. 版本升级为独立实验候选 `0.2.4.1`，防止与已发现误判的 `0.2.4.0` 混淆。

## 二、核心代码变更对比

### 修改文件

- `MultiObserver/MultiObserverShadowCoordinator.cs`
- `MultiObserver/MultiObserverShadowLedger.cs`
- `WhitelistTests/MultiObserverShadowTests.cs`
- `WhitelistTests/Program.cs`
- `SteamP2PFriendsPlugin.cs`
- `Properties/AssemblyInfo.cs`
- `EXPERIMENTAL-ARCHITECTURE.md`
- `README.md`
- `CHANGELOG.md`

```diff
- Increment(_zombieDemand, state.ZombieBound);
+ if (state.HasFunctionalZombieBound)
+     Increment(_zombieDemand, state.ZombieBound);

+ bool hasFunctionalZombieBound =
+     LevelNavigation.checkSafe(observer.Movement.bound);
```

`HasFunctionalZombieBound` 被保存到 sample/state/snapshot，且属于 relevance 变化条件；因此
`invalid -> valid -> invalid` 转移会正确增减 Zombie demand。此变化不调用 `generateZombies`、
不写入 `isZombiesLoaded`、`isNetworked`、RPC、物理、动画或审核状态，P0 writer 保持唯一权威。

## 三、编译与自测状态

| 项目 | 结果 |
| --- | --- |
| 插件 Release Rebuild | PASS，0 errors / 0 warnings |
| 插件 Debug Rebuild | PASS，0 errors / 0 warnings |
| WhitelistTests Release Rebuild | PASS，0 errors / 0 warnings |
| 自动化回归 | `75/75 PASS` |
| 新增 M14 | PASS：无效 bound 时 observer 与 Item demand 保留；有效 bound 恢复 Zombie demand；再次无效时仅清 Zombie demand |

实验 DLL 指纹：

| 构建 | SHA-256 |
| --- | --- |
| Debug | `DC097B520B567E31CC7FC027CF2650BCA58DB95EF854D7B02E0F03E99FFF75EB` |
| Release | `9FF6D42166A46F1A73F8070DA96EB09C16944288043DDF69EC41E949D1FA69B0` |

稳定区仍冻结：

| 稳定构建 | SHA-256 |
| --- | --- |
| `0.2.3.70-beta.2` Debug | `A575A0F72DB8C1C1837223F03A3973F51043044D4B5BEFA7035C9AC7B5365E37` |
| `0.2.3.70-beta.2` Release | `B8282D8111438FDA896334B9CE7A6F10015550A6A70F3365B84F553AAA17F95A` |

## 四、子智能体审核记录

| 审核项 | 判定 | 说明 |
| --- | --- | --- |
| U3 语义一致性 | 通过 | `checkSafe` 与 `ZombieManager.onBoundUpdated` 同一判断边界 |
| 待审核玩家 | 通过 | 有效 bound 与 `GameplayAuthorized` 独立，M01 覆盖 `authorized=false` 仍产生 demand |
| 无效 bound 状态保持 | 通过 | M14 覆盖 observer 与 Item demand 不被移除 |
| 只读 M0 边界 | 通过 | 无 writer/RPC/loaded flag/Unity 对象写入 |
| 最终复审 | PASS | 无阻断项，独立复跑 `75/75 PASS` |

## 五、最终结论

`0.2.4.1` 是 M0 返修候选，静态、编译、自动化和独立审核均已通过，可部署进行新的同哈希双机
M0 复测。此前 `0.2.4.0` 的运行证据不得继承至本候选。只有新日志确认无效 bound 不再产生
`zombie-demand:255` mismatch，且待审核/已授权 observer 与 Item shadow 无回归后，M0 才可关闭并开始 M1。
