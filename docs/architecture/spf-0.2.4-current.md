# SPF 0.2.4 架构规划快照（2026-10-08）

本图保留10月8日的规划与读图说明；其中“Item尚未构建”是当时状态。2026-10-29当前Item实现和实机结论见[迁移索引](migration-manifest.md)及[运行报告](../../audit/2026-10-29/RuntimeAcceptance-0.2.4.10-R04.md)，不据历史图重判本版就绪状态。

[打开交互架构图](spf-0.2.4-current.html) · [Archify源图](spf-0.2.4-current.architecture.json) · [交付与验证记录](spf-0.2.4-current.delivery.json)

## 读图

上方是开房/加入、Steam Networking Sockets传输及会话身份/审批。世界在场观察者的事实进入共享需求投影，各域声明自己的Policy；共享生命周期引擎将领域需求变成Acquire/Release，领域执行端口才执行原生覆盖。

Resource与Collision已经接入这条共享链，Collision于2026-10-07由用户裁定完成。权威实体数据仍由Unturned原生Manager持有，Guest复制及Host本地音画沿原生路径衔接。Item、Zombie、Animal、工事的既有适配器存在，不等于它们已全部迁入新的共享租约编排。

虚线标明0.2.4.10下一阶段Item迁移：七票已批准、尚未构建；20米拾取拒绝是独立权威交互任务，不由租约授予权限。图中没有把LMN作为SPF原生世界同步的前置依赖。

## 本次核对的仓库事实

| 组件/结论 | 来源 |
|---|---|
| 插件启动、配置与入口就绪 | 根目录SteamP2PFriendsPlugin.cs、Core/Registration/PatchRegistrationOrchestrator.cs |
| 传输、开房/加入与审批边界 | Platform/Transport、Platform/Host、Platform/Client、Security及README联机路径 |
| 共享事实/需求/编排 | Core/ControlPlane/MultiObserverShadowCoordinator.cs的ConfigureControlPlane及Resource/Collision观察者接线 |
| Resource / Collision生产端口 | Adapters/Resource、Adapters/Collision；共享生命周期注册消费点 |
| 其他域既有注册 | Core/Registration/SteamP2PFriendsPlugin.PatchRegistrationDomainModules.cs |
| 原生仓库/RPC与listen-host边界 | CONTEXT.md、ADR0021及Item规格编制的SDK源码事实 |
| Collision完成及历史运行证据 | .scratch/collision-migration-slice/issue.md、票09、audit/README.md |
| Item下一阶段及独立距离任务 | [七票图](../../.scratch/item-migration-slice/ticket-plan.md)、[独立拾取票](../../.scratch/item-authoritative-pickup-distance/issue.md) |

研究源码HEAD为57e3c4e4411b4f8831f0bf6d489b9fea9922c47c；live元数据源已由用户指定推进0.2.4.10，未重新编译DLL。已完成阶段保留在 [0.2.4.9归档](../../Develop-Stage/SteamP2PFriends-0.2.4.9/ACCEPTANCE-MANIFEST.md)。

## 图件验证

Archify architecture类型，9/9 showcase检查、0错误/0警告；Chrome检查1440×900、1600×1000、1920×1080、2048×1320均无页面溢出，浅/深主题截图已检查。字节身份及两轮视觉修正记录见交付JSON；截图/浏览器证据见同目录visual-check文件。图件通过不授予SPF构建或Runtime验收。
