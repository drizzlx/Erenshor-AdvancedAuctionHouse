using HarmonyLib;

namespace AdvancedAuctionHouse.patch
{

    [HarmonyPatch(typeof(AuctionHouseUI), "OpenAuctionHouse")]
    public static class AuctionHouseUIOpenAuctionHousePatch
    {
        
        [HarmonyPostfix]
        public static void Postfix(AuctionHouseUI __instance)
        {
            // Close the original window
            __instance.AHWindow.SetActive(false);
            // Open custom UI
            AdvancedAuctionHousePlugin.Instance.OpenAuctionHouseUI();
        }
    }
    
    [HarmonyPatch(typeof(AuctionHouseUI), "Update")]
    public static class AuctionHouseUIUpdatePatch
    {
        
        [HarmonyPrefix]
        public static bool Prefix(AuctionHouseUI __instance)
        {
            return true;
        }
    }
}