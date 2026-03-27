using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class GlobalHoverScrollToggle : MonoBehaviour
{
    private readonly Dictionary<HoverScrollColorLerp2D, float> originalProgress = new Dictionary<HoverScrollColorLerp2D, float>();
    private HoverScrollColorLerp2D[] cachedTargets = System.Array.Empty<HoverScrollColorLerp2D>();
    private bool isGlobalModeActive;

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

        cachedTargets = FindObjectsByType<HoverScrollColorLerp2D>(FindObjectsSortMode.None);
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
    }
}
