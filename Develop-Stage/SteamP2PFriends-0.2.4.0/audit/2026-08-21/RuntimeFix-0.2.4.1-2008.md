# M0 返修运行验收报告 - 0.2.4.1

## 结论

**PASS：M0 已正式关闭。** 本报告只接受 M0 影子账本的双机运行证据；不表示 M1 writer 已实现、已构建或已验证。

## 候选身份与部署

| 端 | 证据 | 结论 |
| --- | --- | --- |
| 客机 | `UMM-诊断包_20260821_200042/LogOutput.log:14` | 加载 `SteamP2PFriends 0.2.4.1` |
| Host | `UMM-诊断包_20260821_200056/LogOutput.log:14` | 加载 `SteamP2PFriends 0.2.4.1` |
| Host 部署 DLL | `E:/Steam/.../BepInEx/plugins/SteamP2PFriends.dll` | SHA-256 `9FF6D42166A46F1A73F8070DA96EB09C16944288043DDF69EC41E949D1FA69B0` |
| M0 开关 | Host cfg | `EnableMultiObserverShadow = true` |

## 运行验收矩阵

| M0 门禁 | 双端证据 | 判定 |
| --- | --- | --- |
| 待审核玩家仍为观察者并参与有效 Zombie demand | Host `:335` `authorized=False`；`:345` `pendingObservers=1`、`zombieDemandBounds=1` | PASS |
| 审核通过不移除 world presence | Host `:437` `AuthorizationChanged ... observer presence unchanged`；`:438` observers=2、Item=18、Zombie=1，仅 pending 归零 | PASS |
| 撤销/超时/拒绝/断开回收 | Host `:312-318`、`:345-352`、`:378-384`、`:404-414`、`:447-456` 均产生对应 `ObserverRemoved`；Client `:135-136`、`:159-160`、`:207-208`、`:232-237` 对应状态机完成 | PASS |
| 无效 bound 不产生伪 Zombie demand | Host PEI 初始 `:217` observers=1、`zombieDemandBounds=0`；有效远端加入 `:310` 变为1；两端无 `zombie-demand:255` mismatch | PASS |
| Item shadow 无回归 | Host `:310`、`:323`、`:438`、`:450` Item demand 随远端加入/离开 `9 -> 18 -> 9`；无 item mismatch，均为 `shadowOnly=true` | PASS |
| M0 自身故障 | 两端原始 `LogOutput.log` 中 M0 fault/capture/session identity/item/zombie mismatch 为0条 | PASS |

## 版本门禁

本轮已将以下规则写入 `EXPERIMENTAL-ARCHITECTURE.md`：阶段从首次构建到静态、自动化和相应双机运行验收完成前保持同一对外版本；阶段内返修仅通过 DLL SHA-256、Case ID 和报告区分；仅在当前阶段明确关闭后才为下一阶段分配版本号。

历史说明：M0 未关闭时创建的 `0.2.4.1` 已有真实部署、日志和哈希，不能事后重标为 `0.2.4.0`，否则会破坏可追溯性。该例外到此为止；后续严格按版本门禁执行。

## 残余风险与下一步

M0 是只读影子层，未触碰生成、loaded flag、RPC、物理、动画、AI 或审核 writer。它证明观察模型正确，不证明 M1 的 `ItemGenerationAuthorityAdapter` 行为。

开始 M1 前需获得明确实施授权并分配下阶段版本号；M1 完成前再次保持该新版本不变，所有返修以哈希和 Case ID 追踪。
