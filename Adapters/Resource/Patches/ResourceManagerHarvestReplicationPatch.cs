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
        internal interface IHarvestPatchBackend
        {
            void Patch(MethodInfo original, MethodInfo patchMethod);
            void Unpatch(MethodInfo original, MethodInfo patchMethod);
            bool HasPatch(MethodInfo original, MethodInfo patchMethod, out string summary);
        }

        internal sealed class HookRegistrationState
        {
            internal HookRegistrationState(string name, string owner)
            {
                Name = name;
                Owner = owner;
                Target = "unresolved";
                PatchMethod = "unresolved";
                Outcome = "not-attempted";
                RollbackStatus = "not-run";
            }

            public string Name { get; }
            public string Owner { get; }
            public string Target { get; internal set; }
            public string PatchMethod { get; internal set; }
            public bool RegistrationAttempted { get; internal set; }
            public bool Registered { get; internal set; }
            public string Outcome { get; internal set; }
            public string Verification { get; internal set; }
            public string RollbackStatus { get; internal set; }
            public bool RollbackResidual { get; internal set; }
            public bool RollbackResidualKnown { get; internal set; }
            public bool RollbackVerificationCompleted { get; internal set; }

            public override string ToString()
            {
                return Name + "{owner=" + Owner + " target=" + Target + " patch=" + PatchMethod +
                    " attempted=" + RegistrationAttempted + " registered=" + Registered +
                     " outcome=" + Outcome + " rollback=" + RollbackStatus +
                     " residual=" + RollbackResidual +
                     " residualKnown=" + RollbackResidualKnown +
                     " rollbackVerificationCompleted=" + RollbackVerificationCompleted + "}";
            }
        }

        private sealed class HarmonyPatchBackend : IHarvestPatchBackend
        {
            private readonly Harmony _harmony;

            internal HarmonyPatchBackend(Harmony harmony) { _harmony = harmony; }

            public void Patch(MethodInfo original, MethodInfo patchMethod)
            {
                _harmony.Patch(original, postfix: new HarmonyMethod(patchMethod));
            }

            public void Unpatch(MethodInfo original, MethodInfo patchMethod)
            {
                _harmony.Unpatch(original, patchMethod);
            }

            public bool HasPatch(MethodInfo original, MethodInfo patchMethod, out string summary)
            {
                return VerifyPatchIdentity(original, patchMethod, out summary);
            }
        }

        private const string HarmonyId = SteamP2PFriendsPlugin.HARMONY_ID;
        public static bool RegistrationSucceeded { get; private set; }
        public static string RegistrationSummary { get; private set; } = "未登记";
        internal static IReadOnlyList<HookRegistrationState> HookStates { get; private set; }

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
            if (harmony == null)
            {
                ResetHookState();
                RegistrationSummary = "harmony=null";
                ResourceObservability.Error("[Shared]", "HarvestRegistration", "-", 0UL, 0UL, 0U,
                    "Fallback", false, "failed", "reason=harmony-null");
                return false;
            }

            return RegisterManualCore(new HarmonyPatchBackend(harmony));
        }

        internal static bool RegisterManualForTesting(IHarvestPatchBackend backend)
        {
            return RegisterManualCore(backend);
        }

        private static bool RegisterManualCore(IHarvestPatchBackend backend)
        {
            ResetHookState();
            if (backend == null)
            {
                RegistrationSummary = "backend=null";
                ResourceObservability.Error("[Shared]", "HarvestRegistration", "-", 0UL, 0UL, 0U,
                    "Fallback", false, "failed", "reason=backend-null");
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
                    MarkResolutionFailure("ServerSetResourceDead", deadMethod, deadPostfix);
                    MarkResolutionFailure("ServerSetResourceAlive", aliveMethod, alivePostfix);
                    MarkResolutionFailure("ReceiveResourceDead", receiveDeadMethod, receiveDeadPostfix);
                    MarkResolutionFailure("ReceiveResourceAlive", receiveAliveMethod, receiveAlivePostfix);
                    RegistrationSummary = "目标或补丁方法解析失败";
                    ResourceObservability.Error("[Shared]", "HarvestRegistration", "-", 0UL, 0UL, 0U,
                        "Fallback", false, "failed", "reason=target-or-patch-missing");
                    return false;
                }

                if (!PatchAndVerify(backend, deadMethod, deadPostfix, "ServerSetResourceDead", applied,
                        value => ServerSetResourceDeadPostfixRegistered = value)
                    || !PatchAndVerify(backend, aliveMethod, alivePostfix, "ServerSetResourceAlive", applied,
                        value => ServerSetResourceAlivePostfixRegistered = value)
                    || !PatchAndVerify(backend, receiveDeadMethod, receiveDeadPostfix, "ReceiveResourceDead", applied,
                        value => ReceiveResourceDeadPostfixRegistered = value)
                    || !PatchAndVerify(backend, receiveAliveMethod, receiveAlivePostfix, "ReceiveResourceAlive", applied,
                        value => ReceiveResourceAlivePostfixRegistered = value))
                {
                    int appliedCount = applied.Count;
                    bool rollbackClean = RollbackRegistration(backend, applied);
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
                    bool rollbackClean = RollbackRegistration(backend, applied);
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
            IHarvestPatchBackend backend,
            MethodInfo original,
            MethodInfo patchMethod,
            string label,
            List<Tuple<MethodInfo, MethodInfo>> applied,
            Action<bool> stateSetter)
        {
            HookRegistrationState state = GetHookState(label);
            state.RegistrationAttempted = true;
            state.Target = FormatMethod(original);
            state.PatchMethod = FormatMethod(patchMethod);
            try
            {
                if (backend.HasPatch(original, patchMethod, out string preflightVerification))
                {
                    stateSetter(true);
                    state.Registered = true;
                    state.Outcome = "already-registered";
                    state.Verification = preflightVerification;
                    ResourceObservability.Info("[Shared]", "HarvestHookRegistration", "-", 0UL, 0UL, 0U,
                        "Native", false, "skipped", "hook=" + label + " owner=" + state.Owner +
                        " target=" + state.Target + " patch=" + state.PatchMethod +
                        " verification=" + preflightVerification + " reason=already-registered");
                    return true;
                }

                if (HasOwnedRegistrationConflict(preflightVerification))
                {
                    state.Outcome = "failed";
                    state.Verification = preflightVerification;
                    RegistrationSummary = label + " owner/patch 唯一性验证失败: " + preflightVerification;
                    ResourceObservability.Error("[Shared]", "HarvestHookRegistration", "-", 0UL, 0UL, 0U,
                        "Fallback", false, "failed", "hook=" + label + " owner=" + HarmonyId +
                        " target=" + state.Target + " patch=" + state.PatchMethod +
                        " verification=" + preflightVerification + " reason=duplicate-owned-registration");
                    return false;
                }

                applied.Add(Tuple.Create(original, patchMethod));
                backend.Patch(original, patchMethod);
                if (!backend.HasPatch(original, patchMethod, out string verification))
                {
                    state.Outcome = "failed";
                    state.Verification = verification;
                    RegistrationSummary = label + " owner/target/patch method 验证失败: " + verification;
                    ResourceObservability.Error("[Shared]", "HarvestHookRegistration", "-", 0UL, 0UL, 0U,
                        "Fallback", false, "failed", "hook=" + label + " owner=" + HarmonyId +
                        " target=" + state.Target + " patch=" + state.PatchMethod +
                        " verification=" + verification);
                    return false;
                }

                stateSetter(true);
                state.Registered = true;
                state.Outcome = "success";
                state.Verification = verification;
                ResourceObservability.Info("[Shared]", "HarvestHookRegistration", "-", 0UL, 0UL, 0U,
                    "Native", false, "success", "hook=" + label + " owner=" + state.Owner +
                    " target=" + state.Target + " patch=" + state.PatchMethod +
                    " verification=" + verification);
                return true;
            }
            catch (Exception ex)
            {
                state.Outcome = "failed";
                state.Verification = "exception=" + ex.GetType().Name;
                RegistrationSummary = label + " 登记异常: " + ex.GetType().Name + ": " + ex.Message;
                ResourceObservability.Error("[Shared]", "HarvestHookRegistration", "-", 0UL, 0UL, 0U,
                    "Fallback", false, "failed", "target=" + (original == null ? "null" : original.Name) +
                    " patch=" + (patchMethod == null ? "null" : patchMethod.Name) +
                    " owner=" + state.Owner + " exception=" + ex.GetType().Name);
                return false;
            }
        }

        private static void MarkResolutionFailure(
            string label,
            MethodInfo original,
            MethodInfo patchMethod)
        {
            HookRegistrationState state = GetHookState(label);
            state.RegistrationAttempted = true;
            state.Target = FormatMethod(original);
            state.PatchMethod = FormatMethod(patchMethod);
            state.Outcome = "failed";
            state.Verification = "target-or-patch-missing";
            state.RollbackStatus = "not-required";
            ResourceObservability.Error("[Shared]", "HarvestHookResolution", "-", 0UL, 0UL, 0U,
                "Fallback", false, "failed", "hook=" + label + " owner=" + state.Owner +
                " target=" + state.Target + " patch=" + state.PatchMethod +
                " reason=target-or-patch-missing");
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
            int matchingPatchCount = 0;
            foreach (HarmonyLib.Patch patch in patches)
            {
                if (patch.owner != HarmonyId) continue;
                ownerCount++;
                if (patch.PatchMethod == expectedPatch)
                    matchingPatchCount++;
            }

            summary = "ownerCount=" + ownerCount + " matchingPatchCount=" + matchingPatchCount +
                " target=" + original.DeclaringType.FullName + "." + original.Name +
                " expectedPatch=" + expectedPatch.DeclaringType.FullName + "." + expectedPatch.Name;
            return ownerCount == 1 && matchingPatchCount == 1;
        }

        private static bool HasOwnedRegistrationConflict(string verification)
        {
            if (string.IsNullOrEmpty(verification)) return false;
            const string prefix = "ownerCount=";
            if (!verification.StartsWith(prefix, StringComparison.Ordinal)) return false;
            int start = prefix.Length;
            int end = verification.IndexOf(' ', start);
            string value = end < 0 ? verification.Substring(start) : verification.Substring(start, end - start);
            return int.TryParse(value, out int ownerCount) && ownerCount > 0;
        }

        private static bool RollbackRegistration(IHarvestPatchBackend backend, List<Tuple<MethodInfo, MethodInfo>> applied)
        {
            bool clean = true;
            for (int i = applied.Count - 1; i >= 0; i--)
            {
                Tuple<MethodInfo, MethodInfo> hook = applied[i];
                HookRegistrationState state = FindHookState(hook.Item1, hook.Item2);
                bool unpatchSucceeded = true;
                try
                {
                    backend.Unpatch(hook.Item1, hook.Item2);
                }
                catch (Exception ex)
                {
                    unpatchSucceeded = false;
                    clean = false;
                    if (state != null)
                    {
                        state.RollbackStatus = "unpatch-failed-exception=" + ex.GetType().Name;
                        state.RollbackVerificationCompleted = false;
                    }
                    ResourceObservability.Error("[Shared]", "HarvestRegistrationRollback", "-", 0UL, 0UL, 0U,
                        "Fallback", false, "failed", "exception=" + ex.GetType().Name);
                }
                bool residual = true;
                string verification = "unverified";
                bool verificationCompleted = false;
                try
                {
                    residual = backend.HasPatch(hook.Item1, hook.Item2, out verification);
                    verificationCompleted = true;
                }
                catch (Exception ex)
                {
                    clean = false;
                    if (state != null)
                    {
                        state.Registered = false;
                        state.RollbackResidual = false;
                        state.RollbackResidualKnown = false;
                        state.RollbackVerificationCompleted = false;
                        state.RollbackStatus = "rollback-verification-failed-exception=" + ex.GetType().Name;
                        state.Verification = "exception=" + ex.GetType().Name;
                    }
                    ResourceObservability.Error("[Shared]", "HarvestRegistrationRollback", "-", 0UL, 0UL, 0U,
                        "Fallback", false, "failed",
                        "reason=rollback-verification-failed residual=unknown exception=" + ex.GetType().Name);
                }
                if (state != null)
                {
                    if (verificationCompleted)
                    {
                        state.Registered = residual;
                        state.RollbackResidual = residual;
                        state.RollbackResidualKnown = true;
                        state.RollbackVerificationCompleted = true;
                        state.RollbackStatus = !unpatchSucceeded
                            ? "unpatch-failed; verificationResult=" + (residual ? "residual" : "clean")
                            : residual ? "residual" : "verified-clean";
                        state.Verification = verification;
                    }
                    ResourceObservability.Info("[Shared]", "HarvestHookRollback", "-", 0UL, 0UL, 0U,
                        !unpatchSucceeded || !verificationCompleted || residual ? "Fallback" : "Native", false,
                        !unpatchSucceeded || !verificationCompleted || residual ? "failed" : "success", "hook=" + state.Name +
                        " owner=" + state.Owner + " target=" + state.Target +
                        " patch=" + state.PatchMethod + " rollbackStatus=" + state.RollbackStatus);
                }
                if (!verificationCompleted || residual) clean = false;
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
            if (HookStates == null) return "uninitialized";
            return string.Join(" | ", HookStates);
        }

        private static void ResetHookState()
        {
            RegistrationSucceeded = false;
            ServerSetResourceDeadPostfixRegistered = false;
            ServerSetResourceAlivePostfixRegistered = false;
            ReceiveResourceDeadPostfixRegistered = false;
            ReceiveResourceAlivePostfixRegistered = false;
            HookStates = new List<HookRegistrationState>
            {
                new HookRegistrationState("ServerSetResourceDead", HarmonyId),
                new HookRegistrationState("ServerSetResourceAlive", HarmonyId),
                new HookRegistrationState("ReceiveResourceDead", HarmonyId),
                new HookRegistrationState("ReceiveResourceAlive", HarmonyId)
            };
        }

        private static HookRegistrationState GetHookState(string name)
        {
            foreach (HookRegistrationState state in HookStates)
                if (state.Name == name) return state;
            throw new InvalidOperationException("Unknown Resource Harvest hook: " + name);
        }

        private static HookRegistrationState FindHookState(MethodInfo original, MethodInfo patchMethod)
        {
            if (HookStates == null) return null;
            string target = FormatMethod(original);
            string patch = FormatMethod(patchMethod);
            foreach (HookRegistrationState state in HookStates)
                if (state.Target == target && state.PatchMethod == patch) return state;
            return null;
        }

        private static string FormatMethod(MethodInfo method)
        {
            if (method == null) return "null";
            ParameterInfo[] parameters = method.GetParameters();
            var signature = new List<string>();
            foreach (ParameterInfo parameter in parameters)
                signature.Add(parameter.ParameterType.FullName ?? parameter.ParameterType.Name);
            return (method.DeclaringType?.FullName ?? "<unknown>") + "." + method.Name +
                "(" + string.Join(",", signature) + ")";
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
                RegionKey regionKey = new RegionKey(x, y);
                if (!TryValidateNativeHarvestOutcome(regionKey, index, expectedDead: true, out string nativeReason))
                {
                    bool spiActive = SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator.IsResourceProductionActive;
                    ResourceObservability.Warn("[Host]", "HarvestDead", regionKey.ToString(),
                        ResourceRegionLifecycleAdapter.CurrentSessionEpoch, 0UL,
                        ResourceSnapshotAdapter.GetRegionGeneration(regionKey),
                        "Fallback", spiActive, "rejected", "reason=" + nativeReason +
                        " ledgerUpdate=not-run");
                    return;
                }
                uint nextGen = ResourceRegionLifecycleAdapter.RecordResourceDead(x, y, index);
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
                else
                {
                    bool spiActive = SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator.IsResourceProductionActive;
                    ResourceObservability.QuotaSuppressed("[Host]", "HarvestDead", regionKey.ToString(),
                        ResourceRegionLifecycleAdapter.CurrentSessionEpoch, 0UL, nextGen,
                        ResourceObservability.NativePath(spiActive), spiActive,
                        "ResourceHarvest.ServerSetResourceDead");
                }
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
                RegionKey regionKey = new RegionKey(x, y);
                if (!TryValidateNativeHarvestOutcome(regionKey, index, expectedDead: false, out string nativeReason))
                {
                    bool spiActive = SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator.IsResourceProductionActive;
                    ResourceObservability.Warn("[Host]", "HarvestAlive", regionKey.ToString(),
                        ResourceRegionLifecycleAdapter.CurrentSessionEpoch, 0UL,
                        ResourceSnapshotAdapter.GetRegionGeneration(regionKey),
                        "Fallback", spiActive, "rejected", "reason=" + nativeReason +
                        " ledgerUpdate=not-run");
                    return;
                }
                uint nextGen = ResourceRegionLifecycleAdapter.RecordResourceAlive(x, y, index);
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
                else
                {
                    bool spiActive = SteamP2PFriends.MultiObserver.MultiObserverShadowCoordinator.IsResourceProductionActive;
                    ResourceObservability.QuotaSuppressed("[Host]", "HarvestAlive", regionKey.ToString(),
                        ResourceRegionLifecycleAdapter.CurrentSessionEpoch, 0UL, nextGen,
                        ResourceObservability.NativePath(spiActive), spiActive,
                        "ResourceHarvest.ServerSetResourceAlive");
                }
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

        private static bool TryValidateNativeHarvestOutcome(
            RegionKey regionKey,
            ushort index,
            bool expectedDead,
            out string reason)
        {
            try
            {
                List<ResourceSpawnpoint> trees = LevelGround.GetTreesOrNullInRegion(regionKey.X, regionKey.Y);
                if (trees == null)
                {
                    reason = "native-resource-trees-unavailable";
                    return false;
                }
                if (index >= trees.Count || trees[index] == null)
                {
                    reason = index >= trees.Count
                        ? "native-resource-index-out-of-range"
                        : "native-resource-tree-null";
                    return false;
                }

                bool nativeDead = trees[index].isDead;
                bool ledgerBefore = ResourceRegionLifecycleAdapter.IsResourceDead(
                    regionKey.X, regionKey.Y, index);
                if (nativeDead != expectedDead)
                {
                    reason = "native-postcondition-mismatch";
                    return false;
                }
                if (ledgerBefore == expectedDead)
                {
                    reason = "native-no-op-or-unobserved-transition";
                    return false;
                }

                reason = "none";
                return true;
            }
            catch (Exception ex)
            {
                reason = "native-postcondition-read-failed:" + ex.GetType().Name;
                return false;
            }
        }

        private static string GenerationFailureReason(Exception ex)
        {
            return ex is ResourceGenerationExhaustedException
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
