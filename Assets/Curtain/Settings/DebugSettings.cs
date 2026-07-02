using UnityEngine;

namespace Curtain.Settings
{
    [CreateAssetMenu(menuName = "Curtain/Settings/Debug Settings", fileName = "DebugSettings")]
    public sealed class DebugSettings : ScriptableObject
    {
        [Header("Enemy")]
        public bool drawEnemyVision;
        public bool drawEnemySearchState;
        public bool drawAiState;

        [Header("World / Building")]
        public bool drawNavigation;
        public bool drawFootprints;
        public bool drawInteractionPoints;
        public bool drawOpenings;
        public bool drawOccluders;

        [Header("Logging")]
        public bool logEnemySpawns;
        public bool logStateChanges;
    }
}

