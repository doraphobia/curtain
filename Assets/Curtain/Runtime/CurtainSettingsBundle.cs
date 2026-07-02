#if UNITY_EDITOR
using UnityEngine;

namespace Curtain.Settings
{
    [CreateAssetMenu(menuName = "Curtain/Settings/Settings Bundle", fileName = "CurtainSettingsBundle")]
    public sealed class CurtainSettingsBundle : ScriptableObject
    {
        public EnemySettings enemy;
        public VisionSettings vision;
        public DoorSettings door;
        public CameraSettings camera;
        public SanitySettings sanity;
        public EconomySettings economy;
        public FootprintSettings footprint;
        public AccessibilitySettings accessibility;
        public LocalizationSettings localization;
        public DebugSettings debug;
    }
}

#endif
