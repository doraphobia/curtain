using System;
using UnityEngine;

[Serializable]
public sealed class DuoCurtainSettingsData
{
    [Range(0f, 1f)]
    public float masterVolume = 0.8f;

    public int resolutionWidth = 0;
    public int resolutionHeight = 0;

    public FullScreenMode fullScreenMode = FullScreenMode.FullScreenWindow;
}

