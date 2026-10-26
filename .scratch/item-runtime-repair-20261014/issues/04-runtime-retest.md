# R04 — 合并返修候选的游戏内复测与运行证据回收

**What to build:** 在三张源码返修均交付后，提供同一候选 DLL、身份记录和最小游戏内复测步骤；用户只操作游戏及导出 UMM 包，Agent 分析日志，分别判断旧阻断是否消失及原验收哪些项目仍未执行。

**Blocked by:** 当前GuestBindingAdmission候选已交付；剩余阻塞为用户执行新会话的人工R04并回传各端UMM诊断包。旧1H1G失败历史及之前NativeIdentityLookup候选均保留为历史记录。

**Label:** ready-for-human
**Status:** Open — 本票源码候选已交付，等待用户执行R04实机验收；旧1H1G失败和僵尸掉落未确认状态保留。
**Version:** SPF 0.2.4.10 Experimental / 实机复测 R04

## 2026-10-27 当前GuestBindingAdmission候选（R04待测）

- 来源提交：`0f94e1333b755bc2fe07d81e1cc2dbe32c74a0ba`；唯一测试包：`artifacts/Item-GuestBindingAdmission/Item-GuestBindingAdmission-0f94e13-final.zip`；DLL：`artifacts/Item-GuestBindingAdmission/0f94e13-20261027-0259/SteamP2PFriends.dll`。
- DLL身份：版本`0.2.4.10`、net472/MSIL(AnyCPU)、MVID `2ec64d10-dc14-4c9c-afe2-6c22276e6d14`、SHA-256 `133BBC899B4B0A032769715E2231F3F7940D8F4DE8C19ED29D81CB6C243C8826`、Case-ID `SPF-0.2.4.10-Experimental-ItemSlice-Cutover`、Cutover；独立指纹核验PASS。
- 当前候选自动化632/632 PASS（Framework 630 + CoreCLR x86 NIL01/GBA01各1）；Round 3 Standards/Spec CLEAN。此为源码/自动化证据，不是Runtime PASS。
- 安装前关闭三端游戏、逐端备份旧DLL，只替换此DLL并核对版本/MVID；步骤及回滚说明见[包内README](../../../artifacts/Item-GuestBindingAdmission/0f94e13-20261027-0259/README.zh-CN.md)。
- **当前最小1 Host + 2 Guest流程：** Host初区确认物品可见/可拾取；Guest 1首次加入确认无九区“可能半包”提示并检查交互；Guest 2加入同区，两位Guest各拾取并丢下；Guest 1去远区后退出、Guest 2留房，Host进入新区，确认两端仍能活动；Guest 1重连检查物资；Host结束并新开世界，Guest 1首次加入再检查交付。
- 玩家只观察/操作游戏，通过UMM启动新会话并导出三端诊断包；不查看BepInEx窗口、不手工注入发送失败。异常后正常退出，再按角色回传UMM包并标明区域/操作顺序。
- 原06的其他双Guest独立离开、分帧/多包、恢复与到期项以及07正常/受控超距请求仍分别OPEN；僵尸掉落仍未确认。R04将按本流程回传证据后再逐项判定，不关闭原06/07或僵尸事项。

## Acceptance criteria

- [ ] 先核前置票各自限定提交、实际验证、当前增量的独立双 CLEAN 及真实主会话交付；以合并后的完整候选授予唯一 MVID、SHA-256 和 Case-ID。
- [ ] 当前候选构建包含全部返修；后票改变前票共用路径时补相应回归与审查，不沿用旧 DLL 或旧指纹。
- [ ] 首轮最小 1 Host + 1 Guest 症状复测覆盖多种刷新物品、拾取后模型消失、入包再丢下、Guest 入服及远离 Host 的新城镇、Guest 断线后 Host 继续交互。
- [ ] 玩家步骤只需游戏内观察/操作，不安排查看 BepInEx 窗口；异常只记阶段和角色，正常退出后按角色提供 UMM 诊断包。
- [ ] Agent 先核两端加载身份，再按仓库/队列/模型及结算/预算/通知证据分别判断；缺少关键观测保持未确认，不用零错误或 Acquire success 代替物品交付。
- [ ] 原严重症状消失后，继续原 06 的 1 Host + 2 Guest 验收；双 Guest 同区、独立离开、正常分帧/多包、恢复及掉落到期等未测项继续 OPEN。
- [ ] 07 正常及受控超距权威请求分别保留结论；客户端无法选择远物品不算距离门通过，不能由本返修替代验收。
- [ ] 若任一症状重现，回收带身份的诊断包并重新路由对应返修，不交付未双 CLEAN 的实验 DLL 让用户试错。
- [ ] 运行分析与正式审计按仓库流程交付；源码通过不等于 Runtime PASS，原 06/07 关单、冻结和发布仍由用户裁决。

## 非目标

不创建无必要的构建子会话，不部署稳定版，不要求用户现场看日志，不关闭或重写原 06/07 父票。

## 2026-10-26 只读诊断记录

- 玩家报告：Host 初区正常；Guest 加入后双方新区域物资与僵尸掉落异常，Guest 退出后 Host 异常持续。
- Host 实际堆栈与已安装游戏类型实现支持 `FindClient` 的 `SteamPlayerID != null` 重载判空缺陷；补偿失败后 Item 故障升级到共享停写。首次复制的原始堆栈仍缺失。
- 五次通用掉落已到接收入口但递延快照，尚无击杀来源关联，不能判定僵尸生成故障已确认。Guest 时钟与日志归属缺口单列；不按跨端墙钟对齐。
- 两端日志指纹匹配此前候选；R01/R02/R03 静态 CLEAN 记录保留，未覆盖本轮失败的原生运行边界，不据此判 Runtime PASS。
- 完整报告：`.scratch/matt-orchestrate/item-runtime-diagnosis-20261025-2329/expert/diagnosis.md`；新增修复草案：`.scratch/matt-orchestrate/item-runtime-diagnosis-20261025-2329/drafts/item-native-identity-lookup.md`。
- 当前只授权诊断、记录与票草案；生产修改、构建、提交、部署和发布均未获本轮授权。

## 历史：2026-10-26 原生身份查找候选交付记录（已由上方GBA包替代本轮测试候选）

- 代码提交：`bd451123f98626d9ea991b4de681dd1b826b9f2c`；双轴第3轮Standards/Spec均CLEAN；正式Main 624/624（Framework本地623 + CoreCLR真实NIL01子进程1），不是Runtime通过。
- 供人工测试的独立候选：`artifacts/Item-NativeIdentityLookup/bd45112-20261026-1030/SteamP2PFriends.dll`；版本0.2.4.10，net472/MSIL，MVID `88f119d8-9167-42c4-8e44-f560636aeca5`，SHA-256 `15B55640134CD4B2F9195BB65C4C38D5FC4005F219E00BE85BAA7F0E9D23FF9F`，Case-ID `SPF-0.2.4.10-Experimental-ItemSlice-Cutover`，role `Cutover`。独立产物门PASS；逐端安装、核SHA、回滚步骤见本票 [交付说明](../../item-native-identity-lookup/delivery.md) 和包内 `README.zh-CN.md`。
- **待用户执行的顺序**：关闭游戏，各端分别备份旧DLL→只替换插件DLL→按上述SHA逐端核验→经UMM启动新会话；异常退出后恢复各端原备份，不用旧fault会话验证。
  1. Host独自在初区确认物资可见、可拾取且无幽灵重复。
  2. Guest加入；Guest先去Host未到过的远区/新区，Host随后进入新区，检查双方物资及无幽灵重复。
  3. Guest退出后Host继续进入新区交互；另一Guest加入/继续观察，检查Host仍正常、无幽灵重复。
  4. 继续保留原06双Guest同区/独立离开/分帧与多包/恢复及到期项目；07正常及受控超距权威请求仍独立验收。僵尸击杀/掉落单独记录，当前仍未证实。
- 正常退出后先Guest再Host，由各端导出UMM诊断包。用户只看游戏，不需查看BepInEx窗口。R04原验收标准仍逐项待证；本段不改变关单/发布决策权。

**Approval:** 用户于 2026-10-14 批准四票拆分；前置交付后由主调度提供人工复测步骤。
