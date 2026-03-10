using System.Collections.Generic;
using TMPro;
using Unity.FPS.Game;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class ScoreboardUI : MonoBehaviour
{
    // Colors
    static readonly Color BgDark = new Color(0.04f, 0.055f, 0.08f, 0.95f);
    static readonly Color BgCard = new Color(0.08f, 0.11f, 0.15f, 0.9f);
    static readonly Color BgRow = new Color(0.1f, 0.13f, 0.18f, 0.8f);
    static readonly Color BgRowLocal = new Color(0f, 0.9f, 1f, 0.1f);
    static readonly Color AccentCyan = new Color(0f, 0.9f, 1f);
    static readonly Color AccentOrange = new Color(1f, 0.42f, 0.17f);
    static readonly Color TextPrimary = new Color(0.91f, 0.93f, 0.95f);
    static readonly Color TextSecondary = new Color(0.42f, 0.5f, 0.58f);
    static readonly Color TextDim = new Color(0.23f, 0.29f, 0.36f);

    Canvas m_Canvas;
    GameObject m_ScoreboardPanel;
    GameObject m_RowsContainer;
    TextMeshProUGUI m_TimerText;
    TextMeshProUGUI m_KillsHUDText;
    TextMeshProUGUI m_DeathsHUDText;
    GameFlowManager m_GameFlowManager;

    List<GameObject> m_ScoreRows = new List<GameObject>();

    void Start()
    {
        BuildUI();
        m_ScoreboardPanel.SetActive(false);
    }

    void Update()
    {
        // Lazy find GameFlowManager
        if (m_GameFlowManager == null)
        {
            m_GameFlowManager = FindFirstObjectByType<GameFlowManager>();
            if (m_GameFlowManager == null) return;
        }

        // Update timer
        UpdateTimer();

        // Update mini HUD kills/deaths
        UpdateMiniHUD();

        // Toggle scoreboard with Tab
        if (Input.GetKeyDown(KeyCode.Q))
        {
            m_ScoreboardPanel.SetActive(true);
            RefreshScoreboard();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (Input.GetKeyUp(KeyCode.Q))
        {
            m_ScoreboardPanel.SetActive(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void UpdateTimer()
    {
        float time = m_GameFlowManager.MatchTimer.Value;
        int minutes = Mathf.FloorToInt(time / 60f);
        int seconds = Mathf.FloorToInt(time % 60f);
        m_TimerText.text = $"{minutes:00}:{seconds:00}";

        // Flash red when under 30 seconds
        if (time <= 30f)
        {
            m_TimerText.color = Color.Lerp(AccentOrange, TextPrimary,
                Mathf.PingPong(Time.time * 2f, 1f));
        }
        else
        {
            m_TimerText.color = TextPrimary;
        }
    }

    void UpdateMiniHUD()
    {
        if (NetworkManager.Singleton == null) return;

        ulong localId = NetworkManager.Singleton.LocalClientId;
        int kills = m_GameFlowManager.GetKills(localId);
        int deaths = m_GameFlowManager.GetDeaths(localId);

        m_KillsHUDText.text = kills.ToString();
        m_DeathsHUDText.text = deaths.ToString();
    }

    void RefreshScoreboard()
    {
        // Clear old rows
        foreach (var row in m_ScoreRows)
        {
            Destroy(row);
        }
        m_ScoreRows.Clear();

        if (m_GameFlowManager == null) return;

        // Build sorted player list
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

        // Sort by kills descending
        players.Sort((a, b) => b.Kills.CompareTo(a.Kills));

        ulong localId = NetworkManager.Singleton.LocalClientId;
        float yPos = -45;

        for (int i = 0; i < players.Count; i++)
        {
            bool isLocal = players[i].ClientId == localId;
            CreateScoreRow(m_RowsContainer, players[i], i + 1, isLocal, yPos);
            yPos -= 44;
        }
    }

    void BuildUI()
    {
        // ===== CANVAS =====
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

        // ===== TOP HUD BAR =====
        BuildTopHUD(canvasObj);

        // ===== SCOREBOARD PANEL (Tab toggle) =====
        BuildScoreboardPanel(canvasObj);
    }

    void BuildTopHUD(GameObject canvas)
    {
        // Timer (top center)
        GameObject timerBg = CreatePanel(canvas, "TimerBg", BgDark,
            new Vector2(0, -30), new Vector2(140, 50),
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));

        m_TimerText = CreateText(timerBg, "TimerText", "03:00", 28,
            TextPrimary, FontStyles.Bold, Vector2.zero, new Vector2(130, 45));

        // Kills (top center left)
        GameObject killsBg = CreatePanel(canvas, "KillsBg", BgDark,
            new Vector2(-100, -30), new Vector2(80, 50),
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));

        CreateText(killsBg, "KillsLabel", "K", 12,
            AccentCyan, FontStyles.Bold, new Vector2(-20, 0), new Vector2(30, 45));

        m_KillsHUDText = CreateText(killsBg, "KillsValue", "0", 24,
            TextPrimary, FontStyles.Bold, new Vector2(12, 0), new Vector2(50, 45));

        // Deaths (top center right)
        GameObject deathsBg = CreatePanel(canvas, "DeathsBg", BgDark,
            new Vector2(100, -30), new Vector2(80, 50),
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));

        CreateText(deathsBg, "DeathsLabel", "D", 12,
            AccentOrange, FontStyles.Bold, new Vector2(-20, 0), new Vector2(30, 45));

        m_DeathsHUDText = CreateText(deathsBg, "DeathsValue", "0", 24,
            TextPrimary, FontStyles.Bold, new Vector2(12, 0), new Vector2(50, 45));

        // Tab hint
        CreateText(canvas, "TabHint", "[ TAB ] SCOREBOARD", 10,
            TextDim, FontStyles.Normal, new Vector2(0, -65),
            new Vector2(200, 20), TextAlignmentOptions.Center,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
    }

    void BuildScoreboardPanel(GameObject canvas)
    {
        // Dark overlay
        m_ScoreboardPanel = CreatePanel(canvas, "ScoreboardOverlay",
            new Color(0, 0, 0, 0.6f), Vector2.zero, new Vector2(1920, 1080));

        // Scoreboard container
        GameObject board = CreatePanel(m_ScoreboardPanel, "Board", BgDark,
            new Vector2(0, 40), new Vector2(600, 400));

        // Title
        CreateText(board, "Title", "SCOREBOARD", 14,
            AccentCyan, FontStyles.Bold, new Vector2(0, 170), new Vector2(560, 30));

        // Divider
        CreateImage(board, "Divider", AccentCyan * 0.3f,
            new Vector2(0, 152), new Vector2(560, 1));

        // Column headers
        float headerY = 135;
        CreateText(board, "RankHeader", "#", 12, TextDim, FontStyles.Normal,
            new Vector2(-250, headerY), new Vector2(40, 25), TextAlignmentOptions.Center);
        CreateText(board, "PlayerHeader", "PLAYER", 12, TextDim, FontStyles.Normal,
            new Vector2(-120, headerY), new Vector2(200, 25), TextAlignmentOptions.Left);
        CreateText(board, "KillsHeader", "KILLS", 12, TextDim, FontStyles.Normal,
            new Vector2(120, headerY), new Vector2(80, 25), TextAlignmentOptions.Center);
        CreateText(board, "DeathsHeader", "DEATHS", 12, TextDim, FontStyles.Normal,
            new Vector2(210, headerY), new Vector2(80, 25), TextAlignmentOptions.Center);
        CreateText(board, "KDHeader", "K/D", 12, TextDim, FontStyles.Normal,
            new Vector2(270, headerY), new Vector2(60, 25), TextAlignmentOptions.Center);

        // Rows container
        m_RowsContainer = new GameObject("RowsContainer");
        m_RowsContainer.transform.SetParent(board.transform, false);
        RectTransform rowsRect = m_RowsContainer.AddComponent<RectTransform>();
        rowsRect.anchoredPosition = new Vector2(0, 70);
        rowsRect.sizeDelta = new Vector2(560, 280);

        // Match info at bottom
        CreateText(board, "MatchInfo", "DEATHMATCH // FIRST TO SURVIVE", 10,
            TextDim, FontStyles.Normal, new Vector2(0, -180), new Vector2(560, 20));
    }

    void CreateScoreRow(GameObject parent, PlayerScoreData data, int rank,
        bool isLocal, float yPos)
    {
        Color rowColor = isLocal ? BgRowLocal : BgRow;
        Color nameColor = isLocal ? AccentCyan : TextPrimary;

        GameObject row = CreatePanel(parent, $"Row_{data.ClientId}", rowColor,
            new Vector2(0, yPos), new Vector2(560, 40));
        m_ScoreRows.Add(row);

        // Rank indicator
        if (rank == 1)
        {
            CreateImage(row, "RankBar", AccentCyan,
                new Vector2(-278, 0), new Vector2(3, 36));
        }

        // Rank number
        CreateText(row, "Rank", rank.ToString(), 16,
            rank == 1 ? AccentCyan : TextSecondary, FontStyles.Bold,
            new Vector2(-250, 0), new Vector2(40, 35), TextAlignmentOptions.Center);

        // Player name
        string playerName = isLocal ? "YOU" : $"Player {data.ClientId}";
        CreateText(row, "Name", playerName, 16,
            nameColor, FontStyles.Bold,
            new Vector2(-120, 0), new Vector2(200, 35), TextAlignmentOptions.Left);

        // Kills
        CreateText(row, "Kills", data.Kills.ToString(), 18,
            TextPrimary, FontStyles.Bold,
            new Vector2(120, 0), new Vector2(80, 35), TextAlignmentOptions.Center);

        // Deaths
        CreateText(row, "Deaths", data.Deaths.ToString(), 18,
            TextPrimary, FontStyles.Normal,
            new Vector2(210, 0), new Vector2(80, 35), TextAlignmentOptions.Center);

        // K/D ratio
        float kd = data.Deaths > 0
            ? (float)data.Kills / data.Deaths
            : data.Kills;
        Color kdColor = kd >= 1f ? AccentCyan : AccentOrange;
        CreateText(row, "KD", kd.ToString("F1"), 16,
            kdColor, FontStyles.Bold,
            new Vector2(270, 0), new Vector2(60, 35), TextAlignmentOptions.Center);
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
        {
            tmp.font = TMP_Settings.defaultFontAsset;
        }

        return tmp;
    }

    struct PlayerScoreData
    {
        public ulong ClientId;
        public int Kills;
        public int Deaths;
    }
}