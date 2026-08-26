using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using SDG.NetPak;
using SDG.NetTransport;
using SDG.Unturned;
using SteamP2PFriends.Client;
using SteamP2PFriends.Core.Registration;
using SteamP2PFriends.Host;
using SteamP2PFriends.MultiObserver;
using SteamP2PFriends.Core.Patches;
using SteamP2PFriends.Shared;
using SteamP2PFriends.UI;
using Steamworks;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;


using SteamP2PFriends.Security;

using SteamP2PFriends.Security.Patches;

namespace SteamP2PFriends
{
    public partial class SteamP2PFriendsPlugin
    {
        private bool HasOwnedPatch(MethodBase original, MethodInfo expectedPatch, bool prefix)
        {
            if (original == null || expectedPatch == null) return false;
            HarmonyLib.Patches info = Harmony.GetPatchInfo(original);
            if (info == null) return false;
            IEnumerable<Patch> patches = prefix ? info.Prefixes : info.Postfixes;
            foreach (Patch patch in patches)
            {
                if (patch.owner == HARMONY_ID && patch.PatchMethod == expectedPatch) return true;
            }
            return false;
        }

        private bool VerifyRouteBAttributeRegistrations()
        {
            MethodInfo whitelistOriginal = AccessTools.Method(typeof(SteamWhitelist),
                nameof(SteamWhitelist.checkWhitelisted), new[] { typeof(CSteamID) });
            MethodInfo whitelistPostfix = AccessTools.Method(
                typeof(Patch_ServerConnectValidation), nameof(Patch_ServerConnectValidation.Postfix));

            MethodInfo damageOriginal = AccessTools.Method(typeof(PlayerLife), nameof(PlayerLife.askDamage),
                new[]
                {
                    typeof(byte), typeof(Vector3), typeof(EDeathCause), typeof(ELimb), typeof(CSteamID),
                    typeof(EPlayerKill).MakeByRefType(), typeof(bool), typeof(ERagdollEffect), typeof(bool), typeof(bool)
                });
            MethodInfo damagePrefix = AccessTools.Method(
                typeof(P2PQuarantineDamageGuardPatch), nameof(P2PQuarantineDamageGuardPatch.Prefix));

            MethodInfo inputOriginal = AccessTools.Method(typeof(PlayerInput), nameof(PlayerInput.ReceiveInputs));
            MethodInfo inputPostfix = AccessTools.Method(
                typeof(P2PQuarantineClientInputPatch), nameof(P2PQuarantineClientInputPatch.Postfix));

            bool handshakeOk = HasOwnedPatch(whitelistOriginal, whitelistPostfix, false);
            bool damageOk = HasOwnedPatch(damageOriginal, damagePrefix, true);
            bool inputOk = HasOwnedPatch(inputOriginal, inputPostfix, false);
            if (!handshakeOk || !damageOk || !inputOk)
            {
                RoleLogger.Error("[Shared]",
                    "[P2P-Approval] Route B attribute patch missing handshake=" + handshakeOk +
                    " damage=" + damageOk + " input=" + inputOk);
            }
            return handshakeOk && damageOk && inputOk;
        }

        private bool VerifyRouteBRegistration(bool requireLifecycleHooks)
        {
            return P2PQuarantineActionGatePatch.RegistrationValid &&
                Patch_PlayerDashboardPlayersUI.RegistrationValid &&
                VerifyRouteBAttributeRegistrations() &&
                P2PApprovalManager.QuarantineSignalMask == 0x80000000u &&
                (!requireLifecycleHooks || P2PApprovalManager.LifecycleHooksInstalled);
        }

        private bool VerifyStage78UnifiedConnectRegistrations()
        {
            MethodBase routeOriginal = Core.Patches.MenuPlayConnectP2PRoutePatch.TargetMethod();
            MethodInfo routePrefix = AccessTools.Method(typeof(Core.Patches.MenuPlayConnectP2PRoutePatch), "Prefix");
            MethodBase indicatorOriginal = Core.Patches.MenuPlayConnectP2PIndicatorPatch.TargetMethod();
            MethodInfo indicatorPostfix = AccessTools.Method(typeof(Core.Patches.MenuPlayConnectP2PIndicatorPatch), "Postfix");

            bool routeOk = HasOwnedPatch(routeOriginal, routePrefix, true);
            bool indicatorOk = HasOwnedPatch(indicatorOriginal, indicatorPostfix, false);
            if (!routeOk || !indicatorOk)
            {
                RoleLogger.Error("[Shared]", "[Stage7-8] patch missing route=" + routeOk +
                    " indicator=" + indicatorOk);
            }
            else
            {
                RoleLogger.Info("[Shared]", "[Stage7-8] OK vanilla connect SteamID route + indicator registered");
            }
            return routeOk && indicatorOk;
        }

        /// <summary>
        /// under our Harmony owner with the exact Postfix MethodInfo (not just attribute presence).
        /// Failure aggregates into DiagnosticBuildValid=false.
        /// </summary>
        private bool VerifyStage92SinglePortRegistration()
        {
            MethodBase original = Core.Patches.DirectIpSinglePortQueryPortPatch.TargetMethod();
            MethodInfo postfix = AccessTools.Method(
                typeof(Core.Patches.DirectIpSinglePortQueryPortPatch),
                nameof(Core.Patches.DirectIpSinglePortQueryPortPatch.Postfix));

            bool ok = HasOwnedPatch(original, postfix, false);
            if (!ok)
            {
                RoleLogger.Error("[Shared]",
                    "[Stage9-2] Direct-IP single-port query projection patch missing " +
                    "(original=" + (original == null ? "null" : original.Name) +
                    " postfix=" + (postfix == null ? "null" : postfix.Name) + ")");
            }
            else
            {
                RoleLogger.Info("[Shared]",
                    "[Stage9-2] OK single-port Direct-IP query projection registered");
            }
            return ok;
        }

        private bool VerifyStage10DeathCommitRegistration()
        {
            MethodBase original = Core.Patches.P2PWorldDeathCommitPatch.TargetMethod();
            MethodInfo prefix = AccessTools.Method(typeof(Core.Patches.P2PWorldDeathCommitPatch),
                nameof(Core.Patches.P2PWorldDeathCommitPatch.Prefix));
            MethodInfo postfix = AccessTools.Method(typeof(Core.Patches.P2PWorldDeathCommitPatch),
                nameof(Core.Patches.P2PWorldDeathCommitPatch.Postfix));

            bool ok = HasOwnedPatch(original, prefix, true) &&
                      HasOwnedPatch(original, postfix, false);
            if (ok)
                RoleLogger.Info("[Shared]",
                    "[Stage10] OK authoritative death commit fallback registered");
            else
                RoleLogger.Error("[Shared]",
                    "[Stage10] authoritative death commit fallback registration missing");
            return ok;
        }

        private bool VerifyStage10ChatAvatarRegistration()
        {
            MethodInfo prefix = AccessTools.Method(typeof(Core.Patches.P2PWorldChatAvatarPatch),
                nameof(Core.Patches.P2PWorldChatAvatarPatch.PrefixProject));
            MethodInfo v1 = AccessTools.PropertySetter(typeof(SleekChatEntryV1),
                "representingChatMessage");
            MethodInfo v2 = AccessTools.PropertySetter(typeof(SleekChatEntryV2),
                "representingChatMessage");
            bool ok = HasOwnedPatch(v1, prefix, true) && HasOwnedPatch(v2, prefix, true);
            if (ok)
                RoleLogger.Info("[Shared]", "[Stage10] OK chat avatar projection registered V1+V2");
            else
                RoleLogger.Error("[Shared]", "[Stage10] chat avatar projection registration missing");
            return ok;
        }

        private bool VerifyInventoryUiProjectionRegistration()
        {
            System.Type[] dragArgs =
            {
                typeof(byte), typeof(byte), typeof(byte), typeof(byte),
                typeof(byte), typeof(byte), typeof(byte)
            };
            System.Type[] swapArgs =
            {
                typeof(byte), typeof(byte), typeof(byte), typeof(byte),
                typeof(byte), typeof(byte), typeof(byte), typeof(byte)
            };
            System.Type[] dropArgs = { typeof(byte), typeof(byte), typeof(byte) };

            MethodInfo drag = AccessTools.Method(typeof(PlayerInventory),
                nameof(PlayerInventory.ReceiveDragItem), dragArgs);
            MethodInfo swap = AccessTools.Method(typeof(PlayerInventory),
                nameof(PlayerInventory.ReceiveSwapItem), swapArgs);
            MethodInfo drop = AccessTools.Method(typeof(PlayerInventory),
                nameof(PlayerInventory.ReceiveDropItem), dropArgs);

            bool ok = Core.Patches.ListenHostInventoryUiProjectionPatch.ReflectionContractAvailable &&
                      HasOwnedPatch(drag,
                          Core.Patches.ListenHostInventoryUiProjectionPatch.DragPostfix, false) &&
                      HasOwnedPatch(swap,
                          Core.Patches.ListenHostInventoryUiProjectionPatch.SwapPostfix, false) &&
                      HasOwnedPatch(drop,
                          Core.Patches.ListenHostInventoryUiProjectionPatch.DropPostfix, false);
            if (ok)
                RoleLogger.Info("[Shared]",
                    "[InventoryUI-Reconcile] OK listen-host drag/swap/drop projection repair registered");
            else
                RoleLogger.Error("[Shared]",
                    "[InventoryUI-Reconcile] projection repair registration or reflection contract missing");
            return ok;
        }

        /// <summary>
        /// 任一关键 Patch 缺失时输出 DIAGNOSTIC BUILD INVALID 并禁用所有 P2P 入口。
        ///   （declaring type + method name），不只依赖 owner + 数量。
        /// </summary>
    }
}
