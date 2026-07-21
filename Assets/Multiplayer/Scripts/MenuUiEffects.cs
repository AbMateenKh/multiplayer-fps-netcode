using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UIElements;
using CanvasButton = UnityEngine.UI.Button;
using CanvasImage = UnityEngine.UI.Image;
using ToolkitButton = UnityEngine.UIElements.Button;
using ToolkitLabel = UnityEngine.UIElements.Label;

[RequireComponent(typeof(CanvasGroup), typeof(RectTransform))]
public class AnimatedMenuPanel : MonoBehaviour
{
    public Vector2 HiddenOffset = new Vector2(0f, 28f);
    public float Duration = 0.32f;

    CanvasGroup m_CanvasGroup;
    RectTransform m_RectTransform;
    Vector2 m_ShownPosition;

    void Awake()
    {
        m_CanvasGroup = GetComponent<CanvasGroup>();
        m_RectTransform = GetComponent<RectTransform>();
        m_ShownPosition = m_RectTransform.anchoredPosition;
    }

    public void Show(bool instant = false)
    {
        gameObject.SetActive(true);
        StopAllCoroutines();

        if (instant)
        {
            m_RectTransform.anchoredPosition = m_ShownPosition;
            m_CanvasGroup.alpha = 1f;
            m_CanvasGroup.interactable = true;
            m_CanvasGroup.blocksRaycasts = true;
            return;
        }

        StartCoroutine(Animate(true));
    }

    public void Hide(bool instant = false)
    {
        StopAllCoroutines();

        if (instant)
        {
            m_RectTransform.anchoredPosition = m_ShownPosition + HiddenOffset;
            m_CanvasGroup.alpha = 0f;
            m_CanvasGroup.interactable = false;
            m_CanvasGroup.blocksRaycasts = false;
            gameObject.SetActive(false);
            return;
        }

        StartCoroutine(Animate(false));
    }

    System.Collections.IEnumerator Animate(bool show)
    {
        Vector2 startPosition = m_RectTransform.anchoredPosition;
        Vector2 endPosition = show ? m_ShownPosition : m_ShownPosition + HiddenOffset;
        float startAlpha = m_CanvasGroup.alpha;
        float endAlpha = show ? 1f : 0f;
        float elapsed = 0f;

        if (show)
        {
            m_CanvasGroup.interactable = false;
            m_CanvasGroup.blocksRaycasts = false;
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }
        }

        while (elapsed < Duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / Duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            m_RectTransform.anchoredPosition = Vector2.LerpUnclamped(startPosition, endPosition, eased);
            m_CanvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, eased);
            yield return null;
        }

        m_RectTransform.anchoredPosition = endPosition;
        m_CanvasGroup.alpha = endAlpha;
        m_CanvasGroup.interactable = show;
        m_CanvasGroup.blocksRaycasts = show;

        if (!show)
        {
            gameObject.SetActive(false);
        }
    }
}

public class FloatingUiElement : MonoBehaviour
{
    public Vector2 PositionAmplitude = new Vector2(18f, 12f);
    public float PositionSpeed = 0.45f;
    public float RotationAmplitude = 6f;
    public float RotationSpeed = 0.28f;
    public float ScaleAmplitude = 0.05f;
    public float ScaleSpeed = 0.52f;
    public float AlphaPulseAmplitude = 0.1f;
    public float AlphaPulseSpeed = 0.4f;
    public float PhaseOffset;

    RectTransform m_RectTransform;
    CanvasImage m_Image;
    Vector2 m_BasePosition;
    Vector3 m_BaseScale;
    float m_BaseRotation;
    float m_BaseAlpha;

    void Awake()
    {
        m_RectTransform = GetComponent<RectTransform>();
        m_Image = GetComponent<CanvasImage>();
        CaptureInitialState();
    }

    public void CaptureInitialState()
    {
        if (m_RectTransform == null)
        {
            m_RectTransform = GetComponent<RectTransform>();
        }

        if (m_Image == null)
        {
            m_Image = GetComponent<CanvasImage>();
        }

        m_BasePosition = m_RectTransform.anchoredPosition;
        m_BaseScale = m_RectTransform.localScale;
        m_BaseRotation = m_RectTransform.localEulerAngles.z;
        m_BaseAlpha = m_Image != null ? m_Image.color.a : 1f;
    }

    void Update()
    {
        float time = Time.unscaledTime + PhaseOffset;

        float x = Mathf.Sin(time * PositionSpeed) * PositionAmplitude.x;
        float y = Mathf.Cos(time * (PositionSpeed * 0.83f)) * PositionAmplitude.y;
        m_RectTransform.anchoredPosition = m_BasePosition + new Vector2(x, y);

        float scale = 1f + Mathf.Sin(time * ScaleSpeed) * ScaleAmplitude;
        m_RectTransform.localScale = m_BaseScale * scale;

        float rotation = m_BaseRotation + Mathf.Sin(time * RotationSpeed) * RotationAmplitude;
        m_RectTransform.localEulerAngles = new Vector3(0f, 0f, rotation);

        if (m_Image != null)
        {
            Color color = m_Image.color;
            color.a = Mathf.Clamp01(m_BaseAlpha + Mathf.Sin(time * AlphaPulseSpeed) * AlphaPulseAmplitude);
            m_Image.color = color;
        }
    }
}

[RequireComponent(typeof(CanvasButton), typeof(RectTransform))]
public class MenuButtonAnimator : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public float HoverScale = 1.03f;
    public float PressScale = 0.98f;
    public float LerpSpeed = 12f;

    RectTransform m_RectTransform;
    CanvasImage m_Image;
    Vector3 m_BaseScale;
    Color m_BaseColor;
    float m_TargetScale = 1f;
    float m_ColorBoost;

    void Awake()
    {
        m_RectTransform = GetComponent<RectTransform>();
        m_Image = GetComponent<CanvasImage>();
        m_BaseScale = m_RectTransform.localScale;
        m_BaseColor = m_Image != null ? m_Image.color : Color.white;
    }

    void Update()
    {
        float scale = Mathf.Lerp(m_RectTransform.localScale.x, m_TargetScale, Time.unscaledDeltaTime * LerpSpeed);
        m_RectTransform.localScale = m_BaseScale * scale;

        if (m_Image != null)
        {
            m_ColorBoost = Mathf.Lerp(m_ColorBoost, m_TargetScale > 1f ? 0.1f : 0f, Time.unscaledDeltaTime * LerpSpeed);
            m_Image.color = new Color(
                Mathf.Clamp01(m_BaseColor.r + m_ColorBoost),
                Mathf.Clamp01(m_BaseColor.g + m_ColorBoost),
                Mathf.Clamp01(m_BaseColor.b + m_ColorBoost),
                m_BaseColor.a);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        m_TargetScale = HoverScale;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        m_TargetScale = 1f;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        m_TargetScale = PressScale;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        m_TargetScale = HoverScale;
    }
}

public class MenuLoadingOverlay : MonoBehaviour
{
    CanvasGroup m_CanvasGroup;
    RectTransform m_Spinner;
    RectTransform m_ProgressFill;
    TextMeshProUGUI m_TitleText;
    TextMeshProUGUI m_MessageText;
    TextMeshProUGUI m_ProgressText;

    float m_TargetAlpha;
    float m_TargetProgress;
    float m_DisplayedProgress;
    float m_ShowStartTime;
    bool m_Visible;

    public void Bind(
        CanvasGroup canvasGroup,
        RectTransform spinner,
        RectTransform progressFill,
        TextMeshProUGUI titleText,
        TextMeshProUGUI messageText,
        TextMeshProUGUI progressText)
    {
        m_CanvasGroup = canvasGroup;
        m_Spinner = spinner;
        m_ProgressFill = progressFill;
        m_TitleText = titleText;
        m_MessageText = messageText;
        m_ProgressText = progressText;
        HideImmediate();
    }

    public void Show(string title, string message)
    {
        m_Visible = true;
        m_TargetAlpha = 1f;
        m_ShowStartTime = Time.unscaledTime;
        if (m_TitleText != null) m_TitleText.text = title;
        if (m_MessageText != null) m_MessageText.text = message;
        gameObject.SetActive(true);
    }

    public void SetTitle(string title)
    {
        if (m_TitleText != null) m_TitleText.text = title;
    }

    public void SetMessage(string message)
    {
        if (m_MessageText != null) m_MessageText.text = message;
    }

    public void SetProgress(float value)
    {
        m_TargetProgress = Mathf.Clamp01(value);
    }

    public bool HasMetMinimumVisibleTime(float duration)
    {
        return Time.unscaledTime - m_ShowStartTime >= duration;
    }

    public void HideImmediate()
    {
        m_Visible = false;
        m_TargetAlpha = 0f;
        m_TargetProgress = 0f;
        m_DisplayedProgress = 0f;

        if (m_CanvasGroup != null)
        {
            m_CanvasGroup.alpha = 0f;
            m_CanvasGroup.interactable = false;
            m_CanvasGroup.blocksRaycasts = false;
        }

        if (m_ProgressFill != null)
        {
            m_ProgressFill.localScale = new Vector3(0f, 1f, 1f);
        }

        gameObject.SetActive(false);
    }

    public void Hide()
    {
        m_Visible = false;
        m_TargetAlpha = 0f;
    }

    void Update()
    {
        if (m_CanvasGroup == null)
        {
            return;
        }

        m_CanvasGroup.alpha = Mathf.MoveTowards(m_CanvasGroup.alpha, m_TargetAlpha, Time.unscaledDeltaTime * 4f);
        m_CanvasGroup.interactable = m_CanvasGroup.alpha > 0.99f;
        m_CanvasGroup.blocksRaycasts = m_CanvasGroup.alpha > 0.01f;

        m_DisplayedProgress = Mathf.MoveTowards(m_DisplayedProgress, m_TargetProgress, Time.unscaledDeltaTime * 0.75f);
        if (m_ProgressFill != null)
        {
            m_ProgressFill.localScale = new Vector3(Mathf.Max(0.001f, m_DisplayedProgress), 1f, 1f);
        }

        if (m_ProgressText != null)
        {
            m_ProgressText.text = $"{Mathf.RoundToInt(m_DisplayedProgress * 100f)}%";
        }

        if (m_Spinner != null && m_Visible)
        {
            m_Spinner.Rotate(0f, 0f, -120f * Time.unscaledDeltaTime);
        }

        if (!m_Visible && m_CanvasGroup.alpha <= 0.001f && gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }
    }
}

public static class VanguardUITheme
{
    public static readonly Color Base = new Color32(12, 17, 22, 255);
    public static readonly Color BaseTransparent = new Color32(12, 17, 22, 232);
    public static readonly Color Panel = new Color32(18, 23, 29, 244);
    public static readonly Color PanelSoft = new Color32(24, 29, 34, 220);
    public static readonly Color Ink = new Color32(243, 242, 242, 255);
    public static readonly Color InkDim = new Color32(243, 242, 242, 143);
    public static readonly Color InkFaint = new Color32(243, 242, 242, 76);
    public static readonly Color Amber = new Color32(232, 166, 61, 255);
    public static readonly Color AmberSoft = new Color32(232, 166, 61, 48);
    public static readonly Color Green = new Color32(76, 174, 107, 255);
    public static readonly Color GreenSoft = new Color32(76, 174, 107, 48);
    public static readonly Color Red = new Color32(236, 48, 19, 255);
    public static readonly Color RedSoft = new Color32(236, 48, 19, 42);
    public static readonly Color Border = new Color32(243, 242, 242, 45);
    public static readonly Color BorderStrong = new Color32(243, 242, 242, 78);

    public static void ConfigureScaler(CanvasScaler scaler)
    {
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        scaler.referencePixelsPerUnit = 100f;
    }

    public static GameObject CreateSafeArea(GameObject canvas)
    {
        GameObject root = new GameObject("SafeArea");
        root.transform.SetParent(canvas.transform, false);

        RectTransform rect = root.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        root.AddComponent<ResponsiveSafeArea>();
        return root;
    }

    public static void AddCornerFrame(GameObject parent, float inset = 20f, float arm = 32f, float thickness = 4f)
    {
        CreateCorner(parent, "CornerTL", new Vector2(0f, 1f), new Vector2(inset, -inset), arm, thickness, 1f, -1f);
        CreateCorner(parent, "CornerTR", new Vector2(1f, 1f), new Vector2(-inset, -inset), arm, thickness, -1f, -1f);
        CreateCorner(parent, "CornerBL", new Vector2(0f, 0f), new Vector2(inset, inset), arm, thickness, 1f, 1f);
        CreateCorner(parent, "CornerBR", new Vector2(1f, 0f), new Vector2(-inset, inset), arm, thickness, -1f, 1f);
    }

    static void CreateCorner(
        GameObject parent,
        string name,
        Vector2 anchor,
        Vector2 position,
        float arm,
        float thickness,
        float horizontalDirection,
        float verticalDirection)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(parent.transform, false);
        RectTransform rootRect = root.AddComponent<RectTransform>();
        rootRect.anchorMin = anchor;
        rootRect.anchorMax = anchor;
        rootRect.pivot = anchor;
        rootRect.anchoredPosition = position;
        rootRect.sizeDelta = Vector2.zero;

        CreateRule(root, "Horizontal", new Vector2(horizontalDirection * arm * 0.5f, 0f),
            new Vector2(arm, thickness));
        CreateRule(root, "Vertical", new Vector2(0f, verticalDirection * arm * 0.5f),
            new Vector2(thickness, arm));
    }

    static void CreateRule(GameObject parent, string name, Vector2 position, Vector2 size)
    {
        GameObject rule = new GameObject(name);
        rule.transform.SetParent(parent.transform, false);
        RectTransform rect = rule.AddComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        CanvasImage image = rule.AddComponent<CanvasImage>();
        image.color = Amber;
        image.raycastTarget = false;
    }
}

[RequireComponent(typeof(RectTransform))]
public class ResponsiveSafeArea : MonoBehaviour
{
    RectTransform m_RectTransform;
    Rect m_LastSafeArea;
    Vector2Int m_LastScreenSize;

    void Awake()
    {
        m_RectTransform = GetComponent<RectTransform>();
        Apply();
    }

    void Update()
    {
        if (m_LastSafeArea != Screen.safeArea ||
            m_LastScreenSize.x != Screen.width ||
            m_LastScreenSize.y != Screen.height)
        {
            Apply();
        }
    }

    void Apply()
    {
        Rect safe = Screen.safeArea;
        float width = Mathf.Max(1f, Screen.width);
        float height = Mathf.Max(1f, Screen.height);

        m_RectTransform.anchorMin = new Vector2(safe.xMin / width, safe.yMin / height);
        m_RectTransform.anchorMax = new Vector2(safe.xMax / width, safe.yMax / height);
        m_RectTransform.offsetMin = Vector2.zero;
        m_RectTransform.offsetMax = Vector2.zero;

        m_LastSafeArea = safe;
        m_LastScreenSize = new Vector2Int(Screen.width, Screen.height);
    }
}

public static class VanguardToolkitRuntime
{
    public static UIDocument CreateDocument(GameObject owner, string resourcePath, int sortingOrder)
    {
        VisualTreeAsset tree = Resources.Load<VisualTreeAsset>(resourcePath);
        if (tree == null)
        {
            Debug.LogError($"[UI Toolkit] Missing VisualTreeAsset at Resources/{resourcePath}.");
            return null;
        }

        ThemeStyleSheet theme = Resources.Load<ThemeStyleSheet>("Vanguard/RuntimeTheme");
        if (theme == null)
        {
            Debug.LogError("[UI Toolkit] Missing runtime ThemeStyleSheet at Resources/Vanguard/RuntimeTheme.");
            return null;
        }

        GameObject documentObject = new GameObject(resourcePath.Replace('/', '_'));
        documentObject.transform.SetParent(owner.transform, false);
        documentObject.SetActive(false);

        PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.name = $"{resourcePath}_PanelSettings";
        settings.hideFlags = HideFlags.DontSave;
        settings.themeStyleSheet = theme;
        settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        settings.referenceResolution = new Vector2Int(1920, 1080);
        settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        settings.match = 0.5f;

        UIDocument document = documentObject.AddComponent<UIDocument>();
        document.panelSettings = settings;
        document.visualTreeAsset = tree;
        document.sortingOrder = sortingOrder;
        documentObject.SetActive(true);

        VisualElement root = document.rootVisualElement;
        root.RegisterCallback<GeometryChangedEvent>(_ =>
        {
            float width = root.resolvedStyle.width;
            float height = root.resolvedStyle.height;
            bool compact = width < 1500f || height < 820f;
            root.EnableInClassList("compact", compact);
        });

        return document;
    }

    public static void SetDisplayed(this VisualElement element, bool displayed)
    {
        if (element != null)
        {
            element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    public static void PrimeEntry(VisualElement element, string baseClass, string seedClass, int delayMs = 1)
    {
        if (element == null)
            return;

        element.AddToClassList(baseClass);
        element.AddToClassList(seedClass);
        element.schedule.Execute(() => element.RemoveFromClassList(seedClass)).StartingIn(delayMs);
    }

    public static void Pulse(VisualElement element, string className = "juice-punch", int durationMs = 140)
    {
        if (element == null)
            return;

        element.RemoveFromClassList(className);
        element.schedule.Execute(() =>
        {
            element.AddToClassList(className);
            element.schedule.Execute(() => element.RemoveFromClassList(className)).StartingIn(durationMs);
        }).StartingIn(1);
    }

    public static void Shake(VisualElement element)
    {
        if (element == null)
            return;

        element.RemoveFromClassList("juice-shake-left");
        element.RemoveFromClassList("juice-shake-right");
        element.schedule.Execute(() => element.AddToClassList("juice-shake-left")).StartingIn(1);
        element.schedule.Execute(() =>
        {
            element.RemoveFromClassList("juice-shake-left");
            element.AddToClassList("juice-shake-right");
        }).StartingIn(55);
        element.schedule.Execute(() => element.RemoveFromClassList("juice-shake-right")).StartingIn(110);
    }
}

public sealed class VanguardMenuToolkit
{
    readonly MenuUI m_Owner;
    readonly UIDocument m_Document;
    readonly VisualElement m_Root;
    readonly VisualElement[] m_Screens;
    readonly VisualElement[] m_PlayerSlots;
    readonly TextField m_PlayerNameField;
    readonly TextField m_JoinCodeField;
    readonly ToolkitLabel m_StatusLabel;
    readonly ToolkitLabel m_LobbyType;
    readonly ToolkitLabel m_LobbyCode;
    readonly ToolkitLabel m_PlayerCount;
    readonly VisualElement m_LobbyCodeSection;
    readonly ToolkitButton m_ReadyButton;
    readonly ToolkitButton m_StartButton;
    readonly VisualElement m_LoadingScreen;
    readonly ToolkitLabel m_LoadingTitle;
    readonly ToolkitLabel m_LoadingMessage;
    readonly ToolkitLabel m_LoadingPercent;
    readonly VisualElement m_LoadingProgress;
    readonly VisualElement m_LoadingSpark;
    readonly VisualElement m_ScanSweep;
    readonly VisualElement m_StatusDot;
    readonly ToolkitLabel m_SensitivityValue;
    readonly ToolkitLabel m_VolumeValue;
    VisualElement m_ActiveScreen;
    string m_LastStatus;

    public string PlayerName => m_PlayerNameField?.value?.Trim();
    public string JoinCode => m_JoinCodeField?.value?.Trim().ToUpperInvariant();
    public bool IsReady => m_Document != null && m_Root != null;
    public bool IsRenderable => IsReady && m_Root.panel != null &&
        m_Root.resolvedStyle.width > 1f && m_Root.resolvedStyle.height > 1f;

    public VanguardMenuToolkit(MenuUI owner)
    {
        m_Owner = owner;
        m_Document = VanguardToolkitRuntime.CreateDocument(owner.gameObject, "Vanguard/Menu", 200);
        if (m_Document == null)
        {
            return;
        }

        m_Root = m_Document.rootVisualElement.Q("menu-root");
        if (m_Root == null)
        {
            Debug.LogError("[UI Toolkit] Menu root is missing.");
            return;
        }

        m_Screens = new[]
        {
            m_Root.Q("landing-screen"),
            m_Root.Q("mode-screen"),
            m_Root.Q("join-screen"),
            m_Root.Q("lobby-screen"),
            m_Root.Q("settings-screen")
        };

        m_PlayerSlots = new VisualElement[4];
        for (int i = 0; i < m_PlayerSlots.Length; i++)
        {
            m_PlayerSlots[i] = m_Root.Q($"player-slot-{i}");
        }

        m_PlayerNameField = m_Root.Q<TextField>("player-name-field");
        m_JoinCodeField = m_Root.Q<TextField>("join-code-field");
        m_StatusLabel = m_Root.Q<ToolkitLabel>("status-label");
        m_LobbyType = m_Root.Q<ToolkitLabel>("lobby-type");
        m_LobbyCode = m_Root.Q<ToolkitLabel>("lobby-code");
        m_PlayerCount = m_Root.Q<ToolkitLabel>("player-count");
        m_LobbyCodeSection = m_Root.Q("lobby-code-section");
        m_ReadyButton = m_Root.Q<ToolkitButton>("ready-button");
        m_StartButton = m_Root.Q<ToolkitButton>("start-match-button");
        m_LoadingScreen = m_Root.Q("loading-screen");
        m_LoadingTitle = m_Root.Q<ToolkitLabel>("loading-title");
        m_LoadingMessage = m_Root.Q<ToolkitLabel>("loading-message");
        m_LoadingPercent = m_Root.Q<ToolkitLabel>("loading-percent");
        m_LoadingProgress = m_Root.Q("loading-progress");
        m_LoadingSpark = m_Root.Q("loading-spark");
        m_ScanSweep = m_Root.Q("menu-scan-sweep");
        m_StatusDot = m_Root.Q(className: "status-dot");
        m_SensitivityValue = m_Root.Q<ToolkitLabel>("menu-sensitivity-value");
        m_VolumeValue = m_Root.Q<ToolkitLabel>("menu-volume-value");

        Bind("single-player-button", owner.ToolkitStartSinglePlayer);
        Bind("multiplayer-button", owner.ToolkitOpenMainMenu);
        Bind("settings-button", owner.ToolkitOpenSettings);
        Bind("exit-button", owner.ToolkitExitGame);
        Bind("mode-back-button", owner.ToolkitReturnToLanding);
        Bind("public-button", owner.ToolkitCreatePublicLobby);
        Bind("private-button", owner.ToolkitCreatePrivateLobby);
        Bind("join-code-button", owner.ToolkitOpenJoinPanel);
        Bind("quick-join-button", owner.ToolkitQuickJoin);
        Bind("join-back-button", owner.ToolkitOpenMainMenu);
        Bind("join-button", owner.ToolkitJoinByCode);
        Bind("copy-code-button", owner.ToolkitCopyCode);
        Bind("ready-button", owner.ToolkitToggleReady);
        Bind("leave-lobby-button", owner.ToolkitLeaveLobby);
        Bind("start-match-button", owner.ToolkitStartGame);
        Bind("settings-back-button", owner.ToolkitReturnToLanding);
        Bind("menu-sensitivity-minus", () => owner.ToolkitAdjustSensitivity(-0.1f));
        Bind("menu-sensitivity-plus", () => owner.ToolkitAdjustSensitivity(0.1f));
        Bind("menu-volume-minus", () => owner.ToolkitAdjustVolume(-0.1f));
        Bind("menu-volume-plus", () => owner.ToolkitAdjustVolume(0.1f));
        Bind("menu-fullscreen-button", owner.ToolkitToggleFullscreen);

        if (m_JoinCodeField != null)
        {
            m_JoinCodeField.RegisterValueChangedCallback(evt =>
            {
                string normalized = evt.newValue.ToUpperInvariant();
                if (normalized != evt.newValue)
                {
                    m_JoinCodeField.SetValueWithoutNotify(normalized);
                }
            });
        }
    }

    void Bind(string name, System.Action action)
    {
        ToolkitButton button = m_Root.Q<ToolkitButton>(name);
        if (button != null)
        {
            button.clicked += action;
            button.RegisterCallback<PointerDownEvent>(_ =>
                VanguardToolkitRuntime.Pulse(button, "juice-punch", 90));
        }
    }

    public void ShowScreen(string state)
    {
        if (!IsReady)
            return;

        int activeIndex = state switch
        {
            "Landing" => 0,
            "ModeSelect" => 1,
            "Join" => 2,
            "Lobby" => 3,
            "Settings" => 4,
            _ => 0
        };

        for (int i = 0; i < m_Screens.Length; i++)
        {
            m_Screens[i].SetDisplayed(i == activeIndex);
        }

        VisualElement nextScreen = m_Screens[activeIndex];
        if (m_ActiveScreen != nextScreen)
        {
            m_ActiveScreen = nextScreen;
            VanguardToolkitRuntime.PrimeEntry(nextScreen, "juice-screen", "juice-screen--seed");
        }
    }

    public void SetStatus(string message)
    {
        if (m_StatusLabel != null)
        {
            string normalized = message.ToUpperInvariant();
            if (normalized != m_LastStatus)
            {
                m_LastStatus = normalized;
                m_StatusLabel.text = normalized;
                VanguardToolkitRuntime.Pulse(m_StatusLabel);
                VanguardToolkitRuntime.Pulse(m_StatusDot, "juice-impact", 120);
            }
        }
    }

    public void SetLobbyHeader(bool isPrivate, string code, int playerCount, int maxPlayers)
    {
        if (m_LobbyType != null) m_LobbyType.text = isPrivate ? "PRIVATE LOBBY" : "PUBLIC LOBBY";
        if (m_LobbyCode != null) m_LobbyCode.text = string.IsNullOrWhiteSpace(code) ? "------" : code;
        if (m_PlayerCount != null) m_PlayerCount.text = $"PLAYERS {playerCount}/{maxPlayers}";
        m_LobbyCodeSection?.SetDisplayed(isPrivate);
    }

    public void SetPlayers(List<PlayerLobbyData> players, int maxPlayers)
    {
        if (!IsReady)
            return;

        for (int i = 0; i < m_PlayerSlots.Length; i++)
        {
            VisualElement slot = m_PlayerSlots[i];
            slot.Clear();
            slot.EnableInClassList("player-slot--host", false);
            slot.EnableInClassList("player-slot--empty", false);

            if (i >= players.Count || i >= maxPlayers)
            {
                slot.EnableInClassList("player-slot--empty", true);
                slot.Add(new ToolkitLabel("⊕  WAITING FOR PLAYER"));
                VanguardToolkitRuntime.PrimeEntry(slot, "juice-row", "juice-row--seed", 35 * i);
                continue;
            }

            PlayerLobbyData player = players[i];
            slot.EnableInClassList("player-slot--host", player.IsHost);

            ToolkitLabel badge = new ToolkitLabel(GetInitials(player.Name));
            badge.AddToClassList("player-slot__badge");
            slot.Add(badge);

            VisualElement details = new VisualElement();
            details.AddToClassList("player-slot__details");
            ToolkitLabel name = new ToolkitLabel(player.IsHost ? $"{player.Name}  ◆" : player.Name);
            name.AddToClassList("player-slot__name");
            ToolkitLabel meta = new ToolkitLabel(player.IsHost ? "HOST · RELAY AUTHORITY" : "CONNECTED");
            meta.AddToClassList("player-slot__meta");
            details.Add(name);
            details.Add(meta);
            slot.Add(details);

            ToolkitLabel ready = new ToolkitLabel(player.IsHost || player.IsReady ? "READY" : "NOT READY");
            ready.AddToClassList("player-slot__state");
            ready.EnableInClassList("player-slot__state--ready", player.IsHost || player.IsReady);
            slot.Add(ready);
            VanguardToolkitRuntime.PrimeEntry(slot, "juice-row", "juice-row--seed", 35 * i);
        }
    }

    static string GetInitials(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return "--";

        string trimmed = playerName.Trim();
        return trimmed.Length == 1
            ? trimmed.ToUpperInvariant()
            : $"{char.ToUpperInvariant(trimmed[0])}{char.ToUpperInvariant(trimmed[^1])}";
    }

    public void SetLobbyControls(bool isHost, bool allReady, bool localReady, bool transitioning)
    {
        m_StartButton?.SetDisplayed(isHost);
        m_ReadyButton?.SetDisplayed(!isHost);

        if (m_StartButton != null)
        {
            m_StartButton.SetEnabled(isHost && allReady && !transitioning);
            m_StartButton.text = allReady ? "START MATCH" : "WAITING FOR READY PLAYERS";
        }

        if (m_ReadyButton != null)
        {
            m_ReadyButton.SetEnabled(!transitioning);
            m_ReadyButton.text = localReady ? "CANCEL READY" : "READY UP";
        }
    }

    public void SetSettings(float sensitivity, float volume)
    {
        if (m_SensitivityValue != null) m_SensitivityValue.text = sensitivity.ToString("0.0");
        if (m_VolumeValue != null) m_VolumeValue.text = $"{Mathf.RoundToInt(volume * 100f)}%";
    }

    public void ShowLoading(string title, string message, float progress)
    {
        m_LoadingScreen?.SetDisplayed(true);
        VanguardToolkitRuntime.PrimeEntry(m_LoadingScreen, "juice-overlay", "juice-overlay--seed");
        SetLoading(title, message, progress);
    }

    public void SetLoading(string title, string message, float progress)
    {
        progress = Mathf.Clamp01(progress);
        if (m_LoadingTitle != null) m_LoadingTitle.text = title.ToUpperInvariant();
        if (m_LoadingMessage != null) m_LoadingMessage.text = message;
        if (m_LoadingPercent != null) m_LoadingPercent.text = $"{Mathf.RoundToInt(progress * 100f)}%";
        if (m_LoadingProgress != null) m_LoadingProgress.style.width = Length.Percent(progress * 100f);
    }

    public void HideLoading()
    {
        m_LoadingScreen?.SetDisplayed(false);
    }

    public void Tick(float unscaledTime)
    {
        if (!IsReady)
            return;

        if (m_ScanSweep != null)
        {
            float sweep = Mathf.Repeat(unscaledTime * 7f, 108f) - 4f;
            m_ScanSweep.style.left = Length.Percent(sweep);
            m_ScanSweep.style.opacity = 0.24f + Mathf.PingPong(unscaledTime * 0.16f, 0.22f);
        }

        if (m_StatusDot != null)
        {
            m_StatusDot.style.opacity = 0.5f + Mathf.PingPong(unscaledTime * 0.75f, 0.5f);
        }

        if (m_LoadingSpark != null && m_LoadingScreen != null &&
            m_LoadingScreen.resolvedStyle.display != DisplayStyle.None)
        {
            m_LoadingSpark.style.opacity = 0.45f + Mathf.PingPong(unscaledTime * 2.5f, 0.55f);
        }
    }
}

public readonly struct VanguardScoreRowData
{
    public readonly string Rank;
    public readonly string Player;
    public readonly string Kills;
    public readonly string Deaths;
    public readonly string Ratio;
    public readonly bool IsLocal;

    public VanguardScoreRowData(int rank, string player, int kills, int deaths, bool isLocal)
    {
        Rank = rank.ToString();
        Player = player;
        Kills = kills.ToString();
        Deaths = deaths.ToString();
        Ratio = (deaths > 0 ? (float)kills / deaths : kills).ToString("F2");
        IsLocal = isLocal;
    }
}

public sealed class VanguardMatchToolkit
{
    readonly ScoreboardUI m_Owner;
    readonly UIDocument m_Document;
    readonly VisualElement m_Root;
    readonly VisualElement m_Hud;
    readonly VisualElement m_Countdown;
    readonly VisualElement m_Death;
    readonly VisualElement m_Scoreboard;
    readonly VisualElement m_Results;
    readonly VisualElement m_Pause;
    readonly VisualElement m_Help;
    readonly VisualElement m_DamageVignette;
    readonly VisualElement m_HealthFill;
    readonly VisualElement m_HealthPanel;
    readonly VisualElement m_HudScanline;
    readonly VisualElement m_KillFeed;
    readonly VisualElement m_ScoreRows;
    readonly VisualElement m_ResultRows;
    readonly VisualElement m_HitMarker;
    readonly VisualElement m_KillConfirm;
    readonly VisualElement m_PickupFeedback;
    readonly VisualElement m_LowHealth;
    readonly VisualElement m_CountdownValue;
    readonly VisualElement m_AmmoValue;
    readonly VisualElement m_AmmoReserve;
    readonly VisualElement m_ScoreProgressFill;
    readonly VisualElement[] m_AmmoPips;
    readonly VisualElement m_KillsValue;
    readonly ToolkitButton m_NextRound;
    string m_LastHealth;
    string m_LastAmmo;
    string m_LastKills;
    string m_LastCountdown;
    string m_LastScoreSignature;
    string m_LastResultSignature;
    string m_LastKillFeedSignature;
    float m_LastHealthRatio = -1f;
    float m_LastScoreProgress = -1f;
    float m_NextAnimationTick;
    bool m_HitVisible;
    bool m_KillVisible;
    bool m_PickupFeedbackVisible;
    bool m_DamageVisible;
    bool m_LowHealthVisible;
    bool m_CountdownVisible;
    bool m_DeathVisible;
    bool m_ScoreboardVisible;
    bool m_ResultsVisible;
    bool m_PauseVisible;
    bool m_HelpVisible;

    public bool IsReady => m_Document != null && m_Root != null;
    public bool IsRenderable => IsReady && m_Root.panel != null &&
        m_Root.resolvedStyle.width > 1f && m_Root.resolvedStyle.height > 1f;

    public VanguardMatchToolkit(ScoreboardUI owner)
    {
        m_Owner = owner;
        m_Document = VanguardToolkitRuntime.CreateDocument(owner.gameObject, "Vanguard/Match", 50);
        if (m_Document == null)
            return;

        m_Root = m_Document.rootVisualElement.Q("match-root");
        if (m_Root == null)
            return;

        m_Root.pickingMode = PickingMode.Ignore;
        m_Hud = m_Root.Q("hud-layer");
        m_Countdown = m_Root.Q("countdown-overlay");
        m_Death = m_Root.Q("death-overlay");
        m_Scoreboard = m_Root.Q("scoreboard-overlay");
        m_Results = m_Root.Q("results-overlay");
        m_Pause = m_Root.Q("pause-overlay");
        m_Help = m_Root.Q("help-overlay");
        m_DamageVignette = m_Root.Q("damage-vignette");
        m_HealthFill = m_Root.Q("health-fill");
        m_HealthPanel = m_Root.Q(className: "health");
        m_HudScanline = m_Root.Q("hud-scanline");
        m_KillFeed = m_Root.Q("kill-feed");
        m_ScoreRows = m_Root.Q("scoreboard-rows");
        m_ResultRows = m_Root.Q("results-rows");
        m_HitMarker = m_Root.Q("hit-marker");
        m_KillConfirm = m_Root.Q("kill-confirm");
        m_PickupFeedback = m_Root.Q("pickup-feedback");
        m_LowHealth = m_Root.Q("low-health");
        m_CountdownValue = m_Root.Q("countdown-value");
        m_AmmoValue = m_Root.Q("ammo-current");
        m_AmmoReserve = m_Root.Q("ammo-reserve");
        m_ScoreProgressFill = m_Root.Q("score-progress-fill");
        m_AmmoPips = new VisualElement[8];
        for (int i = 0; i < m_AmmoPips.Length; i++)
            m_AmmoPips[i] = m_Root.Q(className: $"ammo-pip--{i + 1}");
        m_KillsValue = m_Root.Q("hud-kills");
        m_NextRound = m_Root.Q<ToolkitButton>("next-round-button");

        m_HitMarker?.AddToClassList("juice-reactive");
        m_KillConfirm?.AddToClassList("juice-reactive");
        m_PickupFeedback?.AddToClassList("juice-reactive");
        m_LowHealth?.AddToClassList("juice-reactive");
        m_CountdownValue?.AddToClassList("juice-reactive");
        m_AmmoValue?.AddToClassList("juice-reactive");
        m_KillsValue?.AddToClassList("juice-reactive");
        m_HealthPanel?.AddToClassList("juice-reactive");

        m_Hud.pickingMode = PickingMode.Ignore;
        m_Countdown.pickingMode = PickingMode.Ignore;
        m_Death.pickingMode = PickingMode.Ignore;
        m_Scoreboard.pickingMode = PickingMode.Ignore;
        m_DamageVignette.pickingMode = PickingMode.Ignore;
        m_Results.pickingMode = PickingMode.Position;
        m_Pause.pickingMode = PickingMode.Position;
        m_Help.pickingMode = PickingMode.Ignore;

        Bind("resume-button", owner.ToolkitResume);
        Bind("leave-match-button", owner.ToolkitLeaveMatch);
        Bind("return-menu-button", owner.ToolkitLeaveMatch);
        Bind("next-round-button", owner.ToolkitPlayAgain);
        Bind("pause-sensitivity-minus", () => owner.ToolkitAdjustSensitivity(-0.1f));
        Bind("pause-sensitivity-plus", () => owner.ToolkitAdjustSensitivity(0.1f));
        Bind("pause-volume-minus", () => owner.ToolkitAdjustVolume(-0.1f));
        Bind("pause-volume-plus", () => owner.ToolkitAdjustVolume(0.1f));
        Bind("pause-fullscreen-button", owner.ToolkitToggleFullscreen);
    }

    void Bind(string name, System.Action action)
    {
        ToolkitButton button = m_Root.Q<ToolkitButton>(name);
        if (button != null)
        {
            button.clicked += action;
            button.RegisterCallback<PointerDownEvent>(_ =>
                VanguardToolkitRuntime.Pulse(button, "juice-punch", 90));
        }
    }

    void SetLabel(string name, string text)
    {
        ToolkitLabel label = m_Root.Q<ToolkitLabel>(name);
        if (label != null && label.text != text)
        {
            label.text = text;
        }
    }

    void SetVisible(string name, bool visible)
    {
        VisualElement element = m_Root.Q(name);
        if (element != null &&
            element.resolvedStyle.display != (visible ? DisplayStyle.Flex : DisplayStyle.None))
        {
            element.SetDisplayed(visible);
        }
    }

    public void SetHud(
        string timer,
        string kills,
        string deaths,
        string health,
        float healthRatio,
        string weapon,
        int currentAmmo,
        int maxAmmo,
        int scoreLimit,
        bool visible)
    {
        m_Hud?.SetDisplayed(visible);
        SetLabel("hud-timer", timer);
        if (kills != m_LastKills)
        {
            bool hadPreviousKills = m_LastKills != null;
            m_LastKills = kills;
            SetLabel("hud-kills", kills);
            if (hadPreviousKills)
                VanguardToolkitRuntime.Pulse(m_KillsValue, "juice-impact", 150);
        }
        SetLabel("hud-deaths", deaths);
        if (int.TryParse(kills, out int killCount) && int.TryParse(deaths, out int deathCount))
        {
            int targetScore = Mathf.Max(1, scoreLimit);
            float ratio = deathCount > 0 ? (float)killCount / deathCount : killCount;
            SetLabel("hud-ratio", ratio.ToString("0.00"));
            SetLabel("score-progress-label", $"{killCount} / {targetScore}");
            SetLabel("match-mode", $"DEATHMATCH \u00B7 FIRST TO {targetScore}");
            float scoreProgress = Mathf.Clamp01((float)killCount / targetScore);
            if (m_ScoreProgressFill != null && !Mathf.Approximately(scoreProgress, m_LastScoreProgress))
            {
                m_LastScoreProgress = scoreProgress;
                m_ScoreProgressFill.style.width = Length.Percent(scoreProgress * 100f);
            }
        }
        if (health != m_LastHealth)
        {
            bool hadPreviousHealth = m_LastHealth != null;
            m_LastHealth = health;
            SetLabel("health-value", health);
            if (hadPreviousHealth)
                VanguardToolkitRuntime.Pulse(m_Root.Q("health-value"));
        }
        SetLabel("weapon-name", weapon);
        string ammo = $"{currentAmmo}/{maxAmmo}";
        if (ammo != m_LastAmmo)
        {
            bool hadPreviousAmmo = m_LastAmmo != null;
            m_LastAmmo = ammo;
            SetLabel("ammo-current", currentAmmo.ToString("00"));
            SetLabel("ammo-reserve", maxAmmo.ToString("00"));
            if (hadPreviousAmmo)
                VanguardToolkitRuntime.Pulse(m_AmmoValue);
        }

        float ammoRatio = maxAmmo > 0 ? Mathf.Clamp01((float)currentAmmo / maxAmmo) : 0f;
        int activePips = Mathf.CeilToInt(ammoRatio * m_AmmoPips.Length);
        for (int i = 0; i < m_AmmoPips.Length; i++)
            m_AmmoPips[i]?.EnableInClassList("ammo-pip--empty", i >= activePips);
        m_AmmoValue?.EnableInClassList("ammo__current--low", currentAmmo > 0 && ammoRatio <= 0.25f);

        if (m_HealthFill != null && !Mathf.Approximately(healthRatio, m_LastHealthRatio))
        {
            m_LastHealthRatio = healthRatio;
            m_HealthFill.style.width = Length.Percent(Mathf.Clamp01(healthRatio) * 100f);
            bool critical = healthRatio > 0f && healthRatio <= 0.3f;
            m_HealthFill.EnableInClassList("health__fill--critical", critical);
            m_Root.Q("health-value")?.EnableInClassList("health__value--critical", critical);
            SetLabel("health-status", critical ? "CRITICAL" : healthRatio <= 0.65f ? "WOUNDED" : "COMBAT READY");
            m_Root.Q("health-status")?.EnableInClassList("health__status--critical", critical);
        }
    }

    public void SetFeedback(
        bool crosshair,
        bool hit,
        string hitText,
        bool kill,
        string killText,
        bool protection,
        string protectionText,
        bool pickupPrompt,
        string pickupText,
        bool pickupFeedback,
        string pickupFeedbackText,
        bool lowHealth,
        bool damageVignette)
    {
        SetVisible("crosshair", crosshair);
        SetVisible("hit-marker", hit);
        SetLabel("hit-marker", hitText);
        SetVisible("kill-confirm", kill);
        SetLabel("kill-confirm", killText);
        SetVisible("protection-label", protection);
        SetLabel("protection-label", protectionText);
        SetVisible("pickup-prompt", pickupPrompt);
        SetLabel("pickup-prompt-label", StripPickupKey(pickupText));
        SetVisible("pickup-feedback", pickupFeedback);
        SetLabel("pickup-feedback", pickupFeedbackText);
        SetVisible("low-health", lowHealth);
        m_DamageVignette?.SetDisplayed(damageVignette);

        if (hit && !m_HitVisible)
            VanguardToolkitRuntime.Pulse(m_HitMarker, "juice-impact", 105);
        if (kill && !m_KillVisible)
            VanguardToolkitRuntime.Pulse(m_KillConfirm, "juice-impact", 150);
        if (pickupFeedback && !m_PickupFeedbackVisible)
            VanguardToolkitRuntime.Pulse(m_PickupFeedback, "juice-impact", 150);
        if (damageVignette && !m_DamageVisible)
        {
            VanguardToolkitRuntime.Pulse(m_DamageVignette, "juice-impact", 100);
            VanguardToolkitRuntime.Shake(m_HealthPanel);
        }
        if (lowHealth && !m_LowHealthVisible)
            VanguardToolkitRuntime.Pulse(m_LowHealth, "juice-impact", 180);

        m_HitVisible = hit;
        m_KillVisible = kill;
        m_PickupFeedbackVisible = pickupFeedback;
        m_DamageVisible = damageVignette;
        m_LowHealthVisible = lowHealth;
    }

    static string StripPickupKey(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "Pick up item";

        return text.StartsWith("E", System.StringComparison.OrdinalIgnoreCase)
            ? text.Substring(1).Trim()
            : text;
    }

    public void SetOverlays(bool countdown, bool death, bool scoreboard, bool results, bool pause, bool help)
    {
        SetOverlay(m_Countdown, countdown, ref m_CountdownVisible);
        SetOverlay(m_Death, death, ref m_DeathVisible);
        SetOverlay(m_Scoreboard, scoreboard, ref m_ScoreboardVisible);
        SetOverlay(m_Results, results, ref m_ResultsVisible);
        SetOverlay(m_Pause, pause, ref m_PauseVisible);
        SetOverlay(m_Help, help, ref m_HelpVisible);
    }

    static void SetOverlay(VisualElement overlay, bool visible, ref bool wasVisible)
    {
        if (visible == wasVisible)
            return;

        overlay?.SetDisplayed(visible);
        if (visible && !wasVisible)
            VanguardToolkitRuntime.PrimeEntry(overlay, "juice-overlay", "juice-overlay--seed");
        wasVisible = visible;
    }

    public void SetCountdown(string label, string value)
    {
        SetLabel("countdown-label", label);
        if (value != m_LastCountdown)
        {
            m_LastCountdown = value;
            SetLabel("countdown-value", value);
            VanguardToolkitRuntime.Pulse(m_CountdownValue, "juice-impact", 150);
        }
    }

    public void SetDeath(string title, string subtitle, string timer)
    {
        SetLabel("death-title", title);
        SetLabel("death-subtitle", subtitle);
        SetLabel("death-timer", ExtractNumericTimer(timer));
    }

    static string ExtractNumericTimer(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "0.0";

        System.Text.StringBuilder result = new System.Text.StringBuilder();
        foreach (char character in value)
        {
            if (char.IsDigit(character) || character == '.')
            {
                result.Append(character);
            }
        }

        return result.Length > 0 ? result.ToString() : "0.0";
    }

    public void SetScoreboard(string timer, IReadOnlyList<VanguardScoreRowData> rows)
    {
        SetLabel("scoreboard-timer", timer.ToUpperInvariant());
        string signature = BuildRowsSignature(rows);
        if (signature != m_LastScoreSignature)
        {
            m_LastScoreSignature = signature;
            RebuildRows(m_ScoreRows, rows);
        }
    }

    public void SetResults(
        string title,
        string subtitle,
        string record,
        string placement,
        IReadOnlyList<VanguardScoreRowData> rows,
        bool canStartRound,
        string roundButtonText)
    {
        SetLabel("results-title", title.ToUpperInvariant());
        SetLabel("results-subtitle", subtitle.ToUpperInvariant());
        SetLabel("results-record", record.ToUpperInvariant());
        SetLabel("results-placement", placement.ToUpperInvariant());
        string signature = BuildRowsSignature(rows);
        if (signature != m_LastResultSignature)
        {
            m_LastResultSignature = signature;
            RebuildRows(m_ResultRows, rows);
        }
        if (m_NextRound != null)
        {
            m_NextRound.SetEnabled(canStartRound);
            m_NextRound.text = roundButtonText.ToUpperInvariant();
        }
    }

    static void RebuildRows(VisualElement container, IReadOnlyList<VanguardScoreRowData> rows)
    {
        if (container == null)
            return;

        container.Clear();
        for (int i = 0; i < rows.Count; i++)
        {
            VanguardScoreRowData data = rows[i];
            VisualElement row = new VisualElement();
            row.AddToClassList("score-row");
            row.EnableInClassList("score-row--local", data.IsLocal);
            AddCell(row, data.Rank, "score-row__rank");
            AddCell(row, data.Player, "score-row__player");
            AddCell(row, data.Kills, "score-row__stat");
            AddCell(row, data.Deaths, "score-row__stat");
            AddCell(row, data.Ratio, "score-row__kd");
            container.Add(row);
            VanguardToolkitRuntime.PrimeEntry(row, "juice-row", "juice-row--seed", i * 38);
        }
    }

    static string BuildRowsSignature(IReadOnlyList<VanguardScoreRowData> rows)
    {
        System.Text.StringBuilder signature = new System.Text.StringBuilder(rows.Count * 24);
        for (int i = 0; i < rows.Count; i++)
        {
            VanguardScoreRowData row = rows[i];
            signature.Append(row.Rank).Append('|')
                .Append(row.Player).Append('|')
                .Append(row.Kills).Append('|')
                .Append(row.Deaths).Append('|')
                .Append(row.Ratio).Append('|')
                .Append(row.IsLocal).Append(';');
        }
        return signature.ToString();
    }

    static void AddCell(VisualElement row, string text, string className)
    {
        ToolkitLabel label = new ToolkitLabel(text);
        label.AddToClassList("score-row__value");
        label.AddToClassList(className);
        row.Add(label);
    }

    public void SetKillFeed(IReadOnlyList<string> messages)
    {
        if (m_KillFeed == null)
            return;

        System.Text.StringBuilder signatureBuilder = new System.Text.StringBuilder();
        for (int i = 0; i < messages.Count; i++)
            signatureBuilder.Append(messages[i]).Append('\n');
        string signature = signatureBuilder.ToString();
        if (signature == m_LastKillFeedSignature)
            return;
        m_LastKillFeedSignature = signature;

        m_KillFeed.Clear();
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            ToolkitLabel row = new ToolkitLabel(messages[i]);
            row.AddToClassList("kill-feed__row");
            m_KillFeed.Add(row);
            VanguardToolkitRuntime.PrimeEntry(row, "juice-row", "juice-row--seed",
                (messages.Count - 1 - i) * 32);
        }
    }

    public void SetSettings(string sensitivity, string volume)
    {
        SetLabel("pause-sensitivity-value", sensitivity);
        SetLabel("pause-volume-value", volume);
    }

    public void Tick(float unscaledTime)
    {
        if (!IsReady || unscaledTime < m_NextAnimationTick)
            return;

        m_NextAnimationTick = unscaledTime + (1f / 20f);
        if (m_HudScanline != null)
        {
            float sweep = Mathf.Repeat(unscaledTime * 34f, 1180f) - 50f;
            m_HudScanline.style.top = sweep;
        }

        if (m_LowHealthVisible && m_LowHealth != null)
        {
            m_LowHealth.style.opacity = 0.55f + Mathf.PingPong(unscaledTime * 1.8f, 0.45f);
        }
        else if (m_LowHealth != null)
        {
            m_LowHealth.style.opacity = 1f;
        }
    }
}
