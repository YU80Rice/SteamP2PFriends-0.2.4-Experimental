# 11: Resource 旧 Authority Writer 退出与运行时验收

**What to build:** 让 Resource 的旧生产写入路径退出权威地位，并用绑定当次 DLL 身份的 Host/Guest 运行时证据证明迁移没有引入行为回归。

**Blocked by:** 10 / Resource Production Control Seam 行为迁移

**Status:** implemented-pending-runtime

- [x] Migration Manifest 明确记录旧 Resource 租约释放 Writer 已退出；原生 `ResourceManager` 数据/协议执行器保留，待 Runtime Gate 证明其不再承担独立租约权威。
- [ ] 共享 Case-ID 的 Host、Guest、多观察者、重连和离开/重新进入场景均取得运行时证据。
- [ ] Resource 碰撞、采伐、租约释放、generation 防护和双端状态复制通过 Runtime Gate。
- [ ] Host/Guest 日志与当次 DLL 的独立 SHA-256、MVID、版本和插件 GUID 一致关联。
- [ ] 独立审核 PASS 后，才允许将 Resource Migration Slice 标记完成并推进其他领域。

本轮已建立 `ResourceAuthorityRetirementStaticILContractTests`，确认旧的
`ResourceRegionLifecycleAdapter.OnObserverRelease` 入口已删除，而由 `ResourceProductionControlSeam` 经
`ResourceDomainAdapter.OnRelease` 调用 generation/session 校验的 `CommitRelease`。真实游戏 Runtime
证据尚未提供，因此本票不得标记为完成。

## 本轮交付证据（2026-08-27）

- Release 构建：主项目与测试项目均为 `0 errors / 0 warnings`。
- 完整测试：唯一测试入口 `204/204 PASS`，`Failed: 0`。
- Evidence Class 布局：`PureMemory 30 / StaticIL 13 / BuildArtifact 1 / Runtime 1`，布局核验 `PASS`。
- 独立产物核验：当前 `bin/Release/SteamP2PFriends.dll` 的版本/FileVersion 为 `0.2.4.8`，MVID 为
  `87329b03-8e73-4219-b88b-7b3da2cfcc3e`，SHA-256 为
  `B1B63DD3B69E562509A3279E5D856060FFB9EC30564DD982EE30B325C4B8DE95`，插件 GUID 为
  `com.yu80rice.steamp2pfriends`，Default Case-ID 为
  `SPF-0.2.4.8-Experimental-StructureBaseline`；`INDEPENDENT_ARTIFACT_VERIFICATION_PASS`。
- 独立审核：本轮 Spec/Standards 子智能体未在限定时间内返回正式 PASS，已关闭；按门禁处理为未通过。
- Runtime：同一 Case-ID 下的 Host、Guest、多观察者、重连、离开/重新进入以及 Resource 碰撞、采伐、租约释放、generation、双端复制日志均未提供，保持 `PENDING`。

详细报告：`audit/2026-08-27/Implementation-0.2.4.8-Ticket11.md`。

## 修复轮交付证据（2026-09-04，取证轮之后）

- H1 取证裁决：`audit/2026-09-04/Forensics-0.2.4.8-Ticket11-H1-1046.md`——9/9 失败均为 `native-resource-trees-unavailable`，根因锁定为 Unturned 植被重构后区域树渐进生成与 SPI 快照的时序错配，单区域失败整批回滚 + 指数退避致生产瘫痪。
- 步骤④容错修复落地：`ProcessSingleRegionEntry` 单区域 Acquire 失败隔离（M6P31 转绿）+ `ResourceNativeSnapshotUnavailableException` 暂缓快照、2s 温和退避重试（M6P32 新增）；M6P07/15/21/28 按隔离语义升级；新增 StaticIL 分类边界契约。裁决记录见 `audit/2026-09-04/Implementation-0.2.4.8-Ticket11-1133.md` §3。
- 静态门禁：259/259 PASS；修复版 DLL MVID `6ae0e666-882b-48ca-b6e2-024d79af9aae`、SHA-256 `B6A3ACC9131BFCD22564644140D2E874CB854BC5F8FE8D435B39DBC33C885986`（INDEPENDENT_ARTIFACT_VERIFICATION_PASS）。
- Runtime：仍 `PENDING`——修复效果待 1 Host + 2 Guest 同 DLL 同 Case-ID 动态测试证明（重点：主机进入 foliage 未烘焙区域后自动补 lease、多观察者与客机重连场景），本票不得关闭。
