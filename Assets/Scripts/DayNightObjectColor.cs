using UnityEngine;
using System;
using System.Collections.Generic;

[RequireComponent(typeof(SpriteRenderer))]
public class DayNightObjectColor : MonoBehaviour
{
    [Serializable]
    public class StageVisual
    {
        public string stageId = "Day";
        public Color color = Color.white;
        public GameObject[] activeObjects;
    }

    [Header("Target")]
    public SpriteRenderer targetRenderer;

    [Header("Stage Source")]
    public StageCycleController stageController;

    [Header("Fallback Colors")]
    public Color dayColor = Color.white;
    public Color nightColor = Color.black;

    [Header("Fallback Timing (seconds)")]
    [Tooltip("如果没有绑定 StageCycleController，就继续使用旧版白天/黑夜循环")]
    public float cycleDuration = 10f;

    [Tooltip("如果没有绑定 StageCycleController，就使用这个渐变时长")]
    public float transitionDuration = 1f;

    [Header("Fallback Day Objects")]
    public GameObject dayObjectA;
    public GameObject dayObjectB;

    [Header("Stage Visuals")]
    [Tooltip("给每个阶段配置颜色和要显示的物体；stageId 要和 StageCycleController 里的 id 一致")]
    public List<StageVisual> stageVisuals = new List<StageVisual>
    {
        new StageVisual { stageId = "DayTop", color = Color.white },
        new StageVisual { stageId = "DayBottom", color = new Color(0.85f, 0.85f, 0.85f, 1f) },
        new StageVisual { stageId = "Night", color = Color.black }
    };

    private float timer;
    private bool isDay = true;

    void Start()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponent<SpriteRenderer>();

        if (stageController != null)
        {
            ApplyStageVisualImmediate(stageController.CurrentStageId);
        }
        else
        {
            ApplyColorImmediate(dayColor);
            timer = 0f;
            isDay = true;
            UpdateFallbackDayObjects();
        }
    }

    void Update()
    {
        if (targetRenderer == null) return;

        if (stageController != null)
        {
            UpdateFromStageController();
            return;
        }

        UpdateFallbackCycle();
    }

    void UpdateFromStageController()
    {
        StageVisual currentVisual = FindStageVisual(stageController.CurrentStageId);
        StageVisual nextVisual = FindStageVisual(stageController.NextStageId);

        Color currentColor = currentVisual != null ? currentVisual.color : targetRenderer.color;
        Color nextColor = nextVisual != null ? nextVisual.color : currentColor;

        if (stageController.IsTransitioning)
        {
            targetRenderer.color = Color.Lerp(currentColor, nextColor, stageController.TransitionProgress);
        }
        else
        {
            ApplyColorImmediate(currentColor);
        }

        ApplyActiveObjects(currentVisual);
    }

    void UpdateFallbackCycle()
    {
        float cd = Mathf.Max(0.01f, cycleDuration);
        float td = Mathf.Clamp(transitionDuration, 0f, cd);

        timer += Time.deltaTime;

        if (timer >= cd)
        {
            timer -= cd;
            isDay = !isDay;
        }

        Color holdColor = isDay ? dayColor : nightColor;
        Color nextColor = isDay ? nightColor : dayColor;

        float transitionStart = cd - td;

        if (td <= 0f || timer < transitionStart)
        {
            ApplyColorImmediate(holdColor);
        }
        else
        {
            float t = Mathf.InverseLerp(transitionStart, cd, timer);
            targetRenderer.color = Color.Lerp(holdColor, nextColor, t);
        }

        UpdateFallbackDayObjects();
    }

    void UpdateFallbackDayObjects()
    {
        if (isDay)
        {
            float halfTime = cycleDuration * 0.5f;

            if (timer < halfTime)
            {
                if (dayObjectA != null) dayObjectA.SetActive(true);
                if (dayObjectB != null) dayObjectB.SetActive(false);
            }
            else
            {
                if (dayObjectA != null) dayObjectA.SetActive(false);
                if (dayObjectB != null) dayObjectB.SetActive(true);
            }
        }
        else
        {
            if (dayObjectA != null) dayObjectA.SetActive(false);
            if (dayObjectB != null) dayObjectB.SetActive(false);
        }
    }

    void ApplyStageVisualImmediate(string stageId)
    {
        StageVisual visual = FindStageVisual(stageId);
        if (visual == null)
            return;

        ApplyColorImmediate(visual.color);
        ApplyActiveObjects(visual);
    }

    StageVisual FindStageVisual(string stageId)
    {
        if (stageVisuals == null)
            return null;

        for (int i = 0; i < stageVisuals.Count; i++)
        {
            StageVisual visual = stageVisuals[i];
            if (visual != null && string.Equals(visual.stageId, stageId, StringComparison.OrdinalIgnoreCase))
                return visual;
        }

        return null;
    }

    void ApplyActiveObjects(StageVisual activeVisual)
    {
        if (stageVisuals == null)
            return;

        for (int i = 0; i < stageVisuals.Count; i++)
        {
            StageVisual visual = stageVisuals[i];
            if (visual == null || visual.activeObjects == null)
                continue;

            bool shouldBeActive = ReferenceEquals(visual, activeVisual);

            for (int j = 0; j < visual.activeObjects.Length; j++)
            {
                GameObject go = visual.activeObjects[j];
                if (go != null)
                    go.SetActive(shouldBeActive);
            }
        }
    }

    void ApplyColorImmediate(Color c)
    {
        if (targetRenderer.color != c)
            targetRenderer.color = c;
    }
}
