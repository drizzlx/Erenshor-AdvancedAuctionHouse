using HarmonyLib;
using UnityEngine.EventSystems;

namespace AdvancedAuctionHouse.patch
{

    [HarmonyPatch(typeof(ItemIcon), "Awake")]
    public static class ItemIconAwakePatch
    {
        static bool Prefix(ItemIcon __instance)
        {
            // Prevent errors with AuctionHouseUI
            if (AdvancedAuctionHousePlugin.Instance != null 
                && AdvancedAuctionHousePlugin.Instance.IsAuctionHouseWindowOpen())
                return false;

            return true;
        }
    }
    
    [HarmonyPatch(typeof(ItemIcon), "InteractItemSlot")]
    public static class ItemIconInteractItemSlotPatch
    {
        static bool Prefix(ItemIcon __instance)
        {
            // Disable native click during sell window
            if (AdvancedAuctionHousePlugin.Instance != null)
            {
                if (AdvancedAuctionHousePlugin.Instance.IsAuctionHouseSellWindowOpen() && !__instance.VendorSlot)
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