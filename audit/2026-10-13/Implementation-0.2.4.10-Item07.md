# Implementation-0.2.4.10-Item07

**工单**：`.scratch/item-authoritative-pickup-distance/`
**状态**：`implemented-pending-runtime`
**基线**：`a73ce912`
**Runtime**：OPEN。本记录不是 Runtime PASS，也不关闭 06。

## 源码验收

- 有效 P2P listen-host 的 `ReceiveTakeItemRequest` 前缀复用原生距离平方 `> 400` 拒绝。平方 400 不拒绝。
- 依据调用玩家 `transform.position` 与 `instanceID` 对应 `ItemData.point`，不读取客户端物品坐标。
- 超距或区域未就绪时不写背包、不删地面。合法距离后原生事务只放行一次。
- Host/Guest 同一入口。租约不能授权。单机、Guest 客户端自身与 dedicated 保持原路径，不伪造 dedicated。
- 05 就绪门仍在。测试宿主不能对 `ReceiveTakeItemRequest` 做真实 Harmony.Patch（ECall），绑定证据为 StaticIL 与注册调用。

## 审查链

第 1 轮，冻结于未提交增量、审查前无后续编辑：

- Standards `ses_ecbc5e697ffemhmuqsCkM6FMtN`，agent `standards-reviewer`，haowei-ai/grok-4.7，assistant variant low，CLEAN。无硬违规。
- Spec `ses_ecbc5c944ffeO9H14ZY0q3Mlmg`，agent `spec-reviewer`，haowei-ai/grok-4.7，assistant variant medium，CLEAN。

可延后判断题：未读的 `callerIsHost` / `clientReportedItemPoint` / `sameRegionLease`；`nativeTransaction: () => true` 是放行标志而非背包写入；就绪谓词与结算门并列重写；IT100 三次原生拒绝共用同一个失败回调。

## 证据

插件 Release 连续两次构建指纹一致。WhitelistTests `544/544 PASS`。日志：`.scratch/item-authoritative-pickup-distance/test-run-final.txt`。

候选：`bin/Release/SteamP2PFriends.dll`，MVID `3a352443-c924-4986-ba7f-d019faccdb16`，SHA256 `1B28140880CB9522F66DF9D0ADCAE0429D20F0BF66550C623D3408179E0884C7`。这不是 05 的 `3432e5c6-9bfb-4119-b986-06e3f5d36742` / `0173FE0D181358C11D4E257B3C5FDB1964C0C81C528A6C50055CB9ADC7261815`。

部署与正常拾取、受控超距采证归用户。未到达真实权威入口前不得报 Runtime PASS。
