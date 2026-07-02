using UnityEngine;

namespace Curtain.Settings
{
    [CreateAssetMenu(menuName = "Curtain/Settings/Build Archive Settings", fileName = "BuildArchiveSettings")]
    public sealed class BuildArchiveSettings : ScriptableObject
    {
        [Min(1)]
        public int maxArchivesPerPlatform = 5;
    }
}
