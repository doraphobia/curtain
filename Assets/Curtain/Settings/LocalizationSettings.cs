using UnityEngine;

namespace Curtain.Settings
{
    [CreateAssetMenu(menuName = "Curtain/Settings/Localization Settings", fileName = "LocalizationSettings")]
    public sealed class LocalizationSettings : ScriptableObject
    {
        [Header("Placeholder")]
        [Tooltip("Reserved for future localization tooling; not used yet.")]
        public bool reserved;
    }
}

