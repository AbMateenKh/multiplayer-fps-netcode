using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class MenuUI : MonoBehaviour
{
    enum MenuScreenState
    {
        Landing,
        ModeSelect,
        Join,
        Lobby
    }

    [Header("Settings")]
    public string GameSceneName = "MainScene";
    public int MaxPlayers = 4;

    static readonly Color BgPrimary = new Color(0.04f, 0.05f, 0.08f);
    static readonly Color BgSurface = new Color(0.07f, 0.09f, 0.13f, 0.94f);
    static readonly Color BgInput = new Color(0.11f, 0.13f, 0.18f);
    static readonly Color BgPlayerRow = new Color(0.1f, 0.12f, 0.16f, 0.92f);
    static readonly Color BgPlayerRowHost = new Color(0.2f, 0.57f, 0.93f, 0.16f);
    static readonly Color AccentBlue = new Color(0.37f, 0.78f, 1f);
    static readonly Color AccentBlueDark = new Color(0.08f, 0.34f, 0.58f);
    static readonly Color AccentOrange = new Color(0.98f, 0.54f, 0.16f);
    static readonly Color AccentOrangeText = new Color(1f, 0.94f, 0.88f);
    static readonly Color AccentGreen = new Color(0.23f, 0.5f, 0.16f);
    static readonly Color AccentGreenText = new Color(0.91f, 0.98f, 0.88f);
    static readonly Color AccentPink = new Color(0.94f, 0.27f, 0.47f, 0.22f);
    static readonly Color TextPrimary = new Color(0.94f, 0.97f, 1f);
    static readonly Color TextSecondary = new Color(0.57f, 0.63f, 0.72f);
    static readonly Color TextDim = new Color(0.33f, 0.39f, 0.47f);
    static readonly Color BorderSubtle = new Color(1f, 1f, 1f, 0.08f);
    static readonly Color BorderFocus = new Color(0.37f, 0.78f, 1f, 0.45f);
    static readonly Color EmptySlot = new Color(1f, 1f, 1f, 0.035f);

    const float k_MinGameplayTransitionDuration = 1f;
    const string k_PendingStatusMessageKey = "NetcodeFPS.PendingMenuStatus";

    Canvas m_Canvas;
    TMP_InputField m_PlayerNameInput;
    TMP_InputField m_LobbyCodeInput;
    TextMeshProUGUI m_StatusText;
    TextMeshProUGUI m_LobbyCodeDisplay;
    TextMeshProUGUI m_PlayerCountText;
    TextMeshProUGUI m_LobbyTypeText;

    AnimatedMenuPanel m_LandingPanel;
    AnimatedMenuPanel m_MainMenuPanel;
    AnimatedMenuPanel m_LobbyPanel;
    AnimatedMenuPanel m_JoinPanel;
    MenuLoadingOverlay m_LoadingOverlay;

    GameObject m_LobbyCodeSection;
    Button m_StartGameButton;

    readonly List<GameObject> m_PlayerSlots = new List<GameObject>();
    readonly List<TextMeshProUGUI> m_PlayerSlotNames = new List<TextMeshProUGUI>();
    readonly List<Image> m_PlayerSlotBgs = new List<Image>();
    readonly List<GameObject> m_PlayerSlotIcons = new List<GameObject>();
    readonly List<GameObject> m_PlayerSlotEmptyDots = new List<GameObject>();

    MenuScreenState m_CurrentScreen;
    string m_LobbyCode;
    bool m_IsPrivateLobby;
    string m_PlayerName;
    bool m_IsTransitioningToGameplay;
    bool m_IsTransitionCompleting;
    bool m_IsShowingPendingStatusMessage;

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        BuildUI();
        ShowScreen(MenuScreenState.Landing, true);
        SubscribeNetworkCallbacks();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnUnitySceneLoaded;

        string pendingStatus = ConsumePendingStatusMessage();
        m_IsShowingPendingStatusMessage = !string.IsNullOrEmpty(pendingStatus);
        SetInitialStatus(m_IsShowingPendingStatusMessage ? pendingStatus : "Initializing services...");
        CheckServicesReady();
    }

    async void CheckServicesReady()
    {
        while (!ServicesInitializer.IsInitialized)
        {
            await Task.Delay(100);
        }

        if (!m_IsShowingPendingStatusMessage)
        {
            SetStatus("Connected and ready");
        }
    }

    void SubscribeNetworkCallbacks()
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        if (NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent += OnNetworkSceneEvent;
        }

        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    void UnsubscribeNetworkCallbacks()
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        if (NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnNetworkSceneEvent;
        }

        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.IsHost)
        {
            return;
        }

        bool serverDisconnected = clientId == NetworkManager.ServerClientId ||
            !NetworkManager.Singleton.IsConnectedClient;
        if (!serverDisconnected)
        {
            return;
        }

        m_IsTransitioningToGameplay = false;
        m_IsTransitionCompleting = false;
        HideLoading();
        ShowScreen(MenuScreenState.ModeSelect);
        SetStatus("Host disconnected. Match ended.");
        UnlockCursor();

        if (NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }
    }

    void OnNetworkSceneEvent(SceneEvent sceneEvent)
    {
        if (sceneEvent.SceneName != GameSceneName)
        {
            return;
        }

        if (sceneEvent.SceneEventType == SceneEventType.Load)
        {
            if (!m_IsTransitioningToGameplay)
            {
                BeginGameplayTransition("Deploying To Arena", "Synchronizing with the host...");
            }

            m_LoadingOverlay.SetMessage("Streaming arena geometry...");
            m_LoadingOverlay.SetProgress(0.7f);
        }
        else if (sceneEvent.SceneEventType == SceneEventType.LoadComplete &&
                 sceneEvent.ClientId == NetworkManager.Singleton.LocalClientId)
        {
            m_LoadingOverlay.SetMessage("Scene loaded. Finalizing spawn state...");
            m_LoadingOverlay.SetProgress(0.88f);
        }
        else if (sceneEvent.SceneEventType == SceneEventType.LoadEventCompleted && !m_IsTransitionCompleting)
        {
            StartCoroutine(FinishGameplayTransition());
        }
    }

    void OnUnitySceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (scene.name != GameSceneName)
        {
            return;
        }

        if (!m_IsTransitioningToGameplay || m_IsTransitionCompleting)
        {
            return;
        }

        m_LoadingOverlay.SetMessage("Scene activated. Finalizing deployment...");
        m_LoadingOverlay.SetProgress(0.96f);
        StartCoroutine(FinishGameplayTransition());
    }

    void ShowScreen(MenuScreenState state, bool instant = false)
    {
        m_CurrentScreen = state;

        TogglePanel(m_LandingPanel, state == MenuScreenState.Landing, instant);
        TogglePanel(m_MainMenuPanel, state == MenuScreenState.ModeSelect, instant);
        TogglePanel(m_JoinPanel, state == MenuScreenState.Join, instant);
        TogglePanel(m_LobbyPanel, state == MenuScreenState.Lobby, instant);

        if (!m_IsTransitioningToGameplay)
        {
            UnlockCursor();
        }
    }

    void TogglePanel(AnimatedMenuPanel panel, bool show, bool instant)
    {
        if (panel == null)
        {
            return;
        }

        if (show)
        {
            panel.Show(instant);
        }
        else if (instant || panel.gameObject.activeSelf)
        {
            panel.Hide(instant);
        }
    }

    void ShowLobby(bool isPrivate)
    {
        m_IsPrivateLobby = isPrivate;
        m_LobbyCodeSection.SetActive(isPrivate);
        m_LobbyTypeText.text = isPrivate ? "Private squad lobby" : "Public squad lobby";
        m_StartGameButton.gameObject.SetActive(LobbyManager.Instance != null && LobbyManager.Instance.IsHost());
        ShowScreen(MenuScreenState.Lobby);
    }

    void ShowLoading(string title, string message, float progress)
    {
        m_LoadingOverlay.Show(title, message);
        m_LoadingOverlay.SetProgress(progress);
    }

    void HideLoading()
    {
        if (!m_IsTransitioningToGameplay)
        {
            m_LoadingOverlay.Hide();
        }
    }

    void BeginGameplayTransition(string title, string message)
    {
        m_IsTransitioningToGameplay = true;
        ShowLoading(title, message, 0.18f);
        m_LoadingOverlay.SetTitle(title);
        m_LoadingOverlay.SetMessage(message);
        m_LoadingOverlay.SetProgress(0.18f);
    }

    IEnumerator FinishGameplayTransition()
    {
        m_IsTransitionCompleting = true;
        m_LoadingOverlay.SetTitle("Arena Ready");
        m_LoadingOverlay.SetMessage("Drop sequence complete...");
        m_LoadingOverlay.SetProgress(1f);

        while (!m_LoadingOverlay.HasMetMinimumVisibleTime(k_MinGameplayTransitionDuration))
        {
            yield return null;
        }

        Destroy(gameObject);
    }

    void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    string GetPlayerName()
    {
        m_PlayerName = m_PlayerNameInput.text.Trim();
        if (string.IsNullOrEmpty(m_PlayerName))
        {
            m_PlayerName = "Player";
        }

        return m_PlayerName;
    }

    void OnOpenMainMenu()
    {
        ShowScreen(MenuScreenState.ModeSelect);
        SetStatus("Choose a lobby flow");
    }

    void OnReturnToLanding()
    {
        ShowScreen(MenuScreenState.Landing);
        SetStatus("Connected and ready");
    }

    void OnOpenJoinPanel()
    {
        ShowScreen(MenuScreenState.Join);
        SetStatus("Enter a lobby code to link up");
    }

    async void OnCreatePublicLobby()
    {
        if (!ServicesInitializer.IsInitialized)
        {
            SetStatus("Services not ready");
            return;
        }

        ShowLoading("Opening Public Lobby", "Allocating relay and matchmaking slot...", 0.12f);
        m_IsPrivateLobby = false;
        string name = GetPlayerName();

        var lobby = await LobbyManager.Instance.CreatePublicLobby(name + "'s Game", name, MaxPlayers);
        HideLoading();

        if (lobby != null)
        {
            LobbyManager.Instance.OnLobbyPlayersChanged += OnLobbyPlayersChanged;
            ShowLobby(false);
            SetStatus("Public lobby live. Waiting for squad members");
        }
        else
        {
            SetStatus("Failed to create public lobby");
            ShowScreen(MenuScreenState.ModeSelect);
        }
    }

    async void OnCreatePrivateLobby()
    {
        if (!ServicesInitializer.IsInitialized)
        {
            SetStatus("Services not ready");
            return;
        }

        ShowLoading("Opening Private Lobby", "Creating relay channel and join code...", 0.12f);
        m_IsPrivateLobby = true;
        string name = GetPlayerName();

        var lobby = await LobbyManager.Instance.CreatePrivateLobby(name + "'s Game", name, MaxPlayers);
        HideLoading();

        if (lobby != null)
        {
            m_LobbyCode = lobby.LobbyCode;
            m_LobbyCodeDisplay.text = lobby.LobbyCode;
            LobbyManager.Instance.OnLobbyPlayersChanged += OnLobbyPlayersChanged;
            ShowLobby(true);
            SetStatus("Private lobby ready. Share the code");
        }
        else
        {
            SetStatus("Failed to create private lobby");
            ShowScreen(MenuScreenState.ModeSelect);
        }
    }

    async void OnQuickJoin()
    {
        ShowLoading("Searching Matches", "Looking for an active public lobby...", 0.08f);
        string name = GetPlayerName();
        bool success = await LobbyManager.Instance.QuickJoin(name);
        HideLoading();

        if (success)
        {
            LobbyManager.Instance.OnLobbyPlayersChanged += OnLobbyPlayersChanged;
            ShowLobby(false);
            SetStatus("Joined public lobby. Waiting for host");
        }
        else
        {
            SetStatus("No public matches found");
            ShowScreen(MenuScreenState.ModeSelect);
        }
    }

    async void OnJoinByCode()
    {
        string code = m_LobbyCodeInput.text.Trim().ToUpper();
        if (string.IsNullOrEmpty(code))
        {
            SetStatus("Enter a lobby code");
            return;
        }

        ShowLoading("Joining Lobby", $"Connecting to squad {code}...", 0.1f);
        string name = GetPlayerName();
        bool success = await LobbyManager.Instance.JoinLobbyByCode(code, name);
        HideLoading();

        if (success)
        {
            LobbyManager.Instance.OnLobbyPlayersChanged += OnLobbyPlayersChanged;
            m_IsPrivateLobby = true;
            m_LobbyCode = code;
            m_LobbyCodeDisplay.text = code;
            ShowLobby(true);
            SetStatus("Joined lobby. Waiting for host");
        }
        else
        {
            SetStatus("Failed to join lobby");
        }
    }

    void OnLobbyPlayersChanged(List<PlayerLobbyData> players)
    {
        int max = LobbyManager.Instance.GetMaxPlayers();
        m_PlayerCountText.text = $"Players ({players.Count}/{max})";

        for (int i = 0; i < m_PlayerSlots.Count; i++)
        {
            if (i < players.Count)
            {
                m_PlayerSlotNames[i].text = players[i].IsHost
                    ? $"\u2605 {players[i].Name} (Host)"
                    : players[i].Name;
                m_PlayerSlotNames[i].color = players[i].IsHost ? AccentBlue : TextPrimary;
                m_PlayerSlotBgs[i].color = players[i].IsHost ? BgPlayerRowHost : BgPlayerRow;
                m_PlayerSlotNames[i].gameObject.SetActive(true);
                m_PlayerSlotIcons[i].SetActive(true);
                m_PlayerSlotEmptyDots[i].SetActive(false);
            }
            else
            {
                m_PlayerSlotNames[i].gameObject.SetActive(false);
                m_PlayerSlotIcons[i].SetActive(false);
                m_PlayerSlotEmptyDots[i].SetActive(true);
                m_PlayerSlotBgs[i].color = EmptySlot;
            }
        }
    }

    void OnStartGame()
    {
        if (m_IsTransitioningToGameplay)
        {
            return;
        }

        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnLobbyPlayersChanged -= OnLobbyPlayersChanged;
        }

        StartCoroutine(StartGameplayTransitionRoutine());
    }

    IEnumerator StartGameplayTransitionRoutine()
    {
        BeginGameplayTransition("Opening Drop Corridor", "Booting host and preparing the arena...");
        SetStatus("Starting match...");

        yield return new WaitForSecondsRealtime(0.35f);
        m_LoadingOverlay.SetMessage("Starting host authority...");
        m_LoadingOverlay.SetProgress(0.32f);

        bool hostStarted = NetworkManager.Singleton.IsHost || NetworkManager.Singleton.StartHost();
        if (!hostStarted)
        {
            m_IsTransitioningToGameplay = false;
            m_LoadingOverlay.Hide();
            SetStatus("Failed to start host");
            yield break;
        }

        yield return new WaitForSecondsRealtime(0.2f);
        m_LoadingOverlay.SetMessage("Loading combat zone...");
        m_LoadingOverlay.SetProgress(0.46f);
        NetworkManager.Singleton.SceneManager.LoadScene(GameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    void OnCopyCode()
    {
        GUIUtility.systemCopyBuffer = m_LobbyCode;
        SetStatus("Lobby code copied");
    }

    void OnBackFromLobby()
    {
        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnLobbyPlayersChanged -= OnLobbyPlayersChanged;
            LobbyManager.Instance.LeaveLobby();
        }

        if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsClient))
        {
            NetworkManager.Singleton.Shutdown();
        }

        ShowScreen(MenuScreenState.ModeSelect);
        SetStatus("Connected and ready");
    }

    void SetStatus(string message)
    {
        m_IsShowingPendingStatusMessage = false;
        if (m_StatusText != null)
        {
            m_StatusText.text = message;
        }
    }

    void SetInitialStatus(string message)
    {
        if (m_StatusText != null)
        {
            m_StatusText.text = message;
        }
    }

    public static void QueuePendingStatusMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        PlayerPrefs.SetString(k_PendingStatusMessageKey, message);
        PlayerPrefs.Save();
    }

    static string ConsumePendingStatusMessage()
    {
        if (!PlayerPrefs.HasKey(k_PendingStatusMessageKey))
        {
            return null;
        }

        string message = PlayerPrefs.GetString(k_PendingStatusMessageKey);
        PlayerPrefs.DeleteKey(k_PendingStatusMessageKey);
        PlayerPrefs.Save();
        return message;
    }

    void BuildUI()
    {
        GameObject canvasObj = new GameObject("MenuCanvas");
        canvasObj.transform.SetParent(transform);
        m_Canvas = canvasObj.AddComponent<Canvas>();
        m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        m_Canvas.sortingOrder = 200;

        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        BuildAnimatedBackground(canvasObj);
        BuildHeader(canvasObj);

        m_LandingPanel = CreateAnimatedCard(canvasObj, "LandingPanel", new Vector2(0, 30), new Vector2(700, 360));
        BuildLandingPanel(m_LandingPanel.gameObject);

        m_MainMenuPanel = CreateAnimatedCard(canvasObj, "MainMenuPanel", new Vector2(0, 40), new Vector2(440, 430));
        BuildMainMenu(m_MainMenuPanel.gameObject);

        m_LobbyPanel = CreateAnimatedCard(canvasObj, "LobbyPanel", new Vector2(0, 20), new Vector2(460, 520));
        BuildLobbyPanel(m_LobbyPanel.gameObject);

        m_JoinPanel = CreateAnimatedCard(canvasObj, "JoinPanel", new Vector2(0, 40), new Vector2(430, 320));
        BuildJoinPanel(m_JoinPanel.gameObject);

        BuildLoadingOverlay(canvasObj);
        BuildStatusBar(canvasObj);
    }

    void BuildAnimatedBackground(GameObject canvas)
    {
        CreatePanel(canvas, "BackgroundBase", BgPrimary, Vector2.zero, new Vector2(1920, 1080));

        GameObject grid = CreateContainer(canvas, "Grid", Vector2.zero, new Vector2(1920, 1080));
        for (int i = -4; i <= 4; i++)
        {
            CreateImage(grid, $"GridV_{i}", new Color(1f, 1f, 1f, 0.04f), new Vector2(i * 220f, 0f), new Vector2(1f, 1080f));
        }

        for (int i = -3; i <= 3; i++)
        {
            CreateImage(grid, $"GridH_{i}", new Color(1f, 1f, 1f, 0.03f), new Vector2(0f, i * 150f), new Vector2(1920f, 1f));
        }

        CreateAmbientBand(canvas, "GlowBandA", new Color(0.1f, 0.4f, 0.65f, 0.18f), new Vector2(-460f, 210f), new Vector2(760f, 220f), -22f, 0f);
        CreateAmbientBand(canvas, "GlowBandB", AccentPink, new Vector2(530f, -180f), new Vector2(680f, 260f), 16f, 1.2f);
        CreateAmbientBand(canvas, "GlowBandC", new Color(0.95f, 0.48f, 0.12f, 0.14f), new Vector2(620f, 260f), new Vector2(520f, 150f), -8f, 2.4f);
        CreateAmbientBand(canvas, "GlowBandD", new Color(0.12f, 0.78f, 0.58f, 0.12f), new Vector2(-690f, -250f), new Vector2(420f, 140f), 14f, 1.8f);

        for (int i = 0; i < 12; i++)
        {
            float x = -820f + i * 150f;
            float y = (i % 2 == 0) ? -360f + i * 18f : 320f - i * 14f;
            GameObject spark = CreatePanel(canvas, $"Spark_{i}", new Color(1f, 1f, 1f, 0.08f), new Vector2(x, y), new Vector2(6f, 6f));
            var floater = spark.AddComponent<FloatingUiElement>();
            floater.PositionAmplitude = new Vector2(18f + i, 10f + i * 0.3f);
            floater.PositionSpeed = 0.22f + i * 0.02f;
            floater.ScaleAmplitude = 0.18f;
            floater.RotationAmplitude = 12f;
            floater.AlphaPulseAmplitude = 0.08f;
            floater.PhaseOffset = i * 0.45f;
            floater.CaptureInitialState();
        }
    }

    void CreateAmbientBand(GameObject parent, string name, Color color, Vector2 position, Vector2 size, float rotation, float phase)
    {
        GameObject band = CreatePanel(parent, name, color, position, size);
        band.GetComponent<RectTransform>().localEulerAngles = new Vector3(0f, 0f, rotation);

        var floater = band.AddComponent<FloatingUiElement>();
        floater.PositionAmplitude = new Vector2(34f, 22f);
        floater.PositionSpeed = 0.14f;
        floater.ScaleAmplitude = 0.08f;
        floater.RotationAmplitude = 4f;
        floater.AlphaPulseAmplitude = 0.06f;
        floater.PhaseOffset = phase;
        floater.CaptureInitialState();
    }

    void BuildHeader(GameObject canvas)
    {
        CreateText(canvas, "Eyebrow", "TACTICAL RELAY // UNITY NETCODE ARENA", 11,
            AccentBlue, FontStyles.Bold, new Vector2(0f, 386f), new Vector2(720f, 20f));

        TextMeshProUGUI title = CreateText(canvas, "Title", "FIREZONE", 58,
            TextPrimary, FontStyles.Bold, new Vector2(0f, 330f), new Vector2(760f, 68f));
        title.characterSpacing = 14f;

        CreateText(canvas, "Subtitle", "Fast squad setup, decisive firefights, and a cleaner trip from menu to match.", 14,
            TextSecondary, FontStyles.Normal, new Vector2(0f, 288f), new Vector2(760f, 24f));

        CreateImage(canvas, "HeaderLineL", BorderFocus, new Vector2(-188f, 262f), new Vector2(140f, 2f));
        CreateImage(canvas, "HeaderLineR", BorderFocus, new Vector2(188f, 262f), new Vector2(140f, 2f));
    }

    void BuildLandingPanel(GameObject parent)
    {
        CreateTag(parent, "LandingTag", "DROP READY", new Vector2(0f, 112f), AccentOrangeText, AccentOrange);
        CreateText(parent, "LandingHeadline", "Bring the menu to life, then hit the arena.", 30,
            TextPrimary, FontStyles.Bold, new Vector2(0f, 46f), new Vector2(580f, 80f));
        CreateText(parent, "LandingBody", "Start opens the lobby controls, the backdrop breathes, and the handoff into gameplay now has room to feel deliberate.", 14,
            TextSecondary, FontStyles.Normal, new Vector2(0f, -18f), new Vector2(560f, 54f));

        Button startButton = CreateButton(parent, "PlayButton", "Play / Open Lobby", TextPrimary, AccentBlueDark,
            new Vector2(0f, -94f), new Vector2(280f, 50f));
        startButton.onClick.AddListener(OnOpenMainMenu);

        CreateText(parent, "LandingHint", "Build your name, create or join a lobby, then launch when the squad is ready.", 11,
            TextDim, FontStyles.Normal, new Vector2(0f, -146f), new Vector2(520f, 20f));
    }

    void BuildMainMenu(GameObject parent)
    {
        CreateTag(parent, "MenuTag", "LOBBY CONTROL", new Vector2(0f, 168f), AccentBlue, new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.14f));
        CreateText(parent, "MenuTitle", "Choose your entry route", 24,
            TextPrimary, FontStyles.Bold, new Vector2(0f, 126f), new Vector2(360f, 32f));
        CreateText(parent, "MenuSubtitle", "Public for fast matchmaking, private for direct squad invites.", 12,
            TextSecondary, FontStyles.Normal, new Vector2(0f, 98f), new Vector2(360f, 20f));

        CreateText(parent, "NameLabel", "Your name", 11,
            TextDim, FontStyles.Normal, new Vector2(-150f, 50f), new Vector2(100f, 16f), TextAlignmentOptions.Left);

        m_PlayerNameInput = CreateInputField(parent, "NameInput", "Enter your callsign...", new Vector2(0f, 18f), new Vector2(380f, 44f));

        Button publicButton = CreateButton(parent, "PublicButton", "Create public match",
            TextPrimary, AccentBlueDark, new Vector2(0f, -52f), new Vector2(380f, 46f));
        publicButton.onClick.AddListener(OnCreatePublicLobby);

        Button privateButton = CreateButton(parent, "PrivateButton", "Create private match",
            TextPrimary, BgInput, new Vector2(0f, -106f), new Vector2(380f, 46f), BorderSubtle);
        privateButton.onClick.AddListener(OnCreatePrivateLobby);

        CreateOrDivider(parent, "MainDivider", "or join", new Vector2(0f, -154f));

        Button quickJoinButton = CreateButton(parent, "QuickJoinButton", "Quick join",
            TextPrimary, BgInput, new Vector2(0f, -204f), new Vector2(380f, 46f), BorderSubtle);
        quickJoinButton.onClick.AddListener(OnQuickJoin);

        Button joinCodeButton = CreateButton(parent, "JoinCodeButton", "Join with code",
            AccentOrangeText, AccentOrange, new Vector2(0f, -258f), new Vector2(380f, 46f));
        joinCodeButton.onClick.AddListener(OnOpenJoinPanel);

        Button backButton = CreateButton(parent, "BackToLandingButton", "Back",
            TextSecondary, Color.clear, new Vector2(0f, -314f), new Vector2(140f, 36f), BorderSubtle);
        backButton.onClick.AddListener(OnReturnToLanding);
    }

    void BuildLobbyPanel(GameObject parent)
    {
        CreateTag(parent, "LobbyTag", "SQUAD ROOM", new Vector2(0f, 206f), AccentBlue, new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.14f));
        m_LobbyTypeText = CreateText(parent, "LobbyType", "Private squad lobby", 14,
            TextSecondary, FontStyles.Normal, new Vector2(0f, 166f), new Vector2(360f, 22f));

        m_LobbyCodeSection = CreateContainer(parent, "CodeSection", new Vector2(0f, 112f), new Vector2(390f, 70f));
        GameObject codeCard = CreatePanel(m_LobbyCodeSection, "CodeCard", BgInput, Vector2.zero, new Vector2(390f, 62f));
        AddBorder(codeCard, BorderFocus);
        CreateText(codeCard, "CodeLabel", "Lobby code", 10, TextSecondary, FontStyles.Normal,
            new Vector2(-110f, 0f), new Vector2(100f, 16f), TextAlignmentOptions.Left);
        m_LobbyCodeDisplay = CreateText(codeCard, "CodeValue", "------", 22, AccentBlue, FontStyles.Bold,
            new Vector2(28f, 0f), new Vector2(150f, 30f));
        m_LobbyCodeDisplay.characterSpacing = 8f;

        Button copyButton = CreateButton(codeCard, "CopyCodeButton", "Copy",
            AccentBlue, Color.clear, new Vector2(146f, 0f), new Vector2(74f, 30f), BorderFocus);
        copyButton.onClick.AddListener(OnCopyCode);

        m_PlayerCountText = CreateText(parent, "PlayerCount", "Players (1/4)", 13,
            TextSecondary, FontStyles.Normal, new Vector2(-94f, 54f), new Vector2(200f, 20f), TextAlignmentOptions.Left);

        GameObject listCard = CreatePanel(parent, "PlayerListCard", BgInput, new Vector2(0f, -34f), new Vector2(390f, 206f));
        AddBorder(listCard, BorderSubtle);

        float slotY = 72f;
        for (int i = 0; i < MaxPlayers; i++)
        {
            CreatePlayerSlot(listCard, i, new Vector2(0f, slotY));
            slotY -= 46f;
        }

        m_StartGameButton = CreateButton(parent, "StartButton", "\u25B6  Start game",
            AccentGreenText, AccentGreen, new Vector2(0f, -178f), new Vector2(390f, 48f));
        m_StartGameButton.onClick.AddListener(OnStartGame);

        Button backButton = CreateButton(parent, "LobbyBackButton", "\u2190  Leave lobby",
            TextSecondary, BgInput, new Vector2(0f, -234f), new Vector2(390f, 40f), BorderSubtle);
        backButton.onClick.AddListener(OnBackFromLobby);
    }

    void CreatePlayerSlot(GameObject parent, int index, Vector2 position)
    {
        GameObject slot = CreatePanel(parent, $"Slot_{index}", EmptySlot, position, new Vector2(350f, 40f));
        m_PlayerSlots.Add(slot);

        Image slotBg = slot.GetComponent<Image>();
        m_PlayerSlotBgs.Add(slotBg);

        GameObject icon = CreatePanel(slot, "Icon", AccentBlueDark, new Vector2(-150f, 0f), new Vector2(24f, 24f));
        m_PlayerSlotIcons.Add(icon);
        CreateText(icon, "Initial", (index + 1).ToString(), 11, AccentBlue, FontStyles.Bold, Vector2.zero, new Vector2(24f, 24f));
        icon.SetActive(false);

        TextMeshProUGUI nameText = CreateText(slot, "Name", string.Empty, 14, TextPrimary, FontStyles.Normal,
            new Vector2(12f, 0f), new Vector2(280f, 34f), TextAlignmentOptions.Left);
        nameText.gameObject.SetActive(false);
        m_PlayerSlotNames.Add(nameText);

        GameObject emptyDots = CreateContainer(slot, "EmptyDots", Vector2.zero, new Vector2(350f, 40f));
        for (int i = 0; i < 4; i++)
        {
            CreatePanel(emptyDots, $"Dot_{i}", new Color(1f, 1f, 1f, 0.06f), new Vector2(-28f + i * 18f, 0f), new Vector2(4f, 4f));
        }

        CreateText(emptyDots, "WaitingText", "Waiting for player...", 11, TextDim, FontStyles.Normal,
            new Vector2(34f, 0f), new Vector2(220f, 34f), TextAlignmentOptions.Left);

        m_PlayerSlotEmptyDots.Add(emptyDots);
    }

    void BuildJoinPanel(GameObject parent)
    {
        CreateTag(parent, "JoinTag", "DIRECT CONNECT", new Vector2(0f, 110f), AccentOrangeText, new Color(AccentOrange.r, AccentOrange.g, AccentOrange.b, 0.2f));
        CreateText(parent, "JoinTitle", "Enter lobby code", 24,
            TextPrimary, FontStyles.Bold, new Vector2(0f, 66f), new Vector2(340f, 30f));
        CreateText(parent, "JoinSubtitle", "Private squads can hand you a six-character code.", 12,
            TextSecondary, FontStyles.Normal, new Vector2(0f, 38f), new Vector2(340f, 18f));

        m_LobbyCodeInput = CreateInputField(parent, "CodeInput", "_ _ _ _ _ _", new Vector2(0f, -24f), new Vector2(380f, 50f));
        m_LobbyCodeInput.characterLimit = 6;
        m_LobbyCodeInput.contentType = TMP_InputField.ContentType.Alphanumeric;
        m_LobbyCodeInput.onValueChanged.AddListener(OnLobbyCodeChanged);
        m_LobbyCodeInput.textComponent.alignment = TextAlignmentOptions.Center;
        m_LobbyCodeInput.textComponent.characterSpacing = 10f;
        m_LobbyCodeInput.textComponent.fontSize = 22f;

        var placeholder = m_LobbyCodeInput.placeholder as TextMeshProUGUI;
        if (placeholder != null)
        {
            placeholder.alignment = TextAlignmentOptions.Center;
            placeholder.characterSpacing = 10f;
            placeholder.fontSize = 22f;
        }

        Button joinButton = CreateButton(parent, "JoinButton", "Join lobby",
            AccentOrangeText, AccentOrange, new Vector2(0f, -98f), new Vector2(380f, 48f));
        joinButton.onClick.AddListener(OnJoinByCode);

        Button backButton = CreateButton(parent, "JoinBackButton", "Back",
            TextSecondary, BgInput, new Vector2(0f, -154f), new Vector2(380f, 40f), BorderSubtle);
        backButton.onClick.AddListener(() => ShowScreen(MenuScreenState.ModeSelect));
    }

    void OnLobbyCodeChanged(string value)
    {
        string normalized = value.ToUpper();
        if (m_LobbyCodeInput.text != normalized)
        {
            m_LobbyCodeInput.SetTextWithoutNotify(normalized);
        }
    }

    void BuildLoadingOverlay(GameObject canvas)
    {
        GameObject overlay = CreatePanel(canvas, "LoadingOverlay", new Color(0.03f, 0.04f, 0.07f, 0.97f),
            Vector2.zero, new Vector2(1920f, 1080f));
        CanvasGroup group = overlay.AddComponent<CanvasGroup>();

        GameObject frame = CreatePanel(overlay, "LoadingFrame", new Color(0.08f, 0.1f, 0.15f, 0.92f),
            new Vector2(0f, 20f), new Vector2(560f, 260f));
        AddBorder(frame, BorderFocus);

        TextMeshProUGUI title = CreateText(frame, "LoadingTitle", "Opening Drop Corridor", 28,
            TextPrimary, FontStyles.Bold, new Vector2(0f, 76f), new Vector2(440f, 34f));
        TextMeshProUGUI message = CreateText(frame, "LoadingMessage", "Preparing the arena...", 13,
            TextSecondary, FontStyles.Normal, new Vector2(0f, 38f), new Vector2(440f, 20f));

        GameObject spinner = CreatePanel(frame, "Spinner", Color.clear, new Vector2(-216f, 68f), new Vector2(42f, 42f));
        CreateImage(spinner, "SpinnerRingA", AccentBlue, Vector2.zero, new Vector2(42f, 4f));
        CreateImage(spinner, "SpinnerRingB", new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.35f), new Vector2(0f, 14f), new Vector2(30f, 3f));

        GameObject track = CreatePanel(frame, "ProgressTrack", BgInput, new Vector2(0f, -12f), new Vector2(440f, 12f));
        AddBorder(track, BorderSubtle);

        GameObject fill = CreatePanel(track, "ProgressFill", AccentBlue, new Vector2(-220f, 0f), new Vector2(440f, 12f));
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.anchorMin = new Vector2(0f, 0.5f);
        fillRect.anchorMax = new Vector2(0f, 0.5f);
        fillRect.anchoredPosition = new Vector2(-220f, 0f);
        fillRect.localScale = new Vector3(0.001f, 1f, 1f);

        TextMeshProUGUI progressText = CreateText(frame, "ProgressText", "0%", 12,
            TextSecondary, FontStyles.Bold, new Vector2(204f, -34f), new Vector2(60f, 18f), TextAlignmentOptions.Right);

        CreateText(frame, "LoadingFooter", "Session handoff and scene sync are intentionally staged here.", 11,
            TextDim, FontStyles.Normal, new Vector2(0f, -82f), new Vector2(420f, 18f));

        m_LoadingOverlay = overlay.AddComponent<MenuLoadingOverlay>();
        m_LoadingOverlay.Bind(group, spinner.GetComponent<RectTransform>(), fillRect, title, message, progressText);
    }

    void BuildStatusBar(GameObject canvas)
    {
        GameObject statusBar = CreatePanel(canvas, "StatusBar", BgSurface, new Vector2(0f, -472f), new Vector2(420f, 34f));
        AddBorder(statusBar, BorderSubtle);

        GameObject dot = CreatePanel(statusBar, "StatusDot", AccentBlue, new Vector2(-182f, 0f), new Vector2(6f, 6f));
        var dotFloat = dot.AddComponent<FloatingUiElement>();
        dotFloat.PositionAmplitude = new Vector2(0f, 0f);
        dotFloat.ScaleAmplitude = 0.22f;
        dotFloat.AlphaPulseAmplitude = 0.18f;
        dotFloat.AlphaPulseSpeed = 1.2f;
        dotFloat.CaptureInitialState();

        m_StatusText = CreateText(statusBar, "StatusText", "Initializing services...", 11,
            TextSecondary, FontStyles.Normal, new Vector2(8f, 0f), new Vector2(320f, 26f), TextAlignmentOptions.Left);
        CreateText(canvas, "Version", "v0.2.0", 10, TextDim, FontStyles.Normal,
            new Vector2(-872f, -510f), new Vector2(100f, 20f), TextAlignmentOptions.Left);
    }

    AnimatedMenuPanel CreateAnimatedCard(GameObject parent, string name, Vector2 position, Vector2 size)
    {
        GameObject card = CreatePanel(parent, name, BgSurface, position, size);
        AddBorder(card, BorderSubtle);
        card.AddComponent<CanvasGroup>();
        AnimatedMenuPanel animatedPanel = card.AddComponent<AnimatedMenuPanel>();
        animatedPanel.HiddenOffset = new Vector2(0f, 30f);
        animatedPanel.Duration = 0.28f;
        return animatedPanel;
    }

    void CreateTag(GameObject parent, string name, string text, Vector2 position, Color textColor, Color bgColor)
    {
        GameObject tag = CreatePanel(parent, name, bgColor, position, new Vector2(160f, 26f));
        AddBorder(tag, new Color(textColor.r, textColor.g, textColor.b, 0.32f));
        TextMeshProUGUI label = CreateText(tag, "Label", text, 10, textColor, FontStyles.Bold, Vector2.zero, new Vector2(150f, 20f));
        label.characterSpacing = 4f;
    }

    GameObject CreateContainer(GameObject parent, string name, Vector2 position, Vector2 size)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return obj;
    }

    GameObject CreatePanel(GameObject parent, string name, Color color, Vector2 position, Vector2 size)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image image = obj.AddComponent<Image>();
        image.color = color;
        return obj;
    }

    Image CreateImage(GameObject parent, string name, Color color, Vector2 position, Vector2 size)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image image = obj.AddComponent<Image>();
        image.color = color;
        return image;
    }

    TextMeshProUGUI CreateText(
        GameObject parent,
        string name,
        string text,
        float fontSize,
        Color color,
        FontStyles style,
        Vector2 position,
        Vector2 size,
        TextAlignmentOptions alignment = TextAlignmentOptions.Center)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        TextMeshProUGUI textComponent = obj.AddComponent<TextMeshProUGUI>();
        textComponent.text = text;
        textComponent.fontSize = fontSize;
        textComponent.color = color;
        textComponent.fontStyle = style;
        textComponent.alignment = alignment;
        textComponent.enableWordWrapping = true;

        if (textComponent.font == null)
        {
            textComponent.font = TMP_Settings.defaultFontAsset;
        }

        return textComponent;
    }

    Button CreateButton(
        GameObject parent,
        string name,
        string label,
        Color textColor,
        Color bgColor,
        Vector2 position,
        Vector2 size,
        Color? borderColor = null)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image image = obj.AddComponent<Image>();
        image.color = bgColor;

        if (borderColor.HasValue)
        {
            AddBorder(obj, borderColor.Value);
        }

        Button button = obj.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.88f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        button.colors = colors;

        obj.AddComponent<MenuButtonAnimator>();

        TextMeshProUGUI labelText = CreateText(obj, "Label", label, 13, textColor, FontStyles.Bold, Vector2.zero, size);
        labelText.enableWordWrapping = false;
        return button;
    }

    TMP_InputField CreateInputField(GameObject parent, string name, string placeholder, Vector2 position, Vector2 size)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image bg = obj.AddComponent<Image>();
        bg.color = BgInput;
        AddBorder(obj, BorderSubtle);

        GameObject textArea = new GameObject("TextArea");
        textArea.transform.SetParent(obj.transform, false);
        RectTransform textAreaRect = textArea.AddComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(14f, 6f);
        textAreaRect.offsetMax = new Vector2(-14f, -6f);
        textArea.AddComponent<RectMask2D>();

        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(textArea.transform, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TextMeshProUGUI inputText = textObj.AddComponent<TextMeshProUGUI>();
        inputText.fontSize = 15f;
        inputText.color = TextPrimary;
        inputText.alignment = TextAlignmentOptions.MidlineLeft;
        inputText.enableWordWrapping = false;
        if (inputText.font == null)
        {
            inputText.font = TMP_Settings.defaultFontAsset;
        }

        GameObject placeholderObj = new GameObject("Placeholder");
        placeholderObj.transform.SetParent(textArea.transform, false);
        RectTransform placeholderRect = placeholderObj.AddComponent<RectTransform>();
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = Vector2.zero;
        placeholderRect.offsetMax = Vector2.zero;

        TextMeshProUGUI placeholderText = placeholderObj.AddComponent<TextMeshProUGUI>();
        placeholderText.text = placeholder;
        placeholderText.fontSize = 15f;
        placeholderText.color = TextDim;
        placeholderText.alignment = TextAlignmentOptions.MidlineLeft;
        placeholderText.enableWordWrapping = false;
        if (placeholderText.font == null)
        {
            placeholderText.font = TMP_Settings.defaultFontAsset;
        }

        TMP_InputField input = obj.AddComponent<TMP_InputField>();
        input.textViewport = textAreaRect;
        input.textComponent = inputText;
        input.placeholder = placeholderText;
        input.caretColor = AccentBlue;
        return input;
    }

    void CreateOrDivider(GameObject parent, string name, string text, Vector2 position)
    {
        CreateImage(parent, name + "_L", BorderSubtle, new Vector2(position.x - 120f, position.y), new Vector2(126f, 1f));
        CreateText(parent, name + "_T", text, 11, TextDim, FontStyles.Normal, position, new Vector2(70f, 16f));
        CreateImage(parent, name + "_R", BorderSubtle, new Vector2(position.x + 120f, position.y), new Vector2(126f, 1f));
    }

    void AddBorder(GameObject obj, Color color)
    {
        Outline outline = obj.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(1f, 1f);
    }

    void OnDestroy()
    {
        UnsubscribeNetworkCallbacks();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnUnitySceneLoaded;

        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnLobbyPlayersChanged -= OnLobbyPlayersChanged;
        }
    }
}
