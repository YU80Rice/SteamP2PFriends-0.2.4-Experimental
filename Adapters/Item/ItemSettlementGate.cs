using System;
using SDG.Unturned;

namespace SteamP2PFriends.Adapters.Item
{
    public static class ItemSettlementGate
    {
        public static Func<byte, byte, bool> AllowsConsumption { get; set; }
        public static Func<byte, byte, bool> BlocksRegion { get; set; }
        public static System.Action<byte, byte> EnsureStepFiveSettlement { get; set; }
        public static System.Action<Player, byte, byte> RegisterStepFiveObserver { get; set; }
        public static Func<byte, byte, bool> SuppressDeliveredStepFive { get; set; }
        public static Func<byte, byte, bool> WasDelivered { get; set; }
        public static int BlockedPickups { get; private set; }
        public static int BlockedIncrements { get; private set; }
        [ThreadStatic] private static byte _regionX;
        [ThreadStatic] private static byte _regionY;
        [ThreadStatic] private static bool _hasRegion;

        public static void SetRegionContext(byte x, byte y, byte step)
        {
            _regionX = x;
            _regionY = y;
            _hasRegion = step == 5;
        }

        public static bool TryAuthorizeHostGeneration()
        {
            return !_hasRegion || TryAuthorizeIncrement(_regionX, _regionY);
        }

        public static void EnsureRegionStepFive(byte x, byte y) =>
            EnsureStepFiveSettlement?.Invoke(x, y);

        public static bool ShouldHoldRegion(byte x, byte y)
        {
            EnsureRegionStepFive(x, y);
            return (WasDelivered != null && WasDelivered(x, y))
                || (BlocksRegion != null && BlocksRegion(x, y));
        }

        public static bool RegisterAndShouldHold(Player player, byte x, byte y)
        {
            RegisterStepFive(player, x, y);
            return ShouldHoldRegion(x, y);
        }

        public static void RegisterStepFive(Player player, byte x, byte y) =>
            RegisterStepFiveObserver?.Invoke(player, x, y);

        public static bool ShouldSuppressDeliveredStepFive(byte x, byte y) =>
            SuppressDeliveredStepFive != null && SuppressDeliveredStepFive(x, y);

        public static bool TryAuthorizePickup(byte x, byte y)
        {
            if (AllowsConsumption == null || AllowsConsumption(x, y)) return true;
            BlockedPickups++;
            return false;
        }

        public static bool TryAuthorizeIncrement(byte x, byte y)
        {
            if (AllowsConsumption == null || AllowsConsumption(x, y)) return true;
            BlockedIncrements++;
            return false;
        }

        public static bool TryPickup(byte x, byte y, System.Action writeInventory, System.Action deleteGround)
        {
            if (!TryAuthorizePickup(x, y)) return false;
            writeInventory();
            deleteGround();
            return true;
        }
    }
}
