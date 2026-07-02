using System;
using System.Collections;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PauseManager : MonoBehaviour
{
    [Header("Key")]
    public KeyCode toggleKey = KeyCode.Space;

    [Header("Options")]
    [Tooltip("暂停时是否也暂停音频（全局）")]
    public bool pauseAudio = true;
    public bool pauseDotweenTweens = true;

    [Header("Blur")]
    public bool showBlurOverlay = true;
    public bool captureWorldCameraOnly = true;
    public Camera blurSourceCamera;
    [Range(1, 8)]
    public int blurDownsample = 3;
    [Range(0, 12)]
    public int blurRadius = 5;
    [Range(1, 4)]
    public int blurIterations = 2;
    public Color blurTint = new Color(0f, 0f, 0f, 0.18f);
    public int blurCanvasSortingOrder = 5000;

    [Header("Pause Transition")]
    [Min(0f)]
    public float pauseFadeInDuration = 0.28f;
    [Min(0f)]
    public float pauseFadeOutDuration = 0.2f;
    public AnimationCurve pauseFadeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Pause Title")]
    public bool showPauseTitle = true;
    public string pauseTitle = "GAME PAUSED";
    public string pauseTitleChinese = "游戏暂停";
    public string pauseTitleEnglish = "GAME PAUSED";
    [Min(1f)]
    public float pauseTitleFontSize = 72f;
    public Color pauseTitleColor = Color.white;
    public Vector2 pauseTitleAnchoredPosition = Vector2.zero;

    [Header("Language")]
    public bool showLanguageToggle = true;
    public Vector2 languageToggleAnchoredPosition = new Vector2(0f, -150f);
    public Vector2 languageToggleSize = new Vector2(360f, 72f);
    public float languageToggleFontSize = 28f;
    public Color languageToggleBackgroundColor = new Color(1f, 1f, 1f, 0.16f);
    public Color languageToggleTextColor = Color.white;

    [Header("Pause Actions")]
    public bool showReturnToTitleButton = true;
    public bool showQuitButton = true;
    public Vector2 returnToTitleAnchoredPosition = new Vector2(0f, -238f);
    public Vector2 quitAnchoredPosition = new Vector2(0f, -320f);
    public Vector2 pauseActionButtonSize = new Vector2(360f, 64f);
    public float pauseActionFontSize = 26f;
    public Color pauseActionBackgroundColor = new Color(1f, 1f, 1f, 0.14f);
    public Color pauseActionTextColor = Color.white;
    public string returnToTitleChinese = "回到标题";
    public string returnToTitleEnglish = "Return to Title";
    public string quitChinese = "退出游戏";
    public string quitEnglish = "Quit Game";

    public static bool IsGamePaused { get; private set; }
    public static event Action<bool> PauseChanged;

    private bool paused;
    private float prevTimeScale = 1f;
    private float prevFixedDeltaTime = 0.02f;
    private bool prevAudioPaused;
    private Coroutine blurCaptureRoutine;
    private Canvas blurCanvas;
    private CanvasGroup blurCanvasGroup;
    private RawImage blurImage;
    private Image blurTintImage;
    private TextMeshProUGUI pauseTitleText;
    private Button languageToggleButton;
    private TextMeshProUGUI languageToggleText;
    private Button returnToTitleButton;
    private TextMeshProUGUI returnToTitleText;
    private Button quitButton;
    private TextMeshProUGUI quitText;
    private Texture2D blurTexture;
    private Coroutine blurFadeRoutine;
    private Coroutine resumeRoutine;
    private bool resumeInProgress;

    void Update()
    {
        if (DuoCurtain.RuntimeTileMesh.FusionSanityController.IsDeathActive)
            return;

        if (Input.GetKeyDown(toggleKey))
            TogglePause();
    }

    void OnEnable()
    {
        DuoCurtainLocalization.LanguageChanged += RefreshLocalizedPauseText;
    }

    public void TogglePause()
    {
        SetPaused(!paused);
    }

    public void SetPaused(bool shouldPause)
    {
        if (paused == shouldPause)
            return;

        if (shouldPause)
            Pause();
        else
            Resume();
    }

    public bool IsPaused()
    {
        return paused;
    }

    private void Pause()
    {
        prevTimeScale = Time.timeScale;
        prevFixedDeltaTime = Time.fixedDeltaTime;
        prevAudioPaused = AudioListener.pause;

        paused = true;
        IsGamePaused = true;

        Time.timeScale = 0f;
        if (pauseAudio)
            AudioListener.pause = true;

        if (pauseDotweenTweens)
            TrySetDotweenPaused(true);

        CaptureBlurOverlay();
        PauseChanged?.Invoke(true);
    }

    private void Resume()
    {
        if (resumeInProgress)
            return;

        if (blurCaptureRoutine != null)
        {
            StopCoroutine(blurCaptureRoutine);
            blurCaptureRoutine = null;
        }

        if (showBlurOverlay && blurCanvas != null && blurCanvas.gameObject.activeSelf && pauseFadeOutDuration > 0.0001f)
        {
            resumeInProgress = true;
            if (blurFadeRoutine != null)
            {
                StopCoroutine(blurFadeRoutine);
                blurFadeRoutine = null;
            }
            if (resumeRoutine != null)
                StopCoroutine(resumeRoutine);
            resumeRoutine = StartCoroutine(ResumeAfterFadeOut());
            return;
        }

        CompleteResume();
    }

    private IEnumerator ResumeAfterFadeOut()
    {
        yield return FadeBlurOverlay(blurCanvasGroup != null ? blurCanvasGroup.alpha : 1f, 0f, pauseFadeOutDuration);
        resumeRoutine = null;
        resumeInProgress = false;
        CompleteResume();
    }

    private void CompleteResume()
    {
        HideBlurOverlay(true);

        Time.timeScale = prevTimeScale <= 0f ? 1f : prevTimeScale;
        Time.fixedDeltaTime = prevFixedDeltaTime > 0f ? prevFixedDeltaTime : Time.fixedDeltaTime;

        if (pauseAudio)
            AudioListener.pause = prevAudioPaused;

        if (pauseDotweenTweens)
            TrySetDotweenPaused(false);

        paused = false;
        IsGamePaused = false;
        PauseChanged?.Invoke(false);
    }

    private void CaptureBlurOverlay()
    {
        if (!showBlurOverlay)
            return;

        EnsureBlurOverlay();
        if (blurCanvas != null)
            blurCanvas.gameObject.SetActive(false);

        if (blurCaptureRoutine != null)
            StopCoroutine(blurCaptureRoutine);

        blurCaptureRoutine = StartCoroutine(CaptureBlurOverlayAtEndOfFrame());
    }

    private IEnumerator CaptureBlurOverlayAtEndOfFrame()
    {
        yield return new WaitForEndOfFrame();
        blurCaptureRoutine = null;

        if (!paused || !showBlurOverlay)
            yield break;

        Texture2D screenshot = captureWorldCameraOnly
            ? CaptureCameraAsTexture(blurSourceCamera != null ? blurSourceCamera : Camera.main)
            : ScreenCapture.CaptureScreenshotAsTexture();
        if (screenshot == null)
            yield break;

        Texture2D blurred = CreateBlurredTexture(
            screenshot,
            Mathf.Max(1, blurDownsample),
            Mathf.Max(0, blurRadius),
            Mathf.Max(1, blurIterations));

        Destroy(screenshot);

        if (!paused || blurred == null)
        {
            if (blurred != null)
                Destroy(blurred);
            yield break;
        }

        SetBlurTexture(blurred);
    }

    public static Texture2D CaptureCameraAsTexture(Camera sourceCamera)
    {
        if (sourceCamera == null)
            return ScreenCapture.CaptureScreenshotAsTexture();

        int width = Mathf.Max(1, sourceCamera.pixelWidth > 0 ? sourceCamera.pixelWidth : Screen.width);
        int height = Mathf.Max(1, sourceCamera.pixelHeight > 0 ? sourceCamera.pixelHeight : Screen.height);
        RenderTexture previousTargetTexture = sourceCamera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);

        Texture2D texture = null;
        try
        {
            sourceCamera.targetTexture = renderTexture;
            sourceCamera.Render();

            RenderTexture.active = renderTexture;
            texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
            texture.Apply(false, false);
            return texture;
        }
        catch (Exception)
        {
            if (texture != null)
                Destroy(texture);

            return ScreenCapture.CaptureScreenshotAsTexture();
        }
        finally
        {
            sourceCamera.targetTexture = previousTargetTexture;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(renderTexture);
        }
    }

    private void EnsureBlurOverlay()
    {
        if (blurCanvas != null &&
            blurImage != null &&
            blurTintImage != null &&
            pauseTitleText != null &&
            (!showLanguageToggle || languageToggleButton != null) &&
            (!showReturnToTitleButton || returnToTitleButton != null) &&
            (!showQuitButton || quitButton != null))
            return;

        EnsureEventSystem();

        GameObject canvasObject = new GameObject(
            "Pause Blur Canvas",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(CanvasGroup),
            typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        blurCanvas = canvasObject.GetComponent<Canvas>();
        blurCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        blurCanvas.sortingOrder = blurCanvasSortingOrder;
        blurCanvasGroup = canvasObject.GetComponent<CanvasGroup>();
        blurCanvasGroup.alpha = 0f;
        blurCanvasGroup.interactable = true;
        blurCanvasGroup.blocksRaycasts = true;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject imageObject = new GameObject("Blurred Screen", typeof(RectTransform), typeof(RawImage));
        imageObject.transform.SetParent(canvasObject.transform, false);
        RectTransform imageRect = imageObject.GetComponent<RectTransform>();
        StretchToParent(imageRect);
        blurImage = imageObject.GetComponent<RawImage>();
        blurImage.raycastTarget = false;
        blurImage.color = Color.white;

        GameObject tintObject = new GameObject("Blur Tint", typeof(RectTransform), typeof(Image));
        tintObject.transform.SetParent(canvasObject.transform, false);
        RectTransform tintRect = tintObject.GetComponent<RectTransform>();
        StretchToParent(tintRect);
        blurTintImage = tintObject.GetComponent<Image>();
        blurTintImage.raycastTarget = false;
        blurTintImage.color = blurTint;

        GameObject titleObject = new GameObject("Pause Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        titleObject.transform.SetParent(canvasObject.transform, false);
        RectTransform titleRect = titleObject.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 0.5f);
        titleRect.anchorMax = new Vector2(0.5f, 0.5f);
        titleRect.pivot = new Vector2(0.5f, 0.5f);
        titleRect.anchoredPosition = pauseTitleAnchoredPosition;
        titleRect.sizeDelta = new Vector2(960f, 160f);
        pauseTitleText = titleObject.GetComponent<TextMeshProUGUI>();
        pauseTitleText.raycastTarget = false;
        pauseTitleText.alignment = TextAlignmentOptions.Center;
        pauseTitleText.fontSize = pauseTitleFontSize;
        pauseTitleText.color = pauseTitleColor;

        CreateLanguageToggle(canvasObject.transform);
        CreatePauseActionButtons(canvasObject.transform);
        RefreshLocalizedPauseText();

        canvasObject.SetActive(false);
    }

    private void CreateLanguageToggle(Transform parent)
    {
        GameObject buttonObject = new GameObject(
            "Language Toggle",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = languageToggleAnchoredPosition;
        rect.sizeDelta = languageToggleSize;

        Image image = buttonObject.GetComponent<Image>();
        image.color = languageToggleBackgroundColor;
        HeadingPointUiHoverEffect.Ensure(
            image,
            languageToggleBackgroundColor,
            new Color(
                Mathf.Lerp(languageToggleBackgroundColor.r, 1f, 0.55f),
                Mathf.Lerp(languageToggleBackgroundColor.g, 1f, 0.55f),
                Mathf.Lerp(languageToggleBackgroundColor.b, 1f, 0.55f),
                Mathf.Clamp01(languageToggleBackgroundColor.a + 0.18f)));

        languageToggleButton = buttonObject.GetComponent<Button>();
        languageToggleButton.onClick.AddListener(DuoCurtainLocalization.ToggleLanguage);

        GameObject textObject = new GameObject(
            "Language Toggle Text",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        textObject.transform.SetParent(buttonObject.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        StretchToParent(textRect);

        languageToggleText = textObject.GetComponent<TextMeshProUGUI>();
        languageToggleText.raycastTarget = false;
        languageToggleText.alignment = TextAlignmentOptions.Center;
    }

    private void CreatePauseActionButtons(Transform parent)
    {
        if (returnToTitleButton == null)
            returnToTitleButton = CreatePauseActionButton(
                parent,
                "Return To Title",
                returnToTitleAnchoredPosition,
                HandleReturnToTitlePressed,
                out returnToTitleText);

        if (quitButton == null)
            quitButton = CreatePauseActionButton(
                parent,
                "Quit Game",
                quitAnchoredPosition,
                HandleQuitPressed,
                out quitText);
    }

    private Button CreatePauseActionButton(
        Transform parent,
        string objectName,
        Vector2 anchoredPosition,
        UnityAction callback,
        out TextMeshProUGUI label)
    {
        GameObject buttonObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = pauseActionButtonSize;

        Image image = buttonObject.GetComponent<Image>();
        image.color = pauseActionBackgroundColor;
        HeadingPointUiHoverEffect.Ensure(
            image,
            pauseActionBackgroundColor,
            new Color(
                Mathf.Lerp(pauseActionBackgroundColor.r, 1f, 0.55f),
                Mathf.Lerp(pauseActionBackgroundColor.g, 1f, 0.55f),
                Mathf.Lerp(pauseActionBackgroundColor.b, 1f, 0.55f),
                Mathf.Clamp01(pauseActionBackgroundColor.a + 0.18f)));

        Button button = buttonObject.GetComponent<Button>();
        button.onClick.AddListener(callback);

        GameObject textObject = new GameObject(
            $"{objectName} Text",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        textObject.transform.SetParent(buttonObject.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        StretchToParent(textRect);

        label = textObject.GetComponent<TextMeshProUGUI>();
        label.raycastTarget = false;
        label.alignment = TextAlignmentOptions.Center;
        return button;
    }

    private void RefreshLocalizedPauseText()
    {
        if (pauseTitleText != null)
        {
            string title = DuoCurtainLocalization.Text("pause.title", pauseTitleChinese, pauseTitleEnglish);
            pauseTitleText.text = title;
            pauseTitleText.fontSize = pauseTitleFontSize;
            pauseTitleText.color = pauseTitleColor;
            DuoCurtainLocalization.ApplyFont(pauseTitleText, title);
        }

        if (languageToggleButton != null)
            languageToggleButton.gameObject.SetActive(showLanguageToggle);

        if (languageToggleText != null)
        {
            string text = DuoCurtainLocalization.Text(
                "pause.languageToggle",
                "语言：中文",
                "Language: English");
            languageToggleText.text = text;
            languageToggleText.fontSize = languageToggleFontSize;
            languageToggleText.color = languageToggleTextColor;
            DuoCurtainLocalization.ApplyFont(languageToggleText, text);
        }

        RefreshPauseActionButton(
            returnToTitleButton,
            returnToTitleText,
            showReturnToTitleButton,
            returnToTitleChinese,
            returnToTitleEnglish);
        RefreshPauseActionButton(
            quitButton,
            quitText,
            showQuitButton,
            quitChinese,
            quitEnglish);
    }

    private void RefreshPauseActionButton(
        Button button,
        TextMeshProUGUI label,
        bool visible,
        string chinese,
        string english)
    {
        if (button != null)
            button.gameObject.SetActive(visible);

        if (label == null)
            return;

        string text = DuoCurtainLocalization.Text("pause.action", chinese, english);
        label.text = text;
        label.fontSize = pauseActionFontSize;
        label.color = pauseActionTextColor;
        DuoCurtainLocalization.ApplyFont(label, text);

        Image image = button != null ? button.GetComponent<Image>() : null;
        if (image != null)
            image.color = pauseActionBackgroundColor;
    }

    private void HandleReturnToTitlePressed()
    {
        ForceResumeForExternalNavigation();
        BootWorldStateController bootWorld = BootWorldStateController.Active;
        if (bootWorld == null)
            bootWorld = FindFirstObjectByType<BootWorldStateController>();

        if (bootWorld != null)
        {
            bootWorld.SetListenForAnyInput(true);
            bootWorld.EnterBootWorld();
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid() && activeScene.buildIndex >= 0)
            SceneManager.LoadScene(activeScene.buildIndex);
        else if (activeScene.IsValid() && !string.IsNullOrEmpty(activeScene.name))
            SceneManager.LoadScene(activeScene.name);
    }

    private void HandleQuitPressed()
    {
        ForceResumeForExternalNavigation();
        Application.Quit();

        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #endif
    }

    private void ForceResumeForExternalNavigation()
    {
        if (blurCaptureRoutine != null)
        {
            StopCoroutine(blurCaptureRoutine);
            blurCaptureRoutine = null;
        }
        if (blurFadeRoutine != null)
        {
            StopCoroutine(blurFadeRoutine);
            blurFadeRoutine = null;
        }
        if (resumeRoutine != null)
        {
            StopCoroutine(resumeRoutine);
            resumeRoutine = null;
        }

        resumeInProgress = false;
        HideBlurOverlay(true);

        Time.timeScale = prevTimeScale <= 0f ? 1f : prevTimeScale;
        Time.fixedDeltaTime = prevFixedDeltaTime > 0f ? prevFixedDeltaTime : Time.fixedDeltaTime;
        if (pauseAudio)
            AudioListener.pause = prevAudioPaused;
        if (pauseDotweenTweens)
            TrySetDotweenPaused(false);

        bool wasPaused = paused || IsGamePaused;
        paused = false;
        IsGamePaused = false;
        if (wasPaused)
            PauseChanged?.Invoke(false);
    }

    private void SetBlurTexture(Texture2D texture)
    {
        EnsureBlurOverlay();
        HideBlurOverlay(true);

        blurTexture = texture;
        blurTexture.name = "Pause Gaussian Blur";
        blurTexture.wrapMode = TextureWrapMode.Clamp;
        blurTexture.filterMode = FilterMode.Bilinear;

        blurImage.texture = blurTexture;
        blurTintImage.color = blurTint;
        if (pauseTitleText != null)
        {
            pauseTitleText.gameObject.SetActive(showPauseTitle);
            pauseTitleText.fontSize = pauseTitleFontSize;
            pauseTitleText.color = pauseTitleColor;
            pauseTitleText.rectTransform.anchoredPosition = pauseTitleAnchoredPosition;
        }
        RefreshLocalizedPauseText();

        blurCanvas.sortingOrder = blurCanvasSortingOrder;
        if (blurCanvasGroup != null)
        {
            blurCanvasGroup.interactable = true;
            blurCanvasGroup.blocksRaycasts = true;
            blurCanvasGroup.alpha = pauseFadeInDuration > 0.0001f ? 0f : 1f;
        }
        blurCanvas.gameObject.SetActive(true);

        if (pauseFadeInDuration > 0.0001f)
        {
            if (blurFadeRoutine != null)
                StopCoroutine(blurFadeRoutine);
            blurFadeRoutine = StartCoroutine(FadeBlurOverlay(0f, 1f, pauseFadeInDuration));
        }
    }

    private void HideBlurOverlay(bool destroyTexture)
    {
        if (blurFadeRoutine != null)
        {
            StopCoroutine(blurFadeRoutine);
            blurFadeRoutine = null;
        }

        if (blurCanvas != null)
            blurCanvas.gameObject.SetActive(false);

        if (blurCanvasGroup != null)
        {
            blurCanvasGroup.interactable = false;
            blurCanvasGroup.blocksRaycasts = false;
        }

        if (blurImage != null)
            blurImage.texture = null;

        if (!destroyTexture || blurTexture == null)
            return;

        Destroy(blurTexture);
        blurTexture = null;
    }

    private IEnumerator FadeBlurOverlay(float from, float to, float duration)
    {
        EnsureBlurOverlay();
        if (blurCanvasGroup == null)
            yield break;

        if (to > 0f)
        {
            blurCanvasGroup.interactable = true;
            blurCanvasGroup.blocksRaycasts = true;
        }

        if (duration <= 0.0001f)
        {
            blurCanvasGroup.alpha = to;
            blurFadeRoutine = null;
            yield break;
        }

        float start = Time.unscaledTime;
        while (true)
        {
            float normalized = Mathf.Clamp01((Time.unscaledTime - start) / duration);
            float eased = pauseFadeCurve != null ? pauseFadeCurve.Evaluate(normalized) : normalized;
            blurCanvasGroup.alpha = Mathf.LerpUnclamped(from, to, eased);
            if (normalized >= 1f)
                break;
            yield return null;
        }

        blurCanvasGroup.alpha = to;
        if (Mathf.Approximately(to, 0f))
        {
            blurCanvasGroup.interactable = false;
            blurCanvasGroup.blocksRaycasts = false;
        }
        blurFadeRoutine = null;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindFirstObjectByType<EventSystem>() != null)
            return;

        new GameObject(
            "EventSystem",
            typeof(EventSystem),
            typeof(StandaloneInputModule));
    }

    private static void StretchToParent(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }

    public static Texture2D CreateBlurredTexture(
        Texture2D source,
        int downsample,
        int radius,
        int iterations)
    {
        int targetWidth = Mathf.Max(1, source.width / Mathf.Max(1, downsample));
        int targetHeight = Mathf.Max(1, source.height / Mathf.Max(1, downsample));
        Color[] pixels = Downsample(source.GetPixels32(), source.width, source.height, targetWidth, targetHeight);

        if (radius > 0)
        {
            float[] kernel = BuildGaussianKernel(radius);
            Color[] temp = new Color[pixels.Length];
            for (int i = 0; i < iterations; i++)
            {
                BlurHorizontal(pixels, temp, targetWidth, targetHeight, kernel, radius);
                BlurVertical(temp, pixels, targetWidth, targetHeight, kernel, radius);
            }
        }

        Texture2D texture = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return texture;
    }

    private static Color[] Downsample(
        Color32[] source,
        int sourceWidth,
        int sourceHeight,
        int targetWidth,
        int targetHeight)
    {
        Color[] result = new Color[targetWidth * targetHeight];
        float scaleX = sourceWidth / (float)targetWidth;
        float scaleY = sourceHeight / (float)targetHeight;

        for (int y = 0; y < targetHeight; y++)
        {
            int yStart = Mathf.Clamp(Mathf.FloorToInt(y * scaleY), 0, sourceHeight - 1);
            int yEnd = Mathf.Clamp(Mathf.CeilToInt((y + 1) * scaleY), yStart + 1, sourceHeight);
            for (int x = 0; x < targetWidth; x++)
            {
                int xStart = Mathf.Clamp(Mathf.FloorToInt(x * scaleX), 0, sourceWidth - 1);
                int xEnd = Mathf.Clamp(Mathf.CeilToInt((x + 1) * scaleX), xStart + 1, sourceWidth);
                Color sum = Color.clear;
                int count = 0;

                for (int sampleY = yStart; sampleY < yEnd; sampleY++)
                {
                    int row = sampleY * sourceWidth;
                    for (int sampleX = xStart; sampleX < xEnd; sampleX++)
                    {
                        sum += source[row + sampleX];
                        count++;
                    }
                }

                result[y * targetWidth + x] = count > 0 ? sum / count : Color.clear;
            }
        }

        return result;
    }

    private static float[] BuildGaussianKernel(int radius)
    {
        int size = radius * 2 + 1;
        float[] kernel = new float[size];
        float sigma = Mathf.Max(0.1f, radius * 0.5f);
        float twoSigmaSquare = 2f * sigma * sigma;
        float sum = 0f;

        for (int i = 0; i < size; i++)
        {
            int offset = i - radius;
            float value = Mathf.Exp(-(offset * offset) / twoSigmaSquare);
            kernel[i] = value;
            sum += value;
        }

        for (int i = 0; i < size; i++)
            kernel[i] /= sum;

        return kernel;
    }

    private static void BlurHorizontal(
        Color[] source,
        Color[] target,
        int width,
        int height,
        float[] kernel,
        int radius)
    {
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                Color sum = Color.clear;
                for (int k = -radius; k <= radius; k++)
                {
                    int sampleX = Mathf.Clamp(x + k, 0, width - 1);
                    sum += source[row + sampleX] * kernel[k + radius];
                }

                target[row + x] = sum;
            }
        }
    }

    private static void BlurVertical(
        Color[] source,
        Color[] target,
        int width,
        int height,
        float[] kernel,
        int radius)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color sum = Color.clear;
                for (int k = -radius; k <= radius; k++)
                {
                    int sampleY = Mathf.Clamp(y + k, 0, height - 1);
                    sum += source[sampleY * width + x] * kernel[k + radius];
                }

                target[y * width + x] = sum;
            }
        }
    }

    private static void TrySetDotweenPaused(bool shouldPause)
    {
        Type dotweenType = FindType("DG.Tweening.DOTween");
        if (dotweenType == null)
            return;

        string methodName = shouldPause ? "PauseAll" : "PlayAll";
        MethodInfo method = dotweenType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
        if (method == null)
            return;

        method.Invoke(null, null);
    }

    private static Type FindType(string typeName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type type = assemblies[i].GetType(typeName);
            if (type != null)
                return type;
        }

        return null;
    }

    void OnDisable()
    {
        DuoCurtainLocalization.LanguageChanged -= RefreshLocalizedPauseText;

        if (!paused)
            return;

        if (blurCaptureRoutine != null)
        {
            StopCoroutine(blurCaptureRoutine);
            blurCaptureRoutine = null;
        }
        if (blurFadeRoutine != null)
        {
            StopCoroutine(blurFadeRoutine);
            blurFadeRoutine = null;
        }
        if (resumeRoutine != null)
        {
            StopCoroutine(resumeRoutine);
            resumeRoutine = null;
        }
        resumeInProgress = false;
        HideBlurOverlay(true);
        Time.timeScale = prevTimeScale <= 0f ? 1f : prevTimeScale;
        Time.fixedDeltaTime = prevFixedDeltaTime > 0f ? prevFixedDeltaTime : Time.fixedDeltaTime;
        if (pauseAudio)
            AudioListener.pause = prevAudioPaused;
        if (pauseDotweenTweens)
            TrySetDotweenPaused(false);

        paused = false;
        IsGamePaused = false;
        PauseChanged?.Invoke(false);
    }
}
