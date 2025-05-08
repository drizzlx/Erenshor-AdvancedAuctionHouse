using HarmonyLib;
using UnityEngine.EventSystems;

namespace AdvancedAuctionHouse.patch
{
    
    [HarmonyPatch(typeof(ItemIcon), "InteractItemSlot")]
    public static class ItemIconInteractItemSlotPatch
    {
        static bool Prefix(ItemIcon __instance)
        {
            // Disable native click during sell window
            if (AdvancedAuctionHousePlugin.Instance != null)
            {
                if (AdvancedAuctionHousePlugin.Instance.IsAuctionHouseSellWindowOpen())
                {
                    AdvancedAuctionHousePlugin.Instance.OnSellItemClicked(__instance);
                    return false;
                }
                
                if (AdvancedAuctionHousePlugin.Instance.IsAuctionHouseWindowOpen() && __instance.VendorSlot)
                {
                    return false;
                }
            }

            return true;
        }
    }
}