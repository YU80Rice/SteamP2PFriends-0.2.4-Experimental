using SteamP2PFriends.Adapters.Animal;
using SteamP2PFriends.Adapters.Collision;
using SteamP2PFriends.Adapters.Item;
using SteamP2PFriends.Adapters.Resource;
using SteamP2PFriends.Adapters.Structure;
using SteamP2PFriends.Adapters.Zombie;
using SteamP2PFriends.Patches;
using SteamP2PFriends.Shared;
using System;
using System.Collections.Generic;

namespace SteamP2PFriends
{
    public partial class SteamP2PFriendsPlugin
    {
        private Core.Registration.RegistrationClosure _registrationClosure;

        private Core.Registration.PatchRegistrationPlan CreatePatchRegistrationPlan()
        {
            _registrationClosure = new Core.Registration.RegistrationClosure(new[]
            {
                new Core.Registration.RegistrationRequirement(
                    Core.Registration.RegistrationDomainIds.Item, true, true),
                new Core.Registration.RegistrationRequirement(
                    Core.Registration.RegistrationDomainIds.Resource, true, true),
                new Core.Registration.RegistrationRequirement(
                    Core.Registration.RegistrationDomainIds.Building, true, true),
                new Core.Registration.RegistrationRequirement(
                    Core.Registration.RegistrationDomainIds.Zombie, true, true),
                new Core.Registration.RegistrationRequirement(
                    Core.Registration.RegistrationDomainIds.Animal, true, true),
                new Core.Registration.RegistrationRequirement(
                    Core.Registration.RegistrationDomainIds.Collision, true, false)
            });

            var stages = new List<Core.Registration.PatchRegistrationStage>
            {
                new Core.Registration.PatchRegistrationStage(
                    1, "Transport", "U3-REG-01-Wrapper", HARMONY_ID, "default",
                    "SteamNetworkingSockets/Callback wrappers", ApplyManualWrapperPatches),
                new Core.Registration.PatchRegistrationStage(
                    2, "Diagnostics", "U3-REG-02-InternalDiagnostics", HARMONY_ID, "default",
                    "internal NetMessages and lifecycle handlers", ApplyManualDiagnosticPatches),
                new Core.Registration.PatchRegistrationStage(
                    3, "Security", "U3-REG-03-RouteB", HARMONY_ID, "declared by patch",
                    "Route B admission and command permission", RegisterRouteBPatches),
                new Core.Registration.PatchRegistrationStage(
                    4, "Diagnostics", "U3-REG-04-AssetAndAudit", HARMONY_ID, "declared by patch",
                    "asset integrity and audit fixes", RegisterAssetAndAuditPatches),
                new Core.Registration.PatchRegistrationStage(
                    5, "Domain", "U3-REG-05-WorldSyncAndAdapters", HARMONY_ID, "declared by patch",
                    "world-sync reset, barricade lifecycle and adapter catalog", RegisterDomainStage),
                new Core.Registration.PatchRegistrationStage(
                    6, "Diagnostics", "U3-REG-06-Probes", HARMONY_ID, "n/a",
                    "Unity log bridge, SNS probe and redaction self-test", InitializeDiagnosticStage),
                new Core.Registration.PatchRegistrationStage(
                    7, "Verification", "U3-REG-07-ClosureAndVerification", HARMONY_ID, "n/a",
                    "post-registration verification and closure", VerifyRegistrationStage)
            };

            return new Core.Registration.PatchRegistrationPlan(
                _registrationClosure, stages, HARMONY_ID, VerifyRegistrationClosure);
        }

        private void RegisterRouteBPatches()
        {
            try
            {
                P2PQuarantineActionGatePatch.RegisterManual(_harmony);
                Patch_PlayerDashboardPlayersUI.RegisterManual(_harmony);
                P2PListenHostCommandPermissionPatch.RegisterManual(_harmony);
                RoleLogger.Info("[Shared]",
                    "[P2P-Approval] Route B patches registered; lifecycle hooks waiting for game thread");
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", "[Stage7-6] manual registration failed: " + ex);
            }
        }

        private void RegisterAssetAndAuditPatches()
        {
            try
            {
                Patches.AssetIntegritySnapshotPatch.RuntimeProbe();
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", $"AssetIntegritySnapshotPatch.RuntimeProbe 失败: {ex}");
            }

            try
            {
                RegisterAssetIntegritySnapshotPatches();
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", $"RegisterAssetIntegritySnapshotPatches 整体异常（不阻断）: {ex}");
            }

            ApplyV2AuditFixPatches();
        }

        private void RegisterDomainStage()
        {
            try
            {
                Patches.WorldSyncDiagnosticCore.RegisterSessionResetCallback(
                    Patches.P0EBarricadeLifecycle.BarricadeLifecycleHelper.ResetHitLogs);
                Patches.P0EBarricadeLifecycle.BarricadeLifecycleRegistration.MarkResetCallbackRegistered();
                RoleLogger.Info("[Shared]",
                    "[5B-1B/Plugin] OK RegisterSessionResetCallback(BarricadeLifecycleHelper.ResetHitLogs) 已登记 + MarkResetCallbackRegistered");
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]",
                    $"[5B-1B/Plugin] RegisterSessionResetCallback 失败: {ex.Message}");
            }

            try
            {
                Patches.P0EBarricadeLifecycle.BarricadeLifecycleRegistration.RegisterAtomically(_harmony);
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]",
                    $"[5B-1B/Plugin] BarricadeLifecycleRegistration.RegisterAtomically 异常: {ex.Message}");
            }

            RegisterDomainAdapters();
        }

        private void RegisterDomainAdapters()
        {
            var item = new ItemDomainAdapter();
            RegisterLifecycle(item);
            RegisterReplication(item);
            var resource = new ResourceDomainAdapter();
            RegisterLifecycle(resource);
            RegisterReplication(resource);
            var building = new BuildingDomainAdapter();
            RegisterLifecycle(building);
            RegisterReplication(building);
            var zombie = new ZombieDomainAdapter();
            RegisterLifecycle(zombie);
            RegisterReplication(zombie);
            var animal = new AnimalDomainAdapter();
            RegisterLifecycle(animal);
            RegisterReplication(animal);
            RegisterLifecycle(new LevelObjectCollisionAdapter());
        }

        private void RegisterLifecycle(MultiObserver.SPI.ILifecycleDomainAdapter adapter)
        {
            string failure;
            if (!_registrationClosure.TryRegisterLifecycle(adapter, out failure))
                RoleLogger.Error("[Shared]", "[Registration] lifecycle adapter rejected: " + failure);
        }

        private void RegisterReplication(MultiObserver.SPI.IStateReplicationAdapter adapter)
        {
            string failure;
            if (!_registrationClosure.TryRegisterReplication(adapter, out failure))
                RoleLogger.Error("[Shared]", "[Registration] replication adapter rejected: " + failure);
        }

        private void InitializeDiagnosticStage()
        {
            try
            {
                Patches.UnityLogBridgePatch.Initialize();
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", $"UnityLogBridgePatch.Initialize 失败: {ex}");
            }

            try
            {
                SteamP2PFriends.Client.NativeSnsLogProbe.Enable(RouteDiagnostics.Value, VerboseLog.Value);
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", $"NativeSnsLogProbe.Enable 失败: {ex}");
            }
        }

        private void VerifyRegistrationStage()
        {
            bool redactionSelfTestPassed = false;
            try
            {
                redactionSelfTestPassed = SteamP2PFriends.Shared.SnsDiagnosticUtil.RunRedactionSelfTest();
                RedactionSelfTestPassed = redactionSelfTestPassed;
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]", $"RunRedactionSelfTest 异常（视为 FAIL）: {ex.Message}");
                RedactionSelfTestPassed = false;
            }

            Stage76QuarantineRegistrationValid = VerifyRouteBRegistration(requireLifecycleHooks: false);
            if (!Stage76QuarantineRegistrationValid)
            {
                RoleLogger.Error("[Shared]",
                    "[P2P-Approval] !!! Route B quarantine registration/signal compatibility gate failed");
            }

            Stage78UnifiedRegistrationValid = VerifyStage78UnifiedConnectRegistrations() &&
                P2PListenHostCommandPermissionPatch.RegistrationValid;
            if (!Stage78UnifiedRegistrationValid)
            {
                RoleLogger.Error("[Shared]",
                    "[Stage7-8] !!! unified connect route/indicator registration gate failed");
            }

            Stage92SinglePortRegistrationValid = VerifyStage92SinglePortRegistration();
            if (!Stage92SinglePortRegistrationValid)
            {
                RoleLogger.Error("[Shared]",
                    "[Stage9-2] !!! single-port Direct-IP query projection registration gate failed");
            }

        }

        private void VerifyRegistrationClosure()
        {
            foreach (Core.Registration.RegistrationRecord record in _registrationClosure.Snapshot)
            {
                RoleLogger.Info("[Shared]",
                    $"[Registration] closure order={record.Order} domain={record.DomainId} " +
                    $"role={record.Role} capability={record.Capability} adapter={record.AdapterTypeName}");
            }
            RoleLogger.Info("[Shared]",
                "[Registration] Registration Closure complete; adapter catalog is immutable for this session");

            HarmonyCompatibilityAudit.Reset();
            VerifyCriticalPatches(RedactionSelfTestPassed);
        }
    }
}
