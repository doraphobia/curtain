using System;
using UnityEngine;

[DisallowMultipleComponent]
public class StagePanelVisibility : MonoBehaviour
{
    [Header("References")]
    public StageCycleController stageController;
    public GameObject targetPanel;

    [Header("Stage")]
    [Tooltip("当当前阶段 id 等于这个值时显示 Panel")]
    public string visibleStageId = StageIds.BeforeNight;
    public bool ignoreCase = true;

    private bool lastVisibleState;

    void Start()
    {
        ApplyVisibility(forceUpdate: true);
    }

    void Update()
    {
        ApplyVisibility(forceUpdate: false);
    }

    private void ApplyVisibility(bool forceUpdate)
    {
        if (targetPanel == null)
            return;

        bool shouldShow = false;

        if (stageController != null && !string.IsNullOrEmpty(visibleStageId))
        {
            StringComparison comparison = ignoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            shouldShow = string.Equals(stageController.CurrentStageId, visibleStageId, comparison);
        }

        if (forceUpdate || shouldShow != lastVisibleState)
        {
            targetPanel.SetActive(shouldShow);
            lastVisibleState = shouldShow;
        }
    }
}
