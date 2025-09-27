using AdvancedAuctionHouse;
using UnityEngine;

namespace ErenBot.UI;

public static class UIManager
{
    public static float GetResponsiveWidth() => Screen.width * 0.34f;
    public static float GetResponsiveHeight() => Screen.height * 0.3f;
    public static float MainUIWidth = 1000f;
    public static float MainUIHeight = 500f;
    private static Vector2 _lastScreenSize = Vector2.zero;

    public static void Update()
    {
        if (!Mathf.Approximately(_lastScreenSize.x, Screen.width) || !Mathf.Approximately(_lastScreenSize.y, Screen.height))
        {
            _lastScreenSize = new Vector2(Screen.width, Screen.height);
        
            // Recalculate dimensions and apply them
            if (AdvancedAuctionHousePlugin.AuctionHouseUIRoot != null)
            {
                MainUIWidth = GetResponsiveWidth();
                MainUIHeight = GetResponsiveHeight();

                var rect = AdvancedAuctionHousePlugin.AuctionHouseUIRoot.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(MainUIWidth, MainUIHeight);
                rect.anchoredPosition = new Vector2(-MainUIWidth / 2f, -MainUIHeight / 2f);
            }
        }
    }
    
    public static bool IsAuctionHouseWindowOpen()
    {
        return AdvancedAuctionHousePlugin.AuctionHouseUIRoot != null && AdvancedAuctionHousePlugin.AuctionHouseUIRoot.activeSelf;
    }
}