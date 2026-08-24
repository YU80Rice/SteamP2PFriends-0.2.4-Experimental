# SteamP2PFriends M2 阶段验收清单

## 阶段身份

- 阶段归档名称：`SteamP2PFriends-0.2.4.2`
- 阶段：Multi-Observer M2 `ReliableEnqueueBaseline`
- 验收结论：PASS
- 验收日期：2026-08-21
- 下一阶段：M3（尚未开始构建）

## 归档指纹

| 文件 | 长度 | SHA-256 |
| --- | ---: | --- |
| `bin/Debug/SteamP2PFriends.dll` | 995328 | `3A83295E685EAD79BDA64A8816FCBDF821C1CBDFE0B9DE37E0692E2EEE483ED3` |
| `bin/Release/SteamP2PFriends.dll` | 925696 | `8B0B169EB6FA26BFF0CA9E161D97E17EBE1EA1C8D0D96C80EF56DA65A3C9C926` |

## 验收范围

- 待审核观察者仍进入原生世界基线，不被白名单硬阻断。
- 单客机与双客机同区/异区均保持独立 observer、connection generation 和区域账本。
- 审批状态变化不销毁 observer；已生成区域通过 authority gate skip，避免二次随机生成。
- 断线和重连按 SteamID 精确清理与恢复，既有权威区域不重生。
- R01-R10 全部 PASS；R07/R08 证据来自 `acceptance-evidence/UMM-诊断包_20260821_233451` 与 `..._233509`。

完整运行验收记录：`audit/2026-08-21/RuntimeFix-0.2.4.2-2359.md`。

## 归档边界

本目录冻结 M2 的源码、Debug/Release 构建、审计记录和双客机 UMM 原始包。它不包含 M3 源码或后续版本号。
