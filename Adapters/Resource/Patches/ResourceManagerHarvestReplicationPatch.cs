using HarmonyLib;
using SDG.Unturned;
using SteamP2PFriends.Adapters.Resource;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.Host;
using SteamP2PFriends.Shared;
using System;
using System.Collections;
using System.Collections.Generic;
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
        public static bool ServerSetResourceDeadPostfixRegistered { get; private set; }
        public static bool ServerSetResourceAlivePostfixRegistered { get; private set; }
        public static bool ReceiveResourceDeadPostfixRegistered { get; private set; }
        public static bool ReceiveResourceAlivePostfixRegistered { get; private set; }
        public static bool AllHookRegistrationsSucceeded =>
            ServerSetResourceDeadPostfixRegistered && ServerSetResourceAlivePostfixRegistered
            && ReceiveResourceDeadPostfixRegistered && ReceiveResourceAlivePostfixRegistered;

        public static bool RegisterManual(Harmony harmony)
        {
            RegistrationSucceeded = false;
            ServerSetResourceDeadPostfixRegistered = false;
            ServerSetResourceAlivePostfixRegistered = false;
            ReceiveResourceDeadPostfixRegistered = false;
            ReceiveResourceAlivePostfixRegistered = false;
            if (harmony == null)
            {
                RegistrationSummary = "harmony=null";
                ResourceObservability.Error("[Shared]", "HarvestRegistration", "-", 0UL, 0UL, 0U,
                    "Fallback", false, "failed", "reason=harmony-null");
                return false;
            }

            var applied = new List<Tuple<MethodInfo, MethodInfo>>();
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
                    ResourceObservability.Error("[Shared]", "HarvestRegistration", "-", 0UL, 0UL, 0U,
                        "Fallback", false, "failed", "reason=target-or-patch-missing");
                    return false;
                }

                if (!PatchAndVerify(harmony, deadMethod, deadPostfix, "ServerSetResourceDead", applied,
                        value => ServerSetResourceDeadPostfixRegistered = value)
                    || !PatchAndVerify(harmony, aliveMethod, alivePostfix, "ServerSetResourceAlive", applied,
                        value => ServerSetResourceAlivePostfixRegistered = value)
                    || !PatchAndVerify(harmony, receiveDeadMethod, receiveDeadPostfix, "ReceiveResourceDead", applied,
                        value => ReceiveResourceDeadPostfixRegistered = value)
                    || !PatchAndVerify(harmony, receiveAliveMethod, receiveAlivePostfix, "ReceiveResourceAlive", applied,
                        value => ReceiveResourceAlivePostfixRegistered = value))
                {
                    int appliedCount = applied.Count;
                    bool rollbackClean = RollbackRegistration(harmony, applied);
                    RegistrationSummary = "部分登记失败，已回滚 applied=" + appliedCount +
                        " rollbackClean=" + rollbackClean + " hooks=" + DescribeHookState();
                    ResourceObservability.Error("[Shared]", "HarvestRegistration", "-", 0UL, 0UL, 0U,
                        "Fallback", false, "failed", "rollback=true rollbackClean=" + rollbackClean + " applied=" + appliedCount +
                        " hooks=" + DescribeHookState());
                    return false;
                }

                RegistrationSucceeded = true;
                RegistrationSummary = "ServerSetResourceDead+ServerSetResourceAlive+ReceiveResourceDead+ReceiveResourceAlive.Postfix OK";
                ResourceObservability.Info("[Shared]", "HarvestRegistration", "-", 0UL, 0UL, 0U,
                    "Native", false, "success", "owner=" + HarmonyId + " summary=" + RegistrationSummary);
                return true;
            }
            catch (Exception ex)
            {
                if (applied.Count > 0)
                {
                    bool rollbackClean = RollbackRegistration(harmony, applied);
                    ResourceObservability.Error("[Shared]", "HarvestRegistrationRollback", "-", 0UL, 0UL, 0U,
                        "Fallback", false, rollbackClean ? "success" : "failed",
                        "reason=registration-exception rollbackClean=" + rollbackClean);
                }
                RegistrationSummary = "登记异常: " + ex.GetType().Name + ": " + ex.Message;
                ResourceObservability.Error("[Shared]", "HarvestRegistration", "-", 0UL, 0UL, 0U,
                    "Fallback", false, "failed", "exception=" + ex.GetType().Name);
                return false;
            }
        }

        private static bool PatchAndVerify(
            Harmony harmony,
            MethodInfo original,
            MethodInfo patchMethod,
            string label,
            List<Tuple<MethodInfo, MethodInfo>> applied,
            Action<bool> stateSetter)
        {
            try
            {
                harmony.Patch(original, postfix: new HarmonyMethod(patchMethod));
                applied.Add(Tuple.Create(original, patchMethod));
                if (!VerifyPatchIdentity(original, patchMethod, out string verification))
                {
                    RegistrationSummary = label + " owner/target/patch method 验证失败: " + verification;
                    ResourceObservability.Error("[Shared]", "HarvestHookRegistration", "-", 0UL, 0UL, 0U,
                        "Fallback", false, "failed", "target=" + original.Name + " patch=" + patchMethod.Name);
                    return false;
                }

                stateSetter(true);
                return true;
            }
            catch (Exception ex)
            {
                RegistrationSummary = label + " 登记异常: " + ex.GetType().Name + ": " + ex.Message;
                ResourceObservability.Error("[Shared]", "HarvestHookRegistration", "-", 0UL, 0UL, 0U,
                    "Fallback", false, "failed", "target=" + (original == null ? "null" : original.Name) +
                    " patch=" + (patchMethod == null ? "null" : patchMethod.Name));
                return false;
            }
        }

        private static bool VerifyPatchIdentity(MethodInfo original, MethodInfo expectedPatch, out string summary)
        {
            summary = "未验证";
            if (original == null || expectedPatch == null)
            {
                summary = "original-or-patch-null";
                return false;
            }

            HarmonyLib.Patches info = Harmony.GetPatchInfo(original);
            ICollection patches = info?.Postfixes as ICollection;
            if (patches == null)
            {
                summary = "postfixes-null";
                return false;
            }

            int ownerCount = 0;
            foreach (HarmonyLib.Patch patch in patches)
            {
                if (patch.owner != HarmonyId) continue;
                ownerCount++;
                if (patch.PatchMethod == expectedPatch)
                {
                    summary = "owner=" + patch.owner + " target=" + original.DeclaringType.FullName + "." + original.Name +
                        " patch=" + expectedPatch.DeclaringType.FullName + "." + expectedPatch.Name;
                    return true;
                }
            }

            summary = "ownerCount=" + ownerCount + " expectedPatch=" + expectedPatch.DeclaringType.FullName + "." + expectedPatch.Name;
            return false;
        }

        private static bool RollbackRegistration(Harmony harmony, List<Tuple<MethodInfo, MethodInfo>> applied)
        {
            bool clean = true;
            for (int i = applied.Count - 1; i >= 0; i--)
            {
                try
                {
                    harmony.Unpatch(applied[i].Item1, applied[i].Item2);
                }
                catch (Exception ex)
                {
                    clean = false;
                    ResourceObservability.Error("[Shared]", "HarvestRegistrationRollback", "-", 0UL, 0UL, 0U,
                        "Fallback", false, "failed", "exception=" + ex.GetType().Name);
                }
                if (VerifyPatchIdentity(applied[i].Item1, applied[i].Item2, out _)) clean = false;
            }

            applied.Clear();
            ServerSetResourceDeadPostfixRegistered = false;
            ServerSetResourceAlivePostfixRegistered = false;
            ReceiveResourceDeadPostfixRegistered = false;
            ReceiveResourceAlivePostfixRegistered = false;
            RegistrationSucceeded = false;
            return clean;
        }

        private static string DescribeHookState()
        {
            return "ServerSetResourceDead=" + ServerSetResourceDeadPostfixRegistered
                + " ServerSetResourceAlive=" + ServerSetResourceAlivePostfixRegistered
                + " ReceiveResourceDead=" + ReceiveResourceDeadPostfixRegistered
                + " ReceiveResourceAlive=" + ReceiveResourceAlivePostfixRegistered;
        }

        public static void ServerSetResourceDead_Postfix(byte x, byte y, ushort index, Vector3 baseForce)
        {
            if (!HostManager.IsP2PHostMode || !HostManager.ShouldProcessClientHostListen())
            {
                ResourceObservability.Info("[Shared]", "HarvestDead", $"({x},{y})", 0UL, 0UL, 0U,
                    ResourceObservability.NativePath(false), false, "skipped", "reason=not-p2p-listen-host");
                return;
            }

            try
            {
                uint nextGen = ResourceRegionLifecycleAdapter.RecordResourceDead(x, y, index);
                RegionKey regionKey = new RegionKey(x, y);
                ResourceSnapshotAdapter.UpdateRegionGeneration(regionKey, nextGen);
                int staleBefore = ResourceSnapshotAdapter.StaleDeltaRejectCount;
                int acceptedObservers = ResourceSnapshotAdapter.RecordNativeDelta(regionKey, nextGen);
                bool rejected = ResourceSnapshotAdapter.StaleDeltaRejectCount > staleBefore;
                string outcome = rejected ? "rejected" : acceptedObservers == 0 ? "skipped" : "accepted";

                if (_deadLogCount < MaxLogCount)
                {
                    _deadLogCount++;
                    bool spiActive = SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator.IsResourceProductionActive;
                    ResourceObservability.Info("[Host]", "HarvestDead", regionKey.ToString(),
                        ResourceRegionLifecycleAdapter.CurrentSessionEpoch, 0UL, nextGen,
                    ResourceObservability.NativePath(spiActive), spiActive, outcome,
                        $"index={index} spiDeltaObservers={acceptedObservers} stale={rejected} " +
                        $"accepted={acceptedObservers > 0} rejected={rejected} deltaSequence={ResourceSnapshotAdapter.GetLatestDeltaSequence(regionKey)} " +
                        "stateEncoder=ResourceManager.ServerSetResourceDead");
                }
                else ResourceObservability.QuotaSuppressed("[Host]", "HarvestDead", regionKey.ToString(),
                    ResourceRegionLifecycleAdapter.CurrentSessionEpoch, 0UL, nextGen, "Native",
                    SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator.IsResourceProductionActive,
                    "ResourceHarvest.ServerSetResourceDead");
            }
            catch (Exception ex)
            {
                ResourceObservability.Error("[Host]", "HarvestDead", $"({x},{y})",
                    ResourceRegionLifecycleAdapter.CurrentSessionEpoch, 0UL, 0U, "Fallback", true,
                    "failed", "reason=" + GenerationFailureReason(ex) + " exception=" + ex.GetType().Name);
            }
        }

        public static void ServerSetResourceAlive_Postfix(byte x, byte y, ushort index)
        {
            if (!HostManager.IsP2PHostMode || !HostManager.ShouldProcessClientHostListen())
            {
                ResourceObservability.Info("[Shared]", "HarvestAlive", $"({x},{y})", 0UL, 0UL, 0U,
                    ResourceObservability.NativePath(false), false, "skipped", "reason=not-p2p-listen-host");
                return;
            }

            try
            {
                uint nextGen = ResourceRegionLifecycleAdapter.RecordResourceAlive(x, y, index);
                RegionKey regionKey = new RegionKey(x, y);
                ResourceSnapshotAdapter.UpdateRegionGeneration(regionKey, nextGen);
                int staleBefore = ResourceSnapshotAdapter.StaleDeltaRejectCount;
                int acceptedObservers = ResourceSnapshotAdapter.RecordNativeDelta(regionKey, nextGen);
                bool rejected = ResourceSnapshotAdapter.StaleDeltaRejectCount > staleBefore;
                string outcome = rejected ? "rejected" : acceptedObservers == 0 ? "skipped" : "accepted";

                if (_aliveLogCount < MaxLogCount)
                {
                    _aliveLogCount++;
                    bool spiActive = SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator.IsResourceProductionActive;
                    ResourceObservability.Info("[Host]", "HarvestAlive", regionKey.ToString(),
                        ResourceRegionLifecycleAdapter.CurrentSessionEpoch, 0UL, nextGen,
                        ResourceObservability.NativePath(spiActive), spiActive, outcome,
                        $"index={index} spiDeltaObservers={acceptedObservers} stale={rejected} " +
                        $"accepted={acceptedObservers > 0} rejected={rejected} deltaSequence={ResourceSnapshotAdapter.GetLatestDeltaSequence(regionKey)} " +
                        "stateEncoder=ResourceManager.ServerSetResourceAlive");
                }
                else ResourceObservability.QuotaSuppressed("[Host]", "HarvestAlive", regionKey.ToString(),
                    ResourceRegionLifecycleAdapter.CurrentSessionEpoch, 0UL, nextGen, "Native",
                    SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator.IsResourceProductionActive,
                    "ResourceHarvest.ServerSetResourceAlive");
            }
            catch (Exception ex)
            {
                ResourceObservability.Error("[Host]", "HarvestAlive", $"({x},{y})",
                    ResourceRegionLifecycleAdapter.CurrentSessionEpoch, 0UL, 0U, "Fallback", true,
                    "failed", "reason=" + GenerationFailureReason(ex) + " exception=" + ex.GetType().Name);
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
                ResourceObservability.Info("[Host]", "DeltaReceive", $"({x},{y})",
                    ResourceRegionLifecycleAdapter.CurrentSessionEpoch, 0UL, 0U,
                    ResourceObservability.NativePath(false), false, "skipped", "reason=host-does-not-decode-client-delta");
                return;
            }

            try
            {
                RegionKey regionKey = new RegionKey(x, y);
                ResourceDeltaReceiveObservation observation = ResourceSnapshotAdapter.RecordNativeDeltaReceive(
                    regionKey, ResourceSnapshotAdapter.LocalConnectionGeneration);
                ResourceObservability.Info("[Guest]", "DeltaReceive", regionKey.ToString(),
                    observation.SessionEpoch, observation.ConnectionGeneration,
                    observation.RegionGeneration, "Native", false, "observed",
                    $"index={index} deltaSequence={observation.DeltaSequence} stale=unknown accepted=unknown rejected=unknown " +
                    "deltaSequenceSource=local-receive-observation decisionSource=not-available " +
                    $"stateDecoder=ResourceManager.{decoder}");
            }
            catch (Exception ex)
            {
                ResourceObservability.Error("[Guest]", "DeltaReceive", $"({x},{y})",
                    ResourceSnapshotAdapter.CurrentSessionEpoch, ResourceSnapshotAdapter.LocalConnectionGeneration, 0U, "Fallback", false,
                    "failed", "decoder=" + decoder + " exception=" + ex.GetType().Name);
            }
        }

        private static string GenerationFailureReason(Exception ex)
        {
            return ex != null && ex.Message.IndexOf("exhausted", StringComparison.OrdinalIgnoreCase) >= 0
                ? "generation-overflow"
                : "generation-update-failed";
        }

        public static void ResetLogCounters()
        {
            _deadLogCount = 0;
            _aliveLogCount = 0;
        }
    }
}
