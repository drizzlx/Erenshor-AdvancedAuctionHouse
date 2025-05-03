using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AdvancedAuctionHouse;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class AdvancedAuctionHousePlugin : BaseUnityPlugin
{
    public static AdvancedAuctionHousePlugin Instance { get; private set; }
    
    private Harmony _harmony;
    
    private GameObject _auctionHouseUIRoot;
    private GameObject _listingPanelRoot;
    private GameObject _weaponsClassSubPanel;
    private GameObject _weaponsTypeSubPanel;
    private GameObject _armorClassSubPanel;
    private GameObject _armorTypeSubPanel;
    
    public static Vector2 SavedAuctionHouseSize;
    public static Vector2 SavedAuctionHousePosition;
    
    private readonly float _auctionHouseUIWidth = 1000f;
    private readonly float _auctionHouseUIHeight = 500f;
    
    private readonly int _pageCount = 9999;
    
    private string _selectedClassName;
    private Class _selectedClass;
    private readonly string _arcanistClassName = "Arcanist";
    private readonly string _duelistClassName = "Duelist";
    private readonly string _druidClassName = "Druid";
    private readonly string _paladinClassName = "Paladin";
    
    private bool _defaultLoaded = false;
    
    private string _assetDirectory;
    private Button _showAllButton;
    private ScrollRect _listingScrollRect;

    private readonly Color _leftRightPanelBackgroundColor = new Color(0.125f, 0.1137f, 0.102f, 1f);
    private readonly Color _borderColor = new Color(0.125f, 0.1137f, 0.102f, 1f);
    private readonly Color _scrollBarColor = new Color(0.235f, 0.2f, 0.165f, 1f);
    private readonly Color _buyPromptBorderColor = new Color(0.45f, 0.34f, 0.14f, 0.8f);
    private readonly Color _closeButtonColor = new Color(0.235f, 0.2f, 0.165f, 1f);
    
    private int _currentPage = 0;
    private const int _listingsPerPage = 30;
    private string _currentCategory = "Default";
    
    private readonly float _listingIconWidth = 40f;
    
    // Lazy loading
    private int _loadedPage = 0;
    private bool _isLoadingPage = false;
    private const float _scrollThreshold = 0.25f; // near bottom
    
    private Button _buyButton;
    private Color _buyButtonEnabledTextColor = new Color(0.8f, 0.75f, 0.3f, 1f);
    private Color _buyButtonDisabledTextColor = new Color(0.5f, 0.5f, 0.5f, 0.75f);
    private GameObject _confirmPanel;
    private Button _yesButton;
    private Button _noButton;

    private Text _buyPromptText;
    
    private AuctionHouseListing _selectedAuctionHouseListing;
        
    private void Awake()
    {
        var dllPath = Info.Location;
        
        if (dllPath != null)
        {
            // Get asset path dynamically
            _assetDirectory = Path.GetDirectoryName(dllPath);
        }
        else
        {
            // Fallback
            _assetDirectory = Path.Combine(Paths.PluginPath, "drizzlx-ErenshorAdvancedAuctionHouse");
        }
        
        Instance = this;
        
        // Apply all Patches
        _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        
        _harmony.PatchAll();
    }
    
    private Texture2D LoadImageTexture(string assetPath)
    {
        if (!Directory.Exists(_assetDirectory))
        {
            Logger.LogError("Plugin directory not found " + _assetDirectory);

            return null;
        }
        
        if (!File.Exists(assetPath))
        {
            Logger.LogError("Texture not found " + assetPath);

            return null;
        }
        
        var data = File.ReadAllBytes(assetPath);
        
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

        if (!texture.LoadImage(data))
            Logger.LogError("Failed to load texture from " + assetPath);

        return texture;
    }

    private void OnDestroy()
    {
        // Destroy minimap UI root
        if (_auctionHouseUIRoot != null)
        {
            Destroy(_auctionHouseUIRoot);
            _auctionHouseUIRoot = null;
        }
        
        if (_listingPanelRoot != null)
        {
            Destroy(_listingPanelRoot);
            _listingPanelRoot = null;
        }
        
        if (_weaponsClassSubPanel != null)
        {
            Destroy(_weaponsClassSubPanel);
            _weaponsClassSubPanel = null;
        }
        
        if (_weaponsTypeSubPanel != null)
        {
            Destroy(_weaponsTypeSubPanel);
            _weaponsTypeSubPanel = null;
        }
        
        if (_armorClassSubPanel != null)
        {
            Destroy(_armorClassSubPanel);
            _armorClassSubPanel = null;
        }
        
        if (_armorTypeSubPanel != null)
        {
            Destroy(_armorTypeSubPanel);
            _armorTypeSubPanel = null;
        }
        
        _defaultLoaded = false;
        
        _harmony?.UnpatchSelf();
        _harmony = null;
    }
    
    private void OnScrollValueChanged(Vector2 scrollPos)
    {
        // Vertical scroll: 1 = top, 0 = bottom
        if (scrollPos.y <= _scrollThreshold)
        {
            LoadNextPage();
        }
    }

    public void CloseAuctionHouseUI()
    {
        if (_auctionHouseUIRoot != null)
            _auctionHouseUIRoot.SetActive(false);
        
        _defaultLoaded = false;
    }

    public void OpenAuctionHouseUI()
    {
        if (_auctionHouseUIRoot == null)
            CreateAuctionHouseUI("browse");

        if (_auctionHouseUIRoot != null)
        {
            GameData.PlayerAuctionItemsOpen = false;
            GameData.AuctionWindowOpen = true;
            _auctionHouseUIRoot.SetActive(true);
            
            if (!_defaultLoaded)
            {
                _showAllButton.onClick.Invoke();

                // Mark the button as selected in the UI
                EventSystem.current.SetSelectedGameObject(_showAllButton.gameObject);

                // Optionally force its visual color to selected
                var image = _showAllButton.GetComponent<Image>();
                image.color = Color.white;

                _defaultLoaded = true;
            }

        }
    }

    private void CreateAuctionHouseUI(string tab)
    {
        // === Canvas ===
        var canvasGo = new GameObject("AuctionHouseUICanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 0; // 0 for behind other UI elements
        
        // === Auction House Panel ===
        var panelGo = new GameObject("AuctionHouseUIPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        panelGo.transform.SetParent(canvasGo.transform, false);
        
        _auctionHouseUIRoot = panelGo;
        
        var rect = panelGo.GetComponent<RectTransform>();
        
        if (SavedAuctionHouseSize != Vector2.zero)
        {
            rect.sizeDelta = SavedAuctionHouseSize;
        }
        else
        {
            rect.sizeDelta = new Vector2(_auctionHouseUIWidth, _auctionHouseUIHeight);
        }
        
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        if (SavedAuctionHousePosition != Vector2.zero)
        {
            rect.anchoredPosition = SavedAuctionHousePosition;
        }
        else
        {
            rect.anchoredPosition = Vector2.zero;
        }
        
        var headerHeight = rect.sizeDelta.y * 0.1f;
        
        // === Border ===
        var borderGo = new GameObject("AuctionHouseUIBorder", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        borderGo.transform.SetParent(panelGo.transform, false);
        borderGo.transform.SetAsFirstSibling();
        
        var borderRect = borderGo.GetComponent<RectTransform>();
        borderRect.anchorMin = new Vector2(0f, 0f);
        borderRect.anchorMax = new Vector2(1f, 1f);
        borderRect.offsetMin = new Vector2(-5f, -5f); // Make border slightly outside
        borderRect.offsetMax = new Vector2(5f, 5f);
        borderRect.pivot = new Vector2(0.5f, 0.5f);

        // Set border image
        var borderImage = borderGo.GetComponent<Image>();
        borderImage.color = _borderColor;
        
        // === Top Header Panel ===
        var headerGo = new GameObject("AuctionHouseUIHeader", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        headerGo.transform.SetParent(panelGo.transform, false);

        var headerRect = headerGo.GetComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(1f, 1f);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.anchoredPosition = Vector2.zero;
        headerRect.sizeDelta = new Vector2(0f, headerHeight);

        var headerImage = headerGo.GetComponent<Image>();
        headerImage.color = _leftRightPanelBackgroundColor;
        
        // === Bottom Footer Panel ===
        var footerGo = new GameObject("AuctionHouseUIFooter", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        footerGo.transform.SetParent(panelGo.transform, false);

        var footerRect = footerGo.GetComponent<RectTransform>();
        footerRect.anchorMin = new Vector2(0f, 0f);
        footerRect.anchorMax = new Vector2(1f, 0f);
        footerRect.pivot = new Vector2(0.5f, 0f);
        footerRect.anchoredPosition = Vector2.zero;
        footerRect.sizeDelta = new Vector2(0f, headerHeight);

        var footerImage = footerGo.GetComponent<Image>();
        footerImage.color = _leftRightPanelBackgroundColor;

        // === Footer Buy Button ===
        var buyButtonGo = new GameObject("FooterBuyButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Button));
        buyButtonGo.transform.SetParent(footerGo.transform, false);

        _buyButton = buyButtonGo.GetComponent<Button>();
        SetButtonInteractable(_buyButton, false, _buyButtonEnabledTextColor, _buyButtonDisabledTextColor);

        var btnRect = buyButtonGo.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(1f, 0.5f);
        btnRect.anchorMax = new Vector2(1f, 0.5f);
        btnRect.pivot = new Vector2(1f, 0.5f);
        btnRect.sizeDelta = new Vector2(80f, headerHeight * 0.65f);
        btnRect.anchoredPosition = new Vector2(-80f + -40f, 0f);

        // === Background with Outline ===
        var bgGo = new GameObject("ButtonBackground", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        bgGo.transform.SetParent(buyButtonGo.transform, false);

        var bgRect = bgGo.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        var bgImage = bgGo.GetComponent<Image>();
        bgImage.color = Color.white;

        var bgOutline = bgGo.GetComponent<Outline>();
        bgOutline.effectColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        bgOutline.effectDistance = new Vector2(1f, -1f);

        // === Assign background as targetGraphic ===
        var button = buyButtonGo.GetComponent<Button>();
        button.targetGraphic = bgImage;
        
        var colors = button.colors;
        colors.normalColor = new Color(0.235f, 0.2f, 0.165f, 1f);
        colors.highlightedColor = new Color(0.314f, 0.263f, 0.212f, 1f);
        colors.pressedColor = new Color(0.165f, 0.141f, 0.122f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);   // Gray when disabled
        button.colors = colors;
        
        button.onClick.AddListener(() =>
        {
            if (_confirmPanel != null)
            {
                var item = _selectedAuctionHouseListing.Item;
                var price = _selectedAuctionHouseListing.Price;
                
                _buyPromptText.text = item.ItemName + " for " + price + "g";
                _confirmPanel.SetActive(true);
            }
        });

        // === Button Text ===
        var textGo = new GameObject("BuyButtonText", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textGo.transform.SetParent(buyButtonGo.transform, false);

        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        var txt = textGo.GetComponent<Text>();
        txt.text = "Buyout";
        txt.alignment = TextAnchor.MiddleCenter;
        txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        txt.color = Color.white;
        txt.fontSize = 18;
        
        // === Close Button ===
        var closeButtonGo = new GameObject("FooterCloseButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Button));
        closeButtonGo.transform.SetParent(footerGo.transform, false);

        var closeBtn = closeButtonGo.GetComponent<Button>();
        closeBtn.interactable = true;

        var closeBtnRect = closeButtonGo.GetComponent<RectTransform>();
        closeBtnRect.anchorMin = new Vector2(1f, 0.5f);
        closeBtnRect.anchorMax = new Vector2(1f, 0.5f);
        closeBtnRect.pivot = new Vector2(1f, 0.5f);
        closeBtnRect.sizeDelta = new Vector2(80f, headerHeight * 0.65f);
        closeBtnRect.anchoredPosition = new Vector2(-25f, 0f); // 25px from left

        // === Background with Outline ===
        var closeBgGo = new GameObject("ButtonBackground", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        closeBgGo.transform.SetParent(closeButtonGo.transform, false);

        var closeBgRect = closeBgGo.GetComponent<RectTransform>();
        closeBgRect.anchorMin = Vector2.zero;
        closeBgRect.anchorMax = Vector2.one;
        closeBgRect.offsetMin = Vector2.zero;
        closeBgRect.offsetMax = Vector2.zero;

        var closeBgImage = closeBgGo.GetComponent<Image>();
        closeBgImage.color = _closeButtonColor;

        var closeOutline = closeBgGo.GetComponent<Outline>();
        closeOutline.effectColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        closeOutline.effectDistance = new Vector2(1f, -1f);

        // Assign background
        closeBtn.targetGraphic = closeBgImage;
        closeBtn.onClick.AddListener(() =>
        {
            if (_auctionHouseUIRoot != null)
            {
                Destroy(_auctionHouseUIRoot);
                _auctionHouseUIRoot = null;
            }
        
            if (_listingPanelRoot != null)
            {
                Destroy(_listingPanelRoot);
                _listingPanelRoot = null;
            }
        
            if (_weaponsClassSubPanel != null)
            {
                Destroy(_weaponsClassSubPanel);
                _weaponsClassSubPanel = null;
            }
        
            if (_weaponsTypeSubPanel != null)
            {
                Destroy(_weaponsTypeSubPanel);
                _weaponsTypeSubPanel = null;
            }
        
            if (_armorClassSubPanel != null)
            {
                Destroy(_armorClassSubPanel);
                _armorClassSubPanel = null;
            }
        
            if (_armorTypeSubPanel != null)
            {
                Destroy(_armorTypeSubPanel);
                _armorTypeSubPanel = null;
            }
        
            _defaultLoaded = false;
        });

        // === Close Button Text ===
        var closeTextGo = new GameObject("CloseButtonText", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        closeTextGo.transform.SetParent(closeButtonGo.transform, false);

        var closeTextRect = closeTextGo.GetComponent<RectTransform>();
        closeTextRect.anchorMin = Vector2.zero;
        closeTextRect.anchorMax = Vector2.one;
        closeTextRect.offsetMin = Vector2.zero;
        closeTextRect.offsetMax = Vector2.zero;

        var closeTxt = closeTextGo.GetComponent<Text>();
        closeTxt.text = "Close";
        closeTxt.alignment = TextAnchor.MiddleCenter;
        closeTxt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        closeTxt.color = _buyButtonEnabledTextColor;
        closeTxt.fontSize = 18;
        
        // === Left Panel ===
        var leftPanelGo = new GameObject("AuctionHouseUILeftPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        leftPanelGo.transform.SetParent(panelGo.transform, false);
        
        // === Scroll Container for Left Panel ===
        var leftScrollRectGo = new GameObject("LeftScrollRect", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
        leftScrollRectGo.transform.SetParent(panelGo.transform, false);

        var leftScrollRect = leftScrollRectGo.GetComponent<ScrollRect>();
        leftScrollRect.horizontal = false;
        leftScrollRect.vertical = true;
        leftScrollRect.movementType = ScrollRect.MovementType.Clamped;
        leftScrollRect.scrollSensitivity = 50f;

        var leftScrollRectRect = leftScrollRectGo.GetComponent<RectTransform>();
        leftScrollRectRect.anchorMin = new Vector2(0f, 0f);
        leftScrollRectRect.anchorMax = new Vector2(0.25f, 1f);
        leftScrollRectRect.pivot = new Vector2(0f, 1f);
        leftScrollRectRect.offsetMin = new Vector2(0f, headerHeight);
        leftScrollRectRect.offsetMax = new Vector2(0f, -headerHeight);
        leftScrollRectGo.GetComponent<Image>().color = _leftRightPanelBackgroundColor;

        var leftViewportGo = new GameObject("LeftViewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
        leftViewportGo.transform.SetParent(leftScrollRectGo.transform, false);

        var leftViewportRect = leftViewportGo.GetComponent<RectTransform>();
        leftViewportRect.anchorMin = Vector2.zero;
        leftViewportRect.anchorMax = Vector2.one;
        leftViewportRect.offsetMin = Vector2.zero;
        leftViewportRect.offsetMax = Vector2.zero;

        var leftViewportImage = leftViewportGo.GetComponent<Image>();
        leftViewportImage.color = _leftRightPanelBackgroundColor;
        leftViewportImage.type = Image.Type.Sliced;
        
        leftPanelGo.transform.SetParent(leftViewportGo.transform, false);
        leftScrollRect.content = leftPanelGo.GetComponent<RectTransform>();
        
        var leftRect = leftPanelGo.GetComponent<RectTransform>();
        leftRect.anchorMin = new Vector2(0f, 0f);
        leftRect.anchorMax = new Vector2(1f, 1f);
        leftRect.pivot = new Vector2(0f, 1f);
        leftRect.offsetMin = new Vector2(0f, headerHeight);
        leftRect.offsetMax = new Vector2(0f, -headerHeight);
        
        leftScrollRect.content = leftRect;
        leftScrollRect.vertical = true;
        leftScrollRect.horizontal = false;
        leftScrollRect.movementType = ScrollRect.MovementType.Clamped;
        
        var leftImage = leftPanelGo.GetComponent<Image>();
        leftImage.color = _leftRightPanelBackgroundColor;
        
        var layoutGroup = leftPanelGo.AddComponent<VerticalLayoutGroup>();
        layoutGroup.childForceExpandWidth = true;   // Stretch width
        layoutGroup.childForceExpandHeight = false; // Don't force height (unless you want equal height buttons)
        layoutGroup.childAlignment = TextAnchor.UpperCenter;
        layoutGroup.spacing = 3f; // space between buttons
        layoutGroup.padding = new RectOffset(10, 0, 10, 10); // ← left, right, top, bottom
        
        var fitterLeft = leftPanelGo.AddComponent<ContentSizeFitter>();
        fitterLeft.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitterLeft.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        if (tab == "browse")
        {
            _showAllButton = CreateCategoryButton("View All Listings", leftPanelGo.transform);
        
            _showAllButton.onClick.AddListener(() =>
            {
                _cleanupAuctionHouse(0);
                ShowItemsByCategory("Default");
            });
        
            var weaponsButton = CreateCategoryButton("Weapons", leftPanelGo.transform);
        
            weaponsButton.onClick.AddListener(() =>
            {
                _cleanupAuctionHouse(0);
                ShowItemsByCategory("Weapon");
                ToggleWeaponsClassSubPanel(weaponsButton.transform.parent);
            });
        
            var armorButton = CreateCategoryButton("Armor", leftPanelGo.transform);
        
            armorButton.onClick.AddListener(() =>
            {
                _cleanupAuctionHouse(0);
                ShowItemsByCategory("Armor");
                ToggleArmorClassSubPanel(armorButton.transform.parent);
            });
        
            var miscButton = CreateCategoryButton("Miscellaneous", leftPanelGo.transform);
        
            miscButton.onClick.AddListener(() =>
            {
                _cleanupAuctionHouse(0);
                ShowItemsByCategory("General");
            });
        
            var auraButton = CreateCategoryButton("Auras", leftPanelGo.transform);
        
            auraButton.onClick.AddListener(() =>
            {
                _cleanupAuctionHouse(0);
                ShowItemsByCategory("Aura");
            });
        
            var charmButton = CreateCategoryButton("Charms", leftPanelGo.transform);
        
            charmButton.onClick.AddListener(() =>
            {
                _cleanupAuctionHouse(0);
                ShowItemsByCategory("Charm");
            });
        }
        
        // === Right Panel Root ===
        var rightPanelGo = new GameObject("AuctionHouseUIRightPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
        rightPanelGo.transform.SetParent(panelGo.transform, false);

        var rightRect = rightPanelGo.GetComponent<RectTransform>();
        rightRect.anchorMin = new Vector2(0.25f, 0f);
        rightRect.anchorMax = new Vector2(1f, 1f); // full height
        rightRect.pivot = new Vector2(0f, 1f);
        rightRect.offsetMin = new Vector2(0f, headerHeight + 5f);
        rightRect.offsetMax = new Vector2(0f, -headerHeight - 10f);

        rightPanelGo.GetComponent<Image>().color = _leftRightPanelBackgroundColor;

        var scrollRect = rightPanelGo.GetComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        
        // === Viewport ===
        var viewportGo = new GameObject("AuctionHouseUIViewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
        viewportGo.transform.SetParent(rightPanelGo.transform, false);
        
        var clickCatcher = viewportGo.AddComponent<ScrollClickCatcher>();
        clickCatcher.Plugin = this;
        clickCatcher.ScrollRect = _listingScrollRect;

        var viewportRect = viewportGo.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        viewportRect.pivot = new Vector2(0.5f, 0.5f);
        viewportRect.anchoredPosition = Vector2.zero;
        viewportRect.sizeDelta = Vector2.zero;

        var viewportImage = viewportGo.GetComponent<Image>();
        viewportImage.color = _leftRightPanelBackgroundColor;
        viewportImage.type = Image.Type.Sliced;

        scrollRect.viewport = viewportRect;
        
        // === Content panel ===
        var contentGo = new GameObject("AuctionHouseUIListingPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewportGo.transform, false);
        
        _listingPanelRoot = contentGo;

        var contentRect = contentGo.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0f, 0f);

        // Layout group for vertical stacking
        var layoutContent = contentGo.GetComponent<VerticalLayoutGroup>();
        layoutContent.childForceExpandWidth = true;
        layoutContent.childControlWidth = true;
        layoutContent.childForceExpandHeight = false;
        layoutContent.childAlignment = TextAnchor.UpperCenter;
        layoutContent.padding = new RectOffset(0, 14, 0, 0);
        layoutContent.spacing = 2f;

        // Automatically fit height based on children
        var fitter = contentGo.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        scrollRect.content = contentRect;
        
        // === Vertical Scrollbar ===
        var scrollbarGo = new GameObject("AuctionHouseUIScrollbar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Scrollbar));
        scrollbarGo.transform.SetParent(rightPanelGo.transform, false);

        var sbRect = scrollbarGo.GetComponent<RectTransform>();
        sbRect.anchorMin = new Vector2(1f, 0f);
        sbRect.anchorMax = new Vector2(1f, 1f);
        sbRect.pivot = new Vector2(1f, 0.5f);
        sbRect.anchoredPosition = Vector2.zero;
        sbRect.sizeDelta = new Vector2(20f, 0f);
        sbRect.offsetMin = new Vector2(-20f, 10f);
        sbRect.offsetMax = new Vector2(0f, 0f);

        scrollbarGo.GetComponent<Image>().color = _leftRightPanelBackgroundColor;

        var scrollbar = scrollbarGo.GetComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        // === Scrollbar Handle ===
        var handleGo = new GameObject("AuctionHouseUIHandle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        handleGo.transform.SetParent(scrollbarGo.transform, false);

        var handleRect = handleGo.GetComponent<RectTransform>();
        handleRect.anchorMin = new Vector2(0f, 0f);
        handleRect.anchorMax = new Vector2(1f, 1f);
        handleRect.pivot = new Vector2(0.5f, 0.5f);
        handleRect.anchoredPosition = Vector2.zero;
        handleRect.sizeDelta = Vector2.zero; // Let Scrollbar control size

        var handleImage = handleGo.GetComponent<Image>();
        handleImage.color = _scrollBarColor;

        scrollbar.targetGraphic = handleImage;
        scrollbar.handleRect = handleRect;

        // Connect scrollbar to scrollRect
        scrollRect.verticalScrollbar = scrollbar;
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        scrollRect.scrollSensitivity = 50f;
        _listingScrollRect = scrollRect;
        _listingScrollRect.onValueChanged.AddListener(OnScrollValueChanged);
        
        // === Confirmation Dialog Panel ===
        _confirmPanel = new GameObject("ConfirmBuyPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _confirmPanel.transform.SetParent(_auctionHouseUIRoot.transform, false);

        var confirmRect = _confirmPanel.GetComponent<RectTransform>();
        confirmRect.sizeDelta = new Vector2(300f, 150f);
        confirmRect.anchorMin = new Vector2(0.5f, 0.5f);
        confirmRect.anchorMax = new Vector2(0.5f, 0.5f);
        confirmRect.pivot = new Vector2(0.5f, 0.5f);
        confirmRect.anchoredPosition = Vector2.zero;

        var confirmImage = _confirmPanel.GetComponent<Image>();
        confirmImage.color = _leftRightPanelBackgroundColor;
        
        var confirmOutline = _confirmPanel.AddComponent<Outline>();
        confirmOutline.effectColor = _buyPromptBorderColor;
        confirmOutline.effectDistance = new Vector2(2f, -2f);
        
        _confirmPanel.SetActive(false); // Hide by default

        var msgTextGo = new GameObject("ConfirmMessage", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        msgTextGo.transform.SetParent(_confirmPanel.transform, false);

        var msgTextRect = msgTextGo.GetComponent<RectTransform>();
        msgTextRect.anchorMin = new Vector2(0f, 0.5f);
        msgTextRect.anchorMax = new Vector2(1f, 1f);
        msgTextRect.offsetMin = new Vector2(10f, -10f);
        msgTextRect.offsetMax = new Vector2(-10f, -50f);

        _buyPromptText = msgTextGo.GetComponent<Text>();
        _buyPromptText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        _buyPromptText.fontSize = 16;
        _buyPromptText.color = Color.white;
        _buyPromptText.alignment = TextAnchor.MiddleCenter;
        _buyPromptText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _buyPromptText.verticalOverflow = VerticalWrapMode.Overflow;

        _yesButton = CreateConfirmButton("YesButton", "Confirm", _confirmPanel.transform, new Vector2(-60f, 15f), () =>
        {
            _confirmPanel.SetActive(false);
            BuyItem();
        });

        _noButton = CreateConfirmButton("NoButton", "Cancel", _confirmPanel.transform, new Vector2(60f, 15f), () =>
        {
            _confirmPanel.SetActive(false);
        });
    }
    
    void SetButtonInteractable(Button button, bool state, Color enabledTextColor, Color disabledTextColor)
    {
        button.interactable = state;
        var txt = button.GetComponentInChildren<Text>();
        if (txt != null)
            txt.color = state ? enabledTextColor : disabledTextColor;
    }

    private void BuyItem()
    {
        // TEST REMOVE ME
        GameData.PlayerInv.Gold += _selectedAuctionHouseListing.Price;

        GameData.AHUI.BuyItem();

        // === Remove the listing row from UI ===
        if (_selectedAuctionHouseListing != null && _selectedAuctionHouseListing.ItemIcon != null)
        {
            var row = _selectedAuctionHouseListing.ItemIcon.transform;

            // Walk up to the ListingRow, which is inside ListingBorderWrapper
            while (row != null && row.name != "ListingRow")
            {
                row = row.parent;
            }

            if (row != null)
            {
                var wrapper = row.parent;
                if (wrapper != null)
                {
                    GameObject.Destroy(wrapper.gameObject);
                }
            }

            _selectedAuctionHouseListing = null;
            SetButtonInteractable(_buyButton, false, _buyButtonEnabledTextColor, _buyButtonDisabledTextColor);
        }
    }
    
    private Button CreateConfirmButton(string name, string label, Transform parent, Vector2 anchoredPos, Action onClick)
    {
        var btnGo = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnGo.transform.SetParent(parent, false);

        var btnRect = btnGo.GetComponent<RectTransform>();
        btnRect.sizeDelta = new Vector2(100f, 35f);
        btnRect.anchorMin = btnRect.anchorMax = new Vector2(0.5f, 0f);
        btnRect.pivot = new Vector2(0.5f, 0f);
        btnRect.anchoredPosition = anchoredPos;

        var img = btnGo.GetComponent<Image>();
        img.color = name == "YesButton" ? new Color(0.2f, 0.8f, 0.2f, 0.7f) : new Color(0.8f, 0.2f, 0.2f, 0.7f);
        
        var outline = btnGo.AddComponent<Outline>();
        outline.effectColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        outline.effectDistance = new Vector2(1f, -1f);

        var btn = btnGo.GetComponent<Button>();
        btn.onClick.AddListener(() => onClick());

        var txtGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        txtGo.transform.SetParent(btnGo.transform, false);

        var txtRect = txtGo.GetComponent<RectTransform>();
        txtRect.anchorMin = Vector2.zero;
        txtRect.anchorMax = Vector2.one;
        txtRect.offsetMin = Vector2.zero;
        txtRect.offsetMax = Vector2.zero;

        var txt = txtGo.GetComponent<Text>();
        txt.text = label;
        txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        txt.fontSize = 16;
        txt.color = Color.black;
        txt.alignment = TextAnchor.MiddleCenter;

        return btn;
    }
    
    private void ToggleWeaponsClassSubPanel(Transform leftPanel)
    {
        // === Step 1: If not built, delay and try again next frame ===
        if (_weaponsClassSubPanel == null)
        {
            _weaponsClassSubPanel = new GameObject("WeaponsClassSubPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(VerticalLayoutGroup));

            var rect = _weaponsClassSubPanel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0, 80);

            var layout = _weaponsClassSubPanel.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = 3f;

            var layoutElement = _weaponsClassSubPanel.AddComponent<LayoutElement>();
            layoutElement.flexibleWidth = 1f;

            // Add subcategory buttons
            var arcanist = CreateClassSubCategoryButton(_arcanistClassName, _weaponsClassSubPanel.transform);
            arcanist.onClick.AddListener(() =>
            {
                _cleanupAuctionHouse(1);
                
                _selectedClassName = _arcanistClassName;
                _selectedClass = GameData.ClassDB.Arcanist;
                
                ShowItemsByCategory("Weapon");
                ToggleWeaponsTypeSubPanel(arcanist.transform.parent);
            });

            var duelist = CreateClassSubCategoryButton(_duelistClassName, _weaponsClassSubPanel.transform);
            duelist.onClick.AddListener(() =>
            {
                _cleanupAuctionHouse(1);
                
                _selectedClassName = _duelistClassName;
                _selectedClass = GameData.ClassDB.Duelist;
                
                ShowItemsByCategory("Weapon");
                ToggleWeaponsTypeSubPanel(duelist.transform.parent);
            });

            var druid = CreateClassSubCategoryButton(_druidClassName, _weaponsClassSubPanel.transform);
            druid.onClick.AddListener(() =>
            {
                _cleanupAuctionHouse(1);
                
                _selectedClassName = _druidClassName;
                _selectedClass = GameData.ClassDB.Druid;

                ShowItemsByCategory("Weapon");
                ToggleWeaponsTypeSubPanel(druid.transform.parent);
            });

            var paladin = CreateClassSubCategoryButton(_paladinClassName, _weaponsClassSubPanel.transform);
            paladin.onClick.AddListener(() =>
            {
                _cleanupAuctionHouse(1);
                
                _selectedClassName = _paladinClassName;
                _selectedClass = GameData.ClassDB.Warrior;

                ShowItemsByCategory("Weapon");
                ToggleWeaponsTypeSubPanel(paladin.transform.parent);
            });

            // Insert below Weapons button
            int index = -1;
            for (int i = 0; i < leftPanel.childCount; i++)
            {
                if (leftPanel.GetChild(i).name == "WeaponsCategoryButton")
                {
                    index = i;
                    break;
                }
            }

            _weaponsClassSubPanel.transform.SetParent(leftPanel, false);
            if (index >= 0)
            {
                _weaponsClassSubPanel.transform.SetSiblingIndex(index + 1);
            }

            // Delay activation by one frame to let layout process
            StartCoroutine(ActivateWeaponsClassSubPanelNextFrame());
            return;
        }

        // === Step 2: Already built, just toggle visibility ===
        _weaponsClassSubPanel.SetActive(!_weaponsClassSubPanel.activeSelf);
    }
    
    private void ToggleWeaponsTypeSubPanel(Transform leftPanel)
    {
        if (_weaponsTypeSubPanel == null)
        {
            _weaponsTypeSubPanel = new GameObject("WeaponsTypeSubPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(VerticalLayoutGroup));
        
            var rect = _weaponsTypeSubPanel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0, 80);

            var layout = _weaponsTypeSubPanel.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = 3f;

            var layoutElement = _weaponsTypeSubPanel.AddComponent<LayoutElement>();
            layoutElement.flexibleWidth = 1f;

            // Add subcategory buttons
            var oneHandedMelee = CreateTypeSubCategoryButton("One-Handed Melee", _weaponsTypeSubPanel.transform);
            oneHandedMelee.onClick.AddListener(() =>
            {
                if (_buyButton != null)
                    SetButtonInteractable(_buyButton, false, _buyButtonEnabledTextColor, _buyButtonDisabledTextColor);
                
                ShowItemsByCategory("OneHandMelee");
            });

            var twoHandedMelee = CreateTypeSubCategoryButton("Two-Handed Melee", _weaponsTypeSubPanel.transform);
            twoHandedMelee.onClick.AddListener(() =>
            {
                if (_buyButton != null)
                    SetButtonInteractable(_buyButton, false, _buyButtonEnabledTextColor, _buyButtonDisabledTextColor);
                
                ShowItemsByCategory("TwoHandMelee");
            });
            
            var oneHandedDaggers = CreateTypeSubCategoryButton("One-Handed Daggers", _weaponsTypeSubPanel.transform);
            oneHandedDaggers.onClick.AddListener(() =>
            {
                if (_buyButton != null)
                    SetButtonInteractable(_buyButton, false, _buyButtonEnabledTextColor, _buyButtonDisabledTextColor);
                
                ShowItemsByCategory("OneHandDagger");
            });
            
            var twoHandedStaffs = CreateTypeSubCategoryButton("Two-Handed Staffs", _weaponsTypeSubPanel.transform);
            twoHandedStaffs.onClick.AddListener(() =>
            {
                if (_buyButton != null)
                    SetButtonInteractable(_buyButton, false, _buyButtonEnabledTextColor, _buyButtonDisabledTextColor);
                
                ShowItemsByCategory("TwoHandStaff");
            });
            
            int index = -1;
            for (int i = 0; i < leftPanel.childCount; i++)
            {
                if (leftPanel.GetChild(i).name.StartsWith(_selectedClassName) && leftPanel.GetChild(i).name.EndsWith("ClassSubCategoryButton"))
                {
                    index = i;
                    break;
                }
            }

            _weaponsTypeSubPanel.transform.SetParent(leftPanel, false);
            if (index >= 0)
            {
                _weaponsTypeSubPanel.transform.SetSiblingIndex(index + 1);
            }
            
            // Delay activation by one frame to let layout process
            StartCoroutine(ActivateWeaponsSubPanelNextFrame());
            return;
        }

        _weaponsTypeSubPanel.SetActive(!_weaponsTypeSubPanel.activeSelf);
    }
    
    private void ToggleArmorClassSubPanel(Transform leftPanel)
    {
        if (_armorClassSubPanel == null)
        {
            _armorClassSubPanel = new GameObject("ArmorClassSubPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(VerticalLayoutGroup));
        
            var rect = _armorClassSubPanel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0, 80);

            var layout = _armorClassSubPanel.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = 3f;

            var layoutElement = _armorClassSubPanel.AddComponent<LayoutElement>();
            layoutElement.flexibleWidth = 1f;

            // Add subcategory buttons
            var arcanist = CreateClassSubCategoryButton(_arcanistClassName, _armorClassSubPanel.transform);
            arcanist.onClick.AddListener(() =>
            {
                _cleanupAuctionHouse(1);
                
                _selectedClassName = _arcanistClassName;
                _selectedClass = GameData.ClassDB.Arcanist;
                
                ShowItemsByCategory("Armor");
                ToggleArmorTypeSubPanel(arcanist.transform.parent);
            });

            var duelist = CreateClassSubCategoryButton(_duelistClassName, _armorClassSubPanel.transform);
            duelist.onClick.AddListener(() =>
            {
                _cleanupAuctionHouse(1);
                
                _selectedClassName = _duelistClassName;
                _selectedClass = GameData.ClassDB.Duelist;
                
                ShowItemsByCategory("Armor");
                ToggleArmorTypeSubPanel(duelist.transform.parent);
            });
            
            var druid = CreateClassSubCategoryButton(_druidClassName, _armorClassSubPanel.transform);
            druid.onClick.AddListener(() =>
            {
                _cleanupAuctionHouse(1);
                
                _selectedClassName = _druidClassName;
                _selectedClass = GameData.ClassDB.Druid;
                
                ShowItemsByCategory("Armor");
                ToggleArmorTypeSubPanel(druid.transform.parent);
            });
            
            var paladin = CreateClassSubCategoryButton(_paladinClassName, _armorClassSubPanel.transform);
            paladin.onClick.AddListener(() =>
            {
                _cleanupAuctionHouse(1);
                
                _selectedClassName = _paladinClassName;
                _selectedClass = GameData.ClassDB.Warrior;
                
                ShowItemsByCategory("Armor");
                ToggleArmorTypeSubPanel(paladin.transform.parent);
            });
            
            int index = -1;
            for (int i = 0; i < leftPanel.childCount; i++)
            {
                if (leftPanel.GetChild(i).name == "ArmorCategoryButton")
                {
                    index = i;
                    break;
                }
            }

            _armorClassSubPanel.transform.SetParent(leftPanel, false);
            if (index >= 0)
            {
                _armorClassSubPanel.transform.SetSiblingIndex(index + 1);
            }
            
            // Delay activation by one frame to let layout process
            StartCoroutine(ActivateArmorClassSubPanelNextFrame());
            return;
        }

        _armorClassSubPanel.SetActive(!_armorClassSubPanel.activeSelf);
    }
    
    private void ToggleArmorTypeSubPanel(Transform leftPanel)
    {
        if (_armorTypeSubPanel == null)
        {
            _armorTypeSubPanel = new GameObject("ArmorTypeSubPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(VerticalLayoutGroup));
        
            var rect = _armorTypeSubPanel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0, 80);

            var layout = _armorTypeSubPanel.GetComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = 3f;

            var layoutElement = _armorTypeSubPanel.AddComponent<LayoutElement>();
            layoutElement.flexibleWidth = 1f;

            // Add subcategory buttons
            var head = CreateTypeSubCategoryButton("Head", _armorTypeSubPanel.transform);
            head.onClick.AddListener(() =>
            {
                if (_buyButton != null)
                    SetButtonInteractable(_buyButton, false, _buyButtonEnabledTextColor, _buyButtonDisabledTextColor);
                
                ShowItemsByCategory("ArmorHead");
            });

            var neck = CreateTypeSubCategoryButton("Neck", _armorTypeSubPanel.transform);
            neck.onClick.AddListener(() =>
            {
                if (_buyButton != null)
                    SetButtonInteractable(_buyButton, false, _buyButtonEnabledTextColor, _buyButtonDisabledTextColor);
                
                ShowItemsByCategory("ArmorNeck");
            });
            
            var chest = CreateTypeSubCategoryButton("Chest", _armorTypeSubPanel.transform);
            chest.onClick.AddListener(() =>
            {
                if (_buyButton != null)
                    SetButtonInteractable(_buyButton, false, _buyButtonEnabledTextColor, _buyButtonDisabledTextColor);
                
                ShowItemsByCategory("ArmorChest");
            });
            
            var shoulder = CreateTypeSubCategoryButton("Shoulder", _armorTypeSubPanel.transform);
            shoulder.onClick.AddListener(() =>
            {
                if (_buyButton != null)
                    SetButtonInteractable(_buyButton, false, _buyButtonEnabledTextColor, _buyButtonDisabledTextColor);
                
                ShowItemsByCategory("ArmorShoulder");
            });
            
            var arm = CreateTypeSubCategoryButton("Arm", _armorTypeSubPanel.transform);
            arm.onClick.AddListener(() =>
            {
                if (_buyButton != null)
                    SetButtonInteractable(_buyButton, false, _buyButtonEnabledTextColor, _buyButtonDisabledTextColor);
                
                ShowItemsByCategory("ArmorArm");
            });
            
            var bracer = CreateTypeSubCategoryButton("Bracer", _armorTypeSubPanel.transform);
            bracer.onClick.AddListener(() =>
            {
                if (_buyButton != null)
                    SetButtonInteractable(_buyButton, false, _buyButtonEnabledTextColor, _buyButtonDisabledTextColor);
                
                ShowItemsByCategory("ArmorBracer");
            });
            
            var ring = CreateTypeSubCategoryButton("Ring", _armorTypeSubPanel.transform);
            ring.onClick.AddListener(() =>
            {
                if (_buyButton != null)
                    SetButtonInteractable(_buyButton, false, _buyButtonEnabledTextColor, _buyButtonDisabledTextColor);
                
                ShowItemsByCategory("ArmorRing");
            });
            
            var hand = CreateTypeSubCategoryButton("Hand", _armorTypeSubPanel.transform);
            hand.onClick.AddListener(() =>
            {
                if (_buyButton != null)
                    SetButtonInteractable(_buyButton, false, _buyButtonEnabledTextColor, _buyButtonDisabledTextColor);
                
                ShowItemsByCategory("ArmorHand");
            });
            
            var foot = CreateTypeSubCategoryButton("Foot", _armorTypeSubPanel.transform);
            foot.onClick.AddListener(() =>
            {
                if (_buyButton != null)
                    SetButtonInteractable(_buyButton, false, _buyButtonEnabledTextColor, _buyButtonDisabledTextColor);
                
                ShowItemsByCategory("ArmorFoot");
            });
            
            var leg = CreateTypeSubCategoryButton("Leg", _armorTypeSubPanel.transform);
            leg.onClick.AddListener(() =>
            {
                if (_buyButton != null)
                    SetButtonInteractable(_buyButton, false, _buyButtonEnabledTextColor, _buyButtonDisabledTextColor);
                
                ShowItemsByCategory("ArmorLeg");
            });
            
            var back = CreateTypeSubCategoryButton("Back", _armorTypeSubPanel.transform);
            back.onClick.AddListener(() =>
            {
                if (_buyButton != null)
                    SetButtonInteractable(_buyButton, false, _buyButtonEnabledTextColor, _buyButtonDisabledTextColor);
                
                ShowItemsByCategory("ArmorBack");
            });
            
            var waist = CreateTypeSubCategoryButton("Waist", _armorTypeSubPanel.transform);
            waist.onClick.AddListener(() =>
            {
                if (_buyButton != null)
                    SetButtonInteractable(_buyButton, false, _buyButtonEnabledTextColor, _buyButtonDisabledTextColor);
                
                ShowItemsByCategory("ArmorWaist");
            });
            
            int index = -1;
            for (int i = 0; i < leftPanel.childCount; i++)
            {
                if (leftPanel.GetChild(i).name.StartsWith(_selectedClassName) && leftPanel.GetChild(i).name.EndsWith("ClassSubCategoryButton"))
                {
                    index = i;
                    break;
                }
            }

            _armorTypeSubPanel.transform.SetParent(leftPanel, false);
            if (index >= 0)
            {
                _armorTypeSubPanel.transform.SetSiblingIndex(index + 1);
            }
            
            // Delay activation by one frame to let layout process
            StartCoroutine(ActivateArmorSubPanelNextFrame());
            return;
        }

        _armorTypeSubPanel.SetActive(!_armorTypeSubPanel.activeSelf);
    }
    
    private IEnumerator ActivateWeaponsClassSubPanelNextFrame()
    {
        yield return new WaitForEndOfFrame();
        _weaponsClassSubPanel.SetActive(true);
    }
    
    private IEnumerator ActivateWeaponsSubPanelNextFrame()
    {
        yield return new WaitForEndOfFrame();
        _weaponsTypeSubPanel.SetActive(true);
    }
    
    private IEnumerator ActivateArmorClassSubPanelNextFrame()
    {
        yield return new WaitForEndOfFrame();
        _armorClassSubPanel.SetActive(true);
    }
    
    private IEnumerator ActivateArmorSubPanelNextFrame()
    {
        yield return new WaitForEndOfFrame();
        _armorTypeSubPanel.SetActive(true);
    }

    private void _cleanupAuctionHouse(int level)
    {
        if (_listingScrollRect != null)
        {
            _listingScrollRect.verticalNormalizedPosition = 1f;
        }
        
        _selectedClassName = null;
        _selectedClass = null;
        
        if (_buyButton != null)
            SetButtonInteractable(_buyButton, false, _buyButtonEnabledTextColor, _buyButtonDisabledTextColor);
        
        // Clean all
        if (level == 0)
        {
            if (_weaponsClassSubPanel != null)
            {
                Destroy(_weaponsClassSubPanel);
                _weaponsClassSubPanel = null;
            }
        
            if (_weaponsTypeSubPanel != null)
            {
                Destroy(_weaponsTypeSubPanel);
                _weaponsTypeSubPanel = null;
            }
        
            if (_armorClassSubPanel != null)
            {
                Destroy(_armorClassSubPanel);
                _armorClassSubPanel = null;
            }
        
            if (_armorTypeSubPanel != null)
            {
                Destroy(_armorTypeSubPanel);
                _armorTypeSubPanel = null;
            }
        }
        
        // Clean nested 1
        if (level == 1)
        {
            if (_weaponsTypeSubPanel != null)
            {
                Destroy(_weaponsTypeSubPanel);
                _weaponsTypeSubPanel = null;
            }
        
            if (_armorTypeSubPanel != null)
            {
                Destroy(_armorTypeSubPanel);
                _armorTypeSubPanel = null;
            }
        }
    }
    
    private Vector4 GetColumnWidths()
    {
        if (_listingPanelRoot != null)
        {
            var totalWidth = _listingPanelRoot.GetComponent<RectTransform>().rect.width;

            if (totalWidth <= 0f) totalWidth = _auctionHouseUIWidth * 0.75f;

            var itemNameWidth = totalWidth * 0.40f;
            var levelWidth = totalWidth * 0.10f;
            var sellerWidth = totalWidth * 0.25f;
            var priceWidth = totalWidth * 0.25f;

            return new Vector4(itemNameWidth, levelWidth, sellerWidth, priceWidth);
        }
        
        return Vector4.zero;
    }
    
    private void CreateListingHeaderRow(Transform parent)
    {
        // === Border Wrapper ===
        var borderWrapper = new GameObject("HeaderBorderWrapper", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        borderWrapper.transform.SetParent(parent, false);

        var wrapperRect = borderWrapper.GetComponent<RectTransform>();
        wrapperRect.sizeDelta = new Vector2(0f, _listingIconWidth + 10f); // slightly taller for border padding
        wrapperRect.anchorMin = new Vector2(0f, 1f);
        wrapperRect.anchorMax = new Vector2(1f, 1f);
        wrapperRect.pivot = new Vector2(0.5f, 1f);

        var wrapperImage = borderWrapper.GetComponent<Image>();
        wrapperImage.color = new Color(1f, 1f, 1f, 0.1f); // soft border color

        var wrapperLayout = borderWrapper.AddComponent<VerticalLayoutGroup>();
        wrapperLayout.childAlignment = TextAnchor.MiddleCenter;
        wrapperLayout.padding = new RectOffset(1, 1, 1, 1); // border thickness
        wrapperLayout.childForceExpandHeight = false;
        wrapperLayout.childForceExpandWidth = true;

        var wrapperFitter = borderWrapper.AddComponent<ContentSizeFitter>();
        wrapperFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // === Actual Header Row ===
        var rowGo = new GameObject("ListingHeaderRow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        rowGo.transform.SetParent(borderWrapper.transform, false);

        var rect = rowGo.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(0f, _listingIconWidth);

        var bg = rowGo.GetComponent<Image>();
        bg.color = new Color(0.1f, 0.1f, 0.1f, 0.7f);

        var layoutElement = rowGo.AddComponent<LayoutElement>();
        layoutElement.minHeight = _listingIconWidth;
        layoutElement.flexibleWidth = 1f;

        var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
        layout.childForceExpandHeight = true;
        layout.childForceExpandWidth = false;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.spacing = 10f;

        // === Dummy icon spacer for alignment ===
        var iconDummy = new GameObject("IconHeaderSpacer", typeof(RectTransform), typeof(CanvasRenderer), typeof(LayoutElement));
        iconDummy.transform.SetParent(rowGo.transform, false);

        var iconElement = iconDummy.GetComponent<LayoutElement>();
        iconElement.preferredWidth = _listingIconWidth + 5f; // match icon + spacer width
        iconElement.flexibleWidth = 0f;

        var width = GetColumnWidths();

        AddText("Item", rowGo.transform, width.x);
        AddText("Lvl", rowGo.transform, width.y);
        AddText("Seller", rowGo.transform, width.z);
        AddText("Price", rowGo.transform, width.w);
    }
    
    private void CreateListingRow(AuctionHouseListing listing, Transform parent)
    {
        // === Border Wrapper ===
        var borderWrapper = new GameObject("ListingBorderWrapper", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        borderWrapper.transform.SetParent(parent, false);

        var wrapperRect = borderWrapper.GetComponent<RectTransform>();
        wrapperRect.sizeDelta = new Vector2(0f, 60f); // slightly taller to show border
        wrapperRect.anchorMin = new Vector2(0f, 1f);
        wrapperRect.anchorMax = new Vector2(1f, 1f);
        wrapperRect.pivot = new Vector2(0.5f, 1f);

        var wrapperImage = borderWrapper.GetComponent<Image>();
        wrapperImage.color = new Color(1f, 1f, 1f, 0.1f); // light semi-transparent border

        var wrapperLayout = borderWrapper.AddComponent<VerticalLayoutGroup>();
        wrapperLayout.childAlignment = TextAnchor.MiddleCenter;
        wrapperLayout.padding = new RectOffset(1, 1, 1, 1); // border thickness
        wrapperLayout.childForceExpandHeight = false;
        wrapperLayout.childForceExpandWidth = true;

        var wrapperFitter = borderWrapper.AddComponent<ContentSizeFitter>();
        wrapperFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // === Listing Row (moved under wrapper) ===
        var rowGo = new GameObject("ListingRow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        rowGo.transform.SetParent(borderWrapper.transform, false);

        var rect = rowGo.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(0f, 50f);

        var image = rowGo.GetComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 1f); // Normal

        var layoutElement = rowGo.AddComponent<LayoutElement>();
        layoutElement.minHeight = 50f;
        layoutElement.flexibleWidth = 1f;

        var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
        layout.childForceExpandHeight = true;
        layout.childForceExpandWidth = false;
        layout.childControlWidth = true;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.spacing = 1f;
        layout.padding = new RectOffset(5, 5, 5, 5);
        
        // === Container for icon with required parent image ===
        var iconContainer = new GameObject("IconContainer", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        iconContainer.transform.SetParent(rowGo.transform, false);
        
        var containerLayout = iconContainer.AddComponent<LayoutElement>();
        containerLayout.preferredWidth = _listingIconWidth;
        containerLayout.preferredHeight = _listingIconWidth;

        var containerImage = iconContainer.GetComponent<Image>();
        containerImage.color = new Color(0f, 0f, 0f, 0f); // fully transparent
        
        // === Spacer between icon and item name ===
        var spacer = new GameObject("IconSpacer", typeof(RectTransform), typeof(CanvasRenderer), typeof(LayoutElement));
        spacer.transform.SetParent(rowGo.transform, false);

        var spacerElement = spacer.GetComponent<LayoutElement>();
        spacerElement.preferredWidth = 10f; // tweak spacing as needed
        spacerElement.flexibleWidth = 0f;

        var iconGo = new GameObject("ItemIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ItemIcon));
        iconGo.transform.SetParent(iconContainer.transform, false); // << Important: parent is the container

        var iconRect = iconGo.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0f, 0f);
        iconRect.anchorMax = new Vector2(1f, 1f);
        iconRect.offsetMin = Vector2.zero;
        iconRect.offsetMax = Vector2.zero;

        var iconImage = iconGo.GetComponent<Image>();
        iconImage.sprite = listing.Item.ItemIcon;
        iconImage.color = Color.white;
        iconImage.preserveAspect = true;

        var itemIcon = iconGo.GetComponent<ItemIcon>();
        itemIcon.MyItem = listing.Item;
        itemIcon.Quantity = listing.Quantity;
        itemIcon.VendorSlot = true;
        itemIcon.ForceInitInv();    // Safe now
        itemIcon.UpdateSlotImage();

        listing.ItemIcon = itemIcon;
        
        var width = GetColumnWidths();

        AddText($"{listing.Item.ItemName}", rowGo.transform, width.x);
        AddText($"{listing.Item.ItemLevel}", rowGo.transform, width.y);
        AddText($"{listing.SellerName}", rowGo.transform, width.z);
        AddText($"{listing.Price}g", rowGo.transform, width.w);

        // === Button click and highlighting ===
        var button = rowGo.GetComponent<Button>();
        var colors = button.colors;
        colors.normalColor = new Color(0.1f, 0.1f, 0.1f, 1f); // dark background
        colors.highlightedColor = colors.normalColor;
        colors.pressedColor = new Color(0.3f, 0.3f, 0.3f, 1f);
        colors.selectedColor = new Color(0.2f, 0.2f, 0.2f, 0.9f);;
        button.colors = colors;

        button.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
        
        button.onClick.AddListener(() =>
        {
            GameData.AHUI.CurrentSellerData = listing.SellerData;
            GameData.PlayerAud.PlayOneShot(GameData.Misc.Click, GameData.SFXVol);
            GameData.ActivateSlotForAuction(listing.ItemIcon);

            _selectedAuctionHouseListing = listing;
            
            if (_buyButton != null)
                _buyButton.interactable = true;

            SetButtonInteractable(_buyButton, true, _buyButtonEnabledTextColor, _buyButtonDisabledTextColor);
        });
    }

    private void AddText(string text, Transform parent, float preferredWidth)
    {
        var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textGo.transform.SetParent(parent, false);

        var txt = textGo.GetComponent<Text>();
        txt.text = text;
        txt.alignment = TextAnchor.MiddleLeft;
        txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        txt.color = Color.white;

        var rect = textGo.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(5f, 0f);  // small left padding
        rect.offsetMax = new Vector2(-5f, 0f); // right padding

        var layoutElement = textGo.AddComponent<LayoutElement>();
        layoutElement.preferredWidth = preferredWidth;
        layoutElement.flexibleWidth = 0f; // Prevent stretching
    }
    
    private Button CreateCategoryButton(string text, Transform parent)
    {
        var buttonGo = new GameObject(text + "CategoryButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonGo.transform.SetParent(parent, false);
        
        var shadow = buttonGo.AddComponent<UnityEngine.UI.Shadow>();
        shadow.effectColor = new Color(0.769f, 0.643f, 0.373f, 0.7f);
        shadow.effectDistance = new Vector2(1f, -1f);

        var rect = buttonGo.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0, 50f); // Width is flexible

        var image = buttonGo.GetComponent<Image>();
        image.color = Color.white;

        var button = buttonGo.GetComponent<Button>();
        button.targetGraphic = image;

        var colors = button.colors;
        colors.normalColor = new Color(0.235f, 0.2f, 0.165f, 1f);
        colors.highlightedColor = new Color(0.314f, 0.263f, 0.212f, 1f);
        colors.pressedColor = new Color(0.165f, 0.141f, 0.122f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.1f, 0.1f, 0.1f, 0.5f);
        button.colors = colors;

        // === IMPORTANT: LayoutElement ===
        var layoutElement = buttonGo.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = 40f;
        layoutElement.flexibleWidth = 1f; // Allow stretching horizontally

        // Add Text child
        var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textGo.transform.SetParent(buttonGo.transform, false);

        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        var txt = textGo.GetComponent<Text>();
        txt.text = text;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = _buyButtonEnabledTextColor;
        txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        return button;
    }
    
    private Button CreateClassSubCategoryButton(string text, Transform parent)
    {
        var buttonGo = new GameObject(text + "ClassSubCategoryButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonGo.transform.SetParent(parent, false);

        var rect = buttonGo.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0, 40f); // Width is flexible, height is 40

        var image = buttonGo.GetComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.2f, 0.8f); // dark semi-transparent background

        var button = buttonGo.GetComponent<Button>();

        // === IMPORTANT: LayoutElement ===
        var layoutElement = buttonGo.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = 40f;
        layoutElement.flexibleWidth = 1f; // Allow stretching horizontally

        // Add Text child
        var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textGo.transform.SetParent(buttonGo.transform, false);

        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        var txt = textGo.GetComponent<Text>();
        txt.text = text;
        txt.alignment = TextAnchor.MiddleLeft;
        textRect.offsetMin = new Vector2(20f, 0f);
        textRect.offsetMax = Vector2.zero;
        txt.color = Color.white;
        txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        return button;
    }
    
    private Button CreateTypeSubCategoryButton(string text, Transform parent)
    {
        var buttonGo = new GameObject(text + "TypeSubCategoryButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonGo.transform.SetParent(parent, false);

        var rect = buttonGo.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(0, 40f); // Width is flexible, height is 40

        var image = buttonGo.GetComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.2f, 0.8f); // dark semi-transparent background

        var button = buttonGo.GetComponent<Button>();

        // === IMPORTANT: LayoutElement ===
        var layoutElement = buttonGo.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = 40f;
        layoutElement.flexibleWidth = 1f; // Allow stretching horizontally

        // Add Text child
        var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textGo.transform.SetParent(buttonGo.transform, false);

        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        var txt = textGo.GetComponent<Text>();
        txt.text = text;
        txt.alignment = TextAnchor.MiddleLeft;
        textRect.offsetMin = new Vector2(40f, 0f);
        textRect.offsetMax = Vector2.zero;
        txt.color = Color.white;
        txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        return button;
    }
    
    public IEnumerator GetAuctionHouseDataAsync(
        Item.SlotType[] slotTypeRequest, 
        Item.WeaponType[] weaponTypeRequest, 
        int page,
        int perPage,
        Action<List<AuctionHouseListing>> onComplete)
    {
        var result = new List<AuctionHouseListing>();
        var seenNames = new HashSet<string>();

        int skipped = 0, taken = 0, frameBudget = 1000;
        int targetStart = page * perPage;

        foreach (var seller in AuctionHouse.AllData)
        {
            if (--frameBudget <= 0)
            {
                frameBudget = 1000;
                yield return null; // prevent hitching
            }

            if (seller == null || string.IsNullOrWhiteSpace(seller.SellerName))
                continue;

            if (seenNames.Contains(seller.SellerName))
                continue;

            seenNames.Add(seller.SellerName);

            if (skipped < targetStart)
            {
                skipped++;
                continue;
            }

            if (taken >= perPage)
                break;

            if (seller.SellerItems == null || seller.SellerItems.Count == 0)
                continue;

            foreach (var itemId in seller.SellerItems)
            {
                var item = GameData.ItemDB.GetItemByID(itemId);
                
                if (item == null)
                    continue;

                // Filtering
                if (slotTypeRequest.Any() && !slotTypeRequest.Contains(item.RequiredSlot))
                    continue;

                if (item.ThisWeaponType != Item.WeaponType.None && weaponTypeRequest.Any() &&
                    !weaponTypeRequest.Contains(item.ThisWeaponType))
                    continue;

                if (_selectedClass != null && !item.Classes.Contains(_selectedClass))
                {
                    continue;
                }

                var listing = new AuctionHouseListing
                {
                    SellerData = seller,
                    Item = item,
                    SellerName = seller.SellerName,
                    Price = Mathf.RoundToInt(item.ItemValue * 5f * seller.PriceMod)
                };

                result.Add(listing);

                if (result.Count >= perPage)
                    break;
            }

            taken++;
        }

        onComplete?.Invoke(result);
    }
    
    public IEnumerator LoadPagedSellerDataAsync(int page, int perPage, Action<Dictionary<string, AuctionHouseSave>> onComplete)
    {
        var result = new Dictionary<string, AuctionHouseSave>();
        var seenNames = new HashSet<string>();

        int skipped = 0, taken = 0, frameBudget = 1000;
        int targetStart = page * perPage;

        foreach (var sellerData in AuctionHouse.AllData)
        {
            if (--frameBudget <= 0)
            {
                frameBudget = 1000;
                yield return null; // Yield to prevent frame hitch
            }

            if (sellerData == null || string.IsNullOrWhiteSpace(sellerData.SellerName))
                continue;

            if (seenNames.Contains(sellerData.SellerName))
                continue;

            seenNames.Add(sellerData.SellerName);

            if (skipped < targetStart)
            {
                skipped++;
                continue;
            }

            result[sellerData.SellerName] = sellerData;
            if (++taken >= perPage)
                break;
        }

        onComplete?.Invoke(result);
    }

    private static Dictionary<string, AuctionHouseSave> GetVisibleSellerData(AuctionHouseUI auctionUI)
    {
        var map = new Dictionary<string, AuctionHouseSave>();

        foreach (var sellerText in auctionUI.Sellers)
        {
            string name = sellerText.text;

            if (string.IsNullOrWhiteSpace(name) || name == "No Seller")
                continue;

            if (name.Contains("("))
                name = name.Split('(')[0].Trim();

            AuctionHouseSave data = null;
            foreach (var entry in AuctionHouse.AllData)
            {
                if (entry != null && entry.SellerName == name)
                {
                    data = entry;
                    break;
                }
            }

            if (data != null)
                map[name] = data;
        }

        return map;
    }
    
    private void ShowItemsByCategory(string categoryName)
    {
        _currentCategory = categoryName;
        _loadedPage = 0;
        _isLoadingPage = false;

        // Reset scroll position
        if (_listingScrollRect != null)
            _listingScrollRect.verticalNormalizedPosition = 1f;

        // Clear existing listings
        foreach (Transform child in _listingPanelRoot.transform)
            Destroy(child.gameObject);

        LoadNextPage();
    }
    
    private void LoadNextPage()
    {
        if (_isLoadingPage) return;

        _isLoadingPage = true;

        StartCoroutine(GetListingsByCategoryAsync(_currentCategory, _loadedPage, _listingsPerPage, listings =>
        {
            if (_loadedPage == 0)
                CreateListingHeaderRow(_listingPanelRoot.transform); // Only on first load

            foreach (var listing in listings)
                CreateListingRow(listing, _listingPanelRoot.transform);

            _loadedPage++;
            _isLoadingPage = false;
        }));
    }
    
    private IEnumerator GetListingsByCategoryAsync(
    string categoryName,
    int page,
    int perPage,
    Action<List<AuctionHouseListing>> onComplete)
    {
        // Default to no filters
        Item.SlotType[] slotTypes = [];
        Item.WeaponType[] weaponTypes = [];

        switch (categoryName)
        {
            case "Default":
                break;

            case "Weapon":
                slotTypes = [Item.SlotType.Primary, Item.SlotType.Secondary, Item.SlotType.PrimaryOrSecondary];
                weaponTypes = [Item.WeaponType.OneHandDagger, Item.WeaponType.TwoHandMelee, Item.WeaponType.TwoHandStaff, Item.WeaponType.OneHandMelee];
                break;

            case "OneHandMelee":
                slotTypes = [Item.SlotType.Primary, Item.SlotType.Secondary, Item.SlotType.PrimaryOrSecondary];
                weaponTypes = [Item.WeaponType.OneHandMelee];
                break;

            case "TwoHandMelee":
                slotTypes = [Item.SlotType.Primary, Item.SlotType.Secondary, Item.SlotType.PrimaryOrSecondary];
                weaponTypes = [Item.WeaponType.TwoHandMelee];
                break;

            case "OneHandDagger":
                slotTypes = [Item.SlotType.Primary, Item.SlotType.Secondary, Item.SlotType.PrimaryOrSecondary];
                weaponTypes = [Item.WeaponType.OneHandDagger];
                break;

            case "TwoHandStaff":
                slotTypes = [Item.SlotType.Primary, Item.SlotType.Secondary, Item.SlotType.PrimaryOrSecondary];
                weaponTypes = [Item.WeaponType.TwoHandStaff];
                break;

            case "Armor":
                slotTypes = [
                    Item.SlotType.Head, Item.SlotType.Neck, Item.SlotType.Chest, Item.SlotType.Shoulder,
                    Item.SlotType.Arm, Item.SlotType.Bracer, Item.SlotType.Ring, Item.SlotType.Hand,
                    Item.SlotType.Foot, Item.SlotType.Leg, Item.SlotType.Back, Item.SlotType.Waist
                ];
                weaponTypes = [Item.WeaponType.None];
                break;

            case "ArmorHead": slotTypes = [Item.SlotType.Head]; weaponTypes = [Item.WeaponType.None]; break;
            case "ArmorNeck": slotTypes = [Item.SlotType.Neck]; weaponTypes = [Item.WeaponType.None]; break;
            case "ArmorChest": slotTypes = [Item.SlotType.Chest]; weaponTypes = [Item.WeaponType.None]; break;
            case "ArmorShoulder": slotTypes = [Item.SlotType.Shoulder]; weaponTypes = [Item.WeaponType.None]; break;
            case "ArmorArm": slotTypes = [Item.SlotType.Arm]; weaponTypes = [Item.WeaponType.None]; break;
            case "ArmorBracer": slotTypes = [Item.SlotType.Bracer]; weaponTypes = [Item.WeaponType.None]; break;
            case "ArmorRing": slotTypes = [Item.SlotType.Ring]; weaponTypes = [Item.WeaponType.None]; break;
            case "ArmorHand": slotTypes = [Item.SlotType.Hand]; weaponTypes = [Item.WeaponType.None]; break;
            case "ArmorFoot": slotTypes = [Item.SlotType.Foot]; weaponTypes = [Item.WeaponType.None]; break;
            case "ArmorLeg": slotTypes = [Item.SlotType.Leg]; weaponTypes = [Item.WeaponType.None]; break;
            case "ArmorBack": slotTypes = [Item.SlotType.Back]; weaponTypes = [Item.WeaponType.None]; break;
            case "ArmorWaist": slotTypes = [Item.SlotType.Waist]; weaponTypes = [Item.WeaponType.None]; break;

            case "General":
                slotTypes = [Item.SlotType.General];
                weaponTypes = [Item.WeaponType.None];
                break;

            case "Aura":
                slotTypes = [Item.SlotType.Aura];
                weaponTypes = [Item.WeaponType.None];
                break;

            case "Charm":
                slotTypes = [Item.SlotType.Charm];
                weaponTypes = [Item.WeaponType.None];
                break;
        }

        yield return GetAuctionHouseDataAsync(slotTypes, weaponTypes, page, perPage, onComplete);
    }
    
    public float GetScrollThreshold() => _scrollThreshold;

    public void TriggerManualPagination()
    {
        if (!_isLoadingPage)
            LoadNextPage();
    }
}

public class AuctionHouseListing
{
    public AuctionHouseSave SellerData;
    public Item Item;
    public ItemIcon ItemIcon;
    public readonly int Quantity = 1;
    public string SellerName;
    public int Price;
}

public class ScrollClickCatcher : MonoBehaviour, IPointerClickHandler
{
    public AdvancedAuctionHousePlugin Plugin;
    public ScrollRect ScrollRect;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (ScrollRect != null && Plugin != null)
        {
            if (ScrollRect.verticalNormalizedPosition <= Plugin.GetScrollThreshold())
            {
                Plugin.TriggerManualPagination();
            }
        }
    }
}