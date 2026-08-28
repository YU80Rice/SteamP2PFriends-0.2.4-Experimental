using SteamP2PFriends.Adapters.Resource.Patches;
using System;
using System.Reflection;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class ResourceHarvestRegistrationStaticILContractTests
    {
        internal static bool Test_All()
        {
            MethodInfo[] methods = typeof(ResourceManagerHarvestReplicationPatch).GetMethods(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return ContainsCall(methods, "VerifyPatchIdentity")
                && ContainsCall(methods, "Unpatch")
                && ContainsCall(methods, "Patch");
        }

        internal static bool Test_FourHookRegistrationStateIsExplicit()
        {
            string[] expectedProperties =
            {
                "ServerSetResourceDeadPostfixRegistered",
                "ServerSetResourceAlivePostfixRegistered",
                "ReceiveResourceDeadPostfixRegistered",
                "ReceiveResourceAlivePostfixRegistered"
            };

            foreach (string name in expectedProperties)
            {
                if (typeof(ResourceManagerHarvestReplicationPatch).GetProperty(
                    name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) == null)
                    return false;
            }

            return true;
        }

        private static bool ContainsCall(MethodInfo[] methods, string name)
        {
            foreach (MethodInfo method in methods)
            {
                if (CallsNamed(method, name)) return true;
            }

            return false;
        }

        private static bool CallsNamed(MethodInfo method, string name)
        {
            byte[] il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return false;

            for (int offset = 0; offset + 4 < il.Length; offset++)
            {
                int token = BitConverter.ToInt32(il, offset);
                try
                {
                    MethodBase called = method.Module.ResolveMethod(token);
                    if (called != null && string.Equals(called.Name, name, StringComparison.Ordinal)) return true;
                }
                catch { }
            }

            return false;
        }
    }
}
