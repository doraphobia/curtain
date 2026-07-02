using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public sealed class BootWorldSettingsPanel : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private int sortingOrder = 2600;
    [SerializeField] private Color panelColor = new Color(0f, 0f, 0f, 0.46f);
    [SerializeField] private Color buttonColor = new Color(0f, 0f, 0f, 0.32f);
    [SerializeField] private Color textColor = Color.white;

    private CanvasGroup canvasGroup;
    private RectTransform panelRect;

    private TextMeshProUGUI titleLabel;
    private TextMeshProUGUI volumeLabel;
    private Slider volumeSlider;
    private TextMeshProUGUI volumeValueLabel;

    private TextMeshProUGUI resolutionLabel;
    private Button resolutionButton;
    private TextMeshProUGUI resolutionValueLabel;

    private TextMeshProUGUI fullscreenLabel;
    private Button fullscreenButton;
    private TextMeshProUGUI fullscreenValueLabel;

    private Button resetButton;
    private TextMeshProUGUI resetLabel;
    private Button backButton;
    private TextMeshProUGUI backLabel;

    private IReadOnlyList<Vector2Int> resolutionOptions = Array.Empty<Vector2Int>();
    private int resolutionIndex;

    private bool initialized;
    private bool suppressEvents;

    public void Initialize()
    {
        if (initialized)
            return;

        initialized = true;
        EnsureEventSystem();
        CreateUi();

        DuoCurtainLocalization.LanguageChanged += RefreshLocalizedText;
        if (DuoCurtainSettingsManager.Instance != null)
            DuoCurtainSettingsManager.Instance.SettingsChanged += RefreshValues;

        RefreshOptions();
        RefreshValues();
        RefreshLocalizedText();
    }

    private void OnDestroy()
    {
        DuoCurtainLocalization.LanguageChanged -= RefreshLocalizedText;
        if (DuoCurtainSettingsManager.Instance != null)
            DuoCurtainSettingsManager.Instance.SettingsChanged -= RefreshValues;
    }

    public void Show(bool visible)
    {
        if (!initialized)
            Initialize();

        if (canvasGroup == null)
            return;

        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
        gameObject.SetActive(visible);
    }

    public bool IsVisible => canvasGroup != null && canvasGroup.alpha > 0.5f && canvasGroup.blocksRaycasts;

    private void RefreshOptions()
    {
        DuoCurtainSettingsManager settings = DuoCurtainSettingsManager.Instance;
        if (settings == null)
        {
            resolutionOptions = Array.Empty<Vector2Int>();
            resolutionIndex = 0;
            return;
        }

        resolutionOptions = settings.GetSupportedCommonResolutions();

        int w = settings.Current.resolutionWidth > 0 ? settings.Current.resolutionWidth : Screen.width;
        int h = settings.Current.resolutionHeight > 0 ? settings.Current.resolutionHeight : Screen.height;
        resolutionIndex = FindResolutionIndex(w, h);
    }

    private void RefreshValues()
    {
        DuoCurtainSettingsManager settings = DuoCurtainSettingsManager.Instance;
        if (settings == null)
            return;

        suppressEvents = true;
        try
        {
            if (volumeSlider != null)
                volumeSlider.value = Mathf.Clamp01(settings.Current.masterVolume) * 100f;

            RefreshOptions();
            RefreshResolutionText();
            RefreshFullscreenText();
            RefreshVolumeValueText();
        }
        finally
        {
            suppressEvents = false;
        }
    }

    private void RefreshLocalizedText()
    {
        if (titleLabel != null)
            SetLabel(titleLabel, DuoCurtainLocalization.Text("settings.title", "设置", "Settings"));
        if (volumeLabel != null)
            SetLabel(volumeLabel, DuoCurtainLocalization.Text("settings.volume", "音量", "Volume"));
        if (resolutionLabel != null)
            SetLabel(resolutionLabel, DuoCurtainLocalization.Text("settings.resolution", "分辨率", "Resolution"));
        if (fullscreenLabel != null)
            SetLabel(fullscreenLabel, DuoCurtainLocalization.Text("settings.displayMode", "显示模式", "Display Mode"));
        if (resetLabel != null)
            SetLabel(resetLabel, DuoCurtainLocalization.Text("settings.reset", "恢复默认", "Reset"));
        if (backLabel != null)
            SetLabel(backLabel, DuoCurtainLocalization.Text("settings.back", "返回", "Back"));

        RefreshResolutionText();
        RefreshFullscreenText();
        RefreshVolumeValueText();
    }

    private void RefreshVolumeValueText()
    {
        if (volumeValueLabel == null || volumeSlider == null)
            return;

        int pct = Mathf.RoundToInt(volumeSlider.value);
        SetLabel(volumeValueLabel, pct + "%");
    }

    private void RefreshResolutionText()
    {
        if (resolutionValueLabel == null)
            return;

        Vector2Int res = GetCurrentResolutionOption();
        SetLabel(resolutionValueLabel, $"{res.x} × {res.y}");
    }

    private void RefreshFullscreenText()
    {
        if (fullscreenValueLabel == null)
            return;

        DuoCurtainSettingsManager settings = DuoCurtainSettingsManager.Instance;
        FullScreenMode mode = settings != null ? settings.Current.fullScreenMode : Screen.fullScreenMode;

        string label = mode == FullScreenMode.Windowed
            ? DuoCurtainLocalization.Text("settings.windowed", "窗口化", "Windowed")
            : DuoCurtainLocalization.Text("settings.fullscreen", "全屏", "Fullscreen");
        SetLabel(fullscreenValueLabel, label);
    }

    private Vector2Int GetCurrentResolutionOption()
    {
        if (resolutionOptions == null || resolutionOptions.Count == 0)
            return new Vector2Int(Screen.width, Screen.height);

        resolutionIndex = Mathf.Clamp(resolutionIndex, 0, resolutionOptions.Count - 1);
        return resolutionOptions[resolutionIndex];
    }

    private int FindResolutionIndex(int width, int height)
    {
        if (resolutionOptions == null || resolutionOptions.Count == 0)
            return 0;

        for (int i = 0; i < resolutionOptions.Count; i++)
        {
            if (resolutionOptions[i].x == width && resolutionOptions[i].y == height)
                return i;
        }

        return 0;
    }

    private void HandleVolumeChanged(float value)
    {
        if (suppressEvents)
            return;

        RefreshVolumeValueText();

        DuoCurtainSettingsManager settings = DuoCurtainSettingsManager.Instance;
        if (settings != null)
            settings.SetMasterVolume01(Mathf.Clamp01(value / 100f));
    }

    private void HandleResolutionPressed()
    {
        DuoCurtainSettingsManager settings = DuoCurtainSettingsManager.Instance;
        if (settings == null)
            return;

        if (resolutionOptions == null || resolutionOptions.Count == 0)
            RefreshOptions();

        if (resolutionOptions == null || resolutionOptions.Count == 0)
            return;

        resolutionIndex = (resolutionIndex + 1) % resolutionOptions.Count;
        Vector2Int res = GetCurrentResolutionOption();
        settings.SetResolution(res.x, res.y);
        RefreshResolutionText();
    }

    private void HandleFullscreenPressed()
    {
        DuoCurtainSettingsManager settings = DuoCurtainSettingsManager.Instance;
        if (settings == null)
            return;

        FullScreenMode current = settings.Current.fullScreenMode;
        FullScreenMode next = current == FullScreenMode.Windowed ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        settings.SetFullscreenMode(next);
        RefreshFullscreenText();
    }

    private void HandleResetPressed()
    {
        DuoCurtainSettingsManager settings = DuoCurtainSettingsManager.Instance;
        if (settings == null)
            return;

        settings.ResetToDefaults();
        RefreshValues();
    }

    private void HandleBackPressed()
    {
        Show(false);
    }

    private void CreateUi()
    {
        GameObject canvasObject = new GameObject(
            "Boot World Settings Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(CanvasGroup));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGroup = canvasObject.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.anchorMin = Vector2.zero;
        canvasRect.anchorMax = Vector2.one;
        canvasRect.offsetMin = Vector2.zero;
        canvasRect.offsetMax = Vector2.zero;

        GameObject panelObject = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelObject.transform.SetParent(canvasRect, false);
        panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(860f, 520f);
        panelRect.anchoredPosition = new Vector2(0f, -10f);

        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = panelColor;

        titleLabel = CreateText(panelRect, "Title", "Settings", 32f, new Vector2(0f, 210f), new Vector2(760f, 64f), TextAlignmentOptions.Center);

        volumeLabel = CreateText(panelRect, "Volume Label", "Volume", 22f, new Vector2(-310f, 120f), new Vector2(220f, 44f), TextAlignmentOptions.Left);
        volumeSlider = CreateSlider(panelRect, "Volume Slider", new Vector2(70f, 120f), new Vector2(420f, 32f), 0f, 100f);
        volumeSlider.onValueChanged.AddListener(HandleVolumeChanged);
        volumeValueLabel = CreateText(panelRect, "Volume Value", "80%", 22f, new Vector2(360f, 120f), new Vector2(120f, 44f), TextAlignmentOptions.Right);

        resolutionLabel = CreateText(panelRect, "Resolution Label", "Resolution", 22f, new Vector2(-310f, 48f), new Vector2(220f, 44f), TextAlignmentOptions.Left);
        resolutionButton = CreateButton(panelRect, "Resolution Button", new Vector2(140f, 48f), new Vector2(560f, 48f), HandleResolutionPressed);
        resolutionValueLabel = CreateText(resolutionButton.GetComponent<RectTransform>(), "Resolution Value", "1920 × 1080", 22f, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
        StretchToParent(resolutionValueLabel.rectTransform);

        fullscreenLabel = CreateText(panelRect, "Fullscreen Label", "Display Mode", 22f, new Vector2(-310f, -20f), new Vector2(220f, 44f), TextAlignmentOptions.Left);
        fullscreenButton = CreateButton(panelRect, "Fullscreen Button", new Vector2(140f, -20f), new Vector2(560f, 48f), HandleFullscreenPressed);
        fullscreenValueLabel = CreateText(fullscreenButton.GetComponent<RectTransform>(), "Fullscreen Value", "Fullscreen", 22f, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
        StretchToParent(fullscreenValueLabel.rectTransform);

        resetButton = CreateButton(panelRect, "Reset Button", new Vector2(-170f, -196f), new Vector2(240f, 54f), HandleResetPressed);
        resetLabel = CreateText(resetButton.GetComponent<RectTransform>(), "Reset Label", "Reset", 22f, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
        StretchToParent(resetLabel.rectTransform);

        backButton = CreateButton(panelRect, "Back Button", new Vector2(170f, -196f), new Vector2(240f, 54f), HandleBackPressed);
        backLabel = CreateText(backButton.GetComponent<RectTransform>(), "Back Label", "Back", 22f, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
        StretchToParent(backLabel.rectTransform);
    }

    private TextMeshProUGUI CreateText(
        Transform parent,
        string name,
        string text,
        float fontSize,
        Vector2 anchoredPosition,
        Vector2 size,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        if (size != Vector2.zero)
            rect.sizeDelta = size;

        TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = textColor;
        label.raycastTarget = false;
        DuoCurtainLocalization.ApplyFont(label, text);
        return label;
    }

    private Button CreateButton(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, UnityAction callback)
    {
        GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Image image = buttonObject.GetComponent<Image>();
        image.color = buttonColor;

        Button button = buttonObject.GetComponent<Button>();
        button.onClick.AddListener(callback);
        return button;
    }

    private Slider CreateSlider(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, float min, float max)
    {
        GameObject sliderObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Slider));
        sliderObject.transform.SetParent(parent, false);

        RectTransform rect = sliderObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        GameObject bg = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        bg.transform.SetParent(sliderObject.transform, false);
        Image bgImage = bg.GetComponent<Image>();
        bgImage.color = new Color(1f, 1f, 1f, 0.12f);
        RectTransform bgRect = bg.GetComponent<RectTransform>();
        StretchToParent(bgRect);

        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderObject.transform, false);
        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0f, 0f);
        fillAreaRect.anchorMax = new Vector2(1f, 1f);
        fillAreaRect.offsetMin = new Vector2(6f, 6f);
        fillAreaRect.offsetMax = new Vector2(-6f, -6f);

        GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        Image fillImage = fill.GetComponent<Image>();
        fillImage.color = new Color(1f, 1f, 1f, 0.42f);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        StretchToParent(fillRect);

        GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        handle.transform.SetParent(sliderObject.transform, false);
        Image handleImage = handle.GetComponent<Image>();
        handleImage.color = new Color(1f, 1f, 1f, 0.72f);
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(18f, 44f);

        Slider slider = sliderObject.GetComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = true;
        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.targetGraphic = handleImage;
        slider.direction = Slider.Direction.LeftToRight;

        return slider;
    }

    private static void StretchToParent(RectTransform rect)
    {
        if (rect == null)
            return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void EnsureEventSystem()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null || FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null)
            return;

        new GameObject(
            "EventSystem",
            typeof(UnityEngine.EventSystems.EventSystem),
            typeof(UnityEngine.EventSystems.StandaloneInputModule));
    }

    private static void SetLabel(TextMeshProUGUI label, string text)
    {
        if (label == null)
            return;

        label.text = text;
        DuoCurtainLocalization.ApplyFont(label, text);
    }
}

