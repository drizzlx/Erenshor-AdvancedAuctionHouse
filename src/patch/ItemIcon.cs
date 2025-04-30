using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ImprovedAuctionHouse.patch
{

    [HarmonyPatch(typeof(ItemIcon), "Awake")]
    public static class ItemIconAwakePatch
    {
        static bool Prefix(ItemIcon __instance)
        {
            // Prevent errors with AuctionHouseUI
            if (GameData.AuctionWindowOpen)
                return false;

            return true;
        }
    }

}