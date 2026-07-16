using System.Collections.Generic;
using TMPro;
using Unity.FPS.Game;
using Unity.FPS.Gameplay;
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
    GameObject m_HudBar;
    GameObject m_ScoreboardPanel;
    GameObject m_MatchEndPanel;
    GameObject m_DeathPanel;
    GameObject m_HelpPanel;
    GameObject m_RowsContainer;
    GameObject m_EndRowsContainer;
    TextMeshProUGUI m_TimerText;
    TextMeshProUGUI m_KillsText;
    TextMeshProUGUI m_DeathsText;
    TextMeshProUGUI m_ScoreboardTimerText;
    TextMeshProUGUI m_EndTitle;
    TextMeshProUGUI m_EndSubtitle;
    TextMeshProUGUI m_EndWinnerText;
    TextMeshProUGUI m_EndPlacementText;
    TextMeshProUGUI m_PlayAgainButtonLabel;
    Button m_PlayAgainButton;
    TextMeshProUGUI m_DeathTitleText;
    TextMeshProUGUI m_DeathSubtitleText;
    TextMeshProUGUI m_DeathTimerText;
    TextMeshProUGUI m_HitMarkerText;
    TextMeshProUGUI m_KillConfirmText;
    TextMeshProUGUI m_ProtectionText;
    TextMeshProUGUI m_PickupPromptText;
    TextMeshProUGUI m_PickupFeedbackText;
    TextMeshProUGUI m_CountdownText;
    TextMeshProUGUI m_CountdownLabelText;
    GameObject m_CountdownPanel;
    GameObject m_KillFeedContainer;
    GameFlowManager m_GameFlowManager;
    PlayerCharacterController m_LocalPlayer;
    Health m_LocalHealth;

    List<GameObject> m_ScoreRows = new List<GameObject>();
    List<GameObject> m_EndScoreRows = new List<GameObject>();
    List<GameObject> m_KillFeedRows = new List<GameObject>();

    bool m_MatchEndShown = false;
    bool m_DeathShown = false;
    float m_RespawnCountdownEndTime = Mathf.NegativeInfinity;
    float m_HitMarkerVisibleUntil = Mathf.NegativeInfinity;
    float m_HitMarkerDuration = 0.16f;
    float m_KillConfirmVisibleUntil = Mathf.NegativeInfinity;
    float m_PickupFeedbackVisibleUntil = Mathf.NegativeInfinity;

    void Start()
    {
        BuildUI();
        m_ScoreboardPanel.SetActive(false);
        m_MatchEndPanel.SetActive(false);
        m_DeathPanel.SetActive(false);
        m_HelpPanel.SetActive(false);
        m_HitMarkerText.gameObject.SetActive(false);
        m_KillConfirmText.gameObject.SetActive(false);
        m_ProtectionText.gameObject.SetActive(false);
        m_PickupPromptText.gameObject.SetActive(false);
        m_PickupFeedbackText.gameObject.SetActive(false);

        PlayerCharacterController.OnLocalPlayerSpawned += OnLocalPlayerSpawned;
        PlayerCharacterController.OnLocalShotConfirmed += OnLocalShotConfirmed;
        PlayerCharacterController.OnLocalShotBlocked += OnLocalShotBlocked;
        Pickup.OnLocalPickupConfirmed += OnLocalPickupConfirmed;
        GameFlowManager.OnPlayerKilled += OnPlayerKilled;
    }

    void Update()
    {
        if (m_GameFlowManager == null)
        {
            m_GameFlowManager = FindFirstObjectByType<GameFlowManager>();
            if (m_GameFlowManager == null) return;

            // Subscribe to match end
            m_GameFlowManager.IsMatchOver.OnValueChanged += OnMatchOverChanged;
        }

        UpdateTimer();
        UpdateMiniHUD();
        UpdateDeathOverlay();
        UpdateRespawnProtection();
        UpdatePickupPrompt();
        UpdatePickupFeedback();
        UpdateCountdown();
        UpdateCombatFeedback();
        UpdateHelpInput();

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
                if (m_HelpPanel == null || !m_HelpPanel.activeSelf)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
        }
    }

    void OnMatchOverChanged(bool prev, bool isOver)
    {
        if (isOver)
        {
            ShowMatchEnd();
        }
        else
        {
            HideMatchEnd();
        }
    }

    void ShowMatchEnd()
    {
        m_MatchEndShown = true;
        HideDeathOverlay();
        SetHelpOverlayVisible(false);
        m_ScoreboardPanel.SetActive(false);
        m_MatchEndPanel.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        ulong localId = NetworkManager.Singleton.LocalClientId;
        ulong winnerId = m_GameFlowManager.GetWinnerId();
        int kills = m_GameFlowManager.GetKills(localId);
        int deaths = m_GameFlowManager.GetDeaths(localId);
        int placement = GetLocalPlacement(localId);
        int playerCount = Mathf.Max(1, m_GameFlowManager.PlayerIds.Count);

        bool isWinner = localId == winnerId;
        m_EndTitle.text = isWinner ? "You won!" : "Match over";
        m_EndTitle.color = isWinner ? AccentGreenText : TextPrimary;
        m_EndSubtitle.text = $"{kills} kills, {deaths} deaths";
        m_EndWinnerText.text = $"Winner: {GetDisplayName(winnerId)}  |  {GetMatchEndReason(winnerId)}";
        m_EndPlacementText.text = $"Your placement: {FormatOrdinal(placement)} of {playerCount}";
        UpdateRoundControlButton();

        RefreshScoreboard(m_EndRowsContainer, m_EndScoreRows);
    }

    void OnLocalShotConfirmed()
    {
        ShowHitMarker("X", TextPrimary, 0.16f);
    }

    void OnLocalShotBlocked()
    {
        ShowHitMarker("PROTECTED", AccentBlue, 0.45f);
    }

    void ShowHitMarker(string text, Color color, float duration)
    {
        m_HitMarkerText.text = text;
        m_HitMarkerText.color = color;
        m_HitMarkerDuration = duration;
        m_HitMarkerVisibleUntil = Time.time + duration;
        m_HitMarkerText.gameObject.SetActive(true);
    }

    void OnLocalPlayerSpawned(PlayerCharacterController player)
    {
        m_LocalPlayer = player;
        m_LocalHealth = player != null ? player.GetComponent<Health>() : null;
    }

    void OnLocalPickupConfirmed(Pickup pickup, PlayerCharacterController player)
    {
        if (player != m_LocalPlayer || pickup == null || m_DeathShown)
            return;

        m_PickupFeedbackText.text = GetPickupFeedbackText(pickup);
        m_PickupFeedbackVisibleUntil = Time.time + 1.15f;
        m_PickupFeedbackText.gameObject.SetActive(true);
        m_PickupPromptText.gameObject.SetActive(false);
    }

    void OnPlayerKilled(ulong victimId, ulong killerId)
    {
        AddKillFeedRow(victimId, killerId);

        if (NetworkManager.Singleton == null)
            return;

        ulong localId = NetworkManager.Singleton.LocalClientId;

        if (victimId == localId)
        {
            ShowDeathOverlay(victimId, killerId);
        }

        if (killerId == localId && victimId != killerId)
        {
            m_KillConfirmText.text = "ELIMINATION";
            m_KillConfirmVisibleUntil = Time.time + 1.25f;
            m_KillConfirmText.gameObject.SetActive(true);
        }
    }

    void ShowDeathOverlay(ulong victimId, ulong killerId)
    {
        if (m_MatchEndShown)
            return;

        m_DeathShown = true;
        m_RespawnCountdownEndTime = Time.time + GameFlowManager.RespawnDelay;

        string killerName = GetDisplayName(killerId);
        m_DeathTitleText.text = victimId == killerId ? "You eliminated yourself" : "You were eliminated";
        m_DeathSubtitleText.text = victimId == killerId ? "Watch your footing" : $"By {killerName}";

        m_DeathPanel.SetActive(true);
        SetCombatHudVisible(false);
        m_HitMarkerText.gameObject.SetActive(false);
        m_KillConfirmText.gameObject.SetActive(false);
        m_ProtectionText.gameObject.SetActive(false);
    }

    void HideDeathOverlay()
    {
        m_DeathShown = false;
        m_DeathPanel.SetActive(false);
        SetCombatHudVisible(true);
    }

    void UpdateCombatFeedback()
    {
        if (m_HitMarkerText.gameObject.activeSelf)
        {
            float remaining = m_HitMarkerVisibleUntil - Time.time;
            if (remaining <= 0f)
            {
                m_HitMarkerText.gameObject.SetActive(false);
            }
            else
            {
                Color color = m_HitMarkerText.color;
                color.a = Mathf.Clamp01(remaining / m_HitMarkerDuration);
                m_HitMarkerText.color = color;
                m_HitMarkerText.rectTransform.localScale = Vector3.one *
                    Mathf.Lerp(0.92f, 1.16f, remaining / m_HitMarkerDuration);
            }
        }

        if (m_KillConfirmText.gameObject.activeSelf)
        {
            float remaining = m_KillConfirmVisibleUntil - Time.time;
            if (remaining <= 0f)
            {
                m_KillConfirmText.gameObject.SetActive(false);
            }
            else
            {
                Color color = m_KillConfirmText.color;
                color.a = Mathf.Clamp01(remaining / 0.35f);
                m_KillConfirmText.color = color;
            }
        }

        for (int i = m_KillFeedRows.Count - 1; i >= 0; i--)
        {
            GameObject row = m_KillFeedRows[i];
            if (row == null)
            {
                m_KillFeedRows.RemoveAt(i);
                continue;
            }

            KillFeedRow feedRow = row.GetComponent<KillFeedRow>();
            if (feedRow != null && feedRow.HasExpired)
            {
                m_KillFeedRows.RemoveAt(i);
                Destroy(row);
            }
        }
    }

    void HideMatchEnd()
    {
        m_MatchEndShown = false;
        m_MatchEndPanel.SetActive(false);
        m_ScoreboardPanel.SetActive(false);
        SetHelpOverlayVisible(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void UpdateHelpInput()
    {
        if (m_HelpPanel == null)
            return;

        if (m_MatchEndShown)
        {
            if (m_HelpPanel.activeSelf)
            {
                SetHelpOverlayVisible(false);
            }

            return;
        }

        if (Input.GetKeyDown(KeyCode.F1) || Input.GetKeyDown(KeyCode.H))
        {
            SetHelpOverlayVisible(!m_HelpPanel.activeSelf);
        }
        else if (Input.GetKeyDown(KeyCode.Escape) && m_HelpPanel.activeSelf)
        {
            SetHelpOverlayVisible(false);
        }
    }

    void SetHelpOverlayVisible(bool visible)
    {
        if (m_HelpPanel == null)
            return;

        m_HelpPanel.SetActive(visible);

        if (visible)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (!m_MatchEndShown && (m_ScoreboardPanel == null || !m_ScoreboardPanel.activeSelf))
        {
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

    void UpdateDeathOverlay()
    {
        if (!m_DeathShown)
        {
            if (m_HudBar != null && !m_MatchEndShown)
            {
                SetCombatHudVisible(true);
            }

            return;
        }

        if (m_MatchEndShown)
        {
            HideDeathOverlay();
            return;
        }

        float remaining = Mathf.Max(0f, m_RespawnCountdownEndTime - Time.time);
        m_DeathTimerText.text = remaining > 0.05f
            ? $"Respawning in {remaining:0.0}s"
            : "Respawning...";

        bool hasRespawned = m_LocalHealth != null && m_LocalHealth.CurrentHealth.Value > 0f && remaining <= 0.05f;
        if (hasRespawned)
        {
            HideDeathOverlay();
        }
    }

    void UpdateRespawnProtection()
    {
        if (m_ProtectionText == null)
            return;

        bool showProtection = !m_DeathShown && m_LocalHealth != null && m_LocalHealth.IsRespawnProtected;
        m_ProtectionText.gameObject.SetActive(showProtection);
        if (!showProtection)
            return;

        float remaining = m_LocalHealth.RespawnProtectionTimer.Value;
        m_ProtectionText.text = $"PROTECTED {remaining:0.0}s";
        Color color = AccentBlue;
        color.a = Mathf.Lerp(0.55f, 1f, Mathf.PingPong(Time.time * 3f, 1f));
        m_ProtectionText.color = color;
    }

    void UpdatePickupPrompt()
    {
        if (m_PickupPromptText == null)
            return;

        if (m_DeathShown || m_MatchEndShown || m_LocalPlayer == null)
        {
            m_PickupPromptText.gameObject.SetActive(false);
            return;
        }

        Pickup pickup = Pickup.FindClosestAvailable(m_LocalPlayer.transform.position, 2.4f);
        if (pickup == null)
        {
            m_PickupPromptText.gameObject.SetActive(false);
            return;
        }

        m_PickupPromptText.text = GetPickupPromptText(pickup);
        m_PickupPromptText.gameObject.SetActive(true);
    }

    void UpdatePickupFeedback()
    {
        if (m_PickupFeedbackText == null || !m_PickupFeedbackText.gameObject.activeSelf)
            return;

        float remaining = m_PickupFeedbackVisibleUntil - Time.time;
        if (remaining <= 0f)
        {
            m_PickupFeedbackText.gameObject.SetActive(false);
            return;
        }

        Color color = m_PickupFeedbackText.color;
        color.a = Mathf.Clamp01(remaining / 0.35f);
        m_PickupFeedbackText.color = color;
        m_PickupFeedbackText.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.96f, 1.05f, remaining / 1.15f);
    }

    void UpdateCountdown()
    {
        if (m_CountdownPanel == null || m_GameFlowManager == null)
            return;

        bool showCountdown = m_GameFlowManager.IsCountdownActive.Value && !m_GameFlowManager.IsMatchOver.Value;
        m_CountdownPanel.SetActive(showCountdown);
        if (!showCountdown)
            return;

        float remaining = m_GameFlowManager.CountdownTimer.Value;
        int displayValue = Mathf.CeilToInt(Mathf.Max(remaining, 0.01f));
        m_CountdownText.text = displayValue.ToString();
        m_CountdownLabelText.text = "GET READY";

        float pulse = 1f + Mathf.PingPong(Time.time * 0.8f, 0.08f);
        m_CountdownText.rectTransform.localScale = Vector3.one * pulse;
    }

    void RefreshScoreboard(GameObject container, List<GameObject> rows)
    {
        foreach (var row in rows) Destroy(row);
        rows.Clear();

        if (m_GameFlowManager == null) return;

        List<PlayerScoreData> players = GetSortedPlayers();
        ulong localId = NetworkManager.Singleton.LocalClientId;
        float yPos = -35;

        for (int i = 0; i < players.Count; i++)
        {
            bool isLocal = players[i].ClientId == localId;
            CreateScoreRow(container, rows, players[i], i + 1, isLocal, yPos);
            yPos -= 42;
        }
    }

    List<PlayerScoreData> GetSortedPlayers()
    {
        List<PlayerScoreData> players = new List<PlayerScoreData>();
        if (m_GameFlowManager == null)
            return players;

        for (int i = 0; i < m_GameFlowManager.PlayerIds.Count; i++)
        {
            players.Add(new PlayerScoreData
            {
                ClientId = m_GameFlowManager.PlayerIds[i],
                Kills = m_GameFlowManager.PlayerKills[i],
                Deaths = m_GameFlowManager.PlayerDeaths[i]
            });
        }

        players.Sort((a, b) =>
        {
            int killCompare = b.Kills.CompareTo(a.Kills);
            if (killCompare != 0)
                return killCompare;

            int deathCompare = a.Deaths.CompareTo(b.Deaths);
            if (deathCompare != 0)
                return deathCompare;

            return a.ClientId.CompareTo(b.ClientId);
        });

        return players;
    }

    int GetLocalPlacement(ulong localId)
    {
        List<PlayerScoreData> players = GetSortedPlayers();
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].ClientId == localId)
                return i + 1;
        }

        return 1;
    }

    string GetMatchEndReason(ulong winnerId)
    {
        if (m_GameFlowManager == null)
            return "Final standings";

        int winnerKills = m_GameFlowManager.GetKills(winnerId);
        if (m_GameFlowManager.ScoreLimit > 0 && winnerKills >= m_GameFlowManager.ScoreLimit)
        {
            return $"{winnerKills}/{m_GameFlowManager.ScoreLimit} score limit";
        }

        if (m_GameFlowManager.CurrentMatchEndReason == MatchEndReason.TimeExpired)
        {
            int tiedKillLeaders = CountPlayersWithKills(winnerKills);
            return tiedKillLeaders > 1
                ? "Time expired: kills tied, fewer deaths wins"
                : $"Time expired: most kills ({winnerKills})";
        }

        return "Most kills wins";
    }

    int CountPlayersWithKills(int killCount)
    {
        int count = 0;

        if (m_GameFlowManager == null)
            return count;

        for (int i = 0; i < m_GameFlowManager.PlayerIds.Count; i++)
        {
            if (m_GameFlowManager.PlayerKills[i] == killCount)
            {
                count++;
            }
        }

        return count;
    }

    static string FormatOrdinal(int value)
    {
        int tens = value % 100;
        if (tens >= 11 && tens <= 13)
            return $"{value}th";

        int ones = value % 10;
        return ones switch
        {
            1 => $"{value}st",
            2 => $"{value}nd",
            3 => $"{value}rd",
            _ => $"{value}th"
        };
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
        BuildCombatFeedback(canvasObj);
        BuildDeathOverlay(canvasObj);
        BuildCountdownOverlay(canvasObj);
        BuildScoreboardOverlay(canvasObj);
        BuildMatchEndOverlay(canvasObj);
        BuildHelpOverlay(canvasObj);
    }

    void BuildHUD(GameObject canvas)
    {
        // HUD bar
        m_HudBar = CreatePanel(canvas, "HUDBar", BgHUD,
            new Vector2(0, -30), new Vector2(260, 44),
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));

        // Kills section (left)
        GameObject killBadge = CreatePanel(m_HudBar, "KBadge",
            new Color(AccentBlue.r, AccentBlue.g, AccentBlue.b, 0.15f),
            new Vector2(-95, 0), new Vector2(24, 18));
        CreateText(killBadge, "KLabel", "K", 10,
            AccentBlue, FontStyles.Bold, Vector2.zero, new Vector2(24, 18));

        m_KillsText = CreateText(m_HudBar, "Kills", "0", 22,
            TextPrimary, FontStyles.Bold, new Vector2(-62, 0), new Vector2(40, 40));

        // Timer (center) with borders
        CreateImage(m_HudBar, "DivL", BorderSubtle, new Vector2(-35, 0), new Vector2(1, 30));
        m_TimerText = CreateText(m_HudBar, "Timer", "03:00", 22,
            TextPrimary, FontStyles.Bold, new Vector2(0, 0), new Vector2(80, 40));
        CreateImage(m_HudBar, "DivR", BorderSubtle, new Vector2(35, 0), new Vector2(1, 30));

        // Deaths section (right)
        m_DeathsText = CreateText(m_HudBar, "Deaths", "0", 22,
            TextPrimary, FontStyles.Bold, new Vector2(62, 0), new Vector2(40, 40));

        GameObject deathBadge = CreatePanel(m_HudBar, "DBadge",
            new Color(AccentRed.r, AccentRed.g, AccentRed.b, 0.15f),
            new Vector2(95, 0), new Vector2(24, 18));
        CreateText(deathBadge, "DLabel", "D", 10,
            AccentRed, FontStyles.Bold, Vector2.zero, new Vector2(24, 18));

        // Tab hint
        CreateText(canvas, "TabHint", "Tab  Scoreboard", 10,
            TextDim, FontStyles.Normal, new Vector2(0, -60),
            new Vector2(160, 16), TextAlignmentOptions.Center,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));

        CreateText(canvas, "HelpHint", "F1  Rules", 10,
            TextDim, FontStyles.Normal, new Vector2(0, -78),
            new Vector2(160, 16), TextAlignmentOptions.Center,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
    }

    void BuildCombatFeedback(GameObject canvas)
    {
        m_HitMarkerText = CreateText(canvas, "HitMarker", "X", 28,
            new Color(1f, 1f, 1f, 0f), FontStyles.Bold, Vector2.zero, new Vector2(60, 60));
        m_HitMarkerText.raycastTarget = false;

        m_KillConfirmText = CreateText(canvas, "KillConfirm", "ELIMINATION", 18,
            AccentAmber, FontStyles.Bold, new Vector2(0, -96), new Vector2(260, 32));
        m_KillConfirmText.raycastTarget = false;

        m_ProtectionText = CreateText(canvas, "RespawnProtection", "PROTECTED", 14,
            AccentBlue, FontStyles.Bold, new Vector2(0, -132), new Vector2(260, 28));
        m_ProtectionText.raycastTarget = false;

        m_PickupPromptText = CreateText(canvas, "PickupPrompt", "PICKUP", 13,
            TextSecondary, FontStyles.Bold, new Vector2(0, -176), new Vector2(300, 26));
        m_PickupPromptText.raycastTarget = false;

        m_PickupFeedbackText = CreateText(canvas, "PickupFeedback", "+ PICKUP", 16,
            AccentGreenText, FontStyles.Bold, new Vector2(0, -206), new Vector2(320, 30));
        m_PickupFeedbackText.raycastTarget = false;

        m_KillFeedContainer = new GameObject("KillFeed");
        m_KillFeedContainer.transform.SetParent(canvas.transform, false);
        RectTransform feedRect = m_KillFeedContainer.AddComponent<RectTransform>();
        feedRect.anchorMin = new Vector2(1f, 1f);
        feedRect.anchorMax = new Vector2(1f, 1f);
        feedRect.pivot = new Vector2(1f, 1f);
        feedRect.anchoredPosition = new Vector2(-24, -92);
        feedRect.sizeDelta = new Vector2(360, 220);
    }

    void BuildDeathOverlay(GameObject canvas)
    {
        m_DeathPanel = CreatePanel(canvas, "DeathOverlay",
            new Color(0f, 0f, 0f, 0.34f), Vector2.zero, new Vector2(1920, 1080));

        GameObject notice = CreatePanel(m_DeathPanel, "DeathNotice",
            new Color(0.05f, 0.055f, 0.07f, 0.86f), new Vector2(0, -20), new Vector2(360, 150));

        m_DeathTitleText = CreateText(notice, "DeathTitle", "You were eliminated", 22,
            TextPrimary, FontStyles.Bold, new Vector2(0, 40), new Vector2(320, 34));

        m_DeathSubtitleText = CreateText(notice, "DeathSubtitle", "By Player", 14,
            TextSecondary, FontStyles.Normal, new Vector2(0, 8), new Vector2(320, 24));

        m_DeathTimerText = CreateText(notice, "DeathTimer", "Respawning in 3.0s", 16,
            AccentAmber, FontStyles.Bold, new Vector2(0, -36), new Vector2(320, 28));

        m_DeathTitleText.raycastTarget = false;
        m_DeathSubtitleText.raycastTarget = false;
        m_DeathTimerText.raycastTarget = false;
    }

    void BuildCountdownOverlay(GameObject canvas)
    {
        m_CountdownPanel = CreatePanel(canvas, "CountdownOverlay",
            new Color(0f, 0f, 0f, 0.18f), Vector2.zero, new Vector2(1920, 1080));
        m_CountdownPanel.SetActive(false);

        m_CountdownLabelText = CreateText(m_CountdownPanel, "CountdownLabel", "GET READY", 18,
            TextSecondary, FontStyles.Bold, new Vector2(0, 92), new Vector2(260, 32));

        m_CountdownText = CreateText(m_CountdownPanel, "CountdownValue", "3", 86,
            TextPrimary, FontStyles.Bold, new Vector2(0, 20), new Vector2(220, 120));
        m_CountdownText.raycastTarget = false;
        m_CountdownLabelText.raycastTarget = false;
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
        CreateText(board, "Footer", "Deathmatch \u2014 Most kills when time expires wins", 10,
            TextDim, FontStyles.Normal, new Vector2(0, -160), new Vector2(480, 16));
    }

    void BuildMatchEndOverlay(GameObject canvas)
    {
        m_MatchEndPanel = CreatePanel(canvas, "MatchEndOverlay", BgOverlay,
            Vector2.zero, new Vector2(1920, 1080));

        GameObject board = CreatePanel(m_MatchEndPanel, "EndBoard", BgCard,
            new Vector2(0, 60), new Vector2(560, 500));

        // Match complete label
        CreateText(board, "EndLabel", "Match complete", 11,
            TextDim, FontStyles.Normal, new Vector2(0, 218), new Vector2(500, 16));

        // Winner title
        m_EndTitle = CreateText(board, "EndTitle", "You won!", 26,
            AccentGreenText, FontStyles.Bold, new Vector2(0, 190), new Vector2(500, 35));

        // Stats subtitle
        m_EndSubtitle = CreateText(board, "EndStats", "0 kills, 0 deaths", 14,
            TextSecondary, FontStyles.Normal, new Vector2(0, 162), new Vector2(500, 20));

        m_EndWinnerText = CreateText(board, "EndWinner", "Winner: Player",
            13, AccentAmber, FontStyles.Bold, new Vector2(0, 136), new Vector2(500, 20));

        m_EndPlacementText = CreateText(board, "EndPlacement", "Your placement: 1st of 1",
            13, TextSecondary, FontStyles.Normal, new Vector2(0, 113), new Vector2(500, 20));

        // Divider
        CreateImage(board, "EndDiv", BorderSubtle, new Vector2(0, 95), new Vector2(500, 1));

        // Column headers
        CreateScoreHeader(board, 78);

        // Rows container
        m_EndRowsContainer = new GameObject("EndRows");
        m_EndRowsContainer.transform.SetParent(board.transform, false);
        RectTransform rowsRect = m_EndRowsContainer.AddComponent<RectTransform>();
        rowsRect.anchoredPosition = new Vector2(0, 8);
        rowsRect.sizeDelta = new Vector2(500, 220);

        // Buttons
        float btnY = -190;

        Button playAgain = CreateButton(board, "PlayAgain", "Start next round",
            TextPrimary, new Color(0.05f, 0.27f, 0.49f),
            new Vector2(0, btnY), new Vector2(500, 44));
        m_PlayAgainButton = playAgain;
        m_PlayAgainButtonLabel = playAgain.GetComponentInChildren<TextMeshProUGUI>();
        m_PlayAgainButton.onClick.AddListener(OnPlayAgain);

        Button backToMenu = CreateButton(board, "BackMenu", "Back to menu",
            TextSecondary, new Color(0.09f, 0.1f, 0.13f),
            new Vector2(0, btnY - 50), new Vector2(500, 40));
        backToMenu.onClick.AddListener(OnBackToMenu);
    }

    void BuildHelpOverlay(GameObject canvas)
    {
        m_HelpPanel = CreatePanel(canvas, "HelpOverlay",
            new Color(0f, 0f, 0f, 0.58f), Vector2.zero, new Vector2(1920, 1080));

        GameObject board = CreatePanel(m_HelpPanel, "HelpBoard", BgCard,
            new Vector2(0, 35), new Vector2(600, 390));

        CreateText(board, "HelpLabel", "Quick reference", 11,
            TextDim, FontStyles.Normal, new Vector2(0, 164), new Vector2(520, 18));

        CreateText(board, "HelpTitle", "Deathmatch Rules", 24,
            TextPrimary, FontStyles.Bold, new Vector2(0, 132), new Vector2(520, 34));

        TextMeshProUGUI objective = CreateText(board, "Objective",
            "Most kills when the timer ends wins. If kills are tied, fewer deaths decides the winner. Score limit ends the round early.",
            13, TextSecondary, FontStyles.Normal, new Vector2(0, 88), new Vector2(520, 52));
        objective.textWrappingMode = TextWrappingModes.Normal;

        CreateImage(board, "HelpDiv", BorderSubtle, new Vector2(0, 52), new Vector2(520, 1));

        TextMeshProUGUI controls = CreateText(board, "Controls",
            "WASD        Move\nMouse       Aim\nLeft Click  Fire\nR           Reload\n1-3/Scroll  Switch weapon",
            13, TextPrimary, FontStyles.Normal, new Vector2(-145, -25), new Vector2(230, 135),
            TextAlignmentOptions.Left);
        controls.textWrappingMode = TextWrappingModes.NoWrap;

        TextMeshProUGUI gameplay = CreateText(board, "Gameplay",
            "E           Pick up\nTab         Scoreboard\nF1 or H     Rules\nEsc         Close this panel\nRespawn     3 seconds + protection",
            13, TextPrimary, FontStyles.Normal, new Vector2(155, -25), new Vector2(260, 135),
            TextAlignmentOptions.Left);
        gameplay.textWrappingMode = TextWrappingModes.NoWrap;

        CreateText(board, "HelpFooter", "Pickups reset every round. Host controls the next round.",
            11, TextDim, FontStyles.Normal, new Vector2(0, -168), new Vector2(520, 18));
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
        if (!CanLocalPlayerControlRound())
        {
            m_EndSubtitle.text = "Waiting for host to start the next round...";
            return;
        }

        if (m_GameFlowManager == null)
        {
            m_GameFlowManager = FindFirstObjectByType<GameFlowManager>();
        }

        if (m_GameFlowManager != null)
        {
            m_EndSubtitle.text = "Starting next round...";
            m_GameFlowManager.RestartMatchServerRpc();
        }
    }

    void UpdateRoundControlButton()
    {
        if (m_PlayAgainButton == null)
            return;

        bool canControlRound = CanLocalPlayerControlRound();
        m_PlayAgainButton.interactable = canControlRound;

        Image buttonImage = m_PlayAgainButton.GetComponent<Image>();
        if (buttonImage != null)
        {
            buttonImage.color = canControlRound
                ? new Color(0.05f, 0.27f, 0.49f)
                : new Color(0.09f, 0.1f, 0.13f);
        }

        if (m_PlayAgainButtonLabel != null)
        {
            m_PlayAgainButtonLabel.text = canControlRound ? "Start next round" : "Waiting for host";
            m_PlayAgainButtonLabel.color = canControlRound ? TextPrimary : TextSecondary;
        }
    }

    bool CanLocalPlayerControlRound()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
    }

    void AddKillFeedRow(ulong victimId, ulong killerId)
    {
        if (m_KillFeedContainer == null)
        {
            return;
        }

        while (m_KillFeedRows.Count >= 5)
        {
            GameObject oldest = m_KillFeedRows[0];
            m_KillFeedRows.RemoveAt(0);
            if (oldest != null)
            {
                Destroy(oldest);
            }
        }

        int rank = m_KillFeedRows.Count;
        GameObject row = CreatePanel(m_KillFeedContainer, $"KillFeed_{Time.frameCount}", new Color(0.04f, 0.05f, 0.07f, 0.78f),
            new Vector2(0, -rank * 34), new Vector2(340, 28),
            new Vector2(1f, 1f), new Vector2(1f, 1f));

        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.pivot = new Vector2(1f, 1f);

        bool localWasKiller = NetworkManager.Singleton != null && killerId == NetworkManager.Singleton.LocalClientId;
        bool localWasVictim = NetworkManager.Singleton != null && victimId == NetworkManager.Singleton.LocalClientId;
        Color textColor = localWasKiller ? AccentGreenText : localWasVictim ? AccentRed : TextPrimary;

        string killerName = GetDisplayName(killerId);
        string victimName = GetDisplayName(victimId);
        string message = victimId == killerId
            ? $"{victimName} eliminated themselves"
            : $"{killerName} eliminated {victimName}";

        TextMeshProUGUI label = CreateText(row, "Label", message, 12,
            textColor, FontStyles.Bold, Vector2.zero, new Vector2(316, 24), TextAlignmentOptions.Right);
        label.raycastTarget = false;

        KillFeedRow feedRow = row.AddComponent<KillFeedRow>();
        feedRow.Initialize(Time.time, 4.5f, label);

        m_KillFeedRows.Add(row);
    }

    string GetDisplayName(ulong clientId)
    {
        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
        {
            return "You";
        }

        return $"Player {clientId}";
    }

    string GetPickupPromptText(Pickup pickup)
    {
        return $"PICK UP {GetPickupName(pickup)}";
    }

    string GetPickupFeedbackText(Pickup pickup)
    {
        if (pickup is HealthPickup)
            return "HEALTH RESTORED";

        if (pickup is AmmoPickup)
            return "+ AMMO";

        if (pickup is WeaponPickup)
            return $"+ {GetPickupName(pickup)}";

        if (pickup is JetpackPickup)
            return "JETPACK UNLOCKED";

        return "+ PICKUP";
    }

    string GetPickupName(Pickup pickup)
    {
        if (pickup is HealthPickup)
            return "HEALTH";

        if (pickup is AmmoPickup ammoPickup)
        {
            string weaponName = ammoPickup.Weapon != null ? ammoPickup.Weapon.WeaponName : "AMMO";
            return $"{weaponName} AMMO";
        }

        if (pickup is WeaponPickup weaponPickup)
        {
            return weaponPickup.WeaponPrefab != null && !string.IsNullOrWhiteSpace(weaponPickup.WeaponPrefab.WeaponName)
                ? weaponPickup.WeaponPrefab.WeaponName.ToUpperInvariant()
                : "WEAPON";
        }

        if (pickup is JetpackPickup)
            return "JETPACK";

        return "PICKUP";
    }

    void SetCombatHudVisible(bool visible)
    {
        if (m_HudBar != null)
        {
            m_HudBar.SetActive(visible);
        }
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
        tmp.textWrappingMode = TextWrappingModes.NoWrap;

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

        PlayerCharacterController.OnLocalPlayerSpawned -= OnLocalPlayerSpawned;
        PlayerCharacterController.OnLocalShotConfirmed -= OnLocalShotConfirmed;
        PlayerCharacterController.OnLocalShotBlocked -= OnLocalShotBlocked;
        Pickup.OnLocalPickupConfirmed -= OnLocalPickupConfirmed;
        GameFlowManager.OnPlayerKilled -= OnPlayerKilled;
    }

    class KillFeedRow : MonoBehaviour
    {
        TextMeshProUGUI m_Label;
        float m_InitTime;
        float m_VisibleDuration;

        public bool HasExpired => Time.time >= m_InitTime + m_VisibleDuration;

        public void Initialize(float initTime, float visibleDuration, TextMeshProUGUI label)
        {
            m_InitTime = initTime;
            m_VisibleDuration = visibleDuration;
            m_Label = label;
        }

        void Update()
        {
            if (m_Label == null)
            {
                return;
            }

            float remaining = (m_InitTime + m_VisibleDuration) - Time.time;
            Color color = m_Label.color;
            color.a = Mathf.Clamp01(remaining / 0.6f);
            m_Label.color = color;
        }
    }
}
