#if UNITY_EDITOR
using UnityEngine;

namespace Curtain.Settings
{
    [CreateAssetMenu(menuName = "Curtain/Settings/Camera Settings", fileName = "CameraSettings")]
    public sealed class CameraSettings : ScriptableObject
    {
        [Header("Follow")]
        [Min(0f)] public float followSmoothTime = 0.28f;
        [Min(0.01f)] public float maxFollowSpeed = 24f;
        [Min(0f)] public float deadZoneRadius = 0.35f;
        [Min(0f)] public float lookAheadDistance = 0.75f;
        [Min(0f)] public float lookAheadSmoothTime = 0.18f;

        [Header("Overview")]
        [Min(0f)] public float overviewSmoothTime = 0.35f;
        [Min(0f)] public float overviewPadding = 1.5f;
        [Min(0.01f)] public float minOverviewOrthographicSize = 4f;
        [Min(0.01f)] public float maxOverviewOrthographicSize = 32f;

        [Header("Transition")]
        [Min(0f)] public float defaultTransitionDuration = 0.65f;
        public AnimationCurve transitionCurve;
    }
}

#endif

