using System.Collections;
using TMPro;
#if UNITY_EDITOR
using Curtain.Settings;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DuoCurtain.RuntimeTileMesh
{
    [DefaultExecutionOrder(-34)]
    [DisallowMultipleComponent]
    public sealed class FusionSanityController : MonoBehaviour
    {
        public static FusionSanityController Active { get; private set; }
        public static bool IsDeathActive => Active != null && Active.deathActive;

        [Header("References")]
        public PlayerControl playerControl;
        public RuntimeTileMeshFusionSandbox fusionSandbox;
        public StageCycleController stageController;
        public FusionGameModeController gameModeController;
        public TimeCounterUI currencySource;
        public Camera blurSourceCamera;

        [Header("Sanity")]
        [Min(1f)]
        public float maxSanity = 100f;
        [Min(0f)]
        public float startSanity = 100f;
        [Min(0f)]
        public float nightOutdoorDrainPerSecond = 4f;
        [Min(0f)]
        public float nightIndoorRecoveryPerSecond = 1.25f;
        [Min(0f)]
        public float dayIndoorRecoveryPerSecond = 2f;
        [Min(0f)]
        public float dayOutdoorRecoveryPerSecond = 3.5f;
        [Min(0f)]
        public float enemyTouchDamage = 10f;
        [Min(0f)]
        public float windowDetectionDamage = 8f;

        [Header("Management Restore")]
        public bool enableManagementRestore = true;
        [Min(0)]
        public int restoreCost = 100;
        [Min(0f)]
        public float restoreAmount = 20f;
        public Vector2 restoreButtonAnchoredPosition = new Vector2(32f, 24f);
        public Vector2 restoreButtonSize = new Vector2(260f, 48f);
        public Color restoreButtonColor = new Color(1f, 1f, 1f, 0.16f);
        public Color restoreTextColor = Color.white;
        public bool liftRestoreUiAboveShop = true;
        [Min(0f)]
        public float shopUiClearance = 18f;
        public bool forceRestoreButtonAboveShopInManagement = true;
        public bool keepRestoreButtonClearOfSanityHud = true;
        [Min(0f)]
        public float restoreButtonManagementExtraLift = 72f;
        [Min(0f)]
        public float restoreButtonSanityHudGap = 14f;

        [Header("HUD")]
        public bool createHudIfMissing = true;
        public TextMeshProUGUI sanityText;
        public Slider sanitySlider;
        public Vector2 sanityHudAnchoredPosition = new Vector2(32f, 96f);
        public Vector2 sanityHudSize = new Vector2(360f, 56f);
        public Color sanityTextColor = Color.white;

        [Header("Low Sanity Screen")]
        public bool showGreyOverlay = true;
        [Range(0f, 1f)]
        public float greyOverlayMaxAlpha = 0.48f;
        public int greyOverlaySortingOrder = 4600;

        [Header("Death")]
        public bool freezeOnDeath = true;
        public Color deathTint = new Color(0.8f, 0f, 0f, 0.42f);
        public string deathTitleChinese = "你死了";
        public string deathTitleEnglish = "YOU DIED";
        public string deathRestartChinese = "重新开始";
        public string deathRestartEnglish = "RESTART";
        public float deathTitleFontSize = 88f;
        public float deathFadeDuration = 0.75f;
        [Range(1, 8)]
        public int deathBlurDownsample = 3;
        [Range(0, 12)]
        public int deathBlurRadius = 6;
        [Range(1, 4)]
        public int deathBlurIterations = 2;

        private float currentSanity;
        private bool initialized;
        private bool deathActive;
        private float previousTimeScale = 1f;
        private float previousFixedDeltaTime = 0.02f;
        private bool previousAudioPaused;
        private Canvas hudCanvas;
        private Canvas overlayCanvas;
        private Canvas deathCanvas;
        private Image greyOverlayImage;
        private CanvasGroup deathCanvasGroup;
        private RawImage deathBlurImage;
        private Image deathTintImage;
        private TextMeshProUGUI deathTitleText;
        private TextMeshProUGUI deathRestartText;
        private Button deathRestartButton;
        private Button restoreButton;
        private TextMeshProUGUI restoreButtonText;
        private RectTransform sanityTextRectTransform;
        private RectTransform restoreButtonRectTransform;
        private Texture2D deathBlurTexture;
        private Coroutine deathOverlayRoutine;

        public float CurrentSanity => currentSanity;
        public float NormalizedSanity => maxSanity > 0f ? Mathf.Clamp01(currentSanity / maxSanity) : 0f;
        public bool IsDead => deathActive;

        void Awake()
        {
            if (Active != null && Active != this)
            {
                Destroy(gameObject);
                return;
            }

            Active = this;
            ApplySettingsIfPresent();
            currentSanity = Mathf.Clamp(startSanity, 0f, maxSanity);
            initialized = true;
            ResolveReferences();
            EnsureHud();
            EnsureGreyOverlay();
            RefreshAllText();
        }

        void OnEnable()
        {
            DuoCurtainLocalization.LanguageChanged += RefreshAllText;
        }

        void OnDisable()
        {
            DuoCurtainLocalization.LanguageChanged -= RefreshAllText;
            if (Active == this)
                Active = null;
        }

        void OnDestroy()
        {
            if (deathBlurTexture != null)
                Destroy(deathBlurTexture);

            if (Active == this)
                Active = null;
        }

        void Update()
        {
            if (!initialized)
                return;

            ResolveReferences();
            ApplySettingsIfPresent();
            if (deathActive)
            {
                if (Input.GetKeyDown(KeyCode.Space))
                    RestartScene();
                return;
            }

            TickSanity(Time.deltaTime);
            ApplyHudLayout();
            RefreshHud();
            RefreshGreyOverlay();
            UpdateRestoreButtonVisibility();
        }

        private void ApplySettingsIfPresent()
        {
#if UNITY_EDITOR
            SanitySettings source = CurtainSettingsLocator.Sanity;
            if (source == null)
                return;

            maxSanity = source.maxSanity;
            startSanity = source.startSanity;
            nightOutdoorDrainPerSecond = source.nightOutdoorDrainPerSecond;
            nightIndoorRecoveryPerSecond = source.nightIndoorRecoveryPerSecond;
            dayIndoorRecoveryPerSecond = source.dayIndoorRecoveryPerSecond;
            dayOutdoorRecoveryPerSecond = source.dayOutdoorRecoveryPerSecond;
            enemyTouchDamage = source.enemyTouchDamage;
            windowDetectionDamage = source.windowDetectionDamage;

            freezeOnDeath = source.freezeOnDeath;
            deathTint = source.deathTint;
            deathFadeDuration = source.deathFadeDuration;
            deathBlurDownsample = source.deathBlurDownsample;
            deathBlurRadius = source.deathBlurRadius;
            deathBlurIterations = source.deathBlurIterations;
#endif
        }

        public void DrainSanity(float amount)
        {
            if (amount <= 0f || deathActive)
                return;

            SetSanity(currentSanity - amount);
        }

        public void AddSanity(float amount)
        {
            if (amount <= 0f || deathActive)
                return;

            SetSanity(currentSanity + amount);
        }

        public void ApplyHalfSanityPenalty()
        {
            if (deathActive)
                return;

            SetSanity(currentSanity * 0.5f);
        }

        public bool TryRestoreWithMoney()
        {
            if (!enableManagementRestore || deathActive || currentSanity >= maxSanity - 0.001f)
                return false;

            ResolveReferences();
            if (currencySource == null || !currencySource.TrySpend(restoreCost))
                return false;

            AddSanity(restoreAmount);
            return true;
        }

        private void TickSanity(float deltaTime)
        {
            if (PauseManager.IsGamePaused || deltaTime <= 0f)
                return;

            bool isNight = stageController != null && stageController.IsNight;
            bool isOutside = playerControl != null && playerControl.IsOutsideRuntimeRoom;
            float delta;
            if (isNight)
                delta = isOutside ? -nightOutdoorDrainPerSecond : nightIndoorRecoveryPerSecond;
            else
                delta = isOutside ? dayOutdoorRecoveryPerSecond : dayIndoorRecoveryPerSecond;

            if (Mathf.Abs(delta) > 0.0001f)
                SetSanity(currentSanity + delta * deltaTime);
        }

        private void SetSanity(float value)
        {
            currentSanity = Mathf.Clamp(value, 0f, maxSanity);
            RefreshHud();
            RefreshGreyOverlay();

            if (currentSanity <= 0f && !deathActive)
                TriggerDeath();
        }

        private void TriggerDeath()
        {
            deathActive = true;
            if (freezeOnDeath)
            {
                previousTimeScale = Time.timeScale;
                previousFixedDeltaTime = Time.fixedDeltaTime;
                previousAudioPaused = AudioListener.pause;
                Time.timeScale = 0f;
                AudioListener.pause = true;
            }

            if (deathOverlayRoutine != null)
                StopCoroutine(deathOverlayRoutine);
            deathOverlayRoutine = StartCoroutine(ShowDeathOverlay());
        }

        private IEnumerator ShowDeathOverlay()
        {
            EnsureDeathOverlay();
            deathCanvas.gameObject.SetActive(false);

            yield return new WaitForEndOfFrame();

            Texture2D screenshot = PauseManager.CaptureCameraAsTexture(blurSourceCamera != null ? blurSourceCamera : Camera.main);
            if (screenshot != null)
            {
                Texture2D blurred = PauseManager.CreateBlurredTexture(
                    screenshot,
                    Mathf.Max(1, deathBlurDownsample),
                    Mathf.Max(0, deathBlurRadius),
                    Mathf.Max(1, deathBlurIterations));
                Destroy(screenshot);

                if (deathBlurTexture != null)
                    Destroy(deathBlurTexture);
                deathBlurTexture = blurred;
                deathBlurImage.texture = deathBlurTexture;
            }

            RefreshAllText();
            deathCanvasGroup.alpha = 0f;
            deathCanvas.gameObject.SetActive(true);

            float start = Time.unscaledTime;
            float duration = Mathf.Max(0.0001f, deathFadeDuration);
            while (deathCanvasGroup.alpha < 1f)
            {
                float normalized = Mathf.Clamp01((Time.unscaledTime - start) / duration);
                deathCanvasGroup.alpha = normalized;
                yield return null;
            }

            deathCanvasGroup.alpha = 1f;
            deathOverlayRoutine = null;
        }

        private void RestartScene()
        {
            if (!deathActive)
                return;

            Time.timeScale = previousTimeScale <= 0f ? 1f : previousTimeScale;
            Time.fixedDeltaTime = previousFixedDeltaTime > 0f ? previousFixedDeltaTime : Time.fixedDeltaTime;
            AudioListener.pause = previousAudioPaused;
            deathActive = false;
            Scene activeScene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(activeScene.name);
        }

        private void ResolveReferences()
        {
            if (playerControl == null)
                playerControl = PlayerControl.Active != null ? PlayerControl.Active : FindFirstObjectByType<PlayerControl>();
            if (fusionSandbox == null)
                fusionSandbox = FindFirstObjectByType<RuntimeTileMeshFusionSandbox>();
            if (stageController == null)
                stageController = FindFirstObjectByType<StageCycleController>();
            if (gameModeController == null)
                gameModeController = FindFirstObjectByType<FusionGameModeController>();
            if (currencySource == null)
                currencySource = gameModeController != null && gameModeController.currencySource != null
                    ? gameModeController.currencySource
                    : FindFirstObjectByType<TimeCounterUI>();
            if (blurSourceCamera == null)
                blurSourceCamera = Camera.main;
        }

        private void EnsureHud()
        {
            if (!createHudIfMissing)
                return;

            Canvas canvas = GetOrCreateCanvas("Fusion Sanity Canvas", 1300);
            hudCanvas = canvas;

            if (sanityText == null)
            {
                GameObject textObject = new GameObject("Sanity TMP", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                textObject.transform.SetParent(canvas.transform, false);
                RectTransform rect = textObject.GetComponent<RectTransform>();
                sanityTextRectTransform = rect;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.zero;
                rect.pivot = new Vector2(0f, 0f);

                sanityText = textObject.GetComponent<TextMeshProUGUI>();
                sanityText.alignment = TextAlignmentOptions.BottomLeft;
                sanityText.fontSize = 28f;
                sanityText.color = sanityTextColor;
                sanityText.raycastTarget = false;
            }

            if (restoreButton == null)
                CreateRestoreButton(canvas.transform);

            if (sanityText != null && sanityTextRectTransform == null)
                sanityTextRectTransform = sanityText.rectTransform;
            if (restoreButton != null && restoreButtonRectTransform == null)
                restoreButtonRectTransform = restoreButton.GetComponent<RectTransform>();
            ApplyHudLayout();
        }

        private void CreateRestoreButton(Transform parent)
        {
            GameObject buttonObject = new GameObject(
                "Restore Sanity Button",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            restoreButtonRectTransform = rect;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0f, 0f);

            Image image = buttonObject.GetComponent<Image>();
            image.color = restoreButtonColor;

            restoreButton = buttonObject.GetComponent<Button>();
            restoreButton.onClick.AddListener(() => TryRestoreWithMoney());

            GameObject textObject = new GameObject("Restore Sanity Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(buttonObject.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            restoreButtonText = textObject.GetComponent<TextMeshProUGUI>();
            restoreButtonText.raycastTarget = false;
            restoreButtonText.alignment = TextAlignmentOptions.Center;
            restoreButtonText.fontSize = 20f;
            restoreButtonText.color = restoreTextColor;
        }

        private void ApplyHudLayout()
        {
            float shopLift = 0f;
            if (liftRestoreUiAboveShop && gameModeController != null)
            {
                shopLift = gameModeController.VisibleShopTopInset;
                if (forceRestoreButtonAboveShopInManagement && gameModeController.IsManagementMode)
                {
                    float expectedShopTop = gameModeController.ShopBannerHeight + gameModeController.ShopBottomInset;
                    shopLift = Mathf.Max(shopLift, expectedShopTop);
                }

                if (shopLift > 0.001f)
                {
                    float progress = forceRestoreButtonAboveShopInManagement && gameModeController.IsManagementMode
                        ? Mathf.Max(gameModeController.ShopSlideProgress, 1f)
                        : gameModeController.ShopSlideProgress;
                    shopLift += shopUiClearance * progress;
                }
            }

            Vector2 sanityPosition = sanityHudAnchoredPosition + Vector2.up * shopLift;
            Vector2 restorePosition = restoreButtonAnchoredPosition + Vector2.up * shopLift;
            if (forceRestoreButtonAboveShopInManagement &&
                gameModeController != null &&
                gameModeController.IsManagementMode)
            {
                float expectedShopTop = gameModeController.ShopBannerHeight + gameModeController.ShopBottomInset;
                restorePosition.y = Mathf.Max(
                    restorePosition.y,
                    expectedShopTop + shopUiClearance + restoreButtonManagementExtraLift);
            }

            if (keepRestoreButtonClearOfSanityHud)
            {
                float sanityTop = sanityPosition.y + Mathf.Max(0f, sanityHudSize.y);
                restorePosition.y = Mathf.Max(restorePosition.y, sanityTop + restoreButtonSanityHudGap);
            }

            if (sanityText != null)
            {
                if (sanityTextRectTransform == null)
                    sanityTextRectTransform = sanityText.rectTransform;

                sanityTextRectTransform.anchorMin = Vector2.zero;
                sanityTextRectTransform.anchorMax = Vector2.zero;
                sanityTextRectTransform.pivot = Vector2.zero;
                sanityTextRectTransform.sizeDelta = sanityHudSize;
                sanityTextRectTransform.anchoredPosition = sanityPosition;
            }

            if (restoreButton != null)
            {
                if (restoreButtonRectTransform == null)
                    restoreButtonRectTransform = restoreButton.GetComponent<RectTransform>();

                restoreButtonRectTransform.anchorMin = Vector2.zero;
                restoreButtonRectTransform.anchorMax = Vector2.zero;
                restoreButtonRectTransform.pivot = Vector2.zero;
                restoreButtonRectTransform.sizeDelta = restoreButtonSize;
                restoreButtonRectTransform.anchoredPosition = restorePosition;
            }
        }

        private void EnsureGreyOverlay()
        {
            if (!showGreyOverlay || greyOverlayImage != null)
                return;

            Canvas canvas = GetOrCreateCanvas("Fusion Sanity Grey Overlay", greyOverlaySortingOrder);
            overlayCanvas = canvas;
            GameObject imageObject = new GameObject("Low Sanity Grey Overlay", typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(canvas.transform, false);
            RectTransform rect = imageObject.GetComponent<RectTransform>();
            StretchToParent(rect);
            greyOverlayImage = imageObject.GetComponent<Image>();
            greyOverlayImage.raycastTarget = false;
            RefreshGreyOverlay();
        }

        private void EnsureDeathOverlay()
        {
            if (deathCanvas != null)
                return;

            deathCanvas = GetOrCreateCanvas("Fusion Death Canvas", 7000);
            deathCanvasGroup = deathCanvas.GetComponent<CanvasGroup>();
            if (deathCanvasGroup == null)
                deathCanvasGroup = deathCanvas.gameObject.AddComponent<CanvasGroup>();
            deathCanvasGroup.blocksRaycasts = true;
            deathCanvasGroup.interactable = true;

            GameObject blurObject = new GameObject("Death Blur", typeof(RectTransform), typeof(RawImage));
            blurObject.transform.SetParent(deathCanvas.transform, false);
            StretchToParent(blurObject.GetComponent<RectTransform>());
            deathBlurImage = blurObject.GetComponent<RawImage>();
            deathBlurImage.raycastTarget = false;
            deathBlurImage.color = Color.white;

            GameObject tintObject = new GameObject("Death Red Tint", typeof(RectTransform), typeof(Image));
            tintObject.transform.SetParent(deathCanvas.transform, false);
            StretchToParent(tintObject.GetComponent<RectTransform>());
            deathTintImage = tintObject.GetComponent<Image>();
            deathTintImage.raycastTarget = false;
            deathTintImage.color = deathTint;

            GameObject titleObject = new GameObject("Death Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            titleObject.transform.SetParent(deathCanvas.transform, false);
            RectTransform titleRect = titleObject.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 0.5f);
            titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.anchoredPosition = new Vector2(0f, 50f);
            titleRect.sizeDelta = new Vector2(900f, 140f);
            deathTitleText = titleObject.GetComponent<TextMeshProUGUI>();
            deathTitleText.alignment = TextAlignmentOptions.Center;
            deathTitleText.fontSize = deathTitleFontSize;
            deathTitleText.color = Color.white;
            deathTitleText.raycastTarget = false;

            GameObject buttonObject = new GameObject("Death Restart Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(deathCanvas.transform, false);
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.anchoredPosition = new Vector2(0f, -90f);
            buttonRect.sizeDelta = new Vector2(360f, 78f);
            buttonObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.16f);
            deathRestartButton = buttonObject.GetComponent<Button>();
            deathRestartButton.onClick.AddListener(RestartScene);

            GameObject restartTextObject = new GameObject("Death Restart Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            restartTextObject.transform.SetParent(buttonObject.transform, false);
            StretchToParent(restartTextObject.GetComponent<RectTransform>());
            deathRestartText = restartTextObject.GetComponent<TextMeshProUGUI>();
            deathRestartText.alignment = TextAlignmentOptions.Center;
            deathRestartText.fontSize = 30f;
            deathRestartText.color = Color.white;
            deathRestartText.raycastTarget = false;

            deathCanvas.gameObject.SetActive(false);
        }

        private void RefreshHud()
        {
            if (sanityText != null)
            {
                string text = DuoCurtainLocalization.Format(
                    "hud.sanity",
                    "理智：{0}",
                    "SANITY: {0}",
                    Mathf.CeilToInt(currentSanity));
                sanityText.text = text;
                sanityText.color = sanityTextColor;
                DuoCurtainLocalization.ApplyFont(sanityText, text);
            }

            if (sanitySlider != null)
            {
                sanitySlider.minValue = 0f;
                sanitySlider.maxValue = maxSanity;
                sanitySlider.value = currentSanity;
            }
        }

        private void RefreshGreyOverlay()
        {
            if (!showGreyOverlay || greyOverlayImage == null)
                return;

            float danger = 1f - NormalizedSanity;
            Color color = new Color(0.42f, 0.42f, 0.42f, danger * greyOverlayMaxAlpha);
            greyOverlayImage.color = color;
            greyOverlayImage.gameObject.SetActive(color.a > 0.001f && !deathActive);
        }

        private void RefreshAllText()
        {
            RefreshHud();
            RefreshRestoreButtonText();

            if (deathTitleText != null)
            {
                string text = DuoCurtainLocalization.Text("death.title", deathTitleChinese, deathTitleEnglish);
                deathTitleText.text = text;
                DuoCurtainLocalization.ApplyFont(deathTitleText, text);
            }

            if (deathRestartText != null)
            {
                string text = DuoCurtainLocalization.Text("death.restart", deathRestartChinese, deathRestartEnglish);
                deathRestartText.text = text;
                DuoCurtainLocalization.ApplyFont(deathRestartText, text);
            }
        }

        private void RefreshRestoreButtonText()
        {
            if (restoreButtonText == null)
                return;

            string text = DuoCurtainLocalization.Format(
                "sanity.restore",
                "恢复理智 ${0} / +{1}",
                "RESTORE ${0} / +{1}",
                restoreCost,
                Mathf.CeilToInt(restoreAmount));
            restoreButtonText.text = text;
            restoreButtonText.color = restoreTextColor;
            DuoCurtainLocalization.ApplyFont(restoreButtonText, text);
        }

        private void UpdateRestoreButtonVisibility()
        {
            if (restoreButton == null)
                return;

            bool visible = enableManagementRestore &&
                gameModeController != null &&
                gameModeController.IsManagementMode &&
                !deathActive;
            restoreButton.gameObject.SetActive(visible);
            if (visible)
                RefreshRestoreButtonText();
        }

        private static Canvas GetOrCreateCanvas(string name, int sortingOrder)
        {
            GameObject existing = GameObject.Find(name);
            Canvas canvas = existing != null ? existing.GetComponent<Canvas>() : null;
            if (canvas != null)
                return canvas;

            GameObject canvasObject = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        private static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
