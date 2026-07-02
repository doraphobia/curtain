using UnityEngine;

namespace Curtain.Settings
{
    [CreateAssetMenu(menuName = "Curtain/Settings/Footprint Settings", fileName = "FootprintSettings")]
    public sealed class FootprintSettings : ScriptableObject
    {
        [Header("Lifetime")]
        [Min(0f)] public float lifetimeSeconds = 8f;
        [Min(0f)] public float fadeSeconds = 1.5f;

        [Header("Spacing")]
        [Min(0f)] public float spacing = 0.25f;
    }
}

