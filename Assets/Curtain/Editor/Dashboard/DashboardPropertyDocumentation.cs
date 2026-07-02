using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Curtain.Editor.Dashboard
{
    /// <summary>
    /// Optional Dashboard-only property descriptions.
    /// Keys use "SettingsTypeName.propertyName". Add entries here over time.
    /// </summary>
    internal static class DashboardPropertyDocumentation
    {
        private static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
        {
            ["SanitySettings.nightOutdoorDrainPerSecond"] =
                "Sanity lost every second while the player remains outdoors during nighttime.",
            ["SanitySettings.nightIndoorRecoveryPerSecond"] =
                "Sanity recovered every second while indoors during nighttime.",
            ["SanitySettings.dayIndoorRecoveryPerSecond"] =
                "Sanity recovered every second while indoors during daytime.",
            ["SanitySettings.dayOutdoorRecoveryPerSecond"] =
                "Sanity recovered every second while outdoors during daytime.",
            ["SanitySettings.enemyTouchDamage"] =
                "Sanity damage applied when an enemy touches the player.",
            ["SanitySettings.windowDetectionDamage"] =
                "Sanity damage applied when the player is detected through a window.",
            ["EnemySettings.moveSpeed"] =
                "Base movement speed for outside-room enemy pursuit.",
            ["VisionSettings.baseRayCount"] =
                "Initial number of rays used when sampling the vision cone.",
            ["DoorSettings.maxHealth"] =
                "Total health before the exterior door is breached.",
            ["CameraSettings.followSmoothTime"] =
                "Smooth damp time used while the camera follows the player.",
            ["FootprintSettings.lifetimeSeconds"] =
                "How long an enemy footprint remains visible before it is eligible to fade or disappear.",
            ["FootprintSettings.fadeSeconds"] =
                "Duration used when fading footprint visuals out.",
            ["FootprintSettings.spacing"] =
                "Minimum movement spacing between spawned enemy footprints.",
            ["EconomySettings.windowCost"] =
                "Currency cost used when buying or placing a window-related structure.",
            ["EconomySettings.doorCost"] =
                "Currency cost used when buying or placing a door-related structure.",
            ["EconomySettings.sanityRecoveryCost"] =
                "Currency cost for sanity recovery actions.",
            ["AccessibilitySettings.gameplayContrast"] =
                "Global readability multiplier reserved for gameplay-facing contrast controls.",
            ["DebugSettings.logEnemySpawns"] =
                "Logs enemy spawn decisions for debugging night enemy behavior.",
        };

        public static GUIContent BuildLabelContent(SerializedProperty property)
        {
            if (property == null)
                return GUIContent.none;

            return new GUIContent(property.displayName, ResolveTooltip(property));
        }

        public static string ResolveTooltip(SerializedProperty property)
        {
            if (property == null)
                return string.Empty;

            Object target = property.serializedObject != null
                ? property.serializedObject.targetObject
                : null;
            string typeName = target != null ? target.GetType().Name : string.Empty;
            string key = string.IsNullOrEmpty(typeName)
                ? property.name
                : typeName + "." + property.name;

            if (Descriptions.TryGetValue(key, out string description) && !string.IsNullOrWhiteSpace(description))
                return description;

            return property.tooltip ?? string.Empty;
        }

        public static string ResolveKey(SerializedProperty property)
        {
            if (property == null)
                return string.Empty;

            Object target = property.serializedObject != null
                ? property.serializedObject.targetObject
                : null;
            string typeName = target != null ? target.GetType().Name : string.Empty;
            return string.IsNullOrEmpty(typeName)
                ? property.name
                : typeName + "." + property.name;
        }

        public static bool TryGetDescription(string settingsTypeName, string propertyName, out string description)
        {
            description = null;
            if (string.IsNullOrEmpty(settingsTypeName) || string.IsNullOrEmpty(propertyName))
                return false;

            return Descriptions.TryGetValue(settingsTypeName + "." + propertyName, out description) &&
                   !string.IsNullOrWhiteSpace(description);
        }
    }
}
