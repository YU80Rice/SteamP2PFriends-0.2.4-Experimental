# 缺陷修复执行报告 - 0.2.4.8 / Ticket 11 循环审计

## 一、问题定位与修复策略

- **用户动态测试结论**：此前 1 Host + 2 Guest 清单中的游戏操作均成功，包含远区碰撞、砍伐、物品生成/拾取、重连和离开/重新进入。因此本轮不把功能现象误判为失败。
- **审计发现**：旧诊断包只能证明原生 `ResourceManager` 数据/协议路径在工作，不能证明 Multiple-Observer/SPI 已接入 Resource 数据面；同时缺少 Guest 端 `ReceiveResourceDead/Alive` 的真实接收证据。
- **修复策略**：保留原生 Resource 状态处理与网络协议执行，不新增并行 Authority Writer；将原生快照写入、全量接收、Host 采伐事件和 Guest 增量接收接入 Resource 复制账本，并补充 SPI 生命周期、滞回、重入和 generation 证据。

## 二、核心修复

1. `ResourceSnapshotReplicationLedger`
   - 增加原生快照写入、全量接收、Host 增量和 Guest 增量计数。
   - 增加 Region Generation 单调保护。
   - 合法的新代次增量推进观察者快照 generation 和 delta sequence；倒退消息拒绝。
2. `ResourceDomainAdapter` / `ResourceProductionControlSeam`
   - 增加 SessionBegin/End、LeaseAcquire/Release、ReleaseScheduled、ReleaseDeferred、Reentry、SnapshotEnqueue/Remove 和断线证据。
   - 修复重入分支先删除 pending 状态导致 `LeaseReentry` 永远不记录的问题。
   - 明确 `ResourceProductionControlSeam` 是 lease authority，`ResourceManager` 仅是原生 state encoder/decoder。
3. Resource 原生入口桥接
   - `SendResources_Write`：记录 Host 快照写入及 SPI 跟踪观察者数。
   - `ReceiveResources`：记录 Guest 全量快照接收。
   - `ServerSetResourceDead/Alive`：记录 Host 采伐增量。
   - `ReceiveResourceDead/Alive`：记录 Guest 实际增量接收，不拦截原生处理。
4. 回归测试
   - 新增 `M6S08 ResourceNativeDataPlane`。
   - StaticIL 验证 Host/Guest 真实入口到 ResourceSnapshotAdapter 的调用链。
   - `M6P03` 增加重入计数断言。

## 三、验证结果

| 门禁 | 结果 |
| :--- | :--- |
| 主项目 Release 构建 | PASS，0 errors / 0 warnings |
| 测试项目 Release 构建 | PASS，0 errors / 0 warnings |
| 完整测试入口 | PASS，205/205，Failed: 0 |
| Evidence Class 布局 | PASS，PureMemory 30 / StaticIL 13 / BuildArtifact 1 / Runtime 1 |
| DLL 独立核验 | PASS |
| `git diff --check` | PASS |

## 四、当前 Release DLL 指纹

| 字段 | 值 |
| :--- | :--- |
| Version / AssemblyVersion / FileVersion | `0.2.4.8` |
| MVID | `7436a515-6da4-4a28-90f6-12879f349006` |
| DLL SHA-256 | `8B576D9B1DCAA47FC78E1300F34FFAAD2E7BE28F9408FE6D07BA7A597FE6124E` |
| Plugin GUID | `com.yu80rice.steamp2pfriends` |
| Case-ID | `SPF-0.2.4.8-Experimental-StructureBaseline` |
| Metadata source | `Build/Version.props` |

## 五、独立审核与 Runtime 状态

- 独立 Spec/Standards 审核子任务已启动并关闭，但在限定等待时间内未返回结构化结论；按门禁规则记录为**未形成 PASS**，不能将超时解释为通过。
- 旧诊断包属于旧 DLL，不能作为本次修复后 Runtime 证据。
- 本次修复后的 DLL 尚未由用户重新部署到 Host、Guest A、Guest B 并采集新日志。

## 六、提交与结论

- 本轮代码提交：`49b819f fix(resource): close runtime audit data plane gap`
- 分支：`codex/structure-baseline-0.2.4`
- 当前标签、已归档版本和用户原有未跟踪文件均未修改或纳入提交。

**结论：代码修复和自动化门禁通过；Ticket 11 仍不得关闭。**

下一步请使用当前 `bin/Release/SteamP2PFriends.dll`，按原 1 Host + 2 Guest 清单重新动态测试，并提交新的 Host、Guest A、Guest B 诊断包。新日志必须与上述 SHA-256、MVID、版本、插件 GUID 和共享 Case-ID 关联；在这些证据及独立审核 PASS 前，不宣布 Ticket 11 关闭。
