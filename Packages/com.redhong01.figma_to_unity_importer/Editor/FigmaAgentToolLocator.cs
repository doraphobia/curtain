using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FigmaImporter.Editor
{
    internal static class FigmaAgentToolLocator
    {
        internal readonly struct DetectionResult
        {
            public readonly string CodexExecutable;
            public readonly string CursorExecutable;
            public readonly string CursorAgentExecutable;

            public DetectionResult(string codexExecutable, string cursorExecutable, string cursorAgentExecutable)
            {
                CodexExecutable = codexExecutable ?? string.Empty;
                CursorExecutable = cursorExecutable ?? string.Empty;
                CursorAgentExecutable = cursorAgentExecutable ?? string.Empty;
            }
        }

        internal static DetectionResult Detect()
        {
            var codexExecutable = FindExecutable("codex", GetKnownCodexPaths());
            var cursorExecutable = FindExecutable("cursor", GetKnownCursorPaths());
            var cursorAgentExecutable = FindExecutable("cursor-agent", GetKnownCursorAgentPaths());
            return new DetectionResult(codexExecutable, cursorExecutable, cursorAgentExecutable);
        }

        internal static string BuildSummary(string codexExecutable, string cursorAgentExecutable, string cursorExecutable)
        {
            var codexState = IsExecutableAvailable(codexExecutable) ? "Codex=Yes" : "Codex=No";
            var cursorAgentState = IsExecutableAvailable(cursorAgentExecutable) ? "CursorAgent=Yes" : "CursorAgent=No";
            var cursorState = IsExecutableAvailable(cursorExecutable) ? "Cursor=Yes" : "Cursor=No";
            return $"{codexState}, {cursorAgentState}, {cursorState}";
        }

        internal static bool IsExecutableAvailable(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        }

        private static string FindExecutable(string executableName, IEnumerable<string> knownPaths)
        {
            var fromPath = FindExecutableInPath(executableName);
            if (IsExecutableAvailable(fromPath))
            {
                return fromPath;
            }

            foreach (var knownPath in knownPaths)
            {
                if (IsExecutableAvailable(knownPath))
                {
                    return knownPath;
                }
            }

            return string.Empty;
        }

        private static string FindExecutableInPath(string executableName)
        {
            if (string.IsNullOrWhiteSpace(executableName))
            {
                return string.Empty;
            }

            var pathValue = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(pathValue))
            {
                return string.Empty;
            }

            var candidates = BuildExecutableCandidates(executableName);
            var directories = pathValue
                .Split(Path.PathSeparator)
                .Select(x => (x ?? string.Empty).Trim().Trim('"'))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var directory in directories)
            {
                foreach (var candidate in candidates)
                {
                    var fullPath = Path.Combine(directory, candidate);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
            }

            return string.Empty;
        }

        private static List<string> BuildExecutableCandidates(string executableName)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { executableName };
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                return result.ToList();
            }

            if (!string.IsNullOrEmpty(Path.GetExtension(executableName)))
            {
                return result.ToList();
            }

            foreach (var ext in GetWindowsExecutableExtensions())
            {
                result.Add(executableName + ext);
            }

            return result.ToList();
        }

        private static IEnumerable<string> GetWindowsExecutableExtensions()
        {
            var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
            if (!string.IsNullOrWhiteSpace(pathExt))
            {
                foreach (var token in pathExt.Split(';'))
                {
                    var ext = (token ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(ext))
                    {
                        continue;
                    }

                    if (!ext.StartsWith(".", StringComparison.Ordinal))
                    {
                        ext = "." + ext;
                    }

                    yield return ext.ToLowerInvariant();
                }
            }

            yield return ".exe";
            yield return ".cmd";
            yield return ".bat";
        }

        private static IEnumerable<string> GetKnownCodexPaths()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                yield return "/Applications/Codex.app/Contents/Resources/codex";
                yield break;
            }

            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                yield break;
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                yield return Path.Combine(localAppData, "Programs", "Codex", "codex.exe");
                yield return Path.Combine(localAppData, "Programs", "Codex", "resources", "codex.exe");
            }

            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                yield return Path.Combine(programFiles, "Codex", "codex.exe");
                yield return Path.Combine(programFiles, "Codex", "resources", "codex.exe");
            }
        }

        private static IEnumerable<string> GetKnownCursorPaths()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                yield return "/Applications/Cursor.app/Contents/Resources/app/bin/cursor";
                yield break;
            }

            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                yield break;
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                yield return Path.Combine(localAppData, "Programs", "Cursor", "resources", "app", "bin", "cursor.cmd");
                yield return Path.Combine(localAppData, "Programs", "Cursor", "resources", "app", "bin", "cursor.exe");
            }

            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                yield return Path.Combine(programFiles, "Cursor", "resources", "app", "bin", "cursor.cmd");
                yield return Path.Combine(programFiles, "Cursor", "resources", "app", "bin", "cursor.exe");
            }
        }

        private static IEnumerable<string> GetKnownCursorAgentPaths()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                yield return "/Applications/Cursor.app/Contents/Resources/app/bin/cursor-agent";
                yield break;
            }

            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                yield break;
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                yield return Path.Combine(localAppData, "Programs", "Cursor", "resources", "app", "bin", "cursor-agent.cmd");
                yield return Path.Combine(localAppData, "Programs", "Cursor", "resources", "app", "bin", "cursor-agent.exe");
            }

            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                yield return Path.Combine(programFiles, "Cursor", "resources", "app", "bin", "cursor-agent.cmd");
                yield return Path.Combine(programFiles, "Cursor", "resources", "app", "bin", "cursor-agent.exe");
            }
        }
    }
}
