using SDG.Unturned;
using SteamP2PFriends.Adapters.Zombie.Patches;
using SteamP2PFriends.Host;
using SteamP2PFriends.Shared;
using System;
using System.Reflection;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// Ticket 01（listen-host-dedicated-gate）PureMemory 契约：
    ///   ZG-P1 资格真值表：专用服真、听主机真、普通单机假、客机假。
    ///   ZG-P2 听主机在无信标、非 Horde 的普通 PEI 场景下资格位为真：
    ///       vanilla 早退子句 `(!Dedicator.IsDedicatedServer && !hasBeacon && type!=HORDE)`
    ///       中被替换的唯一资格项此时为 true → 子句短路为 false → 不得再走单机直接 return。
    ///       （接线本身由 StaticIL ZG1/ZG2 锁死，两个证据域共同构成完整契约。）
    ///   ZG-P3 听主机前置状态缺一律 fail-closed（未连入/未进图/非 server/模式未启动）。
    ///   ZG-P4 重生门观测纯函数（respawnZombieIndex 轮转判定）。
    /// 纪律：只触碰 vanilla/HostManager 的静态 backing field，finally 中逐字段精确还原，
    /// 不写入其余静态状态，不留污染。
    /// </summary>
    internal static class ZombieRespawnDedicatedGateEligibilityTests
    {
        private static readonly Type P2PModeType = typeof(HostManager);

        private static FieldInfo StaticField(Type type, string name)
        {
            return type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        }

        /// <summary>
        /// 按给定世界形态设置资格输入，返回 IsDedicatedOrP2PHost() 的判定。
        /// hostMode 用字符串（"None"/"P2P"/"LAN"）反射解析，避免枚举编译期耦合。
        /// 任一必需字段不可解析时抛异常（由测试入口记为 FAIL，不静默跳过）。
        /// </summary>
        private static bool RunEligibilityScenario(
            bool dedicated, bool p2pActive, string hostMode,
            bool connected, bool isServer, bool levelLoaded)
        {
            FieldInfo fDedicated = StaticField(typeof(Dedicator), "_isDedicated");
            FieldInfo fProviderServer = StaticField(typeof(Provider), "_isServer");
            FieldInfo fProviderConnected = StaticField(typeof(Provider), "_isConnected");
            FieldInfo fLevelLoaded = StaticField(typeof(Level), "_isLoaded");
            FieldInfo fHostMode = StaticField(P2PModeType, "_hostMode");
            FieldInfo fP2PActive = StaticField(P2PModeType, "<IsP2PServerActive>k__BackingField");

            if (fDedicated == null || fProviderServer == null || fProviderConnected == null ||
                fLevelLoaded == null || fHostMode == null || fP2PActive == null)
            {
                throw new InvalidOperationException("资格场景 backing field 缺失: " +
                    $"dedicated={fDedicated != null}, providerServer={fProviderServer != null}, " +
                    $"providerConnected={fProviderConnected != null}, levelLoaded={fLevelLoaded != null}, " +
                    $"hostMode={fHostMode != null}, p2pActive={fP2PActive != null}");
            }

            object savedDedicated = fDedicated.GetValue(null);
            object savedProviderServer = fProviderServer.GetValue(null);
            object savedProviderConnected = fProviderConnected.GetValue(null);
            object savedLevelLoaded = fLevelLoaded.GetValue(null);
            object savedHostMode = fHostMode.GetValue(null);
            object savedP2PActive = fP2PActive.GetValue(null);

            try
            {
                object parsedHostMode = Enum.Parse(fHostMode.FieldType, hostMode);
                fDedicated.SetValue(null, dedicated);
                fProviderServer.SetValue(null, isServer);
                fProviderConnected.SetValue(null, connected);
                fLevelLoaded.SetValue(null, levelLoaded);
                fHostMode.SetValue(null, parsedHostMode);
                fP2PActive.SetValue(null, p2pActive);

                return ListenRegionSyncEligibility.IsDedicatedOrP2PHost();
            }
            finally
            {
                fP2PActive.SetValue(null, savedP2PActive);
                fHostMode.SetValue(null, savedHostMode);
                fLevelLoaded.SetValue(null, savedLevelLoaded);
                fProviderConnected.SetValue(null, savedProviderConnected);
                fProviderServer.SetValue(null, savedProviderServer);
                fDedicated.SetValue(null, savedDedicated);
            }
        }

        internal static bool Test_ZG_P1_EligibilityTruthTable()
        {
            // 专用服：资格 true（与 listen 状态无关，短路于 dedicated 检查）
            if (!RunEligibilityScenario(true, false, "None", false, false, false)) return false;
            if (!RunEligibilityScenario(true, false, "P2P", true, true, true)) return false;

            // 听主机：true
            if (!RunEligibilityScenario(false, true, "P2P", true, true, true)) return false;

            // 普通单机（内部 isServer=true 但无 P2P listen 会话）：false
            if (RunEligibilityScenario(false, false, "None", true, true, true)) return false;

            // 客机（连入但非 server、无 listen 资格）：false
            if (RunEligibilityScenario(false, false, "None", true, false, true)) return false;

            // 菜单阶段：false
            if (RunEligibilityScenario(false, false, "None", false, false, false)) return false;

            return true;
        }

        internal static bool Test_ZG_P2_ListenHostBeaconFreeNormalPeiNotBlocked()
        {
            // 听主机、普通 PEI（无信标、非 Horde 图由 vanilla 其他项决定，与资格位无关）：
            // 资格位为 true ⇒ vanilla 子句 (!elig && !hasBeacon && type!=HORDE) 恒假，
            // 单机式直接 return 不再生效。
            if (!RunEligibilityScenario(false, true, "P2P", true, true, true)) return false;

            // 回归锁：听主机资格位对 hostMode 的具体非 None 取值不敏感
            // （ShouldProcessClientHostListen 只要求 _hostMode != None），LAN 测试形态同样为真。
            if (!RunEligibilityScenario(false, true, "LAN", true, true, true)) return false;
            return true;
        }

        internal static bool Test_ZG_P3_ListenHostPartStatesFailClosed()
        {
            // P2P 激活但 hostMode 未进入：false
            if (RunEligibilityScenario(false, true, "None", true, true, true)) return false;
            // 未进图：false
            if (RunEligibilityScenario(false, true, "P2P", true, true, false)) return false;
            // 未连入：false
            if (RunEligibilityScenario(false, true, "P2P", false, true, true)) return false;
            // 非 server（客机）：false
            if (RunEligibilityScenario(false, true, "P2P", true, false, true)) return false;
            return true;
        }

        internal static bool Test_ZG_P4_GuardPassObservationFromIndexRotation()
        {
            var t = typeof(ZombieManagerRespawnZombiesDedicatedGatePatch);
            MethodInfo observe = t.GetMethod("ObservePass",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (observe == null) return false;

            Type obsType = observe.ReturnType;
            Func<string, object> named = name => Enum.Parse(obsType, name);

            // 轮转发生 ⇒ 已过守卫（count>=2 时轮转必改变索引）
            if ((int)observe.Invoke(null, new object[] { 5, (ushort)2, (ushort)3 }) !=
                Convert.ToInt32(named("PassedAlignedGate"))) return false;
            // 回绕（4 → 0）同样可判
            if ((int)observe.Invoke(null, new object[] { 5, (ushort)4, (ushort)0 }) !=
                Convert.ToInt32(named("PassedAlignedGate"))) return false;
            // 索引原样 ⇒ 仍早退
            if ((int)observe.Invoke(null, new object[] { 5, (ushort)2, (ushort)2 }) !=
                Convert.ToInt32(named("EarlyReturned"))) return false;
            // 空区域：vanilla 守卫 zombies.Count<=0 必早退
            if ((int)observe.Invoke(null, new object[] { 0, (ushort)1, (ushort)1 }) !=
                Convert.ToInt32(named("EarlyReturned"))) return false;
            // 单僵尸区域过守卫后回绕到同一 0：不可判，必须显式具名
            if ((int)observe.Invoke(null, new object[] { 1, (ushort)0, (ushort)0 }) !=
                Convert.ToInt32(named("AmbiguousSingleZombie"))) return false;
            // 单僵尸区域但索引被 clamp（陈旧 >0 索引 → 0）：仍可判为已过守卫
            if ((int)observe.Invoke(null, new object[] { 1, (ushort)3, (ushort)0 }) !=
                Convert.ToInt32(named("PassedAlignedGate"))) return false;
            // 单僵尸区域且陈旧索引原样保留（3→3）：过守卫必把索引收敛到 0，故为早退
            if ((int)observe.Invoke(null, new object[] { 1, (ushort)3, (ushort)3 }) !=
                Convert.ToInt32(named("EarlyReturned"))) return false;
            return true;
        }
    }
}
