using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FigmaImporter.Editor
{
    public class TMPUtils
    {
        public static TextAlignmentOptions FigmaAlignmentToTMP(string horizontalAlignment, string verticalAlignment)
        {
            int alignment = 0;
            alignment += (verticalAlignment == "TOP" ? 1 : 0) << 8;
            alignment += (verticalAlignment == "CENTER" ? 1 : 0) << 9;
            alignment += (verticalAlignment == "BOTTOM" ? 1 : 0) << 10;
            alignment += (horizontalAlignment == "LEFT" ? 1 : 0) << 0;
            alignment += (horizontalAlignment == "CENTER" ? 1 : 0) << 1;
            alignment += (horizontalAlignment == "RIGHT" ? 1 : 0) << 2;
            alignment += (horizontalAlignment == "JUSTIFIED" ? 1 : 0) << 3;
            return (TextAlignmentOptions) alignment;
        }

        public static FontStyles FigmaFontStyleToTMP(
            string textDecoration,
            string textCase,
            Style style = null)
        {
            FontStyles fontStyle = 0;
            fontStyle |= (textDecoration == "UNDERLINE" ? FontStyles.Underline : 0);
            fontStyle |= (textDecoration == "STRIKETHROUGH" ? FontStyles.Strikethrough : 0);

            fontStyle |= (textCase == "UPPER" ? FontStyles.UpperCase : 0);
            fontStyle |= (textCase == "LOWER" ? FontStyles.LowerCase : 0);
            fontStyle |= (textCase == "SMALL_CAPS" ? FontStyles.SmallCaps : 0);

            var normalizedFigmaFontStyle = GetOptionalStyleString(style, "fontStyle").Trim().ToLowerInvariant();
            if (normalizedFigmaFontStyle.Contains("italic") || normalizedFigmaFontStyle.Contains("oblique"))
            {
                fontStyle |= FontStyles.Italic;
            }

            return fontStyle;
        }

        public static TextMeshProUGUI GetOrAddTMPComponentToObject(GameObject nodeGo)
        {
            var t = TransformUtils.EnsureRectTransform(nodeGo, "TMP");
            if (t == null)
            {
                return null;
            }

            var tmp = nodeGo.GetComponent<TextMeshProUGUI>();
            if (tmp == null)
            {
                // Adding TextMeshProUGUI may reset RectTransform anchors/pivot/offsets.
                // Preserve full layout state so auto-layout positioning remains stable.
                var anchorMin = t.anchorMin;
                var anchorMax = t.anchorMax;
                var pivot = t.pivot;
                var anchoredPosition3D = t.anchoredPosition3D;
                var sizeDelta = t.sizeDelta;
                var offsetMin = t.offsetMin;
                var offsetMax = t.offsetMax;
                var localRotation = t.localRotation;
                var localScale = t.localScale;

                tmp = nodeGo.AddComponent<TextMeshProUGUI>();

                t.anchorMin = anchorMin;
                t.anchorMax = anchorMax;
                t.pivot = pivot;
                t.anchoredPosition3D = anchoredPosition3D;
                t.sizeDelta = sizeDelta;
                t.offsetMin = offsetMin;
                t.offsetMax = offsetMax;
                t.localRotation = localRotation;
                t.localScale = localScale;
            }

            if (tmp.font == null && TMP_Settings.defaultFontAsset != null)
            {
                TryAssignFontSafely(tmp, TMP_Settings.defaultFontAsset, "default TMP font");
            }

            return tmp;
        }

        public static void ApplyFigmaStyleToTMP(TextMeshProUGUI tmp, Style style, float scale, bool applyTypographyDetails = true)
        {
            if (tmp == null || style == null)
            {
                return;
            }

            tmp.fontSize = style.fontSize * scale;
            if (applyTypographyDetails)
            {
                ApplyFigmaTypographyDetails(tmp, style, scale);
            }
            FontLinks fontLinksAsset = FontAssetResolver.GetOrCreateFontLinksAsset();
            if (fontLinksAsset == null)
            {
                Debug.LogError("[FigmaImporter] Couldn't find FontLinks.asset, please create one. Using default font.");
                if (TMP_Settings.defaultFontAsset != null)
                {
                    TryAssignFontSafely(tmp, TMP_Settings.defaultFontAsset, "default TMP font (no FontLinks)");
                }
                if (applyTypographyDetails)
                {
                    ApplyFigmaTypographyDetails(tmp, style, scale);
                }
                return;
            }

            var candidates = FontAssetResolver.GetFontNameCandidates(style);
            if (ImportFallbackRegistry.TryGetFontOverride(candidates, out var overrideFont) && overrideFont != null)
            {
                if (TryAssignFontSafely(tmp, overrideFont, "font override"))
                {
                    FontAssetResolver.EnsureFallbackCoverage(tmp.font, fontLinksAsset, tmp.text, out _);
                    return;
                }

                ImportFallbackRegistry.ReportMissingIssue(
                    "Font",
                    "OverrideAssignment",
                    $"Assigned override font '{overrideFont.name}' could not be used for text object '{tmp.name}'.");
            }

            var font = FontAssetResolver.ResolveOrImport(fontLinksAsset, candidates, out _, out var details);
            if (font == null)
            {
                var fallbackFont = FontAssetResolver.ResolveAutomaticFallbackFont(
                    fontLinksAsset,
                    tmp.text,
                    out var fallbackName,
                    out var fallbackDetails);
                if (fallbackFont != null)
                {
                    var fallbackAssigned = TryAssignFontSafely(tmp, fallbackFont, "auto fallback font");
                    if (!fallbackAssigned)
                    {
                        fallbackFont = null;
                    }
                    ImportFallbackRegistry.ReportMissingFont(candidates, style, tmp.text, fallbackFont, fallbackDetails);
                    if (fallbackAssigned && FontAssetResolver.ShouldLogOnce($"fallback:{tmp.font.name}"))
                    {
                        Debug.Log(
                            $"[FigmaImporter] Missing requested font ({string.Join(", ", candidates.Where(x => !string.IsNullOrWhiteSpace(x)))}) - using auto fallback '{fallbackName ?? tmp.font.name}'. {fallbackDetails}");
                    }
                    else if (!fallbackAssigned)
                    {
                        ImportFallbackRegistry.ReportMissingIssue(
                            "Font",
                            "FallbackAssignment",
                            $"Auto fallback font could not be assigned for '{tmp.name}'. Falling back to TMP default.");
                    }
                }

                if (fallbackFont == null)
                {
                    foreach (var candidate in candidates)
                    {
                        fontLinksAsset.AddName(candidate);
                    }

                    EditorUtility.SetDirty(fontLinksAsset);
                    AssetDatabase.SaveAssets();

                    if (TMP_Settings.defaultFontAsset != null)
                    {
                        var defaultAssigned = TryAssignFontSafely(tmp, TMP_Settings.defaultFontAsset, "TMP default fallback");
                        ImportFallbackRegistry.ReportMissingFont(
                            candidates,
                            style,
                            tmp.text,
                            defaultAssigned ? TMP_Settings.defaultFontAsset : null,
                            details);
                        if (defaultAssigned && FontAssetResolver.ShouldLogOnce($"missing:{string.Join("|", candidates)}"))
                        {
                            Debug.LogWarning(
                                $"[FigmaImporter] Missing font ({string.Join(", ", candidates.Where(x => !string.IsNullOrWhiteSpace(x)))}) - using TMP default '{TMP_Settings.defaultFontAsset.name}'. {details}");
                        }
                        else if (!defaultAssigned)
                        {
                            ImportFallbackRegistry.ReportMissingIssue(
                                "Font",
                                "DefaultAssignment",
                                $"TMP default font exists but could not be assigned for '{tmp.name}'.");
                        }
                    }
                    else
                    {
                        ImportFallbackRegistry.ReportMissingFont(candidates, style, tmp.text, null, details);
                        if (FontAssetResolver.ShouldLogOnce($"missing:{string.Join("|", candidates)}"))
                        {
                            Debug.LogWarning(
                                $"[FigmaImporter] Missing font ({string.Join(", ", candidates.Where(x => !string.IsNullOrWhiteSpace(x)))}), and TMP default font is not set. {details}");
                        }
                    }
                }
            }
            else
            {
                if (!TryAssignFontSafely(tmp, font, "resolved font"))
                {
                    ImportFallbackRegistry.ReportMissingIssue(
                        "Font",
                        "ResolvedAssignment",
                        $"Resolved font '{font.name}' could not be assigned for '{tmp.name}'.");
                }
            }

            // Hard fallback for CJK text when TMP default font is still selected.
            if (FontAssetResolver.ContainsCjkText(tmp.text) &&
                (tmp.font == null || tmp.font == TMP_Settings.defaultFontAsset))
            {
                var cjkFont = FontAssetResolver.ResolvePreferredCjkFont(fontLinksAsset, out var cjkDetails);
                if (cjkFont != null)
                {
                    if (!TryAssignFontSafely(tmp, cjkFont, "CJK primary font"))
                    {
                        ImportFallbackRegistry.ReportMissingIssue(
                            "Font",
                            "CjkAssignment",
                            $"CJK font '{cjkFont.name}' could not be assigned for '{tmp.name}'.");
                    }
                    if (FontAssetResolver.ShouldLogOnce($"cjk-primary:{cjkFont.name}"))
                    {
                        Debug.Log($"[FigmaImporter] Assigned CJK primary font '{cjkFont.name}' for multilingual text. {cjkDetails}");
                    }
                }
            }

            FontAssetResolver.EnsureFallbackCoverage(tmp.font, fontLinksAsset, tmp.text, out var coverageInfo);
            if (!string.IsNullOrWhiteSpace(coverageInfo) &&
                tmp.font != null &&
                FontAssetResolver.ShouldLogOnce($"coverage:{tmp.font.name}:{coverageInfo}"))
            {
                Debug.Log($"[FigmaImporter] {coverageInfo}");
            }

            // Re-apply typography after font resolution so line-height math uses the final font metrics.
            if (applyTypographyDetails)
            {
                ApplyFigmaTypographyDetails(tmp, style, scale);
            }
        }

        public static void ApplyStyleOverrideFallbacksToTMP(TextMeshProUGUI tmp, Node node)
        {
            if (tmp == null || node == null || node.styleOverrideTable == null || node.styleOverrideTable.Count == 0)
            {
                return;
            }

            var fontLinksAsset = FontAssetResolver.GetOrCreateFontLinksAsset();
            if (fontLinksAsset == null)
            {
                return;
            }

            var resolvedOverrideFonts = new List<TMP_FontAsset>();
            foreach (var pair in node.styleOverrideTable)
            {
                var overrideStyle = pair.Value;
                if (overrideStyle == null)
                {
                    continue;
                }

                var candidates = FontAssetResolver.GetFontNameCandidates(overrideStyle);
                if (candidates == null || candidates.Count == 0)
                {
                    continue;
                }

                var resolved = FontAssetResolver.ResolveOrImport(fontLinksAsset, candidates, out _, out var details);
                if (resolved == null)
                {
                    var fallback = FontAssetResolver.ResolveAutomaticFallbackFont(
                        fontLinksAsset,
                        tmp.text,
                        out _,
                        out var fallbackDetails);
                    if (fallback != null)
                    {
                        resolved = fallback;
                    }
                    ImportFallbackRegistry.ReportMissingFont(candidates, overrideStyle, tmp.text, fallback, details ?? fallbackDetails);
                }

                if (resolved == null)
                {
                    continue;
                }

                if (resolvedOverrideFonts.Any(x => x == resolved))
                {
                    continue;
                }

                resolvedOverrideFonts.Add(resolved);
            }

            if (resolvedOverrideFonts.Count == 0)
            {
                return;
            }

            if (tmp.font == null)
            {
                TryAssignFontSafely(tmp, resolvedOverrideFonts[0], "style override primary");
            }

            var primaryFont = tmp.font;
            if (primaryFont == null)
            {
                return;
            }

            var changed = false;
            if (primaryFont.fallbackFontAssetTable == null)
            {
                primaryFont.fallbackFontAssetTable = new List<TMP_FontAsset>();
                changed = true;
            }

            foreach (var overrideFont in resolvedOverrideFonts)
            {
                if (overrideFont == null || overrideFont == primaryFont)
                {
                    continue;
                }

                if (primaryFont.fallbackFontAssetTable.Contains(overrideFont))
                {
                    continue;
                }

                primaryFont.fallbackFontAssetTable.Add(overrideFont);
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            EditorUtility.SetDirty(primaryFont);
            AssetDatabase.SaveAssets();
        }

        public static void ApplyFigmaNodeTypographyBehavior(TextMeshProUGUI tmp, Node node, Style style)
        {
            if (tmp == null || style == null)
            {
                return;
            }

            var textAutoResize = GetOptionalStyleString(style, "textAutoResize").Trim().ToUpperInvariant();
            var textTruncation = GetOptionalNodeString(node, "textTruncation").Trim().ToUpperInvariant();
            var maxLines = GetOptionalNodeInt(node, "maxLines");
            var isTruncate = textAutoResize == "TRUNCATE" || textTruncation == "ENDING";

            switch (textAutoResize)
            {
                case "WIDTH_AND_HEIGHT":
                    SetWordWrapping(tmp, false);
                    break;
                case "HEIGHT":
                case "NONE":
                    SetWordWrapping(tmp, true);
                    break;
                case "TRUNCATE":
                    SetWordWrapping(tmp, maxLines != 1);
                    break;
            }

            tmp.overflowMode = isTruncate ? TextOverflowModes.Truncate : TextOverflowModes.Overflow;

            if (maxLines > 0)
            {
                tmp.maxVisibleLines = maxLines;
                if (isTruncate && maxLines == 1)
                {
                    SetWordWrapping(tmp, false);
                }
            }
            else
            {
                tmp.maxVisibleLines = 99999;
            }
        }

        private static bool TryAssignFontSafely(TextMeshProUGUI tmp, TMP_FontAsset font, string context)
        {
            if (tmp == null || font == null)
            {
                return false;
            }

            try
            {
                if (!FontAssetResolver.TryEnsureUsableFontAsset(font, out var usableFont, out var details) || usableFont == null)
                {
                    Debug.LogWarning(
                        $"[FigmaImporter] Could not assign font '{font.name}' ({context}) because it is unusable. {details}");
                    return false;
                }

                tmp.font = usableFont;
                if (usableFont.material != null)
                {
                    tmp.fontSharedMaterial = usableFont.material;
                }
                return tmp.font != null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FigmaImporter] Could not assign font '{font.name}' ({context}): {e.Message}");
                return false;
            }
        }

        private static void ApplyFigmaTypographyDetails(TextMeshProUGUI tmp, Style style, float scale)
        {
            if (tmp == null || style == null)
            {
                return;
            }

            var scaledFontSize = style.fontSize * scale;
            if (!float.IsNaN(scaledFontSize) && !float.IsInfinity(scaledFontSize) && scaledFontSize > 0f)
            {
                tmp.fontSize = scaledFontSize;
            }

            var characterSpacing = style.letterSpacing * scale;
            if (!float.IsNaN(characterSpacing) && !float.IsInfinity(characterSpacing))
            {
                tmp.characterSpacing = characterSpacing;
            }

            var wordSpacing = GetOptionalStyleFloat(style, "wordSpacing") * scale;
            if (!float.IsNaN(wordSpacing) && !float.IsInfinity(wordSpacing))
            {
                tmp.wordSpacing = wordSpacing;
            }

            var paragraphSpacing = GetOptionalStyleFloat(style, "paragraphSpacing") * scale;
            if (!float.IsNaN(paragraphSpacing) && !float.IsInfinity(paragraphSpacing))
            {
                tmp.paragraphSpacing = paragraphSpacing;
            }

            var paragraphIndent = GetOptionalStyleFloat(style, "paragraphIndent") * scale;
            if (!float.IsNaN(paragraphIndent) && !float.IsInfinity(paragraphIndent))
            {
                SetOptionalTmpFloatProperty(tmp, "paragraphIndent", paragraphIndent);
            }

            var resolvedLineSpacing = ResolveLineSpacingFromFigma(tmp, style, scale, tmp.fontSize);
            if (!float.IsNaN(resolvedLineSpacing) && !float.IsInfinity(resolvedLineSpacing))
            {
                tmp.lineSpacing = resolvedLineSpacing;
            }
        }

        private static float ResolveLineSpacingFromFigma(TextMeshProUGUI tmp, Style style, float scale, float scaledFontSize)
        {
            if (tmp == null || style == null)
            {
                return 0f;
            }

            var lineHeightUnit = (style.lineHeightUnit ?? string.Empty).Trim().ToUpperInvariant();
            if (lineHeightUnit == "AUTO")
            {
                return 0f;
            }

            var baseLineHeight = EstimateBaseLineHeight(tmp, scaledFontSize);
            if (baseLineHeight <= 0f)
            {
                return 0f;
            }

            var targetLineHeight = ResolveTargetLineHeight(style, lineHeightUnit, scale, scaledFontSize, baseLineHeight);
            if (targetLineHeight <= 0f)
            {
                return 0f;
            }

            return targetLineHeight - baseLineHeight;
        }

        private static float ResolveTargetLineHeight(
            Style style,
            string lineHeightUnit,
            float scale,
            float scaledFontSize,
            float baseLineHeight)
        {
            if (style == null)
            {
                return 0f;
            }

            var lineHeightPercentFontSize = GetOptionalStyleFloat(style, "lineHeightPercentFontSize");

            switch (lineHeightUnit)
            {
                case "PIXELS":
                    if (style.lineHeightPx > 0f)
                    {
                        return style.lineHeightPx * scale;
                    }
                    break;
                case "FONT_SIZE_%":
                    if (lineHeightPercentFontSize > 0f)
                    {
                        return scaledFontSize * (lineHeightPercentFontSize / 100f);
                    }
                    break;
                case "INTRINSIC_%":
                    if (style.lineHeightPercent > 0f)
                    {
                        return baseLineHeight * (style.lineHeightPercent / 100f);
                    }
                    break;
            }

            if (style.lineHeightPx > 0f)
            {
                return style.lineHeightPx * scale;
            }

            if (lineHeightPercentFontSize > 0f)
            {
                return scaledFontSize * (lineHeightPercentFontSize / 100f);
            }

            if (style.lineHeightPercent > 0f)
            {
                return baseLineHeight * (style.lineHeightPercent / 100f);
            }

            return 0f;
        }

        private static string GetOptionalStyleString(Style style, string fieldName)
        {
            if (style == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return string.Empty;
            }

            const System.Reflection.BindingFlags Flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            try
            {
                var styleType = style.GetType();
                var property = styleType.GetProperty(fieldName, Flags);
                if (property != null && property.PropertyType == typeof(string))
                {
                    return property.GetValue(style) as string ?? string.Empty;
                }

                var field = styleType.GetField(fieldName, Flags);
                if (field != null && field.FieldType == typeof(string))
                {
                    return field.GetValue(style) as string ?? string.Empty;
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static float GetOptionalStyleFloat(Style style, string fieldName, float fallback = 0f)
        {
            if (style == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return fallback;
            }

            const System.Reflection.BindingFlags Flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            try
            {
                var styleType = style.GetType();
                var property = styleType.GetProperty(fieldName, Flags);
                if (property != null)
                {
                    var value = property.GetValue(style);
                    if (value is float floatValue)
                    {
                        return floatValue;
                    }

                    if (value is double doubleValue)
                    {
                        return (float)doubleValue;
                    }
                }

                var field = styleType.GetField(fieldName, Flags);
                if (field != null)
                {
                    var value = field.GetValue(style);
                    if (value is float floatValue)
                    {
                        return floatValue;
                    }

                    if (value is double doubleValue)
                    {
                        return (float)doubleValue;
                    }
                }
            }
            catch
            {
            }

            return fallback;
        }

        private static string GetOptionalNodeString(Node node, string fieldName)
        {
            if (node == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return string.Empty;
            }

            const System.Reflection.BindingFlags Flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            try
            {
                var nodeType = node.GetType();
                var property = nodeType.GetProperty(fieldName, Flags);
                if (property != null && property.PropertyType == typeof(string))
                {
                    return property.GetValue(node) as string ?? string.Empty;
                }

                var field = nodeType.GetField(fieldName, Flags);
                if (field != null && field.FieldType == typeof(string))
                {
                    return field.GetValue(node) as string ?? string.Empty;
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static int GetOptionalNodeInt(Node node, string fieldName, int fallback = 0)
        {
            if (node == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return fallback;
            }

            const System.Reflection.BindingFlags Flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            try
            {
                var nodeType = node.GetType();
                var property = nodeType.GetProperty(fieldName, Flags);
                if (property != null)
                {
                    var value = property.GetValue(node);
                    if (value is int intValue)
                    {
                        return intValue;
                    }
                }

                var field = nodeType.GetField(fieldName, Flags);
                if (field != null)
                {
                    var value = field.GetValue(node);
                    if (value is int intValue)
                    {
                        return intValue;
                    }
                }
            }
            catch
            {
            }

            return fallback;
        }

        private static bool SetOptionalTmpFloatProperty(TextMeshProUGUI tmp, string propertyName, float value)
        {
            if (tmp == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            const System.Reflection.BindingFlags Flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            try
            {
                var tmpType = tmp.GetType();
                var property = tmpType.GetProperty(propertyName, Flags);
                if (property != null && property.CanWrite)
                {
                    if (property.PropertyType == typeof(float))
                    {
                        property.SetValue(tmp, value);
                        return true;
                    }

                    if (property.PropertyType == typeof(double))
                    {
                        property.SetValue(tmp, (double)value);
                        return true;
                    }
                }

                var field = tmpType.GetField(propertyName, Flags);
                if (field != null)
                {
                    if (field.FieldType == typeof(float))
                    {
                        field.SetValue(tmp, value);
                        return true;
                    }

                    if (field.FieldType == typeof(double))
                    {
                        field.SetValue(tmp, (double)value);
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static void SetWordWrapping(TextMeshProUGUI tmp, bool enabled)
        {
            if (tmp == null)
            {
                return;
            }

            const System.Reflection.BindingFlags Flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            try
            {
                var tmpType = tmp.GetType();
                var wrappingModeProperty = tmpType.GetProperty("textWrappingMode", Flags);
                if (wrappingModeProperty != null &&
                    wrappingModeProperty.CanWrite &&
                    wrappingModeProperty.PropertyType.IsEnum)
                {
                    var enumType = wrappingModeProperty.PropertyType;
                    var preferredNames = enabled
                        ? new[] { "Normal", "Wrap", "PreserveWhitespace" }
                        : new[] { "NoWrap" };

                    foreach (var preferredName in preferredNames)
                    {
                        if (!Enum.GetNames(enumType).Any(x => string.Equals(x, preferredName, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        var enumValue = Enum.Parse(enumType, preferredName, true);
                        wrappingModeProperty.SetValue(tmp, enumValue);
                        return;
                    }

                    var allValues = Enum.GetValues(enumType);
                    if (allValues != null && allValues.Length > 0)
                    {
                        var fallbackValue = enabled
                            ? allValues.GetValue(0)
                            : allValues.GetValue(Math.Min(1, allValues.Length - 1));
                        wrappingModeProperty.SetValue(tmp, fallbackValue);
                        return;
                    }
                }
            }
            catch
            {
            }

            try
            {
                // Backward-compatible fallback for older TMP APIs.
                var legacyProperty = tmp.GetType().GetProperty("enableWordWrapping", Flags);
                if (legacyProperty != null && legacyProperty.CanWrite && legacyProperty.PropertyType == typeof(bool))
                {
                    legacyProperty.SetValue(tmp, enabled);
                }
            }
            catch
            {
            }
        }

        private static float EstimateBaseLineHeight(TextMeshProUGUI tmp, float scaledFontSize)
        {
            if (tmp == null)
            {
                return 0f;
            }

            if (!float.IsNaN(scaledFontSize) && !float.IsInfinity(scaledFontSize) && scaledFontSize > 0f)
            {
                var font = tmp.font;
                if (font != null)
                {
                    var faceInfo = font.faceInfo;
                    if (faceInfo.pointSize > 0f && faceInfo.lineHeight > 0f)
                    {
                        return (faceInfo.lineHeight / faceInfo.pointSize) * scaledFontSize;
                    }
                }

                return scaledFontSize;
            }

            return 0f;
        }

        public static int RepairBrokenFontsInOpenScenes()
        {
            var repairedCount = 0;
            var tmpTexts = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();
            if (tmpTexts == null || tmpTexts.Length == 0)
            {
                return repairedCount;
            }

            foreach (var tmpText in tmpTexts)
            {
                if (tmpText == null || EditorUtility.IsPersistent(tmpText))
                {
                    continue;
                }

                var currentFont = tmpText.font;
                if (currentFont == null)
                {
                    continue;
                }

                if (FontAssetResolver.IsFontAssetUsable(currentFont, out _))
                {
                    continue;
                }

                var replaced = false;
                if (FontAssetResolver.TryEnsureUsableFontAsset(currentFont, out var repairedFont, out _)
                    && repairedFont != null)
                {
                    replaced = TryAssignFontSafely(tmpText, repairedFont, "scene repair");
                }

                if (!replaced && TMP_Settings.defaultFontAsset != null)
                {
                    replaced = TryAssignFontSafely(tmpText, TMP_Settings.defaultFontAsset, "scene repair fallback");
                }

                if (!replaced)
                {
                    continue;
                }

                repairedCount++;
                EditorUtility.SetDirty(tmpText);
                if (tmpText.gameObject != null && tmpText.gameObject.scene.IsValid())
                {
                    EditorSceneManager.MarkSceneDirty(tmpText.gameObject.scene);
                }
            }

            return repairedCount;
        }
    }
}
