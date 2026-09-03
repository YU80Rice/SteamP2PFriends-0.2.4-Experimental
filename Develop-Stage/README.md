# Develop-Stage（开发阶段归档）

本目录保存已冻结的历史开发阶段快照。**归档版本不可回写**；每次归档是一份整树副本，
附带该版本的验收清单（`ACCEPTANCE-MANIFEST.md`，0.2.4.0–0.2.4.4 存在，0.2.4.5 起
改用本索引 + 冻结提交记录）。

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

## 当前版本

- live 树版本为 `0.2.4.8 / Experimental`（见 `Build/Version.props`），尚未在
  `Develop-Stage/` 归档；冻结/归档动作由审计验收流程执行。
- 每个快照的差异、票据引用与证据归档位置，见 `audit/README.md` 索引与
  `docs/architecture/migration-manifest.md`。

## 维护规则

- 只读冻结：不修改、不删除已归档快照内容。
- 快照内含 `bin/` 等构建产物是**有意保留**（`.gitignore` 对
  `Develop-Stage/**` 的显式放行），用于离线可交付归档，不等于 live 树允许提交构建产物。
