using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class StageProgressSliderUI : MonoBehaviour
{
    [Header("References")]
    public StageCycleController stageController;
    public Slider daySlider;
    public Slider nightSlider;

    [Header("Stage Groups")]
    [Tooltip("这些阶段会计入白天进度条")]
    public List<string> dayStageIds = new List<string> { "DayTop", "DayBottom", "BeforeNight" };
    [Tooltip("这些阶段会计入夜晚进度条")]
    public List<string> nightStageIds = new List<string> { "Night" };

    void Start()
    {
        if (stageController == null)
            stageController = FindFirstObjectByType<StageCycleController>();

        UpdateSliders();
    }

    void Update()
    {
        UpdateSliders();
    }

    private void UpdateSliders()
    {
        if (stageController == null)
            return;

        float dayProgress = CalculateGroupProgress(dayStageIds);
        float nightProgress = CalculateGroupProgress(nightStageIds);

        ApplySlider(daySlider, dayProgress);
        ApplySlider(nightSlider, nightProgress);
    }

    private float CalculateGroupProgress(List<string> stageIds)
    {
        if (stageIds == null || stageIds.Count == 0 || stageController.stages == null || stageController.stages.Count == 0)
            return 0f;

        float totalDuration = 0f;
        float elapsedDuration = 0f;
        bool currentStageFoundInGroup = false;

        for (int i = 0; i < stageController.stages.Count; i++)
        {
            StageCycleController.StageDefinition stage = stageController.stages[i];
            if (stage == null || !stageIds.Contains(stage.id))
                continue;

            float stageDuration = Mathf.Max(0.01f, stage.duration);
            totalDuration += stageDuration;

            if (stage.id == stageController.CurrentStageId)
            {
                elapsedDuration += Mathf.Clamp(stageController.StageTimer, 0f, stageDuration);
                currentStageFoundInGroup = true;
                break;
            }

            elapsedDuration += stageDuration;
        }

        if (!currentStageFoundInGroup)
            return 0f;

        if (totalDuration <= 0f)
            return 0f;

        return Mathf.Clamp01(elapsedDuration / totalDuration);
    }

    private void ApplySlider(Slider slider, float progress)
    {
        if (slider == null)
            return;

        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = progress;
    }
}
