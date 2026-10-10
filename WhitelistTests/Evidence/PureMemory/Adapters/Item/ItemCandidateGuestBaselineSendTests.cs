using HarmonyLib;
using SDG.NetTransport;
using SDG.Unturned;
using SteamP2PFriends.Adapters.Item;
using SteamP2PFriends.Adapters.Item.Patches;
using SteamP2PFriends.Host;
using SteamP2PFriends.MultiObserver;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class ItemCandidateGuestBaselineSendTests
    {
        internal static bool Test_IT27_CandidateOwnedAskItemsRunsAndRejectedSendDoesNotCommit()
        {
            ItemProductionMode savedMode = MultiObserverShadowCoordinator.ItemProductionMode;
            bool savedReady = ItemObserverReplicationAdapter.IsReady;
            FieldInfo fDedicated = typeof(Dedicator).GetField("_isDedicated", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo fServer = typeof(Provider).GetField("_isServer", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo fHostMode = typeof(HostManager).GetField("_hostMode", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo fP2P = typeof(HostManager).GetField("<IsP2PServerActive>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            if (fDedicated == null || fServer == null || fHostMode == null || fP2P == null) return false;
            object savedDedicated = fDedicated.GetValue(null);
            object savedServer = fServer.GetValue(null);
            object savedHostMode = fHostMode.GetValue(null);
            object savedP2P = fP2P.GetValue(null);
            try
            {
                if (!TryPatchAskItems()) return false;
                ItemObserverReplicationAdapter.SetRegistrationReady(true);
                ApplyRole(fDedicated, fServer, fHostMode, fP2P, false, true, "P2P", true);
                MultiObserverShadowCoordinator.ItemProductionMode = ItemProductionMode.Candidate;
                int before = _nativeBodyRuns;
                bool loaded = false;
                ItemObserverReplicationAdapter.DispatchCandidateGuestAskItems(
                    InvokePatchedAskItems,
                    () => loaded = true);
                if (_nativeBodyRuns != before + 1 || !loaded) return false;
                int legacyBefore = _nativeBodyRuns;
                InvokePatchedAskItems();
                if (_nativeBodyRuns != legacyBefore) return false;
                return RejectedSendDoesNotCommit() && NativeRoleBoundaries(fDedicated, fServer, fHostMode, fP2P);
            }
            finally
            {
                UnpatchAskItems();
                MultiObserverShadowCoordinator.ItemProductionMode = savedMode;
                ItemObserverReplicationAdapter.SetRegistrationReady(savedReady);
                fP2P.SetValue(null, savedP2P);
                fHostMode.SetValue(null, savedHostMode);
                fServer.SetValue(null, savedServer);
                fDedicated.SetValue(null, savedDedicated);
            }
        }

        private static int _nativeBodyRuns;
        private static MethodInfo _askItems;
        private static object _manager;
        private static Harmony _harmony;

        private static void NoteNativeBody() { _nativeBodyRuns++; }

        private static IEnumerable<CodeInstruction> ReplaceAskItemsBody(IEnumerable<CodeInstruction> _)
        {
            return new[]
            {
                new CodeInstruction(OpCodes.Call, typeof(ItemCandidateGuestBaselineSendTests).GetMethod(
                    nameof(NoteNativeBody), BindingFlags.Static | BindingFlags.NonPublic)),
                new CodeInstruction(OpCodes.Ret)
            };
        }

        private static bool TryPatchAskItems()
        {
            _askItems = typeof(ItemManager).GetMethod("askItems", BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(ITransportConnection), typeof(byte), typeof(byte), typeof(float) }, null);
            MethodInfo prefix = typeof(ItemManagerRegionSyncPatch).GetMethod("AskItems_Prefix", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo postfix = typeof(ItemManagerRegionSyncPatch).GetMethod("AskItems_Postfix", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo body = typeof(ItemCandidateGuestBaselineSendTests).GetMethod(nameof(ReplaceAskItemsBody), BindingFlags.Static | BindingFlags.NonPublic);
            if (_askItems == null || prefix == null || postfix == null || body == null) return false;
            _manager = FormatterServices.GetUninitializedObject(typeof(ItemManager));
            _harmony = new Harmony("spf.it27.candidate-askitems");
            _harmony.Patch(_askItems, new HarmonyMethod(prefix), new HarmonyMethod(postfix), new HarmonyMethod(body));
            return true;
        }

        private static void UnpatchAskItems()
        {
            if (_harmony == null || _askItems == null) return;
            _harmony.Unpatch(_askItems, HarmonyPatchType.All, "spf.it27.candidate-askitems");
            _harmony = null;
        }

        private static void InvokePatchedAskItems()
        {
            _askItems.Invoke(_manager, new object[] { null, (byte)1, (byte)2, 0f });
        }

        private static bool RejectedSendDoesNotCommit()
        {
            bool loaded = false;
            bool threw = false;
            ItemObserverReplicationAdapter.SetRegistrationReady(false);
            int unreadyBefore = _nativeBodyRuns;
            loaded = false;
            threw = false;
            try
            {
                ItemObserverReplicationAdapter.DispatchCandidateGuestAskItems(
                    InvokePatchedAskItems,
                    () => loaded = true);
            }
            catch (InvalidOperationException) { threw = true; }
            ItemObserverReplicationAdapter.SetRegistrationReady(true);
            return threw && !loaded && _nativeBodyRuns == unreadyBefore;
        }

        private static void ApplyRole(
            FieldInfo dedicated, FieldInfo server, FieldInfo hostMode, FieldInfo p2pActive,
            bool isDedicated, bool isServer, string mode, bool p2p)
        {
            dedicated.SetValue(null, isDedicated);
            server.SetValue(null, isServer);
            hostMode.SetValue(null, Enum.Parse(hostMode.FieldType, mode));
            p2pActive.SetValue(null, p2p);
        }

        private static bool NativeRoleBoundaries(
            FieldInfo dedicated, FieldInfo server, FieldInfo hostMode, FieldInfo p2pActive)
        {
            if (!RoleRunsNativeBody(dedicated, server, hostMode, p2pActive, true, false, "None", false)) return false;
            if (!RoleRunsNativeBody(dedicated, server, hostMode, p2pActive, false, true, "None", false)) return false;
            if (!RoleRunsNativeBody(dedicated, server, hostMode, p2pActive, false, false, "None", false)) return false;
            MultiObserverShadowCoordinator.ItemProductionMode = ItemProductionMode.LegacyWriter;
            ApplyRole(dedicated, server, hostMode, p2pActive, false, true, "P2P", true);
            int before = _nativeBodyRuns;
            try { InvokePatchedAskItems(); }
            catch (TargetInvocationException ex) when (ex.InnerException is NotSupportedException) { return _nativeBodyRuns == before; }
            return false;
        }

        private static bool RoleRunsNativeBody(
            FieldInfo dedicated, FieldInfo server, FieldInfo hostMode, FieldInfo p2pActive,
            bool isDedicated, bool isServer, string mode, bool p2p)
        {
            ApplyRole(dedicated, server, hostMode, p2pActive, isDedicated, isServer, mode, p2p);
            int before = _nativeBodyRuns;
            InvokePatchedAskItems();
            return _nativeBodyRuns == before + 1;
        }

    }
}
