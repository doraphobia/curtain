using System;
using UnityEngine;

public enum FootprintSide
{
    Left,
    Right
}

public enum EnemyTraceState
{
    NormalMoving,
    Watching,
    TargetingDoor,
    BreakingDoor,
    ChasingPlayer,
    Attacking,
    Disabled
}

/// <summary>
/// Shared visual/animation settings passed to each <see cref="FootprintInstance"/>.
/// </summary>
[Serializable]
public class FootprintVisualProfile
{
    public float fadeInDuration = 0.12f;
    public AnimationCurve fadeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    public float residualDecayDuration = 0.25f;
    public AnimationCurve residualDecayCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.75f);
    public float fadeOutDuration = 0.6f;
    public AnimationCurve fadeOutCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    public float latestAlpha = 1f;
    public float residualAlphaMultiplier = 0.78f;
    public float minimumResidualAlpha = 0.05f;
    public Color normalFootprintColor = Color.white;
    public Color breakingDoorFootprintColor = Color.red;
}
