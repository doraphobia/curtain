using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FigmaImporter.Editor
{
    [InitializeOnLoad]
    internal static class FigmaImporterIssueTracker
    {
        private const int MaxEntries = 400;
        private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";
        private static readonly object SyncRoot = new object();
        private static readonly List<ImporterIssueEntry> Entries = new List<ImporterIssueEntry>(MaxEntries);

        static FigmaImporterIssueTracker()
        {
            Application.logMessageReceivedThreaded += OnLogMessage;
        }

        public static bool HasEntries
        {
            get
            {
                lock (SyncRoot)
                {
                    return Entries.Count > 0;
                }
            }
        }

        public static int EntryCount
        {
            get
            {
                lock (SyncRoot)
                {
                    return Entries.Count;
                }
            }
        }

        public static int ErrorLikeCount
        {
            get
            {
                lock (SyncRoot)
                {
                    var count = 0;
                    for (var i = 0; i < Entries.Count; i++)
                    {
                        if (Entries[i].IsErrorLike)
                        {
                            count++;
                        }
                    }

                    return count;
                }
            }
        }

        public static List<ImporterIssueEntry> GetRecentEntries(int maxCount = 120)
        {
            lock (SyncRoot)
            {
                if (Entries.Count == 0)
                {
                    return new List<ImporterIssueEntry>();
                }

                var safeMax = Mathf.Max(1, maxCount);
                var skip = Mathf.Max(0, Entries.Count - safeMax);
                var result = new List<ImporterIssueEntry>(Entries.Count - skip);
                for (var i = skip; i < Entries.Count; i++)
                {
                    result.Add(Entries[i].Clone());
                }

                return result;
            }
        }

        public static void Clear()
        {
            lock (SyncRoot)
            {
                Entries.Clear();
            }
        }

        public static string CreateIssuePack(string outputRootAssetPath, int maxEntries = 120)
        {
            var normalizedRoot = FigmaPathUtils.NormalizeAssetFolderPath(
                string.IsNullOrWhiteSpace(outputRootAssetPath)
                    ? "Assets/FigmaImporter/_Local/IssueHandoff"
                    : outputRootAssetPath);
            FigmaPathUtils.EnsureAssetFolderExists(normalizedRoot);

            var rootAbsolutePath = FigmaPathUtils.ToAbsolutePathFromAssetPath(normalizedRoot);
            Directory.CreateDirectory(rootAbsolutePath);

            var runFolderName = $"Run_{DateTime.Now:yyyyMMdd_HHmmss}";
            var runAbsolutePath = Path.Combine(rootAbsolutePath, runFolderName);
            Directory.CreateDirectory(runAbsolutePath);

            var collectedEntries = GetRecentEntries(maxEntries);

            var registry = ImportFallbackRegistry.GetOrCreate();
            var settings = FigmaImporterSettings.GetInstance();

            var reportMarkdown = BuildIssueReportMarkdown(runAbsolutePath, collectedEntries, registry, settings);
            var plainLog = BuildIssuePlainLog(collectedEntries);
            var settingsSnapshot = BuildSettingsSnapshot(settings);
            var fallbackSnapshot = BuildFallbackSnapshot(registry);
            var entriesJson = JArray.FromObject(collectedEntries);

            File.WriteAllText(Path.Combine(runAbsolutePath, "importer_issue_report.md"), reportMarkdown);
            File.WriteAllText(Path.Combine(runAbsolutePath, "importer_issue_console.log"), plainLog);
            File.WriteAllText(
                Path.Combine(runAbsolutePath, "importer_issue_entries.json"),
                JsonConvert.SerializeObject(entriesJson, Formatting.Indented));
            File.WriteAllText(
                Path.Combine(runAbsolutePath, "importer_settings_snapshot.json"),
                JsonConvert.SerializeObject(settingsSnapshot, Formatting.Indented));
            File.WriteAllText(
                Path.Combine(runAbsolutePath, "fallback_registry_snapshot.json"),
                JsonConvert.SerializeObject(fallbackSnapshot, Formatting.Indented));

            AssetDatabase.Refresh();
            return runAbsolutePath;
        }

        private static JObject BuildSettingsSnapshot(FigmaImporterSettings settings)
        {
            return new JObject
            {
                ["url"] = settings != null ? settings.Url : string.Empty,
                ["rendersPath"] = settings != null ? settings.RendersPath : string.Empty,
                ["tokenPresent"] = settings != null && !string.IsNullOrWhiteSpace(settings.Token),
                ["clientCodePresent"] = settings != null && !string.IsNullOrWhiteSpace(settings.ClientCode)
            };
        }

        private static JObject BuildFallbackSnapshot(ImportFallbackRegistry registry)
        {
            return new JObject
            {
                ["sessionActive"] = registry != null && registry.SessionActive,
                ["lastSessionLabel"] = registry != null ? registry.LastSessionLabel : string.Empty,
                ["lastSessionStartedAt"] = registry != null ? registry.LastSessionStartedAt : string.Empty,
                ["lastSessionFinishedAt"] = registry != null ? registry.LastSessionFinishedAt : string.Empty,
                ["lastSessionMissingFonts"] = registry != null ? registry.LastSessionMissingFonts : 0,
                ["lastSessionSvgFallbacks"] = registry != null ? registry.LastSessionSvgFallbacks : 0,
                ["lastSessionSvgToPngFallbacks"] = registry != null ? registry.LastSessionSvgToPngFallbacks : 0,
                ["lastSessionMissingIssues"] = registry != null ? registry.LastSessionMissingIssues : 0,
                ["missingFontsTotal"] = registry != null && registry.MissingFonts != null ? registry.MissingFonts.Count : 0,
                ["svgFallbacksTotal"] = registry != null && registry.SvgFallbacks != null ? registry.SvgFallbacks.Count : 0,
                ["missingIssuesTotal"] = registry != null && registry.MissingIssues != null ? registry.MissingIssues.Count : 0
            };
        }

        private static string BuildIssueReportMarkdown(
            string outputDirectory,
            IList<ImporterIssueEntry> entries,
            ImportFallbackRegistry registry,
            FigmaImporterSettings settings)
        {
            var errorLike = entries.Count(x => x != null && x.IsErrorLike);
            var warningCount = entries.Count(x => x != null && x.Type == LogType.Warning.ToString());

            var sb = new StringBuilder();
            sb.AppendLine("# Figma Importer Issue Handoff Pack");
            sb.AppendLine();
            sb.AppendLine($"Generated: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"Output: `{outputDirectory}`");
            sb.AppendLine();
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine($"- Captured importer issue entries: {entries.Count}");
            sb.AppendLine($"- Error/Exception entries: {errorLike}");
            sb.AppendLine($"- Warning entries: {warningCount}");
            sb.AppendLine($"- Last session fallback summary: fonts={registry?.LastSessionMissingFonts ?? 0}, svg={registry?.LastSessionSvgFallbacks ?? 0}, svg->png={registry?.LastSessionSvgToPngFallbacks ?? 0}, other={registry?.LastSessionMissingIssues ?? 0}");
            sb.AppendLine($"- Token configured: {(settings != null && !string.IsNullOrWhiteSpace(settings.Token) ? "yes" : "no")}");
            sb.AppendLine();
            sb.AppendLine("## Files");
            sb.AppendLine();
            sb.AppendLine("- `importer_issue_report.md`");
            sb.AppendLine("- `importer_issue_console.log`");
            sb.AppendLine("- `importer_issue_entries.json`");
            sb.AppendLine("- `importer_settings_snapshot.json`");
            sb.AppendLine("- `fallback_registry_snapshot.json`");
            sb.AppendLine();
            sb.AppendLine("## Recent Error/Exception Entries");
            sb.AppendLine();

            var printed = 0;
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (entry == null || !entry.IsErrorLike)
                {
                    continue;
                }

                sb.AppendLine($"- [{entry.Time}] {entry.Type}: {TrimSingleLine(entry.Message, 220)}");
                printed++;
                if (printed >= 12)
                {
                    break;
                }
            }

            if (printed == 0)
            {
                sb.AppendLine("- (No Error/Exception entries in current capture buffer)");
            }

            return sb.ToString();
        }

        private static string BuildIssuePlainLog(IList<ImporterIssueEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null)
                {
                    continue;
                }

                sb.Append('[');
                sb.Append(entry.Time);
                sb.Append("] ");
                sb.Append(entry.Type);
                sb.Append(": ");
                sb.AppendLine(entry.Message ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(entry.StackTrace))
                {
                    sb.AppendLine(entry.StackTrace);
                }
            }

            return sb.ToString();
        }

        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (!IsSupportedType(type))
            {
                return;
            }

            if (!IsImporterRelated(condition, stackTrace))
            {
                return;
            }

            var entry = new ImporterIssueEntry
            {
                Time = DateTime.Now.ToString(TimestampFormat, CultureInfo.InvariantCulture),
                Type = type.ToString(),
                Message = condition ?? string.Empty,
                StackTrace = stackTrace ?? string.Empty,
                IsErrorLike = type == LogType.Error || type == LogType.Assert || type == LogType.Exception
            };

            lock (SyncRoot)
            {
                Entries.Add(entry);
                if (Entries.Count > MaxEntries)
                {
                    var overflow = Entries.Count - MaxEntries;
                    Entries.RemoveRange(0, overflow);
                }
            }
        }

        private static bool IsSupportedType(LogType type)
        {
            return type == LogType.Error ||
                   type == LogType.Assert ||
                   type == LogType.Exception ||
                   type == LogType.Warning;
        }

        private static bool IsImporterRelated(string condition, string stackTrace)
        {
            if (ContainsAny(condition))
            {
                return true;
            }

            return ContainsAny(stackTrace);
        }

        private static bool ContainsAny(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return text.IndexOf("[FigmaImporter]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("FigmaImporter.Editor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("com.redhong01.figma_to_unity_importer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("/Packages/com.redhong01.figma_to_unity_importer/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string TrimSingleLine(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (normalized.Length <= maxLength)
            {
                return normalized;
            }

            return normalized.Substring(0, maxLength) + "...";
        }
    }

    [Serializable]
    internal sealed class ImporterIssueEntry
    {
        public string Time;
        public string Type;
        public string Message;
        public string StackTrace;
        public bool IsErrorLike;

        public ImporterIssueEntry Clone()
        {
            return new ImporterIssueEntry
            {
                Time = Time,
                Type = Type,
                Message = Message,
                StackTrace = StackTrace,
                IsErrorLike = IsErrorLike
            };
        }
    }
}
