using SteamP2PFriends.Adapters.Zombie;
using SteamP2PFriends.Core.Identity;
using SteamP2PFriends.MultiObserver.SPI;
using System;
using System.Linq;
using System.Reflection;

namespace SteamP2PFriends.WhitelistTests
{
    /// <summary>
    /// Ticket 03 的 StaticIL 契约门禁。这里检查已编译类型的公开形状，
    /// 不把运行时 Harmony 执行或真实游戏回调误报为静态证据。
    /// </summary>
    internal static class IdentityStaticILContractTests
    {
        internal static bool Test_All()
        {
            return Test_RegionKeyHasNoImplicitIntegerConversion()
                && Test_DomainIdHasNoPublicArbitraryConstructor()
                && Test_ZombieSnapshotUsesBoundKey()
                && Test_LeaseTicketSeparatesLifecycleAxes()
                && Test_SpatialInterfacesUseTypedIdentities();
        }

        private static bool Test_RegionKeyHasNoImplicitIntegerConversion()
        {
            return !HasImplicitOperator(typeof(RegionKey))
                && !HasImplicitOperator(typeof(BoundKey));
        }

        private static bool Test_DomainIdHasNoPublicArbitraryConstructor()
        {
            return typeof(DomainId).GetConstructor(new[] { typeof(string) }) == null;
        }

        private static bool HasImplicitOperator(Type identityType)
        {
            return identityType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Any(method => method.Name == "op_Implicit");
        }

        private static bool Test_ZombieSnapshotUsesBoundKey()
        {
            FieldInfo bound = typeof(ZombieSnapshotToken).GetField("Bound");
            return bound != null && bound.FieldType == typeof(BoundKey);
        }

        private static bool Test_LeaseTicketSeparatesLifecycleAxes()
        {
            FieldInfo session = typeof(LeaseTicket).GetField("SessionEpoch");
            FieldInfo region = typeof(LeaseTicket).GetField("RegionGeneration");
            return session != null && region != null
                && session.FieldType == typeof(SessionEpoch)
                && region.FieldType == typeof(RegionGeneration)
                && session.FieldType != region.FieldType;
        }

        private static bool Test_SpatialInterfacesUseTypedIdentities()
        {
            MethodInfo regionEntry = typeof(IStateReplicationAdapter).GetMethod("OnObserverEntered");
            MethodInfo boundEntry = typeof(IBoundStateReplicationAdapter).GetMethod("OnBoundObserverEntered");
            return regionEntry != null && boundEntry != null
                && regionEntry.GetParameters().Last().ParameterType == typeof(RegionKey)
                && boundEntry.GetParameters().Last().ParameterType == typeof(BoundKey);
        }
    }
}
