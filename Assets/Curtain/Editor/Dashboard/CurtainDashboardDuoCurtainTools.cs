#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Curtain.Editor.Dashboard
{
    internal static class CurtainDashboardDuoCurtainTools
    {
        internal readonly struct ToolEntry
        {
            public readonly string Group;
            public readonly string Label;
            public readonly string MenuPath;
            public readonly string Description;

            public ToolEntry(string group, string label, string menuPath, string description = null)
            {
                Group = group;
                Label = label;
                MenuPath = menuPath;
                Description = description;
            }
        }

        private static readonly ToolEntry[] Entries =
        {
            new ToolEntry("Build", "Build Packager", "Tools/Duo Curtain/Build/Build Packager", "Open build settings window."),
            new ToolEntry("Build", "Build All Platforms", "Tools/Duo Curtain/Build/Build All Platforms", "Mac, Windows, WebGL → Builds/."),
            new ToolEntry("Build", "Build Mac", "Tools/Duo Curtain/Build/Build Mac", "Builds/Curtain_Mac/Curtain_Mac.app"),
            new ToolEntry("Build", "Build Windows", "Tools/Duo Curtain/Build/Build Windows", "Builds/Curtain_Windows/Curtain_Windows.exe"),
            new ToolEntry("Build", "Build WebGL", "Tools/Duo Curtain/Build/Build WebGL", "Builds/Curtain_Web/"),

            new ToolEntry("Curtain", "Ensure Settings Bundle", "Tools/Curtain/Ensure Settings Bundle", "Assets/Curtain/Settings/CurtainSettingsBundle.asset"),

            new ToolEntry("Auto Reload", "Toggle Enabled", "Tools/Duo Curtain/Auto Reload/Enabled"),
            new ToolEntry("Auto Reload", "Reload Project Now", "Tools/Duo Curtain/Auto Reload/Reload Project Now"),

            new ToolEntry("Compile", "Toggle Auto Compile", "Tools/Duo Curtain/Compile/Auto Compile Enabled"),
            new ToolEntry("Compile", "Compile Now", "Tools/Duo Curtain/Compile/Compile Now"),

            new ToolEntry("DOTween", "Enable Editor Tools", "Tools/Duo Curtain/DOTween/Enable Editor Tools And Open Panel"),
            new ToolEntry("DOTween", "Silence Editor Tools", "Tools/Duo Curtain/DOTween/Silence Editor Tools"),

            new ToolEntry("Foley", "Install Foley System", "Tools/Duo Curtain/Foley/Install Foley System"),

            new ToolEntry("Fusion", "Rebuild Bayon TMP Font", "Tools/Duo Curtain/Fusion/Rebuild Bayon TMP Font Asset"),
            new ToolEntry("Fusion", "Rebuild CJK UI TMP Font", "Tools/Duo Curtain/Fusion/Rebuild CJK UI TMP Font Asset"),

            new ToolEntry("Grid", "Normalize Integer Tile Grid", "Tools/Duo Curtain/Grid/Normalize Integer Tile Grid"),

            new ToolEntry("Runtime Tile Mesh", "Fusion Integrity Monitor", "Tools/Duo Curtain/Runtime Tile Mesh/Fusion Integrity Monitor"),
            new ToolEntry("Runtime Tile Mesh", "Run Self Test", "Tools/Duo Curtain/Runtime Tile Mesh/Run Self Test"),
            new ToolEntry("Runtime Tile Mesh", "Create RedScene", "Tools/Duo Curtain/Runtime Tile Mesh/Create RedScene"),

            new ToolEntry("Audio", "Ensure Master Mixer", "Tools/Duo Curtain/Audio/Ensure Master Mixer"),

            new ToolEntry("Branding", "Select App Icon Set", "Tools/Duo Curtain/Branding/Select App Icon Set"),
            new ToolEntry("Branding", "Apply App Icons", "Tools/Duo Curtain/Branding/Apply App Icons (Mac+Windows+WebGL)")
        };

        internal static void DrawToolsPage()
        {
            EditorGUILayout.HelpBox(
                "Runs the same editor actions as Tools / Duo Curtain. Build steps log to the Console; " +
                "successful publishes land under Builds/Curtain_Mac, Builds/Curtain_Windows, and Builds/Curtain_Web.",
                MessageType.Info);

            string currentGroup = null;
            for (int i = 0; i < Entries.Length; i++)
            {
                ToolEntry entry = Entries[i];
                if (!string.Equals(entry.Group, currentGroup, System.StringComparison.Ordinal))
                {
                    currentGroup = entry.Group;
                    GUILayout.Space(10f);
                    GUILayout.Label(currentGroup, EditorStyles.boldLabel);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(entry.Label, GUILayout.Width(260f)))
                        EditorApplication.ExecuteMenuItem(entry.MenuPath);

                    if (!string.IsNullOrEmpty(entry.Description))
                        GUILayout.Label(entry.Description, EditorStyles.miniLabel);
                }
            }

            GUILayout.Space(16f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reveal Builds Folder", GUILayout.Width(200f)))
                {
                    string buildsPath = Path.GetFullPath("Builds");
                    if (!Directory.Exists(buildsPath))
                        Directory.CreateDirectory(buildsPath);
                    EditorUtility.RevealInFinder(buildsPath);
                }

                if (GUILayout.Button("Reveal Build Staging", GUILayout.Width(200f)))
                {
                    string stagingPath = Path.GetFullPath("Builds/.BuildStaging");
                    if (!Directory.Exists(stagingPath))
                        Directory.CreateDirectory(stagingPath);
                    EditorUtility.RevealInFinder(stagingPath);
                }
            }
        }
    }
}
#endif
