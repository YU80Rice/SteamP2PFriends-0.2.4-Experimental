using System;

namespace SteamP2PFriends.Adapters.Item
{
    public static class ItemSettlementGate
    {
        public static Func<byte, byte, bool> AllowsConsumption { get; set; }
        public static int BlockedPickups { get; private set; }
        public static int BlockedIncrements { get; private set; }

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
