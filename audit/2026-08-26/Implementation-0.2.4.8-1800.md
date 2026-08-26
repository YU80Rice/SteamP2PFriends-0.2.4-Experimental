# Ticket 05 返工复核与交付报告：Resource / Collision 结构迁移

## 一、返工原因

此前 Ticket 05 的源码迁移和验证报告已经存在，但票据文件仍保持 `ready-for-agent`，五项验收项均未勾选，导致依赖图与实际交付状态不一致。本轮返工补齐票据状态和可追溯证据，并重新执行验证；未新增生产功能，也未接线 Resource Production Control Seam。

本报告同时作为 `Implementation-0.2.4.8-1742.md` 的勘误与当前复核依据：旧报告保留为历史记录，不覆盖；其中测试 EXE 指纹、PureMemory 汇总和补丁文件数量以本报告及 Migration Manifest 当前值为准。

## 二、需求与证据矩阵

| 票据要求 | 结果 | 证据 |
|---|---|---|
| Resource/Collision 领域边界、namespace、注册归属明确 | PASS | `docs/architecture/resource-collision-ownership.md`；`Core/Ownership/ModuleOwnershipCatalog.cs`；五个补丁由 `ResourceCollisionOwnershipStaticILContractTests` 验证唯一 FullName |
| 目录整理保持 Harmony target、顺序、碰撞、采伐、状态复制 | PASS（结构复核） | `d2c3554` 相对父提交仅改变 namespace、using、入口引用和结构证据；`docs/architecture/registration-trace.md` 保留 U3-SDK Resource step 3 / Object step 4；Runtime 行为仍 Pending |
| 旧 Authority Writer 唯一，Production Control Seam 未接线 | PASS | `docs/architecture/resource-collision-ownership.md`、`docs/architecture/migration-manifest.md` Batch 5；源码未新增生产写入路径 |
| Resource PureMemory/StaticIL 通过，迁移记录含未决项 | PASS | Resource 22 项、Collision 8 项相关 PureMemory 测试通过；全量入口为 182 项 PureMemory + 3 项 StaticIL，合计 `185/185 PASS`；Batch 5 明确 Runtime、旧 writer 退出和生产接线未决 |
| Release 构建、回归测试、独立审核通过 | PASS | 主项目与测试项目 Release 0 errors / 0 warnings；`185/185 PASS`；`git diff --check` PASS；本报告记录返工后的票据闭环 |

## 三、源码与结构复核

- Resource 补丁统一位于 `Adapters/Resource/Patches`：世界同步诊断、区域同步、采伐复制、树木/矿石资源碰撞激活；其中 5 个补丁类型由本票覆盖，但本次提交实际修改 4 个补丁文件，`ResourceManagerHarvestReplicationPatch.cs` 已在目标目录中作为既有文件存在，未在本提交重复改写。
- 静态 LevelObject 碰撞位于 `Adapters/Collision/Patches`。
- `LevelGroundRemoteTreeCollisionPatch` 保留在 Resource：其原生目标为 `ResourceSpawnpoint.SetIsActiveInRegion(bool)`，跨领域调用只复用 Collision 的公开覆盖判定，不复制状态或创建第二 writer。
- 注册、关键验证、会话复位、断线清理和 Host 清理均已指向迁移后的领域入口；未改变 owner、priority、Harmony target、patch method 或既有注册调用顺序。

## 四、验证记录

```text
dotnet msbuild SteamP2PFriends.csproj /t:Rebuild /p:Configuration=Release /v:minimal
dotnet msbuild WhitelistTests/SteamP2PFriends.WhitelistTests.csproj /t:Rebuild /p:Configuration=Release /v:minimal
WhitelistTests/bin/Release/SteamP2PFriends.WhitelistTests.exe
git diff --check
audit/2026-08-26/Verify-Ticket04Metadata.ps1
```

- 主项目 Release：0 errors / 0 warnings；
- 测试项目 Release：0 errors / 0 warnings；
- 全量测试：`185/185 PASS`；Resource/Collision StaticIL：PASS；
- `git diff --check`：PASS；
- 独立产物验证：插件 DLL SHA-256 `FE6A8D3EF30711A20285E86F5642F17FEC1CE29E8AAAFA693583C9025245FD15`，MVID `b8093107-d624-4aa8-8f44-bdfd265e8a08`，版本 `0.2.4.8`；测试 EXE SHA-256 `EBAC52B78F2D3E2D9827D15C2638478E708560505BE52D958D3986348A539160`，MVID `26483703-8494-4de1-bd52-e8e791cb7727`。上述值由当前 Release 文件重新计算并由 `Verify-Ticket04Metadata.ps1` 读取。

复核绑定：源码实现基线为提交 `d2c3554f783a7dfd4ef6660165aa3218b00c05b5`；当前分支为 `codex/structure-baseline-0.2.4`；当前仓库未检测到 Git tag，因此无标签可被修改；本轮仅更新票据、迁移清单和审计记录。

## 五、独立审核与范围边界

- Standards：第一轮发现 4 项证据记录阻断，已由本勘误和迁移清单修正；复核结论以再次审核为准；
- Spec：此前源码结构复核无阻断；本轮重新绑定证据；
- 本轮复核的票据闭环：PASS，已将状态更新为 `ready-for-human` 并勾选五项验收；
- Runtime（Singleplayer、listen-host、U3DS、P2P）：PENDING，不以构建、StaticIL 或 PureMemory 替代运行时证据；
- Resource Production Control Seam：未接线；旧 Resource Authority Writer：保持唯一生产权威；
- 当前标签与已归档版本：未修改；
- 当前分支：`codex/structure-baseline-0.2.4`。

## 六、结论

Ticket 05 的实际实现与票据状态已重新对齐；历史报告中的证据冲突已通过追加勘误和更新 Migration Manifest 修正，可移交最终独立审核、人工 Runtime 门禁和后续 Ticket 06；本轮没有扩大行为变更范围。
