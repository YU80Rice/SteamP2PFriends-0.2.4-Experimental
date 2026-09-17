# Ticket 03 Runtime 补测:地址栏 SteamID:port 整串走 Steam P2P

- **日期**:2026-09-18 01:05
- **基线**:同一候选 DLL `A69B78AF…7217` / MVID `2ea4dbc6-be2e-4f72-95d0-dfd438ad0f62`(HEAD `01dc27b`)
- **票据**:`.scratch/join-routing/issues/03-steamid-port-suffix-routing.md`
- **证据**:
  - 客机:`…\UMM-诊断包_20260918_010533\LogOutput.log`(644 行)
  - 主机:`…\UMM-诊断包_20260918_010547\LogOutput.log`(4298 行)
- **用户操作**(关单口径):地址栏粘贴 `房主SteamID:27016` 整串(非端口栏)。识别后端口栏被藏为预期。

## 结论

**票 03 Runtime 闭合。** 两端指纹与 09-17/09-18 候选表逐字节一致。客机 L148 `[UnifiedConnect] route=SteamP2P target=76561199030780228 hasPassword=False started=True`;L156 `ServerAccepted` / L629 `Connected` `failureInfo=NONE(0)`。主机无密码房 L786/L806,L2285 `HOST_ACCEPT` `76561199721762479` `clients=2`。无静默失败、无把整串交给原版的失败枚举。

日志仍无 `hostField` 原文(规格禁止把连接参数细节打进普通诊断)。分类结果 + 用户本轮明确的地址栏整串操作,闭合票 03 与 09-18 04:47 轮「端口栏 27016 + 裸 ID」的缺口。

不改 RELEASES。
