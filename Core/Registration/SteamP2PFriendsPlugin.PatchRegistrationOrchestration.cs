using SteamP2PFriends.Adapters.Animal;
using SteamP2PFriends.Adapters.Collision;
using SteamP2PFriends.Adapters.Item;
using SteamP2PFriends.Adapters.Resource;
using SteamP2PFriends.Adapters.Structure;
using SteamP2PFriends.Adapters.Zombie;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.Core.Patches;
using SteamP2PFriends.Shared;
using System;
using System.Collections.Generic;

using SteamP2PFriends.Security.Patches;

namespace SteamP2PFriends
{
    public partial class SteamP2PFriendsPlugin
    {
        private Core.Registration.RegistrationClosure _registrationClosure;
        private bool _registrationStageFailed;

        private Core.Registration.PatchRegistrationPlan CreatePatchRegistrationPlan()
        {
            _registrationClosure = new Core.Registration.RegistrationClosure(new[]
            {
                new Core.Registration.RegistrationRequirement(
                    DomainIds.Item, true, true),
                new Core.Registration.RegistrationRequirement(
                    DomainIds.Resource, true, true),
                new Core.Registration.RegistrationRequirement(
                    DomainIds.Building, true, true),
                new Core.Registration.RegistrationRequirement(
                    DomainIds.Zombie, true, true),
                new Core.Registration.RegistrationRequirement(
                    DomainIds.Animal, true, true),
                new Core.Registration.RegistrationRequirement(
                    DomainIds.Collision, true, false)
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

        private bool VerifyRegistrationStage()
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

            return RedactionSelfTestPassed && Stage76QuarantineRegistrationValid &&
                Stage78UnifiedRegistrationValid && Stage92SinglePortRegistrationValid;
        }

        private bool VerifyRegistrationClosure()
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
            return VerifyCriticalPatches(RedactionSelfTestPassed);
        }
    }
}
