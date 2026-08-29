using SteamP2PFriends.Adapters.Resource;
using SteamP2PFriends.Adapters.Resource.Patches;
using SteamP2PFriends.Core.Identity;
using System;
using System.Reflection;
using System.Reflection.Emit;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class ResourceObservabilityTests
    {
        internal static bool Test_M6O01_FormatsRequiredResourceFields()
        {
            string line = ResourceObservability.Format(
                "[Host]",
                "LeaseAcquire",
                new RegionKey(3, 4).ToString(),
                7UL,
                11UL,
                13U,
                "SPI",
                true,
                "success",
                "demand=2");

            return ContainsAll(line,
                "caseId=",
                "role=Host",
                "domain=Resource",
                "region=(3,4)",
                "sessionEpoch=7",
                "connectionGeneration=11",
                "regionGeneration=13",
                "path=SPI",
                "spiActive=true",
                "outcome=success",
                "demand=2");
        }

        internal static bool Test_M6O02_FallbackAndSkippedAreExplicit()
        {
            string line = ResourceObservability.Format(
                "[Shared]",
                "ResourceUnavailable",
                "-",
                0UL,
                0UL,
                0U,
                "Fallback",
                false,
                "skipped",
                "reason=spi-not-active");

            return ContainsAll(line,
                "role=Shared",
                "domain=Resource",
                "region=-",
                "sessionEpoch=0",
                "connectionGeneration=0",
                "regionGeneration=0",
                "path=Fallback",
                "spiActive=false",
                "outcome=skipped",
                "reason=spi-not-active");
        }

        internal static bool Test_M6O03_NativeReceiveDoesNotInventAcceptance()
        {
            var ledger = new ResourceSnapshotReplicationLedger();
            ledger.ResetSession(9UL);
            ResourceDeltaReceiveObservation observation = ledger.RecordNativeDeltaReceive(
                new RegionKey(2, 3), 7UL);
            return !observation.DecisionAvailable
                && observation.SessionEpoch == 9UL
                && observation.ConnectionGeneration == 7UL
                && observation.DeltaSequence == 1U;
        }

        internal static bool Test_M6O04_PathAndOutcomeRejectUnknownValues()
        {
            bool pathRejected = false;
            bool outcomeRejected = false;
            try { ResourceObservationPath.From("not-a-real-path"); }
            catch (ArgumentOutOfRangeException) { pathRejected = true; }
            try { ResourceObservationOutcome.From("not-a-real-outcome"); }
            catch (ArgumentOutOfRangeException) { outcomeRejected = true; }
            return pathRejected && outcomeRejected;
        }

        internal static bool Test_M6O05_WorldSyncReflectionFailuresHaveReasons()
        {
            Type type = typeof(ResourceManagerWorldSyncDiagnosticPatch);
            MethodInfo checkSafe = type.GetMethod("TryReadRegionsCheckSafe",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo networked = type.GetMethod("TryCountNetworkedResourceRegions",
                BindingFlags.Static | BindingFlags.NonPublic);

            return checkSafe != null
                && checkSafe.ReturnType == typeof(bool)
                && checkSafe.GetParameters().Length == 4
                && networked != null
                && networked.ReturnType == typeof(bool)
                && networked.GetParameters().Length == 2;
        }

        internal static bool Test_M6O06_WorldSyncNullRegionFailsClosed()
        {
            MethodInfo networked = typeof(ResourceManagerWorldSyncDiagnosticPatch).GetMethod(
                "TryCountNetworkedResourceRegions", BindingFlags.Static | BindingFlags.NonPublic);
            return ContainsStringLiteral(networked, "native-region-null region=");
        }

        internal static bool Test_M6O07_IncompleteObservationPrecedesQuota()
        {
            MethodInfo prefix = typeof(ResourceManagerWorldSyncDiagnosticPatch).GetMethod(
                "OnRegionUpdated_Prefix", BindingFlags.Static | BindingFlags.Public);
            int identityOffset = FindCallOffset(prefix, "TryReadSteamId");
            int quotaOffset = FindCallOffset(prefix, "TryAcquirePlayerQuota");
            int incompleteOffset = FindCallOffset(prefix, "IsObservationComplete");
            return identityOffset >= 0 && quotaOffset > identityOffset && incompleteOffset > identityOffset;
        }

        internal static bool Test_M6O08_ReceiveCompletenessPrecedesQuota()
        {
            MethodInfo postfix = typeof(ResourceManagerWorldSyncDiagnosticPatch).GetMethod(
                "ReceiveResources_Postfix", BindingFlags.Static | BindingFlags.Public);
            int completenessOffset = FindCallOffset(postfix, "TryCountNetworkedResourceRegions");
            int quotaOffset = FindCallOffset(postfix, "TryAcquireQuota");
            return completenessOffset >= 0 && quotaOffset > completenessOffset;
        }

        internal static bool Test_M6O09_HarvestValidatesNativePostcondition()
        {
            MethodInfo dead = typeof(ResourceManagerHarvestReplicationPatch).GetMethod(
                "ServerSetResourceDead_Postfix", BindingFlags.Static | BindingFlags.Public);
            MethodInfo alive = typeof(ResourceManagerHarvestReplicationPatch).GetMethod(
                "ServerSetResourceAlive_Postfix", BindingFlags.Static | BindingFlags.Public);
            return FindCallOffset(dead, "TryValidateNativeHarvestOutcome") >= 0
                && FindCallOffset(alive, "TryValidateNativeHarvestOutcome") >= 0
                && FindCallOffset(dead, "TryValidateNativeHarvestOutcome") < FindCallOffset(dead, "RecordResourceDead")
                && FindCallOffset(alive, "TryValidateNativeHarvestOutcome") < FindCallOffset(alive, "RecordResourceAlive");
        }

        private static bool ContainsStringLiteral(MethodInfo method, string expected)
        {
            if (method == null || expected == null) return false;
            byte[] il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return false;
            for (int offset = 0; offset + 4 < il.Length; offset++)
            {
                if (il[offset] != OpCodes.Ldstr.Value) continue;
                int token = BitConverter.ToInt32(il, offset + 1);
                try
                {
                    if (string.Equals(method.Module.ResolveString(token), expected, StringComparison.Ordinal))
                        return true;
                }
                catch { }
            }
            return false;
        }

        private static int FindCallOffset(MethodInfo method, string name)
        {
            if (method == null) return -1;
            byte[] il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return -1;
            for (int offset = 0; offset + 4 < il.Length; offset++)
            {
                int token = BitConverter.ToInt32(il, offset);
                try
                {
                    MethodBase called = method.Module.ResolveMethod(token);
                    if (called != null && string.Equals(called.Name, name, StringComparison.Ordinal))
                        return offset;
                }
                catch { }
            }
            return -1;
        }

        private static bool ContainsAll(string value, params string[] expected)
        {
            foreach (string item in expected)
            {
                if (value.IndexOf(item, StringComparison.Ordinal) < 0) return false;
            }

            return true;
        }
    }
}
