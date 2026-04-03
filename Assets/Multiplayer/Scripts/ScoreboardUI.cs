using System.Collections.Generic;
using TMPro;
using Unity.FPS.Game;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class ScoreboardUI : MonoBehaviour
{
    // Colors
    static readonly Color BgOverlay = new Color(0, 0, 0, 0.6f);
    static readonly Color BgCard = new Color(0.06f, 0.07f, 0.09f, 0.95f);
    static readonly Color BgRow = new Color(0.09f, 0.1f, 0.13f, 0.9f);
    static readonly Color BgRowLocal = new Color(0.22f, 0.53f, 0.84f, 0.12f);
    static readonly Color BgHUD = new Color(0.06f, 0.07f, 0.09f, 0.85f);
    static readonly Color AccentBlue = new Color(0.33f, 0.66f, 0.96f);
    static readonly Color AccentGreen = new Color(0.23f, 0.43f, 0.07f);
    static readonly Color AccentGreenText = new Color(0.6f, 0.87f, 0.35f);
    static readonly Color AccentRed = new Color(0.89f, 0.29f, 0.29f);
    static readonly Color AccentAmber = new Color(0.94f, 0.62f, 0.15f);
    static readonly Color TextPrimary = new Color(0.91f, 0.93f, 0.95f);
    static readonly Color TextSecondary = new Color(0.55f, 0.58f, 0.63f);
    static readonly Color TextDim = new Color(0.35f, 0.38f, 0.43f);
    static readonly Color BorderSubtle = new Color(1f, 1f, 1f, 0.08f);

    Canvas m_Canvas;
    GameObject m_ScoreboardPanel;
    GameObject m_MatchEndPanel;
    GameObject m_RowsContainer;
    GameObject m_EndRowsContainer;
    TextMeshProUGUI m_TimerText;
    TextMeshProUGUI m_KillsText;
    TextMeshProUGUI m_DeathsText;
    TextMeshProUGUI m_ScoreboardTimerText;
    TextMeshProUGUI m_EndTitle;
    TextMeshProUGUI m_EndSubtitle;
    GameFlowManager m_GameFlowManager;

    List<GameObject> m_ScoreRows = new List<GameObject>();
    List<GameObject> m_EndScoreRows = new List<GameObject>();

    bool m_MatchEndShown = false;

    void Start()
    {
        BuildUI();
        m_ScoreboardPanel.SetActive(false);
        m_MatchEndPanel.SetActive(false);
    }

    void Update()
    {
        if (m_GameFlowManager == null)
        {
            m_GameFlowManager = FindObjectOfType<GameFlowManager>();
            if (m_GameFlowManager == null) return;

            // Subscribe to match end
            m_GameFlowManager.IsMatchOver.OnValueChanged += OnMatchOverChanged;
        }

        UpdateTimer();
        UpdateMiniHUD();

        // Tab toggle scoreboard (not during match end)
        if (!m_MatchEndShown)
        {
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                m_ScoreboardPanel.SetActive(true);
                RefreshScoreboard(m_RowsContainer, m_ScoreRows);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (Input.GetKeyUp(KeyCode.Tab))
            {
                m_ScoreboardPanel.SetActive(false);
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    void OnMatchOverChanged(bool prev, bool isOver)
    {
        if (isOver)
        {
            ShowMatchEnd();
        }
    }

    void ShowMatchEnd()
    {
        m_MatchEndShown = true;
        m_ScoreboardPanel.SetActive(false);
        m_MatchEndPanel.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Determine winner
        ulong localId = NetworkManager.Singleton.LocalClientId;
        ulong winnerId = m_GameFlowManager.GetWinnerId();
        int kills = m_GameFlowManager.GetKills(localId);
        int deaths = m_GameFlowManager.GetDeaths(localId);

        bool isWinner = localId == winnerId;
        m_EndTitle.text = isWinner ? "You won!" : "Match over";
        m_EndTitle.color = isWinner ? AccentGreenText : TextPrimary;
        m_EndSubtitle.text = $"{kills} kills, {deaths} deaths";

        RefreshScoreboard(m_EndRowsContainer, m_EndScoreRows);
    }

    void UpdateTimer()
    {
        float time = m_GameFlowManager.MatchTimer.Value;
        int minutes = Mathf.FloorToInt(time / 60f);
        int seconds = Mathf.FloorToInt(time % 60f);
        m_TimerText.text = $"{minutes:00}:{seconds:00}";

        if (time <= 30f)
        {
            m_TimerText.color = Color.Lerp(AccentRed, TextPrimary,
                Mathf.PingPong(Time.time * 2f, 1f));
        }
        else
        {
            m_TimerText.color = TextPrimary;
        }

        // Update scoreboard timer too
        if (m_ScoreboardTimerText != null)
        {
            m_ScoreboardTimerText.text = $"Scoreboard \u2014 {minutes:00}:{seconds:00} remaining";
        }
    }

    void UpdateMiniHUD()
    {
        if (NetworkManager.Singleton == null) return;

        ulong localId = NetworkManager.Singleton.LocalClientId;
        int kills = m_GameFlowManager.GetKills(localId);
        int deaths = m_GameFlowManager.GetDeaths(localId);

        m_KillsText.text = kills.ToString();
        m_DeathsText.text = deaths.ToString();
    }

    void RefreshScoreboard(GameObject container, List<GameObject> rows)
    {
        foreach (var row in rows) Destroy(row);
        rows.Clear();

        if (m_GameFlowManager == null) return;

        List<PlayerScoreData> players = new List<PlayerScoreData>();
        for (int i = 0; i < m_GameFlowManager.PlayerIds.Count; i++)
        {
            players.Add(new PlayerScoreData
            {
                ClientId = m_GameFlowManager.PlayerIds[i],
                Kills = m_GameFlowManager.PlayerKills[i],
                Deaths = m_GameFlowManager.PlayerDeaths[i]
            });
        }

        players.Sort((a, b) => b.Kills.CompareTo(a.Kills));

        ulong localId = NetworkManager.Singleton.LocalClientId;
        float yPos = -35;

        for (int i = 0; i < players.Count; i++)
        {
            bool isLocal = players[i].ClientId == localId;
            CreateScoreRow(container, rows, players[i], i + 1, isLocal, yPos);
            yPos -= 42;
        }
    }

    void BuildUI()
    {
        // Canvas
        GameObject canvasObj = new GameObject("ScoreboardCanvas");
        canvasObj.transform.SetParent(transform);
        m_Canvas = canvasObj.AddComponent<Canvas>();
        m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        m_Canvas.sortingOrder = 50;

        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        BuildHUD(canvasObj);
        BuildScoreboardOverlay(canvasObj);
        BuildMatchEndOverlay(canvasObj);
    }

    void BuildHUD(GameObject canvas)
    {
        // HUD bar
        GameObject hudBar = CreatePanel(canvas, "HUDBar", BgHUD,
            new Vector2(0, -30), new Vector2(260, 44),
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));

        // Kills section (left)
        GameObject killBadge = CreatePanel(hudBar, "KBadge",
            new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.15f),
            new Vector2(-95, 0), new Vector2(24, 18));
        CreateText(killBadge, "KLabel", "K", 10,
            AccentBlue, FontStyles.Bold, Vector2.zero, new Vector2(24, 18));

        m_KillsText = CreateText(hudBar, "Kills", "0", 22,
            TextPrimary, FontStyles.Bold, new Vector2(-62, 0), new Vector2(40, 40));

        // Timer (center) with borders
        CreateImage(hudBar, "DivL", BorderSubtle, new Vector2(-35, 0), new Vector2(1, 30));
        m_TimerText = CreateText(hudBar, "Timer", "03:00", 22,
            TextPrimary, FontStyles.Bold, new Vector2(0, 0), new Vector2(80, 40));
        CreateImage(hudBar, "DivR", BorderSubtle, new Vector2(35, 0), new Vector2(1, 30));

        // Deaths section (right)
        m_DeathsText = CreateText(hudBar, "Deaths", "0", 22,
            TextPrimary, FontStyles.Bold, new Vector2(62, 0), new Vector2(40, 40));

        GameObject deathBadge = CreatePanel(hudBar, "DBadge",
            new Color(AccentRed.r, AccentRed.g, AccentRed.b, 0.15f),
            new Vector2(95, 0), new Vector2(24, 18));
        CreateText(deathBadge, "DLabel", "D", 10,
            AccentRed, FontStyles.Bold, Vector2.zero, new Vector2(24, 18));

        // Tab hint
        CreateText(canvas, "TabHint", "Tab  Scoreboard", 10,
            TextDim, FontStyles.Normal, new Vector2(0, -60),
            new Vector2(160, 16), TextAlignmentOptions.Center,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
    }

    void BuildScoreboardOverlay(GameObject canvas)
    {
        m_ScoreboardPanel = CreatePanel(canvas, "ScoreOverlay", BgOverlay,
            Vector2.zero, new Vector2(1920, 1080));

        GameObject board = CreatePanel(m_ScoreboardPanel, "Board", BgCard,
            new Vector2(0, 40), new Vector2(520, 360));

        m_ScoreboardTimerText = CreateText(board, "SBTimer",
            "Scoreboard \u2014 03:00 remaining", 12,
            TextSecondary, FontStyles.Normal, new Vector2(0, 155), new Vector2(480, 20));

        // Column headers
        CreateScoreHeader(board, 130);

        // Rows container
        m_RowsContainer = new GameObject("Rows");
        m_RowsContainer.transform.SetParent(board.transform, false);
        RectTransform rowsRect = m_RowsContainer.AddComponent<RectTransform>();
        rowsRect.anchoredPosition = new Vector2(0, 60);
        rowsRect.sizeDelta = new Vector2(480, 240);

        // Footer
        CreateText(board, "Footer", "Deathmatch \u2014 Highest kills wins", 10,
            TextDim, FontStyles.Normal, new Vector2(0, -160), new Vector2(480, 16));
    }

    void BuildMatchEndOverlay(GameObject canvas)
    {
        m_MatchEndPanel = CreatePanel(canvas, "MatchEndOverlay", BgOverlay,
            Vector2.zero, new Vector2(1920, 1080));

        GameObject board = CreatePanel(m_MatchEndPanel, "EndBoard", BgCard,
            new Vector2(0, 60), new Vector2(520, 450));

        // Match complete label
        CreateText(board, "EndLabel", "Match complete", 11,
            TextDim, FontStyles.Normal, new Vector2(0, 195), new Vector2(480, 16));

        // Winner title
        m_EndTitle = CreateText(board, "EndTitle", "You won!", 26,
            AccentGreenText, FontStyles.Bold, new Vector2(0, 168), new Vector2(480, 35));

        // Stats subtitle
        m_EndSubtitle = CreateText(board, "EndStats", "0 kills, 0 deaths", 14,
            TextSecondary, FontStyles.Normal, new Vector2(0, 142), new Vector2(480, 20));

        // Divider
        CreateImage(board, "EndDiv", BorderSubtle, new Vector2(0, 125), new Vector2(480, 1));

        // Column headers
        CreateScoreHeader(board, 110);

        // Rows container
        m_EndRowsContainer = new GameObject("EndRows");
        m_EndRowsContainer.transform.SetParent(board.transform, false);
        RectTransform rowsRect = m_EndRowsContainer.AddComponent<RectTransform>();
        rowsRect.anchoredPosition = new Vector2(0, 40);
        rowsRect.sizeDelta = new Vector2(480, 200);

        // Buttons
        float btnY = -170;

        Button playAgain = CreateButton(board, "PlayAgain", "Play again",
            TextPrimary, new Color(0.05f, 0.27f, 0.49f),
            new Vector2(0, btnY), new Vector2(480, 44));
        playAgain.onClick.AddListener(OnPlayAgain);

        Button backToMenu = CreateButton(board, "BackMenu", "Back to menu",
            TextSecondary, new Color(0.09f, 0.1f, 0.13f),
            new Vector2(0, btnY - 50), new Vector2(480, 40));
        backToMenu.onClick.AddListener(OnBackToMenu);
    }

    void CreateScoreHeader(GameObject parent, float yPos)
    {
        float[] xPositions = { -210, -120, 100, 170, 220 };
        string[] labels = { "#", "Player", "Kills", "Deaths", "K/D" };
        TextAlignmentOptions[] aligns = {
            TextAlignmentOptions.Center, TextAlignmentOptions.Left,
            TextAlignmentOptions.Center, TextAlignmentOptions.Center,
            TextAlignmentOptions.Center
        };
        float[] widths = { 30, 160, 60, 60, 50 };

        for (int i = 0; i < labels.Length; i++)
        {
            CreateText(parent, $"H_{labels[i]}", labels[i], 10,
                TextDim, FontStyles.Normal, new Vector2(xPositions[i], yPos),
                new Vector2(widths[i], 16), aligns[i]);
        }

        CreateImage(parent, "HeaderDiv", BorderSubtle,
            new Vector2(0, yPos - 12), new Vector2(480, 1));
    }

    void CreateScoreRow(GameObject parent, List<GameObject> rows,
        PlayerScoreData data, int rank, bool isLocal, float yPos)
    {
        Color rowBg = isLocal ? BgRowLocal : BgRow;
        Color nameColor = isLocal ? AccentBlue : TextPrimary;

        GameObject row = CreatePanel(parent, $"Row_{data.ClientId}", rowBg,
            new Vector2(0, yPos), new Vector2(480, 36));
        rows.Add(row);

        // Rank
        CreateText(row, "Rank", rank.ToString(), 14,
            rank == 1 ? AccentBlue : TextSecondary, FontStyles.Bold,
            new Vector2(-210, 0), new Vector2(30, 32), TextAlignmentOptions.Center);

        // Name
        string playerName = isLocal ? "You" : $"Player {data.ClientId}";
        CreateText(row, "Name", playerName, 14,
            nameColor, FontStyles.Bold,
            new Vector2(-120, 0), new Vector2(160, 32), TextAlignmentOptions.Left);

        // Kills
        CreateText(row, "Kills", data.Kills.ToString(), 15,
            TextPrimary, FontStyles.Bold,
            new Vector2(100, 0), new Vector2(60, 32), TextAlignmentOptions.Center);

        // Deaths
        CreateText(row, "Deaths", data.Deaths.ToString(), 15,
            TextPrimary, FontStyles.Normal,
            new Vector2(170, 0), new Vector2(60, 32), TextAlignmentOptions.Center);

        // K/D
        float kd = data.Deaths > 0 ? (float)data.Kills / data.Deaths : data.Kills;
        Color kdColor = kd >= 1f ? AccentGreenText : AccentRed;
        CreateText(row, "KD", kd.ToString("F1"), 14,
            kdColor, FontStyles.Bold,
            new Vector2(220, 0), new Vector2(50, 32), TextAlignmentOptions.Center);
    }

    void OnPlayAgain()
    {
        // TODO: Return to lobby or restart match
        Debug.Log("[Scoreboard] Play again pressed");
    }

    void OnBackToMenu()
    {
        // Disconnect and return to menu
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }
        UnityEngine.SceneManagement.SceneManager.LoadScene("IntroMenu");
    }

    // ===== UI FACTORY METHODS =====

    GameObject CreatePanel(GameObject parent, string name, Color color,
        Vector2 position, Vector2 size,
        Vector2? anchorMin = null, Vector2? anchorMax = null)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        if (anchorMin.HasValue) rect.anchorMin = anchorMin.Value;
        if (anchorMax.HasValue) rect.anchorMax = anchorMax.Value;
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
        TextAlignmentOptions alignment = TextAlignmentOptions.Center,
        Vector2? anchorMin = null, Vector2? anchorMax = null)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent.transform, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        if (anchorMin.HasValue) rect.anchorMin = anchorMin.Value;
        if (anchorMax.HasValue) rect.anchorMax = anchorMax.Value;
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
            tmp.font = TMP_Settings.defaultFontAsset;

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
        colors.highlightedColor = new Color(1, 1, 1, 0.85f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        btn.colors = colors;

        CreateText(obj, "Label", label, 13,
            textColor, FontStyles.Bold, Vector2.zero, size);

        return btn;
    }

    struct PlayerScoreData
    {
        public ulong ClientId;
        public int Kills;
        public int Deaths;
    }

    void OnDestroy()
    {
        if (m_GameFlowManager != null)
        {
            m_GameFlowManager.IsMatchOver.OnValueChanged -= OnMatchOverChanged;
        }
    }
}