using SDG.Unturned;
using SteamP2PFriends.Adapters.Item.Patches;
using SteamP2PFriends.Host;
using SteamP2PFriends.Shared;
using System;
using System.Reflection;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// Ticket 02（listen-host-dedicated-gate）PureMemory 契约：
    ///   IG-P3 资格回归锁：听主机真、普通单机假、客机假（Update 尾部早退所依赖
    ///       的资格位与 Ticket01 同一生产函数 IsDedicatedOrP2PHost；接线由
    ///       StaticIL IG1/IG2 锁死，两证据域联合闭合工单「普通单机/客机早退不变」子句）。
    ///   IG-P1 despawn 门观测纯函数（count 差分判定）。
    ///   IG-P2 respawn 门观测纯函数（含「窗口未到 Cooldown」与「早退仍在」的判别形态：
    ///       早退仍在 ⇒ respawnItems 不被调用 ⇒ calls==0；窗口未到 ⇒ calls>0 且 Cooldown）。
    /// 纪律：只触碰 vanilla/HostManager 的静态 backing field，finally 中逐字段精确还原。
    /// </summary>
    internal static class ItemUpdateDedicatedGateEligibilityTests
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

        internal static bool Test_IGP3_EligibilityListenHostVsSingleplayer()
        {
            // 听主机：true ⇒ Update 尾部 (!elig && !Level.isLoaded) 中资格子句为 false，
            // vanilla 专用服周期 despawn/respawn 轮转对听主机开放。
            if (!RunEligibilityScenario(false, true, "P2P", true, true, true)) return false;
            if (!RunEligibilityScenario(false, true, "LAN", true, true, true)) return false;

            // 专用服：true（vanilla 行为不变）
            if (!RunEligibilityScenario(true, false, "None", false, false, false)) return false;

            // 普通单机（内部 isServer=true 但无 P2P listen 会话）：false ⇒ 早退不变
            if (RunEligibilityScenario(false, false, "None", true, true, true)) return false;

            // 客机：false ⇒ 早退不变
            if (RunEligibilityScenario(false, false, "None", true, false, true)) return false;

            // 菜单阶段：false
            if (RunEligibilityScenario(false, false, "None", false, false, false)) return false;

            return true;
        }

        internal static bool Test_IGP1_DespawnObservationFromCountDelta()
        {
            MethodInfo observeMethod = typeof(ItemManagerUpdateDedicatedGatePatch).GetMethod(
                "ObserveDespawn", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (observeMethod == null) return false;

            Type obsType = observeMethod.ReturnType;
            Func<string, object> named = name => Enum.Parse(obsType, name);

            // vanilla 顶部早退（Level.info null / ARENA 图）：不触区域状态，显式具名
            if ((int)observeMethod.Invoke(null, new object[] { true, 3, 3, false }) !=
                Convert.ToInt32(named("ArenaOrNoLevel"))) return false;
            // count 减少 ⇒ 唯一确定性副作用（过期物品移除并广播 SendDestroyItem）
            if ((int)observeMethod.Invoke(null, new object[] { false, 3, 2, true }) !=
                Convert.ToInt32(named("RemovedExpired"))) return false;
            // 非空无过期 ⇒ 本帧停留于该区域（result=true 且 count 不变）
            if ((int)observeMethod.Invoke(null, new object[] { false, 3, 3, true }) !=
                Convert.ToInt32(named("OccupiedNoExpiry"))) return false;
            // 空区域 ⇒ 继续轮转
            if ((int)observeMethod.Invoke(null, new object[] { false, 0, 0, false }) !=
                Convert.ToInt32(named("EmptyScanned"))) return false;
            // despawnItems 只移除不添加，count 增大是不可能形态，必须显式具名
            if ((int)observeMethod.Invoke(null, new object[] { false, 3, 4, true }) !=
                Convert.ToInt32(named("AnomalyGrew"))) return false;
            return true;
        }

        internal static bool Test_IGP2_RespawnObservationFromCountDeltaAndWindow()
        {
            MethodInfo observeMethod = typeof(ItemManagerUpdateDedicatedGatePatch).GetMethod(
                "ObserveRespawn", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (observeMethod == null) return false;

            Type obsType = observeMethod.ReturnType;
            Func<string, object> named = name => Enum.Parse(obsType, name);

            // 区域无生成点（vanilla 第一子句短路）：显式具名，不与窗口形态混叠
            if ((int)observeMethod.Invoke(null, new object[] { false, true, 2, 2, false }) !=
                Convert.ToInt32(named("NoSpawnpoints"))) return false;
            // 窗口未到（lastRespawn 未过 Respawn_Time）⇒ Runtime 判「等待重生窗口」的关键形态：
            // calls>0（早退已打开）且 Cooldown ≠ 早退仍在（calls==0）
            if ((int)observeMethod.Invoke(null, new object[] { true, false, 2, 2, false }) !=
                Convert.ToInt32(named("Cooldown"))) return false;
            // 窗口已过但本轮无生成（已满/全部被 safezone+间距拒绝）
            if ((int)observeMethod.Invoke(null, new object[] { true, true, 2, 2, false }) !=
                Convert.ToInt32(named("WindowPassedIdle"))) return false;
            // count 增加 ⇒ 唯一确定性副作用（实际生成并广播 SendItem，lastRespawn 更新）
            if ((int)observeMethod.Invoke(null, new object[] { true, true, 2, 5, true }) !=
                Convert.ToInt32(named("Respawned"))) return false;
            // result=true 且 count 不变 ⇒ vanilla 资产缺失 error 路径（flag=true 但未 Add）
            if ((int)observeMethod.Invoke(null, new object[] { true, true, 2, 2, true }) !=
                Convert.ToInt32(named("RespawnedNoAsset"))) return false;
            // respawnItems 只添加不移除，count 减小是不可能形态，必须显式具名
            if ((int)observeMethod.Invoke(null, new object[] { true, true, 2, 1, false }) !=
                Convert.ToInt32(named("AnomalyShrunk"))) return false;
            return true;
        }
    }
}
