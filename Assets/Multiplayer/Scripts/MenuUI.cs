using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MenuUI : MonoBehaviour
{
    [Header("Settings")]
    public string GameSceneName = "MainScene";
    public int MaxPlayers = 4;

    // UI References (created by code)
    Canvas m_Canvas;
    TMP_InputField m_PlayerNameInput;
    TMP_InputField m_LobbyCodeInput;
    TextMeshProUGUI m_StatusText;
    TextMeshProUGUI m_LobbyCodeDisplay;
    Button m_CreateButton;
    Button m_JoinButton;
    Button m_QuickJoinButton;
    GameObject m_LobbyCodePanel;
    GameObject m_LoadingPanel;
    TextMeshProUGUI m_LoadingText;

    // Colors
    static readonly Color BgPrimary = new Color(0.04f, 0.055f, 0.08f);
    static readonly Color BgCard = new Color(0.08f, 0.11f, 0.15f);
    static readonly Color AccentCyan = new Color(0f, 0.9f, 1f);
    static readonly Color AccentOrange = new Color(1f, 0.42f, 0.17f);
    static readonly Color TextPrimary = new Color(0.91f, 0.93f, 0.95f);
    static readonly Color TextSecondary = new Color(0.42f, 0.5f, 0.58f);
    static readonly Color TextDim = new Color(0.23f, 0.29f, 0.36f);

    void Start()
    {
        BuildUI();

        m_CreateButton.onClick.AddListener(() => OnCreateLobby());
        m_JoinButton.onClick.AddListener(() => OnJoinLobby());
        m_QuickJoinButton.onClick.AddListener(() => OnQuickJoin());

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }

        SetStatus("SERVICES INITIALIZING...");
        CheckServicesReady();
    }

    async void CheckServicesReady()
    {
        // Wait for ServicesInitializer
        while (!ServicesInitializer.IsInitialized)
        {
            await Task.Delay(100);
        }
        SetStatus("CONNECTED // READY");
    }

    void BuildUI()
    {
        // ===== CANVAS =====
        GameObject canvasObj = new GameObject("MenuCanvas");
        canvasObj.transform.SetParent(transform);
        m_Canvas = canvasObj.AddComponent<Canvas>();
        m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        m_Canvas.sortingOrder = 100;

        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        // ===== FULL SCREEN BACKGROUND =====
        CreateImage(canvasObj, "Background", BgPrimary, Vector2.zero, new Vector2(1920, 1080));

        // ===== CENTER CONTAINER =====
        GameObject container = CreatePanel(canvasObj, "Container", Color.clear,
            Vector2.zero, new Vector2(500, 750));

        float yPos = 310;

        // ===== TITLE =====
        CreateText(container, "Title", "FIREZONE", 52,
            AccentCyan, FontStyles.Bold, new Vector2(0, yPos), new Vector2(500, 60));
        yPos -= 40;

        CreateText(container, "Subtitle", "MULTIPLAYER COMBAT ARENA", 13,
            TextSecondary, FontStyles.Normal, new Vector2(0, yPos), new Vector2(500, 25));
        yPos -= 15;

        // Divider line
        CreateImage(container, "Divider", AccentCyan * 0.5f,
            new Vector2(0, yPos), new Vector2(120, 1));
        yPos -= 45;

        // ===== CALLSIGN LABEL =====
        CreateText(container, "CallsignLabel", "// CALLSIGN", 11,
            TextDim, FontStyles.Normal, new Vector2(-145, yPos), new Vector2(200, 20),
            TextAlignmentOptions.Left);
        yPos -= 28;

        // ===== PLAYER NAME INPUT =====
        m_PlayerNameInput = CreateInputField(container, "PlayerNameInput",
            "Enter your name", new Vector2(0, yPos), new Vector2(460, 52));
        yPos -= 60;

        // ===== CREATE LOBBY BUTTON =====
        m_CreateButton = CreateButton(container, "CreateBtn", "+ CREATE LOBBY",
            AccentCyan, BgPrimary, new Vector2(0, yPos), new Vector2(460, 56));
        yPos -= 64;

        // ===== QUICK JOIN BUTTON =====
        m_QuickJoinButton = CreateButton(container, "QuickJoinBtn", "⚡ QUICK JOIN",
            TextPrimary, BgCard, new Vector2(0, yPos), new Vector2(460, 56));
        yPos -= 80;

        // ===== JOIN BY CODE SECTION =====
        GameObject joinPanel = CreatePanel(container, "JoinPanel", BgCard,
            new Vector2(0, yPos), new Vector2(460, 110));

        // Orange left accent bar
        CreateImage(joinPanel, "AccentBar", AccentOrange,
            new Vector2(-226, 0), new Vector2(3, 110));

        CreateText(joinPanel, "JoinLabel", "// JOIN WITH CODE", 11,
            TextDim, FontStyles.Normal, new Vector2(-90, 28), new Vector2(200, 20),
            TextAlignmentOptions.Left);

        m_LobbyCodeInput = CreateInputField(joinPanel, "CodeInput",
            "ABC123", new Vector2(-40, -14), new Vector2(280, 48));
        m_LobbyCodeInput.characterLimit = 6;
        m_LobbyCodeInput.onValueChanged.AddListener(
            (val) => m_LobbyCodeInput.text = val.ToUpper());

        m_JoinButton = CreateButton(joinPanel, "JoinBtn", "JOIN →",
            BgPrimary, AccentOrange, new Vector2(170, -14), new Vector2(100, 48));
        yPos -= 80;

        // ===== LOBBY CODE DISPLAY (hidden by default) =====
        m_LobbyCodePanel = CreatePanel(container, "LobbyCodePanel",
            new Color(AccentCyan.r, AccentCyan.g, AccentCyan.b, 0.08f),
            new Vector2(0, yPos), new Vector2(460, 100));

        CreateText(m_LobbyCodePanel, "CodeLabel", "YOUR LOBBY CODE", 10,
            TextSecondary, FontStyles.Normal, new Vector2(0, 26), new Vector2(400, 20));

        m_LobbyCodeDisplay = CreateText(m_LobbyCodePanel, "CodeValue", "------", 36,
            AccentCyan, FontStyles.Bold, new Vector2(0, -2), new Vector2(400, 45));

        CreateText(m_LobbyCodePanel, "CodeHint", "Share this code with your friends", 11,
            TextDim, FontStyles.Normal, new Vector2(0, -32), new Vector2(400, 20));

        m_LobbyCodePanel.SetActive(false);

        // ===== STATUS BAR =====
        GameObject statusPanel = CreatePanel(container, "StatusPanel", BgCard,
            new Vector2(0, -340), new Vector2(350, 36));

        m_StatusText = CreateText(statusPanel, "StatusText", "● INITIALIZING...", 11,
            TextSecondary, FontStyles.Normal, Vector2.zero, new Vector2(330, 30));

        // ===== LOADING OVERLAY (hidden by default) =====
        m_LoadingPanel = CreatePanel(container, "LoadingPanel",
            new Color(BgPrimary.r, BgPrimary.g, BgPrimary.b, 0.9f),
            Vector2.zero, new Vector2(500, 750));

        m_LoadingText = CreateText(m_LoadingPanel, "LoadingText", "CREATING LOBBY...", 14,
            AccentCyan, FontStyles.Normal, new Vector2(0, -10), new Vector2(400, 30));

        m_LoadingPanel.SetActive(false);

        // ===== VERSION TAG =====
        CreateText(canvasObj, "Version", "v0.1.0 // BUILD 2026.03", 10,
            TextDim, FontStyles.Normal, new Vector2(-830, -500), new Vector2(300, 20),
            TextAlignmentOptions.Left);
    }

    // ===== UI FACTORY METHODS =====

    GameObject CreatePanel(GameObject parent, string name, Color color,
        Vector2 position, Vector2 size)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image img = obj.AddComponent<Image>();
        img.color = color;

        return obj;
    }

    Image CreateImage(GameObject parent, string name, Color color,
        Vector2 position, Vector2 size)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image img = obj.AddComponent<Image>();
        img.color = color;

        return img;
    }

    TextMeshProUGUI CreateText(GameObject parent, string name, string text,
        float fontSize, Color color, FontStyles style, Vector2 position, Vector2 size,
        TextAlignmentOptions alignment = TextAlignmentOptions.Center)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.alignment = alignment;
        tmp.enableWordWrapping = false;

        // Use default TMP font if no custom font
        if (tmp.font == null)
        {
            tmp.font = TMP_Settings.defaultFontAsset;
        }

        return tmp;
    }

    Button CreateButton(GameObject parent, string name, string label,
        Color textColor, Color bgColor, Vector2 position, Vector2 size)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image img = obj.AddComponent<Image>();
        img.color = bgColor;

        Button btn = obj.AddComponent<Button>();
        ColorBlock colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1, 1, 1, 0.9f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        btn.colors = colors;

        TextMeshProUGUI tmp = CreateText(obj, "Label", label, 16,
            textColor, FontStyles.Bold, Vector2.zero, size);
        tmp.characterSpacing = 5;

        return btn;
    }

    TMP_InputField CreateInputField(GameObject parent, string name,
        string placeholder, Vector2 position, Vector2 size)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image bg = obj.AddComponent<Image>();
        bg.color = new Color(BgCard.r * 0.8f, BgCard.g * 0.8f, BgCard.b * 0.8f);

        // Text area
        GameObject textArea = new GameObject("TextArea");
        textArea.transform.SetParent(obj.transform, false);
        RectTransform textAreaRect = textArea.AddComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(15, 5);
        textAreaRect.offsetMax = new Vector2(-15, -5);
        textArea.AddComponent<RectMask2D>();

        // Input text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(textArea.transform, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TextMeshProUGUI inputText = textObj.AddComponent<TextMeshProUGUI>();
        inputText.fontSize = 18;
        inputText.color = TextPrimary;
        inputText.alignment = TextAlignmentOptions.MidlineLeft;

        // Placeholder
        GameObject phObj = new GameObject("Placeholder");
        phObj.transform.SetParent(textArea.transform, false);
        RectTransform phRect = phObj.AddComponent<RectTransform>();
        phRect.anchorMin = Vector2.zero;
        phRect.anchorMax = Vector2.one;
        phRect.offsetMin = Vector2.zero;
        phRect.offsetMax = Vector2.zero;

        TextMeshProUGUI phText = phObj.AddComponent<TextMeshProUGUI>();
        phText.text = placeholder;
        phText.fontSize = 18;
        phText.color = TextDim;
        phText.alignment = TextAlignmentOptions.MidlineLeft;

        // Input field component
        TMP_InputField input = obj.AddComponent<TMP_InputField>();
        input.textViewport = textAreaRect;
        input.textComponent = inputText;
        input.placeholder = phText;
        input.caretColor = AccentCyan;

        return input;
    }

    // ===== LOBBY LOGIC =====

    async void OnCreateLobby()
    {
        if (!ServicesInitializer.IsInitialized)
        {
            SetStatus("SERVICES NOT READY");
            return;
        }

        ShowLoading("CREATING LOBBY...");

        string playerName = m_PlayerNameInput.text;
        if (string.IsNullOrEmpty(playerName)) playerName = "Player";

        var lobby = await LobbyManager.Instance.CreateLobby(playerName + "'s Game", MaxPlayers);

        HideLoading();

        if (lobby != null)
        {
            m_LobbyCodePanel.SetActive(true);
            m_LobbyCodeDisplay.text = lobby.LobbyCode;
            SetStatus($"LOBBY ACTIVE // WAITING FOR PLAYERS (1/{MaxPlayers})");

            // Load game scene
            NetworkManager.Singleton.SceneManager.LoadScene(GameSceneName, LoadSceneMode.Single);
        }
        else
        {
            SetStatus("FAILED TO CREATE LOBBY");
        }
    }

    async void OnJoinLobby()
    {
        string code = m_LobbyCodeInput.text.Trim().ToUpper();
        if (string.IsNullOrEmpty(code))
        {
            SetStatus("ENTER A LOBBY CODE");
            return;
        }

        ShowLoading($"JOINING {code}...");
        bool success = await LobbyManager.Instance.JoinLobbyByCode(code);
        HideLoading();

        SetStatus(success ? "JOINED // LOADING GAME..." : "FAILED TO JOIN");
    }

    async void OnQuickJoin()
    {
        ShowLoading("FINDING A GAME...");
        bool success = await LobbyManager.Instance.QuickJoin();
        HideLoading();

        SetStatus(success ? "JOINED // LOADING GAME..." : "NO GAMES FOUND");
    }

    void OnClientConnected(ulong clientId)
    {
        Debug.Log($"[Menu] Client {clientId} connected");
    }

    // ===== UI HELPERS =====

    void SetStatus(string message)
    {
        if (m_StatusText != null)
            m_StatusText.text = $"● {message}";
    }

    void ShowLoading(string message)
    {
        m_LoadingPanel.SetActive(true);
        m_LoadingText.text = message;
    }

    void HideLoading()
    {
        m_LoadingPanel.SetActive(false);
    }

    void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }
}