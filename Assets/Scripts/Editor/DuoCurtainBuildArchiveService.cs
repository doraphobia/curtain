#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Curtain.Settings;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DuoCurtain.Editor
{
    public static class DuoCurtainBuildArchiveService
    {
        public const string OutputRootFolder = "Builds";
        public const string StagingFolderName = ".BuildStaging";
        public const string ArchiveFolderName = "Archive";
        public const string ManifestFileName = ".duo_curtain_build_manifest.json";
        public const string BuildArchiveSettingsAssetPath = "Assets/Curtain/Settings/BuildArchiveSettings.asset";

        public readonly struct PlatformBuildInfo
        {
            public readonly string platformId;
            public readonly string displayName;
            public readonly string folderName;
            public readonly string primaryArtifactName;

            public PlatformBuildInfo(string platformId, string displayName, string folderName, string primaryArtifactName)
            {
                this.platformId = platformId;
                this.displayName = displayName;
                this.folderName = folderName;
                this.primaryArtifactName = primaryArtifactName;
            }
        }

        public readonly struct BuildRecord
        {
            public readonly bool isLatest;
            public readonly string platformId;
            public readonly string displayName;
            public readonly string folderPath;
            public readonly string primaryArtifactPath;
            public readonly DateTime timestampUtc;
            public readonly DuoCurtainBuildManifest manifest;

            public BuildRecord(
                bool isLatest,
                string platformId,
                string displayName,
                string folderPath,
                string primaryArtifactPath,
                DateTime timestampUtc,
                DuoCurtainBuildManifest manifest)
            {
                this.isLatest = isLatest;
                this.platformId = platformId;
                this.displayName = displayName;
                this.folderPath = folderPath;
                this.primaryArtifactPath = primaryArtifactPath;
                this.timestampUtc = timestampUtc;
                this.manifest = manifest;
            }
        }

        private static readonly PlatformBuildInfo[] Platforms =
        {
            new PlatformBuildInfo("Mac", "Mac", "Curtain_Mac", "Curtain_Mac.app"),
            new PlatformBuildInfo("Windows", "Windows", "Curtain_Windows", "Curtain_Windows.exe"),
            new PlatformBuildInfo("WebGL", "WebGL", "Curtain_Web", "index.html")
        };

        public static IReadOnlyList<PlatformBuildInfo> GetPlatforms() => Platforms;

        public static int GetMaxArchivesPerPlatform()
        {
            BuildArchiveSettings settings = AssetDatabase.LoadAssetAtPath<BuildArchiveSettings>(BuildArchiveSettingsAssetPath);
            return settings != null ? Mathf.Max(1, settings.maxArchivesPerPlatform) : 5;
        }

        public static void ArchiveExistingBuild(string platformOutputRoot, string platformId, string displayName)
        {
            if (!Directory.Exists(platformOutputRoot))
            {
                Directory.CreateDirectory(platformOutputRoot);
                return;
            }

            string[] entries = Directory.GetFileSystemEntries(platformOutputRoot)
                .Where(path =>
                {
                    string name = Path.GetFileName(path);
                    return !string.Equals(name, ArchiveFolderName, StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(name, ManifestFileName, StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(name, ".DS_Store", StringComparison.OrdinalIgnoreCase);
                })
                .ToArray();
            if (entries.Length == 0)
                return;

            string manifestSourcePath = Path.Combine(platformOutputRoot, ManifestFileName);
            DuoCurtainBuildManifest previousManifest = TryReadManifest(manifestSourcePath);

            string archiveRoot = Path.Combine(platformOutputRoot, ArchiveFolderName);
            Directory.CreateDirectory(archiveRoot);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string archiveFolder = GetUniquePath(Path.Combine(archiveRoot, timestamp));
            Directory.CreateDirectory(archiveFolder);

            for (int i = 0; i < entries.Length; i++)
            {
                string destination = GetUniquePath(Path.Combine(archiveFolder, Path.GetFileName(entries[i])));
                FileUtil.MoveFileOrDirectory(entries[i], destination);
            }

            DuoCurtainBuildManifest archiveManifest = previousManifest ?? CreateManifest(platformId, displayName, "archive", false);
            archiveManifest.role = "archive";
            archiveManifest.archiveFolderName = Path.GetFileName(archiveFolder);
            archiveManifest.archivedUtc = DateTime.UtcNow.ToString("o");
            if (string.IsNullOrEmpty(archiveManifest.createdUtc))
                archiveManifest.createdUtc = archiveManifest.archivedUtc;

            WriteManifest(Path.Combine(archiveFolder, ManifestFileName), archiveManifest);

            if (File.Exists(manifestSourcePath))
                File.Delete(manifestSourcePath);

            PruneArchives(platformOutputRoot, GetMaxArchivesPerPlatform());

            LogInfo("[DuoCurtainBuildArchive] Archived previous " + displayName + " build to " + ToProjectRelativePath(archiveFolder) + ".");
        }

        public static void WriteLatestManifest(string platformOutputRoot, string platformId, string displayName, bool developmentBuild)
        {
            DuoCurtainBuildManifest manifest = CreateManifest(platformId, displayName, "latest", developmentBuild);
            WriteManifest(Path.Combine(platformOutputRoot, ManifestFileName), manifest);
        }

        public static void PruneArchives(string platformOutputRoot, int maxKeep)
        {
            string archiveRoot = Path.Combine(platformOutputRoot, ArchiveFolderName);
            if (!Directory.Exists(archiveRoot))
                return;

            maxKeep = Mathf.Max(1, maxKeep);
            string[] archiveFolders = Directory.GetDirectories(archiveRoot)
                .OrderByDescending(path => Path.GetFileName(path), StringComparer.Ordinal)
                .ToArray();

            for (int i = maxKeep; i < archiveFolders.Length; i++)
            {
                string folder = archiveFolders[i];
                FileUtil.DeleteFileOrDirectory(folder);
                if (File.Exists(folder + ".meta"))
                    FileUtil.DeleteFileOrDirectory(folder + ".meta");

                LogInfo("[DuoCurtainBuildArchive] Removed old archive: " + ToProjectRelativePath(folder) + ".");
            }
        }

        public static void PruneAllPlatforms()
        {
            int maxKeep = GetMaxArchivesPerPlatform();
            string outputRoot = GetOutputRootPath();
            for (int i = 0; i < Platforms.Length; i++)
            {
                PlatformBuildInfo platform = Platforms[i];
                PruneArchives(Path.Combine(outputRoot, platform.folderName), maxKeep);
            }
        }

        public static List<BuildRecord> ScanAllBuilds()
        {
            List<BuildRecord> results = new List<BuildRecord>();
            string outputRoot = GetOutputRootPath();

            for (int i = 0; i < Platforms.Length; i++)
            {
                PlatformBuildInfo platform = Platforms[i];
                string platformRoot = Path.Combine(outputRoot, platform.folderName);
                results.AddRange(ScanPlatform(platform, platformRoot));
            }

            return results
                .OrderByDescending(record => record.timestampUtc)
                .ToList();
        }

        public static List<BuildRecord> ScanPlatform(PlatformBuildInfo platform, string platformRoot)
        {
            List<BuildRecord> results = new List<BuildRecord>();
            if (!Directory.Exists(platformRoot))
                return results;

            string latestArtifact = Path.Combine(platformRoot, platform.primaryArtifactName);
            if (File.Exists(latestArtifact) || Directory.Exists(latestArtifact))
            {
                DuoCurtainBuildManifest latestManifest = TryReadManifest(Path.Combine(platformRoot, ManifestFileName));
                DateTime latestTime = ResolveTimestamp(latestManifest, Directory.GetLastWriteTimeUtc(platformRoot));
                results.Add(new BuildRecord(
                    true,
                    platform.platformId,
                    platform.displayName,
                    platformRoot,
                    latestArtifact,
                    latestTime,
                    latestManifest));
            }

            string archiveRoot = Path.Combine(platformRoot, ArchiveFolderName);
            if (!Directory.Exists(archiveRoot))
                return results;

            string[] archiveFolders = Directory.GetDirectories(archiveRoot)
                .OrderByDescending(path => Path.GetFileName(path), StringComparer.Ordinal)
                .ToArray();

            for (int i = 0; i < archiveFolders.Length; i++)
            {
                string archiveFolder = archiveFolders[i];
                DuoCurtainBuildManifest manifest = TryReadManifest(Path.Combine(archiveFolder, ManifestFileName));
                string artifactPath = Path.Combine(archiveFolder, platform.primaryArtifactName);
                if (!File.Exists(artifactPath) && !Directory.Exists(artifactPath))
                    artifactPath = archiveFolder;

                DateTime timestamp = ResolveTimestamp(manifest, Directory.GetCreationTimeUtc(archiveFolder));
                results.Add(new BuildRecord(
                    false,
                    platform.platformId,
                    platform.displayName,
                    archiveFolder,
                    artifactPath,
                    timestamp,
                    manifest));
            }

            return results;
        }

        public static void RevealInFileBrowser(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                return;

            string normalized = fullPath.Replace('\\', '/');
            if (!File.Exists(normalized) && !Directory.Exists(normalized))
            {
                string parent = Path.GetDirectoryName(normalized);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    normalized = parent;
                else
                {
                    Debug.LogWarning("[DuoCurtainBuildArchive] Path not found: " + fullPath);
                    return;
                }
            }

            EditorUtility.RevealInFinder(normalized);
        }

        public static string GetOutputRootPath() => Path.GetFullPath(OutputRootFolder);

        public static string ToProjectRelativePath(string fullPath)
        {
            string projectRoot = Directory.GetCurrentDirectory().Replace('\\', '/').TrimEnd('/');
            string normalized = fullPath.Replace('\\', '/');
            if (normalized.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
                return normalized.Substring(projectRoot.Length + 1);
            return normalized;
        }

        private static DuoCurtainBuildManifest CreateManifest(string platformId, string displayName, string role, bool developmentBuild)
        {
            GitSnapshot git = CaptureGitSnapshot();
            return new DuoCurtainBuildManifest
            {
                platform = platformId,
                platformDisplayName = displayName,
                role = role,
                createdUtc = DateTime.UtcNow.ToString("o"),
                gitCommitHash = git.commitHash,
                gitCommitShort = git.commitShort,
                gitBranch = git.branch,
                gitDirty = git.dirty,
                developmentBuild = developmentBuild
            };
        }

        private static DateTime ResolveTimestamp(DuoCurtainBuildManifest manifest, DateTime fallbackUtc)
        {
            if (manifest == null)
                return fallbackUtc;

            if (!string.IsNullOrEmpty(manifest.archivedUtc) &&
                DateTime.TryParse(manifest.archivedUtc, out DateTime archivedUtc))
                return archivedUtc;

            if (!string.IsNullOrEmpty(manifest.createdUtc) &&
                DateTime.TryParse(manifest.createdUtc, out DateTime createdUtc))
                return createdUtc;

            return fallbackUtc;
        }

        private static DuoCurtainBuildManifest TryReadManifest(string path)
        {
            if (!File.Exists(path))
                return null;

            try
            {
                string json = File.ReadAllText(path);
                return string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<DuoCurtainBuildManifest>(json);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[DuoCurtainBuildArchive] Failed to read manifest at " + path + ": " + exception.Message);
                return null;
            }
        }

        private static void WriteManifest(string path, DuoCurtainBuildManifest manifest)
        {
            if (manifest == null)
                return;

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(path, JsonUtility.ToJson(manifest, true));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[DuoCurtainBuildArchive] Failed to write manifest at " + path + ": " + exception.Message);
            }
        }

        private static GitSnapshot CaptureGitSnapshot()
        {
            GitSnapshot snapshot = new GitSnapshot();
            snapshot.commitHash = CrossPlatformEditorUtility.RunGit("rev-parse HEAD");
            snapshot.commitShort = CrossPlatformEditorUtility.RunGit("rev-parse --short HEAD");
            snapshot.branch = CrossPlatformEditorUtility.RunGit("rev-parse --abbrev-ref HEAD");
            string dirtyOutput = CrossPlatformEditorUtility.RunGit("status --porcelain");
            snapshot.dirty = !string.IsNullOrEmpty(dirtyOutput);
            return snapshot;
        }

        private static string GetUniquePath(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return path;

            string directory = Path.GetDirectoryName(path);
            string fileName = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            for (int i = 1; i < 1000; i++)
            {
                string candidate = Path.Combine(directory, fileName + "_" + i + extension);
                if (!File.Exists(candidate) && !Directory.Exists(candidate))
                    return candidate;
            }

            throw new IOException("Unable to create a unique path for " + path + ".");
        }

        private static void LogInfo(string message)
        {
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", message);
        }

        private struct GitSnapshot
        {
            public string commitHash;
            public string commitShort;
            public string branch;
            public bool dirty;
        }
    }

    [Serializable]
    public sealed class DuoCurtainBuildManifest
    {
        public string platform;
        public string platformDisplayName;
        public string role;
        public string createdUtc;
        public string archivedUtc;
        public string archiveFolderName;
        public string gitCommitHash;
        public string gitCommitShort;
        public string gitBranch;
        public bool gitDirty;
        public bool developmentBuild;
    }
}
#endif
