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
    /// 玩家建筑结构（Structure）放置与损坏跨端同步补丁 (StructureStateReplicationPatch)
    /// </summary>
    public static class StructureStateReplicationPatch
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
                RoleLogger.Error("[Shared]", "[StructureReplication] !!! " + RegistrationSummary);
                return false;
            }

            try
            {
                MethodInfo dropMethod = AccessTools.Method(typeof(StructureManager), "dropStructure",
                    new Type[] { typeof(SDG.Unturned.Structure), typeof(Vector3), typeof(float), typeof(float), typeof(float), typeof(ulong), typeof(ulong) });
                MethodInfo damageMethod = AccessTools.Method(typeof(StructureManager), "damage",
                    new Type[] { typeof(Transform), typeof(Vector3), typeof(float), typeof(float), typeof(bool), typeof(Steamworks.CSteamID), typeof(EDamageOrigin) });

                MethodInfo dropPostfix = AccessTools.Method(typeof(StructureStateReplicationPatch), nameof(DropStructure_Postfix));
                MethodInfo damagePostfix = AccessTools.Method(typeof(StructureStateReplicationPatch), nameof(Damage_Postfix));

                if (dropMethod == null || dropPostfix == null || damageMethod == null || damagePostfix == null)
                {
                    RegistrationSummary = "目标或补丁方法解析失败";
                    RoleLogger.Error("[Shared]", "[StructureReplication] !!! " + RegistrationSummary);
                    return false;
                }

                harmony.Patch(dropMethod, postfix: new HarmonyMethod(dropPostfix));
                harmony.Patch(damageMethod, postfix: new HarmonyMethod(damagePostfix));

                RegistrationSucceeded = true;
                RegistrationSummary = "StructureManager.dropStructure+damage.Postfix OK";
                RoleLogger.Info("[Shared]", "[StructureReplication] OK " + RegistrationSummary);
                return true;
            }
            catch (Exception ex)
            {
                RegistrationSummary = "登记异常: " + ex.GetType().Name + ": " + ex.Message;
                RoleLogger.Error("[Shared]", "[StructureReplication] !!! " + RegistrationSummary);
                return false;
            }
        }

        public static void DropStructure_Postfix(bool __result, SDG.Unturned.Structure structure, Vector3 point)
        {
            if (!HostManager.IsP2PHostMode || !HostManager.ShouldProcessClientHostListen())
            {
                return;
            }

            if (!__result)
            {
                return;
            }

            try
            {
                if (Regions.tryGetCoordinate(point, out byte x, out byte y))
                {
                    int key = StructureRegionLifecycleLedger.EncodeKey(x, y);
                    uint nextGen = StructureRegionLifecycleAdapter.RecordStructurePlaced(x, y, 0);
                    StructureSnapshotAdapter.UpdateRegionGeneration(key, nextGen);
                    StructureSnapshotAdapter.AdvanceDeltaSequence(key);

                    if (_dropLogCount < MaxLogCount)
                    {
                        _dropLogCount++;
                        RoleLogger.Info("[Host]", $"[StructureReplication] Structure placed #{_dropLogCount}: region=({x},{y}) nextGen={nextGen}");
                    }
                }
            }
            catch (Exception ex)
            {
                RoleLogger.Warn("[Host]", $"[StructureReplication] DropStructure_Postfix 异常: {ex.GetType().Name}");
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
                    int key = StructureRegionLifecycleLedger.EncodeKey(x, y);
                    uint nextGen = StructureRegionLifecycleAdapter.RecordStructureDamaged(x, y, 0);
                    StructureSnapshotAdapter.UpdateRegionGeneration(key, nextGen);
                    StructureSnapshotAdapter.AdvanceDeltaSequence(key);

                    if (_damageLogCount < MaxLogCount)
                    {
                        _damageLogCount++;
                        RoleLogger.Info("[Host]", $"[StructureReplication] Structure damaged #{_damageLogCount}: region=({x},{y}) nextGen={nextGen}");
                    }
                }
            }
            catch (Exception ex)
            {
                RoleLogger.Warn("[Host]", $"[StructureReplication] Damage_Postfix 异常: {ex.GetType().Name}");
            }
        }

        public static void ResetLogCounters()
        {
            _dropLogCount = 0;
            _damageLogCount = 0;
        }
    }
}
