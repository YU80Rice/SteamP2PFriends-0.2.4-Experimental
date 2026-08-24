# M1 双机运行验收审计 - 0.2.4.1

## 一、结论

**FAIL（证据不足，未发现已证实的 M1 运行故障）。**

M1 版本保持 `0.2.4.1`，不得归档为 `Develop-Stage/SteamP2PFriends-0.2.4.1`，不得进入 M2。下一轮仅补测或补齐诊断，不得以本轮日志升级版本号。

## 二、审计输入与身份边界

| 角色 | UMM 诊断包 | 已证实 |
| --- | --- | --- |
| 客机 | `UMM-诊断包_20260821_204907` | 加载 `SteamP2PFriends 0.2.4.1`，完成首次连接、被撤销踢出和再次连接 |
| 房主 | `UMM-诊断包_20260821_204939` | 加载 `SteamP2PFriends 0.2.4.1`，记录 M1 注册、区域需求、审批和单写者 gate |

两端 `LogOutput.log` 只记录版本号 `0.2.4.1`，没有 DLL SHA-256。因此不能把它们等同于当前实验区 Debug 候选 `1F02A37DF9F944A9AD33B3CD32283ABF2AFB697934E5C0A0DE532E2813CF509A`，也不能证明双端同一二进制。

此外，当前源码中的 `ApplyManualDiagnosticPatches()` 会输出 `[Diag]` / `[WorldSyncDiag/Item]` 注册记录；本轮主机日志不含这些记录。此事实进一步禁止用“当前源码存在完整物品诊断”替代运行包中的接收、拾取和移除证据。

## 三、已闭合证据

| M1 要点 | 判定 | 运行证据 |
| --- | --- | --- |
| M1 与 `generateItems` 单写者门已注册 | PASS | 主机 `LogOutput.log:33,104-105` |
| 远端观察者进入原生 step 5 后触发 M1 决策并调用 `askItems` | PASS | 主机 `LogOutput.log:306-315`：每个 `M1-Item allow` 后紧接 `ListenRegionSync/Item send` |
| 重叠/既有 region 不二次生成 | PASS | 主机 `LogOutput.log:328-342`：已提交 region 均为 `Committed` 后 skip |
| 房主回到客机区域时不覆盖、不重生 | PASS | 主机 `LogOutput.log:456-465`：同一 `(26..28,31..33)` 仍全部 `Committed` 后 skip |
| 连接代与授权不会重置观察者账本 | PASS | 主机 `LogOutput.log:431-443`：待审核 observer 加入后，审批仅记录 `AuthorizationChanged`，presence 未消失 |

本轮未出现 `M1-Item deny`、`ItemAuthorityGate abort`、`ItemAuthorityGate Prefix fail-closed` 或 `ItemRegionSync` error。

## 四、阻断验收项

1. **待审核客机首次远区生成未证明。** 第一段 M1 `allow` 对应已授权客机：主机 `LogOutput.log:245,306-320`。第二次待审核接入虽有 `Pending added`（`:414`）和 observer 记录（`:431`），但仅命中已提交 region 的 skip（`:421-429`）；没有 `authorized=False -> M1 allow -> gate commit -> askItems` 的完整链。
2. **审批后不重生未直接证明。** 已记录审批转换（`:438-443`），但没有同一 region、同一实例的审批前后状态或 `commit/skip` 因果链。
3. **客机拾取的权威移除未覆盖。** 双端日志均无 `ReceiveItem(s)`、实例 ID、take/remove RPC 或状态快照，无法证明客机拾取后房主权威实例移除及主机回区一致。
4. **同哈希未证明。** UMM 包中没有候选 DLL 或 SHA-256 记录；相同 `0.2.4.1` 版本号不是同一二进制的证据。

## 五、固定版本补测契约

继续使用当前 `0.2.4.1` DLL，不修改版本号。准备一个房主从未访问的远区，执行：

1. 两端先记录同一 DLL SHA-256 和统一 Case ID。
2. 未授权客机单独进入该远区；房主日志必须按同一区域记录 `Pending added -> ObserverAdded authorized=False -> M1 allow -> ItemAuthorityGate commit -> askItems`。
3. 房主批准客机；同一区域必须没有第二次 `commit`，并保留明确的 instance/region 对应证据。
4. 客机拾取一个可识别物品；两端记录实例 ID 与 host authoritative remove/客户端 receive 结果。
5. 房主返回该区域；记录该实例仍不存在且 gate 为 `Committed/skip`。

若现有运行 DLL 无法输出步骤 3-4 的实例证据，应先在 `0.2.4.1` 内补齐低频、带 region 与 instance ID 的 M1 诊断，再重复同一补测；这属于阶段返修，不触发 M2 或版本升级。

## 六、编译与独立审核

- 本轮为只读运行验收审计，未修改源码或 DLL，故未重新编译。
- 既有 M1 静态构建记录：Debug/Release `0 errors / 0 warnings`，自动化 `81/81 PASS`，见 `Implementation-0.2.4.1-2030.md`。
- 独立审核：`m0_runtime_acceptance_audit` 复核同一双端日志，结论为 FAIL（证据不足），与本报告一致；无已证实的 M1 运行故障。

## 七、最终结论

现有实测证明 M1 的远端需求链与单写者防重生链正在运行，但不足以关闭 M1 的待审核首次远区生产、审批后稳定性、权威移除和同二进制部署门。保持 `0.2.4.1` 并补测；M2 暂不授权。
