using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
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
    public int blurCanvasSortingOrder = -100;

    public static bool IsGamePaused { get; private set; }
    public static event Action<bool> PauseChanged;

    private bool paused;
    private float prevTimeScale = 1f;
    private float prevFixedDeltaTime = 0.02f;
    private bool prevAudioPaused;
    private Coroutine blurCaptureRoutine;
    private Canvas blurCanvas;
    private RawImage blurImage;
    private Image blurTintImage;
    private Texture2D blurTexture;

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            TogglePause();
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
        if (blurCaptureRoutine != null)
        {
            StopCoroutine(blurCaptureRoutine);
            blurCaptureRoutine = null;
        }

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
            ? CaptureWorldCameraAsTexture()
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

    private Texture2D CaptureWorldCameraAsTexture()
    {
        Camera sourceCamera = blurSourceCamera != null ? blurSourceCamera : Camera.main;
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
        if (blurCanvas != null && blurImage != null && blurTintImage != null)
            return;

        GameObject canvasObject = new GameObject(
            "Pause Blur Canvas",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        blurCanvas = canvasObject.GetComponent<Canvas>();
        blurCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        blurCanvas.sortingOrder = blurCanvasSortingOrder;

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

        canvasObject.SetActive(false);
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
        blurCanvas.sortingOrder = blurCanvasSortingOrder;
        blurCanvas.gameObject.SetActive(true);
    }

    private void HideBlurOverlay(bool destroyTexture)
    {
        if (blurCanvas != null)
            blurCanvas.gameObject.SetActive(false);

        if (blurImage != null)
            blurImage.texture = null;

        if (!destroyTexture || blurTexture == null)
            return;

        Destroy(blurTexture);
        blurTexture = null;
    }

    private static void StretchToParent(RectTransform rectTransform)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }

    private static Texture2D CreateBlurredTexture(
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
        if (!paused)
            return;

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
