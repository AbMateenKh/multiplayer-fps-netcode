using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;

public class MenuUI : MonoBehaviour
{
    enum MenuScreenState
    {
        Landing,
        ModeSelect,
        Join,
        Lobby,
        Settings
    }

    [Header("Settings")]
    public string GameSceneName = "MainScene";
    public int MaxPlayers = 4;
    public ushort OfflinePort = 7777;

    static readonly Color BgPrimary = VanguardUITheme.Base;
    static readonly Color BgSurface = VanguardUITheme.Panel;
    static readonly Color BgInput = VanguardUITheme.PanelSoft;
    static readonly Color BgPlayerRow = new Color32(18, 23, 29, 236);
    static readonly Color BgPlayerRowHost = VanguardUITheme.AmberSoft;
    static readonly Color AccentBlue = VanguardUITheme.Amber;
    static readonly Color AccentBlueDark = VanguardUITheme.Amber;
    static readonly Color AccentOrange = VanguardUITheme.Amber;
    static readonly Color AccentOrangeText = new Color32(26, 18, 6, 255);
    static readonly Color AccentGreen = VanguardUITheme.GreenSoft;
    static readonly Color AccentGreenText = VanguardUITheme.Green;
    static readonly Color TextPrimary = VanguardUITheme.Ink;
    static readonly Color TextSecondary = VanguardUITheme.InkDim;
    static readonly Color TextDim = VanguardUITheme.InkFaint;
    static readonly Color BorderSubtle = VanguardUITheme.Border;
    static readonly Color BorderFocus = VanguardUITheme.BorderStrong;
    static readonly Color EmptySlot = new Color32(243, 242, 242, 8);

    const float k_MinGameplayTransitionDuration = 1f;
    const string k_PendingStatusMessageKey = "NetcodeFPS.PendingMenuStatus";
    const string k_MasterVolumePrefsKey = "NetcodeFPS.MasterVolume";

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
    AnimatedMenuPanel m_SettingsPanel;
    MenuLoadingOverlay m_LoadingOverlay;
    VanguardMenuToolkit m_Toolkit;

    GameObject m_LobbyCodeSection;
    Button m_StartGameButton;
    Button m_ReadyButton;
    TextMeshProUGUI m_ReadyButtonLabel;
    TextMeshProUGUI m_MenuSensitivityValue;
    TextMeshProUGUI m_MenuVolumeValue;

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
    bool m_IsDestroyed;
    bool m_NetworkSceneEventsSubscribed;

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

    void Update()
    {
        EnsureNetworkSceneSubscription();
        m_Toolkit?.Tick(Time.unscaledTime);
        if (m_Toolkit != null && m_Toolkit.IsRenderable && m_Canvas != null && m_Canvas.enabled)
        {
            m_Canvas.enabled = false;
        }
    }

    async void CheckServicesReady()
    {
        while (!ServicesInitializer.IsInitialized && !m_IsDestroyed)
        {
            await Task.Delay(100);
        }

        if (!m_IsDestroyed && !m_IsShowingPendingStatusMessage)
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

        EnsureNetworkSceneSubscription();
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    void EnsureNetworkSceneSubscription()
    {
        if (m_NetworkSceneEventsSubscribed ||
            NetworkManager.Singleton == null ||
            NetworkManager.Singleton.SceneManager == null)
        {
            return;
        }

        NetworkManager.Singleton.SceneManager.OnSceneEvent += OnNetworkSceneEvent;
        m_NetworkSceneEventsSubscribed = true;
    }

    void UnsubscribeNetworkCallbacks()
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        if (m_NetworkSceneEventsSubscribed && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnNetworkSceneEvent;
        }
        m_NetworkSceneEventsSubscribed = false;

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
            m_Toolkit?.SetLoading("Deploying To Arena", "Streaming arena geometry...", 0.7f);
        }
        else if (sceneEvent.SceneEventType == SceneEventType.LoadComplete &&
                 sceneEvent.ClientId == NetworkManager.Singleton.LocalClientId)
        {
            m_LoadingOverlay.SetMessage("Scene loaded. Finalizing spawn state...");
            m_LoadingOverlay.SetProgress(0.88f);
            m_Toolkit?.SetLoading("Deploying To Arena", "Scene loaded. Finalizing spawn state...", 0.88f);
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

        if (!m_IsTransitioningToGameplay)
        {
            BeginGameplayTransition("Deploying To Arena", "Synchronizing with the host...");
        }

        if (m_IsTransitionCompleting)
        {
            return;
        }

        m_LoadingOverlay.SetMessage("Scene activated. Finalizing deployment...");
        m_LoadingOverlay.SetProgress(0.96f);
        m_Toolkit?.SetLoading("Deploying To Arena", "Scene activated. Finalizing deployment...", 0.96f);
        StartCoroutine(FinishGameplayTransition());
    }

    void ShowScreen(MenuScreenState state, bool instant = false)
    {
        m_CurrentScreen = state;

        TogglePanel(m_LandingPanel, state == MenuScreenState.Landing, instant);
        TogglePanel(m_MainMenuPanel, state == MenuScreenState.ModeSelect, instant);
        TogglePanel(m_JoinPanel, state == MenuScreenState.Join, instant);
        TogglePanel(m_LobbyPanel, state == MenuScreenState.Lobby, instant);
        TogglePanel(m_SettingsPanel, state == MenuScreenState.Settings, instant);
        m_Toolkit?.ShowScreen(state.ToString());

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
        m_LobbyTypeText.text = isPrivate ? "PRIVATE LOBBY" : "PUBLIC LOBBY";
        int maxPlayers = LobbyManager.Instance != null ? LobbyManager.Instance.GetMaxPlayers() : MaxPlayers;
        m_Toolkit?.SetLobbyHeader(isPrivate, m_LobbyCode, 1, maxPlayers);
        RefreshLobbyControls();
        ShowScreen(MenuScreenState.Lobby);
        LobbyManager.Instance?.RefreshPlayersSnapshot();
    }

    void ShowLoading(string title, string message, float progress)
    {
        m_LoadingOverlay.Show(title, message);
        m_LoadingOverlay.SetProgress(progress);
        m_Toolkit?.ShowLoading(title, message, progress);
    }

    void HideLoading()
    {
        if (!m_IsTransitioningToGameplay)
        {
            m_LoadingOverlay.Hide();
            m_Toolkit?.HideLoading();
        }
    }

    void BeginGameplayTransition(string title, string message)
    {
        m_IsTransitioningToGameplay = true;
        ShowLoading(title, message, 0.18f);
        m_LoadingOverlay.SetTitle(title);
        m_LoadingOverlay.SetMessage(message);
        m_LoadingOverlay.SetProgress(0.18f);
        m_Toolkit?.ShowLoading(title, message, 0.18f);
    }

    IEnumerator FinishGameplayTransition()
    {
        m_IsTransitionCompleting = true;
        m_LoadingOverlay.SetTitle("Arena Ready");
        m_LoadingOverlay.SetMessage("Drop sequence complete...");
        m_LoadingOverlay.SetProgress(1f);
        m_Toolkit?.SetLoading("Arena Ready", "Drop sequence complete...", 1f);

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
        m_PlayerName = m_Toolkit != null && !string.IsNullOrWhiteSpace(m_Toolkit.PlayerName)
            ? m_Toolkit.PlayerName
            : m_PlayerNameInput.text.Trim();
        if (string.IsNullOrEmpty(m_PlayerName))
        {
            m_PlayerName = "Player";
        }

        return m_PlayerName;
    }

    void OnOpenMainMenu()
    {
        ShowScreen(MenuScreenState.ModeSelect);
        SetStatus("Select a match type");
    }

    void OnStartSinglePlayer()
    {
        if (m_IsTransitioningToGameplay)
        {
            return;
        }

        StartCoroutine(StartSinglePlayerTransitionRoutine());
    }

    IEnumerator StartSinglePlayerTransitionRoutine()
    {
        BeginGameplayTransition("Local Deployment", "Preparing solo combat simulation...");
        SetStatus("Starting single player...");

        yield return new WaitForSecondsRealtime(0.25f);

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            CancelGameplayTransition("Network bootstrap is missing");
            yield break;
        }

        if (networkManager.IsListening)
        {
            networkManager.Shutdown();
            yield return null;
        }

        UnityTransport transport = networkManager.GetComponent<UnityTransport>();
        if (transport == null)
        {
            CancelGameplayTransition("Unity Transport is missing");
            yield break;
        }

        m_LoadingOverlay.SetMessage("Starting local authority...");
        m_LoadingOverlay.SetProgress(0.38f);
        m_Toolkit?.SetLoading("Local Deployment", "Starting local authority...", 0.38f);

#if UNITY_WEBGL && !UNITY_EDITOR
        float servicesTimeout = Time.realtimeSinceStartup + 12f;
        while (!ServicesInitializer.IsInitialized && Time.realtimeSinceStartup < servicesTimeout)
        {
            yield return null;
        }

        if (!ServicesInitializer.IsInitialized || RelayManager.Instance == null)
        {
            CancelGameplayTransition("Online services are required for solo play in a browser");
            yield break;
        }

        m_LoadingOverlay.SetMessage("Opening secure browser session...");
        m_Toolkit?.SetLoading("Local Deployment", "Opening secure browser session...", 0.45f);
        Task<string> relayTask = RelayManager.Instance.CreateRelay(1);
        while (!relayTask.IsCompleted)
        {
            yield return null;
        }

        if (relayTask.IsFaulted || string.IsNullOrEmpty(relayTask.Result))
        {
            CancelGameplayTransition(RelayManager.Instance.LastErrorMessage);
            yield break;
        }
#else
        transport.UseWebSockets = false;
        transport.SetConnectionData(true, "127.0.0.1", OfflinePort, "127.0.0.1");
        if (!networkManager.StartHost())
        {
            CancelGameplayTransition($"Could not start the local session on port {OfflinePort}");
            yield break;
        }
#endif

        yield return new WaitForSecondsRealtime(0.15f);

        m_LoadingOverlay.SetMessage("Loading combat zone...");
        m_LoadingOverlay.SetProgress(0.55f);
        m_Toolkit?.SetLoading("Local Deployment", "Loading combat zone...", 0.55f);
        networkManager.SceneManager.LoadScene(
            GameSceneName,
            UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    void CancelGameplayTransition(string message)
    {
        m_IsTransitioningToGameplay = false;
        m_IsTransitionCompleting = false;
        m_LoadingOverlay.Hide();
        m_Toolkit?.HideLoading();
        SetStatus(message);
        UnlockCursor();
    }

    void OnOpenSettings()
    {
        RefreshMenuSettings();
        ShowScreen(MenuScreenState.Settings);
        SetStatus("Settings saved automatically");
    }

    void OnExitGame()
    {
#if UNITY_EDITOR
        Debug.Log("[Menu] Exit requested. Application.Quit is ignored in the editor.");
#else
        Application.Quit();
#endif
    }

    void AdjustMenuSensitivity(float delta)
    {
        float current = PlayerPrefs.GetFloat(PlayerInputHandler.LookSensitivityPrefsKey, 1f);
        PlayerPrefs.SetFloat(PlayerInputHandler.LookSensitivityPrefsKey, Mathf.Clamp(current + delta, 0.2f, 3f));
        PlayerPrefs.Save();
        RefreshMenuSettings();
    }

    void AdjustMenuVolume(float delta)
    {
        float current = PlayerPrefs.GetFloat(k_MasterVolumePrefsKey, AudioUtility.GetMasterVolume());
        float next = Mathf.Clamp01(current + delta);
        AudioUtility.SetMasterVolume(next);
        PlayerPrefs.SetFloat(k_MasterVolumePrefsKey, next);
        PlayerPrefs.Save();
        RefreshMenuSettings();
    }

    void ToggleMenuFullscreen()
    {
        Screen.fullScreen = !Screen.fullScreen;
        RefreshMenuSettings();
    }

    void RefreshMenuSettings()
    {
        if (m_MenuSensitivityValue != null)
        {
            m_MenuSensitivityValue.text =
                PlayerPrefs.GetFloat(PlayerInputHandler.LookSensitivityPrefsKey, 1f).ToString("0.0");
        }

        if (m_MenuVolumeValue != null)
        {
            float volume = PlayerPrefs.GetFloat(k_MasterVolumePrefsKey, AudioUtility.GetMasterVolume());
            m_MenuVolumeValue.text = $"{Mathf.RoundToInt(volume * 100f)}%";
        }

        float toolkitSensitivity = PlayerPrefs.GetFloat(PlayerInputHandler.LookSensitivityPrefsKey, 1f);
        float toolkitVolume = PlayerPrefs.GetFloat(k_MasterVolumePrefsKey, AudioUtility.GetMasterVolume());
        m_Toolkit?.SetSettings(toolkitSensitivity, toolkitVolume);
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
            SetStatus(GetLobbyError("Failed to create public lobby"));
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
            SetStatus(GetLobbyError("Failed to create private lobby"));
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
            SetStatus(GetLobbyError("No public matches found"));
            ShowScreen(MenuScreenState.ModeSelect);
        }
    }

    async void OnJoinByCode()
    {
        string code = m_Toolkit != null && !string.IsNullOrWhiteSpace(m_Toolkit.JoinCode)
            ? m_Toolkit.JoinCode
            : m_LobbyCodeInput.text.Trim().ToUpper();
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
            SetStatus(GetLobbyError("Failed to join lobby"));
        }
    }

    void OnLobbyPlayersChanged(List<PlayerLobbyData> players)
    {
        int max = LobbyManager.Instance.GetMaxPlayers();
        m_PlayerCountText.text = $"PLAYERS {players.Count}/{max}";
        m_Toolkit?.SetLobbyHeader(m_IsPrivateLobby, m_LobbyCode, players.Count, max);
        m_Toolkit?.SetPlayers(players, max);

        for (int i = 0; i < m_PlayerSlots.Count; i++)
        {
            if (i < players.Count)
            {
                m_PlayerSlotNames[i].text = players[i].IsHost
                    ? $"\u2605 {players[i].Name} (Host)"
                    : players[i].IsReady ? $"{players[i].Name}  Ready" : $"{players[i].Name}  Not ready";
                m_PlayerSlotNames[i].color = players[i].IsHost ? AccentBlue : players[i].IsReady ? AccentGreenText : TextPrimary;
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

        RefreshLobbyControls();
    }

    async void OnToggleReady()
    {
        if (LobbyManager.Instance == null || LobbyManager.Instance.IsHost())
            return;

        bool newReadyState = !LobbyManager.Instance.IsLocalPlayerReady();
        SetStatus(newReadyState ? "Marking you ready..." : "Marking you not ready...");

        bool success = await LobbyManager.Instance.SetLocalReady(newReadyState);
        if (success)
        {
            SetStatus(newReadyState ? "Ready. Waiting for host" : "Not ready");
        }
        else
        {
            SetStatus(GetLobbyError("Could not update ready state"));
        }

        RefreshLobbyControls();
    }

    void RefreshLobbyControls()
    {
        if (LobbyManager.Instance == null || m_StartGameButton == null || m_ReadyButton == null)
            return;

        bool isHost = LobbyManager.Instance.IsHost();
        bool allReady = LobbyManager.Instance.AreAllPlayersReady();
        bool localReady = LobbyManager.Instance.IsLocalPlayerReady();

        m_StartGameButton.gameObject.SetActive(isHost);
        m_StartGameButton.interactable = isHost && allReady && !m_IsTransitioningToGameplay;
        m_ReadyButton.gameObject.SetActive(!isHost);
        m_ReadyButton.interactable = !m_IsTransitioningToGameplay;

        if (m_ReadyButtonLabel != null)
        {
            m_ReadyButtonLabel.text = localReady ? "CANCEL READY" : "READY UP";
            m_ReadyButtonLabel.color = localReady ? AccentGreenText : TextPrimary;
        }

        Image startImage = m_StartGameButton.GetComponent<Image>();
        if (startImage != null)
        {
            startImage.color = allReady ? AccentOrange : BgInput;
        }

        TextMeshProUGUI startLabel = m_StartGameButton.GetComponentInChildren<TextMeshProUGUI>();
        if (startLabel != null)
        {
            startLabel.text = isHost && !allReady ? "WAITING FOR READY PLAYERS" : "START MATCH";
            startLabel.color = allReady ? AccentOrangeText : TextSecondary;
        }

        m_Toolkit?.SetLobbyControls(isHost, allReady, localReady, m_IsTransitioningToGameplay);
    }

    string GetLobbyError(string fallback)
    {
        if (LobbyManager.Instance != null && !string.IsNullOrEmpty(LobbyManager.Instance.LastErrorMessage))
        {
            return LobbyManager.Instance.LastErrorMessage;
        }

        if (RelayManager.Instance != null && !string.IsNullOrEmpty(RelayManager.Instance.LastErrorMessage))
        {
            return RelayManager.Instance.LastErrorMessage;
        }

        return fallback;
    }

    void OnStartGame()
    {
        if (m_IsTransitioningToGameplay)
        {
            return;
        }

        if (LobbyManager.Instance != null && !LobbyManager.Instance.AreAllPlayersReady())
        {
            SetStatus("Waiting for every player to ready up");
            RefreshLobbyControls();
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
        m_Toolkit?.SetLoading("Opening Drop Corridor", "Starting host authority...", 0.32f);

        bool hostStarted = NetworkManager.Singleton.IsHost || NetworkManager.Singleton.StartHost();
        if (!hostStarted)
        {
            m_IsTransitioningToGameplay = false;
            m_LoadingOverlay.Hide();
            SetStatus(GetLobbyError("Failed to start host"));
            yield break;
        }

        yield return new WaitForSecondsRealtime(0.2f);
        m_LoadingOverlay.SetMessage("Loading combat zone...");
        m_LoadingOverlay.SetProgress(0.46f);
        m_Toolkit?.SetLoading("Deploying To Arena", "Loading combat zone...", 0.46f);
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
        m_Toolkit?.SetStatus(message);
    }

    void SetInitialStatus(string message)
    {
        if (m_StatusText != null)
        {
            m_StatusText.text = message;
        }
        m_Toolkit?.SetStatus(message);
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
        VanguardUITheme.ConfigureScaler(scaler);

        canvasObj.AddComponent<GraphicRaycaster>();

        BuildAnimatedBackground(canvasObj);
        GameObject safeArea = VanguardUITheme.CreateSafeArea(canvasObj);
        VanguardUITheme.AddCornerFrame(safeArea);
        BuildHeader(safeArea);

        m_LandingPanel = CreateAnimatedCard(safeArea, "LandingPanel", new Vector2(0, -16), new Vector2(1660, 650));
        BuildLandingPanel(m_LandingPanel.gameObject);

        m_MainMenuPanel = CreateAnimatedCard(safeArea, "MainMenuPanel", new Vector2(0, -12), new Vector2(1660, 770));
        BuildMainMenu(m_MainMenuPanel.gameObject);

        m_LobbyPanel = CreateAnimatedCard(safeArea, "LobbyPanel", new Vector2(0, -8), new Vector2(1660, 800));
        BuildLobbyPanel(m_LobbyPanel.gameObject);

        m_JoinPanel = CreateAnimatedCard(safeArea, "JoinPanel", new Vector2(0, -12), new Vector2(1660, 770));
        BuildJoinPanel(m_JoinPanel.gameObject);

        m_SettingsPanel = CreateAnimatedCard(safeArea, "SettingsPanel", new Vector2(0, -8), new Vector2(1280, 770));
        BuildSettingsPanel(m_SettingsPanel.gameObject);

        BuildLoadingOverlay(canvasObj);
        BuildStatusBar(safeArea);

        m_Toolkit = new VanguardMenuToolkit(this);
    }

    void BuildAnimatedBackground(GameObject canvas)
    {
        CreatePanel(canvas, "BackgroundBase", BgPrimary, Vector2.zero, new Vector2(2400, 1400));

        GameObject grid = CreateContainer(canvas, "Grid", Vector2.zero, new Vector2(1920, 1080));
        for (int i = -4; i <= 4; i++)
        {
            CreateImage(grid, $"GridV_{i}", new Color(1f, 1f, 1f, 0.04f), new Vector2(i * 220f, 0f), new Vector2(1f, 1080f));
        }

        for (int i = -3; i <= 3; i++)
        {
            CreateImage(grid, $"GridH_{i}", new Color(1f, 1f, 1f, 0.03f), new Vector2(0f, i * 150f), new Vector2(1920f, 1f));
        }

        CreateImage(canvas, "TopRule", BorderSubtle, new Vector2(0f, 424f), new Vector2(2400f, 2f));
        CreateImage(canvas, "BottomRule", BorderSubtle, new Vector2(0f, -424f), new Vector2(2400f, 2f));
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
        GameObject identity = CreateContainer(canvas, "Identity", new Vector2(-744f, 428f), new Vector2(360f, 74f));
        GameObject badge = CreatePanel(identity, "Badge", Color.clear, new Vector2(-142f, 0f), new Vector2(58f, 58f));
        AddBorder(badge, BorderFocus);
        CreateText(badge, "Number", "07", 22, AccentBlue, FontStyles.Bold, Vector2.zero, new Vector2(54f, 54f));
        CreateText(identity, "Callsign", "OPERATIVE-07", 20, TextPrimary, FontStyles.Bold,
            new Vector2(6f, 12f), new Vector2(230f, 28f), TextAlignmentOptions.Left);
        CreateText(identity, "Rank", "RANK 14 · 1,204 XP", 13, TextSecondary, FontStyles.Normal,
            new Vector2(18f, -18f), new Vector2(254f, 22f), TextAlignmentOptions.Left);

        CreateText(canvas, "Connection", "▂▄▆  ONLINE · RELAY READY", 13,
            TextSecondary, FontStyles.Bold, new Vector2(724f, 442f), new Vector2(330f, 28f),
            TextAlignmentOptions.Right);
    }

    void BuildLandingPanel(GameObject parent)
    {
        TextMeshProUGUI title = CreateText(parent, "LandingHeadline", "VANGUARD\nPROTOCOL", 76,
            TextPrimary, FontStyles.Bold, new Vector2(-448f, 78f), new Vector2(720f, 210f),
            TextAlignmentOptions.Left);
        title.lineSpacing = -12f;

        TextMeshProUGUI tagline = CreateText(parent, "LandingBody", "4-PLAYER TACTICAL DEATHMATCH", 20,
            AccentBlue, FontStyles.Normal, new Vector2(-448f, -62f), new Vector2(720f, 34f),
            TextAlignmentOptions.Left);
        tagline.characterSpacing = 5f;

        Button singlePlayerButton = CreateButton(parent, "SinglePlayerButton", "SINGLE PLAYER",
            AccentOrangeText, AccentBlueDark, new Vector2(-684f, -166f), new Vector2(250f, 76f));
        singlePlayerButton.onClick.AddListener(OnStartSinglePlayer);

        Button multiplayerButton = CreateButton(parent, "MultiplayerButton", "MULTIPLAYER",
            TextPrimary, Color.clear, new Vector2(-404f, -166f), new Vector2(250f, 76f), BorderFocus);
        multiplayerButton.onClick.AddListener(OnOpenMainMenu);

        Button settingsButton = CreateButton(parent, "SettingsButton", "SETTINGS", TextPrimary, Color.clear,
            new Vector2(-139f, -166f), new Vector2(220f, 76f), BorderFocus);
        settingsButton.onClick.AddListener(OnOpenSettings);

        Button exitButton = CreateButton(parent, "ExitButton", "EXIT", TextSecondary, Color.clear,
            new Vector2(54f, -166f), new Vector2(110f, 76f));
        exitButton.onClick.AddListener(OnExitGame);

        CreateText(parent, "Build", "BUILD 0.4.12 · PROTOTYPE ART PASS", 12,
            TextSecondary, FontStyles.Normal, new Vector2(630f, -286f), new Vector2(380f, 24f),
            TextAlignmentOptions.Right);
    }

    void BuildMainMenu(GameObject parent)
    {
        Button backButton = CreateButton(parent, "BackToLandingButton", "‹  BACK",
            TextSecondary, Color.clear, new Vector2(-754f, 324f), new Vector2(160f, 44f));
        backButton.onClick.AddListener(OnReturnToLanding);

        CreateText(parent, "MenuTag", "MULTIPLAYER", 15, AccentBlue, FontStyles.Normal,
            new Vector2(-704f, 244f), new Vector2(260f, 24f), TextAlignmentOptions.Left);
        CreateText(parent, "MenuTitle", "Select Match Type", 42,
            TextPrimary, FontStyles.Bold, new Vector2(-544f, 190f), new Vector2(580f, 56f),
            TextAlignmentOptions.Left);

        CreateText(parent, "NameLabel", "CALLSIGN", 11, TextSecondary, FontStyles.Bold,
            new Vector2(-690f, 116f), new Vector2(180f, 20f), TextAlignmentOptions.Left);
        m_PlayerNameInput = CreateInputField(parent, "NameInput", "Enter your callsign",
            new Vector2(-508f, 78f), new Vector2(520f, 52f));

        Button publicButton = CreateButton(parent, "PublicButton", "CREATE PUBLIC",
            TextPrimary, Color.clear, new Vector2(-622f, -4f), new Vector2(390f, 72f), BorderFocus);
        publicButton.onClick.AddListener(OnCreatePublicLobby);

        Button privateButton = CreateButton(parent, "PrivateButton", "CREATE PRIVATE",
            TextPrimary, Color.clear, new Vector2(-208f, -4f), new Vector2(390f, 72f), BorderFocus);
        privateButton.onClick.AddListener(OnCreatePrivateLobby);

        Button joinCodeButton = CreateButton(parent, "JoinCodeButton", "JOIN CODE",
            AccentOrangeText, AccentOrange, new Vector2(208f, -4f), new Vector2(390f, 72f));
        joinCodeButton.onClick.AddListener(OnOpenJoinPanel);

        Button quickJoinButton = CreateButton(parent, "QuickJoinButton", "QUICK JOIN",
            TextPrimary, Color.clear, new Vector2(622f, -4f), new Vector2(390f, 72f), BorderFocus);
        quickJoinButton.onClick.AddListener(OnQuickJoin);

        GameObject info = CreatePanel(parent, "ModeInfo", Color.clear,
            new Vector2(0f, -208f), new Vector2(1600f, 270f));
        AddBorder(info, BorderFocus);
        CreateText(info, "Title", "CHOOSE HOW YOUR SQUAD CONNECTS", 15, TextSecondary, FontStyles.Bold,
            new Vector2(0f, 84f), new Vector2(700f, 26f));
        CreateText(info, "Body",
            "Public and quick join use matchmaking. Private matches create a six-character code for direct invites.",
            16, TextPrimary, FontStyles.Normal, new Vector2(0f, 26f), new Vector2(980f, 52f));
        CreateText(info, "Hint", "Lobby capacity 4 · Host starts the match when every client is ready",
            13, TextSecondary, FontStyles.Normal, new Vector2(0f, -48f), new Vector2(900f, 24f));
    }

    void BuildLobbyPanel(GameObject parent)
    {
        m_LobbyTypeText = CreateText(parent, "LobbyType", "PRIVATE LOBBY", 15,
            AccentBlue, FontStyles.Normal, new Vector2(-704f, 318f), new Vector2(280f, 24f),
            TextAlignmentOptions.Left);

        m_LobbyCodeSection = CreateContainer(parent, "CodeSection", new Vector2(-532f, 258f), new Vector2(620f, 72f));
        GameObject codeCard = CreatePanel(m_LobbyCodeSection, "CodeCard", Color.clear, Vector2.zero, new Vector2(620f, 66f));
        CreateText(codeCard, "CodeLabel", "LOBBY CODE", 10, TextSecondary, FontStyles.Bold,
            new Vector2(-234f, 18f), new Vector2(130f, 16f), TextAlignmentOptions.Left);
        m_LobbyCodeDisplay = CreateText(codeCard, "CodeValue", "------", 30, TextPrimary, FontStyles.Bold,
            new Vector2(-132f, -8f), new Vector2(310f, 42f), TextAlignmentOptions.Left);
        m_LobbyCodeDisplay.characterSpacing = 8f;

        Button copyButton = CreateButton(codeCard, "CopyCodeButton", "COPY",
            TextSecondary, Color.clear, new Vector2(176f, -4f), new Vector2(132f, 48f), BorderFocus);
        copyButton.onClick.AddListener(OnCopyCode);

        m_PlayerCountText = CreateText(parent, "PlayerCount", "PLAYERS 1/4", 13,
            TextSecondary, FontStyles.Bold, new Vector2(690f, 316f), new Vector2(220f, 22f),
            TextAlignmentOptions.Right);

        GameObject listCard = CreateContainer(parent, "PlayerList", new Vector2(0f, -6f), new Vector2(1600f, 390f));
        for (int i = 0; i < MaxPlayers; i++)
        {
            float x = i % 2 == 0 ? -410f : 410f;
            float y = 104f - (i / 2) * 178f;
            CreatePlayerSlot(listCard, i, new Vector2(x, y));
        }

        m_StartGameButton = CreateButton(parent, "StartButton", "START MATCH",
            AccentOrangeText, AccentOrange, new Vector2(544f, -326f), new Vector2(520f, 76f));
        m_StartGameButton.onClick.AddListener(OnStartGame);

        m_ReadyButton = CreateButton(parent, "ReadyButton", "READY UP",
            AccentGreenText, AccentGreen, new Vector2(-676f, -326f), new Vector2(260f, 76f), AccentGreenText);
        m_ReadyButtonLabel = m_ReadyButton.GetComponentInChildren<TextMeshProUGUI>();
        m_ReadyButton.onClick.AddListener(OnToggleReady);

        Button backButton = CreateButton(parent, "LobbyBackButton", "LEAVE LOBBY",
            TextSecondary, Color.clear, new Vector2(-430f, -326f), new Vector2(210f, 76f), BorderSubtle);
        backButton.onClick.AddListener(OnBackFromLobby);
    }

    void CreatePlayerSlot(GameObject parent, int index, Vector2 position)
    {
        GameObject slot = CreatePanel(parent, $"Slot_{index}", EmptySlot, position, new Vector2(780f, 136f));
        AddBorder(slot, index == 0 ? AccentBlue : BorderFocus);
        m_PlayerSlots.Add(slot);

        Image slotBg = slot.GetComponent<Image>();
        m_PlayerSlotBgs.Add(slotBg);

        GameObject icon = CreatePanel(slot, "Icon", BgInput, new Vector2(-330f, 0f), new Vector2(76f, 76f));
        AddBorder(icon, index == 0 ? AccentBlue : BorderFocus);
        m_PlayerSlotIcons.Add(icon);
        CreateText(icon, "Initial", (index + 1).ToString("00"), 20, AccentBlue, FontStyles.Bold,
            Vector2.zero, new Vector2(70f, 70f));
        icon.SetActive(false);

        TextMeshProUGUI nameText = CreateText(slot, "Name", string.Empty, 20, TextPrimary, FontStyles.Bold,
            new Vector2(22f, 0f), new Vector2(580f, 68f), TextAlignmentOptions.Left);
        nameText.gameObject.SetActive(false);
        m_PlayerSlotNames.Add(nameText);

        GameObject emptyDots = CreateContainer(slot, "EmptySlot", Vector2.zero, new Vector2(760f, 120f));
        CreateText(emptyDots, "WaitingText", "⊕  WAITING FOR PLAYER", 16, TextSecondary, FontStyles.Bold,
            Vector2.zero, new Vector2(460f, 50f));

        m_PlayerSlotEmptyDots.Add(emptyDots);
    }

    void BuildJoinPanel(GameObject parent)
    {
        Button backButton = CreateButton(parent, "JoinBackButton", "‹  BACK",
            TextSecondary, Color.clear, new Vector2(-754f, 324f), new Vector2(160f, 44f));
        backButton.onClick.AddListener(() => ShowScreen(MenuScreenState.ModeSelect));

        CreateText(parent, "JoinTag", "MULTIPLAYER", 15, AccentBlue, FontStyles.Normal,
            new Vector2(-704f, 244f), new Vector2(260f, 24f), TextAlignmentOptions.Left);
        CreateText(parent, "JoinTitle", "Join Private Lobby", 42,
            TextPrimary, FontStyles.Bold, new Vector2(-528f, 190f), new Vector2(620f, 56f),
            TextAlignmentOptions.Left);

        GameObject codePanel = CreatePanel(parent, "CodePanel", Color.clear,
            new Vector2(0f, -88f), new Vector2(1600f, 500f));
        AddBorder(codePanel, BorderFocus);
        CreateText(codePanel, "Instruction", "ENTER 6-CHARACTER LOBBY CODE", 15,
            TextSecondary, FontStyles.Bold, new Vector2(0f, 156f), new Vector2(620f, 28f));

        m_LobbyCodeInput = CreateInputField(codePanel, "CodeInput", "_ _ _ _ _ _",
            new Vector2(0f, 70f), new Vector2(660f, 92f));
        m_LobbyCodeInput.characterLimit = 6;
        m_LobbyCodeInput.contentType = TMP_InputField.ContentType.Alphanumeric;
        m_LobbyCodeInput.onValueChanged.AddListener(OnLobbyCodeChanged);
        m_LobbyCodeInput.textComponent.alignment = TextAlignmentOptions.Center;
        m_LobbyCodeInput.textComponent.characterSpacing = 18f;
        m_LobbyCodeInput.textComponent.fontSize = 34f;

        var placeholder = m_LobbyCodeInput.placeholder as TextMeshProUGUI;
        if (placeholder != null)
        {
            placeholder.alignment = TextAlignmentOptions.Center;
            placeholder.characterSpacing = 18f;
            placeholder.fontSize = 34f;
        }

        Button joinButton = CreateButton(codePanel, "JoinButton", "JOIN LOBBY",
            AccentOrangeText, AccentOrange, new Vector2(0f, -48f), new Vector2(340f, 72f));
        joinButton.onClick.AddListener(OnJoinByCode);

        CreateText(codePanel, "QuickJoinHint", "NO CODE? RETURN AND USE QUICK JOIN FOR AN OPEN PUBLIC LOBBY",
            12, TextSecondary, FontStyles.Normal, new Vector2(0f, -150f), new Vector2(760f, 24f));
    }

    void BuildSettingsPanel(GameObject parent)
    {
        CreateText(parent, "SettingsEyebrow", "SYSTEM", 15, AccentBlue, FontStyles.Normal,
            new Vector2(-510f, 302f), new Vector2(220f, 24f), TextAlignmentOptions.Left);
        CreateText(parent, "SettingsTitle", "Settings", 42, TextPrimary, FontStyles.Bold,
            new Vector2(-430f, 252f), new Vector2(380f, 56f), TextAlignmentOptions.Left);

        GameObject board = CreatePanel(parent, "SettingsBoard", Color.clear,
            new Vector2(0f, -18f), new Vector2(1180f, 500f));
        AddBorder(board, BorderFocus);

        CreateText(board, "GameplayTab", "GAMEPLAY", 15, AccentOrangeText, FontStyles.Bold,
            new Vector2(-392f, 202f), new Vector2(392f, 70f));
        CreatePanel(board, "GameplayTabBg", AccentOrange, new Vector2(-392f, 202f), new Vector2(392f, 70f))
            .transform.SetAsFirstSibling();
        CreateText(board, "AudioTab", "AUDIO", 15, TextSecondary, FontStyles.Bold,
            new Vector2(0f, 202f), new Vector2(392f, 70f));
        CreateText(board, "VideoTab", "VIDEO", 15, TextSecondary, FontStyles.Bold,
            new Vector2(392f, 202f), new Vector2(392f, 70f));
        CreateImage(board, "TabRule", BorderFocus, new Vector2(0f, 166f), new Vector2(1180f, 2f));

        CreateMenuSettingsStepper(board, "Sensitivity", "Mouse Sensitivity", new Vector2(0f, 92f),
            out m_MenuSensitivityValue,
            () => AdjustMenuSensitivity(-0.1f), () => AdjustMenuSensitivity(0.1f));
        CreateMenuSettingsStepper(board, "Volume", "Master Volume", new Vector2(0f, 10f),
            out m_MenuVolumeValue,
            () => AdjustMenuVolume(-0.1f), () => AdjustMenuVolume(0.1f));

        Button fullscreen = CreateButton(board, "Fullscreen", "TOGGLE FULLSCREEN",
            TextPrimary, Color.clear, new Vector2(0f, -78f), new Vector2(1080f, 58f), BorderFocus);
        fullscreen.onClick.AddListener(ToggleMenuFullscreen);

        Button back = CreateButton(board, "SettingsBack", "BACK",
            AccentOrangeText, AccentOrange, new Vector2(416f, -184f), new Vector2(248f, 64f));
        back.onClick.AddListener(() => ShowScreen(MenuScreenState.Landing));
        RefreshMenuSettings();
    }

    void CreateMenuSettingsStepper(
        GameObject parent,
        string name,
        string label,
        Vector2 position,
        out TextMeshProUGUI valueText,
        UnityEngine.Events.UnityAction onMinus,
        UnityEngine.Events.UnityAction onPlus)
    {
        GameObject row = CreateContainer(parent, name, position, new Vector2(1080f, 64f));
        CreateText(row, "Label", label, 16, TextPrimary, FontStyles.Normal,
            new Vector2(-390f, 0f), new Vector2(300f, 36f), TextAlignmentOptions.Left);
        Button minus = CreateButton(row, "Minus", "−", TextPrimary, Color.clear,
            new Vector2(300f, 0f), new Vector2(54f, 44f), BorderFocus);
        minus.onClick.AddListener(onMinus);
        valueText = CreateText(row, "Value", "1.0", 17, AccentBlue, FontStyles.Bold,
            new Vector2(380f, 0f), new Vector2(90f, 40f));
        Button plus = CreateButton(row, "Plus", "+", TextPrimary, Color.clear,
            new Vector2(460f, 0f), new Vector2(54f, 44f), BorderFocus);
        plus.onClick.AddListener(onPlus);
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
        VanguardUITheme.AddCornerFrame(overlay);

        GameObject frame = CreateContainer(overlay, "LoadingFrame", new Vector2(0f, 20f), new Vector2(940f, 360f));

        TextMeshProUGUI title = CreateText(frame, "LoadingTitle", "DEPLOYING TO ARENA", 38,
            TextPrimary, FontStyles.Bold, new Vector2(0f, 112f), new Vector2(760f, 54f));
        TextMeshProUGUI message = CreateText(frame, "LoadingMessage", "Synchronizing squad state", 15,
            TextSecondary, FontStyles.Normal, new Vector2(0f, 58f), new Vector2(720f, 26f));

        GameObject spinner = CreateContainer(frame, "Spinner", new Vector2(0f, -6f), new Vector2(72f, 72f));
        CreateImage(spinner, "SpinnerRingA", AccentBlue, new Vector2(0f, 26f), new Vector2(72f, 4f));
        CreateImage(spinner, "SpinnerRingB", TextDim, new Vector2(0f, -26f), new Vector2(50f, 3f));

        GameObject track = CreatePanel(frame, "ProgressTrack", BgInput, new Vector2(0f, -88f), new Vector2(720f, 8f));
        AddBorder(track, BorderSubtle);

        GameObject fill = CreatePanel(track, "ProgressFill", AccentBlue, new Vector2(-360f, 0f), new Vector2(720f, 8f));
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.anchorMin = new Vector2(0f, 0.5f);
        fillRect.anchorMax = new Vector2(0f, 0.5f);
        fillRect.anchoredPosition = new Vector2(-360f, 0f);
        fillRect.localScale = new Vector3(0.001f, 1f, 1f);

        TextMeshProUGUI progressText = CreateText(frame, "ProgressText", "0%", 12,
            TextSecondary, FontStyles.Bold, new Vector2(330f, -114f), new Vector2(80f, 20f), TextAlignmentOptions.Right);

        CreateText(frame, "LoadingFooter", "TIP · MOVE WITH YOUR SQUAD AND CONTROL THE PICKUP LANES", 11,
            TextDim, FontStyles.Normal, new Vector2(0f, -146f), new Vector2(720f, 20f));

        m_LoadingOverlay = overlay.AddComponent<MenuLoadingOverlay>();
        m_LoadingOverlay.Bind(group, spinner.GetComponent<RectTransform>(), fillRect, title, message, progressText);
    }

    void BuildStatusBar(GameObject canvas)
    {
        GameObject statusBar = CreateContainer(canvas, "StatusBar", new Vector2(0f, -470f), new Vector2(720f, 34f));

        GameObject dot = CreatePanel(statusBar, "StatusDot", AccentBlue, new Vector2(-292f, 0f), new Vector2(6f, 6f));
        var dotFloat = dot.AddComponent<FloatingUiElement>();
        dotFloat.PositionAmplitude = new Vector2(0f, 0f);
        dotFloat.ScaleAmplitude = 0.22f;
        dotFloat.AlphaPulseAmplitude = 0.18f;
        dotFloat.AlphaPulseSpeed = 1.2f;
        dotFloat.CaptureInitialState();

        m_StatusText = CreateText(statusBar, "StatusText", "Initializing services...", 11,
            TextSecondary, FontStyles.Normal, new Vector2(22f, 0f), new Vector2(570f, 26f), TextAlignmentOptions.Left);
    }

    AnimatedMenuPanel CreateAnimatedCard(GameObject parent, string name, Vector2 position, Vector2 size)
    {
        GameObject card = CreatePanel(parent, name, Color.clear, position, size);
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
        textComponent.textWrappingMode = TextWrappingModes.Normal;

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
        labelText.textWrappingMode = TextWrappingModes.NoWrap;
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
        inputText.textWrappingMode = TextWrappingModes.NoWrap;
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
        placeholderText.textWrappingMode = TextWrappingModes.NoWrap;
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

    internal void ToolkitOpenMainMenu() => OnOpenMainMenu();
    internal void ToolkitStartSinglePlayer() => OnStartSinglePlayer();
    internal void ToolkitOpenSettings() => OnOpenSettings();
    internal void ToolkitExitGame() => OnExitGame();
    internal void ToolkitReturnToLanding() => OnReturnToLanding();
    internal void ToolkitCreatePublicLobby() => OnCreatePublicLobby();
    internal void ToolkitCreatePrivateLobby() => OnCreatePrivateLobby();
    internal void ToolkitOpenJoinPanel() => OnOpenJoinPanel();
    internal void ToolkitQuickJoin() => OnQuickJoin();
    internal void ToolkitJoinByCode() => OnJoinByCode();
    internal void ToolkitCopyCode() => OnCopyCode();
    internal void ToolkitToggleReady() => OnToggleReady();
    internal void ToolkitLeaveLobby() => OnBackFromLobby();
    internal void ToolkitStartGame() => OnStartGame();
    internal void ToolkitAdjustSensitivity(float delta) => AdjustMenuSensitivity(delta);
    internal void ToolkitAdjustVolume(float delta) => AdjustMenuVolume(delta);
    internal void ToolkitToggleFullscreen() => ToggleMenuFullscreen();

    void OnDestroy()
    {
        m_IsDestroyed = true;
        UnsubscribeNetworkCallbacks();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnUnitySceneLoaded;

        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.OnLobbyPlayersChanged -= OnLobbyPlayersChanged;
        }
    }
}
