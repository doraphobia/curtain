#if UNITY_EDITOR
using UnityEngine;

namespace Curtain.Settings
{
    [CreateAssetMenu(menuName = "Curtain/Settings/Vision Settings", fileName = "VisionSettings")]
    public sealed class VisionSettings : ScriptableObject
    {
        [Header("Detection")]
        public bool useVisibilityWorld = true;
        public bool requireActualVisibilityPolygonContainment = true;

        [Header("Cone Sampling")]
        [Range(2, 512)] public int baseRayCount = 96;
        [Range(2, 1024)] public int maxRayCount = 384;
        [Range(0, 8)] public int edgeRefinementIterations = 2;
        [Min(0f)] public float edgeDistanceThreshold = 0.35f;

        [Header("Portals")]
        public bool requireOpenWindow = true;
        [Min(1)] public int windowVisionSampleCount = 5;
        [Min(0f)] public float windowVisionSamplePadding = 0.05f;

        [Header("Debug")]
        public bool debugLogDetectionSource;
    }
}

#endif

