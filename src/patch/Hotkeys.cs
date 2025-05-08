using HarmonyLib;
using UnityEngine.EventSystems;

namespace AdvancedAuctionHouse.patch
{
    
    [HarmonyPatch(typeof(Hotkeys), "Update")]
    public static class HotkeysUpdatePatch
    {
        static bool Prefix(Hotkeys __instance)
        {
            if (AdvancedAuctionHousePlugin.Instance != null &&
                AdvancedAuctionHousePlugin.Instance.BuyoutPriceInputField != null 
                && AdvancedAuctionHousePlugin.Instance.BuyoutPriceInputField.isFocused)
            {
                return false;
            }

            return true;
        }
    }
}