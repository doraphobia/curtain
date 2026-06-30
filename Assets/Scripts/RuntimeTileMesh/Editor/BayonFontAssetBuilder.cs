#if UNITY_EDITOR
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

        static BayonFontAssetBuilder()
        {
            EditorApplication.delayCall += () => EnsureFontAsset();
        }

        [MenuItem("Tools/Duo Curtain/Fusion/Rebuild Bayon TMP Font Asset")]
        public static void RebuildFontAsset()
        {
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (existing != null)
                AssetDatabase.DeleteAsset(FontAssetPath);

            EnsureFontAsset();
        }

        public static TMP_FontAsset EnsureFontAsset()
        {
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (existing != null)
                return existing;

            Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (sourceFont == null)
                return null;

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
            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);

            fontAsset.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 -_", out _);
            PersistSubAssets(fontAsset);
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(
                FontAssetPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            Debug.Log("[BayonFontAssetBuilder] Created Bayon TMP font asset at " + FontAssetPath + ".");
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
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
