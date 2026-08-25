using HarmonyLib;
using SDG.Unturned;
using SteamP2PFriends.Adapters.Structure;
using SteamP2PFriends.Host;
using SteamP2PFriends.Shared;
using System;
using System.Reflection;
using UnityEngine;

namespace SteamP2PFriends.Adapters.Structure.Patches
{
    /// <summary>
    /// 玩家防御工事（Barricade）放置、损坏与交互状态跨端同步补丁 (BarricadeStateReplicationPatch)
    /// </summary>
    public static class BarricadeStateReplicationPatch
    {
        private const string HarmonyId = SteamP2PFriendsPlugin.HARMONY_ID;
        public static bool RegistrationSucceeded { get; private set; }
        public static string RegistrationSummary { get; private set; } = "未登记";

        private const int MaxLogCount = 10;
        private static int _dropLogCount;
        private static int _damageLogCount;

        public static bool RegisterManual(Harmony harmony)
        {
            if (harmony == null)
            {
                RegistrationSummary = "harmony=null";
                RoleLogger.Error("[Shared]", "[BarricadeReplication] !!! " + RegistrationSummary);
                return false;
            }

            try
            {
                MethodInfo dropMethod = AccessTools.Method(typeof(BarricadeManager), "dropBarricade",
                    new Type[] { typeof(Barricade), typeof(Transform), typeof(Vector3), typeof(float), typeof(float), typeof(float), typeof(ulong), typeof(ulong) });
                MethodInfo damageMethod = AccessTools.Method(typeof(BarricadeManager), "damage",
                    new Type[] { typeof(Transform), typeof(float), typeof(float), typeof(bool), typeof(Steamworks.CSteamID), typeof(EDamageOrigin) });

                MethodInfo dropPostfix = AccessTools.Method(typeof(BarricadeStateReplicationPatch), nameof(DropBarricade_Postfix));
                MethodInfo damagePostfix = AccessTools.Method(typeof(BarricadeStateReplicationPatch), nameof(Damage_Postfix));

                if (dropMethod == null || dropPostfix == null || damageMethod == null || damagePostfix == null)
                {
                    RegistrationSummary = "目标或补丁方法解析失败";
                    RoleLogger.Error("[Shared]", "[BarricadeReplication] !!! " + RegistrationSummary);
                    return false;
                }

                harmony.Patch(dropMethod, postfix: new HarmonyMethod(dropPostfix));
                harmony.Patch(damageMethod, postfix: new HarmonyMethod(damagePostfix));

                RegistrationSucceeded = true;
                RegistrationSummary = "BarricadeManager.dropBarricade+damage.Postfix OK";
                RoleLogger.Info("[Shared]", "[BarricadeReplication] OK " + RegistrationSummary);
                return true;
            }
            catch (Exception ex)
            {
                RegistrationSummary = "登记异常: " + ex.GetType().Name + ": " + ex.Message;
                RoleLogger.Error("[Shared]", "[BarricadeReplication] !!! " + RegistrationSummary);
                return false;
            }
        }

        public static void DropBarricade_Postfix(Transform __result, Barricade barricade, Transform hit, Vector3 point)
        {
            if (!HostManager.IsP2PHostMode || !HostManager.ShouldProcessClientHostListen())
            {
                return;
            }

            if (__result == null)
            {
                return;
            }

            try
            {
                if (Regions.tryGetCoordinate(point, out byte x, out byte y))
                {
                    int key = BarricadeRegionLifecycleLedger.EncodeKey(x, y, 0);
                    uint nextGen = BarricadeRegionLifecycleAdapter.RecordBarricadePlaced(x, y, 0, 0);
                    BarricadeSnapshotAdapter.UpdateRegionGeneration(key, nextGen);
                    BarricadeSnapshotAdapter.AdvanceDeltaSequence(key);

                    if (_dropLogCount < MaxLogCount)
                    {
                        _dropLogCount++;
                        RoleLogger.Info("[Host]", $"[BarricadeReplication] Barricade placed #{_dropLogCount}: region=({x},{y}) nextGen={nextGen}");
                    }
                }
            }
            catch (Exception ex)
            {
                RoleLogger.Warn("[Host]", $"[BarricadeReplication] DropBarricade_Postfix 异常: {ex.GetType().Name}");
            }
        }

        public static void Damage_Postfix(Transform transform, float damage)
        {
            if (!HostManager.IsP2PHostMode || !HostManager.ShouldProcessClientHostListen())
            {
                return;
            }

            if (transform == null)
            {
                return;
            }

            try
            {
                if (Regions.tryGetCoordinate(transform.position, out byte x, out byte y))
                {
                    int key = BarricadeRegionLifecycleLedger.EncodeKey(x, y, 0);
                    uint nextGen = BarricadeRegionLifecycleAdapter.RecordBarricadeDamaged(x, y, 0, 0);
                    BarricadeSnapshotAdapter.UpdateRegionGeneration(key, nextGen);
                    BarricadeSnapshotAdapter.AdvanceDeltaSequence(key);

                    if (_damageLogCount < MaxLogCount)
                    {
                        _damageLogCount++;
                        RoleLogger.Info("[Host]", $"[BarricadeReplication] Barricade damaged #{_damageLogCount}: region=({x},{y}) nextGen={nextGen}");
                    }
                }
            }
            catch (Exception ex)
            {
                RoleLogger.Warn("[Host]", $"[BarricadeReplication] Damage_Postfix 异常: {ex.GetType().Name}");
            }
        }

        public static void ResetLogCounters()
        {
            _dropLogCount = 0;
            _damageLogCount = 0;
        }
    }
}
