using System.Collections;
using System.Collections.Generic;
using DuoCurtain.RuntimeTileMesh;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum BootWorldState
{
    ApplicationBoot,
    BootWorld,
    GameplayTransition,
    Gameplay
}

[System.Serializable]
public sealed class BootWorldStateChangedEvent : UnityEvent<BootWorldState> { }

[DisallowMultipleComponent]
public sealed class BootWorldStateController : MonoBehaviour
{
    [Header("State")]
    [SerializeField] private bool startInBootWorld = true;
    [SerializeField] private bool listenForAnyInput = true;
    [SerializeField] private bool includeMouseInput = false;
    [SerializeField] private bool includeGamepadInput = true;
    [SerializeField] private bool autoWireRedScene = true;

    [Header("Day Night")]
    [SerializeField] private StageCycleController stageController;
    [SerializeField] private bool fastForwardDayNightBeforeGameplay = true;
    [SerializeField] private int gameplayStartStageIndex;
    [SerializeField] private float gameplayStartStageTimer;
    [SerializeField] private float dayNightFastForwardMultiplier = 12f;
    [SerializeField] private float maxDayNightFastForwardSeconds = 4f;
    [SerializeField] private float dayNightTargetTolerance = 0.08f;
    [SerializeField] private bool snapDayNightAtTransitionEnd;

    [Header("Title UI")]
    [SerializeField] private CanvasGroup titleCanvasGroup;
    [SerializeField] private GameObject[] titleUiObjects;
    [SerializeField] private bool autoCreateTemporaryTitleUi = true;
    [SerializeField] private string temporaryLogoTextChinese = "CURTAIN";
    [SerializeField] private string temporaryLogoTextEnglish = "CURTAIN";
    [SerializeField] private string temporaryPressAnyKeyText = "PRESS ANY KEY";
    [SerializeField] private string temporaryLanguageText = "LANGUAGE";
    [SerializeField] private string temporarySettingsText = "SETTINGS";
    [SerializeField] private string temporaryQuitText = "QUIT";
    [SerializeField] private float titleFadeOutDuration = 0.35f;
    [SerializeField] private int titleCanvasSortingOrder = 2400;
    [SerializeField] private bool useScreenInvertTitleText = true;

    [Header("Title Cursor")]
    [SerializeField] private bool showTemporaryTitleCursor = true;
    [SerializeField] private int titleCursorCanvasSortingOrder = 7200;
    [SerializeField] private float titleCursorSize = 18f;
    [Range(0f, 1f)]
    [SerializeField] private float titleCursorAlpha = 0.95f;

    [Header("Boot World Disabled During Title")]
    [SerializeField] private Behaviour[] playerBehaviours;
    [SerializeField] private GameObject[] playerObjects;
    [SerializeField] private Behaviour[] gameplayUiBehaviours;
    [SerializeField] private GameObject[] gameplayUiObjects;

    [Header("Boot World Enabled During Title")]
    [SerializeField] private Behaviour[] bootWorldBehaviours;
    [SerializeField] private GameObject[] bootWorldObjects;

    [Header("Events")]
    public BootWorldStateChangedEvent onStateChanged = new BootWorldStateChangedEvent();

    private Coroutine transitionCoroutine;
    private float savedSimulationSpeed = 1f;
    private bool hasAutoWiredScene;
    private TextMeshProUGUI temporaryLogoLabel;
    private TextMeshProUGUI temporaryPressAnyKeyLabel;
    private TextMeshProUGUI temporaryLanguageLabel;
    private TextMeshProUGUI temporarySettingsLabel;
    private TextMeshProUGUI temporaryQuitLabel;
    private readonly Dictionary<TextMeshProUGUI, Material> temporaryTitleInvertMaterials =
        new Dictionary<TextMeshProUGUI, Material>();
    private readonly Dictionary<TextMeshProUGUI, Material> temporaryTitleSourceMaterials =
        new Dictionary<TextMeshProUGUI, Material>();
    private Canvas temporaryTitleCursorCanvas;
    private Image temporaryTitleCursorImage;
    private Material temporaryTitleCursorMaterial;
    private Texture2D temporaryTitleCursorTexture;
    private Sprite temporaryTitleCursorSprite;
    private FusionGameModeController gameModeController;
    private FusionModeCameraRig playerCameraRig;
    private FusionModeCameraRig managementCameraRig;
    private BootWorldSettingsPanel settingsPanel;

    private const string MainCameraTag = "MainCamera";
    private const string UntaggedCameraTag = "Untagged";
    private const string TmpScreenInvertShaderName = "DuoCurtain/UI/TMP Screen Invert";
    private const string HeadingPointInvertShaderName = "DuoCurtain/UI/HeadingPointInvert";

    public static BootWorldStateController Active { get; private set; }
    public BootWorldState CurrentState { get; private set; } = BootWorldState.ApplicationBoot;
    public bool IsBootWorldActive => CurrentState == BootWorldState.BootWorld;
    public bool IsTransitioningToGameplay => CurrentState == BootWorldState.GameplayTransition;
    public bool IsGameplayActive => CurrentState == BootWorldState.Gameplay;
    public static bool IsBootWorldActiveGlobally => Active != null && Active.IsBootWorldActive;
    public static bool IsGameplayActiveGlobally => Active == null || Active.IsGameplayActive;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRedSceneBootWorldController()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || activeScene.name != "RedScene")
            return;

        if (FindFirstObjectByType<BootWorldStateController>() != null)
            return;

        GameObject controllerObject = new GameObject("Boot World State Controller");
        BootWorldStateController controller = controllerObject.AddComponent<BootWorldStateController>();
        controller.startInBootWorld = true;
        controller.autoWireRedScene = true;
        controller.autoCreateTemporaryTitleUi = true;
    }

    private void OnEnable()
    {
        Active = this;
        DuoCurtainLocalization.LanguageChanged += RefreshTemporaryTitleUiText;
    }

    private void OnDisable()
    {
        if (Active == this)
            Active = null;

        DuoCurtainLocalization.LanguageChanged -= RefreshTemporaryTitleUiText;
        SetTemporaryTitleCursorVisible(false);
    }

    private void OnDestroy()
    {
        foreach (Material material in temporaryTitleInvertMaterials.Values)
            DestroyRuntimeObject(material);

        temporaryTitleInvertMaterials.Clear();
        temporaryTitleSourceMaterials.Clear();
        DestroyRuntimeObject(temporaryTitleCursorMaterial);
        DestroyRuntimeObject(temporaryTitleCursorSprite);
        DestroyRuntimeObject(temporaryTitleCursorTexture);
    }

    private void Start()
    {
        ResolveReferencesAndAutoWire();
        EnsureTemporaryTitleUi();

        savedSimulationSpeed = stageController != null ? stageController.simulationSpeedMultiplier : 1f;

        if (startInBootWorld)
            EnterBootWorld();
        else
            EnterGameplayImmediate();
    }

    private void Update()
    {
        UpdateTemporaryTitleCursor();

        if (!listenForAnyInput || CurrentState != BootWorldState.BootWorld)
            return;

        if (HasStartInput())
            BeginGameplayTransition();
    }

    [ContextMenu("Enter Boot World")]
    public void EnterBootWorld()
    {
        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
            transitionCoroutine = null;
        }

        savedSimulationSpeed = stageController != null ? stageController.simulationSpeedMultiplier : savedSimulationSpeed;
        SetState(BootWorldState.BootWorld);
        ApplyBootWorldActive(true);
        SetTitleVisible(true, true);
    }

    [ContextMenu("Begin Gameplay Transition")]
    public void BeginGameplayTransition()
    {
        if (CurrentState == BootWorldState.Gameplay || CurrentState == BootWorldState.GameplayTransition)
            return;

        if (transitionCoroutine != null)
            StopCoroutine(transitionCoroutine);

        transitionCoroutine = StartCoroutine(TransitionToGameplay());
    }

    [ContextMenu("Enter Gameplay Immediately")]
    public void EnterGameplayImmediate()
    {
        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
            transitionCoroutine = null;
        }

        RestoreStageSimulationSpeed();
        SetTitleVisible(false, true);
        ApplyBootWorldActive(false);
        SetState(BootWorldState.Gameplay);
    }

    public void SetStartInBootWorld(bool enabled)
    {
        startInBootWorld = enabled;
    }

    public void SetListenForAnyInput(bool enabled)
    {
        listenForAnyInput = enabled;
    }

    private IEnumerator TransitionToGameplay()
    {
        SetState(BootWorldState.GameplayTransition);
        SetTitleInteractable(false);

        yield return FadeTitleOut();
        yield return FastForwardDayNightIfNeeded();

        transitionCoroutine = null;
        EnterGameplayImmediate();
    }

    private IEnumerator FadeTitleOut()
    {
        if (titleCanvasGroup == null || titleFadeOutDuration <= 0f)
        {
            SetTitleVisible(false, true);
            yield break;
        }

        float startAlpha = titleCanvasGroup.alpha;
        float elapsed = 0f;
        while (elapsed < titleFadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / titleFadeOutDuration);
            titleCanvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t);
            yield return null;
        }

        SetTitleVisible(false, true);
    }

    private IEnumerator FastForwardDayNightIfNeeded()
    {
        if (!fastForwardDayNightBeforeGameplay || stageController == null)
            yield break;

        float cycleDuration = GetCycleDuration();
        if (cycleDuration <= 0.0001f)
            yield break;

        float originalSpeed = stageController.simulationSpeedMultiplier;
        stageController.SetSimulationSpeedMultiplier(dayNightFastForwardMultiplier);

        float elapsed = 0f;
        while (elapsed < maxDayNightFastForwardSeconds)
        {
            if (GetForwardDistanceToGameplayStart(cycleDuration) <= Mathf.Max(0.0001f, dayNightTargetTolerance))
                break;

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        stageController.SetSimulationSpeedMultiplier(originalSpeed);

        if (snapDayNightAtTransitionEnd)
            stageController.SetStageTime(gameplayStartStageIndex, gameplayStartStageTimer);
    }

    private void ApplyBootWorldActive(bool bootActive)
    {
        if (bootActive)
            ActivateBootWorldCamera();
        else
            RestoreGameplayCamera();

        SetBehavioursEnabled(playerBehaviours, !bootActive);
        SetObjectsActive(playerObjects, !bootActive);
        SetBehavioursEnabled(gameplayUiBehaviours, !bootActive);
        SetObjectsActive(gameplayUiObjects, !bootActive);
        SetBehavioursEnabled(bootWorldBehaviours, bootActive);
        SetObjectsActive(bootWorldObjects, bootActive);
    }

    private void ActivateBootWorldCamera()
    {
        ResolveCameraRigs();
        if (managementCameraRig == null)
            return;

        Camera managementCamera = managementCameraRig.Camera;
        if (managementCamera == null)
            return;

        managementCameraRig.enabled = true;
        managementCamera.gameObject.SetActive(true);
        managementCamera.enabled = true;
        managementCamera.tag = MainCameraTag;
        managementCameraRig.SnapToDesiredPose();

        if (playerCameraRig != null)
        {
            playerCameraRig.enabled = false;
            if (playerCameraRig.Camera != null)
            {
                playerCameraRig.Camera.enabled = false;
                if (playerCameraRig.Camera.CompareTag(MainCameraTag))
                    playerCameraRig.Camera.tag = UntaggedCameraTag;
            }
        }
    }

    private void RestoreGameplayCamera()
    {
        ResolveCameraRigs();
        if (gameModeController != null)
        {
            if (!gameModeController.enabled)
                gameModeController.enabled = true;

            gameModeController.SetMode(FusionGameModeController.GameMode.Player, false);
            return;
        }

        if (playerCameraRig != null)
        {
            playerCameraRig.enabled = true;
            if (playerCameraRig.Camera != null)
            {
                playerCameraRig.Camera.gameObject.SetActive(true);
                playerCameraRig.Camera.enabled = true;
                playerCameraRig.Camera.tag = MainCameraTag;
                playerCameraRig.SnapToDesiredPose();
            }
        }

        if (managementCameraRig?.Camera != null)
        {
            managementCameraRig.Camera.enabled = false;
            if (managementCameraRig.Camera.CompareTag(MainCameraTag))
                managementCameraRig.Camera.tag = UntaggedCameraTag;
        }
    }

    private void ResolveCameraRigs()
    {
        if (gameModeController == null)
            gameModeController = FindFirstObjectByType<FusionGameModeController>();

        if (gameModeController != null)
        {
            if (playerCameraRig == null)
                playerCameraRig = gameModeController.playerCamera;
            if (managementCameraRig == null)
                managementCameraRig = gameModeController.managementCamera;
        }

        if (playerCameraRig != null && managementCameraRig != null)
            return;

        FusionModeCameraRig[] rigs = FindObjectsByType<FusionModeCameraRig>(FindObjectsSortMode.None);
        for (int i = 0; i < rigs.Length; i++)
        {
            FusionModeCameraRig rig = rigs[i];
            if (rig == null)
                continue;

            if (rig.mode == FusionModeCameraRig.RigMode.PlayerFollow && playerCameraRig == null)
                playerCameraRig = rig;
            else if (rig.mode == FusionModeCameraRig.RigMode.ManagementOverview && managementCameraRig == null)
                managementCameraRig = rig;
        }
    }

    private void ResolveReferencesAndAutoWire()
    {
        if (stageController == null)
            stageController = FindFirstObjectByType<StageCycleController>();

        if (!autoWireRedScene || hasAutoWiredScene)
            return;

        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || activeScene.name != "RedScene")
            return;

        PlayerControl playerControl = FindFirstObjectByType<PlayerControl>();
        gameModeController = FindFirstObjectByType<FusionGameModeController>();
        ResolveCameraRigs();

        if (playerBehaviours == null || playerBehaviours.Length == 0)
            playerBehaviours = CompactBehaviours(playerControl);

        if (gameplayUiBehaviours == null || gameplayUiBehaviours.Length == 0)
            gameplayUiBehaviours = CompactBehaviours(gameModeController);

        if (playerObjects == null || playerObjects.Length == 0)
        {
            playerObjects = CompactObjects(
                GameObject.Find("Player Control"),
                GameObject.Find("Player"),
                GameObject.Find("Heading Point"));
        }

        if (gameplayUiObjects == null || gameplayUiObjects.Length == 0)
        {
            gameplayUiObjects = CompactObjects(
                GameObject.Find("Player Control Canvas"),
                GameObject.Find("Fusion UI Canvas"),
                GameObject.Find("Fusion Sanity Canvas"),
                GameObject.Find("Topology Map System"));
        }

        hasAutoWiredScene = true;
    }

    private bool HasStartInput()
    {
        bool mouseDown = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
        if (mouseDown)
        {
            return includeMouseInput && !IsPointerOverBootWorldUi();
        }

        if (Input.anyKeyDown)
            return true;

        if (!includeGamepadInput)
            return false;

        return Input.GetKeyDown(KeyCode.JoystickButton0) ||
               Input.GetKeyDown(KeyCode.JoystickButton1) ||
               Input.GetKeyDown(KeyCode.JoystickButton2) ||
               Input.GetKeyDown(KeyCode.JoystickButton3) ||
               Input.GetKeyDown(KeyCode.JoystickButton4) ||
               Input.GetKeyDown(KeyCode.JoystickButton5) ||
               Input.GetKeyDown(KeyCode.JoystickButton6) ||
               Input.GetKeyDown(KeyCode.JoystickButton7) ||
               Input.GetKeyDown(KeyCode.JoystickButton8) ||
               Input.GetKeyDown(KeyCode.JoystickButton9) ||
               Input.GetKeyDown(KeyCode.JoystickButton10) ||
               Input.GetKeyDown(KeyCode.JoystickButton11) ||
               Input.GetKeyDown(KeyCode.JoystickButton12) ||
               Input.GetKeyDown(KeyCode.JoystickButton13) ||
               Input.GetKeyDown(KeyCode.JoystickButton14) ||
               Input.GetKeyDown(KeyCode.JoystickButton15) ||
               Input.GetKeyDown(KeyCode.JoystickButton16) ||
               Input.GetKeyDown(KeyCode.JoystickButton17) ||
               Input.GetKeyDown(KeyCode.JoystickButton18) ||
               Input.GetKeyDown(KeyCode.JoystickButton19);
    }

    private void EnsureTemporaryTitleUi()
    {
        if (!autoCreateTemporaryTitleUi || titleCanvasGroup != null)
        {
            RefreshTemporaryTitleUiText();
            return;
        }

        EnsureEventSystem();

        GameObject canvasObject = new GameObject(
            "Boot World Temporary Title Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(CanvasGroup));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = titleCanvasSortingOrder;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        titleCanvasGroup = canvasObject.GetComponent<CanvasGroup>();
        titleCanvasGroup.interactable = true;
        titleCanvasGroup.blocksRaycasts = true;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.anchorMin = Vector2.zero;
        canvasRect.anchorMax = Vector2.one;
        canvasRect.offsetMin = Vector2.zero;
        canvasRect.offsetMax = Vector2.zero;

        temporaryLogoLabel = CreateTitleText(canvasRect, "Logo", temporaryLogoTextEnglish, 86f, new Vector2(0f, 138f), TextAlignmentOptions.Center);
        temporaryPressAnyKeyLabel = CreateTitleText(canvasRect, "Press Any Key", temporaryPressAnyKeyText, 32f, new Vector2(0f, 40f), TextAlignmentOptions.Center);
        temporaryLanguageLabel = CreateTitleButton(canvasRect, "Language Button", temporaryLanguageText, new Vector2(-180f, -220f), HandleLanguagePressed);
        temporarySettingsLabel = CreateTitleButton(canvasRect, "Settings Button", temporarySettingsText, new Vector2(0f, -220f), HandleSettingsPressed);
        temporaryQuitLabel = CreateTitleButton(canvasRect, "Quit Button", temporaryQuitText, new Vector2(180f, -220f), HandleQuitPressed);
        RefreshTemporaryTitleUiText();
    }

    private TextMeshProUGUI CreateTitleText(
        Transform parent,
        string objectName,
        string text,
        float fontSize,
        Vector2 anchoredPosition,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(760f, 112f);

        TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = Color.white;
        label.raycastTarget = false;
        DuoCurtainLocalization.ApplyFont(label, text);
        ApplyTemporaryTitleTextMaterial(label);
        return label;
    }

    private TextMeshProUGUI CreateTitleButton(
        Transform parent,
        string objectName,
        string labelText,
        Vector2 anchoredPosition,
        UnityAction callback)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(156f, 48f);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.32f);

        Button button = buttonObject.GetComponent<Button>();
        button.onClick.AddListener(callback);

        TextMeshProUGUI label = CreateTitleText(rect, "Label", labelText, 20f, Vector2.zero, TextAlignmentOptions.Center);
        RectTransform labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        return label;
    }

    private void HandleLanguagePressed()
    {
        DuoCurtainLocalization.ToggleLanguage();
        RefreshTemporaryTitleUiText();
    }

    private void HandleSettingsPressed()
    {
        Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this, "[BootWorld] Temporary Settings button pressed.");
        ToggleSettingsPanel();
    }

    private void ToggleSettingsPanel()
    {
        EnsureSettingsPanel();
        if (settingsPanel == null)
            return;

        bool nextVisible = !settingsPanel.IsVisible;
        settingsPanel.Show(nextVisible);

        if (titleCanvasGroup != null)
        {
            titleCanvasGroup.interactable = !nextVisible;
            titleCanvasGroup.blocksRaycasts = !nextVisible;
        }
    }

    private void EnsureSettingsPanel()
    {
        if (settingsPanel != null)
            return;

        BootWorldSettingsPanel existing = FindFirstObjectByType<BootWorldSettingsPanel>();
        if (existing != null)
        {
            settingsPanel = existing;
            settingsPanel.Initialize();
            settingsPanel.Show(false);
            return;
        }

        GameObject go = new GameObject("BootWorld Settings Panel");
        go.transform.SetParent(transform, false);
        settingsPanel = go.AddComponent<BootWorldSettingsPanel>();
        settingsPanel.Initialize();
        settingsPanel.Show(false);
    }

    private void HandleQuitPressed()
    {
        Application.Quit();
    }

    private static bool IsPointerOverBootWorldUi()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private void RefreshTemporaryTitleUiText()
    {
        // Logo intentionally defaults to the same text in both languages for now.
        SetLocalizedLabel(temporaryLogoLabel, temporaryLogoTextChinese, temporaryLogoTextEnglish);
        SetLocalizedLabel(temporaryPressAnyKeyLabel, "按任意键开始", temporaryPressAnyKeyText);
        SetLocalizedLabel(temporaryLanguageLabel, "语言", temporaryLanguageText);
        SetLocalizedLabel(temporarySettingsLabel, "设置", temporarySettingsText);
        SetLocalizedLabel(temporaryQuitLabel, "退出", temporaryQuitText);
    }

    private void SetLocalizedLabel(TextMeshProUGUI label, string chinese, string english)
    {
        if (label == null)
            return;

        string text = DuoCurtainLocalization.Text("boot.temporary", chinese, english);
        label.text = text;
        DuoCurtainLocalization.ApplyFont(label, text);
        ApplyTemporaryTitleTextMaterial(label);
    }

    private void ApplyTemporaryTitleTextMaterial(TextMeshProUGUI label)
    {
        if (label == null || !useScreenInvertTitleText)
            return;

        Shader shader = Shader.Find(TmpScreenInvertShaderName);
        if (shader == null)
            return;

        Material source = label.font != null && label.font.material != null
            ? label.font.material
            : label.fontSharedMaterial;
        bool needsRecreate =
            !temporaryTitleInvertMaterials.TryGetValue(label, out Material material) ||
            material == null ||
            !temporaryTitleSourceMaterials.TryGetValue(label, out Material trackedSource) ||
            !ReferenceEquals(trackedSource, source) ||
            (source != null && material.mainTexture != source.mainTexture);

        if (needsRecreate)
        {
            if (material != null)
                DestroyRuntimeObject(material);

            material = source != null ? new Material(source) : new Material(shader);
            material.name = $"{label.gameObject.name} TMP Screen Invert";
            material.hideFlags = HideFlags.HideAndDontSave;
            temporaryTitleInvertMaterials[label] = material;
            temporaryTitleSourceMaterials[label] = source;
        }

        if (material.shader != shader)
            material.shader = shader;

        if (material.HasProperty("_FaceColor"))
            material.SetColor("_FaceColor", Color.white);
        if (material.HasProperty("_OutlineColor"))
            material.SetColor("_OutlineColor", Color.clear);

        label.color = Color.white;
        label.fontMaterial = material;
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

    private void SetTitleVisible(bool visible, bool immediate)
    {
        if (titleCanvasGroup != null && visible)
            titleCanvasGroup.gameObject.SetActive(true);

        SetObjectsActive(titleUiObjects, visible);
        SetTemporaryTitleCursorVisible(visible);
        if (titleCanvasGroup == null)
            return;

        titleCanvasGroup.alpha = visible ? 1f : 0f;
        SetTitleInteractable(visible);
        if (!visible && immediate)
            titleCanvasGroup.gameObject.SetActive(false);
    }

    private void SetTitleInteractable(bool interactable)
    {
        if (titleCanvasGroup == null)
            return;

        titleCanvasGroup.interactable = interactable;
        titleCanvasGroup.blocksRaycasts = interactable;
    }

    private void SetTemporaryTitleCursorVisible(bool visible)
    {
        if (visible && showTemporaryTitleCursor)
            EnsureTemporaryTitleCursor();

        if (temporaryTitleCursorCanvas != null)
            temporaryTitleCursorCanvas.gameObject.SetActive(visible && showTemporaryTitleCursor);
    }

    private void UpdateTemporaryTitleCursor()
    {
        bool visible =
            showTemporaryTitleCursor &&
            (CurrentState == BootWorldState.BootWorld || CurrentState == BootWorldState.GameplayTransition) &&
            titleCanvasGroup != null &&
            titleCanvasGroup.gameObject.activeInHierarchy &&
            titleCanvasGroup.alpha > 0.001f;

        if (!visible)
        {
            if (temporaryTitleCursorCanvas != null && temporaryTitleCursorCanvas.gameObject.activeSelf)
                temporaryTitleCursorCanvas.gameObject.SetActive(false);
            return;
        }

        EnsureTemporaryTitleCursor();
        if (temporaryTitleCursorCanvas == null || temporaryTitleCursorImage == null)
            return;

        if (!temporaryTitleCursorCanvas.gameObject.activeSelf)
            temporaryTitleCursorCanvas.gameObject.SetActive(true);

        temporaryTitleCursorCanvas.sortingOrder = titleCursorCanvasSortingOrder;
        RectTransform rect = temporaryTitleCursorImage.rectTransform;
        float size = Mathf.Max(1f, titleCursorSize);
        rect.sizeDelta = new Vector2(size, size);
        rect.position = Input.mousePosition;
        temporaryTitleCursorImage.color = new Color(1f, 1f, 1f, Mathf.Clamp01(titleCursorAlpha * titleCanvasGroup.alpha));
    }

    private void EnsureTemporaryTitleCursor()
    {
        if (temporaryTitleCursorCanvas != null && temporaryTitleCursorImage != null)
            return;

        GameObject canvasObject = new GameObject(
            "Boot World Title Heading Point Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.anchorMin = Vector2.zero;
        canvasRect.anchorMax = Vector2.one;
        canvasRect.offsetMin = Vector2.zero;
        canvasRect.offsetMax = Vector2.zero;

        temporaryTitleCursorCanvas = canvasObject.GetComponent<Canvas>();
        temporaryTitleCursorCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        temporaryTitleCursorCanvas.sortingOrder = titleCursorCanvasSortingOrder;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject imageObject = new GameObject("Title Heading Point", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(canvasObject.transform, false);
        temporaryTitleCursorImage = imageObject.GetComponent<Image>();
        temporaryTitleCursorImage.raycastTarget = false;
        temporaryTitleCursorImage.sprite = EnsureTemporaryTitleCursorSprite();
        ApplyTemporaryTitleCursorMaterial();

        canvasObject.SetActive(false);
    }

    private void ApplyTemporaryTitleCursorMaterial()
    {
        if (temporaryTitleCursorImage == null)
            return;

        Shader shader = Shader.Find(HeadingPointInvertShaderName);
        if (shader == null)
            return;

        if (temporaryTitleCursorMaterial == null)
        {
            temporaryTitleCursorMaterial = new Material(shader);
            temporaryTitleCursorMaterial.name = "Boot World Title Heading Point Invert";
            temporaryTitleCursorMaterial.hideFlags = HideFlags.HideAndDontSave;
        }

        if (temporaryTitleCursorMaterial.shader != shader)
            temporaryTitleCursorMaterial.shader = shader;

        temporaryTitleCursorImage.material = temporaryTitleCursorMaterial;
    }

    private Sprite EnsureTemporaryTitleCursorSprite()
    {
        if (temporaryTitleCursorSprite != null)
            return temporaryTitleCursorSprite;

        const int textureSize = 64;
        temporaryTitleCursorTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
        {
            name = "Boot World Title Heading Point Circle",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color[] pixels = new Color[textureSize * textureSize];
        Vector2 center = new Vector2((textureSize - 1) * 0.5f, (textureSize - 1) * 0.5f);
        float radius = textureSize * 0.38f;
        float softEdge = Mathf.Max(1f, textureSize * 0.06f);
        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                float alpha = Mathf.Clamp01((radius - distance) / softEdge);
                pixels[y * textureSize + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        temporaryTitleCursorTexture.SetPixels(pixels);
        temporaryTitleCursorTexture.Apply(false, false);
        temporaryTitleCursorSprite = Sprite.Create(
            temporaryTitleCursorTexture,
            new Rect(0f, 0f, textureSize, textureSize),
            new Vector2(0.5f, 0.5f),
            textureSize);
        temporaryTitleCursorSprite.name = "Boot World Title Heading Point Sprite";
        temporaryTitleCursorSprite.hideFlags = HideFlags.HideAndDontSave;
        return temporaryTitleCursorSprite;
    }

    private void RestoreStageSimulationSpeed()
    {
        if (stageController != null)
            stageController.SetSimulationSpeedMultiplier(Mathf.Max(0f, savedSimulationSpeed));
    }

    private float GetCycleDuration()
    {
        if (stageController == null || stageController.stages == null || stageController.stages.Count == 0)
            return 0f;

        float total = 0f;
        for (int i = 0; i < stageController.stages.Count; i++)
            total += Mathf.Max(0.01f, stageController.stages[i].duration);

        return total;
    }

    private float GetForwardDistanceToGameplayStart(float cycleDuration)
    {
        float current = GetAbsoluteStageTime(stageController.GetCurrentStageIndex(), stageController.StageTimer);
        float target = GetAbsoluteStageTime(gameplayStartStageIndex, gameplayStartStageTimer);
        float distance = target - current;
        if (distance < 0f)
            distance += cycleDuration;

        return distance;
    }

    private float GetAbsoluteStageTime(int stageIndex, float timer)
    {
        if (stageController == null || stageController.stages == null || stageController.stages.Count == 0)
            return 0f;

        int safeStageIndex = Mathf.Clamp(stageIndex, 0, stageController.stages.Count - 1);
        float total = 0f;
        for (int i = 0; i < safeStageIndex; i++)
            total += Mathf.Max(0.01f, stageController.stages[i].duration);

        float stageDuration = Mathf.Max(0.01f, stageController.stages[safeStageIndex].duration);
        return total + Mathf.Clamp(timer, 0f, stageDuration);
    }

    private void SetState(BootWorldState nextState)
    {
        if (CurrentState == nextState)
            return;

        CurrentState = nextState;
        onStateChanged?.Invoke(CurrentState);
    }

    private static void SetBehavioursEnabled(Behaviour[] behaviours, bool enabled)
    {
        if (behaviours == null)
            return;

        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] != null)
                behaviours[i].enabled = enabled;
        }
    }

    private static void SetObjectsActive(GameObject[] objects, bool active)
    {
        if (objects == null)
            return;

        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] != null)
                objects[i].SetActive(active);
        }
    }

    private static Behaviour[] CompactBehaviours(params Behaviour[] source)
    {
        if (source == null || source.Length == 0)
            return new Behaviour[0];

        int count = 0;
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] != null)
                count++;
        }

        Behaviour[] result = new Behaviour[count];
        int index = 0;
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] != null)
                result[index++] = source[i];
        }

        return result;
    }

    private static GameObject[] CompactObjects(params GameObject[] source)
    {
        if (source == null || source.Length == 0)
            return new GameObject[0];

        int count = 0;
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] != null)
                count++;
        }

        GameObject[] result = new GameObject[count];
        int index = 0;
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] != null)
                result[index++] = source[i];
        }

        return result;
    }

    private static void DestroyRuntimeObject(Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }
}
