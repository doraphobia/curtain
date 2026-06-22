using UnityEditor;
using UnityEngine;

namespace AE2Unity.Editor
{
    [FilePath("ProjectSettings/AE2UnitySettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class Ae2ShaderEditorSettings : ScriptableSingleton<Ae2ShaderEditorSettings>
    {
        [SerializeField] private bool autoGenerateOnImport = true;
        [SerializeField] private bool overwriteGeneratedAssets = true;
        [SerializeField] private bool bridgeReceiverEnabled = true;
        [SerializeField] private string defaultBridgeOutputPath = "Assets/AE2Unity/Exports";

        public bool AutoGenerateOnImport
        {
            get => autoGenerateOnImport;
            set
            {
                if (autoGenerateOnImport == value)
                {
                    return;
                }

                autoGenerateOnImport = value;
                Save(true);
            }
        }

        public bool OverwriteGeneratedAssets
        {
            get => overwriteGeneratedAssets;
            set
            {
                if (overwriteGeneratedAssets == value)
                {
                    return;
                }

                overwriteGeneratedAssets = value;
                Save(true);
            }
        }

        public bool BridgeReceiverEnabled
        {
            get => bridgeReceiverEnabled;
            set
            {
                if (bridgeReceiverEnabled == value)
                {
                    return;
                }

                bridgeReceiverEnabled = value;
                Save(true);
            }
        }

        public string DefaultBridgeOutputPath
        {
            get => string.IsNullOrWhiteSpace(defaultBridgeOutputPath)
                ? "Assets/AE2Unity/Exports"
                : defaultBridgeOutputPath;
            set
            {
                if (defaultBridgeOutputPath == value)
                {
                    return;
                }

                defaultBridgeOutputPath = value;
                Save(true);
            }
        }
    }
}
