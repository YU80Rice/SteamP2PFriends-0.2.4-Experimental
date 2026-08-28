using HarmonyLib;
using SDG.Unturned;
using SteamP2PFriends.Adapters.Resource;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.Host;
using SteamP2PFriends.Shared;
using System;
using System.Reflection;
using UnityEngine;

namespace SteamP2PFriends.Adapters.Resource.Patches
{
    /// <summary>
    /// 自然资源（树木/矿石）采伐破坏与倒塌全端同步补丁 (ResourceManagerHarvestReplicationPatch)
    /// 拦截 ServerSetResourceDead 与 ServerSetResourceAlive，
    /// 驱动资源领域生命周期代次递增、倒下物理动画与掉落物联动同步。
    /// </summary>
    public static class ResourceManagerHarvestReplicationPatch
    {
        private const string HarmonyId = SteamP2PFriendsPlugin.HARMONY_ID;
        public static bool RegistrationSucceeded { get; private set; }
        public static string RegistrationSummary { get; private set; } = "未登记";

        private const int MaxLogCount = 10;
        private static int _deadLogCount;
        private static int _aliveLogCount;
        public static bool ReceiveResourceDeadPostfixRegistered { get; private set; }
        public static bool ReceiveResourceAlivePostfixRegistered { get; private set; }

        public static bool RegisterManual(Harmony harmony)
        {
            if (harmony == null)
            {
                RegistrationSummary = "harmony=null";
                RoleLogger.Error("[Shared]", "[ResourceHarvest] !!! " + RegistrationSummary);
                return false;
            }

            try
            {
                MethodInfo deadMethod = AccessTools.Method(typeof(ResourceManager), "ServerSetResourceDead",
                    new Type[] { typeof(byte), typeof(byte), typeof(ushort), typeof(Vector3) });
                MethodInfo aliveMethod = AccessTools.Method(typeof(ResourceManager), "ServerSetResourceAlive",
                    new Type[] { typeof(byte), typeof(byte), typeof(ushort) });
                MethodInfo receiveDeadMethod = AccessTools.Method(typeof(ResourceManager), "ReceiveResourceDead",
                    new Type[] { typeof(byte), typeof(byte), typeof(ushort), typeof(Vector3) });
                MethodInfo receiveAliveMethod = AccessTools.Method(typeof(ResourceManager), "ReceiveResourceAlive",
                    new Type[] { typeof(byte), typeof(byte), typeof(ushort) });

                MethodInfo deadPostfix = AccessTools.Method(typeof(ResourceManagerHarvestReplicationPatch), nameof(ServerSetResourceDead_Postfix));
                MethodInfo alivePostfix = AccessTools.Method(typeof(ResourceManagerHarvestReplicationPatch), nameof(ServerSetResourceAlive_Postfix));
                MethodInfo receiveDeadPostfix = AccessTools.Method(typeof(ResourceManagerHarvestReplicationPatch), nameof(ReceiveResourceDead_Postfix));
                MethodInfo receiveAlivePostfix = AccessTools.Method(typeof(ResourceManagerHarvestReplicationPatch), nameof(ReceiveResourceAlive_Postfix));

                if (deadMethod == null || deadPostfix == null || aliveMethod == null || alivePostfix == null
                    || receiveDeadMethod == null || receiveDeadPostfix == null
                    || receiveAliveMethod == null || receiveAlivePostfix == null)
                {
                    RegistrationSummary = "目标或补丁方法解析失败";
                    RoleLogger.Error("[Shared]", "[ResourceHarvest] !!! " + RegistrationSummary);
                    return false;
                }

                harmony.Patch(deadMethod, postfix: new HarmonyMethod(deadPostfix));
                harmony.Patch(aliveMethod, postfix: new HarmonyMethod(alivePostfix));
                harmony.Patch(receiveDeadMethod, postfix: new HarmonyMethod(receiveDeadPostfix));
                harmony.Patch(receiveAliveMethod, postfix: new HarmonyMethod(receiveAlivePostfix));
                ReceiveResourceDeadPostfixRegistered = true;
                ReceiveResourceAlivePostfixRegistered = true;

                RegistrationSucceeded = true;
                RegistrationSummary = "ServerSetResourceDead+ServerSetResourceAlive+ReceiveResourceDead+ReceiveResourceAlive.Postfix OK";
                RoleLogger.Info("[Shared]", "[ResourceHarvest] OK " + RegistrationSummary);
                return true;
            }
            catch (Exception ex)
            {
                RegistrationSummary = "登记异常: " + ex.GetType().Name + ": " + ex.Message;
                RoleLogger.Error("[Shared]", "[ResourceHarvest] !!! " + RegistrationSummary);
                return false;
            }
        }

        public static void ServerSetResourceDead_Postfix(byte x, byte y, ushort index, Vector3 baseForce)
        {
            if (!HostManager.IsP2PHostMode || !HostManager.ShouldProcessClientHostListen())
            {
                return;
            }

            try
            {
                uint nextGen = ResourceRegionLifecycleAdapter.RecordResourceDead(x, y, index);
                RegionKey regionKey = new RegionKey(x, y);
                ResourceSnapshotAdapter.UpdateRegionGeneration(regionKey, nextGen);
                int acceptedObservers = ResourceSnapshotAdapter.RecordNativeDelta(regionKey, nextGen);

                if (_deadLogCount < MaxLogCount)
                {
                    _deadLogCount++;
                    RoleLogger.Info("[Host]", $"[ResourceHarvest] Tree/Ore dead #{_deadLogCount}: region=({x},{y}) index={index} nextGen={nextGen} spiDeltaObservers={acceptedObservers} leaseAuthority=ResourceProductionControlSeam stateEncoder=ResourceManager.ServerSetResourceDead");
                }
            }
            catch (Exception ex)
            {
                RoleLogger.Warn("[Host]", $"[ResourceHarvest] ServerSetResourceDead_Postfix 异常: {ex.GetType().Name}");
            }
        }

        public static void ServerSetResourceAlive_Postfix(byte x, byte y, ushort index)
        {
            if (!HostManager.IsP2PHostMode || !HostManager.ShouldProcessClientHostListen())
            {
                return;
            }

            try
            {
                uint nextGen = ResourceRegionLifecycleAdapter.RecordResourceAlive(x, y, index);
                RegionKey regionKey = new RegionKey(x, y);
                ResourceSnapshotAdapter.UpdateRegionGeneration(regionKey, nextGen);
                int acceptedObservers = ResourceSnapshotAdapter.RecordNativeDelta(regionKey, nextGen);

                if (_aliveLogCount < MaxLogCount)
                {
                    _aliveLogCount++;
                    RoleLogger.Info("[Host]", $"[ResourceHarvest] Tree/Ore alive #{_aliveLogCount}: region=({x},{y}) index={index} nextGen={nextGen} spiDeltaObservers={acceptedObservers} leaseAuthority=ResourceProductionControlSeam stateEncoder=ResourceManager.ServerSetResourceAlive");
                }
            }
            catch (Exception ex)
            {
                RoleLogger.Warn("[Host]", $"[ResourceHarvest] ServerSetResourceAlive_Postfix 异常: {ex.GetType().Name}");
            }
        }

        public static void ReceiveResourceDead_Postfix(byte x, byte y, ushort index, Vector3 ragdoll)
        {
            RecordClientDelta(x, y, index, "ReceiveResourceDead");
        }

        public static void ReceiveResourceAlive_Postfix(byte x, byte y, ushort index)
        {
            RecordClientDelta(x, y, index, "ReceiveResourceAlive");
        }

        private static void RecordClientDelta(byte x, byte y, ushort index, string decoder)
        {
            if (HostManager.IsP2PHostMode && HostManager.ShouldProcessClientHostListen())
            {
                return;
            }

            try
            {
                RegionKey regionKey = new RegionKey(x, y);
                int receiveCount = ResourceSnapshotAdapter.RecordNativeDeltaReceive();
                RoleLogger.Info("[Client]",
                    $"[ResourceSPI] event=DeltaReceive region=({x},{y}) index={index} " +
                    $"deltaReceiveCount={receiveCount} " +
                    $"leaseAuthority=ResourceProductionControlSeam stateDecoder=ResourceManager.{decoder}");
            }
            catch (Exception ex)
            {
                RoleLogger.Warn("[Client]", $"[ResourceHarvest] {decoder} Postfix 异常: {ex.GetType().Name}");
            }
        }

        public static void ResetLogCounters()
        {
            _deadLogCount = 0;
            _aliveLogCount = 0;
        }
    }
}
