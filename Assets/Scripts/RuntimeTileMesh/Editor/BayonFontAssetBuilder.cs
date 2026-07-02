#if UNITY_EDITOR
using System;
using System.IO;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace DuoCurtain.RuntimeTileMesh.Editor
{
    [InitializeOnLoad]
    public static class BayonFontAssetBuilder
    {
        public const string SourceFontPath = "Assets/Fusion/Resources/Fonts/Bayon-Regular.ttf";
        public const string FontAssetPath = "Assets/Fusion/Resources/Fonts/Bayon-Regular SDF.asset";

        private const string OverlayCharacterSet =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 -_\n\r×";

        static BayonFontAssetBuilder()
        {
            EditorApplication.delayCall += () => EnsureFontAsset();
        }

        [MenuItem("Tools/Duo Curtain/Fusion/Rebuild Bayon TMP Font Asset")]
        public static void RebuildFontAsset()
        {
            DeleteFontAssetPreservingGuid();
            EnsureFontAsset(forceRebuild: true);
        }

        public static TMP_FontAsset EnsureFontAsset(bool forceRebuild = false)
        {
            if (!EnsureSourceFontImported())
                return null;

            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (!forceRebuild && IsFontAssetUsable(existing))
            {
                ConfigureForPlayerBuild(existing);
                return existing;
            }

            if (!forceRebuild && existing != null && TryRepairFontAsset(existing))
                return existing;

            if (existing != null)
                DeleteFontAssetPreservingGuid();

            Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (sourceFont == null)
            {
                Debug.LogError("[BayonFontAssetBuilder] Bayon-Regular.ttf is missing at " + SourceFontPath + ".");
                return null;
            }

            TMP_FontAsset fontAsset = CreatePopulatedFontAsset(sourceFont);
            if (fontAsset == null)
                return null;

            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);
            RestoreFontAssetGuid();
            PersistSubAssets(fontAsset);
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(
                FontAssetPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            TMP_FontAsset saved = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (!IsFontAssetUsable(saved))
            {
                Debug.LogError(
                    "[BayonFontAssetBuilder] Bayon TMP font asset was saved but is still unusable. " +
                    "Use Tools/Duo Curtain/Fusion/Rebuild Bayon TMP Font Asset.");
                return saved;
            }

            Debug.Log("[BayonFontAssetBuilder] Created Bayon TMP font asset at " + FontAssetPath + ".");
            return saved;
        }

        private static TMP_FontAsset CreatePopulatedFontAsset(Font sourceFont)
        {
            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
                sourceFont,
                90,
                9,
                GlyphRenderMode.SDFAA,
                1024,
                1024,
                AtlasPopulationMode.Dynamic,
                true);
            if (fontAsset == null)
            {
                Debug.LogError("[BayonFontAssetBuilder] TMP failed to create the Bayon font asset.");
                return null;
            }

            fontAsset.name = "Bayon-Regular SDF";
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            ConfigureForPlayerBuild(fontAsset);
            PrimeOverlayCharacters(fontAsset);

            if (!IsFontAssetUsable(fontAsset))
            {
                Debug.LogError("[BayonFontAssetBuilder] Bayon glyph atlas was not generated.");
                return null;
            }

            return fontAsset;
        }

        private static bool TryRepairFontAsset(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
                return false;

            if (fontAsset.sourceFontFile == null)
                return false;

            PrimeOverlayCharacters(fontAsset);
            if (!IsFontAssetUsable(fontAsset))
                return false;

            ConfigureForPlayerBuild(fontAsset);
            PersistSubAssets(fontAsset);
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(
                FontAssetPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            Debug.Log("[BayonFontAssetBuilder] Repaired existing Bayon TMP font asset.");
            return IsFontAssetUsable(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath));
        }

        private static void PrimeOverlayCharacters(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
                return;

            if (!fontAsset.TryAddCharacters(OverlayCharacterSet, out string missingCharacters))
            {
                Debug.LogWarning(
                    "[BayonFontAssetBuilder] Bayon is missing characters: " +
                    (string.IsNullOrEmpty(missingCharacters) ? "(none reported)" : missingCharacters));
            }
        }

        private static bool EnsureSourceFontImported()
        {
            if (AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath) != null)
                return true;

            string projectAbsolutePath = Path.GetFullPath(SourceFontPath);
            if (File.Exists(projectAbsolutePath))
            {
                AssetDatabase.ImportAsset(SourceFontPath, ImportAssetOptions.ForceUpdate);
                return AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath) != null;
            }

            string systemFontPath = FindSystemBayonFontPath();
            if (string.IsNullOrWhiteSpace(systemFontPath))
            {
                Debug.LogError(
                    "[BayonFontAssetBuilder] Bayon-Regular.ttf is missing. " +
                    "Install Bayon on macOS or place Bayon-Regular.ttf at " + SourceFontPath + ".");
                return false;
            }

            string fontsFolder = Path.GetDirectoryName(projectAbsolutePath);
            if (!Directory.Exists(fontsFolder))
                Directory.CreateDirectory(fontsFolder);

            File.Copy(systemFontPath, projectAbsolutePath, overwrite: true);
            AssetDatabase.ImportAsset(SourceFontPath, ImportAssetOptions.ForceUpdate);
            Debug.Log("[BayonFontAssetBuilder] Imported Bayon from system font library: " + systemFontPath);
            return AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath) != null;
        }

        private static string FindSystemBayonFontPath()
        {
            string[] searchRoots =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Fonts"),
                "/Library/Fonts",
                "/System/Library/Fonts",
                "/System/Library/Fonts/Supplemental"
            };

            foreach (string root in searchRoots)
            {
                if (!Directory.Exists(root))
                    continue;

                try
                {
                    foreach (string path in Directory.EnumerateFiles(root, "*bayon*", SearchOption.AllDirectories))
                    {
                        string extension = Path.GetExtension(path);
                        if (string.Equals(extension, ".ttf", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(extension, ".otf", StringComparison.OrdinalIgnoreCase))
                        {
                            return path;
                        }
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return null;
        }

        public static bool IsFontAssetUsable(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null || fontAsset.sourceFontFile == null)
                return false;

            if (fontAsset.characterTable != null && fontAsset.characterTable.Count > 0)
                return HasUsableAtlas(fontAsset);

            if (fontAsset.glyphTable != null && fontAsset.glyphTable.Count > 0)
                return HasUsableAtlas(fontAsset);

            return false;
        }

        private static bool HasUsableAtlas(TMP_FontAsset fontAsset)
        {
            Texture2D[] atlases = fontAsset.atlasTextures;
            if (atlases == null || atlases.Length == 0 || atlases[0] == null)
                return false;

            return atlases[0].width > 1 && atlases[0].height > 1;
        }

        private static string preservedFontAssetGuid;

        private static void DeleteFontAssetPreservingGuid()
        {
            preservedFontAssetGuid = ReadGuidFromMeta(FontAssetPath + ".meta");
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath) != null)
                AssetDatabase.DeleteAsset(FontAssetPath);
        }

        private static void RestoreFontAssetGuid()
        {
            if (string.IsNullOrWhiteSpace(preservedFontAssetGuid))
                return;

            string metaPath = FontAssetPath + ".meta";
            if (!File.Exists(metaPath))
                return;

            string meta = File.ReadAllText(metaPath);
            meta = Regex.Replace(meta, @"guid: [0-9a-f]+", "guid: " + preservedFontAssetGuid);
            File.WriteAllText(metaPath, meta);
            AssetDatabase.Refresh();
        }

        private static string ReadGuidFromMeta(string metaPath)
        {
            if (!File.Exists(metaPath))
                return null;

            Match match = Regex.Match(File.ReadAllText(metaPath), @"guid: ([0-9a-f]+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        private static void ConfigureForPlayerBuild(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
                return;

            SerializedObject serializedFont = new SerializedObject(fontAsset);
            SerializedProperty clearOnBuild = serializedFont.FindProperty("m_ClearDynamicDataOnBuild");
            if (clearOnBuild != null)
            {
                clearOnBuild.boolValue = false;
                serializedFont.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void PersistSubAssets(TMP_FontAsset fontAsset)
        {
            if (fontAsset.material != null && !AssetDatabase.Contains(fontAsset.material))
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);

            Texture2D[] atlases = fontAsset.atlasTextures;
            if (atlases == null)
                return;

            for (int i = 0; i < atlases.Length; i++)
            {
                Texture2D atlas = atlases[i];
                if (atlas != null && !AssetDatabase.Contains(atlas))
                    AssetDatabase.AddObjectToAsset(atlas, fontAsset);
            }
        }
    }
}
#endif
