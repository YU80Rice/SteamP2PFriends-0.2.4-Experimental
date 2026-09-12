using HarmonyLib;
using SDG.Unturned;
using SteamP2PFriends.Shared;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace SteamP2PFriends.Adapters.Item.Patches
{
    /// <summary>
    /// Ticket 02（listen-host-dedicated-gate）：物品周期生命周期 Listen-Host Dedicated Gate。
    ///
    /// 目标（Assembly-CSharp / U3-SDK ItemManager.cs）：
    ///   private void Update()（instance、void、0 参）。方法体尾部唯一早退守卫：
    ///     if (!Dedicator.IsDedicatedServer || !Level.isLoaded)
    ///       return;
    ///   守卫之后是两组 do-while 轮转：despawnItems()（despawnItems_X/Y 游标，按
    ///   Despawn_Dropped/Natural_Time 周期移除过期物品并广播 SendDestroyItem）与
    ///   respawnItems()（respawnItems_X/Y 游标，lastRespawn 过 Respawn_Time 且未满
    ///   spawns.Count*Spawn_Chance 时补齐并广播 SendItem）。该守卫是 Update 方法体内
    ///   Dedicator.get_IsDedicatedServer 的唯一调用点（IG1 锁定）。
    ///
    /// 根因：listen host 上 IsDedicatedServer=false，周期 despawn/respawn 轮转根本不跑，
    ///   地面物品只靠本地玩家进区时的一次性 generateItems「只刷新一次」（旧仓根因，
    ///   评审 §4.1；spec 问题陈述）。
    ///
    /// 修法：与 Ticket01 respawnZombies 同款单点 Transpiler，把该 get_IsDedicatedServer
    ///   调用替换为 ListenRegionSyncEligibility.IsDedicatedOrP2PHost()。替换后语义：
    ///     - dedicated：资格恒 true，vanilla 行为不变；
    ///     - listen host：资格 true → 资格子句短路为 false → 守卫仅取决于 Level.isLoaded，
    ///       进入 vanilla 专用服周期轮转（despawn 窗口、Respawn_Time、Spawn_Chance 目标量、
    ///       safezone/间距校验、SendDestroyItem/SendItem 广播全部原版）；
    ///     - 普通单机/客机/菜单：资格 false，早退语义与原版一致（单机节奏不被插件改掉）。
    ///
    /// 不触碰：onLevelLoaded 一次性预生成（spec 明令保留，禁止关闭/缩小/重写）、
    ///   onRegionUpdated 的 generateItems 调用与 askItems 门控（P0-B 系列已对齐）、
    ///   AuthoritativeItemGenerationGatePatch 的每区域每会话一次账本（现有防线）、
    ///   despawnItems/respawnItems 生成算法本体、Dedicator.IsDedicatedServer（不全局伪造）。
    /// </summary>
    public static class ItemManagerUpdateDedicatedGatePatch
    {
        public static bool AllRegistrationsSucceeded { get; private set; }
        public static string RegistrationSummary { get; private set; } = "未登记";
        public static int ReplacementCount { get; private set; } = -1;
        public static bool SignatureResolved { get; private set; }
        public static string SignatureSummary { get; private set; } = "未自检";

        public static bool TranspilerOwnerVerified { get; private set; }
        public static string TranspilerOwnerSummary { get; private set; } = "未自检";

        public static bool GenerateProbePrefixRegistered { get; private set; }
        public static bool DespawnProbePrefixRegistered { get; private set; }
        public static bool DespawnProbePostfixRegistered { get; private set; }
        public static bool RespawnProbePrefixRegistered { get; private set; }
        public static bool RespawnProbePostfixRegistered { get; private set; }

        private const string HarmonyId = SteamP2PFriendsPlugin.HARMONY_ID;
        private const string TargetMethodName = "Update";
        private const string PatchTranspilerName = nameof(Update_Transpiler);
        private const string PatchGeneratePrefixName = nameof(GenerateItems_Probe_Prefix);
        private const string PatchDespawnPrefixName = nameof(DespawnItems_Probe_Prefix);
        private const string PatchDespawnPostfixName = nameof(DespawnItems_Probe_Postfix);
        private const string PatchRespawnPrefixName = nameof(RespawnItems_Probe_Prefix);
        private const string PatchRespawnPostfixName = nameof(RespawnItems_Probe_Postfix);

        private const string DiagLogPrefix = "[ItemGateDiag/Item]";
        private const string DiagQuotaGenerateId = "Item.generateItems";
        private const string DiagQuotaDespawnId = "Item.despawnItems";
        private const string DiagQuotaRespawnId = "Item.respawnItems";
        private const float DiagLogInterval = 5.0f;

        private static float _lastGenerateLogTime = -100f;
        private static float _lastDespawnLogTime = -100f;
        private static float _lastRespawnLogTime = -100f;

        private static FieldInfo _despawnCursorXField;
        private static FieldInfo _despawnCursorYField;
        private static FieldInfo _respawnCursorXField;
        private static FieldInfo _respawnCursorYField;

        static ItemManagerUpdateDedicatedGatePatch()
        {
            Core.Patches.WorldSyncDiagnosticCore.RegisterSessionResetCallback(() =>
            {
                _lastGenerateLogTime = -100f;
                _lastDespawnLogTime = -100f;
                _lastRespawnLogTime = -100f;
                ResetProbeCounters();
            });
        }

        public static bool RegisterManual(Harmony harmony)
        {
            RoleLogger.Info("[Shared]", "[ItemGate/Item] === 手动登记 Transpiler+Probe（ticket02 物品周期生命周期 Listen-Host Dedicated Gate）===");

            if (harmony == null)
            {
                AllRegistrationsSucceeded = false;
                RegistrationSummary = "harmony=null";
                RoleLogger.Error("[Shared]", $"[ItemGate/Item] !!! {RegistrationSummary}");
                return false;
            }

            bool sigOk = VerifyTargetSignature();
            if (!sigOk)
            {
                AllRegistrationsSucceeded = false;
                RegistrationSummary = $"ItemManager.Update 签名自检失败 ({SignatureSummary})";
                RoleLogger.Error("[Shared]", $"[ItemGate/Item] !!! {RegistrationSummary}");
                return false;
            }

            bool transpilerOk = RegisterTranspiler(harmony);
            if (!transpilerOk)
            {
                AllRegistrationsSucceeded = false;
                RegistrationSummary = $"Transpiler 登记失败 (replacement={ReplacementCount})";
                RoleLogger.Error("[Shared]", $"[ItemGate/Item] !!! {RegistrationSummary}");
                return false;
            }

            bool probeOk = RegisterProbeHooks(harmony);
            if (!probeOk)
            {
                AllRegistrationsSucceeded = false;
                RegistrationSummary = $"物品门诊断 hook 登记失败 (gen={GenerateProbePrefixRegistered}, " +
                    $"despawnPre={DespawnProbePrefixRegistered}, despawnPost={DespawnProbePostfixRegistered}, " +
                    $"respawnPre={RespawnProbePrefixRegistered}, respawnPost={RespawnProbePostfixRegistered})";
                RoleLogger.Error("[Shared]", $"[ItemGate/Item] !!! {RegistrationSummary}");
                return false;
            }

            AllRegistrationsSucceeded = true;
            RegistrationSummary = $"signature={SignatureResolved}, replacement=1/1, transpilerOwner={TranspilerOwnerVerified}, " +
                $"genPre={GenerateProbePrefixRegistered}, despawnPre={DespawnProbePrefixRegistered}, " +
                $"despawnPost={DespawnProbePostfixRegistered}, respawnPre={RespawnProbePrefixRegistered}, " +
                $"respawnPost={RespawnProbePostfixRegistered}";
            RoleLogger.Info("[Shared]",
                $"[ItemGate/Item] OK 手动登记成功 summary={RegistrationSummary}");
            return true;
        }

        private static bool RegisterProbeHooks(Harmony harmony)
        {
            var patchType = typeof(ItemManagerUpdateDedicatedGatePatch);
            var itemRegionParams = new System.Type[] { typeof(byte), typeof(byte) };

            GenerateProbePrefixRegistered = Core.Patches.WorldSyncDiagnosticCore.RegisterIdentityPatch(
                harmony, typeof(ItemManager), "generateItems", itemRegionParams,
                AccessTools.Method(patchType, PatchGeneratePrefixName),
                HarmonyPatchType.Prefix, "Item.generateItems.Diag.Pre");

            DespawnProbePrefixRegistered = Core.Patches.WorldSyncDiagnosticCore.RegisterIdentityPatch(
                harmony, typeof(ItemManager), "despawnItems", System.Type.EmptyTypes,
                AccessTools.Method(patchType, PatchDespawnPrefixName),
                HarmonyPatchType.Prefix, "Item.despawnItems.Diag.Pre");

            DespawnProbePostfixRegistered = Core.Patches.WorldSyncDiagnosticCore.RegisterIdentityPatch(
                harmony, typeof(ItemManager), "despawnItems", System.Type.EmptyTypes,
                AccessTools.Method(patchType, PatchDespawnPostfixName),
                HarmonyPatchType.Postfix, "Item.despawnItems.Diag.Post");

            RespawnProbePrefixRegistered = Core.Patches.WorldSyncDiagnosticCore.RegisterIdentityPatch(
                harmony, typeof(ItemManager), "respawnItems", System.Type.EmptyTypes,
                AccessTools.Method(patchType, PatchRespawnPrefixName),
                HarmonyPatchType.Prefix, "Item.respawnItems.Diag.Pre");

            RespawnProbePostfixRegistered = Core.Patches.WorldSyncDiagnosticCore.RegisterIdentityPatch(
                harmony, typeof(ItemManager), "respawnItems", System.Type.EmptyTypes,
                AccessTools.Method(patchType, PatchRespawnPostfixName),
                HarmonyPatchType.Postfix, "Item.respawnItems.Diag.Post");

            return GenerateProbePrefixRegistered
                && DespawnProbePrefixRegistered && DespawnProbePostfixRegistered
                && RespawnProbePrefixRegistered && RespawnProbePostfixRegistered;
        }

        private static bool RegisterTranspiler(Harmony harmony)
        {
            try
            {
                MethodInfo original = AccessTools.Method(typeof(ItemManager), TargetMethodName, System.Type.EmptyTypes);
                if (original == null)
                {
                    RoleLogger.Error("[Shared]", "[ItemGate/Item] !!! ItemManager.Update AccessTools.Method 返回 null");
                    return false;
                }

                MethodInfo transpiler = AccessTools.Method(typeof(ItemManagerUpdateDedicatedGatePatch), PatchTranspilerName);
                if (transpiler == null)
                {
                    RoleLogger.Error("[Shared]", "[ItemGate/Item] !!! Transpiler 方法未找到");
                    return false;
                }

                harmony.Patch(original, transpiler: new HarmonyMethod(transpiler));

                if (ReplacementCount != 1)
                {
                    RoleLogger.Error("[Shared]",
                        $"[ItemGate/Item] !!! DIAGNOSTIC BUILD INVALID: replacement count={ReplacementCount} 期望=1");
                    return false;
                }

                bool ownerOk = VerifyPatchOwner(original);
                if (!ownerOk)
                {
                    RoleLogger.Error("[Shared]",
                        $"[ItemGate/Item] !!! DIAGNOSTIC BUILD INVALID: Transpiler owner 自检失败 summary={TranspilerOwnerSummary}");
                    return false;
                }

                RoleLogger.Info("[Shared]",
                    $"[ItemGate/Item] OK Transpiler 已登记 (replacement=1/1, owner={TranspilerOwnerVerified})");
                return true;
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[ItemGate/Item] !!! RegisterTranspiler 异常: {ex}");
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
                            && patchMethod.DeclaringType == typeof(ItemManagerUpdateDedicatedGatePatch)
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
                MethodInfo method = AccessTools.Method(typeof(ItemManager), TargetMethodName, System.Type.EmptyTypes);
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
                SignatureSummary = "private instance void Update()";
                RoleLogger.Info("[Shared]", $"[ItemGate/Item] OK 签名自检通过: {SignatureSummary}");
                return true;
            }
            catch (System.Exception ex)
            {
                SignatureResolved = false;
                SignatureSummary = $"异常: {ex.Message}";
                RoleLogger.Error("[Shared]", $"[ItemGate/Item] !!! 签名自检异常: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 替换 vanilla Update 尾部早退守卫中的 Dedicator.get_IsDedicatedServer() 调用
        /// 为 ListenRegionSyncEligibility.IsDedicatedOrP2PHost()。
        ///
        /// 栈平衡：原版 call get_IsDedicatedServer()（0 参，bool i4 返回，净 +1）；
        ///   替换 call IsDedicatedOrP2PHost()（0 参，bool i4 返回，净 +1），一致；
        ///   紧随其后的短路分支指令（!A || !B 的 brtrue 消费者）原样保留。
        ///
        /// ReplacementCount 必须精确等于 1（该调用点是 Update 方法体内唯一一处
        /// IsDedicatedServer 访问；IG1 契约测试锁死这一前提）。
        /// </summary>
        [HarmonyTranspiler]
        [HarmonyPatch(typeof(ItemManager), TargetMethodName)]
        public static IEnumerable<CodeInstruction> Update_Transpiler(
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
                    "ItemManagerUpdateDedicatedGatePatch: Dedicator.get_IsDedicatedServer not found");
            }

            if (eligibilityMethod == null)
            {
                ReplacementCount = -1;
                throw new System.InvalidOperationException(
                    "ItemManagerUpdateDedicatedGatePatch: IsDedicatedOrP2PHost not found");
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
                    $"ItemManagerUpdateDedicatedGatePatch: replacement count={replacementCount} expected=1");
            }

            return codes;
        }

        // ============= 物品门诊断探针（观测式，不生成/不销毁任何实体） =============

        /// <summary>
        /// Prefix：按区域 (x,y) 累计 generateItems 调用次数。该方法的两个 vanilla
        /// 调用来源——onLevelLoaded 一次性预生成（dedicated）与 onRegionUpdated 本地
        /// 玩家进区（客户端，含听主机本地玩家）——均被按区域计数；生成算法由
        /// AuthoritativeItemGenerationGatePatch 的账本约束，本探针只观测不干预。
        /// Verbose 关闭时立即返回（每调用成本 ≈1 次 bool 读）。
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ItemManager), "generateItems", new System.Type[] { typeof(byte), typeof(byte) })]
        internal static void GenerateItems_Probe_Prefix(byte x, byte y)
        {
            try
            {
                if (!PluginLogPolicy.IsVerboseDiagnosticsEnabled) return;

                AccumulateGenerate(x, y);
                TryEmitGenerateLog(x, y);
            }
            catch
            {
            }
        }

        /// <summary>despawnItems 探针状态：游标区域与该区域物品数快照。</summary>
        internal sealed class DespawnProbeState
        {
            public byte X;
            public byte Y;
            public bool ArenaOrNoLevel;
            public int CountBefore;
        }

        /// <summary>
        /// Prefix：快照 vanilla 静态轮转游标（despawnItems_X/Y，反射读取）与该区域
        /// items.Count，及 vanilla 顶部早退条件（Level.info null / ARENA）观测值。
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ItemManager), "despawnItems")]
        internal static void DespawnItems_Probe_Prefix(ref DespawnProbeState __state)
        {
            __state = null;
            try
            {
                if (!PluginLogPolicy.IsVerboseDiagnosticsEnabled) return;

                bool arenaOrNoLevel = Level.info == null || Level.info.type == ELevelType.ARENA;

                byte x, y;
                if (!ReadCursors(ref _despawnCursorXField, ref _despawnCursorYField, "despawnItems_X", "despawnItems_Y", out x, out y))
                {
                    return;
                }

                int countBefore = -1;
                ItemRegion[,] regions = ItemManager.regions;
                if (regions != null && x < regions.GetLength(0) && y < regions.GetLength(1)
                    && regions[x, y] != null && regions[x, y].items != null)
                {
                    countBefore = regions[x, y].items.Count;
                }

                __state = new DespawnProbeState
                {
                    X = x,
                    Y = y,
                    ArenaOrNoLevel = arenaOrNoLevel,
                    CountBefore = countBefore
                };
            }
            catch
            {
                __state = null;
            }
        }

        /// <summary>
        /// Postfix：由 (arenaOrNoLevel, countBefore, countAfter, result) 纯函数判定本次
        /// 调用形态并按区域累计。result 为 vanilla despawnItems 返回值（区域非空）。
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ItemManager), "despawnItems")]
        internal static void DespawnItems_Probe_Postfix(DespawnProbeState __state, bool __result)
        {
            try
            {
                if (__state == null) return;

                int countAfter = ReadRegionItemCount(__state.X, __state.Y);
                DespawnGateObservation observation = ObserveDespawn(
                    __state.ArenaOrNoLevel, __state.CountBefore, countAfter, __result);

                AccumulateDespawn(__state.X, __state.Y, observation);
                TryEmitDespawnLog(__state.X, __state.Y);
            }
            catch
            {
            }
        }

        /// <summary>respawnItems 探针状态：游标区域、物品数、生成点存在性与窗口观测。</summary>
        internal sealed class RespawnProbeState
        {
            public byte X;
            public byte Y;
            public bool SpawnsPresent;
            public bool WindowPassed;
            public int CountBefore;
        }

        /// <summary>
        /// Prefix：快照 vanilla 静态轮转游标（respawnItems_X/Y）、区域物品数、生成点
        /// 存在性（LevelItems.spawns[X,Y].Count &gt; 0）与窗口观测值
        /// （now - lastRespawn &gt; Respawn_Time，与 vanilla 同一比较式的只读观测）。
        /// 任一 vanilla 状态未就绪（regions/spawns 为 null）时仅记录可得字段。
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ItemManager), "respawnItems")]
        internal static void RespawnItems_Probe_Prefix(ref RespawnProbeState __state)
        {
            __state = null;
            try
            {
                if (!PluginLogPolicy.IsVerboseDiagnosticsEnabled) return;

                byte x, y;
                if (!ReadCursors(ref _respawnCursorXField, ref _respawnCursorYField, "respawnItems_X", "respawnItems_Y", out x, out y))
                {
                    return;
                }

                bool spawnsPresent = false;
                bool windowPassed = false;
                int countBefore = -1;
                ItemRegion[,] regions = ItemManager.regions;
                bool regionsReady = regions != null
                    && x < regions.GetLength(0) && y < regions.GetLength(1)
                    && regions[x, y] != null;

                if (Level.info != null && LevelItems.spawns != null
                    && x < LevelItems.spawns.GetLength(0) && y < LevelItems.spawns.GetLength(1)
                    && LevelItems.spawns[x, y] != null)
                {
                    spawnsPresent = LevelItems.spawns[x, y].Count > 0;
                }

                if (regionsReady && regions[x, y].items != null)
                {
                    countBefore = regions[x, y].items.Count;
                    windowPassed = UnityEngine.Time.realtimeSinceStartup - regions[x, y].lastRespawn
                        > Provider.modeConfigData.Items.Respawn_Time;
                }

                __state = new RespawnProbeState
                {
                    X = x,
                    Y = y,
                    SpawnsPresent = spawnsPresent,
                    WindowPassed = windowPassed,
                    CountBefore = countBefore
                };
            }
            catch
            {
                __state = null;
            }
        }

        /// <summary>
        /// Postfix：由 (spawnsPresent, windowPassed, countBefore, countAfter, result) 纯函数
        /// 判定本次调用形态并按区域累计。result 为 vanilla respawnItems 返回值。
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ItemManager), "respawnItems")]
        internal static void RespawnItems_Probe_Postfix(RespawnProbeState __state, bool __result)
        {
            try
            {
                if (__state == null) return;

                int countAfter = ReadRegionItemCount(__state.X, __state.Y);
                RespawnGateObservation observation = ObserveRespawn(
                    __state.SpawnsPresent, __state.WindowPassed, __state.CountBefore, countAfter, __result);

                AccumulateRespawn(__state.X, __state.Y, observation);
                TryEmitRespawnLog(__state.X, __state.Y);
            }
            catch
            {
            }
        }

        /// <summary>despawn 门观测结论（封闭枚举，不可能形态必须显式具名，禁止参与算术）。</summary>
        internal enum DespawnGateObservation
        {
            NotObserved,
            ArenaOrNoLevel,
            RemovedExpired,
            OccupiedNoExpiry,
            EmptyScanned,
            AnomalyGrew
        }

        /// <summary>respawn 门观测结论（封闭枚举；Cooldown 形态是 Runtime 区分「窗口未到」与「早退仍在」的判别依据）。</summary>
        internal enum RespawnGateObservation
        {
            NotObserved,
            NoSpawnpoints,
            Cooldown,
            WindowPassedIdle,
            Respawned,
            RespawnedNoAsset,
            AnomalyShrunk
        }

        /// <summary>
        /// 纯函数：由 (arenaOrNoLevel, countBefore, countAfter, result) 判定本次
        /// despawnItems 调用形态。vanilla 语义：顶部 ARENA/无图早退不触区域状态；
        /// 主体只做 RemoveAt（唯一确定性副作用 count 减小并广播 SendDestroyItem）；
        /// 非空无过期本帧停留（result=true 且 count 不变）；空区域继续轮转
        /// （result=false 且 count 不变）；count 增大在 vanilla 下不可能，显式具名。
        /// </summary>
        internal static DespawnGateObservation ObserveDespawn(
            bool arenaOrNoLevel, int countBefore, int countAfter, bool result)
        {
            if (arenaOrNoLevel) return DespawnGateObservation.ArenaOrNoLevel;

            if (countAfter < countBefore) return DespawnGateObservation.RemovedExpired;
            if (countAfter > countBefore) return DespawnGateObservation.AnomalyGrew;

            return result
                ? DespawnGateObservation.OccupiedNoExpiry
                : DespawnGateObservation.EmptyScanned;
        }

        /// <summary>
        /// 纯函数：由 (spawnsPresent, windowPassed, countBefore, countAfter, result) 判定
        /// 本次 respawnItems 调用形态。vanilla 语义：无生成点第一子句短路；窗口未过返回
        /// false（Cooldown，早退已打开时 calls&gt;0 可与「早退仍在」区分）；过窗未生成返回
        /// false（已满/全部被 safezone+间距拒绝）；实际生成 count 增大并广播 SendItem 且
        /// lastRespawn 更新；result=true 而 count 不变为 vanilla 资产缺失 error 路径
        /// （flag=true 但未 Add）；count 减小在 vanilla 下不可能，显式具名。
        /// </summary>
        internal static RespawnGateObservation ObserveRespawn(
            bool spawnsPresent, bool windowPassed, int countBefore, int countAfter, bool result)
        {
            if (!spawnsPresent) return RespawnGateObservation.NoSpawnpoints;

            if (countAfter > countBefore) return RespawnGateObservation.Respawned;
            if (countAfter < countBefore) return RespawnGateObservation.AnomalyShrunk;

            if (result) return RespawnGateObservation.RespawnedNoAsset;

            return windowPassed
                ? RespawnGateObservation.WindowPassedIdle
                : RespawnGateObservation.Cooldown;
        }

        // ============= 计数/辅助（观测式，均不构成第二套生成器） =============

        private sealed class RegionCounters
        {
            public long Calls;
            public long Removed;
            public long Occupied;
            public long Empty;
            public long Arena;
            public long Anomaly;
            public long Spawned;
            public long NoAsset;
            public long Cooldown;
            public long Idle;
            public long NoSpawn;
        }

        private static readonly Dictionary<int, RegionCounters> _generateCounters =
            new Dictionary<int, RegionCounters>();
        private static readonly Dictionary<int, RegionCounters> _despawnCounters =
            new Dictionary<int, RegionCounters>();
        private static readonly Dictionary<int, RegionCounters> _respawnCounters =
            new Dictionary<int, RegionCounters>();
        private static readonly object _probeLock = new object();

        /// <summary>区域 (x,y) 编码为字典键（x 高 8 位、y 低 8 位）。</summary>
        private static int RegionKey(byte x, byte y) => (x << 8) | y;

        private static RegionCounters GetCounters(Dictionary<int, RegionCounters> table, byte x, byte y)
        {
            int key = RegionKey(x, y);
            if (!table.TryGetValue(key, out RegionCounters counters))
            {
                counters = new RegionCounters();
                table[key] = counters;
            }
            return counters;
        }

        private static void AccumulateGenerate(byte x, byte y)
        {
            try
            {
                lock (_probeLock)
                {
                    GetCounters(_generateCounters, x, y).Calls++;
                }
            }
            catch
            {
            }
        }

        private static void AccumulateDespawn(byte x, byte y, DespawnGateObservation observation)
        {
            try
            {
                lock (_probeLock)
                {
                    RegionCounters c = GetCounters(_despawnCounters, x, y);
                    c.Calls++;
                    switch (observation)
                    {
                        case DespawnGateObservation.RemovedExpired: c.Removed++; break;
                        case DespawnGateObservation.OccupiedNoExpiry: c.Occupied++; break;
                        case DespawnGateObservation.EmptyScanned: c.Empty++; break;
                        case DespawnGateObservation.ArenaOrNoLevel: c.Arena++; break;
                        case DespawnGateObservation.AnomalyGrew: c.Anomaly++; break;
                    }
                }
            }
            catch
            {
            }
        }

        private static void AccumulateRespawn(byte x, byte y, RespawnGateObservation observation)
        {
            try
            {
                lock (_probeLock)
                {
                    RegionCounters c = GetCounters(_respawnCounters, x, y);
                    c.Calls++;
                    switch (observation)
                    {
                        case RespawnGateObservation.Respawned: c.Spawned++; break;
                        case RespawnGateObservation.RespawnedNoAsset: c.NoAsset++; break;
                        case RespawnGateObservation.Cooldown: c.Cooldown++; break;
                        case RespawnGateObservation.WindowPassedIdle: c.Idle++; break;
                        case RespawnGateObservation.NoSpawnpoints: c.NoSpawn++; break;
                        case RespawnGateObservation.AnomalyShrunk: c.Anomaly++; break;
                    }
                }
            }
            catch
            {
            }
        }

        private static void ResetProbeCounters()
        {
            lock (_probeLock)
            {
                _generateCounters.Clear();
                _despawnCounters.Clear();
                _respawnCounters.Clear();
            }
        }

        /// <summary>读 vanilla 私有静态轮转游标；despawn 用 despawnItems_X/Y、respawn 用 respawnItems_X/Y，不可解析返回 false。</summary>
        private static bool ReadCursors(ref FieldInfo fieldX, ref FieldInfo fieldY, string nameX, string nameY, out byte x, out byte y)
        {
            x = 0;
            y = 0;
            try
            {
                if (fieldX == null) fieldX = AccessTools.Field(typeof(ItemManager), nameX);
                if (fieldY == null) fieldY = AccessTools.Field(typeof(ItemManager), nameY);
                if (fieldX == null || fieldY == null) return false;

                object vx = fieldX.GetValue(null);
                object vy = fieldY.GetValue(null);
                if (!(vx is byte bx) || !(vy is byte by)) return false;

                x = bx;
                y = by;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>读指定区域当前物品数；状态未就绪返回 -1（与合法 count 0 区分）。</summary>
        private static int ReadRegionItemCount(byte x, byte y)
        {
            try
            {
                ItemRegion[,] regions = ItemManager.regions;
                if (regions == null || x >= regions.GetLength(0) || y >= regions.GetLength(1)) return -1;
                ItemRegion region = regions[x, y];
                if (region == null || region.items == null) return -1;
                return region.items.Count;
            }
            catch
            {
                return -1;
            }
        }

        private static RegionCounters SnapshotCounters(Dictionary<int, RegionCounters> table, byte x, byte y)
        {
            lock (_probeLock)
            {
                if (table.TryGetValue(RegionKey(x, y), out RegionCounters c))
                {
                    return new RegionCounters
                    {
                        Calls = c.Calls, Removed = c.Removed, Occupied = c.Occupied,
                        Empty = c.Empty, Arena = c.Arena, Anomaly = c.Anomaly,
                        Spawned = c.Spawned, NoAsset = c.NoAsset, Cooldown = c.Cooldown,
                        Idle = c.Idle, NoSpawn = c.NoSpawn
                    };
                }
            }
            return new RegionCounters();
        }

        /// <summary>5s 时间窗 + 配额点限频的 generate 区域日志；三路各自独立时钟，互不吞窗。</summary>
        private static void TryEmitGenerateLog(byte x, byte y)
        {
            try
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (now - _lastGenerateLogTime < DiagLogInterval) return;
                _lastGenerateLogTime = now;

                if (!Core.Patches.WorldSyncDiagnosticCore.TryAcquireQuota(DiagQuotaGenerateId, out int count)) return;

                RegionCounters c = SnapshotCounters(_generateCounters, x, y);
                RoleLogger.Info("[Host]",
                    $"{DiagLogPrefix} generateItems #{count}/{Core.Patches.WorldSyncDiagnosticCore.PerPointLimit} " +
                    $"elig={ListenRegionSyncEligibility.IsDedicatedOrP2PHost()} region=({x},{y}) " +
                    $"calls={c.Calls} " +
                    $"(calls=该区域 generateItems 累计；生成算法由 Authoritative 账本约束，本探针只观测)");
            }
            catch
            {
            }
        }

        /// <summary>5s 时间窗 + 配额点限频的 despawn 区域日志；计数不受限频影响。</summary>
        private static void TryEmitDespawnLog(byte x, byte y)
        {
            try
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (now - _lastDespawnLogTime < DiagLogInterval) return;
                _lastDespawnLogTime = now;

                if (!Core.Patches.WorldSyncDiagnosticCore.TryAcquireQuota(DiagQuotaDespawnId, out int count)) return;

                RegionCounters c = SnapshotCounters(_despawnCounters, x, y);
                RoleLogger.Info("[Host]",
                    $"{DiagLogPrefix} despawnItems #{count}/{Core.Patches.WorldSyncDiagnosticCore.PerPointLimit} " +
                    $"elig={ListenRegionSyncEligibility.IsDedicatedOrP2PHost()} region=({x},{y}) " +
                    $"calls={c.Calls} removed={c.Removed} occupied={c.Occupied} empty={c.Empty} " +
                    $"arena={c.Arena} anomaly={c.Anomaly} " +
                    $"(removed=过期物品已移除; occupied=非空无过期; empty=空区域轮转)");
            }
            catch
            {
            }
        }

        /// <summary>5s 时间窗 + 配额点限频的 respawn 区域日志；计数不受限频影响。</summary>
        private static void TryEmitRespawnLog(byte x, byte y)
        {
            try
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (now - _lastRespawnLogTime < DiagLogInterval) return;
                _lastRespawnLogTime = now;

                if (!Core.Patches.WorldSyncDiagnosticCore.TryAcquireQuota(DiagQuotaRespawnId, out int count)) return;

                RegionCounters c = SnapshotCounters(_respawnCounters, x, y);
                RoleLogger.Info("[Host]",
                    $"{DiagLogPrefix} respawnItems #{count}/{Core.Patches.WorldSyncDiagnosticCore.PerPointLimit} " +
                    $"elig={ListenRegionSyncEligibility.IsDedicatedOrP2PHost()} region=({x},{y}) " +
                    $"calls={c.Calls} spawned={c.Spawned} cooldown={c.Cooldown} idle={c.Idle} " +
                    $"noasset={c.NoAsset} nospawn={c.NoSpawn} anomaly={c.Anomaly} " +
                    $"(spawned=已再生; cooldown=窗口未到; calls==0 且 elig=true ⇒ 早退仍在)");
            }
            catch
            {
            }
        }
    }
}
