using SteamP2PFriends.Adapters.Animal;
using SteamP2PFriends.Adapters.Item;
using SteamP2PFriends.Adapters.Resource;
using SteamP2PFriends.Core.Registration;
using SteamP2PFriends.MultiObserver.SPI;
using System;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class RegistrationClosureTests
    {
        internal static bool Test_REG01_ClosureValidatesRolesOrderAndImmutability()
        {
            var closure = new RegistrationClosure(new[]
            {
                new RegistrationRequirement(RegistrationDomainIds.Resource, true, true)
            });

            var resource = new ResourceDomainAdapter();
            string failure;
            if (closure.TryRegisterReplication(resource, out failure)) return false;
            if (failure.IndexOf("顺序", StringComparison.Ordinal) < 0) return false;
            if (!closure.TryRegisterLifecycle(resource, out failure)) return false;
            if (!closure.TryRegisterReplication(resource, out failure)) return false;
            if (closure.TryRegisterLifecycle(resource, out failure)) return false;
            if (failure.IndexOf("重复", StringComparison.Ordinal) < 0) return false;
            if (closure.TryRegisterLifecycle(new ItemDomainAdapter(), out failure)) return false;
            if (failure.IndexOf("未知", StringComparison.Ordinal) < 0) return false;
            if (!closure.TryClose(out failure) || !closure.IsClosed) return false;
            if (closure.Snapshot.Count != 2) return false;
            if (closure.Snapshot[0].Role != RegistrationRole.Lifecycle ||
                closure.Snapshot[0].Order != 1 ||
                closure.Snapshot[1].Role != RegistrationRole.Replication ||
                closure.Snapshot[1].Order != 2) return false;
            if (closure.TryRegisterLifecycle(resource, out failure)) return false;
            if (failure.IndexOf("关闭", StringComparison.Ordinal) < 0) return false;

            ILifecycleDomainAdapter lifecycle;
            IStateReplicationAdapter replication;
            return closure.TryGetLifecycle(RegistrationDomainIds.Resource, out lifecycle) &&
                closure.TryGetReplication(RegistrationDomainIds.Resource, out replication) &&
                ReferenceEquals(lifecycle, resource) && ReferenceEquals(replication, resource);
        }

        internal static bool Test_REG02_ClosureRejectsMissingRequiredRole()
        {
            var closure = new RegistrationClosure(new[]
            {
                new RegistrationRequirement(RegistrationDomainIds.Animal, true, true)
            });
            string failure;
            if (!closure.TryRegisterLifecycle(new AnimalDomainAdapter(), out failure)) return false;
            if (closure.TryClose(out failure)) return false;
            return failure.IndexOf("状态复制", StringComparison.Ordinal) >= 0;
        }

        internal static bool Test_All()
        {
            return Test_REG01_ClosureValidatesRolesOrderAndImmutability() &&
                Test_REG02_ClosureRejectsMissingRequiredRole() &&
                Test_REG03_StageCatalogRejectsOrderConflict();
        }

        private static bool Test_REG03_StageCatalogRejectsOrderConflict()
        {
            var catalog = new SteamP2PFriends.Core.Registration.PatchRegistrationStageCatalog();
            string failure;
            if (!catalog.TryRegister(new SteamP2PFriends.Core.Registration.PatchRegistrationStage(
                2, "Transport", "trace-2", "owner", "default", "target", () => { }), out failure))
                return false;
            if (catalog.TryRegister(new SteamP2PFriends.Core.Registration.PatchRegistrationStage(
                1, "Diagnostics", "trace-1", "owner", "default", "target", () => { }), out failure))
                return false;
            return failure.IndexOf("顺序", StringComparison.Ordinal) >= 0;
        }
    }
}
