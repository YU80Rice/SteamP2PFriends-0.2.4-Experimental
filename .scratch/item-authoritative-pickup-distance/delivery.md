# C-P2-Item-07 交付

状态：源码与测试已关闭。Runtime 仍待人工，不构成 Runtime PASS。未领取 06，未部署。

基线：`a73ce912`。第 1 轮双轴 CLEAN。

- Standards：`ses_ecbc5e697ffemhmuqsCkM6FMtN`，haowei-ai/grok-4.7，assistant variant low，CLEAN。判断题 smell 不阻断。
- Spec：`ses_ecbc5c944ffeO9H14ZY0q3Mlmg`，haowei-ai/grok-4.7，assistant variant medium，CLEAN。

验证：插件 Release 两次构建指纹一致。WhitelistTests `544/544 PASS`。证据：`.scratch/item-authoritative-pickup-distance/test-run-final.txt`。

候选身份（自报告，与第二次 `Get-FileHash` 一致；不是 05 的 DLL）：

- DLL：`bin/Release/SteamP2PFriends.dll`
- version `0.2.4.10`
- MVID `3a352443-c924-4986-ba7f-d019faccdb16`
- SHA256 `1B28140880CB9522F66DF9D0ADCAE0429D20F0BF66550C623D3408179E0884C7`
- caseId `SPF-0.2.4.10-Experimental-ItemSlice-Cutover`（版本元数据字符串；身份以本 MVID/SHA256 为准，不得套用 05 的 `3432e5c6-9bfb-4119-b986-06e3f5d36742` / `0173FE0D…`）

部署由用户执行，不覆盖稳定版。三端各将上述 DLL 放到 `BepInEx/plugins/SteamP2PFriends.dll`，部署前用 SHA256 核对。实机按 `docs/agents/auto-rm-test-sop.md`。07 必须另记：正常拾取到达权威入口并成功；受控超距请求到达 `ReceiveTakeItemRequest` 后被拒绝，背包不增加、地面物品不删除。普通客户端选不中远处物品不算校验通过。可与 06 同轮采证，但保存 07 独立结论。07 通过不关闭 06。

审计：`audit/2026-10-13/Implementation-0.2.4.10-Item07.md`。
