using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using SDG.NetPak;
using SDG.NetTransport;
using SDG.Unturned;
using SteamP2PFriends.Client;
using SteamP2PFriends.Adapters.Collision.Patches;
using SteamP2PFriends.Adapters.Resource.Patches;
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



namespace SteamP2PFriends
{
    public partial class SteamP2PFriendsPlugin
    {
        private bool ApplyManualWrapperPatches()
        {
            _registrationStageFailed = false;
            RoleLogger.Info("[Shared]", "[Diag] === 手动登记 15 个关键 wrapper patch ===");

            TryManualPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "CreateListenSocketIP",
                typeof(SteamUserP2PRedirectPatch), nameof(SteamUserP2PRedirectPatch.CreateListenSocketIP_Prefix));

            TryManualPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "CreateListenSocketP2P",
                typeof(SteamUserP2PRedirectPatch), nameof(SteamUserP2PRedirectPatch.CreateListenSocketP2P_Prefix));
            TryManualPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "AcceptConnection",
                typeof(SteamUserP2PRedirectPatch), nameof(SteamUserP2PRedirectPatch.AcceptConnection_Prefix));
            //   PatchAll 未自动登记 [HarmonyPostfix] attribute（根因未明，可能 Harmony 2.9 + Steamworks.NET 重载解析问题），
            //   VerifyPatchMethodPair 检测到 Postfix 缺失会强制 INVALID。
            //   修复：显式手动登记 Postfix，与 Prefix 配对。
            TryManualPatchPostfix(typeof(Steamworks.SteamGameServerNetworkingSockets), "AcceptConnection",
                typeof(SteamUserP2PRedirectPatch), nameof(SteamUserP2PRedirectPatch.AcceptConnection_Postfix));
            TryManualPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "CloseConnection",
                typeof(SteamUserP2PRedirectPatch), nameof(SteamUserP2PRedirectPatch.CloseConnection_Prefix));
            TryManualPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "SetConnectionPollGroup",
                typeof(SteamUserP2PRedirectPatch), nameof(SteamUserP2PRedirectPatch.SetConnectionPollGroup_Prefix));
            TryManualPatchPostfix(typeof(Steamworks.SteamGameServerNetworkingSockets), "SetConnectionPollGroup",
                typeof(SteamUserP2PRedirectPatch), nameof(SteamUserP2PRedirectPatch.SetConnectionPollGroup_Postfix));
            TryManualPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "CreatePollGroup",
                typeof(SteamUserP2PRedirectPatch), nameof(SteamUserP2PRedirectPatch.CreatePollGroup_Prefix));
            TryManualPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "DestroyPollGroup",
                typeof(SteamUserP2PRedirectPatch), nameof(SteamUserP2PRedirectPatch.DestroyPollGroup_Prefix));
            TryManualPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "ReceiveMessagesOnPollGroup",
                typeof(SteamUserP2PRedirectPatch), nameof(SteamUserP2PRedirectPatch.ReceiveMessagesOnPollGroup_Prefix));
            TryManualPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "CloseListenSocket",
                typeof(SteamUserP2PRedirectPatch), nameof(SteamUserP2PRedirectPatch.CloseListenSocket_Prefix));
            TryManualPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "SendMessageToConnection",
                typeof(SteamUserP2PRedirectPatch), nameof(SteamUserP2PRedirectPatch.SendMessageToConnection_Prefix));
            TryManualPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "ReceiveMessagesOnConnection",
                typeof(SteamUserP2PRedirectPatch), nameof(SteamUserP2PRedirectPatch.ReceiveMessagesOnConnection_Prefix));
            TryManualPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "GetConnectionInfo",
                typeof(SteamUserP2PRedirectPatch), nameof(SteamUserP2PRedirectPatch.GetConnectionInfo_Prefix));
            TryManualPatch(typeof(Steamworks.SteamGameServerNetworkingSockets), "SetConnectionName",
                typeof(SteamUserP2PRedirectPatch), nameof(SteamUserP2PRedirectPatch.SetConnectionName_Prefix));

            TryManualPatch(typeof(Steamworks.Callback<Steamworks.SteamNetConnectionStatusChangedCallback_t>), "CreateGameServer",
                typeof(CallbackCreateGameServerRedirectPatch), nameof(CallbackCreateGameServerRedirectPatch.ConnStatus_CreateGameServer_Prefix));
            TryManualPatch(typeof(Steamworks.Callback<Steamworks.SteamNetAuthenticationStatus_t>), "CreateGameServer",
                typeof(CallbackCreateGameServerRedirectPatch), nameof(CallbackCreateGameServerRedirectPatch.AuthStatus_CreateGameServer_Prefix));

            //   RegisterManual 返回 AllRegistrationsSucceeded，VerifyCriticalPatches 阻断门读取此值。
            try
            {
                if (!Core.Patches.ClientMethodLoopbackPatch.RegisterManual(_harmony)) _registrationStageFailed = true;
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"ClientMethodLoopbackPatch.RegisterManual 失败: {ex}");
            }

            //   Transpiler 替换 onRegionUpdated step 2 中 Dedicator.IsDedicatedServer() 调用为
            //   ListenRegionSyncEligibility.IsDedicatedOrP2PRemoteRecipient(player)，
            //   仅对远程非 loopback 玩家开放 SendRegion 资格。
            try
            {
                if (!Core.Patches.BarricadeManagerRegionSyncPatch.RegisterManual(_harmony)) _registrationStageFailed = true;
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"BarricadeManagerRegionSyncPatch.RegisterManual 失败: {ex}");
            }

            //   Transpiler 替换 onRegionUpdated step 1 中 Dedicator.IsDedicatedServer() 调用。
            try
            {
                if (!Core.Patches.StructureManagerRegionSyncPatch.RegisterManual(_harmony)) _registrationStageFailed = true;
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"StructureManagerRegionSyncPatch.RegisterManual 失败: {ex}");
            }

            //   ItemManagerRegionSyncPatch Transpiler + askItems Prefix
            //   Transpiler 替换 onRegionUpdated step 5 中 Dedicator.IsDedicatedServer() 调用，
            //   仅对远程非 loopback 玩家开放 askItems 资格。
            try
            {
                if (!Core.Patches.ItemManagerRegionSyncPatch.RegisterManual(_harmony)) _registrationStageFailed = true;
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"ItemManagerRegionSyncPatch.RegisterManual 失败: {ex}");
            }

            //   ResourceManagerRegionSyncPatch Transpiler + SendResources_Write Prefix
            //   Transpiler 替换 onRegionUpdated step 3 中 Dedicator.IsDedicatedServer() 调用。
            //   SendResources 为 ClientStaticMethod 字段无独立 ask 方法，Prefix 挂在 SendResources_Write。
            try
            {
                if (!ResourceManagerRegionSyncPatch.RegisterManual(_harmony)) _registrationStageFailed = true;
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"ResourceManagerRegionSyncPatch.RegisterManual 失败: {ex}");
            }

            //   ObjectManagerRegionSyncPatch Transpiler + askObjects Prefix
            //   Transpiler 替换 onRegionUpdated step 4 中 Dedicator.IsDedicatedServer() 调用。
            try
            {
                if (!Core.Patches.ObjectManagerRegionSyncPatch.RegisterManual(_harmony)) _registrationStageFailed = true;
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"ObjectManagerRegionSyncPatch.RegisterManual 失败: {ex}");
            }

            //   listen host is a graphical client, so vanilla only keeps static object collision around the host.
            //   Add remote guests' regional collision coverage while leaving renderer visibility host-local.
            try
            {
                if (!LevelObjectRemoteCollisionPatch.RegisterManual(_harmony)) _registrationStageFailed = true;
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"LevelObjectRemoteCollisionPatch.RegisterManual 失败: {ex}");
            }

            //   Add remote guests' resource (trees & ores) regional collision coverage.
            try
            {
                if (!Adapters.Resource.Patches.LevelGroundRemoteTreeCollisionPatch.RegisterManual(_harmony)) _registrationStageFailed = true;
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"LevelGroundRemoteTreeCollisionPatch.RegisterManual 失败: {ex}");
            }

            //   Add resource harvest & death/alive replication hooks.
            try
            {
                if (!Adapters.Resource.Patches.ResourceManagerHarvestReplicationPatch.RegisterManual(_harmony)) _registrationStageFailed = true;
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"ResourceManagerHarvestReplicationPatch.RegisterManual 失败: {ex}");
            }

            //   Add player barricade & structure state replication hooks (M7).
            try
            {
                if (!Adapters.Structure.Patches.BarricadeStateReplicationPatch.RegisterManual(_harmony)) _registrationStageFailed = true;
                if (!Adapters.Structure.Patches.StructureStateReplicationPatch.RegisterManual(_harmony)) _registrationStageFailed = true;
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"BuildingStateReplicationPatch.RegisterManual 失败: {ex}");
            }

            RoleLogger.Info("[Shared]", "[Diag] === 手动登记完成 ===");
            return !_registrationStageFailed;
        }

        /// <summary>
        /// 用于 AcceptConnection / SetConnectionPollGroup 的 Postfix（PatchAll 未自动登记 [HarmonyPostfix]）。
        /// 仅在本插件的精确 Postfix 已登记时 SKIP；外部 Postfix 不得阻止自身登记。
        /// </summary>
        private bool TryManualPatchPostfix(System.Type targetType, string methodName,
            System.Type patchClass, string patchMethodName)
        {
            string label = $"{targetType?.Name}.{methodName}#Postfix";
            try
            {
                if (targetType == null)
                {
                    _registrationStageFailed = true;
                    RoleLogger.Error("[Shared]", $"[ManualPatch] !!! {patchClass.Name}.{patchMethodName}: targetType=null");
                    return false;
                }

                System.Reflection.MethodInfo original = AccessTools.Method(targetType, methodName);
                if (original == null)
                {
                    _registrationStageFailed = true;
                    RoleLogger.Error("[Shared]",
                        $"[ManualPatch] !!! {label}: AccessTools.Method 返回 null");
                    return false;
                }

                System.Reflection.MethodInfo postfix = AccessTools.Method(patchClass, patchMethodName);
                if (postfix == null)
                {
                    _registrationStageFailed = true;
                    RoleLogger.Error("[Shared]",
                        $"[ManualPatch] !!! {label}: Postfix 方法未找到 {patchClass.FullName}.{patchMethodName}");
                    return false;
                }

                HarmonyLib.Patches existing = Harmony.GetPatchInfo(original);
                if (existing?.Postfixes != null)
                {
                    foreach (HarmonyLib.Patch patch in existing.Postfixes)
                    {
                        if (patch.owner == HARMONY_ID && patch.PatchMethod == postfix)
                        {
                            RoleLogger.Info("[Shared]",
                                $"[ManualPatch] SKIP {label} 本插件 Postfix 已精确登记");
                            return true;
                        }
                    }
                }

                _harmony.Patch(original, postfix: new HarmonyMethod(postfix));

                HarmonyLib.Patches info = Harmony.GetPatchInfo(original);
                bool ownPostfixInstalled = false;
                if (info?.Postfixes != null)
                {
                    foreach (HarmonyLib.Patch patch in info.Postfixes)
                    {
                        if (patch.owner == HARMONY_ID && patch.PatchMethod == postfix)
                        {
                            ownPostfixInstalled = true;
                            break;
                        }
                    }
                }
                if (!ownPostfixInstalled)
                {
                    _registrationStageFailed = true;
                    RoleLogger.Error("[Shared]",
                        $"[ManualPatch] !!! {label}: Harmony.Patch 调用后本插件 Postfix 仍未登记");
                    return false;
                }

                RoleLogger.Info("[Shared]",
                    $"[ManualPatch] OK {label} 本插件 Postfix 已手动登记");
                return true;
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"[ManualPatch] !!! {label} 异常: {ex}");
                return false;
            }
        }

        /// <summary>
        /// </summary>
        private bool TryManualPatch(System.Type targetType, string methodName,
            System.Type patchClass, string patchMethodName)
        {
            string label = $"{targetType?.Name}.{methodName}";
            try
            {
                if (targetType == null)
                {
                    _registrationStageFailed = true;
                    RoleLogger.Error("[Shared]", $"[ManualPatch] !!! {patchClass.Name}.{patchMethodName}: targetType=null");
                    return false;
                }

                System.Reflection.MethodInfo original = AccessTools.Method(targetType, methodName);
                if (original == null)
                {
                    _registrationStageFailed = true;
                    RoleLogger.Error("[Shared]",
                        $"[ManualPatch] !!! {label}: AccessTools.Method 返回 null");
                    return false;
                }

                System.Reflection.MethodInfo prefix = AccessTools.Method(patchClass, patchMethodName);
                if (prefix == null)
                {
                    _registrationStageFailed = true;
                    RoleLogger.Error("[Shared]",
                        $"[ManualPatch] !!! {label}: Prefix 方法未找到 {patchClass.FullName}.{patchMethodName}");
                    return false;
                }

                HarmonyLib.Patches existing = Harmony.GetPatchInfo(original);
                if (existing?.Prefixes != null)
                {
                    foreach (HarmonyLib.Patch patch in existing.Prefixes)
                    {
                        if (patch.owner == HARMONY_ID && patch.PatchMethod == prefix)
                        {
                            RoleLogger.Info("[Shared]",
                                $"[ManualPatch] SKIP {label} 本插件 Prefix 已精确登记");
                            return true;
                        }
                    }
                }

                _harmony.Patch(original, prefix: new HarmonyMethod(prefix));

                HarmonyLib.Patches info = Harmony.GetPatchInfo(original);
                bool ownPrefixInstalled = false;
                if (info?.Prefixes != null)
                {
                    foreach (HarmonyLib.Patch patch in info.Prefixes)
                    {
                        if (patch.owner == HARMONY_ID && patch.PatchMethod == prefix)
                        {
                            ownPrefixInstalled = true;
                            break;
                        }
                    }
                }
                if (!ownPrefixInstalled)
                {
                    _registrationStageFailed = true;
                    RoleLogger.Error("[Shared]",
                        $"[ManualPatch] !!! {label}: Harmony.Patch 调用后本插件 Prefix 仍未登记");
                    return false;
                }

                RoleLogger.Info("[Shared]",
                    $"[ManualPatch] OK {label} 本插件 Prefix 已手动登记");
                return true;
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"[ManualPatch] !!! {label} 异常: {ex}");
                return false;
            }
        }
    }
}
