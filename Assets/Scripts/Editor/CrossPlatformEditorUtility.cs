#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace DuoCurtain.Editor
{
    /// <summary>
    /// Shared guards and path/process helpers for Windows/macOS editor development.
    /// Prefer runtime <see cref="Application.platform"/> checks over compile-time
    /// UNITY_EDITOR_OSX/UNITY_EDITOR_WIN when guarding editor-only behavior that must
    /// compile on every host platform.
    /// </summary>
    public static class CrossPlatformEditorUtility
    {
        public static bool IsMacEditor =>
            Application.platform == RuntimePlatform.OSXEditor;

        public static bool IsWindowsEditor =>
            Application.platform == RuntimePlatform.WindowsEditor;

        public static bool CanRunMacPlayerSmokeTest => IsMacEditor;

        public static bool CanRunWindowsPlayerSmokeTest => IsWindowsEditor;

        public static string NormalizePathSeparators(string path)
        {
            return string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');
        }

        public static string GetProjectRootPath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        public static string ToProjectRelativePath(string fullPath)
        {
            string projectRoot = NormalizePathSeparators(GetProjectRootPath()).TrimEnd('/');
            string normalized = NormalizePathSeparators(Path.GetFullPath(fullPath));
            if (normalized.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
                return normalized.Substring(projectRoot.Length + 1);
            return normalized;
        }

        public static string ResolveGitExecutable()
        {
            string fromPath = FindExecutableInPath("git");
            if (!string.IsNullOrEmpty(fromPath))
                return fromPath;

            if (IsWindowsEditor)
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string[] candidates =
                {
                    Path.Combine(programFiles, "Git", "cmd", "git.exe"),
                    Path.Combine(programFiles, "Git", "bin", "git.exe"),
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                        "Git",
                        "cmd",
                        "git.exe")
                };

                for (int i = 0; i < candidates.Length; i++)
                {
                    if (File.Exists(candidates[i]))
                        return candidates[i];
                }
            }

            if (IsMacEditor)
            {
                string[] candidates =
                {
                    "/opt/homebrew/bin/git",
                    "/usr/local/bin/git",
                    "/usr/bin/git"
                };

                for (int i = 0; i < candidates.Length; i++)
                {
                    if (File.Exists(candidates[i]))
                        return candidates[i];
                }
            }

            return string.Empty;
        }

        public static bool TryCreateGitProcessStartInfo(string arguments, out ProcessStartInfo startInfo)
        {
            startInfo = null;
            string gitExecutable = ResolveGitExecutable();
            if (string.IsNullOrEmpty(gitExecutable))
                return false;

            startInfo = new ProcessStartInfo
            {
                FileName = gitExecutable,
                Arguments = arguments,
                WorkingDirectory = Directory.GetCurrentDirectory(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            return true;
        }

        public static string RunGit(string arguments, int timeoutMilliseconds = 4000)
        {
            if (!TryCreateGitProcessStartInfo(arguments, out ProcessStartInfo startInfo))
                return string.Empty;

            try
            {
                using Process process = Process.Start(startInfo);
                if (process == null)
                    return string.Empty;

                if (!process.WaitForExit(timeoutMilliseconds))
                {
                    TryKillProcess(process);
                    return string.Empty;
                }

                if (process.ExitCode != 0)
                    return string.Empty;

                return process.StandardOutput.ReadToEnd().Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        public static IReadOnlyList<string> GetSystemFontSearchRoots()
        {
            List<string> roots = new List<string>();
            if (IsWindowsEditor)
            {
                string windowsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (!string.IsNullOrWhiteSpace(windowsFolder))
                    roots.Add(Path.Combine(windowsFolder, "Fonts"));

                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrWhiteSpace(localAppData))
                    roots.Add(Path.Combine(localAppData, "Microsoft", "Windows", "Fonts"));
            }
            else if (IsMacEditor)
            {
                roots.Add("/System/Library/Fonts");
                roots.Add("/System/Library/Fonts/Supplemental");
                roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Fonts"));
                roots.Add("/Library/Fonts");
            }

            return roots;
        }

        public static IReadOnlyList<string> GetCjkFontFileCandidates()
        {
            if (IsWindowsEditor)
            {
                return new[]
                {
                    "msyh.ttc",
                    "msyhbd.ttc",
                    "msyhl.ttc",
                    "simhei.ttf",
                    "simsun.ttc",
                    "simsunb.ttf",
                    "Arial Unicode.ttf"
                };
            }

            if (IsMacEditor)
            {
                return new[]
                {
                    "Hiragino Sans GB.ttc",
                    "STHeiti Medium.ttc",
                    "PingFang.ttc",
                    "Arial Unicode.ttf"
                };
            }

            return new[] { "NotoSansCJK-Regular.ttc", "Arial Unicode.ttf" };
        }

        public static string FindFirstExistingSystemFontFile(IEnumerable<string> fileNames)
        {
            IReadOnlyList<string> searchRoots = GetSystemFontSearchRoots();
            if (fileNames == null)
                return null;

            foreach (string fileName in fileNames)
            {
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;

                for (int i = 0; i < searchRoots.Count; i++)
                {
                    string path = Path.Combine(searchRoots[i], fileName);
                    if (File.Exists(path))
                        return path;
                }
            }

            return null;
        }

        public static string FindSystemCjkFontPath()
        {
            return FindFirstExistingSystemFontFile(GetCjkFontFileCandidates());
        }

        public static string FindSystemFontByNamePattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return null;

            IReadOnlyList<string> searchRoots = GetSystemFontSearchRoots();
            for (int i = 0; i < searchRoots.Count; i++)
            {
                string root = searchRoots[i];
                if (!Directory.Exists(root))
                    continue;

                try
                {
                    foreach (string path in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
                    {
                        string extension = Path.GetExtension(path);
                        if (string.Equals(extension, ".ttf", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(extension, ".otf", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(extension, ".ttc", StringComparison.OrdinalIgnoreCase))
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

        private static string FindExecutableInPath(string executableName)
        {
            if (string.IsNullOrWhiteSpace(executableName))
                return string.Empty;

            string pathValue = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(pathValue))
                return string.Empty;

            HashSet<string> candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { executableName };
            if (IsWindowsEditor && string.IsNullOrEmpty(Path.GetExtension(executableName)))
            {
                candidates.Add(executableName + ".exe");
                candidates.Add(executableName + ".cmd");
            }

            string[] directories = pathValue.Split(Path.PathSeparator);
            for (int i = 0; i < directories.Length; i++)
            {
                string directory = (directories[i] ?? string.Empty).Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(directory))
                    continue;

                foreach (string candidate in candidates)
                {
                    string fullPath = Path.Combine(directory, candidate);
                    if (File.Exists(fullPath))
                        return fullPath;
                }
            }

            return string.Empty;
        }

        private static void TryKillProcess(Process process)
        {
            if (process == null)
                return;

            try
            {
                process.Kill();
            }
            catch
            {
                // ignored
            }
        }
    }
}
#endif
