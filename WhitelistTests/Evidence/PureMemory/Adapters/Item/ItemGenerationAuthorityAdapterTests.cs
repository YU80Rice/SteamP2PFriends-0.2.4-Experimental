using SteamP2PFriends.Adapters.Item;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class ItemGenerationAuthorityAdapterTests
    {
        internal static bool Test_M1I01_LocalObserverPreservesVanillaWhenAdapterNotReady()
        {
            return ItemGenerationAuthorityAdapter.Evaluate(true, false, false, false);
        }

        internal static bool Test_M1I02_DedicatedRemotePreservesFullMapAuthority()
        {
            return !ItemGenerationAuthorityAdapter.Evaluate(false, true, true, true);
        }

        internal static bool Test_M1I03_ListenRemoteRequiresRegistration()
        {
            return !ItemGenerationAuthorityAdapter.Evaluate(false, false, false, true)
                && ItemGenerationAuthorityAdapter.Evaluate(false, false, true, true);
        }

        internal static bool Test_M1I04_UnrelatedRemoteCannotGenerate()
        {
            return !ItemGenerationAuthorityAdapter.Evaluate(false, false, true, false);
        }

        internal static bool Test_M1I05_AuthorizationIsNotPartOfWorldPresenceDecision()
        {
            // There is intentionally no approval argument: a created remote Player is a world
            // observer while pending, and must receive the same generated world snapshot.
            return ItemGenerationAuthorityAdapter.Evaluate(false, false, true, true);
        }

    }
}
