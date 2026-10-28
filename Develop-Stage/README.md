# Develop-Stage（开发阶段归档）

本目录保存已冻结的历史开发阶段快照。**归档版本不可回写**；每次归档是一份整树副本，
附带阶段身份与证据来源。0.2.4.0–0.2.4.4含`ACCEPTANCE-MANIFEST.md`，0.2.4.5–0.2.4.7采用本索引及冻结记录；0.2.4.9另含归档清单和逐文件SHA-256。

| 快照 | 状态 | 说明 |
|---|---|---|
| `SteamP2PFriends-0.2.4.0` | 冻结 | M0 实验架构实施；含 `ACCEPTANCE-MANIFEST.md` |
| `SteamP2PFriends-0.2.4.1` | 冻结 | M1 运行验收通过记录；含 `ACCEPTANCE-MANIFEST.md` |
| `SteamP2PFriends-0.2.4.2` | 冻结 | M2 架构实现；含 `ACCEPTANCE-MANIFEST.md` |
| `SteamP2PFriends-0.2.4.3` | 冻结 | M3 实施；含 `ACCEPTANCE-MANIFEST.md` |
| `SteamP2PFriends-0.2.4.4` | 冻结 | M4 多机运行验收审计；含 `ACCEPTANCE-MANIFEST.md` |
| `SteamP2PFriends-0.2.4.5` | 冻结 | M5 Animal 领域适配器阶段归档 |
| `SteamP2PFriends-0.2.4.6` | 冻结 | M6 Level Object / 碰撞阶段归档 |
| `SteamP2PFriends-0.2.4.7` | 冻结 | M7 Barricade / Structure 复制阶段归档 |
| `SteamP2PFriends-0.2.4.9`（本地归档） | 冻结（2026-10-08） | C-P1 Resource/Collision共享控制面与Collision关单；886文件原样复制并核对哈希 |
| `SteamP2PFriends-0.2.4.10-Partial-BLOCKED-20261009-0130`（本地归档） | 历史中止快照（2026-10-09） | 当时Item第01票交付不合格，920文件与已有日志/产物保留；不代表当前0.2.4.10状态 |

## 当前版本

- live版本为`0.2.4.10 / Experimental`（见`Build/Version.props`）；本阶段物品修复与1H+2G目标体验验收完成，发布包见`publishi/0.2.4.10/`。10月9日的Partial/BLOCKED仅是历史记录，后续修复和验收见`audit/README.md`。
- 0.2.4.9已按用户请求保留整树及已有DLL；0.2.4.8没有独立快照，本轮不从当前树补造历史版本。
- 每个快照的差异、票据引用与证据归档位置，见 `audit/README.md` 索引与
  `docs/architecture/migration-manifest.md`。

## 维护规则

- 只读冻结：不修改、不删除已归档快照内容。
- 快照排除.git、Develop-Stage自身递归和本地.zcode状态，保留源码、文档、测试、工单及已有产物；归档清单记录实际工作区身份，不假称只来自提交。
- 快照内含 `bin/` 等构建产物是**有意保留**（`.gitignore` 对
  `Develop-Stage/**` 的显式放行），用于离线可交付归档，不等于 live 树允许提交构建产物。
