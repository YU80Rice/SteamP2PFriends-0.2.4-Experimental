using SteamP2PFriends.Core.Patches;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class InventoryUiProjectionTests
    {
        internal static bool Test_IUI1_ExactProjectionNoRepair()
        {
            object a = new object();
            object b = new object();
            return ListenHostInventoryUiProjectionPatch.ProjectionIsExact(
                new[] { a, b }, new[] { a }, new[] { b });
        }

        internal static bool Test_IUI2_StaleRenderedJarDetected()
        {
            object current = new object();
            object stale = new object();
            return !ListenHostInventoryUiProjectionPatch.ProjectionIsExact(
                new[] { current }, new[] { stale, current }, System.Array.Empty<object>());
        }

        internal static bool Test_IUI3_StalePendingJarDetected()
        {
            object current = new object();
            object stale = new object();
            return !ListenHostInventoryUiProjectionPatch.ProjectionIsExact(
                new[] { current }, System.Array.Empty<object>(), new[] { stale, current });
        }

        internal static bool Test_IUI4_IdentityNotValueEquivalence()
        {
            string authoritative = new string(new[] { 'x' });
            string projection = new string(new[] { 'x' });
            return authoritative == projection &&
                !object.ReferenceEquals(authoritative, projection) &&
                !ListenHostInventoryUiProjectionPatch.ProjectionIsExact(
                    new object[] { authoritative }, new object[] { projection }, System.Array.Empty<object>());
        }
    }
}
