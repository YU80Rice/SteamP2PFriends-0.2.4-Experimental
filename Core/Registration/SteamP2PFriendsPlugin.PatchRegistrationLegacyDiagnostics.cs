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



namespace SteamP2PFriends
{
    public partial class SteamP2PFriendsPlugin
    {
        private bool ApplyManualDiagnosticPatches()
        {
            _registrationStageFailed = false;
            RoleLogger.Info("[Shared]", "[Diag] === 手动登记 internal 类型诊断 patch ===");
            try
            {
                Core.Patches.NetMessagesSendDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"NetMessagesSendDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Core.Patches.AuthHandshakeJournalPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"[P2P-Connection] auth handshake journal registration threw: {ex.GetType().Name}");
            }
            try
            {
                Core.Patches.ClientAcceptedHandlerDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"ClientAcceptedHandlerDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            try
            {
                Core.Patches.ProviderAcceptStageDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"ProviderAcceptStageDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            try
            {
                Core.Patches.PlayerComponentInitializeDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"PlayerComponentInitializeDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            try
            {
                Core.Patches.ProviderRejectDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"ProviderRejectDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            try
            {
                Core.Patches.QueuePositionChangedDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"QueuePositionChangedDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            bool p0cOk = false;
            try
            {
                p0cOk = Core.Patches.InitialStateReceiveDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"InitialStateReceiveDiagnosticPatch.RegisterManual 失败: {ex}");
                p0cOk = false;
            }
            RoleLogger.Info("[Shared]",
                $"[P0-C] AllRegistrationsSucceeded={Core.Patches.InitialStateReceiveDiagnosticPatch.AllRegistrationsSucceeded} " +
                $"summary={Core.Patches.InitialStateReceiveDiagnosticPatch.RegistrationSummary} " +
                $"registerReturned={p0cOk}");

            try
            {
                Core.Patches.LocalRegionProgressDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"LocalRegionProgressDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            try
            {
                Core.Patches.DisconnectTracerPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
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
                Core.Patches.PlayerClothingVisibilityDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"PlayerClothingVisibilityDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Core.Patches.PlayerLookAnimatorDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"PlayerLookAnimatorDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Core.Patches.SteamChannelTransportDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
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
                Core.Patches.PlayerMovementTellStateDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"PlayerMovementTellStateDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Core.Patches.PlayerAnimatorSmrEnabledDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"PlayerAnimatorSmrEnabledDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Core.Patches.LoadingUIUpdateDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"LoadingUIUpdateDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Core.Patches.PlayerIsLoadingClothingDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"PlayerIsLoadingClothingDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Core.Patches.PlayerInitializePlayerStageDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"PlayerInitializePlayerStageDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Core.Patches.UnityTagErrorSourceDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"UnityTagErrorSourceDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            //   - D-Vis-15: PlayerMovement.tellState 调用方追踪（Postfix + StackTrace，验证议题 A/B 独立性）
            //   - D-Vis-16（调整后）: NetMessage 投递路径追踪（区分主机自身 vs 客机）
            //   - D-Vis-17: Player 生命周期就绪追踪（onPlayerCreated + bitmask + isLoadingClothing）
            //   - D-Vis-18: InitializePlayer 分阶段计时（clothing/movement/animator/quests 4 阶段）
            try
            {
                Core.Patches.PlayerMovementTellStateCallerDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"PlayerMovementTellStateCallerDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Core.Patches.NetMessageDeliveryPathDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"NetMessageDeliveryPathDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Core.Patches.PlayerLifecycleReadyDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"PlayerLifecycleReadyDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Core.Patches.PlayerInitializePlayerStagedTimingDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"PlayerInitializePlayerStagedTimingDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            try
            {
                Core.Patches.PlayerManagerBroadcastPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"PlayerManagerBroadcastPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Core.Patches.RemotePlayerClothingVisibleBridgePatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"RemotePlayerClothingVisibleBridgePatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Core.Patches.PlayerManagerBroadcastDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"PlayerManagerBroadcastDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            //   6 个 WorldSyncDiagnostic patch（Item/Resource/Object/Vehicle/Animal/Zombie Manager）
            //   因类级 [HarmonyPatch] 缺失，PatchAll 未登记（运行日志证据：6 组 VerifyRegistration 全 FAIL）。
            //   RegisterManual 返回值不绕过 VerifyRegistration，最终权威仍是 6 个 VerifyRegistration 聚合至 DiagnosticBuildValid。
            RegisterWorldSyncDiagnosticPatches();

            RoleLogger.Info("[Shared]", "[Diag] === internal 诊断 patch 登记完成 ===");
            return !_registrationStageFailed;
        }

        /// <summary>
        /// 最终由 VerifyCriticalPatches 聚合 6 个 VerifyRegistration 结果至 DiagnosticBuildValid 阻断门。
        /// </summary>
        private void RegisterWorldSyncDiagnosticPatches()
        {
            RoleLogger.Info("[Shared]", "[Diag] === v0.2.3.37-P0-B-6-P0-D-ESC-2 手动登记 6 个 WorldSyncDiagnostic patch + 3 个 P0-C-1/C-2 patch + 1 个 P0-B-3 patch（含 P0-B-4 诊断 Prefix/Postfix）+ 1 个 P0-PlayerVisibility patch + 1 个 P0-D-ESC-2 patch + 1 个 P0-C-1-V-a 诊断 patch（identity-based 幂等）===");

            try
            {
                Core.Patches.ItemManagerWorldSyncDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"ItemManagerWorldSyncDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            try
            {
                Core.Patches.AuthoritativeItemGenerationGatePatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"AuthoritativeItemGenerationGatePatch.RegisterManual failed: {ex}");
            }

            // Alpha inventory/world authority probe: read-only fingerprints and transaction tracing.
            try
            {
                Core.Patches.InventoryWorldAuthorityProbe.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"InventoryWorldAuthorityProbe.RegisterManual failed: {ex}");
            }

            try
            {
                Core.Patches.ResourceManagerWorldSyncDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"ResourceManagerWorldSyncDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            try
            {
                Core.Patches.ObjectManagerWorldSyncDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"ObjectManagerWorldSyncDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            try
            {
                Core.Patches.Issue7ObjectBinaryStateDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"Issue7ObjectBinaryStateDiagnosticPatch.RegisterManual failed: {ex}");
            }

            try
            {
                Core.Patches.VehicleManagerWorldSyncDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"VehicleManagerWorldSyncDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            try
            {
                Core.Patches.AnimalManagerWorldSyncDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"AnimalManagerWorldSyncDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            try
            {
                Core.Patches.ZombieManagerWorldSyncDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"ZombieManagerWorldSyncDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            //   ZombieManagerP0DGenerateZombiesPatch：onBoundUpdated Prefix supplement
            //   必须在 ZombieManagerWorldSyncDiagnosticPatch 之后登记，确保 Priority.Low 生效。
            //   VerifyRegistration 聚合至 DiagnosticBuildValid 阻断门。
            try
            {
                Core.Patches.ZombieManagerP0DGenerateZombiesPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"ZombieManagerP0DGenerateZombiesPatch.RegisterManual 失败: {ex}");
            }

            //   ZombieLifecyclePatch：onBoundUpdated Prefix(VeryLow) + Postfix(High) + Finalizer(High)
            //   编码约束：不新增 Tick / Transpiler / 主动 generate/destroy；Finalizer 始终原样返回 __exception。
            //   VerifyRegistration 聚合至 DiagnosticBuildValid 阻断门。
            try
            {
                Core.Patches.P0EZombieLifecycle.ZombieLifecyclePatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"ZombieLifecyclePatch.RegisterManual 失败: {ex}");
            }

            //   ZombieManagerP0C1SendZombieStatesPatch：updateRegionsAndSendZombieStates Transpiler
            //   替换 L1662 IsDedicatedServer -> IsDedicatedOrP2PHost。
            //   VerifyRegistration 聚合至 DiagnosticBuildValid 阻断门。
            try
            {
                Core.Patches.ZombieManagerP0C1SendZombieStatesPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"ZombieManagerP0C1SendZombieStatesPatch.RegisterManual 失败: {ex}");
            }

            //   VehicleManagerP0C1ReplicationPatch：Update Transpiler（L2918）+ OnUpdate Postfix（位移检测+MarkForReplicationUpdate）
            //   VerifyRegistration 聚合至 DiagnosticBuildValid 阻断门。
            try
            {
                Core.Patches.VehicleManagerP0C1ReplicationPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"VehicleManagerP0C1ReplicationPatch.RegisterManual 失败: {ex}");
            }

            //   AnimalManagerP0C2SendAnimalStatesPatch：Update Transpiler（L1057）
            //   替换 IsDedicatedServer -> IsDedicatedOrP2PHost。
            //   VerifyRegistration 聚合至 DiagnosticBuildValid 阻断门。
            try
            {
                Core.Patches.AnimalManagerP0C2SendAnimalStatesPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"AnimalManagerP0C2SendAnimalStatesPatch.RegisterManual 失败: {ex}");
            }

            //   NetMessagesPlayerConnectedLoopbackPatch：SendMessageToClient Prefix
            //   仅当 index==PlayerConnected && transportConnection is TransportConnection_Loopback 时跳过。
            //   VerifyRegistration 聚合至 DiagnosticBuildValid 阻断门。
            try
            {
                Core.Patches.NetMessagesPlayerConnectedLoopbackPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"NetMessagesPlayerConnectedLoopbackPatch.RegisterManual 失败: {ex}");
            }

            //   PlayerUIPauseTimeScalePatch：PlayerUI.updatePauseTimeScale Prefix
            //   当 listen host + 有远端客机时强制保持 timeScale=1 + AudioListener.pause=false。
            //   修复 24th 测试中 timeScale=0.00 持续 33.32s 导致客机世界停滞的问题。
            //   VerifyRegistration 聚合至 DiagnosticBuildValid 阻断门。
            try
            {
                Core.Patches.PlayerUIPauseTimeScalePatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"PlayerUIPauseTimeScalePatch.RegisterManual 失败: {ex}");
            }

            //   VehicleEnterDiagnosticPatch：VehicleManager.enterVehicle + ReceiveEnterVehicleRequest Prefix
            //   仅诊断日志，不修改 vanilla 验证逻辑。确定登车失败具体环节后再决定是否实施驾驶同步。
            //   VerifyRegistration 聚合至 DiagnosticBuildValid 阻断门。
            try
            {
                Core.Patches.VehicleEnterDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"VehicleEnterDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            //   - UseableBarricadeDiagnosticPatch：8 DP（startPrimary/check/checkSpace/checkClaims/ReceiveBarricadeNone/simulate/build/dropBarricade）
            //   - ZombieEntityMappingDiagnosticPatch：7 DP（SendZombies/ReceiveZombies/SendZombieStates/ReceiveZombieStates/onBoundUpdated/sendZombieDead+Alive/ReceiveZombieDead+Alive）
            //   - PlayerManagerCullingDiagnosticPatch：3 DP（SendPlayerStates_Write Prefix/ReceivePlayerStates Postfix/tellState Prefix）
            //   阶段 2 完成后等待阶段 2 重审，不自动进入阶段 3（双机诊断测试）或阶段 5（功能修复）。
            try
            {
                Core.Patches.P0EDiagnostic.UseableBarricadeDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"UseableBarricadeDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Core.Patches.P0EDiagnostic.ZombieEntityMappingDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"ZombieEntityMappingDiagnosticPatch.RegisterManual 失败: {ex}");
            }
            try
            {
                Core.Patches.P0EDiagnostic.PlayerManagerCullingDiagnosticPatch.RegisterManual(_harmony);
            }
            catch (System.Exception ex)
            {
                _registrationStageFailed = true;
                RoleLogger.Error("[Shared]", $"PlayerManagerCullingDiagnosticPatch.RegisterManual 失败: {ex}");
            }

            RoleLogger.Info("[Shared]", "[Diag] === v0.2.3.37-P0-B-6-P0-D-ESC-2 WorldSyncDiagnostic patch 登记完成 ===");
        }

    }
}
