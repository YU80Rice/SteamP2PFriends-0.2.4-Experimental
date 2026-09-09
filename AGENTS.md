# Agent Guidelines & Repository Context (SteamP2PFriends 0.2.4 Experimental)

## 项目概述与语言规范
本仓库为 `SteamP2PFriends-0.2.4-Experimental` 实验开发区，专注于《未转变者》（Unturned）Listen-Host 模式下 **Minecraft 局域网区块票据式多观察者生命周期与状态同步架构 (Multi-Observer LAN Chunk-Ticket Leasing Architecture)** 的研发、重构与演进。

- **默认工作语言**：所有沟通、文档撰写、代码注释及任务审计均使用**简体中文**。
- **架构铁规**：
  - 功能性联机模组强制基于 `LaunchMultiplayerNet` 命名频道实现双端闭环同步。
  - 架构实验与稳定版严格隔离；未经验收的修改禁止覆盖稳定发布版本。
  - 遵循 TDD 闭环：Red $\rightarrow$ Green $\rightarrow$ 自动化验证 $\rightarrow$ 多机运行日志审计 $\rightarrow$ 冻结归档。

---

## Agent skills

### Issue tracker

Issues and specs live as local markdown files under `.scratch/`. See `docs/agents/issue-tracker.md`.

### Triage labels

Canonical 5 triage roles: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout (`CONTEXT.md` + `docs/adr/`). See `docs/agents/domain.md`.

### Dual-axis reviewers

Standards 与 Spec 双轴审查官为用户级全局 Agent(`~/.zcode/agents/{standards-reviewer,spec-reviewer}.md`),每轮以全新实例并行调度。见 `docs/agents/output-review-loop.md`。

## Output review loop

Hard rule: every artifact (production source, tests, DLL artifacts, audit reports) ships only after the red-first TDD and dual-axis independent review loop closes CLEAN. Read `docs/agents/output-review-loop.md` before any production change or any formal output.

## Large writes

Generating a new file or a large edit expected to stream for over ~2 minutes (red-test files, long reports, bulk refactors) → read `docs/agents/large-write-batching.md` first and land the content in batches, each batch one tool call that finishes in seconds. 实现/TDD 任务另执行用户硬规则:每次工具调用一个 Edit(≤60 行)、一次只插一个测试方法、禁止整文件重写;批次间自动连续推进,不停等用户确认。

## Real-machine testing

实机测试为 P2P listen-host 人工三端形态(1 Host + 2 Guest,UMM 诊断包)。每轮实机测试的标准顺序、修复循环与判别/交付边界见 `docs/agents/real-machine-test-loop.md`;部署、指纹核对与证据回传 SOP 见 `docs/agents/auto-rm-test-sop.md`。
