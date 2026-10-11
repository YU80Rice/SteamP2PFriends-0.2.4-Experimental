using System;

namespace SteamP2PFriends.Adapters.Item
{
    public static class ItemSettlementGate
    {
        public static Func<byte, byte, bool> AllowsConsumption { get; set; }
        public static Func<byte, byte, bool> BlocksStepFive { get; set; }
        public static Action<byte, byte> EnsureStepFiveSettlement { get; set; }
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

        public static bool TryAuthorizeStepFive(byte x, byte y)
        {
            EnsureStepFiveSettlement?.Invoke(x, y);
            return BlocksStepFive == null || !BlocksStepFive(x, y);
        }

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

        public static bool TryPickup(byte x, byte y, Action writeInventory, Action deleteGround)
        {
            if (!TryAuthorizePickup(x, y)) return false;
            writeInventory();
            deleteGround();
            return true;
        }
    }
}
