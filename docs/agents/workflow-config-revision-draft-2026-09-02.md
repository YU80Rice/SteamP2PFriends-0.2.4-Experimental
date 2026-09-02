# 工作流配置修订草案（供执行 Agent 对照修改并冻结快照）

> **状态**：草案（handoff artifact）｜**日期**：2026-09-02｜**审计基准**：只读审计「票据格式 / 所有权 / 文档位置 对工作流配置的符合性」
> **用途**：由创建本仓库的 Agent 按本草案逐项修改 `docs/agents/*`、`docs/architecture/module-ownership.md` 等，验收后**提交并冻结一份最新快照**（快照不触碰编译产物）。
> **执行完成后**：本草案文件应删除或归档（不在最终快照中保留为常驻配置）。
> **已锁定决策（2026-09-02，用户确认）**：R-C5＝保留本地 tracker 为主、`.github` 模板标注 legacy 不删不改（由用户后续自行更新）；R-C4＝ADR 标题前缀统一为 `# 000X:`。

---

## 0. 审计结论摘要（对照基准）

| 维度 | 结论 |
|---|---|
| 配置自身完整性（docs/agents 三件套 + AGENTS.md `## Agent skills` 块） | ✅ 完整、互指一致 |
| 布局层（票据均在 `.scratch/` 内、CONTEXT.md 在根、ADR 在 docs/adr/） | ✅ 0 项布局违规 |
| 文档与实况一致性 | ⚠️ 3 种票据元数据风格、2 套词汇表并存，配置只描述了目录布局、未描述字段格式 |
| 配置未覆盖的新增约定 | ⚠️ `docs/architecture/`、`audit/YYYY-MM-DD/`、wayfinder `map-*` 布局、票据所有权字段、`.scratch` 根级数据文件 |
| 配置与实况矛盾 | 🔴 `.github/ISSUE_TEMPLATE/` 指向 GitHub，而仓库无 remote 且已选「本地 markdown tracker」 |

**修订原则**：只做**文档层收编**（让配置如实描述既有约定），不重写 26 个现有票据文件、不改代码、不动编译产物。

---

## 1. 修订项清单

### A. 票据格式（改 `docs/agents/issue-tracker.md`）

**R-A1 · 新增「票据字段格式」小节**
- 现状：`.scratch/` 实际并存 3 种元数据风格，issue-tracker.md 只描述了 `<slug>/issue.md + spec.md` 目录布局。
- 修订：在该文件新增小节，记录 3 种风格及其属主：
  1. **front-matter YAML**（`minecraft-lan-experience-multi-domain-sync/issue.md`：`title:`/`status:`/`labels:`/`created_at:`/`updated_at:`）→ 属主：triage / to-spec。
  2. **键值 bullet**（`structure-baseline-0-2-4-8/issue.md`：`- **Label**:`/`- **Type**:`/`- **Status**:`）→ 属主：to-spec / to-tickets。
  3. **冻结编号 issue**（`structure-baseline-0-2-4-8/issues/*.md`：`**What to build:**`/`**Blocked by:**`/`**Status:**` + 复选框）→ 冻结基线图内原位更新（既有例外已记录）。
- 验收：issue-tracker.md 能据此判定任一 `.scratch/` 文件归属哪种风格。

**R-A2 · 新增「Wayfinder 地图」命名空间约定**
- 现状：`map-m5-to-m8-world-sync-roadmap/`（map.md + ticket-01/02/05/ticket.md）、`map-top-level-architecture-blueprint/`（map.md + ticket-01~05/issue.md）共 10 个 md 使用 `wayfinder:*` 词汇表，配置零覆盖。
- 修订：在 issue-tracker.md 记录：`map-*/map.md + ticket-*/ticket.md`（或 `issue.md`）为 **wayfinder 专属命名空间**，词汇表为 `Label: wayfinder:map/grilling/task/done`、`Type: HITL|AFK`、`Parent:`、`Status: Open (Frontier)`；**排除出 triage / to-tickets 的常规扫描范围**；仅在 wayfinder 收尾并并入主流程（`/to-spec`）时转写为 `<slug>/issue.md + spec.md`。
- 验收：triage/to-tickets 的读取规则与 wayfinder 地图隔离，语义不冲突。

**R-A3 · 新增「`.scratch/` 根级数据文件」约定**
- 现状：`.scratch/ticket03-symbols.txt`、`.scratch/ticket06-tests-current.log` 散落根级，不属任何 issue 目录。
- 修订：在 issue-tracker.md 规定：**过程性数据/日志**（符号转储、测试日志等，非票据）允许放 `.scratch/` 根，命名前缀必须注明所属 ticket（如 `ticketNN-*.txt` / `ticketNN-*.log`），并标记为不可作票据解读。
- 验收：新增条目不再被误认为 issue。

**R-A4 · 修复绝对路径引用**
- 现状：`.scratch/minecraft-lan-experience-multi-domain-sync/issue.md` L16 用 `file:///D:/Agent-工作目录/.../spec.md`。
- 修订：改为相对引用 `[spec.md](./spec.md)`。
- 验收：无 `file:///` 绝对机器路径残留于 `.scratch/`。

### B. 所有权（改 `docs/architecture/module-ownership.md` 与 `docs/agents/issue-tracker.md`）

**R-B1 · 归属表补 `Core/Ownership` 行**
- 现状：`Core/Ownership/ModuleOwnershipCatalog.cs` 物理存在（权威根），但 module-ownership.md 归属表无此行。
- 修订：在表中新增 `Core/Ownership → ModuleOwnershipCatalog`（标注为模块所有权权威根），与 ADR 0007/0008 一致。
- 验收：归属表与 `Core/`、`Adapters/`、`Security/`、`Platform*/` 实际目录一一对应。

**R-B2 · Animal 归属交叉引用**
- 现状：Animal 适配器归属仅记录于 `docs/architecture/animal-structure-ownership.md`，module-ownership.md 无行。
- 修订：在 module-ownership.md 增加 `Adapters/Animal` 行并交叉引用 animal-structure-ownership.md，消除「同域归属分散两处」。
- 验收：任一适配器归属在 module-ownership.md 可直接定位或一跳可达。

**R-B3 · issue-tracker.md 新增「票据所有权（属主技能）」小节**
- 现状：`Owner:`（仅 map-top 的 map.md 一处孤例）、`Parent:`、`Type:`、`Label:`、`Blocked by:` 全配置零定义，只存在于票据数据层。
- 修订：记录各字段属主：
  - `Owner:`（map 级负责人）→ wayfinder / map；
  - `Parent:` → wayfinder map；
  - `Blocked by:`（阻塞边）→ to-tickets；
  - `Label:`（`wayfinder:*` 或 triage 5 标签）→ 按词汇表属主；
  - `Type:`（HITL / AFK / Specification）→ wayfinder / to-spec；
  - `Status:`（票据生命周期，如 `implemented-pending-runtime`）→ 实施流程。
- 并明确一句：**5 个 triage 标签（needs-triage / needs-info / ready-for-agent / ready-for-human / wontfix）只表达「谁来处理」；票据生命周期状态（如 `implemented-pending-runtime`、`Open (Frontier)`）是另一维度，二者不互相替代。**
- 验收：triage-labels.md 的 5 标签与数据层实际状态字段无歧义。

### C. 文档位置（改 `docs/agents/domain.md` 等）

**R-C1 · 记录 `docs/architecture/` 位置**
- 现状：12 个文件（registration-trace.md / migration-manifest.md / module-ownership.md / evidence-* / ownership-* 等），由 ADR 0006 建立，但 domain.md / issue-tracker.md / CONTEXT.md 均未定义。
- 修订：在 domain.md 新增小节：`docs/architecture/` = 结构基线产物目录（Registration Trace / Migration Manifest / Ownership / Evidence Class / Build Fingerprint），由 ADR 0006 建立；消费规则：迁移/所有权/证据类改动须同步该目录。
- 验收：domain.md 覆盖仓库全部 3 处文档位置（CONTEXT.md + docs/adr/ + docs/architecture/）。

**R-C2 · 记录 `audit/YYYY-MM-DD/` 位置**
- 现状：实施/运行审计报告归档位置仅写在票据数据层（`structure-baseline-0-2-4-8/spec.md`），工作流配置未定义。
- 修订：在 issue-tracker.md 或 domain.md 记录：`audit/YYYY-MM-DD/` = 实施（`Implementation-<ver>-<HHMM>.md`）、运行修复（`RuntimeFix-*`）、运行验收（`RuntimeAcceptance-*`）报告归档位置。
- 验收：audit 目录归属有配置依据。

**R-C3 · 澄清根目录文档政策**
- 现状：根目录除 CONTEXT.md 外还有 11 个 .md（README/CHANGELOG/LICENSE/CONTRIBUTORS/BASELINE/DEVELOPMENT_TIMELINE/EXPERIMENTAL-ARCHITECTURE/DEDICATED_SYNC_COMPARISON_CHECKLIST/M4-ZOMBIE-SNAPSHOT-TEST-CHECKLIST/ARCHITECTURE-REVIEW/VIBECODING），domain.md 未定义其位置政策。
- 修订（推荐，最小改动）：在 domain.md 增一句——「根目录可放置项目级文档（README/CHANGELOG/LICENSE/CONTRIBUTORS/检查清单/阶段报告等），属项目文档而非领域模型，不受单上下文约束；领域模型以 CONTEXT.md 为准。」
- 验收：根级 .md 不再被误判为位置不合规。

**R-C4 · ADR 标题前缀统一（已决策：统一）**
- 现状：0001–0003、0006–0008 为 `# 000X:`；0004/0005 为 `# ADR 0004:`（且为双语标题）。
- 修订（决策方向已锁定，执行细节由创建 Agent 落实并与用户协调）：将 0004/0005 首行统一为 `# 0004:` / `# 0005:`，与其余一致。注：domain.md 声称「ADR 是不可变历史记录」，本项仅改标题行外观、不改内容与编号；建议执行 Agent 在 domain.md 补一句「ADR 标题行前缀为展示规范，不构成内容变更」以消歧。
- 验收：8 个 ADR 首行前缀一致。

**R-C5 · `.github` 模板与「本地 tracker」的关系（已决策：保留本地为主，模板标注 legacy）**
- 现状：`.github/ISSUE_TEMPLATE/bug-report.yml` + `config.yml`（指向 `https://github.com/YU80Rice/SteamP2PFriends`），而仓库无 git remote、issue-tracker.md 已选本地 markdown。
- 决策（用户已确认 2026-09-02）：项目**以本地 tracker 为主**；`.github/ISSUE_TEMPLATE/` **保留但视为过时（legacy）**，由用户后续自行更新，**执行 Agent 不删除、不修改该目录内容**。
- 修订：在 issue-tracker.md 加一行注明——「`.github/ISSUE_TEMPLATE/` 为历史遗留/未来 GitHub 迁移预留，当前以本地 tracker 为准，该模板由人工维护」。
- 验收：本地 tracker 权威性明确；`.github/` 未被误删。

**R-C6 · 冻结例外退役标记**
- 现状：issue-tracker.md 的「冻结依赖图例外」随结构基线（Ticket 01–11，Ticket 11 已关）推进，无退役条件。
- 修订：在该例外段落补一句退役条件——「本例外随 0.2.4.8 结构基线验收完成而退役；退役后 `structure-baseline-0-2-4-8/issues/` 转为只读归档，不再原位更新，新 issue 一律用 `<slug>/issue.md + spec.md`。」

---

## 2. 执行清单（按序）

1. （决策已锁定）**R-C5**：保留本地 tracker 为主、`.github` 模板标注 legacy 不删不改（用户后续自行更新）；**R-C4**：ADR 标题前缀统一为 `# 000X:`（执行细节由创建 Agent 落实并与用户协调）。
2. 修改 `docs/agents/issue-tracker.md`：R-A1、R-A2、R-A3、R-B3、R-C6（含 R-C5 附注）。
3. 修改 `docs/agents/domain.md`：R-C1、R-C2（或放 issue-tracker.md）、R-C3。
4. 修改 `docs/architecture/module-ownership.md`：R-B1、R-B2。
5. 修改 `.scratch/minecraft-lan-experience-multi-domain-sync/issue.md`：R-A4（相对路径）。
6. **R-C5**：不删不改 `.github/ISSUE_TEMPLATE/`，仅在 issue-tracker.md 加 legacy 注记（模板由用户后续更新）。
7. 复核 AGENTS.md `## Agent skills` 块：三小节指向不变，**无需修改**（新约定收编进三个 docs 文件内部即可）。
8. 提交并冻结快照（见下）。

---

## 3. 冻结快照流程（不触碰编译产物）

`bin/`、`obj/` 已被 `.gitignore` 排除（已核实），因此快照天然不含编译产物；**禁止** `git add -f` 任何构建产物。

1. `git status --short` 核对未跟踪清单（当前 8 项，应全部纳入快照）：
   - `.scratch/structure-baseline-0-2-4-8/issue.md`、`spec.md`、`issues/01-u3-sdk-registration-trace.md`（冻结图缺件，必须补齐）
   - `.scratch/ticket03-symbols.txt`
   - `ARCHITECTURE-REVIEW-0.2.4-Experimental.md`
   - `audit/2026-08-26/Implementation-0.2.4.8-1033.md`、`audit/2026-08-26/Implementation-0.2.4.8-1154.md`、`audit/2026-08-28/RuntimeFix-0.2.4.8-0916.md`
2. `git add` 上述文件 + 本草案修改的文件；提交信息按仓库约定（示例）：
   `chore(agents): document ticket formats/ownership/doc locations; freeze 0.2.4.8 snapshot`
3. 提交后 `git status --short` 应干净；`git log -1` 确认为最新。
4. 冻结标记（可选，仓库当前无任何 tag）：打 tag `0.2.4.8-experimental`，或按 Develop-Stage 惯例归档一份 `Develop-Stage/SteamP2PFriends-0.2.4.8/`（仅源码/文档，不含 bin/obj）。
5. 移除或归档本草案文件，使快照为最终状态。

---

## 4. 交付验收（执行 Agent 自查）

- [ ] `docs/agents/issue-tracker.md` 覆盖：标准布局 + 3 种字段风格 + wayfinder 地图 + `.scratch` 根级数据文件 + 票据所有权字段属主 + 冻结例外退役条件。
- [ ] `docs/agents/domain.md` 覆盖：CONTEXT.md + docs/adr/ + docs/architecture/ 三处位置，及根目录文档政策。
- [ ] `docs/architecture/module-ownership.md` 含 `Core/Ownership` 与 `Adapters/Animal`，与代码结构一一对应。
- [ ] `.scratch/` 无 `file:///` 绝对路径；`.github/ISSUE_TEMPLATE/` 保留未删、仅在 issue-tracker.md 标注 legacy；ADR 0004/0005 首行已统一为 `# 0004:` / `# 0005:`。
- [ ] 未跟踪 8 项全部入快照，`git status` 干净；bin/ obj/ 未被纳入。
- [ ] AGENTS.md `## Agent skills` 块未破坏。
