using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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
    Image m_Image;
    Vector2 m_BasePosition;
    Vector3 m_BaseScale;
    float m_BaseRotation;
    float m_BaseAlpha;

    void Awake()
    {
        m_RectTransform = GetComponent<RectTransform>();
        m_Image = GetComponent<Image>();
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
            m_Image = GetComponent<Image>();
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

[RequireComponent(typeof(Button), typeof(RectTransform))]
public class MenuButtonAnimator : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public float HoverScale = 1.03f;
    public float PressScale = 0.98f;
    public float LerpSpeed = 12f;

    RectTransform m_RectTransform;
    Image m_Image;
    Vector3 m_BaseScale;
    Color m_BaseColor;
    float m_TargetScale = 1f;
    float m_ColorBoost;

    void Awake()
    {
        m_RectTransform = GetComponent<RectTransform>();
        m_Image = GetComponent<Image>();
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
