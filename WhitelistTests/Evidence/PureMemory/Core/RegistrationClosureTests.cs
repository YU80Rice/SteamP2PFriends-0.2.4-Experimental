using SteamP2PFriends.Adapters.Animal;
using SteamP2PFriends.Adapters.Item;
using SteamP2PFriends.Adapters.Resource;
using SteamP2PFriends.Core.Registration;
using SteamP2PFriends.Core.Identity;
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

        internal static bool Test_REG04_ProductionRequirementsCloseWithDomainCatalog()
        {
            // 票 09 修复轮回归锁:生产要求清单必须与领域登记目录一致。
            // Item/Resource/Building/Zombie/Animal 以生命周期+状态复制登记后必须可闭合;
            // 票 08 退役旧 Collision Writer 后,Collision 不再进 Patch Registration Closure,
            // 要求清单不得残留其登记要求(残留会在真实启动 TryClose 时失败)。
            var closure = new RegistrationClosure(PatchRegistrationRequirements.Create());
            string failure;
            if (!closure.TryRegisterLifecycle(new ItemDomainAdapter(), out failure)) return false;
            if (!closure.TryRegisterReplication(new ItemDomainAdapter(), out failure)) return false;
            if (!closure.TryRegisterLifecycle(new ResourceDomainAdapter(), out failure)) return false;
            if (!closure.TryRegisterReplication(new ResourceDomainAdapter(), out failure)) return false;
            if (!closure.TryRegisterLifecycle(
                new SteamP2PFriends.Adapters.Structure.BuildingDomainAdapter(), out failure)) return false;
            if (!closure.TryRegisterReplication(
                new SteamP2PFriends.Adapters.Structure.BuildingDomainAdapter(), out failure)) return false;
            if (!closure.TryRegisterLifecycle(
                new SteamP2PFriends.Adapters.Zombie.ZombieDomainAdapter(), out failure)) return false;
            if (!closure.TryRegisterReplication(
                new SteamP2PFriends.Adapters.Zombie.ZombieDomainAdapter(), out failure)) return false;
            if (!closure.TryRegisterLifecycle(new AnimalDomainAdapter(), out failure)) return false;
            if (!closure.TryRegisterReplication(new AnimalDomainAdapter(), out failure)) return false;
            if (!closure.TryClose(out failure) || !closure.IsClosed) return false;
            return closure.Snapshot.Count == 10;
        }

        internal static bool Test_All()
        {
            return Test_REG01_ClosureValidatesRolesOrderAndImmutability() &&
                Test_REG02_ClosureRejectsMissingRequiredRole() &&
                Test_REG03_StageCatalogRejectsOrderConflict();
        }

        private static bool Test_REG03_StageCatalogRejectsOrderConflict()
        {
            var catalog = new SteamP2PFriends.Core.Registration.PatchRegistrationStageCatalog("owner");
            string failure;
            if (!catalog.TryRegister(new SteamP2PFriends.Core.Registration.PatchRegistrationStage(
                2, "Diagnostics", "U3-REG-02-InternalDiagnostics", "owner", "default",
                "internal NetMessages and lifecycle handlers", () => true), out failure))
                return false;
            if (catalog.TryRegister(new SteamP2PFriends.Core.Registration.PatchRegistrationStage(
                1, "Transport", "U3-REG-01-Wrapper", "owner", "default",
                "SteamNetworkingSockets/Callback wrappers", () => true), out failure))
                return false;
            return failure.IndexOf("顺序", StringComparison.Ordinal) >= 0;
        }
    }
}
