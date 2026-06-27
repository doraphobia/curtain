using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace FigmaImporter.Editor
{
    internal enum SvgFallbackType
    {
        Unknown = 0,
        RasterPayloadToPng = 1,
        SvgImportFailedToPng = 2
    }

    [Serializable]
    internal class MissingFontEntry
    {
        public string id;
        public List<string> candidateNames = new List<string>();
        public string fontFamily;
        public string postScriptName;
        public int fontWeight;
        public string sampleText;
        public int occurrences;
        public int sessionOccurrences;
        public string lastDetails;
        public string lastSeenAt;
        public TMP_FontAsset suggestedFont;
        public TMP_FontAsset assignedFont;
    }

    [Serializable]
    internal class SvgFallbackEntry
    {
        public string nodeId;
        public string nodeName;
        public string generatedSpritePath;
        public SvgFallbackType fallbackType;
        public string lastReason;
        public int occurrences;
        public int sessionOccurrences;
        public string lastSeenAt;
        public Sprite assignedSprite;
    }

    [Serializable]
    internal class MissingIssueEntry
    {
        public string id;
        public string category;
        public string key;
        public string nodeId;
        public string nodeName;
        public int occurrences;
        public int sessionOccurrences;
        public string lastDetails;
        public string lastSeenAt;
    }

    internal class ImportFallbackRegistry : ScriptableObject
    {
        private const string RegistryAssetPath = FigmaPathUtils.LocalRegistryAssetPath;

        [SerializeField] private List<MissingFontEntry> _missingFonts = new List<MissingFontEntry>();
        [SerializeField] private List<SvgFallbackEntry> _svgFallbacks = new List<SvgFallbackEntry>();
        [SerializeField] private List<MissingIssueEntry> _missingIssues = new List<MissingIssueEntry>();
        [SerializeField] private string _lastSessionLabel = string.Empty;
        [SerializeField] private string _lastSessionStartedAt = string.Empty;
        [SerializeField] private string _lastSessionFinishedAt = string.Empty;
        [SerializeField] private int _lastSessionMissingFonts;
        [SerializeField] private int _lastSessionSvgFallbacks;
        [SerializeField] private int _lastSessionSvgToPngFallbacks;
        [SerializeField] private int _lastSessionMissingIssues;
        [SerializeField] private bool _sessionActive;

        public List<MissingFontEntry> MissingFonts => _missingFonts;
        public List<SvgFallbackEntry> SvgFallbacks => _svgFallbacks;
        public List<MissingIssueEntry> MissingIssues => _missingIssues;
        public string LastSessionLabel => _lastSessionLabel;
        public string LastSessionStartedAt => _lastSessionStartedAt;
        public string LastSessionFinishedAt => _lastSessionFinishedAt;
        public int LastSessionMissingFonts => _lastSessionMissingFonts;
        public int LastSessionSvgFallbacks => _lastSessionSvgFallbacks;
        public int LastSessionSvgToPngFallbacks => _lastSessionSvgToPngFallbacks;
        public int LastSessionMissingIssues => _lastSessionMissingIssues;
        public bool SessionActive => _sessionActive;

        public static ImportFallbackRegistry GetOrCreate()
        {
            var localRegistry = AssetDatabase.LoadAssetAtPath<ImportFallbackRegistry>(RegistryAssetPath);
            if (localRegistry != null)
            {
                return localRegistry;
            }

            FigmaPathUtils.EnsureAssetFolderExists(FigmaPathUtils.LocalRootAssetPath);
            FigmaPathUtils.EnsureAssetFolderExists(FigmaPathUtils.LocalEditorFolderAssetPath);

            var legacyRegistry = AssetDatabase.LoadAssetAtPath<ImportFallbackRegistry>(FigmaPathUtils.LegacySharedRegistryAssetPath);
            if (legacyRegistry != null && AssetDatabase.CopyAsset(FigmaPathUtils.LegacySharedRegistryAssetPath, RegistryAssetPath))
            {
                AssetDatabase.ImportAsset(RegistryAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var migratedRegistry = AssetDatabase.LoadAssetAtPath<ImportFallbackRegistry>(RegistryAssetPath);
                if (migratedRegistry != null)
                {
                    AssetDatabase.SaveAssets();
                    return migratedRegistry;
                }
            }

            var guids = AssetDatabase.FindAssets("t:ImportFallbackRegistry");
            if (guids != null)
            {
                foreach (var guid in guids)
                {
                    var existingPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.Equals(existingPath, RegistryAssetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var existingAsset = AssetDatabase.LoadAssetAtPath<ImportFallbackRegistry>(existingPath);
                    if (existingAsset == null)
                    {
                        continue;
                    }

                    var copied = AssetDatabase.CopyAsset(existingPath, RegistryAssetPath);
                    if (!copied)
                    {
                        break;
                    }

                    AssetDatabase.ImportAsset(RegistryAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    var copiedRegistry = AssetDatabase.LoadAssetAtPath<ImportFallbackRegistry>(RegistryAssetPath);
                    if (copiedRegistry != null)
                    {
                        AssetDatabase.SaveAssets();
                        return copiedRegistry;
                    }
                }
            }

            var asset = CreateInstance<ImportFallbackRegistry>();
            var registryFolder = System.IO.Path.GetDirectoryName(RegistryAssetPath)?.Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(registryFolder))
            {
                FigmaPathUtils.EnsureAssetFolderExists(registryFolder);
            }
            AssetDatabase.CreateAsset(asset, RegistryAssetPath);
            AssetDatabase.SaveAssets();
            return asset;
        }

        public static void BeginGenerationSession(string sessionLabel)
        {
            var registry = GetOrCreate();
            registry._sessionActive = true;
            registry._lastSessionLabel = string.IsNullOrWhiteSpace(sessionLabel) ? "Generate nodes" : sessionLabel.Trim();
            registry._lastSessionStartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            registry._lastSessionFinishedAt = string.Empty;
            registry._lastSessionMissingFonts = 0;
            registry._lastSessionSvgFallbacks = 0;
            registry._lastSessionSvgToPngFallbacks = 0;
            registry._lastSessionMissingIssues = 0;

            ResetSessionCounters(registry);
            SaveRegistry(registry);
        }

        public static void EndGenerationSession()
        {
            var registry = GetOrCreate();
            if (!registry._sessionActive)
            {
                return;
            }

            registry._sessionActive = false;
            registry._lastSessionFinishedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            SaveRegistry(registry);
        }

        public static bool TryGetFontOverride(IList<string> candidates, out TMP_FontAsset fontAsset)
        {
            fontAsset = null;
            if (candidates == null || candidates.Count == 0)
            {
                return false;
            }

            var registry = GetOrCreate();
            var normalized = ToNormalizedCandidates(candidates);
            if (normalized.Count == 0)
            {
                return false;
            }

            foreach (var entry in registry._missingFonts)
            {
                if (entry?.assignedFont == null || entry.candidateNames == null || entry.candidateNames.Count == 0)
                {
                    continue;
                }

                if (!HasAnyCandidateMatch(entry.candidateNames, normalized))
                {
                    continue;
                }

                fontAsset = entry.assignedFont;
                return true;
            }

            return false;
        }

        public static void ReportMissingFont(
            IList<string> candidates,
            Style style,
            string sampleText,
            TMP_FontAsset suggestedFont,
            string details)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return;
            }

            var registry = GetOrCreate();
            var id = BuildFontEntryId(candidates);
            var entry = registry._missingFonts.FirstOrDefault(x =>
                string.Equals(x.id, id, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                entry = new MissingFontEntry
                {
                    id = id
                };
                registry._missingFonts.Add(entry);
            }

            entry.candidateNames = candidates
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            entry.fontFamily = style?.fontFamily ?? string.Empty;
            entry.postScriptName = style?.fontPostScriptName ?? string.Empty;
            entry.fontWeight = style?.fontWeight ?? 0;
            entry.sampleText = string.IsNullOrWhiteSpace(sampleText) ? entry.sampleText : sampleText;
            entry.occurrences++;
            if (registry._sessionActive)
            {
                entry.sessionOccurrences++;
                registry._lastSessionMissingFonts++;
            }
            entry.lastDetails = details ?? string.Empty;
            entry.lastSeenAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            if (entry.suggestedFont == null && suggestedFont != null)
            {
                entry.suggestedFont = suggestedFont;
            }

            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
        }

        public static void ReportSvgFallback(
            string nodeId,
            string nodeName,
            string generatedSpritePath,
            string reason,
            SvgFallbackType fallbackType = SvgFallbackType.Unknown)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return;
            }

            var registry = GetOrCreate();
            var entry = registry._svgFallbacks.FirstOrDefault(x =>
                string.Equals(x.nodeId, nodeId, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                entry = new SvgFallbackEntry
                {
                    nodeId = nodeId
                };
                registry._svgFallbacks.Add(entry);
            }

            entry.nodeName = nodeName ?? string.Empty;
            entry.generatedSpritePath = generatedSpritePath ?? string.Empty;
            entry.fallbackType = fallbackType == SvgFallbackType.Unknown
                ? InferSvgFallbackType(reason, entry.fallbackType)
                : fallbackType;
            entry.lastReason = reason ?? string.Empty;
            entry.occurrences++;
            if (registry._sessionActive)
            {
                entry.sessionOccurrences++;
                registry._lastSessionSvgFallbacks++;
                if (IsSvgToPngFallback(entry))
                {
                    registry._lastSessionSvgToPngFallbacks++;
                }
            }
            entry.lastSeenAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
        }

        public static void ReportMissingIssue(
            string category,
            string key,
            string details,
            string nodeId = null,
            string nodeName = null)
        {
            var safeCategory = string.IsNullOrWhiteSpace(category) ? "Unknown" : category.Trim();
            var safeKey = string.IsNullOrWhiteSpace(key) ? "unknown" : key.Trim();
            var safeNodeId = nodeId ?? string.Empty;
            var safeNodeName = nodeName ?? string.Empty;

            var registry = GetOrCreate();
            var id = BuildIssueId(safeCategory, safeKey, safeNodeId);
            var entry = registry._missingIssues.FirstOrDefault(x =>
                string.Equals(x.id, id, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                entry = new MissingIssueEntry
                {
                    id = id,
                    category = safeCategory,
                    key = safeKey
                };
                registry._missingIssues.Add(entry);
            }

            entry.category = safeCategory;
            entry.key = safeKey;
            entry.nodeId = safeNodeId;
            entry.nodeName = safeNodeName;
            entry.lastDetails = details ?? string.Empty;
            entry.lastSeenAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            entry.occurrences++;
            if (registry._sessionActive)
            {
                entry.sessionOccurrences++;
                registry._lastSessionMissingIssues++;
            }

            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
        }

        public static bool TryGetSvgOverride(string nodeId, out Sprite sprite)
        {
            sprite = null;
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return false;
            }

            var registry = GetOrCreate();
            var entry = registry._svgFallbacks.FirstOrDefault(x =>
                string.Equals(x.nodeId, nodeId, StringComparison.OrdinalIgnoreCase));
            if (entry?.assignedSprite == null)
            {
                return false;
            }

            sprite = entry.assignedSprite;
            return true;
        }

        public static bool IsSvgToPngFallback(SvgFallbackEntry entry)
        {
            if (entry == null)
            {
                return false;
            }

            if (entry.fallbackType == SvgFallbackType.RasterPayloadToPng)
            {
                return true;
            }

            return InferSvgFallbackType(entry.lastReason, entry.fallbackType) == SvgFallbackType.RasterPayloadToPng;
        }

        public static TMP_FontAsset TryAutoAssignFont(MissingFontEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            if (entry.assignedFont != null)
            {
                return entry.assignedFont;
            }

            if (entry.suggestedFont != null)
            {
                entry.assignedFont = entry.suggestedFont;
                return entry.assignedFont;
            }

            var fontLinks = FontAssetResolver.GetOrCreateFontLinksAsset();
            var resolved = FontAssetResolver.ResolveOrImport(
                fontLinks,
                entry.candidateNames,
                out _,
                out _);
            if (resolved != null)
            {
                entry.assignedFont = resolved;
                return resolved;
            }

            resolved = FontAssetResolver.ResolveAutomaticFallbackFont(
                fontLinks,
                entry.sampleText,
                out _,
                out _);
            if (resolved != null)
            {
                entry.assignedFont = resolved;
                return resolved;
            }

            return null;
        }

        public static Sprite TryAutoAssignSvgSprite(SvgFallbackEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            if (entry.assignedSprite != null)
            {
                return entry.assignedSprite;
            }

            if (!string.IsNullOrWhiteSpace(entry.generatedSpritePath))
            {
                var generated = AssetDatabase.LoadAssetAtPath<Sprite>(entry.generatedSpritePath);
                if (generated != null)
                {
                    entry.assignedSprite = generated;
                    return generated;
                }
            }

            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(entry.nodeName))
            {
                candidates.Add(entry.nodeName);
                candidates.Add(FigmaPathUtils.SanitizeFileName(entry.nodeName, "node"));
            }

            if (!string.IsNullOrWhiteSpace(entry.nodeId))
            {
                candidates.Add(entry.nodeId.Replace(':', '_'));
            }

            foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var guids = AssetDatabase.FindAssets($"{candidate} t:Sprite");
                if (guids == null || guids.Length == 0)
                {
                    continue;
                }

                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    if (sprite == null)
                    {
                        continue;
                    }

                    entry.assignedSprite = sprite;
                    return sprite;
                }
            }

            return null;
        }

        public static void ApplyFontAssignmentsToFontLinks(ImportFallbackRegistry registry = null)
        {
            registry ??= GetOrCreate();
            var fontLinks = FontAssetResolver.GetOrCreateFontLinksAsset();
            if (fontLinks == null)
            {
                return;
            }

            var changed = false;
            foreach (var entry in registry._missingFonts)
            {
                if (entry?.assignedFont == null || entry.candidateNames == null || entry.candidateNames.Count == 0)
                {
                    continue;
                }

                foreach (var alias in entry.candidateNames)
                {
                    changed |= fontLinks.Set(alias, entry.assignedFont);
                }
            }

            if (!changed)
            {
                return;
            }

            EditorUtility.SetDirty(fontLinks);
            AssetDatabase.SaveAssets();
        }

        public static bool ReplaceFontReference(TMP_FontAsset oldFont, TMP_FontAsset newFont)
        {
            if (oldFont == null || newFont == null)
            {
                return false;
            }

            var registry = GetOrCreate();
            var changed = false;
            if (registry._missingFonts != null)
            {
                foreach (var entry in registry._missingFonts)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    if (entry.suggestedFont == oldFont)
                    {
                        entry.suggestedFont = newFont;
                        changed = true;
                    }

                    if (entry.assignedFont == oldFont)
                    {
                        entry.assignedFont = newFont;
                        changed = true;
                    }
                }
            }

            if (!changed)
            {
                return false;
            }

            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            return true;
        }

        public static void SaveRegistry(ImportFallbackRegistry registry = null)
        {
            registry ??= GetOrCreate();
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
        }

        private static void ResetSessionCounters(ImportFallbackRegistry registry)
        {
            if (registry == null)
            {
                return;
            }

            if (registry._missingFonts != null)
            {
                foreach (var entry in registry._missingFonts)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    entry.sessionOccurrences = 0;
                }
            }

            if (registry._svgFallbacks != null)
            {
                foreach (var entry in registry._svgFallbacks)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    entry.sessionOccurrences = 0;
                }
            }

            if (registry._missingIssues == null)
            {
                return;
            }

            foreach (var entry in registry._missingIssues)
            {
                if (entry == null)
                {
                    continue;
                }

                entry.sessionOccurrences = 0;
            }
        }

        private static bool HasAnyCandidateMatch(IList<string> entryCandidates, IList<string> normalizedCandidates)
        {
            if (entryCandidates == null || entryCandidates.Count == 0 || normalizedCandidates == null || normalizedCandidates.Count == 0)
            {
                return false;
            }

            var entryNormalized = ToNormalizedCandidates(entryCandidates);
            foreach (var left in entryNormalized)
            {
                foreach (var right in normalizedCandidates)
                {
                    if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string BuildFontEntryId(IList<string> candidates)
        {
            var normalized = ToNormalizedCandidates(candidates);
            if (normalized.Count == 0)
            {
                return "unknown";
            }

            normalized.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join("|", normalized);
        }

        private static string BuildIssueId(string category, string key, string nodeId)
        {
            return string.Join("|",
                NormalizeKey(category),
                NormalizeKey(key),
                NormalizeKey(nodeId));
        }

        private static List<string> ToNormalizedCandidates(IList<string> candidates)
        {
            var normalized = new List<string>();
            if (candidates == null)
            {
                return normalized;
            }

            foreach (var candidate in candidates)
            {
                var value = NormalizeKey(candidate);
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                if (!normalized.Contains(value))
                {
                    normalized.Add(value);
                }
            }

            return normalized;
        }

        private static string NormalizeKey(string value)
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

        private static SvgFallbackType InferSvgFallbackType(string reason, SvgFallbackType existingValue)
        {
            if (existingValue != SvgFallbackType.Unknown)
            {
                return existingValue;
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                return SvgFallbackType.Unknown;
            }

            var normalized = reason.ToLowerInvariant();
            if (normalized.Contains("raster") || normalized.Contains("image/jpg") || normalized.Contains("image/jpeg") || normalized.Contains("image/png"))
            {
                return SvgFallbackType.RasterPayloadToPng;
            }

            if (normalized.Contains("import failed"))
            {
                return SvgFallbackType.SvgImportFailedToPng;
            }

            return SvgFallbackType.Unknown;
        }
    }
}
