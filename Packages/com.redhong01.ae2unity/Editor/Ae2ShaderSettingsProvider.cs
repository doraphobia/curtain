using UnityEditor;

namespace AE2Unity.Editor
{
    internal static class Ae2ShaderSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Project/AE2Unity", SettingsScope.Project)
            {
                label = "AE2Unity",
                guiHandler = _ =>
                {
                    var settings = Ae2ShaderEditorSettings.instance;
                    settings.AutoGenerateOnImport = EditorGUILayout.Toggle("Auto Generate On Import", settings.AutoGenerateOnImport);
                    settings.OverwriteGeneratedAssets = EditorGUILayout.Toggle("Overwrite Generated Assets", settings.OverwriteGeneratedAssets);
                    settings.BridgeReceiverEnabled = EditorGUILayout.Toggle("AEBridge Receiver Enabled", settings.BridgeReceiverEnabled);
                    settings.DefaultBridgeOutputPath = EditorGUILayout.TextField("Bridge Output Path", settings.DefaultBridgeOutputPath);
                }
            };
        }
    }
}
