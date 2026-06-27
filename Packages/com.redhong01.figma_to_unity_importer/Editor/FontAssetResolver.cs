using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace FigmaImporter.Editor
{
    internal static class FontAssetResolver
    {
        private const string FontLinksAssetName = "Font Links.asset";
        private const string FontLinksFallbackPath = FigmaPathUtils.LocalFontLinksAssetPath;
        private const string LegacyFontLinksPath = FigmaPathUtils.LegacySharedFontLinksAssetPath;
        private const string PackageFontLinksPath = "Packages/com.redhong01.figma_to_unity_importer/Editor/Font Links.asset";
        private const string ImportedFontsFolder = FigmaPathUtils.LocalFontsFolderAssetPath;

        private static readonly HashSet<string> SupportedFontExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".ttf",
            ".otf",
            ".ttc",
            ".otc",
            ".dfont"
        };

        private static readonly Dictionary<string, TMP_FontAsset> ResolvedFontCache =
            new Dictionary<string, TMP_FontAsset>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> SystemFontIndex =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> InstalledOsFontNameIndex =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> LoggedMessages =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, TMP_FontAsset> ReplacementFontByBrokenAssetPath =
            new Dictionary<string, TMP_FontAsset>(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] MacCjkFallbackCandidates =
        {
            "PingFang SC",
            "PingFangSC-Regular",
            "Heiti SC",
            "Songti SC",
            "Hiragino Sans GB",
            "STHeiti",
            "Arial Unicode MS"
        };

        private static readonly string[] WindowsCjkFallbackCandidates =
        {
            "Microsoft YaHei",
            "Microsoft YaHei UI",
            "DengXian",
            "SimHei",
            "SimSun",
            "Arial Unicode MS"
        };

        private static readonly string[] LinuxCjkFallbackCandidates =
        {
            "Noto Sans CJK SC",
            "Noto Sans CJK",
            "Source Han Sans SC",
            "WenQuanYi Zen Hei",
            "AR PL UKai CN"
        };

        private static readonly string[] GenericFallbackCandidates =
        {
            "Arial",
            "Segoe UI",
            "Helvetica",
            "Noto Sans",
            "Liberation Sans"
        };

        private static bool _systemFontIndexBuilt;
        private static bool _installedOsFontNameIndexBuilt;
        private const int MaxFontScanDepth = 12;

        public static FontLinks GetOrCreateFontLinksAsset()
        {
            var localAsset = AssetDatabase.LoadAssetAtPath<FontLinks>(FontLinksFallbackPath);
            if (localAsset != null)
            {
                return localAsset;
            }

            FigmaPathUtils.EnsureAssetFolderExists(FigmaPathUtils.LocalRootAssetPath);
            FigmaPathUtils.EnsureAssetFolderExists(FigmaPathUtils.LocalEditorFolderAssetPath);

            var created = ScriptableObject.CreateInstance<FontLinks>();
            var source = AssetDatabase.LoadAssetAtPath<FontLinks>(LegacyFontLinksPath)
                         ?? AssetDatabase.LoadAssetAtPath<FontLinks>(PackageFontLinksPath);
            if (source == null)
            {
                var guids = AssetDatabase.FindAssets("t:FontLinks");
                if (guids != null)
                {
                    foreach (var guid in guids)
                    {
                        var existingPath = AssetDatabase.GUIDToAssetPath(guid);
                        if (string.Equals(existingPath, FontLinksFallbackPath, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        source = AssetDatabase.LoadAssetAtPath<FontLinks>(existingPath);
                        if (source != null)
                        {
                            break;
                        }
                    }
                }
            }

            if (source != null)
            {
                EditorUtility.CopySerialized(source, created);
            }

            AssetDatabase.CreateAsset(created, FontLinksFallbackPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[FigmaImporter] Created missing {FontLinksAssetName} at {FontLinksFallbackPath}");
            return created;
        }

        public static List<string> GetFontNameCandidates(Style style)
        {
            var candidates = new List<string>();
            if (style == null)
            {
                return candidates;
            }

            AddCandidate(candidates, style.fontPostScriptName);
            AddCandidate(candidates, style.fontFamily);
            if (!string.IsNullOrWhiteSpace(style.fontFamily) && style.fontWeight > 0)
            {
                AddCandidate(candidates, $"{style.fontFamily}-{style.fontWeight}");
                foreach (var weightAlias in GetWeightStyleAliases(style.fontWeight))
                {
                    AddCandidate(candidates, $"{style.fontFamily} {weightAlias}");
                    AddCandidate(candidates, $"{style.fontFamily}-{weightAlias}");
                }
            }

            return candidates;
        }

        public static TMP_FontAsset ResolveOrImport(
            FontLinks fontLinks,
            IList<string> candidateNames,
            out string resolvedName,
            out string resolutionDetails)
        {
            resolvedName = null;
            resolutionDetails = null;

            if (candidateNames == null || candidateNames.Count == 0)
            {
                resolutionDetails = "No font candidates in Figma style.";
                return null;
            }

            var normalizedCandidates = candidateNames
                .Select(NormalizeFontKey)
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalizedCandidates.Count == 0)
            {
                resolutionDetails = "No valid font candidates after normalization.";
                return null;
            }

            foreach (var normalizedCandidate in normalizedCandidates)
            {
                if (ResolvedFontCache.TryGetValue(normalizedCandidate, out var cached) && cached != null)
                {
                    if (TryEnsureUsableFontAsset(cached, out var usableCached, out var cacheDetails))
                    {
                        resolvedName = candidateNames.FirstOrDefault(x =>
                            string.Equals(NormalizeFontKey(x), normalizedCandidate, StringComparison.OrdinalIgnoreCase));
                        resolutionDetails = $"Resolved from cache. {cacheDetails}";
                        return usableCached;
                    }

                    ResolvedFontCache.Remove(normalizedCandidate);
                }
            }

            if (fontLinks != null)
            {
                var linkedFont = fontLinks.GetAny(candidateNames, out var linkedName);
                if (linkedFont != null)
                {
                    if (TryEnsureUsableFontAsset(linkedFont, out var usableLinkedFont, out var linkedDetails))
                    {
                        if (usableLinkedFont != linkedFont)
                        {
                            LinkFont(fontLinks, candidateNames, usableLinkedFont);
                        }
                        CacheResolvedFont(normalizedCandidates, usableLinkedFont);
                        resolvedName = linkedName;
                        resolutionDetails = $"Resolved from FontLinks. {linkedDetails}";
                        return usableLinkedFont;
                    }

                    resolutionDetails = $"FontLinks pointed to unusable font '{linkedFont.name}'. {linkedDetails}";
                }
            }

            var projectFont = FindProjectFontAsset(candidateNames, out var projectFontName);
            if (projectFont != null)
            {
                LinkFont(fontLinks, candidateNames, projectFont);
                CacheResolvedFont(normalizedCandidates, projectFont);
                resolvedName = projectFontName;
                resolutionDetails = "Resolved from existing TMP font asset in project.";
                return projectFont;
            }

            var importedFont = TryImportFromSystemFonts(candidateNames, out var importedFontPath, out var importedFontName);
            if (importedFont != null)
            {
                LinkFont(fontLinks, candidateNames, importedFont);
                CacheResolvedFont(normalizedCandidates, importedFont);
                resolvedName = importedFontName;
                resolutionDetails = $"Imported from system font '{importedFontPath}' to '{ImportedFontsFolder}'.";
                return importedFont;
            }

            resolutionDetails = "Font not found in FontLinks, project TMP assets, or system font directories.";
            return null;
        }

        public static TMP_FontAsset ResolveAutomaticFallbackFont(
            FontLinks fontLinks,
            string text,
            out string resolvedName,
            out string resolutionDetails)
        {
            resolvedName = null;
            resolutionDetails = null;

            var fallbackCandidates = GetAutomaticFallbackCandidates(text);
            if (fallbackCandidates.Count == 0)
            {
                resolutionDetails = "No fallback candidates available.";
                return null;
            }

            var projectFont = FindProjectFontAsset(fallbackCandidates, out var projectFontName);
            if (projectFont != null)
            {
                LinkFont(fontLinks, fallbackCandidates, projectFont);
                resolvedName = projectFontName;
                resolutionDetails = $"Auto-fallback resolved from project font '{projectFontName}'.";
                return projectFont;
            }

            var importedFont = TryImportFromSystemFonts(
                fallbackCandidates,
                out var importedFontPath,
                out var importedFontName);
            if (importedFont != null)
            {
                LinkFont(fontLinks, fallbackCandidates, importedFont);
                resolvedName = importedFontName;
                resolutionDetails = $"Auto-fallback imported from system font '{importedFontPath}'.";
                return importedFont;
            }

            resolutionDetails = "Auto-fallback font was not found in project or system font directories.";
            return null;
        }

        public static void EnsureFallbackCoverage(
            TMP_FontAsset primaryFont,
            FontLinks fontLinks,
            string text,
            out string details)
        {
            details = null;
            if (primaryFont == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (!ContainsCjkText(text))
            {
                return;
            }

            var cjkCandidates = GetCjkFallbackCandidates();
            var cjkFont = FindProjectFontAsset(cjkCandidates, out _);
            if (cjkFont == null)
            {
                cjkFont = TryImportFromSystemFonts(cjkCandidates, out _, out _);
            }

            if (cjkFont == null || cjkFont == primaryFont)
            {
                return;
            }

            var updatedAny = false;

            if (primaryFont.fallbackFontAssetTable == null)
            {
                primaryFont.fallbackFontAssetTable = new List<TMP_FontAsset>();
            }

            if (!primaryFont.fallbackFontAssetTable.Contains(cjkFont))
            {
                primaryFont.fallbackFontAssetTable.Add(cjkFont);
                EditorUtility.SetDirty(primaryFont);
                updatedAny = true;
            }

            var tmpSettings = TMP_Settings.instance;
            if (tmpSettings != null && TryAddToTmpSettingsFallbackList(tmpSettings, cjkFont))
            {
                EditorUtility.SetDirty(tmpSettings);
                updatedAny = true;
            }

            if (updatedAny)
            {
                AssetDatabase.SaveAssets();
                details = $"Added CJK fallback '{cjkFont.name}' for multilingual text coverage.";
            }
        }

        public static bool ContainsCjkText(string text)
        {
            return ContainsCjkCharacters(text);
        }

        public static TMP_FontAsset ResolvePreferredCjkFont(FontLinks fontLinks, out string details)
        {
            details = null;
            var cjkCandidates = GetCjkFallbackCandidates();
            var cjkFont = FindProjectFontAsset(cjkCandidates, out var projectFontName);
            if (cjkFont != null)
            {
                LinkFont(fontLinks, cjkCandidates, cjkFont);
                details = $"Resolved CJK font from project: {projectFontName ?? cjkFont.name}";
                return cjkFont;
            }

            cjkFont = TryImportFromSystemFonts(cjkCandidates, out var importedPath, out var importedName);
            if (cjkFont != null)
            {
                LinkFont(fontLinks, cjkCandidates, cjkFont);
                details = $"Imported CJK font from system: {importedName ?? cjkFont.name} ({importedPath})";
                return cjkFont;
            }

            details = "No CJK-capable font found in project or system directories.";
            return null;
        }

        public static bool ShouldLogOnce(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            return LoggedMessages.Add(key);
        }

        public static bool IsFontAssetUsable(TMP_FontAsset fontAsset, out string details)
        {
            details = null;
            if (fontAsset == null)
            {
                details = "Font asset is null.";
                return false;
            }

            try
            {
                if (fontAsset.material == null)
                {
                    details = "Font material is missing.";
                    return false;
                }
            }
            catch (Exception e)
            {
                details = $"Failed to read font material: {e.Message}";
                return false;
            }

            try
            {
                var atlasTextures = fontAsset.atlasTextures;
                if (atlasTextures == null || atlasTextures.Length == 0 || atlasTextures[0] == null)
                {
                    details = "Font atlas texture is missing.";
                    return false;
                }
            }
            catch (Exception e)
            {
                details = $"Failed to read atlas textures: {e.Message}";
                return false;
            }

            details = "Font asset is usable.";
            return true;
        }

        public static bool TryEnsureUsableFontAsset(TMP_FontAsset fontAsset, out TMP_FontAsset usableFont, out string details)
        {
            usableFont = null;
            details = null;
            if (fontAsset == null)
            {
                details = "Font asset is null.";
                return false;
            }

            if (IsFontAssetUsable(fontAsset, out var healthInfo))
            {
                usableFont = fontAsset;
                details = healthInfo;
                return true;
            }

            var brokenAssetPath = AssetDatabase.GetAssetPath(fontAsset);
            if (!string.IsNullOrWhiteSpace(brokenAssetPath) &&
                ReplacementFontByBrokenAssetPath.TryGetValue(brokenAssetPath, out var cachedReplacement) &&
                cachedReplacement != null &&
                IsFontAssetUsable(cachedReplacement, out var cachedReplacementInfo))
            {
                usableFont = cachedReplacement;
                details = $"Using cached replacement font asset. {cachedReplacementInfo}";
                return true;
            }

            if (TryCreateReplacementFontAsset(fontAsset, out var replacementFont, out var replacementDetails))
            {
                if (replacementFont != null && IsFontAssetUsable(replacementFont, out var replacementHealthInfo))
                {
                    if (!string.IsNullOrWhiteSpace(brokenAssetPath))
                    {
                        ReplacementFontByBrokenAssetPath[brokenAssetPath] = replacementFont;
                    }
                    ReplaceBrokenFontReferences(fontAsset, replacementFont);
                    usableFont = replacementFont;
                    details = $"Created replacement font asset. {replacementHealthInfo}";
                    return true;
                }

                details = $"Replacement attempted but still unusable. {replacementDetails}";
                return false;
            }

            details = $"Font asset is unusable and could not be repaired. {healthInfo}";
            return false;
        }

        private static void AddCandidate(ICollection<string> candidates, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var trimmed = value.Trim();
            if (candidates.Any(x => string.Equals(x, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            candidates.Add(trimmed);
        }

        private static void CacheResolvedFont(IEnumerable<string> normalizedCandidates, TMP_FontAsset font)
        {
            if (font == null || normalizedCandidates == null)
            {
                return;
            }

            foreach (var key in normalizedCandidates)
            {
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }
                ResolvedFontCache[key] = font;
            }
        }

        private static TMP_FontAsset FindProjectFontAsset(IList<string> candidateNames, out string matchedName)
        {
            matchedName = null;
            if (candidateNames == null || candidateNames.Count == 0)
            {
                return null;
            }

            var normalizedCandidates = candidateNames
                .Select(NormalizeFontKey)
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalizedCandidates.Count == 0)
            {
                return null;
            }

            var guids = AssetDatabase.FindAssets("t:TMP_FontAsset");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var tmpFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (tmpFont == null)
                {
                    continue;
                }

                if (MatchesCandidate(tmpFont.name, normalizedCandidates))
                {
                    if (TryEnsureUsableFontAsset(tmpFont, out var usableFont, out var details))
                    {
                        matchedName = tmpFont.name;
                        return usableFont;
                    }

                    if (ShouldLogOnce($"unusable-project-font:{tmpFont.name}"))
                    {
                        Debug.LogWarning($"[FigmaImporter] Ignoring unusable project font '{tmpFont.name}'. {details}");
                    }

                    continue;
                }

                var sourceFont = tmpFont.sourceFontFile;
                if (sourceFont == null)
                {
                    continue;
                }

                if (MatchesCandidate(sourceFont.name, normalizedCandidates))
                {
                    if (TryEnsureUsableFontAsset(tmpFont, out var usableFont, out var details))
                    {
                        matchedName = sourceFont.name;
                        return usableFont;
                    }

                    if (ShouldLogOnce($"unusable-project-font:{tmpFont.name}"))
                    {
                        Debug.LogWarning($"[FigmaImporter] Ignoring unusable project font '{tmpFont.name}'. {details}");
                    }

                    continue;
                }

                var sourceFontNames = sourceFont.fontNames;
                if (sourceFontNames == null)
                {
                    continue;
                }

                foreach (var sourceFontName in sourceFontNames)
                {
                    if (!MatchesCandidate(sourceFontName, normalizedCandidates))
                    {
                        continue;
                    }

                    if (TryEnsureUsableFontAsset(tmpFont, out var usableFont, out var details))
                    {
                        matchedName = sourceFontName;
                        return usableFont;
                    }

                    if (ShouldLogOnce($"unusable-project-font:{tmpFont.name}"))
                    {
                        Debug.LogWarning($"[FigmaImporter] Ignoring unusable project font '{tmpFont.name}'. {details}");
                    }

                    break;
                }
            }

            return null;
        }

        private static bool MatchesCandidate(string value, IList<string> normalizedCandidates)
        {
            if (string.IsNullOrWhiteSpace(value) || normalizedCandidates == null || normalizedCandidates.Count == 0)
            {
                return false;
            }

            var key = NormalizeFontKey(value);
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            foreach (var candidate in normalizedCandidates)
            {
                if (key.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            foreach (var candidate in normalizedCandidates)
            {
                if (key.Contains(candidate) || candidate.Contains(key))
                {
                    return true;
                }
            }

            return false;
        }

        private static TMP_FontAsset TryImportFromSystemFonts(
            IList<string> candidateNames,
            out string importedFontPath,
            out string matchedCandidate)
        {
            importedFontPath = null;
            matchedCandidate = null;

            if (TryFindSystemFontPath(candidateNames, out var systemFontPath, out matchedCandidate))
            {
                var importedFromFile = TryImportFontFromFile(
                    systemFontPath,
                    matchedCandidate,
                    out var importedFromFilePath);
                if (importedFromFile != null)
                {
                    importedFontPath = importedFromFilePath;
                    return importedFromFile;
                }

                importedFontPath = systemFontPath;
            }

            var importedFromOsName = TryImportFontFromInstalledOsNames(
                candidateNames,
                out var importedFromOsPath,
                out var matchedCandidateFromOs);
            if (importedFromOsName != null)
            {
                importedFontPath = importedFromOsPath;
                if (string.IsNullOrWhiteSpace(matchedCandidate))
                {
                    matchedCandidate = matchedCandidateFromOs;
                }
                return importedFromOsName;
            }

            if (ShouldLogOnce($"system-font-unresolved:{string.Join("|", candidateNames ?? Array.Empty<string>())}"))
            {
                Debug.LogWarning(
                    $"[FigmaImporter] Could not import system font from file paths or installed OS font names. Candidates: {string.Join(", ", candidateNames ?? Array.Empty<string>())}");
            }

            return null;
        }

        private static TMP_FontAsset TryImportFontFromFile(
            string systemFontPath,
            string matchedCandidate,
            out string importedFontPath)
        {
            importedFontPath = null;
            if (string.IsNullOrWhiteSpace(systemFontPath))
            {
                return null;
            }

            EnsureAssetFolder(ImportedFontsFolder);

            var extension = Path.GetExtension(systemFontPath);
            var sourceBaseName = Path.GetFileNameWithoutExtension(systemFontPath);
            var sanitizedBaseName = FigmaPathUtils.SanitizeFileName(sourceBaseName, "ImportedFont");
            var destinationFileName = $"{sanitizedBaseName}{extension}";
            var destinationAssetPath = $"{ImportedFontsFolder}/{destinationFileName}";
            var destinationAbsolutePath = FigmaPathUtils.ToAbsolutePathFromAssetPath(destinationAssetPath);

            if (!File.Exists(destinationAbsolutePath))
            {
                File.Copy(systemFontPath, destinationAbsolutePath, false);
            }

            AssetDatabase.ImportAsset(destinationAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(destinationAssetPath);
            if (sourceFont == null)
            {
                importedFontPath = systemFontPath;
                return null;
            }

            var tmpAssetPath = $"{ImportedFontsFolder}/{sanitizedBaseName} TMP.asset";
            var tmpFontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(tmpAssetPath);
            if (tmpFontAsset != null)
            {
                if (!TryEnsureUsableFontAsset(tmpFontAsset, out tmpFontAsset, out var existingDetails))
                {
                    if (ShouldLogOnce($"unusable-imported-font:{tmpAssetPath}"))
                    {
                        Debug.LogWarning($"[FigmaImporter] Existing imported TMP font is unusable and will be recreated with a new asset path. {existingDetails}");
                    }

                    tmpFontAsset = null;
                    tmpAssetPath = AssetDatabase.GenerateUniqueAssetPath(tmpAssetPath);
                }
            }

            if (tmpFontAsset == null)
            {
                tmpFontAsset = CreateFontAssetWithCompatibleApi(sourceFont);
                if (tmpFontAsset == null)
                {
                    importedFontPath = systemFontPath;
                    return null;
                }

                AssetDatabase.CreateAsset(tmpFontAsset, tmpAssetPath);
                EnsureFontSubAssetsPersisted(tmpFontAsset, tmpAssetPath);
                PrimeDynamicFontAsset(tmpFontAsset);
                AssetDatabase.ImportAsset(tmpAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                tmpFontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(tmpAssetPath);
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                $"[FigmaImporter] Imported missing font '{matchedCandidate ?? sourceBaseName}' from system path '{systemFontPath}' into '{destinationAssetPath}'.");
            importedFontPath = systemFontPath;
            return tmpFontAsset;
        }

        private static TMP_FontAsset TryImportFontFromInstalledOsNames(
            IList<string> candidateNames,
            out string importedFontPath,
            out string matchedCandidate)
        {
            importedFontPath = null;
            matchedCandidate = null;
            if (!TryFindInstalledOsFontName(candidateNames, out var installedOsFontName, out matchedCandidate))
            {
                return null;
            }

            EnsureAssetFolder(ImportedFontsFolder);

            var sanitizedBaseName = FigmaPathUtils.SanitizeFileName(installedOsFontName, "InstalledOSFont");
            var tmpAssetPath = $"{ImportedFontsFolder}/{sanitizedBaseName} TMP.asset";
            var tmpFontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(tmpAssetPath);
            if (tmpFontAsset != null)
            {
                if (TryEnsureUsableFontAsset(tmpFontAsset, out var usableExisting, out _))
                {
                    importedFontPath = $"os://{installedOsFontName}";
                    return usableExisting;
                }

                tmpAssetPath = AssetDatabase.GenerateUniqueAssetPath(tmpAssetPath);
            }

            var dynamicFont = TryCreateDynamicFontFromOs(installedOsFontName, candidateNames);
            if (dynamicFont == null)
            {
                importedFontPath = $"os://{installedOsFontName}";
                return null;
            }

            tmpFontAsset = CreateFontAssetWithCompatibleApi(dynamicFont);
            if (tmpFontAsset == null)
            {
                importedFontPath = $"os://{installedOsFontName}";
                return null;
            }

            var tmpName = Path.GetFileNameWithoutExtension(tmpAssetPath);
            if (!string.IsNullOrWhiteSpace(tmpName))
            {
                tmpFontAsset.name = tmpName;
            }

            AssetDatabase.CreateAsset(tmpFontAsset, tmpAssetPath);
            EnsureFontSubAssetsPersisted(tmpFontAsset, tmpAssetPath);
            PrimeDynamicFontAsset(tmpFontAsset);
            AssetDatabase.ImportAsset(tmpAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            tmpFontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(tmpAssetPath) ?? tmpFontAsset;
            AssetDatabase.SaveAssets();

            importedFontPath = $"os://{installedOsFontName}";
            Debug.Log(
                $"[FigmaImporter] Imported missing font '{matchedCandidate ?? installedOsFontName}' from installed OS font '{installedOsFontName}' into '{tmpAssetPath}'.");
            return tmpFontAsset;
        }

        private static Font TryCreateDynamicFontFromOs(string preferredOsFontName, IList<string> fallbackCandidates)
        {
            var attemptedNames = new List<string>();
            AddCandidate(attemptedNames, preferredOsFontName);

            if (fallbackCandidates != null)
            {
                foreach (var fallbackCandidate in fallbackCandidates)
                {
                    AddCandidate(attemptedNames, fallbackCandidate);
                }
            }

            foreach (var attemptedName in attemptedNames)
            {
                try
                {
                    var dynamicFont = Font.CreateDynamicFontFromOSFont(attemptedName, 32);
                    if (dynamicFont != null)
                    {
                        return dynamicFont;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static bool TryFindSystemFontPath(IList<string> candidateNames, out string path, out string matchedCandidate)
        {
            path = null;
            matchedCandidate = null;
            if (candidateNames == null || candidateNames.Count == 0)
            {
                return false;
            }

            EnsureSystemFontIndex();
            if (SystemFontIndex.Count == 0)
            {
                return false;
            }

            var normalizedCandidates = candidateNames
                .Select(NormalizeFontKey)
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var candidate in normalizedCandidates)
            {
                if (SystemFontIndex.TryGetValue(candidate, out path))
                {
                    matchedCandidate = candidateNames.FirstOrDefault(x =>
                        string.Equals(NormalizeFontKey(x), candidate, StringComparison.OrdinalIgnoreCase));
                    return true;
                }
            }

            var bestScore = int.MaxValue;
            string bestPath = null;
            string bestCandidate = null;
            foreach (var kv in SystemFontIndex)
            {
                foreach (var candidate in normalizedCandidates)
                {
                    if (!kv.Key.Contains(candidate) && !candidate.Contains(kv.Key))
                    {
                        continue;
                    }

                    var score = Math.Abs(kv.Key.Length - candidate.Length);
                    if (score >= bestScore)
                    {
                        continue;
                    }

                    bestScore = score;
                    bestPath = kv.Value;
                    bestCandidate = candidate;
                }
            }

            if (bestPath == null)
            {
                return false;
            }

            path = bestPath;
            matchedCandidate = candidateNames.FirstOrDefault(x =>
                string.Equals(NormalizeFontKey(x), bestCandidate, StringComparison.OrdinalIgnoreCase));
            return true;
        }

        private static bool TryFindInstalledOsFontName(
            IList<string> candidateNames,
            out string installedOsFontName,
            out string matchedCandidate)
        {
            installedOsFontName = null;
            matchedCandidate = null;
            if (candidateNames == null || candidateNames.Count == 0)
            {
                return false;
            }

            EnsureInstalledOsFontNameIndex();
            if (InstalledOsFontNameIndex.Count == 0)
            {
                return false;
            }

            var normalizedCandidates = candidateNames
                .Select(NormalizeFontKey)
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var candidate in normalizedCandidates)
            {
                if (InstalledOsFontNameIndex.TryGetValue(candidate, out installedOsFontName))
                {
                    matchedCandidate = candidateNames.FirstOrDefault(x =>
                        string.Equals(NormalizeFontKey(x), candidate, StringComparison.OrdinalIgnoreCase));
                    return true;
                }
            }

            var bestScore = int.MaxValue;
            string bestOsFontName = null;
            string bestCandidate = null;
            foreach (var kv in InstalledOsFontNameIndex)
            {
                foreach (var candidate in normalizedCandidates)
                {
                    if (!kv.Key.Contains(candidate) && !candidate.Contains(kv.Key))
                    {
                        continue;
                    }

                    var score = Math.Abs(kv.Key.Length - candidate.Length);
                    if (score >= bestScore)
                    {
                        continue;
                    }

                    bestScore = score;
                    bestOsFontName = kv.Value;
                    bestCandidate = candidate;
                }
            }

            if (bestOsFontName == null)
            {
                return false;
            }

            installedOsFontName = bestOsFontName;
            matchedCandidate = candidateNames.FirstOrDefault(x =>
                string.Equals(NormalizeFontKey(x), bestCandidate, StringComparison.OrdinalIgnoreCase));
            return true;
        }

        private static void EnsureSystemFontIndex()
        {
            if (_systemFontIndexBuilt)
            {
                return;
            }

            _systemFontIndexBuilt = true;
            foreach (var directory in GetSystemFontDirectories())
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                foreach (var fontFile in EnumerateFontFiles(directory))
                {
                    var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fontFile);
                    AddSystemFontAlias(fileNameWithoutExtension, fontFile);
                    AddSystemFontAlias(fileNameWithoutExtension.Replace("-", " "), fontFile);
                    AddSystemFontAlias(fileNameWithoutExtension.Replace("_", " "), fontFile);
                    AddSystemFontAliasesFromFaceInfo(fontFile);
                }
            }
        }

        private static void EnsureInstalledOsFontNameIndex()
        {
            if (_installedOsFontNameIndexBuilt)
            {
                return;
            }

            _installedOsFontNameIndexBuilt = true;
            string[] installedNames;
            try
            {
                installedNames = Font.GetOSInstalledFontNames();
            }
            catch
            {
                installedNames = Array.Empty<string>();
            }

            foreach (var installedName in installedNames)
            {
                if (string.IsNullOrWhiteSpace(installedName))
                {
                    continue;
                }

                AddInstalledOsFontAlias(installedName, installedName);
                AddInstalledOsFontAlias(installedName.Replace("-", " "), installedName);
                AddInstalledOsFontAlias(installedName.Replace("_", " "), installedName);

                var dashIndex = installedName.IndexOf('-');
                if (dashIndex > 0)
                {
                    AddInstalledOsFontAlias(installedName.Substring(0, dashIndex), installedName);
                }

                var regularIndex = installedName.IndexOf(" Regular", StringComparison.OrdinalIgnoreCase);
                if (regularIndex > 0)
                {
                    AddInstalledOsFontAlias(installedName.Substring(0, regularIndex), installedName);
                }
            }
        }

        private static void AddSystemFontAliasesFromFaceInfo(string fontFile)
        {
            if (string.IsNullOrWhiteSpace(fontFile))
            {
                return;
            }

            // TTC files may contain multiple faces. We sample several indexes and stop at first failure
            // after index 0 to avoid expensive full scans while still covering common cases.
            const int MaxFacesToProbe = 8;
            for (var faceIndex = 0; faceIndex < MaxFacesToProbe; faceIndex++)
            {
                FontEngineError error;
                try
                {
                    error = FontEngine.LoadFontFace(fontFile, faceIndex);
                }
                catch
                {
                    break;
                }

                if (error != FontEngineError.Success)
                {
                    if (faceIndex == 0)
                    {
                        return;
                    }

                    break;
                }

                var faceInfo = FontEngine.GetFaceInfo();
                AddSystemFontAlias(faceInfo.familyName, fontFile);
                AddSystemFontAlias(faceInfo.styleName, fontFile);
                AddSystemFontAlias($"{faceInfo.familyName} {faceInfo.styleName}", fontFile);
                AddSystemFontAlias($"{faceInfo.familyName}-{faceInfo.styleName}", fontFile);
            }
        }

        private static IEnumerable<string> GetSystemFontDirectories()
        {
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                var windowsFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                if (!string.IsNullOrWhiteSpace(windowsFonts))
                {
                    directories.Add(windowsFonts);
                }

                var windowsRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (!string.IsNullOrWhiteSpace(windowsRoot))
                {
                    directories.Add(Path.Combine(windowsRoot, "Fonts"));
                }
            }
            else if (Application.platform == RuntimePlatform.OSXEditor)
            {
                directories.Add("/System/Library/Fonts");
                directories.Add("/Library/Fonts");
                var home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                if (!string.IsNullOrWhiteSpace(home))
                {
                    directories.Add(Path.Combine(home, "Library/Fonts"));
                }
            }
            else
            {
                directories.Add("/usr/share/fonts");
                directories.Add("/usr/local/share/fonts");
                var home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                if (!string.IsNullOrWhiteSpace(home))
                {
                    directories.Add(Path.Combine(home, ".fonts"));
                }
            }

            return directories;
        }

        private static IEnumerable<string> EnumerateFontFiles(string rootPath)
        {
            var queue = new Queue<KeyValuePair<string, int>>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            queue.Enqueue(new KeyValuePair<string, int>(rootPath, 0));

            while (queue.Count > 0)
            {
                var currentItem = queue.Dequeue();
                var current = currentItem.Key;
                var depth = currentItem.Value;
                if (depth > MaxFontScanDepth)
                {
                    continue;
                }

                string canonicalPath;
                try
                {
                    canonicalPath = Path.GetFullPath(current);
                }
                catch
                {
                    continue;
                }

                if (!visited.Add(canonicalPath))
                {
                    continue;
                }

                string[] files = null;
                try
                {
                    files = Directory.GetFiles(current);
                }
                catch
                {
                    files = Array.Empty<string>();
                }

                foreach (var file in files)
                {
                    if (!SupportedFontExtensions.Contains(Path.GetExtension(file)))
                    {
                        continue;
                    }

                    yield return file;
                }

                string[] subDirs = null;
                try
                {
                    subDirs = Directory.GetDirectories(current);
                }
                catch
                {
                    subDirs = Array.Empty<string>();
                }

                foreach (var subDir in subDirs)
                {
                    try
                    {
                        var attrs = File.GetAttributes(subDir);
                        if ((attrs & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }
                    }
                    catch
                    {
                        continue;
                    }

                    queue.Enqueue(new KeyValuePair<string, int>(subDir, depth + 1));
                }
            }
        }

        private static void AddSystemFontAlias(string alias, string path)
        {
            var key = NormalizeFontKey(alias);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (!SystemFontIndex.ContainsKey(key))
            {
                SystemFontIndex[key] = path;
            }
        }

        private static void AddInstalledOsFontAlias(string alias, string installedOsFontName)
        {
            var key = NormalizeFontKey(alias);
            if (string.IsNullOrEmpty(key) || string.IsNullOrWhiteSpace(installedOsFontName))
            {
                return;
            }

            if (!InstalledOsFontNameIndex.ContainsKey(key))
            {
                InstalledOsFontNameIndex[key] = installedOsFontName;
            }
        }

        private static List<string> GetAutomaticFallbackCandidates(string text)
        {
            var candidates = new List<string>();
            if (ContainsCjkCharacters(text))
            {
                foreach (var cjkCandidate in GetCjkFallbackCandidates())
                {
                    AddCandidate(candidates, cjkCandidate);
                }
            }

            foreach (var genericCandidate in GenericFallbackCandidates)
            {
                AddCandidate(candidates, genericCandidate);
            }

            return candidates;
        }

        private static string[] GetCjkFallbackCandidates()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                return MacCjkFallbackCandidates;
            }

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                return WindowsCjkFallbackCandidates;
            }

            return LinuxCjkFallbackCandidates;
        }

        private static bool ContainsCjkCharacters(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            foreach (var ch in text)
            {
                if (IsCjkChar(ch))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCjkChar(char c)
        {
            return (c >= '\u3400' && c <= '\u4DBF') ||
                   (c >= '\u4E00' && c <= '\u9FFF') ||
                   (c >= '\uF900' && c <= '\uFAFF');
        }

        private static IEnumerable<string> GetWeightStyleAliases(int fontWeight)
        {
            if (fontWeight <= 0)
            {
                yield break;
            }

            if (fontWeight <= 300)
            {
                yield return "Light";
                yield return "Thin";
                yield return "Book";
                yield break;
            }

            if (fontWeight < 450)
            {
                yield return "Regular";
                yield return "Normal";
                yield break;
            }

            if (fontWeight < 550)
            {
                yield return "Medium";
                yield return "Regular";
                yield break;
            }

            if (fontWeight < 650)
            {
                yield return "SemiBold";
                yield return "Semibold";
                yield return "DemiBold";
                yield break;
            }

            if (fontWeight < 750)
            {
                yield return "Bold";
                yield break;
            }

            if (fontWeight < 850)
            {
                yield return "ExtraBold";
                yield return "Heavy";
                yield break;
            }

            yield return "Black";
            yield return "Heavy";
        }

        private static string NormalizeFontKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }

            return sb.ToString();
        }

        private static bool TryAddToTmpSettingsFallbackList(TMP_Settings settings, TMP_FontAsset fallbackFont)
        {
            if (settings == null || fallbackFont == null)
            {
                return false;
            }

            var settingsType = typeof(TMP_Settings);
            const System.Reflection.BindingFlags Flags =
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Static;

            var property = settingsType.GetProperty("fallbackFontAssets", Flags);
            if (property != null && typeof(IList<TMP_FontAsset>).IsAssignableFrom(property.PropertyType))
            {
                var target = property.GetMethod != null && property.GetMethod.IsStatic ? null : settings;
                var list = property.GetValue(target) as IList<TMP_FontAsset>;
                if (list == null && property.CanWrite)
                {
                    list = new List<TMP_FontAsset>();
                    property.SetValue(target, list);
                }

                if (list != null && !list.Contains(fallbackFont))
                {
                    list.Add(fallbackFont);
                    return true;
                }

                return false;
            }

            var field = settingsType.GetField("fallbackFontAssets", Flags);
            if (field != null && typeof(IList<TMP_FontAsset>).IsAssignableFrom(field.FieldType))
            {
                var target = field.IsStatic ? null : settings;
                var list = field.GetValue(target) as IList<TMP_FontAsset>;
                if (list == null)
                {
                    list = new List<TMP_FontAsset>();
                    field.SetValue(target, list);
                }

                if (!list.Contains(fallbackFont))
                {
                    list.Add(fallbackFont);
                    return true;
                }
            }

            return false;
        }

        public static int RepairImportedFontAssets()
        {
            if (!AssetDatabase.IsValidFolder(ImportedFontsFolder))
            {
                return 0;
            }

            var repairedCount = 0;
            var guids = AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { ImportedFontsFolder });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font == null)
                {
                    continue;
                }

                var expectedName = Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrWhiteSpace(expectedName) &&
                    !string.Equals(font.name, expectedName, StringComparison.Ordinal))
                {
                    font.name = expectedName;
                    EditorUtility.SetDirty(font);
                }

                var wasUsable = IsFontAssetUsable(font, out _);
                if (wasUsable)
                {
                    continue;
                }

                if (TryEnsureUsableFontAsset(font, out var replacement, out _))
                {
                    if (replacement != null && replacement != font)
                    {
                        repairedCount++;
                    }
                }
            }

            AssetDatabase.SaveAssets();

            return repairedCount;
        }

        private static TMP_FontAsset CreateFontAssetWithCompatibleApi(Font sourceFont)
        {
            if (sourceFont == null)
            {
                return null;
            }

            try
            {
                var methods = typeof(TMP_FontAsset)
                    .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .Where(x => x.Name == "CreateFontAsset")
                    .ToList();

                foreach (var method in methods)
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length != 8 || parameters[0].ParameterType != typeof(Font))
                    {
                        continue;
                    }

                    var glyphRenderMode = GetEnumDefaultValue(parameters[3].ParameterType, "SDFAA");
                    var populationMode = GetEnumDefaultValue(parameters[6].ParameterType, "Dynamic");
                    if (glyphRenderMode == null || populationMode == null)
                    {
                        continue;
                    }

                    var args = new object[]
                    {
                        sourceFont,
                        90,
                        9,
                        glyphRenderMode,
                        1024,
                        1024,
                        populationMode,
                        true
                    };

                    var created = method.Invoke(null, args) as TMP_FontAsset;
                    if (created != null)
                    {
                        PrimeDynamicFontAsset(created);
                        return created;
                    }
                }
            }
            catch (Exception e)
            {
                if (ShouldLogOnce("font-create-reflection-failed"))
                {
                    Debug.LogWarning($"[FigmaImporter] Reflection font creation fallback failed: {e.Message}");
                }
            }

            var fallbackCreated = TMP_FontAsset.CreateFontAsset(sourceFont);
            PrimeDynamicFontAsset(fallbackCreated);
            return fallbackCreated;
        }

        private static object GetEnumDefaultValue(Type enumType, string preferredName)
        {
            if (enumType == null || !enumType.IsEnum)
            {
                return null;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(preferredName) &&
                    Enum.GetNames(enumType).Any(x => string.Equals(x, preferredName, StringComparison.OrdinalIgnoreCase)))
                {
                    return Enum.Parse(enumType, preferredName);
                }
            }
            catch
            {
            }

            try
            {
                var values = Enum.GetValues(enumType);
                return values.Length > 0 ? values.GetValue(0) : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryCreateReplacementFontAsset(
            TMP_FontAsset brokenFontAsset,
            out TMP_FontAsset replacementFont,
            out string details)
        {
            replacementFont = null;
            details = null;
            if (brokenFontAsset == null)
            {
                details = "Broken font asset is null.";
                return false;
            }

            var sourceFont = brokenFontAsset.sourceFontFile;
            if (sourceFont == null)
            {
                details = "Source font file is missing.";
                return false;
            }

            var brokenAssetPath = AssetDatabase.GetAssetPath(brokenFontAsset);
            if (string.IsNullOrWhiteSpace(brokenAssetPath))
            {
                details = "Font asset path is unknown.";
                return false;
            }

            try
            {
                var replacementPath = AssetDatabase.GenerateUniqueAssetPath(brokenAssetPath);
                var createdFont = CreateFontAssetWithCompatibleApi(sourceFont);
                if (createdFont == null)
                {
                    details = "TMP font asset creation returned null.";
                    return false;
                }

                var replacementName = Path.GetFileNameWithoutExtension(replacementPath);
                if (!string.IsNullOrWhiteSpace(replacementName))
                {
                    createdFont.name = replacementName;
                }

                AssetDatabase.CreateAsset(createdFont, replacementPath);
                EnsureFontSubAssetsPersisted(createdFont, replacementPath);
                PrimeDynamicFontAsset(createdFont);
                EditorUtility.SetDirty(createdFont);
                AssetDatabase.ImportAsset(
                    replacementPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                AssetDatabase.SaveAssets();

                replacementFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(replacementPath) ?? createdFont;
                details = $"Replacement font asset created at '{replacementPath}'.";
                return true;
            }
            catch (Exception e)
            {
                details = $"Exception during replacement creation: {e.Message}";
                return false;
            }
        }

        private static void PrimeDynamicFontAsset(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
            {
                return;
            }

            try
            {
                fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            }
            catch
            {
            }

            try
            {
                fontAsset.TryAddCharacters("Aa中式云南菌菇火锅", out _);
            }
            catch
            {
            }
        }

        private static void EnsureFontSubAssetsPersisted(TMP_FontAsset fontAsset, string assetPath)
        {
            if (fontAsset == null)
            {
                return;
            }

            if (fontAsset.material != null && !AssetDatabase.Contains(fontAsset.material))
            {
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            var atlasTextures = fontAsset.atlasTextures;
            if (atlasTextures != null)
            {
                foreach (var atlasTexture in atlasTextures)
                {
                    if (atlasTexture == null || AssetDatabase.Contains(atlasTexture))
                    {
                        continue;
                    }

                    AssetDatabase.AddObjectToAsset(atlasTexture, fontAsset);
                }
            }

            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
        }

        private static void ReplaceBrokenFontReferences(TMP_FontAsset oldFont, TMP_FontAsset newFont)
        {
            if (oldFont == null || newFont == null || oldFont == newFont)
            {
                return;
            }

            var fontLinks = GetOrCreateFontLinksAsset();
            if (fontLinks != null && fontLinks.ReplaceFontReference(oldFont, newFont))
            {
                EditorUtility.SetDirty(fontLinks);
            }

            ImportFallbackRegistry.ReplaceFontReference(oldFont, newFont);

            var keys = ResolvedFontCache
                .Where(x => x.Value == oldFont)
                .Select(x => x.Key)
                .ToList();
            foreach (var key in keys)
            {
                ResolvedFontCache[key] = newFont;
            }

            AssetDatabase.SaveAssets();
        }

        private static void LinkFont(FontLinks fontLinks, IEnumerable<string> aliases, TMP_FontAsset fontAsset)
        {
            if (fontLinks == null || aliases == null || fontAsset == null)
            {
                return;
            }

            var changed = false;
            foreach (var alias in aliases)
            {
                if (string.IsNullOrWhiteSpace(alias))
                {
                    continue;
                }
                changed |= fontLinks.Set(alias, fontAsset);
            }

            if (!changed)
            {
                return;
            }

            EditorUtility.SetDirty(fontLinks);
            AssetDatabase.SaveAssets();
        }

        private static void EnsureAssetFolder(string assetFolderPath)
        {
            FigmaPathUtils.EnsureAssetFolderExists(assetFolderPath);
        }
    }
}
