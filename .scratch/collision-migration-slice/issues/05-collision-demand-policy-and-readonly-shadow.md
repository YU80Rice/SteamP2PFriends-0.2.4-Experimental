# 05: Collision 声明式 Demand Policy 与只读影子验证

**What to build:** Collision 按物件区域半径政策从同一套观察者事实算出含 Host 的 typed Collision Demand，并与旧覆盖做可分类的只读对照。旧 Writer 仍是唯一生产写入者。影子运行可帮助发现准入问题，但不能当作正式切换证据。

**Blocked by:** 02 Resource 经声明式 Demand Policy 接入共享投影引擎

**Status:** completed（静态闭环 + 只读影子 Runtime 已完成；静态审计 `audit/2026-09-18/Implementation-0.2.4.9-CollisionTicket05-2225.md`；Runtime 验收 `audit/2026-09-25/RuntimeAcceptance-0.2.4.9-CollisionTicket05-2355.md`；386/386 PASS、Release 双构建 0/0、四门禁 PASS；正式切换 Runtime 仍归票 09）

- [x] Collision Demand Policy 使用原版物件区域半径的切比雪夫投影作为行为保持基线，不升为共享默认半径（`CollisionDemandPolicy`：`RadiusSource = "LevelObjects.OBJECT_REGIONS"`、`ChebyshevSquare2D`、半径与世界尺寸由接线点读入；「Collision Policy Declaration」契约 + CSC12；Demand 命名空间「无共享默认半径」契约保持绿）。
- [x] 消费 canonical observer facts，包含 Host；Guest 资格沿用 World Presence Observer（影子路径提交每个有效样本、不按本地玩家过滤——「Collision Shadow Single Consumer」断言 `IsLocalPlayer` 只被读一次且用于 Host 归因；资格由政策读 `ObserverPresence.GameplayAuthorized`——「Collision Presence Eligibility」断言零授权类型引用；扰动 P10 证明该门有咬合）。
- [x] 产出 typed Collision Demand；不扫描客户端名册（共享投影引擎产出 `DomainDemand`（Domain=Collision）；影子四类型零 `Provider.clients` 引用（「Collision Shadow PureMemory」）+ CSC12；**确认离开才清理**：消费准入计划的缺席移除资格与暂缓集合，暂缓者的既有贡献保留——离开者需求不会永久残留）。
- [x] 不写 LevelObject、门、Collider 或可采集树（「Collision Shadow No Native Write」：影子方法体零原生写入调用、零旧 Writer 入口调用、零 Resource 接缝调用；「Collision Legacy Snapshot ReadOnly」：旧覆盖快照零原生调用、零旧补丁自身方法调用；扰动 P8/P11 取证）。
- [x] 按观察者贡献和聚合 Domain Id + Region Key 对新旧投影做差异分类（`CollisionShadowDifference` 携带 Domain Id + Region Key + 观察者/连接代次；CSC11；11 类分类（预期 4 / 禁止 7）覆盖两侧独有与时间轴差异并带原因词；旧侧认领者按「在场且合格 / 在场但不合格 / 本拍样本不可用（暂缓）/ 已不在事实里」四态分开（`ECollisionShadowLegacyState`），无法归因即失败闭合），逐类落点映射见审计报告 §8-11）。
- [x] Host 新增覆盖、资源写入退出 Collision 等标为预期差异；授权 Guest 缺区、需求抖动、错误释放等标为禁止差异（预期四类：Host 新增覆盖 CSC02、旧侧资源耦合覆盖退出 CSC04/CSC13/CSC15（原因词区分 Pending Guest / 已离开条目 / 暂缓样本）、canonical 生命周期 CSC05 / 暂缓贡献保留；禁止七类：授权 Guest 缺区 CSC03、静止抖动 CSC06、跨观察者错误释放 CSC07、凭空需求 CSC08、越界键 CSC09、异域需求 CSC10、无法归因的旧覆盖 CSC14）。
- [x] 影子候选与正式候选构建指纹可区分（候选角色进程序集元数据与默认 Case-ID；双角色实测身份逐项 `DIFFER`（`ticket05-candidate-role.log`）；BuildArtifact「Candidate Role」门 + 独立核验脚本同源校验）。
- [x] PureMemory、StaticIL、独立影子 BuildArtifact 通过（386/386 PASS；7 项结构契约 + 聚合门；`Verify-BuildFingerprintArtifact.ps1` PASS 于影子候选产物）。
- [x] 只读 1 Host + 2 Guest 影子 Runtime 用于发现和对照；不得替代正式切换 Runtime PASS（**2026-09-25 已通过**：三端 `ReadOnlyShadow` 指纹逐项一致；主机 25/25 条影子汇总 `collisionShadowForbidden=0`；逐条差异均为 `HostAddedCoverage/Expected/host-local-player`。详见 `audit/2026-09-25/RuntimeAcceptance-0.2.4.9-CollisionTicket05-2355.md`。本票不宣称正式切换通过）
- [x] 若影子暴露新的准入阻塞，回写 04，不在本票强行关单（静态面未暴露新阻塞，票 04 的 ARI/SAM/SIG 门保持绿；§11 给出影子运行发现阻塞时的回写口径）。
