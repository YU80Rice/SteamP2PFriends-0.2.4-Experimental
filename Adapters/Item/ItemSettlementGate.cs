using System;
using SDG.Unturned;

namespace SteamP2PFriends.Adapters.Item
{
    public static class ItemSettlementGate
    {
        public static Func<byte, byte, bool> AllowsConsumption { get; set; }
        public static Func<byte, byte, bool> BlocksRegion { get; set; }
        public static Func<ulong, ulong, byte, byte, bool> BlocksRegionForObserver { get; set; }
        public static System.Action<byte, byte> EnsureStepFiveSettlement { get; set; }
        public static Func<byte, byte, bool> WasDelivered { get; set; }
        public static ItemStepFiveAdmission StepFiveAdmission { get; set; }
        public static int BlockedPickups { get; private set; }
        public static int BlockedIncrements { get; private set; }

        public static bool TryAuthorizeHostGeneration()
        {
            // 原生 step5 的 generateItems 位于邻区循环中；区域完成门由 Prefix
            // 对具体 loadedRegions 格逐格投影，不能把中心区域状态扩散成整次循环门。
            return true;
        }

        public static void EnsureRegionStepFive(byte x, byte y) =>
            EnsureStepFiveSettlement?.Invoke(x, y);

        public static bool ShouldHoldRegion(byte x, byte y)
        {
            EnsureRegionStepFive(x, y);
            return BlocksRegion != null && BlocksRegion(x, y);
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

        public static bool TryPickup(byte x, byte y, System.Action writeInventory, System.Action deleteGround)
        {
            if (!TryAuthorizePickup(x, y)) return false;
            writeInventory();
            deleteGround();
            return true;
        }
    }
}
