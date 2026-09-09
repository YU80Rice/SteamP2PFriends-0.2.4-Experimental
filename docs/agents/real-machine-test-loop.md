# 实机测试循环(Real-Machine Test Loop)——2026-09-08 起强制执行

> 来源:用户 2026-09-08 指令持久化(自 `更好的UN体验` 工作区移植同构规程)。适用于本仓库其后所有工单的"真机读数驱动修复"循环。
> 本文档是 `docs/agents/output-review-loop.md` 的实机测试轮补充细则,与其同效力。
> 本项目的实机形态是 **P2P listen-host 人工三端测试**(1 Host + 2 Guest,UMM 诊断包),现行 SOP 见 `docs/agents/auto-rm-test-sop.md`。

## 每轮实机测试的标准顺序(不可跳步、不可调换)

1. **用户实机测试**,提交三端 UMM 诊断包(1 Host + 2 Guest)。
2. **分析日志**(主工作树会话):先核对三端 `dllSha256=`/`mvid=` 与候选构建记录一致,**不一致即停**;再按剧本逐场景核对(引用日志行,不猜测)。
3. **修复循环**(见下节),完成全部修复步骤。
4. **Standards/Spec 双轴独立审查**(并行全新子代理实例,互不可见;见 `docs/agents/output-review-loop.md` Fresh-instance 规则)。
5. **审查未 CLEAN 前,不得通知用户实机复测**——无论判别实验价值多大。
6. **审查 CLEAN 后**,生成新交付物:
   - Release 构建 DLL(0 errors / 0 warnings)
   - SHA-256 + MVID 记录进当轮 audit 报告(仅授予**可复现构建**的产物,见 IndependentReview-0751 对 `pdbonly` 路径绑定问题的裁决)
   - 交付报告(本轮变更、验证矩阵、判读要点),登记 `audit/README.md`
7. **明确告知用户**:"现在可以开始实机复测",并给出**准确 DLL 路径**(`bin/Release/SteamP2PFriends.dll`)与**部署步骤**(三端覆盖 `BepInEx\plugins\SteamP2PFriends.dll` + `Get-FileHash` 核对命令)。

## 修复代码的标准流程(步骤 3 的展开)

1. **记录审查阻断**(audit 报告/票据注释——阻断内容、发现来源、严重级别)。
2. **冻结期仅只读**;解冻后才动手。
3. **修复根因层**(Resource 接缝、generation 机制、产物身份机制等根因层——不做表面修补)。
4. **重新写红测并确认失败**(红测必须先于修复运行并失败——不允许"写完修复再补测试")。
5. **修复到绿**(红测转绿 + 全套不回归)。
6. **重新编译(0 errors / 0 warnings)、唯一测试入口全绿、静态门禁(diff-check、独立指纹验证)全 PASS**。
7. **重新进行 Standards/Spec 双轴独立审查**(全新并行子代理实例)。
8. **如果仍为 FAIL,继续循环**(回到步骤 1 记录新阻断)。
9. **直到 CLEAN 后才交付 DLL 给用户测试**。

## 门禁生效记录

- 2026-09-08:独立审核实例首次以 FAIL 阻断产物交付——`pdbonly` 构建使 DLL 指纹绑定构建绝对路径,Runtime 证据无法绑定 HEAD(IndependentReview-0751)。该轮代码/测试/Standards 全 PASS,唯独产物身份不可复现——**门禁按设计工作**,指纹机制修复完成并重验前,修复轮交付不生效。

## 判别实验与交付的边界

- 取证/判别构建(instrumentation build)**可以**在复审未 CLEAN 时构建,但**只能自用**(本地采集证据,如 2026-09-03 取证轮 `145BEFAA…` 埋点版),**不得**作为交付物给用户部署指令——本规则追溯生效,取证轮的部署属规则明确前的历史行为,此后不再发生。
- 判别日志(如 `region-snapshot-deferred`、`reason=` 锚行)属于生产诊断仪器,其代码本身按正常红绿+审查流程交付。
