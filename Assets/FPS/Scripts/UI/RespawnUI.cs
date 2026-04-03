using System.Collections;
using TMPro;
using Unity.FPS.Game;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class RespawnUI : MonoBehaviour
{
    static readonly Color BgOverlay = new Color(0f, 0f, 0f, 0.65f);
    static readonly Color TextPrimary = new Color(0.91f, 0.93f, 0.95f);
    static readonly Color AccentRed = new Color(0.89f, 0.29f, 0.29f);

    GameObject m_DeathPanel;
    TextMeshProUGUI m_MessageText;
    Coroutine m_CountdownCoroutine;

    void Start()
    {
        BuildUI();
        m_DeathPanel.SetActive(false);
    }

    void OnEnable()
    {
        GameFlowManager.OnPlayerKilled += OnPlayerKilled;
    }

    void OnDisable()
    {
        GameFlowManager.OnPlayerKilled -= OnPlayerKilled;
    }

    void BuildUI()
    {
        GameObject canvasObj = new GameObject("RespawnCanvas");
        canvasObj.transform.SetParent(transform);
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        // Full-screen dark overlay
        m_DeathPanel = new GameObject("DeathPanel");
        m_DeathPanel.transform.SetParent(canvasObj.transform, false);
        RectTransform panelRect = m_DeathPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        Image panelImg = m_DeathPanel.AddComponent<Image>();
        panelImg.color = BgOverlay;

        // "YOU DIED" title
        GameObject titleObj = new GameObject("TitleText");
        titleObj.transform.SetParent(m_DeathPanel.transform, false);
        RectTransform titleRect = titleObj.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 0.5f);
        titleRect.anchorMax = new Vector2(0.5f, 0.5f);
        titleRect.anchoredPosition = new Vector2(0, 40);
        titleRect.sizeDelta = new Vector2(600, 80);
        TextMeshProUGUI titleTmp = titleObj.AddComponent<TextMeshProUGUI>();
        titleTmp.text = "YOU DIED";
        titleTmp.fontSize = 64;
        titleTmp.color = AccentRed;
        titleTmp.fontStyle = FontStyles.Bold;
        titleTmp.alignment = TextAlignmentOptions.Center;

        // Countdown text
        GameObject countdownObj = new GameObject("CountdownText");
        countdownObj.transform.SetParent(m_DeathPanel.transform, false);
        RectTransform countdownRect = countdownObj.AddComponent<RectTransform>();
        countdownRect.anchorMin = new Vector2(0.5f, 0.5f);
        countdownRect.anchorMax = new Vector2(0.5f, 0.5f);
        countdownRect.anchoredPosition = new Vector2(0, -20);
        countdownRect.sizeDelta = new Vector2(600, 40);
        m_MessageText = countdownObj.AddComponent<TextMeshProUGUI>();
        m_MessageText.text = "Respawning in 3...";
        m_MessageText.fontSize = 24;
        m_MessageText.color = TextPrimary;
        m_MessageText.alignment = TextAlignmentOptions.Center;
    }

    void OnPlayerKilled(ulong victimId, ulong killerId)
    {
        if (victimId != NetworkManager.Singleton.LocalClientId) return;

        if (m_CountdownCoroutine != null)
            StopCoroutine(m_CountdownCoroutine);

        m_CountdownCoroutine = StartCoroutine(ShowRespawnCountdown());
    }

    IEnumerator ShowRespawnCountdown()
    {
        m_DeathPanel.SetActive(true);

        float remaining = GameFlowManager.RespawnDelay;
        while (remaining > 0f)
        {
            m_MessageText.text = $"Respawning in {Mathf.CeilToInt(remaining)}...";
            yield return null;
            remaining -= Time.deltaTime;
        }

        m_DeathPanel.SetActive(false);
        m_CountdownCoroutine = null;
    }
}
