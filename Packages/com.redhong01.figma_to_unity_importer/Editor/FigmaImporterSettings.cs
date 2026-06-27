using UnityEditor;
using UnityEngine;

namespace FigmaImporter.Editor
{
    public class FigmaImporterSettings : ScriptableObject
    {
        private const string ClientCodeKey = "FigmaImporter.ClientCode";
        private const string StateKey = "FigmaImporter.State";
        private const string TokenKey = "FigmaImporter.Token";
        private const string UrlKey = "FigmaImporter.Url";
        private const string RendersPathKey = "FigmaImporter.RendersPath";
        private const string RootObjectPickerCanvasOnlyKey = "FigmaImporter.RootObjectPicker.CanvasOnly";
        private const string EnableTypographyAdapterKey = "FigmaImporter.Typography.EnableAdapter";
        private const string EnableTypographyScaleCorrectionKey = "FigmaImporter.Typography.EnableScaleCorrection";
        private const string EscapeTypographyInputTextKey = "FigmaImporter.Typography.EscapeInputText";
        private const string EnableTypographyDebugLogKey = "FigmaImporter.Typography.EnableDebugLog";
        private const string LegacyDefaultRendersPath = "FigmaImporter/Renders";
        private const string HiddenLocalDefaultRendersPath = "FigmaImporter/.Local/Renders";
        private const string LocalDefaultRendersPath = "FigmaImporter/_Local/Renders";

        [SerializeField] private string clientCode = string.Empty;
        [SerializeField] private string state = string.Empty;
        [SerializeField] private string token = string.Empty;
        [SerializeField] private string url = string.Empty;
        [SerializeField] private string rendersPath = LocalDefaultRendersPath;
        [SerializeField] private bool rootObjectPickerCanvasOnly = true;
        [SerializeField] private bool enableTypographyAdapter = true;
        [SerializeField] private bool enableTypographyScaleCorrection = true;
        [SerializeField] private bool escapeTypographyInputText = true;
        [SerializeField] private bool enableTypographyDebugLog = false;

        private static FigmaImporterSettings _instance;
        
        public string ClientCode
        {
            get => clientCode;
            set
            {
                clientCode = value ?? string.Empty;
                EditorPrefs.SetString(ClientCodeKey, clientCode);
            }
        }

        public string State
        {
            get => state;
            set
            {
                state = value ?? string.Empty;
                EditorPrefs.SetString(StateKey, state);
            }
        }

        public string Token
        {
            get => token;
            set
            {
                token = value ?? string.Empty;
                EditorPrefs.SetString(TokenKey, token);
            }
        }

        public string Url
        {
            get => url;
            set
            {
                url = value ?? string.Empty;
                EditorPrefs.SetString(UrlKey, url);
            }
        }

        public string RendersPath
        {
            get => rendersPath;
            set
            {
                rendersPath = FigmaPathUtils.NormalizeRendersFolder(value);
                EditorPrefs.SetString(RendersPathKey, rendersPath);
            }
        }

        public bool RootObjectPickerCanvasOnly
        {
            get => rootObjectPickerCanvasOnly;
            set
            {
                rootObjectPickerCanvasOnly = value;
                EditorPrefs.SetBool(RootObjectPickerCanvasOnlyKey, value);
            }
        }

        public bool EnableTypographyAdapter
        {
            get => enableTypographyAdapter;
            set
            {
                enableTypographyAdapter = value;
                EditorPrefs.SetBool(EnableTypographyAdapterKey, value);
            }
        }

        public bool EnableTypographyScaleCorrection
        {
            get => enableTypographyScaleCorrection;
            set
            {
                enableTypographyScaleCorrection = value;
                EditorPrefs.SetBool(EnableTypographyScaleCorrectionKey, value);
            }
        }

        public bool EscapeTypographyInputText
        {
            get => escapeTypographyInputText;
            set
            {
                escapeTypographyInputText = value;
                EditorPrefs.SetBool(EscapeTypographyInputTextKey, value);
            }
        }

        public bool EnableTypographyDebugLog
        {
            get => enableTypographyDebugLog;
            set
            {
                enableTypographyDebugLog = value;
                EditorPrefs.SetBool(EnableTypographyDebugLogKey, value);
            }
        }

        public static FigmaImporterSettings GetInstance()
        {
            if (_instance != null)
            {
                return _instance;
            }

            _instance = CreateInstance<FigmaImporterSettings>();
            _instance.hideFlags = HideFlags.HideAndDontSave;
            MigrateFromLegacyAssetIfNeeded();

            _instance.clientCode = EditorPrefs.GetString(ClientCodeKey, string.Empty);
            _instance.state = EditorPrefs.GetString(StateKey, string.Empty);
            _instance.token = EditorPrefs.GetString(TokenKey, string.Empty);
            _instance.url = EditorPrefs.GetString(UrlKey, string.Empty);
            var storedRendersPath = EditorPrefs.GetString(RendersPathKey, LocalDefaultRendersPath);
            var normalizedRendersPath = FigmaPathUtils.NormalizeRendersFolder(storedRendersPath);
            if (string.Equals(normalizedRendersPath, LegacyDefaultRendersPath, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedRendersPath, HiddenLocalDefaultRendersPath, System.StringComparison.OrdinalIgnoreCase))
            {
                normalizedRendersPath = LocalDefaultRendersPath;
                EditorPrefs.SetString(RendersPathKey, normalizedRendersPath);
            }

            _instance.rendersPath = normalizedRendersPath;
            _instance.rootObjectPickerCanvasOnly = EditorPrefs.GetBool(RootObjectPickerCanvasOnlyKey, true);
            _instance.enableTypographyAdapter = EditorPrefs.GetBool(EnableTypographyAdapterKey, true);
            _instance.enableTypographyScaleCorrection = EditorPrefs.GetBool(EnableTypographyScaleCorrectionKey, true);
            _instance.escapeTypographyInputText = EditorPrefs.GetBool(EscapeTypographyInputTextKey, true);
            _instance.enableTypographyDebugLog = EditorPrefs.GetBool(EnableTypographyDebugLogKey, false);
            return _instance;
        }

        private static void MigrateFromLegacyAssetIfNeeded()
        {
            if (EditorPrefs.HasKey(RendersPathKey))
            {
                return;
            }

            var assets = AssetDatabase.FindAssets("t:FigmaImporterSettings");
            if (assets == null || assets.Length == 0)
            {
                return;
            }

            var assetPath = AssetDatabase.GUIDToAssetPath(assets[0]);
            var legacySettings = AssetDatabase.LoadAssetAtPath<FigmaImporterSettings>(assetPath);
            if (legacySettings == null)
            {
                return;
            }

            if (!EditorPrefs.HasKey(RendersPathKey) && !string.IsNullOrEmpty(legacySettings.rendersPath))
            {
                EditorPrefs.SetString(RendersPathKey, FigmaPathUtils.NormalizeRendersFolder(legacySettings.rendersPath));
            }
        }
    }
}
