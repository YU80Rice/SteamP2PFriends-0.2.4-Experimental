using SteamP2PFriends.Adapters.Animal;
using SteamP2PFriends.Adapters.Collision;
using SteamP2PFriends.Adapters.Item;
using SteamP2PFriends.Adapters.Resource;
using SteamP2PFriends.Adapters.Structure;
using SteamP2PFriends.Adapters.Zombie;
using SteamP2PFriends.Core.Patches;
using SteamP2PFriends.Shared;
using System;

namespace SteamP2PFriends
{
    public partial class SteamP2PFriendsPlugin
    {
        private bool RegisterDomainStage()
        {
            bool ok = true;
            try
            {
                Core.Patches.WorldSyncDiagnosticCore.RegisterSessionResetCallback(
                    SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleHelper.ResetHitLogs);
                SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleRegistration.MarkResetCallbackRegistered();
                RoleLogger.Info("[Shared]",
                    "[5B-1B/Plugin] OK RegisterSessionResetCallback(BarricadeLifecycleHelper.ResetHitLogs) 已登记 + MarkResetCallbackRegistered");
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]",
                    $"[5B-1B/Plugin] RegisterSessionResetCallback 失败: {ex.Message}");
                ok = false;
            }

            try
            {
                SteamP2PFriends.Adapters.Structure.Patches.P0EBarricadeLifecycle.BarricadeLifecycleRegistration.RegisterAtomically(_harmony);
            }
            catch (Exception ex)
            {
                RoleLogger.Error("[Shared]",
                    $"[5B-1B/Plugin] BarricadeLifecycleRegistration.RegisterAtomically 异常: {ex.Message}");
                ok = false;
            }

            return RegisterDomainAdapters() && ok;
        }

        private bool RegisterDomainAdapters()
        {
            bool ok = true;
            var item = new ItemDomainAdapter();
            ok &= RegisterLifecycle(item);
            ok &= RegisterReplication(item);
            var resource = new ResourceDomainAdapter();
            bool resourceLifecycleRegistered = RegisterLifecycle(resource);
            bool resourceReplicationRegistered = RegisterReplication(resource);
            ok &= resourceLifecycleRegistered;
            ok &= resourceReplicationRegistered;
            if (resourceLifecycleRegistered && resourceReplicationRegistered)
            {
                MultiObserver.MultiObserverShadowCoordinator.ConfigureResourceProduction(resource);
            }
            var building = new BuildingDomainAdapter();
            ok &= RegisterLifecycle(building);
            ok &= RegisterReplication(building);
            var zombie = new ZombieDomainAdapter();
            ok &= RegisterLifecycle(zombie);
            ok &= RegisterReplication(zombie);
            var animal = new AnimalDomainAdapter();
            ok &= RegisterLifecycle(animal);
            ok &= RegisterReplication(animal);
            ok &= RegisterLifecycle(new LevelObjectCollisionAdapter());
            return ok;
        }

        private bool RegisterLifecycle(MultiObserver.SPI.ILifecycleDomainAdapter adapter)
        {
            string failure;
            if (!_registrationClosure.TryRegisterLifecycle(adapter, out failure))
            {
                RoleLogger.Error("[Shared]", "[Registration] lifecycle adapter rejected: " + failure);
                return false;
            }
            return true;
        }

        private bool RegisterReplication(MultiObserver.SPI.IStateReplicationAdapter adapter)
        {
            string failure;
            if (!_registrationClosure.TryRegisterReplication(adapter, out failure))
            {
                RoleLogger.Error("[Shared]", "[Registration] replication adapter rejected: " + failure);
                return false;
            }
            return true;
        }
    }
}
