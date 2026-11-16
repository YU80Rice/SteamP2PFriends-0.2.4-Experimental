using HarmonyLib;
using SteamP2PFriends.Core.Patches;
using SteamP2PFriends.Shared;
using System;

namespace SteamP2PFriends
{
    public partial class SteamP2PFriendsPlugin
    {
        private bool RegisterAssetAndAuditPatches()
        {
            _registrationStageFailed = false;
            try
            {
                Core.Patches.AssetIntegritySnapshotPatch.RuntimeProbe();
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", $"AssetIntegritySnapshotPatch.RuntimeProbe 失败: {ex}");
                return false;
            }

            try
            {
                if (!RegisterAssetIntegritySnapshotPatches())
                    return false;
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", $"RegisterAssetIntegritySnapshotPatches 整体异常（阻断注册阶段）: {ex}");
                return false;
            }

            return ApplyV2AuditFixPatches() && !_registrationStageFailed;
        }

        private bool InitializeDiagnosticStage()
        {
            // R28-01：前序诊断阶段的失败必须能作为起点被观察；测试可置位模拟。
            bool ok = !PriorStageFailedForTest;
            // R28-02 测试开关：只跑 Host 枪声模块的折叠路径，便于 stage 级语义用例
            // 直接观察「模块结果是否被采纳」；生产路径该开关恒 false。
            if (!GunshotStageOnlyForTest)
            {
            try
            {
                RunDiagnosticSectionForTest("unity", Core.Patches.UnityLogBridgePatch.Initialize);
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", $"UnityLogBridgePatch.Initialize 失败: {ex}");
                ok = false;
            }

            try
            {
                RunDiagnosticSectionForTest("nativeSns", () => SteamP2PFriends.Client.NativeSnsLogProbe.Enable(RouteDiagnostics.Value, VerboseLog.Value));
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", $"NativeSnsLogProbe.Enable 失败: {ex}");
                ok = false;
            }

            // Bug1 定向测量仪表（默认关闭）：只在显式开启时安装只读 hook，关闭时零副作用。
            try
            {
                // R30-02：RegisterManual 的 false 语义是「安装失败」（harmony 缺失或 Install/Verify 失败），
                // 不是「未启用」——未启用时它在 :38 直接 return true。因此 false 必须折进 stage ok，
                // 不能丢弃；抛异常走既有 catch（日志 + ok=false），保持诊断阶段 fail-closed。
                RunDiagnosticSectionForTest("bug1Probe", () =>
                {
                    if (!Platform.Diagnostics.Patches.Bug1ProbePatch.RegisterManual(_harmony))
                        throw new InvalidOperationException("Bug1ProbePatch.RegisterManual returned false（安装失败或 harmony 缺失）");
                });
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", $"Bug1ProbePatch.RegisterManual 失败: {ex}");
                ok = false;
            }

            // t45：仪表的生产开启入口（BepInEx 配置文件，默认关闭）。没有它 Enable 永远不会被调用。
            try
            {
                RunDiagnosticSectionForTest("bug1ProbeControl", Platform.Diagnostics.Patches.Bug1ProbeControl.Initialize);
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", $"Bug1ProbeControl.Initialize 失败: {ex}");
                ok = false;
            }

            }

            // t116：Host 枪声叶补偿（默认关闭，独立节 [Bug2HostGunshot]）。未开启时零 patch。
            // R27-01：模块结果如何折进 stage ok 只由纯函数 FoldGunshotOutcome 决定。
            // 生产侧只做一次中间量传递，用例穷举 stageOk × moduleOk 四组真值表，
            // 「把失败翻回 true」的语义反转会直接违反真值表断言，不再依赖 IL 形状。
            bool gunshotModuleOk = ApplyGunshotModuleOutcome(true, _harmony);
            ok = FoldGunshotOutcome(ok, gunshotModuleOk);

            // t85：G 门实验探针（默认关闭，独立节 [Bug1Gate]）。未开启时零日志、零帧回调。
            if (!GunshotStageOnlyForTest)
            {
            try
            {
                RunDiagnosticSectionForTest("gate", Platform.Diagnostics.Patches.Bug1GateProbe.Initialize);
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", $"Bug1GateProbe.Initialize 失败: {ex}");
                ok = false;
            }
            }
            return ok;
        }

        // R25-03 测试缝：替换 Host 枪声模块的初始化动作（默认走生产 Initialize）。
        internal static Func<HarmonyLib.Harmony, bool> GunshotModuleInitializerForTest;

        // R28-01/R28-02 测试开关：默认 false；用例 finally 调 ResetDiagnosticsStageForTest。
        internal static bool PriorStageFailedForTest;
        internal static bool GunshotStageOnlyForTest;

        // R30-01 测试缝：把「某段的执行动作」替换成包装动作（section, real）→ wrapper。
        // RunDiagnosticSectionForTest 仍以唯一调用点 `a()` 执行，注入只能包装、不能跳过；
        // 生产无任何代码路径写入该字段（Reset 清零），故生产恒走真实 action。
        internal static Func<string, Action, Action> DiagnosticStageActionForTest;

        // R31-01 生产侧计数：每进入一次唯一执行点前自增；用于证明生产分支（seam == null）
        // 五段都真的进入，而不是只验证注入分支。
        // 注意：本字段是进程内累计值，任何断言前都必须先调用 ResetDiagnosticsStageForTest()，
        // 否则重复初始化/前序用例会让「== 5」误红（只会误红，不会把未执行段算成已执行）。
        internal static int DiagnosticSectionsEnteredForTest;

        internal static void ResetDiagnosticsStageForTest()
        {
            PriorStageFailedForTest = false;
            GunshotStageOnlyForTest = false;
            DiagnosticStageActionForTest = null;
            DiagnosticSectionsEnteredForTest = 0;
        }

        // R30-01：唯一的动作执行点。seam 只能替换/包装动作，不能跳过调用；
        // 生产（seam == null）直接执行真实 action，零行为变化。
        private static void RunDiagnosticSectionForTest(string section, Action action)
        {
            Func<string, Action, Action> seam = DiagnosticStageActionForTest;
            Action a = seam != null ? seam(section, action) : action;
            // R31-02 测试契约守卫：生产不可达（seam 生产恒 null，a 即真实 action 非空）；
            // 保留它是为了防止测试 seam 返回 null 时静默跳过，令进入计数与真实执行点脱钩。
            if (a == null)
                throw new InvalidOperationException("diagnostic stage action is null for section: " + section);
            // 计数包在唯一调用点的执行体里：a() 真的被调用才计数。
            // 若调用点被改成 if (seam != null) a();，生产模式下计数保持 0（N2 由此变红）。
            Action counted = a;
            a = () =>
            {
                DiagnosticSectionsEnteredForTest++;
                counted();
            };
            a();
        }

        /// <summary>
        /// R25-03：把「Host 枪声模块初始化结果」并入诊断阶段的 ok。
        /// 任何 false/异常都必须让调用方拿到 false（ok 永不置 false 的变异会被用例抓到）。
        /// </summary>
        internal static bool ApplyGunshotModuleOutcome(bool ok, HarmonyLib.Harmony harmony)
        {
            try
            {
                Func<HarmonyLib.Harmony, bool> initializer = GunshotModuleInitializerForTest
                    ?? Core.Patches.GunshotHostAudio.GunshotHostAudioControl.Initialize;
                if (!initializer(harmony))
                {
                    RoleLogger.Error("[Shared]", "GunshotHostAudioControl.Initialize 返回 false（fail-closed）");
                    return false;
                }
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", $"GunshotHostAudioControl.Initialize 失败: {ex}");
                return false;
            }

            return ok;
        }

        /// <summary>
        /// R27-01：模块结果折进 stage ok 的唯一语义（纯函数，不读任何静态状态）。
        /// 仅当 stage 原本为 true 且模块成功时才保持 true；任何失败都必须得到 false，
        /// 不允许把失败「翻回」true。真值表由 Bug2-34 穷举。
        /// </summary>
        internal static bool FoldGunshotOutcome(bool stageOk, bool moduleOk)
        {
            return stageOk && moduleOk;
        }
    }
}
