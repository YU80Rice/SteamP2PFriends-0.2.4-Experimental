using HarmonyLib;
using SteamP2PFriends.Shared;
using System;

namespace SteamP2PFriends.Adapters.Collision.Patches
{
    /// <summary>
    /// Collision 旧 RemoteCoverage Writer 的退役兼容壳；正式写入只经 CollisionExecutionPort。
    /// </summary>
    public static class LevelObjectRemoteCollisionPatch
    {
        public static bool AllRegistrationsSucceeded { get; private set; }
        public static bool RootActivationPostfixRegistered { get; private set; }
        public static bool RegionTrackerPostfixRegistered { get; private set; }
        public static string RegistrationSummary { get; private set; } = "未登记";

        public static bool RegisterManual(Harmony harmony)
        {
            AllRegistrationsSucceeded = false;
            RootActivationPostfixRegistered = false;
            RegionTrackerPostfixRegistered = false;
            RegistrationSummary = "retired=legacy-remote-coverage-writer";
            return false;
        }
    }
}