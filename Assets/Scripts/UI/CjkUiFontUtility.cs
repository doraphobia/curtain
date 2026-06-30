using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class CjkUiFontUtility
{
    public const string DefaultResourcesFontPath = "Fonts/Cjk UI SDF";
    public const string DefaultEditorFontAssetPath = "Assets/Fusion/Resources/Fonts/Cjk UI SDF.asset";

    private static TMP_FontAsset cachedFont;

    private static readonly string[] OsFontCandidates =
    {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        "Hiragino Sans GB",
        "Hiragino Sans GB W3",
        "Hiragino Sans GB W6",
        "HiraginoSansGB-W3",
        "HiraginoSansGB-W6",
        "PingFang SC",
        "PingFang SC Regular",
        "PingFangSC-Regular",
        "STHeiti",
        "STHeiti Medium",
        "STHeitiSC-Medium",
        "Heiti SC",
        "Heiti SC Medium",
#elif UNITY_STANDALONE_WIN
        "Microsoft YaHei",
        "Microsoft YaHei UI",
        "SimHei",
        "SimSun",
#else
        "Noto Sans CJK SC",
        "WenQuanYi Zen Hei",
#endif
        "Arial Unicode MS"
    };

    public static TMP_FontAsset Resolve(
        TMP_FontAsset preferredFont = null,
        string resourcesFontPath = DefaultResourcesFontPath,
        string textToPrime = null)
    {
        if (preferredFont != null)
        {
            if (CanPopulateAtlas(preferredFont))
                PrimeCharacters(preferredFont, textToPrime);
            return preferredFont;
        }

        if (cachedFont != null)
        {
            if (CanPopulateAtlas(cachedFont))
                PrimeCharacters(cachedFont, textToPrime);
            return cachedFont;
        }

        if (!string.IsNullOrWhiteSpace(resourcesFontPath))
        {
            TMP_FontAsset resourcesFont = Resources.Load<TMP_FontAsset>(resourcesFontPath);
            if (resourcesFont != null)
            {
                cachedFont = resourcesFont;
                if (CanPopulateAtlas(cachedFont))
                    PrimeCharacters(cachedFont, textToPrime);
                return cachedFont;
            }
        }

#if UNITY_EDITOR
        TMP_FontAsset editorFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DefaultEditorFontAssetPath);
        if (editorFont != null)
        {
            cachedFont = editorFont;
            if (CanPopulateAtlas(cachedFont))
                PrimeCharacters(cachedFont, textToPrime);
            return cachedFont;
        }
#endif

        cachedFont = CreateRuntimeOsFont(textToPrime);
        return cachedFont;
    }

    private static TMP_FontAsset CreateRuntimeOsFont(string textToPrime)
    {
        string primeText = string.IsNullOrWhiteSpace(textToPrime) ? "中文" : textToPrime;

        for (int i = 0; i < OsFontCandidates.Length; i++)
        {
            Font sourceFont = Font.CreateDynamicFontFromOSFont(OsFontCandidates[i], 48);
            if (sourceFont == null)
                continue;

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
                continue;

            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            fontAsset.name = OsFontCandidates[i] + " Runtime SDF";
            if (fontAsset.TryAddCharacters(primeText, out string missingCharacters) &&
                string.IsNullOrEmpty(missingCharacters))
            {
                return fontAsset;
            }
        }

        TMP_FontAsset fallback = TMP_Settings.defaultFontAsset;
        return fallback;
    }

    private static bool CanPopulateAtlas(TMP_FontAsset fontAsset)
    {
        return fontAsset != null && fontAsset.atlasPopulationMode != AtlasPopulationMode.Static;
    }

    private static void PrimeCharacters(TMP_FontAsset fontAsset, string text)
    {
        if (fontAsset == null || string.IsNullOrEmpty(text))
            return;

        fontAsset.TryAddCharacters(text, out _);
    }
}
