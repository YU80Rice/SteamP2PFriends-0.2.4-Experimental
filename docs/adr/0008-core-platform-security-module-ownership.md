# 0008: Core / Platform / Security Module Ownership

状态：Accepted（Ticket 04，0.2.4.8）

## Context

Ticket 02 已将补丁注册收敛到 `PatchRegistrationOrchestrator`，Ticket 03 已建立稳定的
Domain、Region、Bound 身份接缝。剩余源码仍将核心控制面、外部平台接入、Route B 安全和
跨领域观察补丁分散在旧目录中，容易形成重复权威入口或把目录移动误认为行为迁移。

## Decision

1. `Core` 拥有控制面、身份、生命周期、注册编排和无法证明单一领域归属的受控跨领域补丁。
2. `Platform` 拥有 Client、Host、Transport、UI 和 Diagnostics 的外部运行时接入；这些
   目录是物理归属权威入口。为保持本批次行为不变，既有兼容 namespace 可以暂时保留，
   但不得创建第二份实现或第二个注册入口。
3. `Security` 拥有 P2P approval、whitelist 和 Route B 准入补丁；`Adapters/<Domain>` 不再
   拥有准入状态机实现。
4. `Core/Patches` 是唯一受控跨领域补丁位置。每个保留项必须在 `module-ownership.md`
   登记无法证明单一领域归属的原因，并只能由 `PatchRegistrationOrchestrator` 注册。
5. `ModuleOwnershipCatalog` 只记录物理根、唯一权威类型和 Registration Trace 覆盖，
   不执行 patch registration、不承载生产状态；`ModuleOwnershipStaticILContractTests`
   验证其唯一性与编译产物中的关键入口数量。
6. 本票只做结构归属和证据门禁，不迁移 Resource Production Control Seam，也不改变现有
   Harmony target、owner、priority、注册顺序、P2P 通道、SteamID、配置键、插件 GUID、
   Route B 状态机或日志语义。

## Consequences

- 目录、模块 ID、权威入口与 Registration Trace 具有可审计映射，后续 namespace 迁移可以
  按模块独立推进。
- 结构 StaticIL 和 BuildArtifact 证据可以证明入口唯一与产物身份，但不能替代 SP、U3DS
  或 P2P Runtime 验证。
- `Core/Patches` 暂时保留的跨领域补丁仍是受控债务；在后续迁移中若能证明领域归属，必须
  迁入对应领域并更新清单。

## Verification

- PureMemory：既有领域测试保持不变，新增一条 Ticket 04 结构契约入口，总计 `184/184 PASS`。
- StaticIL：`ModuleOwnershipStaticILContractTests` 验证唯一 Registration Closure、Security
  入口和 Registration Trace 覆盖。
- BuildArtifact：主插件与测试 EXE 的版本、FileVersion、MVID 和 SHA-256 记录在 Ticket 04
  交付报告中，并区分独立重算与运行时自报告。
- Runtime：本票保持 Pending，不由结构整理、构建或测试结果替代。
