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

    // Colors
    static readonly Color BgPrimary = new Color(0.04f, 0.055f, 0.08f);
    static readonly Color BgCard = new Color(0.08f, 0.11f, 0.15f);
    static readonly Color AccentCyan = new Color(0f, 0.9f, 1f);
    static readonly Color AccentOrange = new Color(1f, 0.42f, 0.17f);
    static readonly Color AccentGreen = new Color(0.2f, 0.9f, 0.4f);
    static readonly Color TextPrimary = new Color(0.91f, 0.93f, 0.95f);
    static readonly Color TextSecondary = new Color(0.42f, 0.5f, 0.58f);
    static readonly Color TextDim = new Color(0.23f, 0.29f, 0.36f);

    Canvas m_Canvas;
    TMP_InputField m_PlayerNameInput;
    TMP_InputField m_LobbyCodeInput;
    TextMeshProUGUI m_StatusText;
    TextMeshProUGUI m_LobbyCodeDisplay;
    TextMeshProUGUI m_PlayerCountText;
    TextMeshProUGUI m_LoadingText;

    // Panels
    GameObject m_MainMenuPanel;
    GameObject m_LobbyWaitPanel;
    GameObject m_JoinPanel;
    GameObject m_LoadingPanel;

    // Buttons
    Button m_CreatePublicButton;
    Button m_CreatePrivateButton;
    Button m_QuickJoinButton;
    Button m_JoinByCodeButton;
    Button m_JoinSubmitButton;
    Button m_StartGameButton;
    Button m_BackButton;
    Button m_BackFromJoinButton;
    Button m_CopyCodeButton;

    string m_LobbyCode;
    bool m_IsPrivateLobby;

    void Start()
    {
        BuildUI();
        ShowMainMenu();

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;

        }

        SetStatus("SERVICES INITIALIZING...");
        CheckServicesReady();
    }

    void OnSceneLoaded(string sceneName, LoadSceneMode loadSceneMode,
    System.Collections.Generic.List<ulong> clientsCompleted,
    System.Collections.Generic.List<ulong> clientsTimedOut)
    {
        if (sceneName == GameSceneName)
        {
            // Game scene loaded, destroy menu
            Destroy(gameObject);
        }
    }

    void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnSceneLoaded;
        }

        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnPlayerCountChanged -= OnPlayerCountChanged;
        }
    }

    async void CheckServicesReady()
    {
        while (!ServicesInitializer.IsInitialized)
        {
            await Task.Delay(100);
        }
        SetStatus("CONNECTED // READY");
    }

    // ===== STATE MANAGEMENT =====

    void ShowMainMenu()
    {
        m_MainMenuPanel.SetActive(true);
        m_LobbyWaitPanel.SetActive(false);
        m_JoinPanel.SetActive(false);
        m_LoadingPanel.SetActive(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void ShowLobbyWait()
    {
        m_MainMenuPanel.SetActive(false);
        m_LobbyWaitPanel.SetActive(true);
        m_JoinPanel.SetActive(false);
        m_LoadingPanel.SetActive(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void ShowJoinPanel()
    {
        m_MainMenuPanel.SetActive(false);
        m_LobbyWaitPanel.SetActive(false);
        m_JoinPanel.SetActive(true);
        m_LoadingPanel.SetActive(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void ShowLoading(string message)
    {
        m_LoadingPanel.SetActive(true);
        m_LoadingText.text = message;
    }

    void HideLoading()
    {
        m_LoadingPanel.SetActive(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // ===== LOBBY ACTIONS =====

    async void OnCreatePublicLobby()
    {
        if (!ServicesInitializer.IsInitialized)
        {
            SetStatus("SERVICES NOT READY");
            return;
        }

        ShowLoading("CREATING PUBLIC LOBBY...");

        string playerName = m_PlayerNameInput.text;
        if (string.IsNullOrEmpty(playerName)) playerName = "Player";

        var lobby = await LobbyManager.Instance.CreatePublicLobby(playerName + "'s Game", MaxPlayers);

        HideLoading();

        if (lobby != null)
        {
            // Public: start host and load game immediately
            NetworkManager.Singleton.StartHost();
            SetStatus("STARTING MATCH...");
            NetworkManager.Singleton.SceneManager.LoadScene(GameSceneName,
                UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
        else
        {
            SetStatus("FAILED TO CREATE LOBBY");
        }
    }

    async void OnCreatePrivateLobby()
    {
        if (!ServicesInitializer.IsInitialized)
        {
            SetStatus("SERVICES NOT READY");
            return;
        }

        ShowLoading("CREATING PRIVATE LOBBY...");
        m_IsPrivateLobby = true;

        string playerName = m_PlayerNameInput.text;
        if (string.IsNullOrEmpty(playerName)) playerName = "Player";

        var lobby = await LobbyManager.Instance.CreatePrivateLobby(playerName + "'s Game", MaxPlayers);

        HideLoading();

        if (lobby != null)
        {
            m_LobbyCode = lobby.LobbyCode;
            m_LobbyCodeDisplay.text = lobby.LobbyCode;
            m_PlayerCountText.text = $"PLAYERS: 1 / {MaxPlayers}";
            SetStatus("LOBBY CREATED // WAITING FOR PLAYERS");

            // DON'T start host yet — wait for Start Game button
            LobbyManager.Instance.OnPlayerCountChanged += OnPlayerCountChanged;
            ShowLobbyWait();
        }
        else
        {
            SetStatus("FAILED TO CREATE LOBBY");
            ShowMainMenu();
        }
    }


    void OnPlayerCountChanged(int count)
    {
        m_PlayerCountText.text = $"PLAYERS: {count} / {MaxPlayers}";
    }

    void OnStartGame()
    {
        LobbyManager.Instance.OnPlayerCountChanged -= OnPlayerCountChanged;
        SetStatus("STARTING MATCH...");

        // NOW start host and load game
        NetworkManager.Singleton.StartHost();
        NetworkManager.Singleton.SceneManager.LoadScene(GameSceneName,
            UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    void OnCopyCode()
    {
        GUIUtility.systemCopyBuffer = m_LobbyCode;
        SetStatus("CODE COPIED TO CLIPBOARD");
    }

    void OnShowJoinPanel()
    {
        ShowJoinPanel();
    }

    async void OnJoinByCode()
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

        if (success)
        {
            SetStatus("JOINED // WAITING FOR HOST TO START...");
            // ... after join success ...
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            // ...
        }
        else
        {
            SetStatus("FAILED TO JOIN");
            ShowJoinPanel();
        }
    }

    async void OnQuickJoin()
    {
        ShowLoading("FINDING A GAME...");
        bool success = await LobbyManager.Instance.QuickJoin();
        HideLoading();

        if (success)
        {
            SetStatus("JOINED // LOADING GAME...");
        }
        else
        {
            SetStatus("NO PUBLIC GAMES FOUND");
            ShowMainMenu();
        }
    }

    void OnBack()
    {
        LobbyManager.Instance.LeaveLobby();
        if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsClient)
        {
            NetworkManager.Singleton.Shutdown();
        }
        ShowMainMenu();
        SetStatus("CONNECTED // READY");
    }

    void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnSceneLoaded;
        }
    }

    void SetStatus(string message)
    {
        if (m_StatusText != null)
            m_StatusText.text = $"● {message}";
    }

    // ===== BUILD UI =====

    void BuildUI()
    {
        // Canvas
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

        // Background
        CreatePanel(canvasObj, "Background", BgPrimary,
            Vector2.zero, new Vector2(1920, 1080));

        // Header (always visible)
        BuildHeader(canvasObj);

        // ===== MAIN MENU PANEL =====
        m_MainMenuPanel = new GameObject("MainMenuPanel");
        m_MainMenuPanel.transform.SetParent(canvasObj.transform, false);
        m_MainMenuPanel.AddComponent<RectTransform>().sizeDelta = new Vector2(500, 500);
        BuildMainMenu(m_MainMenuPanel);

        // ===== LOBBY WAIT PANEL =====
        m_LobbyWaitPanel = new GameObject("LobbyWaitPanel");
        m_LobbyWaitPanel.transform.SetParent(canvasObj.transform, false);
        m_LobbyWaitPanel.AddComponent<RectTransform>().sizeDelta = new Vector2(500, 400);
        BuildLobbyWait(m_LobbyWaitPanel);

        // ===== JOIN PANEL =====
        m_JoinPanel = new GameObject("JoinPanel");
        m_JoinPanel.transform.SetParent(canvasObj.transform, false);
        m_JoinPanel.AddComponent<RectTransform>().sizeDelta = new Vector2(500, 300);
        BuildJoinPanel(m_JoinPanel);

        // ===== LOADING OVERLAY =====
        m_LoadingPanel = CreatePanel(canvasObj, "LoadingPanel",
            new Color(BgPrimary.r, BgPrimary.g, BgPrimary.b, 0.9f),
            Vector2.zero, new Vector2(1920, 1080));

        m_LoadingText = CreateText(m_LoadingPanel, "LoadingText", "LOADING...", 16,
            AccentCyan, FontStyles.Bold, Vector2.zero, new Vector2(400, 40));

        // ===== STATUS BAR =====
        GameObject statusPanel = CreatePanel(canvasObj, "StatusPanel", BgCard,
            new Vector2(0, -470), new Vector2(400, 36));

        m_StatusText = CreateText(statusPanel, "StatusText", "● INITIALIZING...", 11,
            TextSecondary, FontStyles.Normal, Vector2.zero, new Vector2(380, 30));

        // Version
        CreateText(canvasObj, "Version", "v0.1.0 // BUILD 2026.03", 10,
            TextDim, FontStyles.Normal, new Vector2(-830, -510), new Vector2(300, 20),
            TextAlignmentOptions.Left);
    }

    void BuildHeader(GameObject canvas)
    {
        CreateText(canvas, "Title", "FIREZONE", 52,
            AccentCyan, FontStyles.Bold, new Vector2(0, 380), new Vector2(500, 60));

        CreateText(canvas, "Subtitle", "MULTIPLAYER COMBAT ARENA", 13,
            TextSecondary, FontStyles.Normal, new Vector2(0, 345), new Vector2(500, 25));

        CreateImage(canvas, "Divider", AccentCyan * 0.5f,
            new Vector2(0, 325), new Vector2(120, 1));
    }

    void BuildMainMenu(GameObject parent)
    {
        float yPos = 100;

        // Player Name
        CreateText(parent, "CallsignLabel", "// CALLSIGN", 11,
            TextDim, FontStyles.Normal, new Vector2(-145, yPos), new Vector2(200, 20),
            TextAlignmentOptions.Left);
        yPos -= 28;

        m_PlayerNameInput = CreateInputField(parent, "PlayerNameInput",
            "Enter your name", new Vector2(0, yPos), new Vector2(460, 52));
        yPos -= 70;

        // Create Public Lobby
        m_CreatePublicButton = CreateButton(parent, "PublicBtn", "CREATE PUBLIC MATCH",
            BgPrimary, AccentCyan, new Vector2(0, yPos), new Vector2(460, 52));
        m_CreatePublicButton.onClick.AddListener(OnCreatePublicLobby);
        yPos -= 60;

        // Create Private Lobby
        m_CreatePrivateButton = CreateButton(parent, "PrivateBtn", "CREATE PRIVATE MATCH",
            TextPrimary, BgCard, new Vector2(0, yPos), new Vector2(460, 52));
        m_CreatePrivateButton.onClick.AddListener(OnCreatePrivateLobby);
        yPos -= 60;

        // Quick Join
        m_QuickJoinButton = CreateButton(parent, "QuickJoinBtn", "⚡ QUICK JOIN",
            TextPrimary, BgCard, new Vector2(0, yPos), new Vector2(460, 52));
        m_QuickJoinButton.onClick.AddListener(OnQuickJoin);
        yPos -= 60;

        // Join by Code
        m_JoinByCodeButton = CreateButton(parent, "JoinCodeBtn", "JOIN WITH CODE",
            AccentOrange, BgCard, new Vector2(0, yPos), new Vector2(460, 52));
        m_JoinByCodeButton.onClick.AddListener(OnShowJoinPanel);
    }

    void BuildLobbyWait(GameObject parent)
    {
        float yPos = 80;

        CreateText(parent, "WaitTitle", "PRIVATE LOBBY", 20,
            TextPrimary, FontStyles.Bold, new Vector2(0, yPos), new Vector2(460, 35));
        yPos -= 40;

        // Lobby code display
        GameObject codeBg = CreatePanel(parent, "CodeBg",
            new Color(AccentCyan.r, AccentCyan.g, AccentCyan.b, 0.08f),
            new Vector2(0, yPos), new Vector2(460, 90));

        CreateText(codeBg, "CodeLabel", "LOBBY CODE", 10,
            TextSecondary, FontStyles.Normal, new Vector2(0, 24), new Vector2(400, 20));

        m_LobbyCodeDisplay = CreateText(codeBg, "CodeValue", "------", 36,
            AccentCyan, FontStyles.Bold, new Vector2(0, -6), new Vector2(400, 45));

        CreateText(codeBg, "CodeHint", "Share this code with your friends", 11,
            TextDim, FontStyles.Normal, new Vector2(0, -32), new Vector2(400, 20));
        yPos -= 110;

        // Copy Code Button
        m_CopyCodeButton = CreateButton(parent, "CopyBtn", "COPY CODE",
            BgPrimary, AccentCyan, new Vector2(0, yPos), new Vector2(220, 44));
        m_CopyCodeButton.onClick.AddListener(OnCopyCode);
        yPos -= 55;

        // Player count
        m_PlayerCountText = CreateText(parent, "PlayerCount", "PLAYERS: 1 / 4", 14,
            TextSecondary, FontStyles.Normal, new Vector2(0, yPos), new Vector2(460, 25));
        yPos -= 50;

        // Start Game Button
        m_StartGameButton = CreateButton(parent, "StartBtn", "▶ START GAME",
            BgPrimary, AccentGreen, new Vector2(0, yPos), new Vector2(460, 56));
        m_StartGameButton.onClick.AddListener(OnStartGame);
        yPos -= 60;

        // Back Button
        m_BackButton = CreateButton(parent, "BackBtn", "← BACK",
            TextSecondary, BgCard, new Vector2(0, yPos), new Vector2(460, 44));
        m_BackButton.onClick.AddListener(OnBack);
    }

    void BuildJoinPanel(GameObject parent)
    {
        float yPos = 60;

        CreateText(parent, "JoinTitle", "JOIN WITH CODE", 20,
            TextPrimary, FontStyles.Bold, new Vector2(0, yPos), new Vector2(460, 35));
        yPos -= 50;

        // Code input
        CreateText(parent, "CodeLabel", "// ENTER LOBBY CODE", 11,
            TextDim, FontStyles.Normal, new Vector2(-130, yPos), new Vector2(200, 20),
            TextAlignmentOptions.Left);
        yPos -= 30;

        m_LobbyCodeInput = CreateInputField(parent, "CodeInput",
            "ABC123", new Vector2(0, yPos), new Vector2(460, 52));
        m_LobbyCodeInput.characterLimit = 6;
        m_LobbyCodeInput.onValueChanged.AddListener(
            (val) => m_LobbyCodeInput.text = val.ToUpper());
        yPos -= 65;

        // Join Button
        m_JoinSubmitButton = CreateButton(parent, "JoinSubmitBtn", "JOIN →",
            BgPrimary, AccentOrange, new Vector2(0, yPos), new Vector2(460, 52));
        m_JoinSubmitButton.onClick.AddListener(OnJoinByCode);
        yPos -= 60;

        // Back Button
        m_BackFromJoinButton = CreateButton(parent, "BackBtn", "← BACK",
            TextSecondary, BgCard, new Vector2(0, yPos), new Vector2(460, 44));
        m_BackFromJoinButton.onClick.AddListener(() => ShowMainMenu());
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

        CreateText(obj, "Label", label, 15,
            textColor, FontStyles.Bold, Vector2.zero, size);

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

        TMP_InputField input = obj.AddComponent<TMP_InputField>();
        input.textViewport = textAreaRect;
        input.textComponent = inputText;
        input.placeholder = phText;
        input.caretColor = AccentCyan;

        return input;
    }

   
}
