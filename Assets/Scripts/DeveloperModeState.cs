using System;
using UnityEngine;

public static class DeveloperModeState
{
    public static bool IsEnabled { get; private set; }

    public static event Action<bool> Changed;

    private static int lastToggleFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        IsEnabled = false;
        Changed = null;
        lastToggleFrame = -1;
    }

    public static bool TryHandleHotkey()
    {
        if (lastToggleFrame == Time.frameCount)
            return false;

        bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (!shiftHeld || !Input.GetKeyDown(KeyCode.D))
            return false;

        lastToggleFrame = Time.frameCount;
        SetEnabled(!IsEnabled);
        return true;
    }

    public static void SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled)
            return;

        IsEnabled = enabled;
        Debug.Log(IsEnabled
            ? "[DeveloperMode] Enabled: shop purchases no longer spend currency."
            : "[DeveloperMode] Disabled: shop purchases use normal currency rules.");

        Changed?.Invoke(IsEnabled);
    }
}
