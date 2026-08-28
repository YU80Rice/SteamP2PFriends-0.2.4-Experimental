using HarmonyLib;
using SDG.Unturned;
using SteamP2PFriends.Adapters.Resource;
using SteamP2PFriends.Host;
using SteamP2PFriends.Adapters.Collision.Patches;
using SteamP2PFriends.Shared;
using System;
using System.Reflection;

namespace SteamP2PFriends.Adapters.Resource.Patches
{
    /// <summary>
    /// 远区自然资源（树木/矿石）物理碰撞与模型激活补丁 (LevelGroundRemoteTreeCollisionPatch)
    /// 拦截 ResourceSpawnpoint.SetIsActiveInRegion，当远端客机身处该 2D 资源区域时，
    /// 阻止原版 Listen-Host 因房主远离而销毁/禁用该区域树木与矿石的物理碰撞体，彻底解决客机穿模问题。
    /// </summary>
    public static class LevelGroundRemoteTreeCollisionPatch
    {
        private const string HarmonyId = SteamP2PFriendsPlugin.HARMONY_ID;
        public static bool RegistrationSucceeded { get; private set; }
        public static string RegistrationSummary { get; private set; } = "未登记";

        public static bool RegisterManual(Harmony harmony)
        {
            if (harmony == null)
            {
                RegistrationSummary = "harmony=null";
                RoleLogger.Error("[Shared]", "[ResourceCollision] !!! " + RegistrationSummary);
                return false;
            }

            try
            {
                MethodInfo targetMethod = AccessTools.Method(typeof(ResourceSpawnpoint), "SetIsActiveInRegion", new Type[] { typeof(bool) });
                MethodInfo prefixMethod = AccessTools.Method(typeof(LevelGroundRemoteTreeCollisionPatch), nameof(SetIsActiveInRegion_Prefix));

                if (targetMethod == null || prefixMethod == null)
                {
                    RegistrationSummary = "目标或补丁方法解析失败";
                    RoleLogger.Error("[Shared]", "[ResourceCollision] !!! " + RegistrationSummary);
                    return false;
                }

                harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod));
                RegistrationSucceeded = true;
                RegistrationSummary = "ResourceSpawnpoint.SetIsActiveInRegion.Prefix OK";
                RoleLogger.Info("[Shared]", "[ResourceCollision] OK " + RegistrationSummary);
                return true;
            }
            catch (Exception ex)
            {
                RegistrationSummary = "登记异常: " + ex.GetType().Name + ": " + ex.Message;
                RoleLogger.Error("[Shared]", "[ResourceCollision] !!! " + RegistrationSummary);
                return false;
            }
        }

        public static void SetIsActiveInRegion_Prefix(ResourceSpawnpoint __instance, ref bool isActive)
        {
            if (isActive)
            {
                ResourceObservability.Info("[Host]", "CollisionActivation", "-", 0UL, 0UL, 0U,
                    ResourceObservability.NativePath(false), false, "skipped", "reason=already-active");
                return; // 原版已要求激活，无需覆盖
            }

            if (!HostManager.IsP2PHostMode || !HostManager.ShouldProcessClientHostListen())
            {
                ResourceObservability.Info("[Shared]", "CollisionActivation", "-", 0UL, 0UL, 0U,
                    ResourceObservability.NativePath(false), false, "skipped", "reason=not-p2p-listen-host");
                return;
            }

            if (__instance == null)
            {
                ResourceObservability.Error("[Shared]", "CollisionActivation", "-", 0UL, 0UL, 0U,
                    "Fallback", false, "failed", "reason=spawnpoint-null");
                return;
            }

            try
            {
                if (Regions.tryGetCoordinate(__instance.point, out byte x, out byte y))
                {
                    bool covered = ResourceRegionLifecycleAdapter.IsRegionActive(x, y)
                        || LevelObjectRemoteCollisionPatch.IsRegionCovered(x, y);
                    bool spiActive = MultiObserver.MultiObserverShadowCoordinator.IsResourceProductionActive;
                    if (covered)
                    {
                        isActive = true; // 远端观察者在此区域，保持树木/矿石碰撞体激活
                        ResourceObservability.Info("[Host]", "CollisionActivation", $"({x},{y})",
                            MultiObserver.MultiObserverShadowCoordinator.ResourceSessionEpoch, 0UL,
                            ResourceSnapshotAdapter.GetRegionGeneration(new Core.Identity.RegionKey(x, y)),
                            ResourceObservability.NativePath(spiActive), spiActive, "success", "covered=true");
                    }
                    else
                    {
                        ResourceObservability.Info("[Host]", "CollisionActivation", $"({x},{y})",
                            MultiObserver.MultiObserverShadowCoordinator.ResourceSessionEpoch, 0UL,
                            ResourceSnapshotAdapter.GetRegionGeneration(new Core.Identity.RegionKey(x, y)),
                            ResourceObservability.NativePath(spiActive), spiActive, "skipped", "reason=no-remote-demand");
                    }
                }
                else
                {
                    ResourceObservability.Warn("[Host]", "CollisionActivation", "-", 0UL, 0UL, 0U,
                        "Fallback", MultiObserver.MultiObserverShadowCoordinator.IsResourceProductionActive,
                        "rejected", "reason=region-coordinate-unavailable");
                }
            }
            catch (Exception ex)
            {
                ResourceObservability.Error("[Shared]", "CollisionActivation", "-", 0UL, 0UL, 0U,
                    "Fallback", MultiObserver.MultiObserverShadowCoordinator.IsResourceProductionActive,
                    "failed", "exception=" + ex.GetType().Name);
            }
        }
    }
}
