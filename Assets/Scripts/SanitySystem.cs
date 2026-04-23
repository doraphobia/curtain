using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class SanitySystem : MonoBehaviour
{
    [Header("References")]
    public Renderer[] warningRenderers;
    public GameObject alternateWarningPanel;
    public TimeCounterUI currencySource;
    public NightWindowEventController nightEventController;
    public TextMeshProUGUI sanityText;
    public Slider sanitySlider;

    [Header("Sanity")]
    public float maxSanity = 20f;
    public float startSanity = 20f;

    [Header("Slider Blink")]
    [Min(0.01f)]
    public float sliderBlinkSpeed = 6f;
    [Range(0f, 1f)]
    public float sliderBlinkMinAlpha = 0.35f;
    [Range(0f, 1f)]
    public float sliderBlinkMaxAlpha = 1f;

    [Header("Fail State")]
    public string sceneToLoadWhenDepleted;

    private float currentSanity;
    private bool hasTriggeredSceneLoad;
    private Graphic[] sanitySliderGraphics = System.Array.Empty<Graphic>();
    private bool sliderShouldBlinkThisFrame;

    public float CurrentSanity => currentSanity;

    void Start()
    {
        currentSanity = Mathf.Clamp(startSanity, 0f, maxSanity);

        RefreshUI();
        SetWarningRenderersEnabled(true);
        SetAlternateWarningVisible(false);
        CacheSliderGraphics();
        SetSliderBlinkAlpha(1f);
    }

    void Update()
    {
        if (hasTriggeredSceneLoad)
            return;

        RefreshUI();
        UpdateSliderBlink();
        HandleSanityDepleted();
        sliderShouldBlinkThisFrame = false;
    }

    void RefreshUI()
    {
        if (sanityText != null)
            sanityText.text = Mathf.CeilToInt(currentSanity).ToString();

        if (sanitySlider != null)
        {
            sanitySlider.minValue = 0f;
            sanitySlider.maxValue = maxSanity;
            sanitySlider.value = currentSanity;
        }
    }

    void SetWarningRenderersEnabled(bool enabled)
    {
        if (warningRenderers == null || warningRenderers.Length == 0)
            return;

        for (int i = 0; i < warningRenderers.Length; i++)
        {
            Renderer target = warningRenderers[i];
            if (target == null)
                continue;

            target.enabled = enabled;
        }
    }

    void SetAlternateWarningVisible(bool visible)
    {
        if (alternateWarningPanel == null)
            return;

        alternateWarningPanel.SetActive(visible);
    }

    void HandleSanityDepleted()
    {
        if (hasTriggeredSceneLoad)
            return;

        if (currentSanity > 0f)
            return;

        if (string.IsNullOrWhiteSpace(sceneToLoadWhenDepleted))
            return;

        hasTriggeredSceneLoad = true;
        SceneManager.LoadScene(sceneToLoadWhenDepleted);
    }

    public void AddSanity(float amount)
    {
        currentSanity = Mathf.Clamp(currentSanity + amount, 0f, maxSanity);

        if (amount < 0f)
            sliderShouldBlinkThisFrame = true;

        RefreshUI();
    }

    public void ApplyHalfSanityPenalty()
    {
        currentSanity = Mathf.Clamp(currentSanity * 0.5f, 0f, maxSanity);
        sliderShouldBlinkThisFrame = true;
        RefreshUI();
    }

    public void DrainSanity(float amount)
    {
        if (amount <= 0f)
            return;

        AddSanity(-amount);
    }

    void CacheSliderGraphics()
    {
        if (sanitySlider == null)
            return;

        sanitySliderGraphics = sanitySlider.GetComponentsInChildren<Graphic>(true);
    }

    void UpdateSliderBlink()
    {
        if (sanitySlider == null)
            return;

        if (sanitySliderGraphics == null || sanitySliderGraphics.Length == 0)
            CacheSliderGraphics();

        if (!sliderShouldBlinkThisFrame)
        {
            SetSliderBlinkAlpha(1f);
            return;
        }

        float t = (Mathf.Sin(Time.unscaledTime * sliderBlinkSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
        SetSliderBlinkAlpha(Mathf.Lerp(sliderBlinkMinAlpha, sliderBlinkMaxAlpha, t));
    }

    void SetSliderBlinkAlpha(float alpha)
    {
        if (sanitySliderGraphics == null)
            return;

        for (int i = 0; i < sanitySliderGraphics.Length; i++)
        {
            Graphic graphic = sanitySliderGraphics[i];
            if (graphic == null)
                continue;

            Color color = graphic.color;
            color.a = alpha;
            graphic.color = color;
        }
    }
}
