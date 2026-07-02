#if UNITY_EDITOR
using UnityEngine;

namespace Curtain.Settings
{
    [CreateAssetMenu(menuName = "Curtain/Settings/Economy Settings", fileName = "EconomySettings")]
    public sealed class EconomySettings : ScriptableObject
    {
        [Header("Costs")]
        [Min(0)] public int windowCost = 12;
        [Min(0)] public int doorCost = 14;
        [Min(0)] public int sanityRecoveryCost = 100;
        [Min(0)] public int repairCost = 50;
    }
}

#endif

