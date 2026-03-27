using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class SanitySystem : MonoBehaviour
{
    [Header("References")]
    public StageCycleController stageController;
    public HoverScrollColorLerp2D[] watchedObjects;
    public Renderer[] warningRenderers;
    public GameObject alternateWarningPanel;
    public TimeCounterUI currencySource;
    public NightWindowEventController nightEventController;
    public TextMeshProUGUI sanityText;
    public Slider sanitySlider;

    [Header("Sanity")]
    public float maxSanity = 20f;
    public float startSanity = 20f;
    public float losePerSecondAtNight = 1f;

    [Header("Warning")]
    [Tooltip("sanity 下降时，一秒内完成一次开关循环")]
    public float warningBlinkInterval = 1f;

    [Header("Fail State")]
    public string sceneToLoadWhenDepleted;

    private float currentSanity;
    private bool hasTriggeredSceneLoad;

    public float CurrentSanity => currentSanity;

    void Start()
    {
        currentSanity = Mathf.Clamp(startSanity, 0f, maxSanity);

        RefreshUI();
        SetWarningRenderersEnabled(true);
        SetAlternateWarningVisible(false);
    }

    void Update()
    {
        if (hasTriggeredSceneLoad)
            return;

        bool shouldLoseSanity = ShouldLoseSanityAtNight();

        if (shouldLoseSanity)
        {
            currentSanity = Mathf.Max(0f, currentSanity - losePerSecondAtNight * Time.deltaTime);
            SetWarningRenderersEnabled(true);
            UpdateAlternateWarningBlink();
        }
        else
        {
            SetWarningRenderersEnabled(true);
            SetAlternateWarningVisible(false);
        }

        RefreshUI();
        HandleSanityDepleted();
    }

    bool ShouldLoseSanityAtNight()
    {
        if (stageController == null)
            return false;

        if (stageController.CurrentStageId != "Night")
            return false;

        if (nightEventController != null && nightEventController.HasEventTonight)
            return false;

        if (watchedObjects == null || watchedObjects.Length == 0)
            return false;

        for (int i = 0; i < watchedObjects.Length; i++)
        {
            HoverScrollColorLerp2D watchedObject = watchedObjects[i];
            if (watchedObject != null && !watchedObject.IsAtColorA)
                return true;
        }

        return false;
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

    void UpdateAlternateWarningBlink()
    {
        float interval = Mathf.Max(0.01f, warningBlinkInterval);
        bool visible = Mathf.Repeat(Time.time, interval) < interval * 0.5f;
        SetAlternateWarningVisible(visible);
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

        if (currencySource != null && currencySource.CurrentValue >= 1f)
        {
            float currencyPenalty = currencySource.CurrentValue * 0.5f;
            currencySource.AddValue(-currencyPenalty);
            currentSanity = Mathf.Clamp(startSanity, 0f, maxSanity);
            RefreshUI();
            SetAlternateWarningVisible(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(sceneToLoadWhenDepleted))
            return;

        hasTriggeredSceneLoad = true;
        SceneManager.LoadScene(sceneToLoadWhenDepleted);
    }

    public void AddSanity(float amount)
    {
        currentSanity = Mathf.Clamp(currentSanity + amount, 0f, maxSanity);
        RefreshUI();
    }

    public void ApplyHalfSanityPenalty()
    {
        currentSanity = Mathf.Clamp(currentSanity * 0.5f, 0f, maxSanity);
        RefreshUI();
    }
}
