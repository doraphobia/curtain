using System.Collections;
using UnityEngine;
using UnityEngine.Events;

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
    [SerializeField] private bool startInBootWorld;
    [SerializeField] private bool listenForAnyInput = true;

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

    public BootWorldState CurrentState { get; private set; } = BootWorldState.ApplicationBoot;
    public bool IsBootWorldActive => CurrentState == BootWorldState.BootWorld;
    public bool IsTransitioningToGameplay => CurrentState == BootWorldState.GameplayTransition;
    public bool IsGameplayActive => CurrentState == BootWorldState.Gameplay;

    private void Start()
    {
        if (stageController == null)
            stageController = FindFirstObjectByType<StageCycleController>();

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

        if (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
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
}
