using System;
using System.Collections.Generic;
using UnityEngine;

public class DayNightCameraWeather : MonoBehaviour
{
    [Serializable]
    public class StageCameraColor
    {
        public string stageId = "Day";
        public Color color = Color.white;
    }

    [Header("Target")]
    public Camera targetCamera;

    [Header("Stage Source")]
    public StageCycleController stageController;

    [Header("Colors")]
    public Color dayColor = Color.white;
    public Color nightColor = Color.black;
    public List<StageCameraColor> stageColors = new List<StageCameraColor>
    {
        new StageCameraColor { stageId = StageIds.DayTop, color = new Color(0.83f, 0.83f, 0.83f, 1f) },
        new StageCameraColor { stageId = StageIds.DayBottom, color = new Color(0.61f, 0.61f, 0.61f, 1f) },
        new StageCameraColor { stageId = StageIds.BeforeNight, color = new Color(0.18f, 0.18f, 0.2f, 1f) },
        new StageCameraColor { stageId = StageIds.Night, color = Color.black }
    };

    [Header("Timing (seconds)")]
    [Tooltip("每次白天/黑夜状态持续多久（包含渐变时间）")]
    public float cycleDuration = 10f;

    [Tooltip("在切换前多少秒开始均匀渐变")]
    public float transitionDuration = 1f;

    private float timer = 0f;
    private bool isDay = true;

    void Start()
    {
        if (targetCamera == null) targetCamera = Camera.main;

        if (stageController == null)
            stageController = FindFirstObjectByType<StageCycleController>();

        if (stageController != null)
            ApplyColorImmediate(ResolveStageColor(stageController.CurrentStageId, dayColor));
        else
            ApplyColorImmediate(dayColor);

        timer = 0f;
        isDay = true;
    }

    void Update()
    {
        if (targetCamera == null) return;

        if (stageController != null)
        {
            UpdateFromStageController();
            return;
        }

        UpdateFallbackCycle();
    }

    private void UpdateFromStageController()
    {
        Color currentColor = ResolveStageColor(stageController.CurrentStageId, targetCamera.backgroundColor);
        Color nextColor = ResolveStageColor(stageController.NextStageId, currentColor);

        if (stageController.IsTransitioning)
            targetCamera.backgroundColor = Color.Lerp(currentColor, nextColor, stageController.TransitionProgress);
        else
            ApplyColorImmediate(currentColor);
    }

    private void UpdateFallbackCycle()
    {
        // 防呆：避免除以0或负数
        float cd = Mathf.Max(0.01f, cycleDuration);
        float td = Mathf.Clamp(transitionDuration, 0f, cd);

        timer += Time.deltaTime;

        // 循环计时
        if (timer >= cd)
        {
            timer -= cd;
            isDay = !isDay; // 切换状态
        }

        // 当前状态下：我们“将要切换到”的目标颜色
        // 例如 isDay=true 表示当前阶段是白天，快结束时会切去夜晚
        Color holdColor = isDay ? dayColor : nightColor;
        Color nextColor = isDay ? nightColor : dayColor;

        // 前 (cd - td) 秒保持不变；最后 td 秒线性渐变到 nextColor
        float transitionStart = cd - td;

        if (td <= 0f || timer < transitionStart)
        {
            // 不在转换窗口：直接保持当前颜色
            ApplyColorImmediate(holdColor);
        }
        else
        {
            // 在最后 td 秒：均匀变色到目标
            float t = Mathf.InverseLerp(transitionStart, cd, timer); // 0->1
            targetCamera.backgroundColor = Color.Lerp(holdColor, nextColor, t);
        }
    }

    private Color ResolveStageColor(string stageId, Color fallback)
    {
        if (stageColors == null)
            return fallback;

        for (int i = 0; i < stageColors.Count; i++)
        {
            StageCameraColor stageColor = stageColors[i];
            if (stageColor != null && StageIds.Matches(stageColor.stageId, stageId))
                return stageColor.color;
        }

        return fallback;
    }

    private void ApplyColorImmediate(Color c)
    {
        // 避免每帧重复写入导致一些管线/后处理出现抖动（可选优化）
        if (targetCamera.backgroundColor != c)
            targetCamera.backgroundColor = c;
    }
}
