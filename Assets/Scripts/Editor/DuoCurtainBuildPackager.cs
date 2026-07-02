#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DuoCurtain.Editor
{
    public sealed class DuoCurtainBuildPackager : EditorWindow
    {
        private const string WindowTitle = "Duo Curtain Build Packager";
        private const string OutputRootFolder = "Builds";
        private const string StagingFolderName = ".BuildStaging";
        private const string ArchiveFolderName = "Archive";
        private const string AppName = "Curtain";
        private const string CommandLinePlatformsArg = "-duoCurtainPlatforms";
        private const string CommandLineDevelopmentArg = "-duoCurtainDevelopment";

        private bool buildMac = true;
        private bool buildWindows;
        private bool buildWebGL;
        private bool developmentBuild;

        [MenuItem("Tools/Duo Curtain/Build/Build Packager")]
        public static void ShowWindow()
        {
            DuoCurtainBuildPackager window = GetWindow<DuoCurtainBuildPackager>(false, WindowTitle);
            window.minSize = new Vector2(420f, 280f);
            window.Show();
        }

        [MenuItem("Tools/Duo Curtain/Build/Build All Platforms")]
        public static void BuildAllPlatformsMenu()
        {
            BuildPlatforms(new[] { PackagedPlatform.Mac, PackagedPlatform.Windows, PackagedPlatform.WebGL }, BuildOptions.None);
        }

        [MenuItem("Tools/Duo Curtain/Build/Build Mac")]
        public static void BuildMacMenu()
        {
            BuildPlatforms(new[] { PackagedPlatform.Mac }, BuildOptions.None);
        }

        [MenuItem("Tools/Duo Curtain/Build/Build Windows")]
        public static void BuildWindowsMenu()
        {
            BuildPlatforms(new[] { PackagedPlatform.Windows }, BuildOptions.None);
        }

        [MenuItem("Tools/Duo Curtain/Build/Build WebGL")]
        public static void BuildWebGLMenu()
        {
            BuildPlatforms(new[] { PackagedPlatform.WebGL }, BuildOptions.None);
        }

        public static void BuildAllPlatformsBatch()
        {
            BuildBatch(new[] { PackagedPlatform.Mac, PackagedPlatform.Windows, PackagedPlatform.WebGL });
        }

        public static void BuildSelectedPlatformsBatch()
        {
            string[] args = Environment.GetCommandLineArgs();
            List<PackagedPlatform> platforms = ParseCommandLinePlatforms(args);
            if (platforms.Count == 0)
                platforms.AddRange(new[] { PackagedPlatform.Mac, PackagedPlatform.Windows, PackagedPlatform.WebGL });

            BuildBatch(platforms);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Builds are staged first, then copied into Builds/Curtain_Mac, Builds/Curtain_Windows, and Builds/Curtain_Web. Existing package contents are moved to Archive/yyyyMMdd_HHmmss before the new build is published.",
                MessageType.Info);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Platforms", EditorStyles.boldLabel);
                buildMac = EditorGUILayout.ToggleLeft("Mac -> Builds/Curtain_Mac", buildMac);
                buildWindows = EditorGUILayout.ToggleLeft("Windows -> Builds/Curtain_Windows", buildWindows);
                buildWebGL = EditorGUILayout.ToggleLeft("WebGL -> Builds/Curtain_Web", buildWebGL);
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
                developmentBuild = EditorGUILayout.ToggleLeft("Development Build", developmentBuild);
                EditorGUILayout.LabelField("Default", "Release build, no auto-run");
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select All"))
                {
                    buildMac = true;
                    buildWindows = true;
                    buildWebGL = true;
                }

                if (GUILayout.Button("Clear"))
                {
                    buildMac = false;
                    buildWindows = false;
                    buildWebGL = false;
                }
            }

            using (new EditorGUI.DisabledScope(!buildMac && !buildWindows && !buildWebGL))
            {
                if (GUILayout.Button("Build Selected Platforms", GUILayout.Height(38f)))
                    BuildPlatforms(GetSelectedPlatforms(), GetBuildOptions());
            }
        }

        private static void BuildBatch(IEnumerable<PackagedPlatform> platforms)
        {
            int exitCode = 0;
            try
            {
                BuildOptions options = Environment.GetCommandLineArgs().Contains(CommandLineDevelopmentArg)
                    ? BuildOptions.Development
                    : BuildOptions.None;
                BuildPlatforms(platforms, options);
            }
            catch (Exception exception)
            {
                exitCode = 1;
                Debug.LogError("[DuoCurtainBuildPackager] Batch build failed: " + exception);
            }
            finally
            {
                if (Application.isBatchMode)
                    EditorApplication.Exit(exitCode);
            }
        }

        private static void BuildPlatforms(IEnumerable<PackagedPlatform> platforms, BuildOptions options)
        {
            List<PlatformSpec> specs = platforms
                .Distinct()
                .Select(GetPlatformSpec)
                .OrderBy(spec => spec.order)
                .ToList();
            if (specs.Count == 0)
                throw new InvalidOperationException("No build platforms selected.");

            string[] scenes = GetEnabledScenes();
            if (scenes.Length == 0)
                throw new InvalidOperationException("No enabled scenes found in EditorBuildSettings.");

            string projectRoot = Directory.GetCurrentDirectory();
            string outputRoot = Path.Combine(projectRoot, OutputRootFolder);
            string stagingRoot = Path.Combine(outputRoot, StagingFolderName);
            Directory.CreateDirectory(outputRoot);

            try
            {
                for (int i = 0; i < specs.Count; i++)
                {
                    PlatformSpec spec = specs[i];
                    EditorUtility.DisplayProgressBar(
                        WindowTitle,
                        "Building " + spec.displayName,
                        specs.Count == 1 ? 0.5f : (float)i / specs.Count);

                    BuildSinglePlatform(spec, scenes, options, outputRoot, stagingRoot);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (Directory.Exists(stagingRoot))
                    FileUtil.DeleteFileOrDirectory(stagingRoot);
                AssetDatabase.Refresh();
            }
        }

        private static void BuildSinglePlatform(
            PlatformSpec spec,
            string[] scenes,
            BuildOptions options,
            string outputRoot,
            string stagingRoot)
        {
            if (!BuildPipeline.IsBuildTargetSupported(spec.targetGroup, spec.target))
            {
                throw new InvalidOperationException(
                    "Build target is not installed or supported: " + spec.displayName + ".");
            }

            string platformStagingRoot = Path.Combine(stagingRoot, spec.folderName);
            if (Directory.Exists(platformStagingRoot))
                FileUtil.DeleteFileOrDirectory(platformStagingRoot);
            Directory.CreateDirectory(platformStagingRoot);

            if (EditorUserBuildSettings.activeBuildTarget != spec.target)
            {
                LogInfo("[DuoCurtainBuildPackager] Switching build target to " + spec.displayName + ".");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(spec.targetGroup, spec.target))
                    throw new InvalidOperationException("Failed to switch build target to " + spec.displayName + ".");
            }

            string locationPath = spec.GetBuildLocation(platformStagingRoot);
            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = locationPath,
                target = spec.target,
                targetGroup = spec.targetGroup,
                options = options
            };

            LogInfo("[DuoCurtainBuildPackager] Building " + spec.displayName + " to staging: " + locationPath);
            BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Build failed for " + spec.displayName + " with result " + report.summary.result + ".");
            }

            string platformOutputRoot = Path.Combine(outputRoot, spec.folderName);
            ArchiveExistingBuild(platformOutputRoot);
            PublishStagedBuild(platformStagingRoot, platformOutputRoot);

            LogInfo(
                "[DuoCurtainBuildPackager] Published " + spec.displayName +
                " build to " + ToProjectRelativePath(platformOutputRoot) + ".");
        }

        private static void ArchiveExistingBuild(string platformOutputRoot)
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
                           !string.Equals(name, ".DS_Store", StringComparison.OrdinalIgnoreCase);
                })
                .ToArray();
            if (entries.Length == 0)
                return;

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

            LogInfo("[DuoCurtainBuildPackager] Archived previous build to " + ToProjectRelativePath(archiveFolder) + ".");
        }

        private static void PublishStagedBuild(string platformStagingRoot, string platformOutputRoot)
        {
            Directory.CreateDirectory(platformOutputRoot);
            string[] stagedEntries = Directory.GetFileSystemEntries(platformStagingRoot);
            if (stagedEntries.Length == 0)
                throw new InvalidOperationException("Build staging folder is empty: " + platformStagingRoot);

            for (int i = 0; i < stagedEntries.Length; i++)
            {
                string destination = Path.Combine(platformOutputRoot, Path.GetFileName(stagedEntries[i]));
                if (File.Exists(destination) || Directory.Exists(destination))
                    FileUtil.DeleteFileOrDirectory(destination);
                FileUtil.MoveFileOrDirectory(stagedEntries[i], destination);
            }
        }

        private static string[] GetEnabledScenes()
        {
            return EditorBuildSettings.scenes
                .Where(scene => scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                .Select(scene => scene.path)
                .ToArray();
        }

        private static List<PackagedPlatform> ParseCommandLinePlatforms(string[] args)
        {
            List<PackagedPlatform> platforms = new List<PackagedPlatform>();
            int index = Array.IndexOf(args, CommandLinePlatformsArg);
            if (index < 0 || index + 1 >= args.Length)
                return platforms;

            string[] tokens = args[index + 1].Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                if (TryParsePlatform(tokens[i], out PackagedPlatform platform))
                    platforms.Add(platform);
                else
                    Debug.LogWarning("[DuoCurtainBuildPackager] Ignoring unknown platform token: " + tokens[i]);
            }

            return platforms;
        }

        private IEnumerable<PackagedPlatform> GetSelectedPlatforms()
        {
            if (buildMac)
                yield return PackagedPlatform.Mac;
            if (buildWindows)
                yield return PackagedPlatform.Windows;
            if (buildWebGL)
                yield return PackagedPlatform.WebGL;
        }

        private BuildOptions GetBuildOptions()
        {
            return developmentBuild ? BuildOptions.Development : BuildOptions.None;
        }

        private static bool TryParsePlatform(string value, out PackagedPlatform platform)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "mac":
                case "osx":
                case "macos":
                    platform = PackagedPlatform.Mac;
                    return true;
                case "win":
                case "windows":
                case "windows64":
                    platform = PackagedPlatform.Windows;
                    return true;
                case "web":
                case "webgl":
                    platform = PackagedPlatform.WebGL;
                    return true;
                default:
                    platform = default;
                    return false;
            }
        }

        private static PlatformSpec GetPlatformSpec(PackagedPlatform platform)
        {
            switch (platform)
            {
                case PackagedPlatform.Mac:
                    return new PlatformSpec(
                        0,
                        PackagedPlatform.Mac,
                        "Mac",
                        "Curtain_Mac",
                        BuildTargetGroup.Standalone,
                        BuildTarget.StandaloneOSX,
                        stagingRoot => Path.Combine(stagingRoot, AppName + "_Mac.app"));
                case PackagedPlatform.Windows:
                    return new PlatformSpec(
                        1,
                        PackagedPlatform.Windows,
                        "Windows",
                        "Curtain_Windows",
                        BuildTargetGroup.Standalone,
                        BuildTarget.StandaloneWindows64,
                        stagingRoot => Path.Combine(stagingRoot, AppName + "_Windows.exe"));
                case PackagedPlatform.WebGL:
                    return new PlatformSpec(
                        2,
                        PackagedPlatform.WebGL,
                        "WebGL",
                        "Curtain_Web",
                        BuildTargetGroup.WebGL,
                        BuildTarget.WebGL,
                        stagingRoot => stagingRoot);
                default:
                    throw new ArgumentOutOfRangeException(nameof(platform), platform, null);
            }
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

        private static string ToProjectRelativePath(string fullPath)
        {
            string projectRoot = Directory.GetCurrentDirectory().Replace('\\', '/').TrimEnd('/');
            string normalized = fullPath.Replace('\\', '/');
            if (normalized.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
                return normalized.Substring(projectRoot.Length + 1);
            return normalized;
        }

        private static void LogInfo(string message)
        {
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", message);
        }

        private enum PackagedPlatform
        {
            Mac,
            Windows,
            WebGL
        }

        private readonly struct PlatformSpec
        {
            public readonly int order;
            public readonly PackagedPlatform platform;
            public readonly string displayName;
            public readonly string folderName;
            public readonly BuildTargetGroup targetGroup;
            public readonly BuildTarget target;
            private readonly Func<string, string> buildLocationFactory;

            public PlatformSpec(
                int order,
                PackagedPlatform platform,
                string displayName,
                string folderName,
                BuildTargetGroup targetGroup,
                BuildTarget target,
                Func<string, string> buildLocationFactory)
            {
                this.order = order;
                this.platform = platform;
                this.displayName = displayName;
                this.folderName = folderName;
                this.targetGroup = targetGroup;
                this.target = target;
                this.buildLocationFactory = buildLocationFactory;
            }

            public string GetBuildLocation(string stagingRoot)
            {
                return buildLocationFactory(stagingRoot);
            }
        }
    }
}
#endif
