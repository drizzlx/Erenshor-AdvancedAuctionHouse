using AdvancedAuctionHouse;
using UnityEngine;

namespace AdvancedAuctionHouse.Manager;

public static class UIManager
{
    public static float GetResponsiveWidth() => Screen.width * 0.34f;
    public static float GetResponsiveHeight() => Screen.height * 0.3f;
    public static float MainUIWidth = 1000f;
    public static float MainUIHeight = 500f;
    private static Vector2 _lastScreenSize = Vector2.zero;
    
    public static bool IsAuctionHouseWindowOpen()
    {
        return AdvancedAuctionHousePlugin.AuctionHouseUIRoot != null && AdvancedAuctionHousePlugin.AuctionHouseUIRoot.activeSelf;
    }
}