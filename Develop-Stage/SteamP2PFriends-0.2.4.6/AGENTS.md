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
