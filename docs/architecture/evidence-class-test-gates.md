# Evidence Class 测试结构与门禁

## 物理布局

`WhitelistTests` 保持一个测试项目和一个 `Program.Main` 测试入口。测试证明按 Evidence Class 放在以下目录：

| Evidence Class | 物理目录 | 证明范围 |
|---|---|---|
| PureMemory | `WhitelistTests/Evidence/PureMemory` | 控制面、空间索引、租约、generation、适配器和 Fake 的纯内存外部行为 |
| StaticIL | `WhitelistTests/Evidence/StaticIL` | 编译产物中的类型、Harmony target、owner、priority、顺序、注册闭合和结构不变量 |
| BuildArtifact | `WhitelistTests/Evidence/BuildArtifact` | 加载 DLL 的版本、FileVersion、MVID、插件 GUID、文件存在性和独立 hash 重算 |
| Runtime | `WhitelistTests/Evidence/Runtime` | 真实游戏 Host/Guest、SP、listen-host、U3DS、P2P 的因果证据；当前为 Pending |

测试不得通过复制同一测试增加 PASS 数量，也不得把一类证据的 PASS 改写为另一类证据的 PASS。

## 唯一入口与输出

`WhitelistTests/Program.cs` 是唯一执行入口。入口按 PureMemory → StaticIL → BuildArtifact → Runtime 的顺序输出分类；Runtime 只输出 `PENDING`，不伪造通过数量。`Tools/Verify-EvidenceClassLayout.ps1` 负责在构建前核验目录、项目编译项和唯一入口，不替代测试项目。

## 已覆盖接缝

- Registration Closure：注册角色、顺序、重复注册、关闭后的不可变性，以及 StaticIL 的 Registration Closure/Registration Trace 形状；
- Identity：Region Key、Bound Key、Domain Id 和插件身份契约；
- Spatial Lease：多观察者区域需求、Acquire、Hysteresis Release、断线、重连和跨区域隔离；
- Generation：Session、Connection、Region、Entity/领域快照的过期与重建拒绝；
- Structure Invariants：Harmony target/owner/priority/order、P2P 通道、SteamID、配置键、插件 GUID、Authority Writer 和 Pending 领域边界；
- BuildArtifact：当前加载 DLL 的版本、MVID、插件身份和 SHA-256 可重算性。

## 门禁解释

PureMemory 通过只表示纯逻辑接缝通过；StaticIL 通过只表示编译结构与注册形状通过；BuildArtifact 通过只表示交付产物身份可追踪；Runtime 必须有真实运行日志和共享 Case-ID 才能通过。结构阶段 Runtime 保持 Pending，并在 Migration Manifest 与审计报告中单独记录。
