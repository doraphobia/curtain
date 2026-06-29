using System.Collections.Generic;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class GlobalHoverScrollToggle : MonoBehaviour
{
    [Header("In-game mode indicator (optional)")]
    [Tooltip("Shown while global scroll mode is ON (wheel affects all curtains).")]
    public TextMeshProUGUI indicatorGlobalOn;
    [Tooltip("Shown while global scroll mode is OFF (hover per curtain).")]
    public TextMeshProUGUI indicatorGlobalOff;

    private readonly Dictionary<HoverScrollColorLerp2D, float> originalProgress = new Dictionary<HoverScrollColorLerp2D, float>();
    private HoverScrollColorLerp2D[] cachedTargets = System.Array.Empty<HoverScrollColorLerp2D>();
    private bool isGlobalModeActive;

    public bool IsGlobalModeActive => isGlobalModeActive;

    void Start()
    {
        RefreshModeIndicator();
    }

    void Update()
    {
        if (!isGlobalModeActive)
            return;

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) < 0.0001f)
            return;

        for (int i = 0; i < cachedTargets.Length; i++)
        {
            HoverScrollColorLerp2D target = cachedTargets[i];
            if (target != null)
                target.ApplyScrollDelta(scroll);
        }
    }

    public void ToggleGlobalMode()
    {
        if (isGlobalModeActive)
            DisableGlobalModeAndRestore();
        else
            EnableGlobalMode();
    }

    public void EnableGlobalMode()
    {
        if (isGlobalModeActive)
            return;

        cachedTargets = WindowQueryUtility.FindAllWindows();
        originalProgress.Clear();

        for (int i = 0; i < cachedTargets.Length; i++)
        {
            HoverScrollColorLerp2D target = cachedTargets[i];
            if (target == null)
                continue;

            originalProgress[target] = target.ColorProgress;
            target.allowLocalHoverInput = false;
        }

        isGlobalModeActive = true;
        RefreshModeIndicator();
    }

    public void DisableGlobalModeAndRestore()
    {
        if (!isGlobalModeActive)
            return;

        for (int i = 0; i < cachedTargets.Length; i++)
        {
            HoverScrollColorLerp2D target = cachedTargets[i];
            if (target == null)
                continue;

            target.allowLocalHoverInput = true;

            if (originalProgress.TryGetValue(target, out float progress))
                target.SetProgress(progress);
        }

        originalProgress.Clear();
        cachedTargets = System.Array.Empty<HoverScrollColorLerp2D>();
        isGlobalModeActive = false;
        RefreshModeIndicator();
    }

    void RefreshModeIndicator()
    {
        if (indicatorGlobalOn != null)
            indicatorGlobalOn.gameObject.SetActive(isGlobalModeActive);
        if (indicatorGlobalOff != null)
            indicatorGlobalOff.gameObject.SetActive(!isGlobalModeActive);
    }
}
