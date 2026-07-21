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
    static readonly Color BgOverlay = new Color32(7, 10, 13, 238);
    static readonly Color BgCard = VanguardUITheme.BaseTransparent;
    static readonly Color BgRow = VanguardUITheme.PanelSoft;
    static readonly Color BgRowLocal = VanguardUITheme.AmberSoft;
    static readonly Color BgHUD = VanguardUITheme.BaseTransparent;
    static readonly Color AccentBlue = VanguardUITheme.Amber;
    static readonly Color AccentGreen = VanguardUITheme.GreenSoft;
    static readonly Color AccentGreenText = VanguardUITheme.Green;
    static readonly Color AccentRed = VanguardUITheme.Red;
    static readonly Color AccentAmber = VanguardUITheme.Amber;
    static readonly Color TextPrimary = VanguardUITheme.Ink;
    static readonly Color TextSecondary = VanguardUITheme.InkDim;
    static readonly Color TextDim = VanguardUITheme.InkFaint;
    static readonly Color BorderSubtle = VanguardUITheme.Border;

    Canvas m_Canvas;
    VanguardMatchToolkit m_Toolkit;
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
    TextMeshProUGUI m_HealthText;
    TextMeshProUGUI m_AmmoText;
    TextMeshProUGUI m_WeaponText;
    Image m_HealthFill;
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
    TextMeshProUGUI m_CrosshairText;
    TextMeshProUGUI m_KillConfirmText;
    TextMeshProUGUI m_ProtectionText;
    TextMeshProUGUI m_PickupPromptText;
    TextMeshProUGUI m_PickupFeedbackText;
    TextMeshProUGUI m_CountdownText;
    TextMeshProUGUI m_CountdownLabelText;
    TextMeshProUGUI m_LowHealthText;
    TextMeshProUGUI m_SensitivityValueText;
    TextMeshProUGUI m_MasterVolumeValueText;
    GameObject m_CountdownPanel;
    GameObject m_PausePanel;
    GameObject m_KillFeedContainer;
    CanvasGroup m_DamageVignetteGroup;
    GameFlowManager m_GameFlowManager;
    PlayerCharacterController m_LocalPlayer;
    Health m_LocalHealth;
    PlayerWeaponsManager m_LocalWeapons;

    List<GameObject> m_ScoreRows = new List<GameObject>();
    List<GameObject> m_EndScoreRows = new List<GameObject>();
    List<GameObject> m_KillFeedRows = new List<GameObject>();

    bool m_MatchEndShown = false;
    bool m_DeathShown = false;
    bool m_IsReturningToMenu;
    float m_RespawnCountdownEndTime = Mathf.NegativeInfinity;
    float m_HitMarkerVisibleUntil = Mathf.NegativeInfinity;
    float m_HitMarkerDuration = 0.16f;
    float m_KillConfirmVisibleUntil = Mathf.NegativeInfinity;
    float m_PickupFeedbackVisibleUntil = Mathf.NegativeInfinity;
    float m_DamageFlashVisibleUntil = Mathf.NegativeInfinity;
    float m_LastHealthValue = float.NaN;
    float m_NextHudRefreshTime;
    int m_LastCountdownDisplayValue = -1;
    int m_LastToolkitKillFeedCount = -1;
    readonly List<string> m_ToolkitKillFeed = new List<string>();

    const string k_MasterVolumePrefsKey = "NetcodeFPS.MasterVolume";
    const float k_HudRefreshInterval = 0.05f;

    [Header("Optional HUD Audio")]
    public AudioClip CountdownTickSfx;
    public AudioClip MatchEndSfx;
    public AudioClip KillConfirmSfx;
    public AudioClip PickupConfirmSfx;
    public AudioClip PauseOpenSfx;

    void Start()
    {
        DisableLegacyHud();
        BuildUI();
        m_ScoreboardPanel.SetActive(false);
        m_MatchEndPanel.SetActive(false);
        m_DeathPanel.SetActive(false);
        m_HelpPanel.SetActive(false);
        m_PausePanel.SetActive(false);
        m_HitMarkerText.gameObject.SetActive(false);
        m_KillConfirmText.gameObject.SetActive(false);
        m_ProtectionText.gameObject.SetActive(false);
        m_PickupPromptText.gameObject.SetActive(false);
        m_PickupFeedbackText.gameObject.SetActive(false);
        m_LowHealthText.gameObject.SetActive(false);
        RefreshSettingsValues();

        PlayerCharacterController.OnLocalPlayerSpawned += OnLocalPlayerSpawned;
        PlayerCharacterController.OnLocalShotConfirmed += OnLocalShotConfirmed;
        PlayerCharacterController.OnLocalShotBlocked += OnLocalShotBlocked;
        Pickup.OnLocalPickupConfirmed += OnLocalPickupConfirmed;
        GameFlowManager.OnPlayerKilled += OnPlayerKilled;
    }

    void DisableLegacyHud()
    {
        GameObject legacyHud = GameObject.Find("HUD");
        if (legacyHud != null)
        {
            legacyHud.SetActive(false);
        }
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

        float unscaledTime = Time.unscaledTime;
        if (unscaledTime >= m_NextHudRefreshTime)
        {
            m_NextHudRefreshTime = unscaledTime + k_HudRefreshInterval;
            UpdateTimer();
            UpdateMiniHUD();
            UpdateDeathOverlay();
            UpdateRespawnProtection();
            UpdatePickupPrompt();
            UpdatePickupFeedback();
            UpdateCountdown();
            UpdateLowHealthWarning();
            SyncToolkitUI();
        }

        UpdateCombatFeedback();
        UpdateHelpInput();
        UpdatePauseInput();

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
                    RestoreGameplayCursorIfNeeded();
                }
            }
        }

        m_Toolkit?.Tick(unscaledTime);
        if (m_Toolkit != null && m_Toolkit.IsRenderable && m_Canvas != null && m_Canvas.enabled)
        {
            m_Canvas.enabled = false;
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
        SetPauseOverlayVisible(false);
        m_MatchEndPanel.SetActive(true);
        PlayHudSfx(MatchEndSfx, AudioUtility.AudioGroups.HUDVictory);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        ulong localId = NetworkManager.Singleton.LocalClientId;
        ulong winnerId = m_GameFlowManager.GetWinnerId();
        int kills = m_GameFlowManager.GetKills(localId);
        int deaths = m_GameFlowManager.GetDeaths(localId);
        int placement = GetLocalPlacement(localId);
        int playerCount = Mathf.Max(1, m_GameFlowManager.PlayerIds.Count);

        bool isWinner = localId == winnerId;
        m_EndTitle.text = $"{GetDisplayName(winnerId).ToUpperInvariant()} WINS";
        m_EndTitle.color = isWinner ? AccentGreenText : TextPrimary;
        m_EndSubtitle.text = GetMatchEndReason(winnerId).ToUpperInvariant();
        m_EndWinnerText.text = $"YOUR RECORD · {kills} KILLS · {deaths} DEATHS";
        m_EndPlacementText.text = $"PLACEMENT · {FormatOrdinal(placement).ToUpperInvariant()} OF {playerCount}";
        UpdateRoundControlButton();

        RefreshScoreboard(m_EndRowsContainer, m_EndScoreRows);
    }

    void OnLocalShotConfirmed()
    {
        ShowHitMarker("×", TextPrimary, 0.16f);
    }

    void OnLocalShotBlocked()
    {
        ShowHitMarker("BLOCKED", AccentBlue, 0.45f);
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
        m_LocalWeapons = player != null ? player.GetComponent<PlayerWeaponsManager>() : null;
        m_LastHealthValue = m_LocalHealth != null ? m_LocalHealth.CurrentHealth.Value : float.NaN;
    }

    void OnLocalPickupConfirmed(Pickup pickup, PlayerCharacterController player)
    {
        if (player != m_LocalPlayer || pickup == null || m_DeathShown)
            return;

        m_PickupFeedbackText.text = GetPickupFeedbackText(pickup);
        m_PickupFeedbackVisibleUntil = Time.time + 1.15f;
        m_PickupFeedbackText.gameObject.SetActive(true);
        m_PickupPromptText.gameObject.SetActive(false);
        PlayHudSfx(PickupConfirmSfx, AudioUtility.AudioGroups.Pickup);
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
            m_KillConfirmText.text = $"ELIMINATED · {GetDisplayName(victimId).ToUpperInvariant()}";
            m_KillConfirmVisibleUntil = Time.time + 1.25f;
            m_KillConfirmText.gameObject.SetActive(true);
            PlayHudSfx(KillConfirmSfx, AudioUtility.AudioGroups.HUDObjective);
        }
    }

    void ShowDeathOverlay(ulong victimId, ulong killerId)
    {
        if (m_MatchEndShown)
            return;

        m_DeathShown = true;
        m_RespawnCountdownEndTime = Time.time + GameFlowManager.RespawnDelay;

        string killerName = GetDisplayName(killerId);
        m_DeathTitleText.text = victimId == killerId ? "SELF ELIMINATION" : $"ELIMINATED BY {killerName.ToUpperInvariant()}";
        m_DeathSubtitleText.text = victimId == killerId ? "Watch your footing" : "Re-entering the combat zone";

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
        SetPauseOverlayVisible(false);

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

    void UpdatePauseInput()
    {
        if (m_PausePanel == null || m_MatchEndShown)
            return;

        if (m_HelpPanel != null && m_HelpPanel.activeSelf)
            return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SetPauseOverlayVisible(!m_PausePanel.activeSelf);
        }
    }

    void SetHelpOverlayVisible(bool visible)
    {
        if (m_HelpPanel == null)
            return;

        m_HelpPanel.SetActive(visible);
        PlayerInputHandler.SetMenuInputBlocked(visible || (m_PausePanel != null && m_PausePanel.activeSelf));

        if (visible)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (!m_MatchEndShown && (m_ScoreboardPanel == null || !m_ScoreboardPanel.activeSelf))
        {
            RestoreGameplayCursorIfNeeded();
        }
    }

    void SetPauseOverlayVisible(bool visible)
    {
        if (m_PausePanel == null)
            return;

        m_PausePanel.SetActive(visible);
        PlayerInputHandler.SetMenuInputBlocked(visible || (m_HelpPanel != null && m_HelpPanel.activeSelf));

        if (visible)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            RefreshSettingsValues();
            PlayHudSfx(PauseOpenSfx, AudioUtility.AudioGroups.HUDObjective);
        }
        else
        {
            RestoreGameplayCursorIfNeeded();
        }
    }

    void RestoreGameplayCursorIfNeeded()
    {
        bool anyOverlayOpen =
            m_MatchEndShown ||
            (m_PausePanel != null && m_PausePanel.activeSelf) ||
            (m_HelpPanel != null && m_HelpPanel.activeSelf) ||
            (m_ScoreboardPanel != null && m_ScoreboardPanel.activeSelf);

        if (anyOverlayOpen)
            return;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
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

        if (m_LocalHealth != null)
        {
            float ratio = Mathf.Clamp01(m_LocalHealth.GetRatio());
            m_HealthText.text = Mathf.CeilToInt(m_LocalHealth.CurrentHealth.Value).ToString();
            m_HealthText.color = ratio <= 0.3f ? AccentRed : TextPrimary;
            m_HealthFill.color = ratio <= 0.3f ? AccentRed : TextPrimary;
            RectTransform fillRect = m_HealthFill.rectTransform;
            fillRect.localScale = new Vector3(Mathf.Max(0.001f, ratio), 1f, 1f);
        }

        WeaponController activeWeapon = m_LocalWeapons != null ? m_LocalWeapons.GetActiveWeapon() : null;
        if (activeWeapon != null)
        {
            m_WeaponText.text = activeWeapon.WeaponName.ToUpperInvariant();
            m_AmmoText.text = $"{activeWeapon.GetCurrentAmmo()}<size=60%> / {activeWeapon.MaxAmmo}</size>";
        }
        else
        {
            m_WeaponText.text = "UNARMED";
            m_AmmoText.text = "0";
        }
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

        bool hasRespawned = m_LocalHealth != null && m_LocalHealth.CurrentHealth.Value > 0f;
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

        if (m_DeathShown || m_MatchEndShown || m_LocalPlayer == null ||
            m_GameFlowManager == null || !m_GameFlowManager.IsGameplayActive)
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
        {
            m_LastCountdownDisplayValue = -1;
            return;
        }

        float remaining = m_GameFlowManager.CountdownTimer.Value;
        int displayValue = Mathf.CeilToInt(Mathf.Max(remaining, 0.01f));
        m_CountdownText.text = displayValue.ToString();
        m_CountdownLabelText.text = "GET READY";

        if (displayValue != m_LastCountdownDisplayValue)
        {
            m_LastCountdownDisplayValue = displayValue;
            PlayHudSfx(CountdownTickSfx, AudioUtility.AudioGroups.HUDObjective);
        }

        float pulse = 1f + Mathf.PingPong(Time.time * 0.8f, 0.08f);
        m_CountdownText.rectTransform.localScale = Vector3.one * pulse;
    }

    void UpdateLowHealthWarning()
    {
        if (m_LowHealthText == null)
            return;

        bool show = !m_DeathShown && !m_MatchEndShown && m_LocalHealth != null &&
            m_LocalHealth.CurrentHealth.Value > 0f && m_LocalHealth.IsCritical();

        if (m_LocalHealth != null)
        {
            float current = m_LocalHealth.CurrentHealth.Value;
            if (!float.IsNaN(m_LastHealthValue) && current < m_LastHealthValue)
            {
                m_DamageFlashVisibleUntil = Time.time + 0.2f;
            }

            m_LastHealthValue = current;
        }

        m_LowHealthText.gameObject.SetActive(show);
        float alpha = show
            ? Mathf.Lerp(0.42f, 1f, Mathf.PingPong(Time.time * 3.5f, 1f))
            : 0f;
        Color color = AccentRed;
        color.a = alpha;
        m_LowHealthText.color = color;

        if (m_DamageVignetteGroup != null)
        {
            float flash = Time.time < m_DamageFlashVisibleUntil ? 1f : 0f;
            m_DamageVignetteGroup.alpha = Mathf.Max(show ? alpha * 0.8f : 0f, flash);
        }
    }

    void RefreshScoreboard(GameObject container, List<GameObject> rows)
    {
        foreach (var row in rows) Destroy(row);
        rows.Clear();

        if (m_GameFlowManager == null) return;

        List<PlayerScoreData> players = GetSortedPlayers();
        ulong localId = NetworkManager.Singleton.LocalClientId;
        float yPos = 62f;

        for (int i = 0; i < players.Count; i++)
        {
            bool isLocal = players[i].ClientId == localId;
            CreateScoreRow(container, rows, players[i], i + 1, isLocal, yPos);
            yPos -= 68f;
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
        VanguardUITheme.ConfigureScaler(scaler);

        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject safeArea = VanguardUITheme.CreateSafeArea(canvasObj);
        BuildHUD(safeArea);
        BuildCombatFeedback(safeArea);
        BuildDeathOverlay(safeArea);
        BuildCountdownOverlay(safeArea);
        BuildScoreboardOverlay(safeArea);
        BuildMatchEndOverlay(safeArea);
        BuildHelpOverlay(safeArea);
        BuildPauseOverlay(safeArea);

        m_Toolkit = new VanguardMatchToolkit(this);
    }

    void BuildHUD(GameObject canvas)
    {
        m_HudBar = new GameObject("HUD");
        m_HudBar.transform.SetParent(canvas.transform, false);
        m_HudBar.AddComponent<RectTransform>();

        GameObject stats = CreatePanel(m_HudBar, "CombatStats", BgHUD,
            new Vector2(64f, -56f), new Vector2(208f, 82f),
            new Vector2(0f, 1f), new Vector2(0f, 1f));
        AddOutline(stats, BorderSubtle);
        CreateText(stats, "KLabel", "K", 12, TextSecondary, FontStyles.Bold,
            new Vector2(-70f, 0f), new Vector2(26f, 46f));
        m_KillsText = CreateText(stats, "Kills", "0", 27, TextPrimary, FontStyles.Bold,
            new Vector2(-42f, 0f), new Vector2(42f, 46f));
        CreateImage(stats, "Divider", BorderSubtle, Vector2.zero, new Vector2(2f, 48f));
        CreateText(stats, "DLabel", "D", 12, TextSecondary, FontStyles.Bold,
            new Vector2(42f, 0f), new Vector2(26f, 46f));
        m_DeathsText = CreateText(stats, "Deaths", "0", 27, TextPrimary, FontStyles.Bold,
            new Vector2(72f, 0f), new Vector2(42f, 46f));

        m_TimerText = CreateText(m_HudBar, "Timer", "03:00", 40,
            TextPrimary, FontStyles.Bold, new Vector2(0f, -48f), new Vector2(230f, 58f),
            TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        TextMeshProUGUI mode = CreateText(m_HudBar, "Mode", "DEATHMATCH · FIRST TO 30", 12,
            TextSecondary, FontStyles.Bold, new Vector2(0f, -90f), new Vector2(360f, 26f),
            TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        mode.characterSpacing = 3f;

        GameObject health = CreatePanel(m_HudBar, "Health", BgHUD,
            new Vector2(64f, 54f), new Vector2(424f, 94f),
            new Vector2(0f, 0f), new Vector2(0f, 0f));
        AddOutline(health, BorderSubtle);
        CreateText(health, "Heart", "♡", 34, TextPrimary, FontStyles.Normal,
            new Vector2(-158f, 0f), new Vector2(54f, 54f));
        GameObject healthTrack = CreatePanel(health, "Track", new Color32(243, 242, 242, 45),
            new Vector2(-18f, 0f), new Vector2(240f, 16f));
        GameObject healthFill = CreatePanel(healthTrack, "Fill", TextPrimary,
            new Vector2(-120f, 0f), new Vector2(240f, 16f));
        RectTransform healthFillRect = healthFill.GetComponent<RectTransform>();
        healthFillRect.anchorMin = new Vector2(0f, 0.5f);
        healthFillRect.anchorMax = new Vector2(0f, 0.5f);
        healthFillRect.pivot = new Vector2(0f, 0.5f);
        healthFillRect.anchoredPosition = new Vector2(-120f, 0f);
        m_HealthFill = healthFill.GetComponent<Image>();
        m_HealthText = CreateText(health, "Value", "100", 28, TextPrimary, FontStyles.Bold,
            new Vector2(158f, 0f), new Vector2(78f, 54f));

        GameObject ammo = CreatePanel(m_HudBar, "Ammo", BgHUD,
            new Vector2(-68f, 54f), new Vector2(270f, 116f),
            new Vector2(1f, 0f), new Vector2(1f, 0f));
        AddOutline(ammo, BorderSubtle);
        m_WeaponText = CreateText(ammo, "Weapon", "ASSAULT RIFLE", 11, TextSecondary, FontStyles.Bold,
            new Vector2(0f, 30f), new Vector2(230f, 24f), TextAlignmentOptions.Right);
        m_AmmoText = CreateText(ammo, "AmmoValue", "24 / 90", 30, TextPrimary, FontStyles.Bold,
            new Vector2(0f, -12f), new Vector2(230f, 50f), TextAlignmentOptions.Right);
    }

    void BuildCombatFeedback(GameObject canvas)
    {
        GameObject vignette = new GameObject("DamageVignette");
        vignette.transform.SetParent(canvas.transform, false);
        RectTransform vignetteRect = vignette.AddComponent<RectTransform>();
        vignetteRect.anchorMin = Vector2.zero;
        vignetteRect.anchorMax = Vector2.one;
        vignetteRect.offsetMin = Vector2.zero;
        vignetteRect.offsetMax = Vector2.zero;
        m_DamageVignetteGroup = vignette.AddComponent<CanvasGroup>();
        m_DamageVignetteGroup.alpha = 0f;
        CreatePanel(vignette, "Top", VanguardUITheme.RedSoft, new Vector2(0f, -70f), new Vector2(0f, 140f),
            new Vector2(0f, 1f), new Vector2(1f, 1f));
        CreatePanel(vignette, "Bottom", VanguardUITheme.RedSoft, new Vector2(0f, 70f), new Vector2(0f, 140f),
            new Vector2(0f, 0f), new Vector2(1f, 0f));
        CreatePanel(vignette, "Left", VanguardUITheme.RedSoft, new Vector2(70f, 0f), new Vector2(140f, 0f),
            new Vector2(0f, 0f), new Vector2(0f, 1f));
        CreatePanel(vignette, "Right", VanguardUITheme.RedSoft, new Vector2(-70f, 0f), new Vector2(140f, 0f),
            new Vector2(1f, 0f), new Vector2(1f, 1f));
        vignette.transform.SetAsFirstSibling();

        m_CrosshairText = CreateText(canvas, "Crosshair", "+", 24,
            TextPrimary, FontStyles.Normal, Vector2.zero, new Vector2(48, 48));
        m_CrosshairText.raycastTarget = false;

        m_HitMarkerText = CreateText(canvas, "HitMarker", "×", 30,
            new Color(1f, 1f, 1f, 0f), FontStyles.Bold, Vector2.zero, new Vector2(60, 60));
        m_HitMarkerText.raycastTarget = false;

        m_KillConfirmText = CreateText(canvas, "KillConfirm", "ELIMINATED", 15,
            AccentAmber, FontStyles.Bold, new Vector2(0, -74), new Vector2(300, 32));
        m_KillConfirmText.raycastTarget = false;

        m_ProtectionText = CreateText(canvas, "RespawnProtection", "PROTECTED", 14,
            AccentBlue, FontStyles.Bold, new Vector2(0, -112), new Vector2(260, 28));
        m_ProtectionText.raycastTarget = false;

        m_PickupPromptText = CreateText(canvas, "PickupPrompt", "E  PICK UP", 15,
            TextPrimary, FontStyles.Normal, new Vector2(0, -324), new Vector2(420, 58));
        AddOutline(m_PickupPromptText.gameObject, AccentAmber);
        m_PickupPromptText.raycastTarget = false;

        m_PickupFeedbackText = CreateText(canvas, "PickupFeedback", "+ PICKUP", 16,
            AccentGreenText, FontStyles.Bold, new Vector2(0, -128), new Vector2(420, 40),
            TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        m_PickupFeedbackText.raycastTarget = false;

        m_LowHealthText = CreateText(canvas, "LowHealth", "CRITICAL · FIND COVER", 14,
            AccentRed, FontStyles.Bold, new Vector2(0, 116), new Vector2(360, 34),
            TextAlignmentOptions.Center, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
        m_LowHealthText.raycastTarget = false;

        m_KillFeedContainer = new GameObject("KillFeed");
        m_KillFeedContainer.transform.SetParent(canvas.transform, false);
        RectTransform feedRect = m_KillFeedContainer.AddComponent<RectTransform>();
        feedRect.anchorMin = new Vector2(1f, 1f);
        feedRect.anchorMax = new Vector2(1f, 1f);
        feedRect.pivot = new Vector2(1f, 1f);
        feedRect.anchoredPosition = new Vector2(-64, -56);
        feedRect.sizeDelta = new Vector2(420, 240);
    }

    void BuildDeathOverlay(GameObject canvas)
    {
        m_DeathPanel = CreatePanel(canvas, "DeathOverlay",
            new Color(6f / 255f, 8f / 255f, 10f / 255f, 0.72f), Vector2.zero, new Vector2(2400, 1400));
        VanguardUITheme.AddCornerFrame(m_DeathPanel);

        GameObject notice = new GameObject("DeathNotice");
        notice.transform.SetParent(m_DeathPanel.transform, false);
        notice.AddComponent<RectTransform>().sizeDelta = new Vector2(920f, 360f);

        CreateText(notice, "Eyebrow", "COMBAT STATUS", 13, AccentRed, FontStyles.Bold,
            new Vector2(0f, 130f), new Vector2(360f, 24f));
        m_DeathTitleText = CreateText(notice, "DeathTitle", "ELIMINATED", 38,
            TextPrimary, FontStyles.Bold, new Vector2(0, 82), new Vector2(820, 54));

        m_DeathSubtitleText = CreateText(notice, "DeathSubtitle", "By Player", 14,
            TextSecondary, FontStyles.Normal, new Vector2(0, 30), new Vector2(720, 30));

        m_DeathTimerText = CreateText(notice, "DeathTimer", "Respawning in 3.0s", 16,
            AccentAmber, FontStyles.Bold, new Vector2(0, -64), new Vector2(360, 80));
        AddOutline(m_DeathTimerText.gameObject, AccentAmber);

        m_DeathTitleText.raycastTarget = false;
        m_DeathSubtitleText.raycastTarget = false;
        m_DeathTimerText.raycastTarget = false;
    }

    void BuildCountdownOverlay(GameObject canvas)
    {
        m_CountdownPanel = CreatePanel(canvas, "CountdownOverlay",
            new Color(0f, 0f, 0f, 0.32f), Vector2.zero, new Vector2(2400, 1400));
        m_CountdownPanel.SetActive(false);

        m_CountdownLabelText = CreateText(m_CountdownPanel, "CountdownLabel", "GET READY", 18,
            AccentAmber, FontStyles.Bold, new Vector2(0, 112), new Vector2(360, 32));

        m_CountdownText = CreateText(m_CountdownPanel, "CountdownValue", "3", 86,
            TextPrimary, FontStyles.Bold, new Vector2(0, 12), new Vector2(220, 140));
        m_CountdownText.raycastTarget = false;
        m_CountdownLabelText.raycastTarget = false;
    }

    void BuildScoreboardOverlay(GameObject canvas)
    {
        m_ScoreboardPanel = CreatePanel(canvas, "ScoreOverlay", BgOverlay,
            Vector2.zero, new Vector2(2400, 1400));
        VanguardUITheme.AddCornerFrame(m_ScoreboardPanel);

        GameObject board = new GameObject("Board");
        board.transform.SetParent(m_ScoreboardPanel.transform, false);
        board.AddComponent<RectTransform>().sizeDelta = new Vector2(1600f, 780f);

        m_ScoreboardTimerText = CreateText(board, "SBTimer",
            "DEATHMATCH · 03:00 REMAINING", 16,
            AccentAmber, FontStyles.Normal, new Vector2(0, 334), new Vector2(760, 28));

        // Column headers
        CreateScoreHeader(board, 262);

        // Rows container
        m_RowsContainer = new GameObject("Rows");
        m_RowsContainer.transform.SetParent(board.transform, false);
        RectTransform rowsRect = m_RowsContainer.AddComponent<RectTransform>();
        rowsRect.anchoredPosition = new Vector2(0, 142);
        rowsRect.sizeDelta = new Vector2(1600, 360);

        // Footer
        CreateText(board, "Footer", "HOLD TAB TO VIEW · RELEASE TO RETURN TO MATCH", 12,
            TextSecondary, FontStyles.Bold, new Vector2(0, -344), new Vector2(760, 24));
    }

    void BuildMatchEndOverlay(GameObject canvas)
    {
        m_MatchEndPanel = CreatePanel(canvas, "MatchEndOverlay", BgOverlay,
            Vector2.zero, new Vector2(2400, 1400));
        VanguardUITheme.AddCornerFrame(m_MatchEndPanel);

        GameObject board = CreatePanel(m_MatchEndPanel, "EndBoard", BgCard,
            new Vector2(0, 30), new Vector2(1180, 820));
        AddOutline(board, BorderSubtle);

        // Match complete label
        CreateText(board, "EndLabel", "Match complete", 11,
            AccentAmber, FontStyles.Bold, new Vector2(0, 354), new Vector2(500, 20));

        // Winner title
        m_EndTitle = CreateText(board, "EndTitle", "You won!", 42,
            TextPrimary, FontStyles.Bold, new Vector2(0, 304), new Vector2(900, 58));

        // Stats subtitle
        m_EndSubtitle = CreateText(board, "EndStats", "0 kills, 0 deaths", 14,
            TextSecondary, FontStyles.Normal, new Vector2(0, 254), new Vector2(700, 24));

        m_EndWinnerText = CreateText(board, "EndWinner", "Winner: Player",
            15, AccentAmber, FontStyles.Bold, new Vector2(0, 218), new Vector2(880, 24));

        m_EndPlacementText = CreateText(board, "EndPlacement", "Your placement: 1st of 1",
            14, TextSecondary, FontStyles.Normal, new Vector2(0, 184), new Vector2(700, 24));

        // Divider
        CreateImage(board, "EndDiv", BorderSubtle, new Vector2(0, 158), new Vector2(1080, 2));

        // Column headers
        CreateScoreHeader(board, 128);

        // Rows container
        m_EndRowsContainer = new GameObject("EndRows");
        m_EndRowsContainer.transform.SetParent(board.transform, false);
        RectTransform rowsRect = m_EndRowsContainer.AddComponent<RectTransform>();
        rowsRect.anchoredPosition = new Vector2(0, 16);
        rowsRect.sizeDelta = new Vector2(1080, 250);

        // Buttons
        float btnY = -316;

        Button playAgain = CreateButton(board, "PlayAgain", "Start next round",
            new Color32(26, 18, 6, 255), AccentAmber,
            new Vector2(290, btnY), new Vector2(500, 64));
        m_PlayAgainButton = playAgain;
        m_PlayAgainButtonLabel = playAgain.GetComponentInChildren<TextMeshProUGUI>();
        m_PlayAgainButton.onClick.AddListener(OnPlayAgain);

        Button backToMenu = CreateButton(board, "BackMenu", "Back to menu",
            TextSecondary, Color.clear,
            new Vector2(-290, btnY), new Vector2(500, 64));
        AddOutline(backToMenu.gameObject, BorderSubtle);
        backToMenu.onClick.AddListener(OnBackToMenu);
    }

    void BuildHelpOverlay(GameObject canvas)
    {
        m_HelpPanel = CreatePanel(canvas, "HelpOverlay",
            BgOverlay, Vector2.zero, new Vector2(2400, 1400));
        VanguardUITheme.AddCornerFrame(m_HelpPanel);

        GameObject board = CreatePanel(m_HelpPanel, "HelpBoard", BgCard,
            new Vector2(0, 35), new Vector2(920, 600));
        AddOutline(board, BorderSubtle);

        CreateText(board, "HelpLabel", "Quick reference", 11,
            AccentAmber, FontStyles.Bold, new Vector2(0, 252), new Vector2(760, 20));

        CreateText(board, "HelpTitle", "Deathmatch Rules", 24,
            TextPrimary, FontStyles.Bold, new Vector2(0, 208), new Vector2(760, 42));

        TextMeshProUGUI objective = CreateText(board, "Objective",
            "Most kills when the timer ends wins. If kills are tied, fewer deaths decides the winner. Score limit ends the round early.",
            14, TextSecondary, FontStyles.Normal, new Vector2(0, 148), new Vector2(760, 62));
        objective.textWrappingMode = TextWrappingModes.Normal;

        CreateImage(board, "HelpDiv", BorderSubtle, new Vector2(0, 98), new Vector2(800, 2));

        TextMeshProUGUI controls = CreateText(board, "Controls",
            "WASD        Move\nMouse       Aim\nLeft Click  Fire\nR           Reload\n1-3/Scroll  Switch weapon",
            14, TextPrimary, FontStyles.Normal, new Vector2(-230, -24), new Vector2(330, 210),
            TextAlignmentOptions.Left);
        controls.textWrappingMode = TextWrappingModes.NoWrap;

        TextMeshProUGUI gameplay = CreateText(board, "Gameplay",
            "E           Pick up\nTab         Scoreboard\nF1 or H     Rules\nEsc         Close this panel\nRespawn     3 seconds + protection",
            14, TextPrimary, FontStyles.Normal, new Vector2(235, -24), new Vector2(360, 210),
            TextAlignmentOptions.Left);
        gameplay.textWrappingMode = TextWrappingModes.NoWrap;

        CreateText(board, "HelpFooter", "Pickups reset every round. Host controls the next round.",
            11, TextDim, FontStyles.Normal, new Vector2(0, -260), new Vector2(760, 20));
    }

    void BuildPauseOverlay(GameObject canvas)
    {
        m_PausePanel = CreatePanel(canvas, "PauseOverlay",
            BgOverlay, Vector2.zero, new Vector2(2400, 1400));
        VanguardUITheme.AddCornerFrame(m_PausePanel);

        GameObject board = CreatePanel(m_PausePanel, "PauseBoard", BgCard,
            new Vector2(0, 25), new Vector2(1280, 770));
        AddOutline(board, BorderSubtle);

        CreateText(board, "PauseLabel", "MATCH PAUSED", 14,
            AccentAmber, FontStyles.Bold, new Vector2(-514, 316), new Vector2(220, 22),
            TextAlignmentOptions.Left);

        CreateText(board, "PauseTitle", "Settings", 34,
            TextPrimary, FontStyles.Bold, new Vector2(-446, 266), new Vector2(360, 46),
            TextAlignmentOptions.Left);

        GameObject gameplayTab = CreatePanel(board, "GameplayTab", AccentAmber,
            new Vector2(-426, 194), new Vector2(426, 76));
        CreateText(gameplayTab, "Label", "GAMEPLAY", 14, new Color32(26, 18, 6, 255), FontStyles.Bold,
            Vector2.zero, new Vector2(400, 70));
        CreateText(board, "AudioTab", "AUDIO", 14, TextSecondary, FontStyles.Bold,
            new Vector2(0, 194), new Vector2(426, 76));
        CreateText(board, "VideoTab", "VIDEO", 14, TextSecondary, FontStyles.Bold,
            new Vector2(426, 194), new Vector2(426, 76));
        CreateImage(board, "TabsRule", BorderSubtle, new Vector2(0, 155), new Vector2(1280, 2));

        Button resumeButton = CreateButton(board, "ResumeButton", "RESUME",
            new Color32(26, 18, 6, 255), AccentAmber, new Vector2(480, -310), new Vector2(250, 64));
        resumeButton.onClick.AddListener(() => SetPauseOverlayVisible(false));

        CreateSettingsStepper(board, "Sensitivity", "Mouse sensitivity",
            new Vector2(0, 92), out m_SensitivityValueText,
            () => AdjustLookSensitivity(-0.1f), () => AdjustLookSensitivity(0.1f));

        CreateSettingsStepper(board, "Volume", "Master volume",
            new Vector2(0, 10), out m_MasterVolumeValueText,
            () => AdjustMasterVolume(-0.1f), () => AdjustMasterVolume(0.1f));

        Button fullscreenButton = CreateButton(board, "FullscreenButton", "FULLSCREEN",
            TextPrimary, Color.clear, new Vector2(0, -78), new Vector2(1120, 56));
        AddOutline(fullscreenButton.gameObject, BorderSubtle);
        fullscreenButton.onClick.AddListener(ToggleFullscreen);

        CreateImage(board, "FooterRule", BorderSubtle, new Vector2(0, -258), new Vector2(1280, 2));
        Button leaveButton = CreateButton(board, "LeaveMatchButton", "LEAVE MATCH",
            AccentRed, Color.clear, new Vector2(-470, -310), new Vector2(286, 64));
        AddOutline(leaveButton.gameObject, AccentRed);
        leaveButton.onClick.AddListener(OnBackToMenu);
    }

    void CreateSettingsStepper(GameObject parent, string name, string label, Vector2 position,
        out TextMeshProUGUI valueText, UnityEngine.Events.UnityAction onMinus, UnityEngine.Events.UnityAction onPlus)
    {
        GameObject row = new GameObject(name);
        row.transform.SetParent(parent.transform, false);
        RectTransform rowRect = row.AddComponent<RectTransform>();
        rowRect.anchoredPosition = position;
        rowRect.sizeDelta = new Vector2(1120, 64);

        CreateText(row, $"{name}Label", label, 16,
            TextPrimary, FontStyles.Normal, new Vector2(-400, 0), new Vector2(300, 32), TextAlignmentOptions.Left);

        Button minus = CreateButton(row, $"{name}Minus", "−", TextPrimary, Color.clear,
            new Vector2(360, 0), new Vector2(50, 42));
        AddOutline(minus.gameObject, BorderSubtle);
        minus.onClick.AddListener(onMinus);

        valueText = CreateText(row, $"{name}Value", "1.0", 17,
            AccentAmber, FontStyles.Bold, new Vector2(438, 0), new Vector2(80, 36));

        Button plus = CreateButton(row, $"{name}Plus", "+", TextPrimary, Color.clear,
            new Vector2(520, 0), new Vector2(50, 42));
        AddOutline(plus.gameObject, BorderSubtle);
        plus.onClick.AddListener(onPlus);
    }

    void CreateScoreHeader(GameObject parent, float yPos)
    {
        float[] xPositions = { -500, -402, 220, 342, 468 };
        string[] labels = { "#", "PLAYER", "K", "D", "K/D" };
        TextAlignmentOptions[] aligns = {
            TextAlignmentOptions.Center, TextAlignmentOptions.Left,
            TextAlignmentOptions.Center, TextAlignmentOptions.Center,
            TextAlignmentOptions.Center
        };
        float[] widths = { 42, 420, 80, 80, 90 };

        for (int i = 0; i < labels.Length; i++)
        {
            CreateText(parent, $"H_{labels[i]}", labels[i], 12,
                TextSecondary, FontStyles.Bold, new Vector2(xPositions[i], yPos),
                new Vector2(widths[i], 20), aligns[i]);
        }

        CreateImage(parent, "HeaderDiv", BorderSubtle,
            new Vector2(0, yPos - 28), new Vector2(1080, 2));
    }

    void CreateScoreRow(GameObject parent, List<GameObject> rows,
        PlayerScoreData data, int rank, bool isLocal, float yPos)
    {
        Color rowBg = isLocal ? BgRowLocal : BgRow;
        Color nameColor = isLocal ? AccentBlue : TextPrimary;

        GameObject row = CreatePanel(parent, $"Row_{data.ClientId}", rowBg,
            new Vector2(0, yPos), new Vector2(1080, 64));
        rows.Add(row);

        // Rank
        CreateText(row, "Rank", rank.ToString(), 14,
            rank == 1 ? AccentBlue : TextSecondary, FontStyles.Bold,
            new Vector2(-500, 0), new Vector2(42, 58), TextAlignmentOptions.Center);

        // Name
        string playerName = isLocal ? "YOU" : $"PLAYER {data.ClientId}";
        CreateText(row, "Name", playerName, 17,
            nameColor, FontStyles.Bold,
            new Vector2(-402, 0), new Vector2(420, 58), TextAlignmentOptions.Left);

        // Kills
        CreateText(row, "Kills", data.Kills.ToString(), 18,
            TextPrimary, FontStyles.Bold,
            new Vector2(220, 0), new Vector2(80, 58), TextAlignmentOptions.Center);

        // Deaths
        CreateText(row, "Deaths", data.Deaths.ToString(), 18,
            TextSecondary, FontStyles.Normal,
            new Vector2(342, 0), new Vector2(80, 58), TextAlignmentOptions.Center);

        // K/D
        float kd = data.Deaths > 0 ? (float)data.Kills / data.Deaths : data.Kills;
        Color kdColor = kd >= 1f ? AccentGreenText : AccentRed;
        CreateText(row, "KD", kd.ToString("F2"), 17,
            kdColor, FontStyles.Bold,
            new Vector2(468, 0), new Vector2(90, 58), TextAlignmentOptions.Center);
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
        return $"E    Pick up {GetPickupName(pickup)}";
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

    void AdjustLookSensitivity(float delta)
    {
        PlayerInputHandler input = m_LocalPlayer != null
            ? m_LocalPlayer.GetComponent<PlayerInputHandler>()
            : FindFirstObjectByType<PlayerInputHandler>();

        float current = input != null
            ? input.LookSensitivity
            : PlayerPrefs.GetFloat(PlayerInputHandler.LookSensitivityPrefsKey, 1f);

        float next = Mathf.Clamp(current + delta, 0.2f, 3f);
        if (input != null)
        {
            input.LookSensitivity = next;
        }

        PlayerPrefs.SetFloat(PlayerInputHandler.LookSensitivityPrefsKey, next);
        PlayerPrefs.Save();
        RefreshSettingsValues();
    }

    void AdjustMasterVolume(float delta)
    {
        float current = PlayerPrefs.GetFloat(k_MasterVolumePrefsKey, AudioUtility.GetMasterVolume());
        float next = Mathf.Clamp01(current + delta);
        AudioUtility.SetMasterVolume(next);
        PlayerPrefs.SetFloat(k_MasterVolumePrefsKey, next);
        PlayerPrefs.Save();
        RefreshSettingsValues();
    }

    void ToggleFullscreen()
    {
        Screen.fullScreen = !Screen.fullScreen;
    }

    void RefreshSettingsValues()
    {
        if (m_SensitivityValueText != null)
        {
            float sensitivity = PlayerPrefs.GetFloat(PlayerInputHandler.LookSensitivityPrefsKey, 1f);
            if (m_LocalPlayer != null)
            {
                PlayerInputHandler input = m_LocalPlayer.GetComponent<PlayerInputHandler>();
                if (input != null)
                {
                    sensitivity = input.LookSensitivity;
                }
            }

            m_SensitivityValueText.text = sensitivity.ToString("0.0");
        }

        if (m_MasterVolumeValueText != null)
        {
            float volume = PlayerPrefs.GetFloat(k_MasterVolumePrefsKey, AudioUtility.GetMasterVolume());
            m_MasterVolumeValueText.text = $"{Mathf.RoundToInt(volume * 100f)}%";
        }
    }

    void PlayHudSfx(AudioClip clip, AudioUtility.AudioGroups group)
    {
        if (clip == null)
            return;

        AudioUtility.CreateSFX(clip, Vector3.zero, group, 0f);
    }

    void SetCombatHudVisible(bool visible)
    {
        if (m_HudBar != null)
        {
            m_HudBar.SetActive(visible);
        }

        if (m_CrosshairText != null)
        {
            m_CrosshairText.gameObject.SetActive(visible);
        }
    }

    void SyncToolkitUI()
    {
        if (m_Toolkit == null || !m_Toolkit.IsReady)
            return;

        float healthRatio = m_LocalHealth != null ? Mathf.Clamp01(m_LocalHealth.GetRatio()) : 0f;
        WeaponController activeWeapon = m_LocalWeapons != null ? m_LocalWeapons.GetActiveWeapon() : null;
        m_Toolkit.SetHud(
            m_TimerText != null ? m_TimerText.text : "00:00",
            m_KillsText != null ? m_KillsText.text : "0",
            m_DeathsText != null ? m_DeathsText.text : "0",
            m_HealthText != null ? m_HealthText.text : "0",
            healthRatio,
            m_WeaponText != null ? m_WeaponText.text : "UNARMED",
            activeWeapon != null ? activeWeapon.GetCurrentAmmo() : 0,
            activeWeapon != null ? activeWeapon.MaxAmmo : 0,
            m_GameFlowManager != null ? m_GameFlowManager.ScoreLimit : 1,
            m_HudBar != null && m_HudBar.activeSelf);

        bool damageVisible = m_DamageVignetteGroup != null && m_DamageVignetteGroup.alpha > 0.01f;
        m_Toolkit.SetFeedback(
            m_CrosshairText != null && m_CrosshairText.gameObject.activeSelf,
            m_HitMarkerText != null && m_HitMarkerText.gameObject.activeSelf,
            m_HitMarkerText != null ? m_HitMarkerText.text : "×",
            m_KillConfirmText != null && m_KillConfirmText.gameObject.activeSelf,
            m_KillConfirmText != null ? m_KillConfirmText.text : "ELIMINATED",
            m_ProtectionText != null && m_ProtectionText.gameObject.activeSelf,
            m_ProtectionText != null ? m_ProtectionText.text : "PROTECTED",
            m_PickupPromptText != null && m_PickupPromptText.gameObject.activeSelf,
            m_PickupPromptText != null ? m_PickupPromptText.text : "Pick up item",
            m_PickupFeedbackText != null && m_PickupFeedbackText.gameObject.activeSelf,
            m_PickupFeedbackText != null ? m_PickupFeedbackText.text : "ITEM ACQUIRED",
            m_LowHealthText != null && m_LowHealthText.gameObject.activeSelf,
            damageVisible);

        bool countdownVisible = m_CountdownPanel != null && m_CountdownPanel.activeSelf;
        bool deathVisible = m_DeathPanel != null && m_DeathPanel.activeSelf;
        bool scoreboardVisible = m_ScoreboardPanel != null && m_ScoreboardPanel.activeSelf;
        bool resultsVisible = m_MatchEndPanel != null && m_MatchEndPanel.activeSelf;
        bool pauseVisible = m_PausePanel != null && m_PausePanel.activeSelf;
        bool helpVisible = m_HelpPanel != null && m_HelpPanel.activeSelf;
        m_Toolkit.SetOverlays(
            countdownVisible,
            deathVisible,
            scoreboardVisible,
            resultsVisible,
            pauseVisible,
            helpVisible);

        if (countdownVisible)
        {
            m_Toolkit.SetCountdown(
                m_CountdownLabelText != null ? m_CountdownLabelText.text : "GET READY",
                m_CountdownText != null ? m_CountdownText.text : "3");
        }

        if (deathVisible)
        {
            m_Toolkit.SetDeath(
                m_DeathTitleText != null ? m_DeathTitleText.text : "ELIMINATED",
                m_DeathSubtitleText != null ? m_DeathSubtitleText.text : string.Empty,
                m_DeathTimerText != null ? m_DeathTimerText.text : "0.0");
        }

        if (scoreboardVisible)
        {
            List<VanguardScoreRowData> rows = BuildToolkitScoreRows();
            m_Toolkit.SetScoreboard(
                m_ScoreboardTimerText != null ? m_ScoreboardTimerText.text : "DEATHMATCH",
                rows);
        }

        if (resultsVisible)
        {
            List<VanguardScoreRowData> rows = BuildToolkitScoreRows();
            m_Toolkit.SetResults(
                m_EndTitle != null ? m_EndTitle.text : "MATCH COMPLETE",
                m_EndSubtitle != null ? m_EndSubtitle.text : string.Empty,
                m_EndWinnerText != null ? m_EndWinnerText.text : string.Empty,
                m_EndPlacementText != null ? m_EndPlacementText.text : string.Empty,
                rows,
                m_PlayAgainButton != null && m_PlayAgainButton.interactable,
                m_PlayAgainButtonLabel != null ? m_PlayAgainButtonLabel.text : "WAITING FOR HOST");
        }

        if (m_LastToolkitKillFeedCount != m_KillFeedRows.Count)
        {
            m_LastToolkitKillFeedCount = m_KillFeedRows.Count;
            m_ToolkitKillFeed.Clear();
            for (int i = 0; i < m_KillFeedRows.Count; i++)
            {
                GameObject row = m_KillFeedRows[i];
                if (row == null)
                    continue;

                TextMeshProUGUI label = row.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                {
                    m_ToolkitKillFeed.Add(label.text);
                }
            }

            m_Toolkit.SetKillFeed(m_ToolkitKillFeed);
        }

        m_Toolkit.SetSettings(
            m_SensitivityValueText != null ? m_SensitivityValueText.text : "1.0",
            m_MasterVolumeValueText != null ? m_MasterVolumeValueText.text : "100%");
    }

    List<VanguardScoreRowData> BuildToolkitScoreRows()
    {
        List<VanguardScoreRowData> rows = new List<VanguardScoreRowData>();
        if (m_GameFlowManager == null || NetworkManager.Singleton == null)
            return rows;

        List<PlayerScoreData> players = GetSortedPlayers();
        ulong localId = NetworkManager.Singleton.LocalClientId;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerScoreData player = players[i];
            bool isLocal = player.ClientId == localId;
            rows.Add(new VanguardScoreRowData(
                i + 1,
                isLocal ? "YOU" : GetDisplayName(player.ClientId).ToUpperInvariant(),
                player.Kills,
                player.Deaths,
                isLocal));
        }

        return rows;
    }

    void OnBackToMenu()
    {
        if (m_IsReturningToMenu)
            return;

        m_IsReturningToMenu = true;
        SetHelpOverlayVisible(false);
        SetPauseOverlayVisible(false);
        HideDeathOverlay();
        m_MatchEndShown = false;

        if (m_ScoreboardPanel != null)
            m_ScoreboardPanel.SetActive(false);

        if (m_MatchEndPanel != null)
            m_MatchEndPanel.SetActive(false);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        PlayerInputHandler.SetMenuInputBlocked(true);
        NetworkSessionExit.ReturnToMenu();
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
        obj.AddComponent<MenuButtonAnimator>();

        CreateText(obj, "Label", label, 13,
            textColor, FontStyles.Bold, Vector2.zero, size);

        return btn;
    }

    void AddOutline(GameObject obj, Color color)
    {
        Outline outline = obj.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(1f, 1f);
    }

    internal void ToolkitResume() => SetPauseOverlayVisible(false);
    internal void ToolkitLeaveMatch() => OnBackToMenu();
    internal void ToolkitPlayAgain() => OnPlayAgain();
    internal void ToolkitAdjustSensitivity(float delta) => AdjustLookSensitivity(delta);
    internal void ToolkitAdjustVolume(float delta) => AdjustMasterVolume(delta);
    internal void ToolkitToggleFullscreen() => ToggleFullscreen();

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
        PlayerInputHandler.SetMenuInputBlocked(false);
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
