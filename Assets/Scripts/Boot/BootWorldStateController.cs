using System.Collections;
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
    [SerializeField] private string temporaryLogoText = "DUO CURTAIN";
    [SerializeField] private string temporaryPressAnyKeyText = "PRESS ANY KEY";
    [SerializeField] private string temporaryLanguageText = "LANGUAGE";
    [SerializeField] private string temporarySettingsText = "SETTINGS";
    [SerializeField] private string temporaryQuitText = "QUIT";
    [SerializeField] private float titleFadeOutDuration = 0.35f;

    [Header("Boot World Disabled During Title")]
    [SerializeField] private Behaviour[] playerBehaviours;
    [SerializeField] private GameObject[] playerObjects;
    [SerializeField] private Behaviour[] gameplayUiBehaviours;
    [SerializeField] private GameObject[] gameplayUiObjects;
    [SerializeField] private Behaviour[] gameplayCameraBehaviours;

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
        SetBehavioursEnabled(playerBehaviours, !bootActive);
        SetObjectsActive(playerObjects, !bootActive);
        SetBehavioursEnabled(gameplayUiBehaviours, !bootActive);
        SetObjectsActive(gameplayUiObjects, !bootActive);
        SetBehavioursEnabled(gameplayCameraBehaviours, !bootActive);

        SetBehavioursEnabled(bootWorldBehaviours, bootActive);
        SetObjectsActive(bootWorldObjects, bootActive);
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
        FusionGameModeController gameModeController = FindFirstObjectByType<FusionGameModeController>();
        FusionModeCameraRig cameraRig = FindFirstObjectByType<FusionModeCameraRig>();

        if (playerBehaviours == null || playerBehaviours.Length == 0)
            playerBehaviours = CompactBehaviours(playerControl);

        if (gameplayUiBehaviours == null || gameplayUiBehaviours.Length == 0)
            gameplayUiBehaviours = CompactBehaviours(gameModeController);

        if (gameplayCameraBehaviours == null || gameplayCameraBehaviours.Length == 0)
            gameplayCameraBehaviours = CompactBehaviours(cameraRig);

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
        canvas.sortingOrder = 2400;

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

        temporaryLogoLabel = CreateTitleText(canvasRect, "Logo", temporaryLogoText, 86f, new Vector2(0f, 138f), TextAlignmentOptions.Center);
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
        Debug.Log("[BootWorld] Temporary Settings button pressed.", this);
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
        SetLocalizedLabel(temporaryLogoLabel, temporaryLogoText, temporaryLogoText);
        SetLocalizedLabel(temporaryPressAnyKeyLabel, "按任意键开始", temporaryPressAnyKeyText);
        SetLocalizedLabel(temporaryLanguageLabel, "语言", temporaryLanguageText);
        SetLocalizedLabel(temporarySettingsLabel, "设置", temporarySettingsText);
        SetLocalizedLabel(temporaryQuitLabel, "退出", temporaryQuitText);
    }

    private static void SetLocalizedLabel(TextMeshProUGUI label, string chinese, string english)
    {
        if (label == null)
            return;

        string text = DuoCurtainLocalization.Text("boot.temporary", chinese, english);
        label.text = text;
        DuoCurtainLocalization.ApplyFont(label, text);
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
}
