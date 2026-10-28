# Item 局部恢复载体补查

状态：只读源码事实与历史方案比较；用户已选原生离区返区恢复，2026-10-08另批准 `/spfitemretry` 及主规格工程默认值。下文历史选项不是待重新裁定事项，不表示已实现或 Runtime PASS。

`S/` 为 `D:/Agent-工作目录/U3-SDK/Assets/Runtime/Assembly-CSharp/`；`P/` 为本仓根。基线同 [规格编制输入](planning-inputs.md)。

## 已可复用的原生载体

- Host 可通过原生聊天定向提示受影响 Guest：`S/Unturned/Managers/ChatManager.cs:707–765` 按 toPlayer.transportConnection Reliable 发送；SPF已有调用先例 `P/Security/P2PApprovalManager.cs:486–498`。
- Guest 文本通过原生可靠 SendChatRequest 到 Host；调用玩家从 ServerInvocationContext 获取，原生聊天频率限制保留：同 ChatManager `397–411,591–622`。
- onChatted 在 process 前执行并可设置 isShown=false：同 ChatManager `501–514`。当前没有 Item 重试命令，须新增专用订阅/处理和当前身份/审批/失败资格验证，不能因此开放管理员命令权限。
- SPF process Prefix 会拦非管理员 slash，单接权限事件不够：`P/Security/Patches/P2PListenHostCommandPermissionPatch.cs:83–112`。专用文本是用户命令，不把聊天内容变成隐蔽机器协议。

## 原生安全离区与返回

- ItemManager `980–1009` 在离开覆盖后取消本地待实例化并置 isNetworked=false，服务器清该玩家 isItemsLoaded；`1015–1038` 返区重发原生清单。ITEM_REGIONS=1见 `44`，只跨脚下一个区域边界未必脱离失败区域覆盖。
- 当前M2离区相关性释放见 `P/Adapters/Item/ItemObserverReplicationAdapter.cs:131–168,328–335`；新路径须由共享观察者事实提供该资格，不复制旧私有扫描。
- 对可能已部分接收的区域，明确提示离区后回来可复用这条原生清理路径；离区取消旧恢复资格，返区按新当前需求尝试基线，不重新生成/重复结算世界物品。

## 不能直接当作完整重同步的接口

- ItemManager `399–414` 的私有 SendClearRegionItems/公开 ReceiveClearRegionItems 只销毁模型、取消队列，不置 isNetworked=false；`416–430` 的 askClearRegionItems 会清主机 region.items，禁止拿来“修客户端”。
- ReceiveItems `584–634` 在已networked时忽略第0包，后续非零包仍能追加；askItems `642–684` Reliable分包，没有总包数/完成ACK。故“清缓存再盲发清单”不成立。
- 同可靠lane顺序可作为实现依据，但只调用现有clear仍缺客户端重新接受第0包的语义；新增该语义须隔离正常清空、增量、多包及旧尝试，不能声称现成原生已解决。
- M2 `P/Adapters/Item/ItemObserverReplicationAdapter.cs:418–442` 调用返回后提交；SDK `S/NetTransport_SteamNetworkingSockets/TransportConnection_SteamNetworkingSockets.cs:126–148` void发送失败只记日志。调用返回不能证明Guest收全，正常模型分帧不能被超时判失败。

## 待用户选择

A（推荐最小范围）：原生定向提示＋用户专用重试命令；明确尚未开始交付时可重试基线，可能半包时提示离开物品加载范围再回来。正常进入无新操作，异常路径不新增机器控制协议。

B：原地手动恢复，需要明确 Item 专用双端有序重置接线/控制语义及其验收；可以继续复用原生物品载荷，不等于必须LMN、通用路由或完整基线ACK。但不能仅靠现有clear实现，也不能把新增语义隐藏为纯原生能力。

用户未选择前，04不得发布为ready-for-agent；载体决定属于异常操作方式，不重开已接受的自然周期政策。

## 用户选择与最后补查 — 2026-10-07

用户选择A：“推荐：沿用原生；提示后手动重试，必要时离开该区域的物品加载范围再回来。正常进区不受影响。”记录见 [0024: Item 异常恢复沿用原生提示与离区返区](../../docs/adr/0024-item-failure-recovery-uses-native-reentry.md)。上述待选文本保留询问顺序，当前04不实现B。

补查确认已有wire可以复用：公开 `ClientStaticMethod<byte,byte>.Get(ReceiveDelegate)` 可取得已有clear句柄并对单连接Invoke，见 `S/NetInvokable/ClientStaticMethod.cs:366–393`；生成方法 `S/NetGen/NetInvokable/ItemManager_NetMethods.cs:173–207` 载荷只有x/y，没有恢复身份/尝试号。SPF单连接桥见 `P/Core/Patches/ClientMethodLoopbackPatch.cs:338–373`。

因此准确边界是“已有clear载荷，但原地恢复还须新增并验证Item双端语义”，不是“无法复用原生wire”。当前选择A，无需新增该语义；同lane可靠顺序也不消除void发送的入队/实际接收证据缺口。
