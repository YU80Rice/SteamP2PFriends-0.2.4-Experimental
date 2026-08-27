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
