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
using SteamP2PFriends.Patches;
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
        private void ApplyV2AuditFixPatches()
        {
            ApplyV2AuditFixPatches_Core();
        }

        /// <summary>
        ///
        /// 但 PatchAll 在处理泛型类 ClientStaticMethod&lt;...&gt;.Invoke 的 attribute 时静默失败，
        /// 导致两个 Prefix 都未注册到 vanilla 方法上（第九次-4 双机测试确认）。
        ///
        /// 修复方案：改为 manual registration，与 14 个 SteamGameServerNetworkingSockets wrapper patch 一致。
        ///
        ///   1. _harmony.GetPatchedMethods() + Harmony.GetPatchInfo() 双重验证
        ///   2. Server-side Prefix 签名验证（methodInfo.DeclaringType + GetParameters()）
        ///   3. 整体 try-catch 包裹，任一登记失败不阻断 Plugin.Awake 后续步骤
        /// </summary>
        private void RegisterAssetIntegritySnapshotPatches()
        {
            RoleLogger.Info("[Shared]", "[Diag] === v0.2.3.16 AssetIntegritySnapshot 手动登记（ServerInvokePrefix 参数名 arg1..arg6 匹配 vanilla）===");

            // ===== Client-side: Assets.ReceiveKickForHashMismatch =====
            try
            {
                System.Reflection.MethodInfo clientTarget = AccessTools.Method(
                    typeof(SDG.Unturned.Assets),
                    "ReceiveKickForHashMismatch",
                    new System.Type[] {
                        typeof(System.Guid), typeof(string), typeof(string),
                        typeof(byte[]), typeof(string), typeof(string)
                    });

                if (clientTarget == null)
                {
                    RoleLogger.Error("[Shared]",
                        "[ManualPatch] FAIL AssetIntegritySnapshot/Client: AccessTools.Method returned null");
                }
                else
                {
                    // 签名验证（审计员要求）：输出 DeclaringType + Parameters
                    RoleLogger.Info("[Shared]",
                        $"[ManualPatch] AssetIntegritySnapshot/Client target resolved: declaringType={clientTarget.DeclaringType?.FullName} " +
                        $"params=[{string.Join(", ", System.Array.ConvertAll(clientTarget.GetParameters(), p => p.ParameterType.Name + " " + p.Name))}]");

                    System.Reflection.MethodInfo clientPrefix = AccessTools.Method(
                        typeof(Patches.AssetIntegritySnapshotPatch),
                        "ClientReceiveKickPrefix");

                    if (clientPrefix == null)
                    {
                        RoleLogger.Error("[Shared]",
                            "[ManualPatch] FAIL AssetIntegritySnapshot/Client: prefix MethodInfo null");
                    }
                    else
                    {
                        _harmony.Patch(clientTarget, prefix: new HarmonyMethod(clientPrefix));

                        // 双重验证 1：Harmony.GetPatchInfo
                        HarmonyLib.Patches patchInfo = Harmony.GetPatchInfo(clientTarget);
                        int prefixCount = patchInfo?.Prefixes?.Count ?? 0;
                        RoleLogger.Info("[Shared]",
                            $"[ManualPatch] OK AssetIntegritySnapshot/Client 已手动登记 (prefixes={prefixCount})");

                        // 双重验证 2：_harmony.GetPatchedMethods() 包含检查
                        bool contains = false;
                        foreach (var pm in _harmony.GetPatchedMethods())
                        {
                            if (pm == clientTarget) { contains = true; break; }
                        }
                        if (contains)
                        {
                            RoleLogger.Info("[Shared]",
                                "[ManualPatch] 验证 OK: Assets.ReceiveKickForHashMismatch 已 patch（GetPatchedMethods 包含）");
                        }
                        else
                        {
                            RoleLogger.Error("[Shared]",
                                "[ManualPatch] 验证 FAIL: Assets.ReceiveKickForHashMismatch 未 patch（GetPatchedMethods 不包含）");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[ManualPatch] FAIL AssetIntegritySnapshot/Client: {ex}");
            }

            // ===== Server-side: ClientStaticMethod<Guid,string,string,byte[],string,string>.Invoke =====
            try
            {
                System.Type genericType = typeof(SDG.Unturned.ClientStaticMethod<
                    System.Guid, string, string, byte[], string, string>);

                System.Reflection.MethodInfo serverTarget = AccessTools.Method(
                    genericType,
                    "Invoke",
                    new System.Type[] {
                        typeof(SDG.NetTransport.ENetReliability),
                        typeof(SDG.NetTransport.ITransportConnection),
                        typeof(System.Guid), typeof(string), typeof(string),
                        typeof(byte[]), typeof(string), typeof(string)
                    });

                if (serverTarget == null)
                {
                    RoleLogger.Error("[Shared]",
                        "[ManualPatch] FAIL AssetIntegritySnapshot/Server: AccessTools.Method returned null");
                }
                else
                {
                    // 签名验证（审计员要求）：输出 DeclaringType + Parameters
                    RoleLogger.Info("[Shared]",
                        $"[ManualPatch] AssetIntegritySnapshot/Server target resolved: declaringType={serverTarget.DeclaringType?.FullName} " +
                        $"params=[{string.Join(", ", System.Array.ConvertAll(serverTarget.GetParameters(), p => p.ParameterType.Name + " " + p.Name))}]");

                    System.Reflection.MethodInfo serverPrefix = AccessTools.Method(
                        typeof(Patches.AssetIntegritySnapshotPatch),
                        "ServerInvokePrefix");

                    if (serverPrefix == null)
                    {
                        RoleLogger.Error("[Shared]",
                            "[ManualPatch] FAIL AssetIntegritySnapshot/Server: prefix MethodInfo null");
                    }
                    else
                    {
                        _harmony.Patch(serverTarget, prefix: new HarmonyMethod(serverPrefix));

                        // 双重验证 1：Harmony.GetPatchInfo
                        HarmonyLib.Patches patchInfo = Harmony.GetPatchInfo(serverTarget);
                        int prefixCount = patchInfo?.Prefixes?.Count ?? 0;
                        RoleLogger.Info("[Shared]",
                            $"[ManualPatch] OK AssetIntegritySnapshot/Server 已手动登记 (prefixes={prefixCount})");

                        // 双重验证 2：_harmony.GetPatchedMethods() 包含检查
                        bool contains = false;
                        foreach (var pm in _harmony.GetPatchedMethods())
                        {
                            if (pm == serverTarget) { contains = true; break; }
                        }
                        if (contains)
                        {
                            RoleLogger.Info("[Shared]",
                                "[ManualPatch] 验证 OK: ClientStaticMethod<...>.Invoke 已 patch（GetPatchedMethods 包含）");
                        }
                        else
                        {
                            RoleLogger.Error("[Shared]",
                                "[ManualPatch] 验证 FAIL: ClientStaticMethod<...>.Invoke 未 patch（GetPatchedMethods 不包含）");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[ManualPatch] FAIL AssetIntegritySnapshot/Server: {ex}");
            }
        }

        /// <summary>
        ///
        /// 登记以下修复 patch：
        ///
        /// 公共 Beta 始终登记完整的兼容性补丁集。
        /// </summary>
        private void ApplyV2AuditFixPatches_Core()
        {
            RoleLogger.Info("[Shared]", "[Diag] === v2 审计放行后修复 patch 登记 ===");

            // 仅状态日志
            try
            {
                Patches.PlayerMovementInitializePlayerPrefixPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"PlayerMovementInitializePlayerPrefixPatch.RegisterManual 失败: {ex}");
            }

            // 完整兼容性补丁集。
                try
                {
                    Patches.SteamPlayerIsLocalServerHostPatch.RegisterManual(_harmony);
                }
                catch (System.Exception ex)
                {
                    RoleLogger.Error("[Shared]", $"SteamPlayerIsLocalServerHostPatch.RegisterManual 失败: {ex}");
                }

                try
                {
                    Patches.PlayerUpdateGuardPatch.RegisterManual(_harmony);
                }
                catch (System.Exception ex)
                {
                    RoleLogger.Error("[Shared]", $"PlayerUpdateGuardPatch.RegisterManual 失败: {ex}");
                }

                try
                {
                    Patches.GameplayReadyBitmaskPatch.RegisterManual(_harmony);
                }
                catch (System.Exception ex)
                {
                    RoleLogger.Error("[Shared]", $"GameplayReadyBitmaskPatch.RegisterManual 失败: {ex}");
                }
            RoleLogger.Info("[Shared]", "[Diag] === v2 审计放行后修复 patch 登记完成 ===");
        }

        private void ApplyManualWrapperPatches()
        {
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
                Patches.ClientMethodLoopbackPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"ClientMethodLoopbackPatch.RegisterManual 失败: {ex}");
            }

            //   Transpiler 替换 onRegionUpdated step 2 中 Dedicator.IsDedicatedServer() 调用为
            //   ListenRegionSyncEligibility.IsDedicatedOrP2PRemoteRecipient(player)，
            //   仅对远程非 loopback 玩家开放 SendRegion 资格。
            try
            {
                Patches.BarricadeManagerRegionSyncPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"BarricadeManagerRegionSyncPatch.RegisterManual 失败: {ex}");
            }

            //   Transpiler 替换 onRegionUpdated step 1 中 Dedicator.IsDedicatedServer() 调用。
            try
            {
                Patches.StructureManagerRegionSyncPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"StructureManagerRegionSyncPatch.RegisterManual 失败: {ex}");
            }

            //   ItemManagerRegionSyncPatch Transpiler + askItems Prefix
            //   Transpiler 替换 onRegionUpdated step 5 中 Dedicator.IsDedicatedServer() 调用，
            //   仅对远程非 loopback 玩家开放 askItems 资格。
            try
            {
                Patches.ItemManagerRegionSyncPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"ItemManagerRegionSyncPatch.RegisterManual 失败: {ex}");
            }

            //   ResourceManagerRegionSyncPatch Transpiler + SendResources_Write Prefix
            //   Transpiler 替换 onRegionUpdated step 3 中 Dedicator.IsDedicatedServer() 调用。
            //   SendResources 为 ClientStaticMethod 字段无独立 ask 方法，Prefix 挂在 SendResources_Write。
            try
            {
                Patches.ResourceManagerRegionSyncPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"ResourceManagerRegionSyncPatch.RegisterManual 失败: {ex}");
            }

            //   ObjectManagerRegionSyncPatch Transpiler + askObjects Prefix
            //   Transpiler 替换 onRegionUpdated step 4 中 Dedicator.IsDedicatedServer() 调用。
            try
            {
                Patches.ObjectManagerRegionSyncPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"ObjectManagerRegionSyncPatch.RegisterManual 失败: {ex}");
            }

            //   listen host is a graphical client, so vanilla only keeps static object collision around the host.
            //   Add remote guests' regional collision coverage while leaving renderer visibility host-local.
            try
            {
                Patches.LevelObjectRemoteCollisionPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"LevelObjectRemoteCollisionPatch.RegisterManual 失败: {ex}");
            }

            //   Add remote guests' resource (trees & ores) regional collision coverage.
            try
            {
                Adapters.Resource.Patches.LevelGroundRemoteTreeCollisionPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"LevelGroundRemoteTreeCollisionPatch.RegisterManual 失败: {ex}");
            }

            //   Add resource harvest & death/alive replication hooks.
            try
            {
                Adapters.Resource.Patches.ResourceManagerHarvestReplicationPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"ResourceManagerHarvestReplicationPatch.RegisterManual 失败: {ex}");
            }

            //   Add player barricade & structure state replication hooks (M7).
            try
            {
                Adapters.Structure.Patches.BarricadeStateReplicationPatch.RegisterManual(_harmony);
                Adapters.Structure.Patches.StructureStateReplicationPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"BuildingStateReplicationPatch.RegisterManual 失败: {ex}");
            }

            RoleLogger.Info("[Shared]", "[Diag] === 手动登记完成 ===");
        }

        /// <summary>
        /// 用于 AcceptConnection / SetConnectionPollGroup 的 Postfix（PatchAll 未自动登记 [HarmonyPostfix]）。
        /// 仅在本插件的精确 Postfix 已登记时 SKIP；外部 Postfix 不得阻止自身登记。
        /// </summary>
        private void TryManualPatchPostfix(System.Type targetType, string methodName,
            System.Type patchClass, string patchMethodName)
        {
            string label = $"{targetType?.Name}.{methodName}#Postfix";
            try
            {
                if (targetType == null)
                {
                    RoleLogger.Error("[Shared]", $"[ManualPatch] !!! {patchClass.Name}.{patchMethodName}: targetType=null");
                    return;
                }

                System.Reflection.MethodInfo original = AccessTools.Method(targetType, methodName);
                if (original == null)
                {
                    RoleLogger.Error("[Shared]",
                        $"[ManualPatch] !!! {label}: AccessTools.Method 返回 null");
                    return;
                }

                System.Reflection.MethodInfo postfix = AccessTools.Method(patchClass, patchMethodName);
                if (postfix == null)
                {
                    RoleLogger.Error("[Shared]",
                        $"[ManualPatch] !!! {label}: Postfix 方法未找到 {patchClass.FullName}.{patchMethodName}");
                    return;
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
                            return;
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
                    RoleLogger.Error("[Shared]",
                        $"[ManualPatch] !!! {label}: Harmony.Patch 调用后本插件 Postfix 仍未登记");
                    return;
                }

                RoleLogger.Info("[Shared]",
                    $"[ManualPatch] OK {label} 本插件 Postfix 已手动登记");
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[ManualPatch] !!! {label} 异常: {ex}");
            }
        }

        /// <summary>
        /// </summary>
        private void ApplyManualDiagnosticPatches()
        {
            RoleLogger.Info("[Shared]", "[Diag] === 手动登记 internal 类型诊断 patch ===");
            try
            {
                Patches.NetMessagesSendDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"NetMessagesSendDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Patches.AuthHandshakeJournalPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[P2P-Connection] auth handshake journal registration threw: {ex.GetType().Name}");
            }
            try
            {
                Patches.ClientAcceptedHandlerDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"ClientAcceptedHandlerDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            try
            {
                Patches.ProviderAcceptStageDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"ProviderAcceptStageDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            try
            {
                Patches.PlayerComponentInitializeDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"PlayerComponentInitializeDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            try
            {
                Patches.ProviderRejectDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"ProviderRejectDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            try
            {
                Patches.QueuePositionChangedDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"QueuePositionChangedDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            bool p0cOk = false;
            try
            {
                p0cOk = Patches.InitialStateReceiveDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"InitialStateReceiveDiagnosticPatch.RegisterManual 失败: {ex}");
                p0cOk = false;
            }
            RoleLogger.Info("[Shared]",
                $"[P0-C] AllRegistrationsSucceeded={Patches.InitialStateReceiveDiagnosticPatch.AllRegistrationsSucceeded} " +
                $"summary={Patches.InitialStateReceiveDiagnosticPatch.RegistrationSummary} " +
                $"registerReturned={p0cOk}");

            try
            {
                Patches.LocalRegionProgressDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"LocalRegionProgressDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            try
            {
                Patches.DisconnectTracerPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"DisconnectTracerPatch.RegisterManual 失败: {ex}");
            }

            //   - D-Vis-2: PlayerEquipment.ReceiveSlot/ReceiveUpdateState/ReceiveEquip
            //   - D-Vis-3: PlayerInput.ReceiveSimulateMispredictedInputs
            //   - D-Vis-4: PlayerAnimator.ReceiveLean/ReceiveGesture
            //   - D-Vis-5: SteamChannel.send（含节流 1 条/秒/调用方 + hex 摘要）
            //   - D-Vis-7: PlayerClothing.sendUpdateShirtQuality
            //   - D-Vis-8: SteamChannel.GetOwnerTransportConnection（含节流 10 秒/steamId）
            //   - D-Vis-6: 扩展 RemotePlayerRenderProbe 采样（不需要 patch 登记）
            try
            {
                Patches.PlayerClothingVisibilityDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"PlayerClothingVisibilityDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Patches.PlayerLookAnimatorDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"PlayerLookAnimatorDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Patches.SteamChannelTransportDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"SteamChannelTransportDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            //   - D-Vis-9: PlayerMovement.tellState（位置同步追踪）
            //   - D-Vis-10: PlayerAnimator InitializePlayer+NotifyClothingIsVisible+onLifeUpdated+PlayerClothing.ReceiveClothingState 4 patch 目标
            //   - D-Vis-11: LoadingUI.Update（5 个 isLoading 标志位追踪）
            //   - D-Vis-12: Player.InitializePlayer+PlayerClothing.ReceiveClothingState+Player.OnDestroy 3 patch 点
            //   - D-Vis-13: Player.InitializePlayer 阶段标记（耗时追踪）
            //   - D-Vis-14: Unity Tag 错误源头追踪
            try
            {
                Patches.PlayerMovementTellStateDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"PlayerMovementTellStateDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Patches.PlayerAnimatorSmrEnabledDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"PlayerAnimatorSmrEnabledDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Patches.LoadingUIUpdateDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"LoadingUIUpdateDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Patches.PlayerIsLoadingClothingDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"PlayerIsLoadingClothingDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Patches.PlayerInitializePlayerStageDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"PlayerInitializePlayerStageDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Patches.UnityTagErrorSourceDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"UnityTagErrorSourceDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            //   - D-Vis-15: PlayerMovement.tellState 调用方追踪（Postfix + StackTrace，验证议题 A/B 独立性）
            //   - D-Vis-16（调整后）: NetMessage 投递路径追踪（区分主机自身 vs 客机）
            //   - D-Vis-17: Player 生命周期就绪追踪（onPlayerCreated + bitmask + isLoadingClothing）
            //   - D-Vis-18: InitializePlayer 分阶段计时（clothing/movement/animator/quests 4 阶段）
            try
            {
                Patches.PlayerMovementTellStateCallerDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"PlayerMovementTellStateCallerDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Patches.NetMessageDeliveryPathDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"NetMessageDeliveryPathDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Patches.PlayerLifecycleReadyDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"PlayerLifecycleReadyDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Patches.PlayerInitializePlayerStagedTimingDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"PlayerInitializePlayerStagedTimingDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            try
            {
                Patches.PlayerManagerBroadcastPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"PlayerManagerBroadcastPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Patches.RemotePlayerClothingVisibleBridgePatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"RemotePlayerClothingVisibleBridgePatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Patches.PlayerManagerBroadcastDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"PlayerManagerBroadcastDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            //   6 个 WorldSyncDiagnostic patch（Item/Resource/Object/Vehicle/Animal/Zombie Manager）
            //   因类级 [HarmonyPatch] 缺失，PatchAll 未登记（运行日志证据：6 组 VerifyRegistration 全 FAIL）。
            //   RegisterManual 返回值不绕过 VerifyRegistration，最终权威仍是 6 个 VerifyRegistration 聚合至 DiagnosticBuildValid。
            RegisterWorldSyncDiagnosticPatches();

            RoleLogger.Info("[Shared]", "[Diag] === internal 诊断 patch 登记完成 ===");
        }

        /// <summary>
        /// 最终由 VerifyCriticalPatches 聚合 6 个 VerifyRegistration 结果至 DiagnosticBuildValid 阻断门。
        /// </summary>
        private void RegisterWorldSyncDiagnosticPatches()
        {
            RoleLogger.Info("[Shared]", "[Diag] === v0.2.3.37-P0-B-6-P0-D-ESC-2 手动登记 6 个 WorldSyncDiagnostic patch + 3 个 P0-C-1/C-2 patch + 1 个 P0-B-3 patch（含 P0-B-4 诊断 Prefix/Postfix）+ 1 个 P0-PlayerVisibility patch + 1 个 P0-D-ESC-2 patch + 1 个 P0-C-1-V-a 诊断 patch（identity-based 幂等）===");

            try
            {
                Patches.ItemManagerWorldSyncDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"ItemManagerWorldSyncDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            try
            {
                Patches.AuthoritativeItemGenerationGatePatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"AuthoritativeItemGenerationGatePatch.RegisterManual failed: {ex}");
            }

            // Alpha inventory/world authority probe: read-only fingerprints and transaction tracing.
            try
            {
                Patches.InventoryWorldAuthorityProbe.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"InventoryWorldAuthorityProbe.RegisterManual failed: {ex}");
            }

            try
            {
                Patches.ResourceManagerWorldSyncDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"ResourceManagerWorldSyncDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            try
            {
                Patches.ObjectManagerWorldSyncDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"ObjectManagerWorldSyncDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            try
            {
                Patches.Issue7ObjectBinaryStateDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"Issue7ObjectBinaryStateDiagnosticPatch.RegisterManual failed: {ex}");
            }

            try
            {
                Patches.VehicleManagerWorldSyncDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"VehicleManagerWorldSyncDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            try
            {
                Patches.AnimalManagerWorldSyncDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"AnimalManagerWorldSyncDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            try
            {
                Patches.ZombieManagerWorldSyncDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"ZombieManagerWorldSyncDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            //   ZombieManagerP0DGenerateZombiesPatch：onBoundUpdated Prefix supplement
            //   必须在 ZombieManagerWorldSyncDiagnosticPatch 之后登记，确保 Priority.Low 生效。
            //   VerifyRegistration 聚合至 DiagnosticBuildValid 阻断门。
            try
            {
                Patches.ZombieManagerP0DGenerateZombiesPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"ZombieManagerP0DGenerateZombiesPatch.RegisterManual 失败: {ex}");
            }

            //   ZombieLifecyclePatch：onBoundUpdated Prefix(VeryLow) + Postfix(High) + Finalizer(High)
            //   编码约束：不新增 Tick / Transpiler / 主动 generate/destroy；Finalizer 始终原样返回 __exception。
            //   VerifyRegistration 聚合至 DiagnosticBuildValid 阻断门。
            try
            {
                Patches.P0EZombieLifecycle.ZombieLifecyclePatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"ZombieLifecyclePatch.RegisterManual 失败: {ex}");
            }

            //   ZombieManagerP0C1SendZombieStatesPatch：updateRegionsAndSendZombieStates Transpiler
            //   替换 L1662 IsDedicatedServer -> IsDedicatedOrP2PHost。
            //   VerifyRegistration 聚合至 DiagnosticBuildValid 阻断门。
            try
            {
                Patches.ZombieManagerP0C1SendZombieStatesPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"ZombieManagerP0C1SendZombieStatesPatch.RegisterManual 失败: {ex}");
            }

            //   VehicleManagerP0C1ReplicationPatch：Update Transpiler（L2918）+ OnUpdate Postfix（位移检测+MarkForReplicationUpdate）
            //   VerifyRegistration 聚合至 DiagnosticBuildValid 阻断门。
            try
            {
                Patches.VehicleManagerP0C1ReplicationPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"VehicleManagerP0C1ReplicationPatch.RegisterManual 失败: {ex}");
            }

            //   AnimalManagerP0C2SendAnimalStatesPatch：Update Transpiler（L1057）
            //   替换 IsDedicatedServer -> IsDedicatedOrP2PHost。
            //   VerifyRegistration 聚合至 DiagnosticBuildValid 阻断门。
            try
            {
                Patches.AnimalManagerP0C2SendAnimalStatesPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"AnimalManagerP0C2SendAnimalStatesPatch.RegisterManual 失败: {ex}");
            }

            //   NetMessagesPlayerConnectedLoopbackPatch：SendMessageToClient Prefix
            //   仅当 index==PlayerConnected && transportConnection is TransportConnection_Loopback 时跳过。
            //   VerifyRegistration 聚合至 DiagnosticBuildValid 阻断门。
            try
            {
                Patches.NetMessagesPlayerConnectedLoopbackPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"NetMessagesPlayerConnectedLoopbackPatch.RegisterManual 失败: {ex}");
            }

            //   PlayerUIPauseTimeScalePatch：PlayerUI.updatePauseTimeScale Prefix
            //   当 listen host + 有远端客机时强制保持 timeScale=1 + AudioListener.pause=false。
            //   修复 24th 测试中 timeScale=0.00 持续 33.32s 导致客机世界停滞的问题。
            //   VerifyRegistration 聚合至 DiagnosticBuildValid 阻断门。
            try
            {
                Patches.PlayerUIPauseTimeScalePatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"PlayerUIPauseTimeScalePatch.RegisterManual 失败: {ex}");
            }

            //   VehicleEnterDiagnosticPatch：VehicleManager.enterVehicle + ReceiveEnterVehicleRequest Prefix
            //   仅诊断日志，不修改 vanilla 验证逻辑。确定登车失败具体环节后再决定是否实施驾驶同步。
            //   VerifyRegistration 聚合至 DiagnosticBuildValid 阻断门。
            try
            {
                Patches.VehicleEnterDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"VehicleEnterDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            //   - UseableBarricadeDiagnosticPatch：8 DP（startPrimary/check/checkSpace/checkClaims/ReceiveBarricadeNone/simulate/build/dropBarricade）
            //   - ZombieEntityMappingDiagnosticPatch：7 DP（SendZombies/ReceiveZombies/SendZombieStates/ReceiveZombieStates/onBoundUpdated/sendZombieDead+Alive/ReceiveZombieDead+Alive）
            //   - PlayerManagerCullingDiagnosticPatch：3 DP（SendPlayerStates_Write Prefix/ReceivePlayerStates Postfix/tellState Prefix）
            //   阶段 2 完成后等待阶段 2 重审，不自动进入阶段 3（双机诊断测试）或阶段 5（功能修复）。
            try
            {
                Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"UseableBarricadeDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Patches.P0EDiagnostic.ZombieEntityMappingDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"ZombieEntityMappingDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Patches.P0EDiagnostic.PlayerManagerCullingDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"PlayerManagerCullingDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            RoleLogger.Info("[Shared]", "[Diag] === v0.2.3.37-P0-B-6-P0-D-ESC-2 WorldSyncDiagnostic patch 登记完成 ===");
        }

        private void TryManualPatch(System.Type targetType, string methodName,
            System.Type patchClass, string patchMethodName)
        {
            string label = $"{targetType?.Name}.{methodName}";
            try
            {
                if (targetType == null)
                {
                    RoleLogger.Error("[Shared]", $"[ManualPatch] !!! {patchClass.Name}.{patchMethodName}: targetType=null");
                    return;
                }

                System.Reflection.MethodInfo original = AccessTools.Method(targetType, methodName);
                if (original == null)
                {
                    RoleLogger.Error("[Shared]",
                        $"[ManualPatch] !!! {label}: AccessTools.Method 返回 null");
                    return;
                }

                System.Reflection.MethodInfo prefix = AccessTools.Method(patchClass, patchMethodName);
                if (prefix == null)
                {
                    RoleLogger.Error("[Shared]",
                        $"[ManualPatch] !!! {label}: Prefix 方法未找到 {patchClass.FullName}.{patchMethodName}");
                    return;
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
                            return;
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
                    RoleLogger.Error("[Shared]",
                        $"[ManualPatch] !!! {label}: Harmony.Patch 调用后本插件 Prefix 仍未登记");
                    return;
                }

                RoleLogger.Info("[Shared]",
                    $"[ManualPatch] OK {label} 本插件 Prefix 已手动登记");
            }
            catch (System.Exception ex)
            {
                RoleLogger.Error("[Shared]", $"[ManualPatch] !!! {label} 异常: {ex}");
            }
        }
    }
}
