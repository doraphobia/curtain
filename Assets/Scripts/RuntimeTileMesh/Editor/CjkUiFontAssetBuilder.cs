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
    public static class CjkUiFontAssetBuilder
    {
        public const string SourceFontPath = "Assets/Fusion/Resources/Fonts/Hiragino Sans GB.ttc";
        public const string FontAssetPath = "Assets/Fusion/Resources/Fonts/Cjk UI SDF.asset";

        private const string DefaultCharacterSet =
            "TYPE: 现在你在屋子外面，目前的你很脆弱！ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 -_,.!?";

        [MenuItem("Tools/Duo Curtain/Fusion/Rebuild CJK UI TMP Font Asset")]
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
            if (!forceRebuild && BayonFontAssetBuilder.IsFontAssetUsable(existing))
                return existing;

            if (!forceRebuild && existing != null && TryRepairFontAsset(existing))
                return existing;

            if (existing != null)
                DeleteFontAssetPreservingGuid();

            Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (sourceFont == null)
            {
                Debug.LogError("[CjkUiFontAssetBuilder] Source font is missing at " + SourceFontPath + ".");
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
            if (!BayonFontAssetBuilder.IsFontAssetUsable(saved))
            {
                Debug.LogError(
                    "[CjkUiFontAssetBuilder] CJK UI TMP font asset was saved but is still unusable. " +
                    "Use Tools/Duo Curtain/Fusion/Rebuild CJK UI TMP Font Asset.");
                return saved;
            }

            Debug.Log("[CjkUiFontAssetBuilder] Created CJK UI TMP font asset at " + FontAssetPath + ".");
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
                Debug.LogError("[CjkUiFontAssetBuilder] TMP failed to create the CJK UI font asset.");
                return null;
            }

            fontAsset.name = "Cjk UI SDF";
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            PrimeCharacters(fontAsset);

            if (!BayonFontAssetBuilder.IsFontAssetUsable(fontAsset))
            {
                Debug.LogError("[CjkUiFontAssetBuilder] CJK glyph atlas was not generated.");
                return null;
            }

            return fontAsset;
        }

        private static bool TryRepairFontAsset(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null || fontAsset.sourceFontFile == null)
                return false;

            PrimeCharacters(fontAsset);
            if (!BayonFontAssetBuilder.IsFontAssetUsable(fontAsset))
                return false;

            PersistSubAssets(fontAsset);
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(
                FontAssetPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            Debug.Log("[CjkUiFontAssetBuilder] Repaired existing CJK UI TMP font asset.");
            return BayonFontAssetBuilder.IsFontAssetUsable(
                AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath));
        }

        private static void PrimeCharacters(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
                return;

            if (!fontAsset.TryAddCharacters(DefaultCharacterSet, out string missingCharacters))
            {
                Debug.LogWarning(
                    "[CjkUiFontAssetBuilder] CJK UI font is missing characters: " +
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

            string systemFontPath = FindSystemCjkFontPath();
            if (string.IsNullOrWhiteSpace(systemFontPath))
            {
                Debug.LogError(
                    "[CjkUiFontAssetBuilder] No CJK system font found. " +
                    "Place Hiragino Sans GB.ttc or another CJK font at " + SourceFontPath + ".");
                return false;
            }

            string fontsFolder = Path.GetDirectoryName(projectAbsolutePath);
            if (!Directory.Exists(fontsFolder))
                Directory.CreateDirectory(fontsFolder);

            File.Copy(systemFontPath, projectAbsolutePath, overwrite: true);
            AssetDatabase.ImportAsset(SourceFontPath, ImportAssetOptions.ForceUpdate);
            Debug.Log("[CjkUiFontAssetBuilder] Imported CJK font from system library: " + systemFontPath);
            return AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath) != null;
        }

        private static string FindSystemCjkFontPath()
        {
            string[] candidates =
            {
                "Hiragino Sans GB.ttc",
                "STHeiti Medium.ttc",
                "PingFang.ttc",
                "Arial Unicode.ttf"
            };

            string[] searchRoots =
            {
                "/System/Library/Fonts",
                "/System/Library/Fonts/Supplemental",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Fonts"),
                "/Library/Fonts"
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                for (int j = 0; j < searchRoots.Length; j++)
                {
                    string path = Path.Combine(searchRoots[j], candidates[i]);
                    if (File.Exists(path))
                        return path;
                }
            }

            return null;
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
