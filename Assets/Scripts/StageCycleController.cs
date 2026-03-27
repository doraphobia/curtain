using System;
using System.Collections.Generic;
using UnityEngine;

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
        new StageDefinition { id = "DayTop", duration = 5f },
        new StageDefinition { id = "DayBottom", duration = 5f },
        new StageDefinition { id = "Night", duration = 8f }
    };

    [Tooltip("在当前阶段结束前，提前多久开始向下一个阶段过渡")]
    [Min(0f)]
    public float transitionDuration = 1f;

    [Header("Start")]
    [Tooltip("开始时使用第几个阶段")]
    [Min(0)]
    public int startStageIndex = 0;

    private int currentStageIndex;
    private float stageTimer;
    private bool paused;

    public string CurrentStageId => GetStageId(currentStageIndex);
    public string NextStageId => GetStageId(GetNextStageIndex());
    public float StageTimer => stageTimer;
    public float CurrentStageDuration => GetStageDuration(currentStageIndex);
    public bool IsPaused => paused;

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
        ResetCycle();
    }

    void Update()
    {
        if (stages == null || stages.Count == 0)
            return;

        if (paused)
            return;

        stageTimer += Time.deltaTime;

        float duration = CurrentStageDuration;
        while (stageTimer >= duration)
        {
            stageTimer -= duration;
            currentStageIndex = GetNextStageIndex();
            duration = CurrentStageDuration;
        }
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
    }

    public int GetCurrentStageIndex()
    {
        return currentStageIndex;
    }

    public void SetPaused(bool shouldPause)
    {
        paused = shouldPause;
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
}
