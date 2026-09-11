using HarmonyLib;
using SDG.Unturned;
using SteamP2PFriends.Shared;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace SteamP2PFriends.Adapters.Zombie.Patches
{
    /// <summary>
    /// Ticket 01（listen-host-dedicated-gate）：僵尸重生 Listen-Host Dedicated Gate。
    ///
    /// 目标（Assembly-CSharp / U3-SDK ZombieManager.cs）：
    ///   public void respawnZombies()（instance、void、0 参），由 ZombieManager.Update 尾部
    ///   每帧调用一次并按 respawnZombiesBound 轮转区域。方法体内 Dedicator.get_IsDedicatedServer
    ///   调用点全方法恰为 1 处，位于唯一早退守卫：
    ///     if (!flagData[bound].spawnZombies
    ///         || region.zombies.Count &lt;= 0
    ///         || (!Dedicator.IsDedicatedServer && !region.hasBeacon
    ///             && Level.info.type != ELevelType.HORDE)
    ///         || (region.hasBeacon && BeaconManager.checkBeacon(bound).getRemaining() == 0))
    ///       return;
    ///
    /// 根因：listen host 上 IsDedicatedServer=false，普通 PEI 区域（无信标、非 Horde 图）
    ///   第三个子句恒真 → 打死僵尸后永不重生（旧仓「僵尸不刷新」根因，评审 §4.1）。
    ///
    /// 修法：与 ZombieManagerP0C1SendZombieStatesPatch 同款单点 Transpiler，把该
    ///   get_IsDedicatedServer 调用替换为 ListenRegionSyncEligibility.IsDedicatedOrP2PHost()。
    ///   替换后语义：
    ///     - dedicated：资格恒 true，vanilla 行为不变；
    ///     - listen host：资格 true → 该子句短路为 false → 进入 vanilla 专用服重生分支
    ///       （生成表抽取、特化、Boss 上限、信标剩余、respawnZombieIndex 轮转、日夜窗口全部原版）;
    ///     - 普通单机/客机/菜单：资格 false，早退语义与原版一致；
    ///     - Horde 图/信标区域：原子句在 vanilla 下本就为 false（hasBeacon 或 HORDE），不受影响。
    ///
    /// 栈平衡：
    ///   原版：call get_IsDedicatedServer()（无参数，返回 bool i4）=> 栈净变化 +1
    ///   替换：call IsDedicatedOrP2PHost()（无参数，返回 bool i4）=> 栈净变化 +1，一致；
    ///   紧随其后的短路分支指令（brtrue）原样保留。
    ///
    /// 诊断（观测式，不构成第二套生成器，见 §RespawnGateProbe）：
    ///   Prefix/Postfix 观察守卫通过后唯一确定性副作用 respawnZombieIndex 轮转，
    ///   按 bound 累计 calls/passed/returned/ambiguous，用于 Runtime 区分
    ///   「重生窗口未到」（passed>0）与「早退仍在」（elig=true 但 passed 恒 0）。
    ///
    /// 不触碰：SendZombieStates 门控（已由 P0-C1 对齐，不再修改）、generateZombies 进图预生成、
    ///   全量 tick 切片（Update 内 dedicated 分支）、Horde 波次、Beacon 剩余、生成表与特化。
    /// </summary>
    public static class ZombieManagerRespawnZombiesDedicatedGatePatch
    {
        public static bool AllRegistrationsSucceeded { get; private set; }
        public static string RegistrationSummary { get; private set; } = "未登记";
        public static int ReplacementCount { get; private set; } = -1;
        public static bool SignatureResolved { get; private set; }
        public static string SignatureSummary { get; private set; } = "未自检";

        public static bool TranspilerOwnerVerified { get; private set; }
        public static string TranspilerOwnerSummary { get; private set; } = "未自检";

        public static bool RespawnProbePrefixRegistered { get; private set; }
        public static bool RespawnProbePostfixRegistered { get; private set; }

        private const string HarmonyId = SteamP2PFriendsPlugin.HARMONY_ID;
        private const string TargetMethodName = "respawnZombies";
        private const string PatchTranspilerName = nameof(RespawnZombies_Transpiler);
        private const string PatchProbePrefixName = nameof(RespawnZombies_Probe_Prefix);
        private const string PatchProbePostfixName = nameof(RespawnZombies_Probe_Postfix);

        private const string DiagLogPrefix = "[RespawnGateDiag/Zombie]";
        private const string DiagQuotaPointId = "Zombie.respawnZombies";
        private const float DiagLogInterval = 5.0f;

        private static float _lastDiagLogTime = -100f;

        private static FieldInfo _respawnZombiesBoundField;

        static ZombieManagerRespawnZombiesDedicatedGatePatch()
        {
            Core.Patches.WorldSyncDiagnosticCore.RegisterSessionResetCallback(() =>
            {
                _lastDiagLogTime = -100f;
                ResetProbeCounters();
            });
        }

        public static bool RegisterManual(Harmony harmony)
        {
            RoleLogger.Info("[Shared]", "[RespawnGate/Zombie] === 手动登记 Transpiler+Probe（ticket01 僵尸重生 Listen-Host Dedicated Gate）===");

            if (harmony == null)
            {
                AllRegistrationsSucceeded = false;
                RegistrationSummary = "harmony=null";
                RoleLogger.Error("[Shared]", $"[RespawnGate/Zombie] !!! {RegistrationSummary}");
                return false;
            }

            bool sigOk = VerifyTargetSignature();
            if (!sigOk)
            {
                AllRegistrationsSucceeded = false;
                RegistrationSummary = $"respawnZombies 签名自检失败 ({SignatureSummary})";
                RoleLogger.Error("[Shared]", $"[RespawnGate/Zombie] !!! {RegistrationSummary}");
                return false;
            }

            bool transpilerOk = RegisterTranspiler(harmony);
            if (!transpilerOk)
            {
                AllRegistrationsSucceeded = false;
                RegistrationSummary = $"Transpiler 登记失败 (replacement={ReplacementCount})";
                RoleLogger.Error("[Shared]", $"[RespawnGate/Zombie] !!! {RegistrationSummary}");
                return false;
            }

            bool probeOk = RegisterProbeHooks(harmony);
            if (!probeOk)
            {
                AllRegistrationsSucceeded = false;
                RegistrationSummary = $"重生门诊断 hook 登记失败 (pre={RespawnProbePrefixRegistered}, post={RespawnProbePostfixRegistered})";
                RoleLogger.Error("[Shared]", $"[RespawnGate/Zombie] !!! {RegistrationSummary}");
                return false;
            }

            AllRegistrationsSucceeded = true;
            RegistrationSummary = $"signature={SignatureResolved}, replacement=1/1, transpilerOwner={TranspilerOwnerVerified}, " +
                $"probePre={RespawnProbePrefixRegistered}, probePost={RespawnProbePostfixRegistered}";
            RoleLogger.Info("[Shared]",
                $"[RespawnGate/Zombie] OK 手动登记成功 summary={RegistrationSummary}");
            return true;
        }

        private static bool RegisterProbeHooks(Harmony harmony)
        {
            var patchType = typeof(ZombieManagerRespawnZombiesDedicatedGatePatch);

            RespawnProbePrefixRegistered = Core.Patches.WorldSyncDiagnosticCore.RegisterIdentityPatch(
                harmony, typeof(ZombieManager), TargetMethodName, System.Type.EmptyTypes,
                AccessTools.Method(patchType, PatchProbePrefixName),
                HarmonyPatchType.Prefix, "Zombie.respawnZombies.Probe.Pre");

            RespawnProbePostfixRegistered = Core.Patches.WorldSyncDiagnosticCore.RegisterIdentityPatch(
                harmony, typeof(ZombieManager), TargetMethodName, System.Type.EmptyTypes,
                AccessTools.Method(patchType, PatchProbePostfixName),
                HarmonyPatchType.Postfix, "Zombie.respawnZombies.Probe.Post");

            return RespawnProbePrefixRegistered && RespawnProbePostfixRegistered;
        }

        private static bool RegisterTranspiler(Harmony harmony)
        {
            try
            {
                MethodInfo original = AccessTools.Method(typeof(ZombieManager), TargetMethodName, System.Type.EmptyTypes);
                if (original == null)
                {
                    RoleLogger.Error("[Shared]", "[RespawnGate/Zombie] !!! respawnZombies AccessTools.Method 返回 null");
                    return false;
                }

                MethodInfo transpiler = AccessTools.Method(typeof(ZombieManagerRespawnZombiesDedicatedGatePatch), PatchTranspilerName);
                if (transpiler == null)
                {
                    RoleLogger.Error("[Shared]", "[RespawnGate/Zombie] !!! Transpiler 方法未找到");
                    return false;
                }

                harmony.Patch(original, transpiler: new HarmonyMethod(transpiler));

                if (ReplacementCount != 1)
                {
                    RoleLogger.Error("[Shared]",
                        $"[RespawnGate/Zombie] !!! DIAGNOSTIC BUILD INVALID: replacement count={ReplacementCount} 期望=1");
                    return false;
                }

                bool ownerOk = VerifyPatchOwner(original);
                if (!ownerOk)
                {
                    RoleLogger.Error("[Shared]",
                        $"[RespawnGate/Zombie] !!! DIAGNOSTIC BUILD INVALID: Transpiler owner 自检失败 summary={TranspilerOwnerSummary}");
                    return false;
                }

                RoleLogger.Info("[Shared]",
                    $"[RespawnGate/Zombie] OK Transpiler 已登记 (replacement=1/1, owner={TranspilerOwnerVerified})");
                return true;
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[RespawnGate/Zombie] !!! RegisterTranspiler 异常: {ex}");
                return false;
            }
        }

        private static bool VerifyPatchOwner(MethodInfo original)
        {
            try
            {
                HarmonyLib.Patches info = Harmony.GetPatchInfo(original);
                System.Collections.ICollection patches = info?.Transpilers as System.Collections.ICollection;

                if (patches == null || patches.Count == 0)
                {
                    TranspilerOwnerVerified = false;
                    TranspilerOwnerSummary = "transpilers count=0";
                    return false;
                }

                int ownCount = 0;
                bool methodMatched = false;
                string firstForeignOwner = null;

                foreach (Patch p in patches)
                {
                    if (p.owner == HarmonyId)
                    {
                        ownCount++;
                        MethodInfo patchMethod = p.PatchMethod;
                        if (patchMethod != null
                            && patchMethod.DeclaringType == typeof(ZombieManagerRespawnZombiesDedicatedGatePatch)
                            && patchMethod.Name == PatchTranspilerName)
                        {
                            methodMatched = true;
                        }
                    }
                    else if (firstForeignOwner == null)
                    {
                        firstForeignOwner = p.owner;
                    }
                }

                string summary = $"ownCount={ownCount} methodMatched={methodMatched} foreignOwner={firstForeignOwner ?? "none"}";

                if (ownCount != 1 || !methodMatched)
                {
                    TranspilerOwnerVerified = false;
                    TranspilerOwnerSummary = summary;
                    return false;
                }

                TranspilerOwnerVerified = true;
                TranspilerOwnerSummary = summary;
                return true;
            }
            catch (System.Exception ex)
            {
                TranspilerOwnerVerified = false;
                TranspilerOwnerSummary = $"异常: {ex.Message}";
                return false;
            }
        }

        private static bool VerifyTargetSignature()
        {
            try
            {
                MethodInfo method = AccessTools.Method(typeof(ZombieManager), TargetMethodName, System.Type.EmptyTypes);
                if (method == null)
                {
                    SignatureResolved = false;
                    SignatureSummary = "AccessTools.Method 返回 null";
                    return false;
                }

                if (method.Name != TargetMethodName)
                {
                    SignatureResolved = false;
                    SignatureSummary = $"Name={method.Name} 期望={TargetMethodName}";
                    return false;
                }

                if (method.IsStatic)
                {
                    SignatureResolved = false;
                    SignatureSummary = "IsStatic=true 期望=false";
                    return false;
                }

                ParameterInfo[] ps = method.GetParameters();
                if (ps.Length != 0)
                {
                    SignatureResolved = false;
                    SignatureSummary = $"paramCount={ps.Length} 期望=0";
                    return false;
                }

                if (method.ReturnType != typeof(void))
                {
                    SignatureResolved = false;
                    SignatureSummary = $"ReturnType={method.ReturnType.Name} 期望=void";
                    return false;
                }

                SignatureResolved = true;
                SignatureSummary = "public instance void respawnZombies()";
                RoleLogger.Info("[Shared]", $"[RespawnGate/Zombie] OK 签名自检通过: {SignatureSummary}");
                return true;
            }
            catch (System.Exception ex)
            {
                SignatureResolved = false;
                SignatureSummary = $"异常: {ex.Message}";
                RoleLogger.Error("[Shared]", $"[RespawnGate/Zombie] !!! 签名自检异常: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 替换 vanilla respawnZombies 早退守卫中的 Dedicator.get_IsDedicatedServer() 调用
        /// 为 ListenRegionSyncEligibility.IsDedicatedOrP2PHost()。
        ///
        /// 栈平衡：原版 call get_IsDedicatedServer()（0 参，bool i4 返回，净 +1）；
        ///   替换 call IsDedicatedOrP2PHost()（0 参，bool i4 返回，净 +1），一致。
        ///
        /// ReplacementCount 必须精确等于 1（该调用点是 respawnZombies 方法体内唯一一处
        /// IsDedicatedServer 访问；ZG1 契约测试锁死这一前提）。
        /// </summary>
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(ZombieManager), TargetMethodName)]
        public static IEnumerable<CodeInstruction> RespawnZombies_Transpiler(
            IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var codes = new List<CodeInstruction>(instructions);

            MethodInfo dedicatedGetter = AccessTools.PropertyGetter(typeof(Dedicator), nameof(Dedicator.IsDedicatedServer));
            MethodInfo eligibilityMethod = AccessTools.Method(
                typeof(ListenRegionSyncEligibility),
                nameof(ListenRegionSyncEligibility.IsDedicatedOrP2PHost),
                System.Type.EmptyTypes);

            if (dedicatedGetter == null)
            {
                ReplacementCount = -1;
                throw new System.InvalidOperationException(
                    "ZombieManagerRespawnZombiesDedicatedGatePatch: Dedicator.get_IsDedicatedServer not found");
            }

            if (eligibilityMethod == null)
            {
                ReplacementCount = -1;
                throw new System.InvalidOperationException(
                    "ZombieManagerRespawnZombiesDedicatedGatePatch: IsDedicatedOrP2PHost not found");
            }

            int replacementCount = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction instr = codes[i];
                if (instr == null) continue;

                if (instr.Calls(dedicatedGetter))
                {
                    instr.opcode = OpCodes.Call;
                    instr.operand = eligibilityMethod;
                    replacementCount++;
                }
            }

            ReplacementCount = replacementCount;

            if (replacementCount != 1)
            {
                throw new System.InvalidOperationException(
                    $"ZombieManagerRespawnZombiesDedicatedGatePatch: replacement count={replacementCount} expected=1");
            }

            return codes;
        }

        // ============= 重生门诊断探针（观测式，不生成任何实体） =============

        /// <summary>
        /// Prefix：仅记录进入 respawnZombies 时的 bound、region.respawnZombieIndex 与
        /// zombies.Count。不读取/改写守卫的任何其他条件，不做资格判断副本。
        /// Verbose 关闭时立即返回（反射与字典访问全部跳过，每帧成本≈1 次 bool 读）。
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ZombieManager), TargetMethodName)]
        internal static void RespawnZombies_Probe_Prefix(ref ProbeState __state)
        {
            __state = null;
            try
            {
                if (!PluginLogPolicy.IsVerboseDiagnosticsEnabled) return;

                ZombieRegion[] regions = ZombieManager.regions;
                if (regions == null) return;

                // int 哨兵避免与合法 bound（含 255）混叠；越界/类型异常一律 -1。
                int boundRaw = ReadRespawnZombiesBound();
                if (boundRaw < 0) return;
                if (boundRaw >= regions.Length) return;
                byte bound = (byte)boundRaw;

                ZombieRegion region = regions[bound];
                if (region == null) return;
                if (region.zombies == null) return;

                __state = new ProbeState
                {
                    Bound = bound,
                    IndexBefore = region.respawnZombieIndex,
                    ZombieCountBefore = region.zombies.Count
                };
            }
            catch
            {
                __state = null;
            }
        }

        /// <summary>
        /// Postfix：守卫通过的唯一确定性副作用是 respawnZombieIndex 轮转（vanilla 过守卫后
        /// 无条件 index++/clamp，早于 isDead/重生窗口/安全区等所有后续检查）。
        /// 观测结果按 bound 累计；日志限频（时间窗 + PerPointLimit），计数不受限频影响。
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ZombieManager), TargetMethodName)]
        internal static void RespawnZombies_Probe_Postfix(ProbeState __state)
        {
            try
            {
                if (__state == null) return;

                ZombieRegion[] regions = ZombieManager.regions;
                if (regions == null || __state.Bound >= regions.Length) return;

                ZombieRegion region = regions[__state.Bound];
                if (region == null || region.zombies == null) return;

                RespawnGateObservation observation = ObservePass(
                    __state.ZombieCountBefore, __state.IndexBefore, region.respawnZombieIndex);

                Accumulate(__state.Bound, observation);

                float now = UnityEngine.Time.realtimeSinceStartup;
                if (now - _lastDiagLogTime < DiagLogInterval) return;
                _lastDiagLogTime = now;

                if (!Core.Patches.WorldSyncDiagnosticCore.TryAcquireQuota(DiagQuotaPointId, out int count)) return;

                BoundCounters counters = SnapshotCounters(__state.Bound);
                RespawnGateTotals totals = AggregateTotals();

                RoleLogger.Info("[Host]",
                    $"{DiagLogPrefix} respawnZombies #{count}/{Core.Patches.WorldSyncDiagnosticCore.PerPointLimit} " +
                    $"elig={ListenRegionSyncEligibility.IsDedicatedOrP2PHost()} " +
                    $"bound={__state.Bound} calls={counters.Calls} passed={counters.Passed} " +
                    $"returned={counters.Returned} ambiguous={counters.Ambiguous} " +
                    $"allPassed={totals.Passed} allReturned={totals.Returned} allAmbiguous={totals.Ambiguous} " +
                    $"(passed=守卫通过进入对齐分支; returned=仍走早退; ambiguous=单僵尸区域轮转不可判)");
            }
            catch
            {
            }
        }

        /// <summary>守卫观测结论（封闭枚举，unknown 必须显式具名，禁止参与算术）。</summary>
        internal enum RespawnGateObservation
        {
            NotObserved,
            PassedAlignedGate,
            EarlyReturned,
            AmbiguousSingleZombie
        }

        /// <summary>
        /// 纯函数：由 (count, indexBefore, indexAfter) 判定本次调用是否通过了整体早退守卫。
        /// vanilla 语义：过守卫后 respawnZombieIndex 无条件先 clamp 再 ++（到界回 0），
        /// 因此 count>=2 时轮转必然发生且结果 != 入参；早退则索引原样保留。
        /// count==1 时过守卫必把索引收敛回 0：before!=after（陈旧 >0 索引被 clamp）判 Passed，
        /// 0→0 为过守卫与早退唯一不可区分形态判 Ambiguous，其余原样保留判 EarlyReturned。
        /// </summary>
        internal static RespawnGateObservation ObservePass(int regionZombieCount, ushort indexBefore, ushort indexAfter)
        {
            // vanilla 守卫第三子句 zombies.Count<=0 ⇒ 空区域必早退，轮转不可能发生。
            if (regionZombieCount <= 0) return RespawnGateObservation.EarlyReturned;

            // 过守卫后 clamp+index++：count>=2 时结果必与入参不同；count==1 时过守卫
            // 必把索引收敛回 0（陈旧 >0 索引被 clamp 的情形因此可判 Passed）。
            if (indexAfter != indexBefore) return RespawnGateObservation.PassedAlignedGate;

            // 索引原样保留：
            //   count==1 且索引 0→0 —— 过守卫（回绕）与早退唯一不可区分的形态，显式具名；
            //   其余（count>=2 不动，或 count==1 的陈旧 >0 索引不动）——过守卫必然改变索引，故为早退。
            if (regionZombieCount == 1 && indexBefore == 0)
            {
                return RespawnGateObservation.AmbiguousSingleZombie;
            }

            return RespawnGateObservation.EarlyReturned;
        }

        internal sealed class ProbeState
        {
            public byte Bound;
            public ushort IndexBefore;
            public int ZombieCountBefore;
        }

        private sealed class BoundCounters
        {
            public int Calls;
            public int Passed;
            public int Returned;
            public int Ambiguous;
        }

        private struct RespawnGateTotals
        {
            public int Passed;
            public int Returned;
            public int Ambiguous;
        }

        private static readonly Dictionary<byte, BoundCounters> _probeCounters =
            new Dictionary<byte, BoundCounters>();
        private static readonly object _probeLock = new object();

        /// <summary>读 vanilla 私有静态 respawnZombiesBound；不可解析或类型异常返回 -1。</summary>
        private static int ReadRespawnZombiesBound()
        {
            try
            {
                if (_respawnZombiesBoundField == null)
                {
                    _respawnZombiesBoundField = AccessTools.Field(
                        typeof(ZombieManager), "respawnZombiesBound");
                }
                if (_respawnZombiesBoundField == null) return -1;

                object value = _respawnZombiesBoundField.GetValue(null);
                if (value is byte b) return b;
                return -1;
            }
            catch
            {
                return -1;
            }
        }

        private static void Accumulate(byte bound, RespawnGateObservation observation)
        {
            try
            {
                lock (_probeLock)
                {
                    if (!_probeCounters.TryGetValue(bound, out BoundCounters counters))
                    {
                        counters = new BoundCounters();
                        _probeCounters[bound] = counters;
                    }
                    counters.Calls++;
                    switch (observation)
                    {
                        case RespawnGateObservation.PassedAlignedGate: counters.Passed++; break;
                        case RespawnGateObservation.EarlyReturned: counters.Returned++; break;
                        case RespawnGateObservation.AmbiguousSingleZombie: counters.Ambiguous++; break;
                    }
                }
            }
            catch
            {
            }
        }

        private static BoundCounters SnapshotCounters(byte bound)
        {
            lock (_probeLock)
            {
                if (_probeCounters.TryGetValue(bound, out BoundCounters counters))
                {
                    return new BoundCounters
                    {
                        Calls = counters.Calls,
                        Passed = counters.Passed,
                        Returned = counters.Returned,
                        Ambiguous = counters.Ambiguous
                    };
                }
            }
            return new BoundCounters();
        }

        private static RespawnGateTotals AggregateTotals()
        {
            var totals = new RespawnGateTotals();
            lock (_probeLock)
            {
                foreach (var pair in _probeCounters)
                {
                    totals.Passed += pair.Value.Passed;
                    totals.Returned += pair.Value.Returned;
                    totals.Ambiguous += pair.Value.Ambiguous;
                }
            }
            return totals;
        }

        private static void ResetProbeCounters()
        {
            lock (_probeLock)
            {
                _probeCounters.Clear();
            }
        }
    }
}
