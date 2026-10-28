# Item-SessionReadinessAndFrameProgress：每世界准备就绪与逐帧返区进度

- Label: ready-for-human
- Type: AFK
- Status: implemented-pending-runtime
- Version: SteamP2PFriends 0.2.4.10 Experimental
- Baseline: `5c496c0b3c8aa61ba825348bec637071759223c0` / `codex/structure-baseline-0.2.4`
- Created: 2026-10-28
- 主调度：Vela `0249e965-1143-4b6b-96f9-67484a145008`
- 规格/常驻指导：Vela `6c55c1a2-e8b8-4f78-90d8-f3f63ca713b7`，Codex gpt-6.1-sol/high
- 实施负责人：Vela `426e9bf4-3888-4a06-b04c-3ed8fac35264`，native `01a17e32-c56c-7de2-85b9-33010a59d147`，Codex `gpt-6-luna/high`；claim及构建记录见本run。
- Blocked by: None；GBA/NIL/R01–R03已交付，作为保留基线，不重开旧会话。

## 问题与用户结果

本轮1H1G玩家未见此前半包提示/幽灵等问题，但进入世界或物资覆盖区后，物资需等待或离返区才出现。玩家未指明端/世界；bound不证明仓库有物资或模型显示。包角色标签反置已由主调度解析，不要求重传或今晚追加人工测试。

当前源码有两个直接相关的集成缺口：共享Resource先启动同一个engine后，Item误以为自身已初始化而提前返回；返区有界工作又被空间采样的一秒门限速。现场后两世界的空基线及随后新增量支持第一条，未自动复现完整屏幕延迟。证据、时基、排序与范围见[本轮诊断](../matt-orchestrate/item-runtime-diagnosis-20261027-delayed-items/expert/diagnosis.md)及同run根`evidence-index.json`、`root-timeline.md`。

## 单一交付

同一票内完成A当前世界Item准备身份/Admission初始化及B逐帧有界返区进度，两者集成验证后交付一份可直接实机测试的独立 `SteamP2PFriends.dll`，不默认ZIP。恢复原生正常自动呈现的必要前置，不承诺所有模型无条件同帧完成。

## 验收标准

- [x] AC1 真实Resource先Begin、同一共享engine已active时，Item仍完成当前epoch/world局部初始化与准备；旧请求拒绝。
- [x] AC2 新世界原生仓库重置后重新首次准备，不能沿旧Ready；输入晚到可在同epoch恢复，重复调用不重生成/清观察者/重置在途工作。
- [x] AC3 多动作结算穿过真实生产一秒门的同实现Red→Green；连续多帧总时长不足1秒仍逐帧有界推进。
- [x] AC4 完成前零旧清单/呈现，完成后各有效等待者一次；同区共用，需求消失已启动工作仍有限收尾。
- [x] AC5 空间采样/自然周期不搬成逐帧全仓工作；共享时钟每帧只加一次delta，无跨域重复推进。
- [x] AC6 物理绑定、精确旧退出、单Writer/原生协议、R01 pending、R02故障预算、NIL引用比较与07距离均保持。
- [x] AC7 原632及必要新增正式登记在当前完整候选全部执行，NIL01/GBA01双runtime路由、身份与失败协议不变。
- [x] AC8 每轮新鲜原生Luna/high Standards/Spec同候选双CLEAN；限定提交、实际Release与独立身份门通过，单DLL及简短安装/回滚/R04说明齐备。双轴为本票第3/3轮；提交`6526b7e`；单DLL身份门PASS，细节见[`delivery.md`](delivery.md)。
- [ ] AC9 实机延迟另待R04验证；不把当前控制流Red/静态CLEAN当视觉PASS，不关闭1H2G/原06/07/僵尸留项。

## 范围与停止点

实施合同为[spec.md](spec.md)。仅修A/B及其必要直接调用者/测试/产物记录；模型消费候选C、Resource观测NRE、僵尸/掉落概率、LMN/ACK、故障清零/放宽恢复均不附带修。正确seam不可达、需引擎/native服务或冻结合同冲突时，保留候选和真实失败，带具体入口求助根/原指导，不用假Red或换依赖绕过。

审查历史保留三轮及G1恢复窗口，不与旧GBA/NIL/R01–03票混计。AC1–8自动化、独立审查及DLL交付完成；AC9 R04 Runtime待后续实测，不能以自动化或静态结果代替。交付工件只有单DLL，不含ZIP，也未部署或发布。
