using HarmonyLib;

namespace AdvancedAuctionHouse.patch
{

    [HarmonyPatch(typeof(AuctionHouseUI), "OpenAuctionHouse")]
    public static class AuctionHouseUIOpenAuctionHousePatch
    {
        
        [HarmonyPostfix]
        public static void Postfix(AuctionHouseUI __instance)
        {
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
            if (AdvancedAuctionHousePlugin.Instance == null)
                return true;
            
            return 
                AdvancedAuctionHousePlugin.Instance.HandleAuctionHouseWindowClosing(__instance);
        }
    }
}