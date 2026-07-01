using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class StageCycleController : MonoBehaviour
{
    [Serializable]
    public class StageDefinition
    {
        public string id = "Day";

        [Min(0.01f)]
        public float duration = 5f;
    }

    [Header("Stages")]
    [Tooltip("按顺序循环播放的阶段列表，例如：白天上 -> 白天下 -> 黑夜")]
    public List<StageDefinition> stages = new List<StageDefinition>
    {
        new StageDefinition { id = StageIds.DayTop, duration = 5f },
        new StageDefinition { id = StageIds.DayBottom, duration = 5f },
        new StageDefinition { id = StageIds.Night, duration = 8f }
    };

    [Tooltip("在当前阶段结束前，提前多久开始向下一个阶段过渡")]
    [Min(0f)]
    public float transitionDuration = 1f;

    [Header("Start")]
    [Tooltip("开始时使用第几个阶段")]
    [Min(0)]
    public int startStageIndex = 0;

    [Header("Simulation Speed")]
    [Min(0f)]
    public float simulationSpeedMultiplier = 1f;

    [Header("Night Audio")]
    public AudioSource nightLoopAudioSource;
    public AudioClip nightLoopClip;
    [Range(0f, 1f)]
    public float nightLoopVolume = 1f;

    [Header("Day Night UI")]
    public Image dayNightImage;
    public Sprite daySprite;
    public Sprite nightSprite;

    [Header("Settlement")]
    [Tooltip("完成多少个「从最后一阶段回到第一阶段」的回合后进入结算（默认流程下即过了多少晚）")]
    [Min(1)]
    public int nightsRequiredForSettlement = 10;

    [Tooltip("结算场景名（须与 File → Build Settings 里添加的场景名一致，例如 REsult）")]
    public string settlementSceneName = "REsult";

    private int currentStageIndex;
    private float stageTimer;
    private bool paused;
    private bool wasNightLastFrame;
    private bool hasInitializedRunStats;
    private bool settlementLoadTriggered;

    public string CurrentStageId => GetStageId(currentStageIndex);
    public string NextStageId => GetStageId(GetNextStageIndex());
    public float StageTimer => stageTimer;
    public float CurrentStageDuration => GetStageDuration(currentStageIndex);
    public bool IsPaused => paused;
    public bool IsNight => StageIds.IsNight(CurrentStageId);

    public bool IsTransitioning
    {
        get
        {
            float duration = CurrentStageDuration;
            float transition = Mathf.Clamp(transitionDuration, 0f, duration);
            return transition > 0f && stageTimer >= duration - transition;
        }
    }

    public float TransitionProgress
    {
        get
        {
            float duration = CurrentStageDuration;
            float transition = Mathf.Clamp(transitionDuration, 0f, duration);

            if (transition <= 0f)
                return 0f;

            float transitionStart = duration - transition;
            return Mathf.Clamp01(Mathf.InverseLerp(transitionStart, duration, stageTimer));
        }
    }

    void Start()
    {
        InitializeRunStats();
        ResetCycle();
        SetupNightAudioSource();
        UpdateNightAudio(false);
        UpdateDayNightImage();
    }

    void Update()
    {
        if (stages == null || stages.Count == 0)
            return;

        if (!paused && !PauseManager.IsGamePaused)
        {
            stageTimer += Time.deltaTime * Mathf.Max(0f, simulationSpeedMultiplier);

            float duration = CurrentStageDuration;
            while (stageTimer >= duration)
            {
                stageTimer -= duration;
                int previousStageIndex = currentStageIndex;
                currentStageIndex = GetNextStageIndex();
                HandleStageAdvanced(previousStageIndex, currentStageIndex);
                duration = CurrentStageDuration;
            }
        }

        UpdateNightAudio(false);
        UpdateDayNightImage();
    }

    void OnDisable()
    {
        UpdateNightAudio(true);
    }

    public void ResetCycle()
    {
        if (stages == null || stages.Count == 0)
        {
            currentStageIndex = 0;
            stageTimer = 0f;
            return;
        }

        currentStageIndex = Mathf.Clamp(startStageIndex, 0, stages.Count - 1);
        stageTimer = 0f;
        wasNightLastFrame = false;
    }

    public int GetCurrentStageIndex()
    {
        return currentStageIndex;
    }

    public void SetPaused(bool shouldPause)
    {
        paused = shouldPause;
    }

    public void SetSimulationSpeedMultiplier(float multiplier)
    {
        simulationSpeedMultiplier = Mathf.Max(0f, multiplier);
    }

    public void ResetSimulationSpeedMultiplier()
    {
        simulationSpeedMultiplier = 1f;
    }

    public void SetStageTime(int stageIndex, float timer)
    {
        if (stages == null || stages.Count == 0)
        {
            currentStageIndex = 0;
            stageTimer = 0f;
            return;
        }

        currentStageIndex = Mathf.Clamp(stageIndex, 0, stages.Count - 1);
        stageTimer = Mathf.Clamp(timer, 0f, GetStageDuration(currentStageIndex));
        UpdateNightAudio(false);
        UpdateDayNightImage();
    }

    private int GetNextStageIndex()
    {
        if (stages == null || stages.Count == 0)
            return 0;

        return (currentStageIndex + 1) % stages.Count;
    }

    private string GetStageId(int index)
    {
        if (stages == null || stages.Count == 0)
            return string.Empty;

        int safeIndex = Mathf.Clamp(index, 0, stages.Count - 1);
        return stages[safeIndex].id ?? string.Empty;
    }

    private float GetStageDuration(int index)
    {
        if (stages == null || stages.Count == 0)
            return 0.01f;

        int safeIndex = Mathf.Clamp(index, 0, stages.Count - 1);
        return Mathf.Max(0.01f, stages[safeIndex].duration);
    }

    private void SetupNightAudioSource()
    {
        if (nightLoopAudioSource == null)
            nightLoopAudioSource = GetComponent<AudioSource>();

        if (nightLoopAudioSource == null)
            nightLoopAudioSource = gameObject.AddComponent<AudioSource>();

        nightLoopAudioSource.loop = true;
        nightLoopAudioSource.playOnAwake = false;
        nightLoopAudioSource.clip = nightLoopClip;
        nightLoopAudioSource.volume = nightLoopVolume;
    }

    private void UpdateNightAudio(bool forceStop)
    {
        if (nightLoopAudioSource == null)
            return;

        nightLoopAudioSource.volume = nightLoopVolume;
        if (nightLoopAudioSource.clip != nightLoopClip)
            nightLoopAudioSource.clip = nightLoopClip;

        bool isNight = !forceStop && IsNight;

        if (isNight && !wasNightLastFrame && nightLoopClip != null)
        {
            nightLoopAudioSource.Stop();
            nightLoopAudioSource.Play();
        }
        else if (!isNight && wasNightLastFrame && nightLoopAudioSource.isPlaying)
        {
            nightLoopAudioSource.Stop();
        }

        wasNightLastFrame = isNight;
    }

    private void InitializeRunStats()
    {
        if (hasInitializedRunStats)
            return;

        GameRunStats.Instance.ResetRun();
        hasInitializedRunStats = true;
    }

    private void HandleStageAdvanced(int previousIndex, int newIndex)
    {
        if (stages == null || stages.Count == 0)
            return;

        bool wrappedToStart = previousIndex == stages.Count - 1 && newIndex == 0;
        if (wrappedToStart)
        {
            GameRunStats.Instance.RecordCompletedDay();
            TryLoadSettlementAfterEnoughNights();
        }
    }

    private void TryLoadSettlementAfterEnoughNights()
    {
        if (settlementLoadTriggered)
            return;

        if (string.IsNullOrWhiteSpace(settlementSceneName))
            return;

        if (GameRunStats.Instance.DaysSurvived < nightsRequiredForSettlement)
            return;

        settlementLoadTriggered = true;
        SetPaused(true);
        SceneManager.LoadScene(settlementSceneName.Trim());
    }

    private void UpdateDayNightImage()
    {
        if (dayNightImage == null)
            return;

        bool isNight = IsNight;
        Sprite targetSprite = isNight ? nightSprite : daySprite;

        if (dayNightImage.sprite != targetSprite)
            dayNightImage.sprite = targetSprite;
    }
}
