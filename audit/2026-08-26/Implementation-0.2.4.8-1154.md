# Ticket 01 执行报告：U3-SDK Registration Trace 与注册基线

## 一、需求执行概述

完成 0.2.4.8 结构基线中的 Ticket 01。Registration Trace 已按插件真实调用链重排，并建立 65 个显式 `RegisterManual/RegisterAtomically` 调用单元的逐项核验矩阵；本轮未修改生产 C#、Harmony 注册逻辑、配置、标签或已归档版本。

## 二、源码溯源清单

| 需求点 | 证据 |
|---|---|
| U3-SDK 对照版本与生命周期锚点 | `docs/architecture/registration-trace.md`，U3-SDK commit `ea7b4973af5ba10f62baad2bfde36ab2e5b060eb` |
| 真实注册阶段与阶段内顺序 | `Core/Lifecycle/SteamP2PFriendsPlugin.PatchRegistry.cs:23-84`、`2671-3404` |
| 65 项逐项 target、patch type、patch method、owner、priority、调用位置、依赖、证据等级 | `docs/architecture/registration-trace.md` 的“显式注册元数据快照（规范矩阵）” |
| Asset Integrity 两个直接 Patch 目标 | `Core/Lifecycle/SteamP2PFriendsPlugin.PatchRegistry.cs:2523-2554`、`2592-2625` |
| Barricade 原子登记与显式 priority | `Patches/P0EBarricadeLifecycle/BarricadeLifecycleRegistration.cs:167-180` |
| Ticket 状态 | `.scratch/structure-baseline-0-2-4-8/issues/01-u3-sdk-registration-trace.md` |

## 三、变更清单

### 修改

- `docs/architecture/registration-trace.md`
- `.scratch/structure-baseline-0-2-4-8/issues/01-u3-sdk-registration-trace.md`

### 明确未修改

- 生产 C# 源码、Harmony target、注册 owner、协议和配置；
- 当前 `0.2.4-Experimental` 标签；
- `0.2.4.8` 之前的归档版本；
- 用户提供的第三方复核报告。

## 四、编译与测试验证

| 验证项 | 结果 |
|---|---|
| `dotnet build SteamP2PFriends.csproj -c Release --no-restore` | 通过，0 errors / 0 warnings |
| `dotnet build WhitelistTests/SteamP2PFriends.WhitelistTests.csproj -c Release --no-restore` | 通过，0 errors / 0 warnings |
| `WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe` | `181/181 PASS` |
| Registration Trace 行数 | 65 |
| 源码显式调用数 | 65 |
| `git diff --check` | 通过 |

## 五、独立审核记录

| 轮次 | 判定 | 结果 |
|---|---|---|
| 修订前复核 | FAIL | 发现逐项字段不完整、Order 不是实际执行顺序、Barricade priority 记录错误 |
| 修订后复核 | PASS | 矩阵 65 行；非 `n/a` 行直接记录 `com.yu80rice.steamp2pfriends`；复合单元依赖已具体化；无阻断项 |

## 六、证据边界

本 Ticket 的 PASS 仅表示源码追踪资料、结构基线文档和构建/测试门禁完成。`S`/`H` 静态登记证据与 `R-Pending` 运行时证据保持分离；尚未证明 Harmony 最终执行排序、原生回调实际触发时序、跨端行为或生产功能验收。

## 七、最终结论

Ticket 01 已完成，可作为 Ticket 02 Patch Registration Orchestrator 拆分的输入。下一步仍应按冻结依赖图执行 Ticket 02，不提前迁移 Resource Production Control Seam。
