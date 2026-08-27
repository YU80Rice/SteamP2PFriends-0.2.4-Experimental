using HarmonyLib;
using SteamP2PFriends.Core.Patches;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SteamP2PFriends.WhitelistTests
{
    internal static class InventoryUiProjectionStaticILTests
    {
        private sealed class IUI6InventoryTarget : SDG.Unturned.PlayerInventory
        {
            public void Drag(byte page_0, byte x_0, byte y_0,
                byte page_1, byte x_1, byte y_1, byte rot_1) { }

            public void Swap(byte page_0, byte x_0, byte y_0, byte rot_0,
                byte page_1, byte x_1, byte y_1, byte rot_1) { }

            public void Drop(byte page, byte x, byte y) { }
        }

        internal static bool Test_IUI5_ReflectionContractExact()
        {
            FieldInfo pages = AccessTools.Field(
                typeof(SDG.Unturned.PlayerDashboardInventoryUI), "items");
            FieldInfo pending = AccessTools.Field(typeof(SDG.Unturned.SleekItems), "pendingItems");
            return ListenHostInventoryUiProjectionPatch.ReflectionContractAvailable &&
                pages != null && pages.FieldType == typeof(SDG.Unturned.SleekItems[]) &&
                pending != null && pending.FieldType == typeof(List<SDG.Unturned.ItemJar>);
        }

        internal static bool Test_IUI6_ProductionPostfixesActivate()
        {
            MethodInfo[] realOriginals =
            {
                AccessTools.Method(typeof(SDG.Unturned.PlayerInventory),
                    nameof(SDG.Unturned.PlayerInventory.ReceiveDragItem),
                    new[] { typeof(byte), typeof(byte), typeof(byte), typeof(byte), typeof(byte), typeof(byte), typeof(byte) }),
                AccessTools.Method(typeof(SDG.Unturned.PlayerInventory),
                    nameof(SDG.Unturned.PlayerInventory.ReceiveSwapItem),
                    new[] { typeof(byte), typeof(byte), typeof(byte), typeof(byte), typeof(byte), typeof(byte), typeof(byte), typeof(byte) }),
                AccessTools.Method(typeof(SDG.Unturned.PlayerInventory),
                    nameof(SDG.Unturned.PlayerInventory.ReceiveDropItem),
                    new[] { typeof(byte), typeof(byte), typeof(byte) })
            };
            MethodInfo[] postfixes =
            {
                ListenHostInventoryUiProjectionPatch.DragPostfix,
                ListenHostInventoryUiProjectionPatch.SwapPostfix,
                ListenHostInventoryUiProjectionPatch.DropPostfix
            };
            if (realOriginals.Any(x => x == null) || postfixes.Any(x => x == null)) return false;

            int[] expectedParameterCounts = { 7, 8, 3 };
            for (int i = 0; i < realOriginals.Length; i++)
            {
                MethodInfo original = realOriginals[i];
                if (original.DeclaringType != typeof(SDG.Unturned.PlayerInventory) ||
                    original.ReturnType != typeof(void)) return false;
                ParameterInfo[] parameters = original.GetParameters();
                if (parameters.Length != expectedParameterCounts[i] ||
                    parameters.Any(parameter => parameter.ParameterType != typeof(byte))) return false;
            }

            MethodInfo[] surrogates =
            {
                AccessTools.Method(typeof(IUI6InventoryTarget), nameof(IUI6InventoryTarget.Drag)),
                AccessTools.Method(typeof(IUI6InventoryTarget), nameof(IUI6InventoryTarget.Swap)),
                AccessTools.Method(typeof(IUI6InventoryTarget), nameof(IUI6InventoryTarget.Drop))
            };
            if (surrogates.Any(x => x == null)) return false;

            const string Owner = "com.yu80rice.steamp2pfriends.test.inventory-ui";
            Harmony harmony = new Harmony(Owner);
            try
            {
                for (int i = 0; i < surrogates.Length; i++)
                    harmony.Patch(surrogates[i], postfix: new HarmonyMethod(postfixes[i]));
                for (int i = 0; i < surrogates.Length; i++)
                {
                    HarmonyLib.Patches info = Harmony.GetPatchInfo(surrogates[i]);
                    if (info == null || !info.Postfixes.Any(p =>
                        p.owner == Owner && p.PatchMethod == postfixes[i])) return false;
                }
                return true;
            }
            finally
            {
                foreach (MethodInfo surrogate in surrogates)
                {
                    try { harmony.Unpatch(surrogate, HarmonyPatchType.All, Owner); }
                    catch { }
                }
            }
        }
    }
}
