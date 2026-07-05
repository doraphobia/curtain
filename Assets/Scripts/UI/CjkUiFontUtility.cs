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
    private static bool loggedMissingFont;

    private static readonly string[] OsFontCandidates =
    {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        "Hiragino Sans GB",
        "PingFang SC",
        "STHeiti",
        "Heiti SC",
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
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
        if (IsFontAssetUsable(preferredFont))
        {
            PrimeCharacters(preferredFont, textToPrime);
            return preferredFont;
        }

        if (IsFontAssetUsable(cachedFont))
        {
            PrimeCharacters(cachedFont, textToPrime);
            return cachedFont;
        }

        cachedFont = null;

        if (!string.IsNullOrWhiteSpace(resourcesFontPath))
        {
            TMP_FontAsset resourcesFont = Resources.Load<TMP_FontAsset>(resourcesFontPath);
            if (IsFontAssetUsable(resourcesFont))
            {
                cachedFont = resourcesFont;
                PrimeCharacters(cachedFont, textToPrime);
                return cachedFont;
            }
        }

#if UNITY_EDITOR
        TMP_FontAsset editorFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DefaultEditorFontAssetPath);
        if (IsFontAssetUsable(editorFont))
        {
            cachedFont = editorFont;
            PrimeCharacters(cachedFont, textToPrime);
            return cachedFont;
        }

        TMP_FontAsset osFont = CreateEditorOsFont(textToPrime);
        if (IsFontAssetUsable(osFont))
        {
            cachedFont = osFont;
            return cachedFont;
        }
#endif

        LogMissingFontOnce();
        return TMP_Settings.defaultFontAsset;
    }

    public static bool IsFontAssetUsable(TMP_FontAsset fontAsset)
    {
        if (fontAsset == null)
            return false;

        if (fontAsset.characterTable != null && fontAsset.characterTable.Count > 0)
            return HasUsableAtlas(fontAsset);

        if (fontAsset.glyphTable != null && fontAsset.glyphTable.Count > 0)
            return HasUsableAtlas(fontAsset);

        return false;
    }

#if UNITY_EDITOR
    private static TMP_FontAsset CreateEditorOsFont(string textToPrime)
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

        return null;
    }
#endif

    private static bool HasUsableAtlas(TMP_FontAsset fontAsset)
    {
        Texture2D[] atlases = fontAsset.atlasTextures;
        if (atlases == null || atlases.Length == 0 || atlases[0] == null)
            return false;

        return atlases[0].width > 1 && atlases[0].height > 1;
    }

    private static bool CanPopulateAtlas(TMP_FontAsset fontAsset)
    {
        return fontAsset != null &&
               fontAsset.atlasPopulationMode != AtlasPopulationMode.Static &&
               fontAsset.sourceFontFile != null;
    }

    private static void PrimeCharacters(TMP_FontAsset fontAsset, string text)
    {
        if (!CanPopulateAtlas(fontAsset) || string.IsNullOrEmpty(text))
            return;

        fontAsset.TryAddCharacters(text, out _);
    }

    private static void LogMissingFontOnce()
    {
        if (loggedMissingFont)
            return;

        loggedMissingFont = true;
        Debug.LogWarning(
            "[CjkUiFontUtility] CJK UI font is missing or empty. " +
            "Rebuild it with Tools/Duo Curtain/Fusion/Rebuild CJK UI TMP Font Asset.");
    }
}
