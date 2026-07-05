#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Curtain.Editor;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DuoCurtain.Editor
{
    public sealed class DuoCurtainBuildPackager : EditorWindow
    {
        private const string WindowTitle = "Duo Curtain Build Packager";
        private const string OutputRootFolder = DuoCurtainBuildArchiveService.OutputRootFolder;
        private const string StagingFolderName = DuoCurtainBuildArchiveService.StagingFolderName;
        private const string AppName = "Curtain";
        private const string CommandLinePlatformsArg = "-duoCurtainPlatforms";
        private const string CommandLineDevelopmentArg = "-duoCurtainDevelopment";
        private const BuildOptions ReliabilityBuildOptions = BuildOptions.CleanBuildCache;

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
                "Builds are staged first, then copied into Builds/Curtain_Mac, Builds/Curtain_Windows, and Builds/Curtain_Web. " +
                "Existing package contents are archived with git metadata, then trimmed to the retention limit configured in Curtain Dashboard → Builds.",
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

            options = NormalizeBuildOptions(options);
            EnsureReadyForBuild();

            bool buildSucceeded = false;
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

                buildSucceeded = true;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (buildSucceeded && Directory.Exists(stagingRoot))
                {
                    FileUtil.DeleteFileOrDirectory(stagingRoot);
                }
                else if (!buildSucceeded && Directory.Exists(stagingRoot))
                {
                    LogInfo("[DuoCurtainBuildPackager] Preserved failed staging output for diagnosis: " + ToProjectRelativePath(stagingRoot));
                }

                AssetDatabase.Refresh();
            }
        }

        private static void EnsureReadyForBuild()
        {
            AssetDatabase.Refresh();
            CurtainSettingsBundleInstaller.EnsureBundle();
            WaitForCompilationToFinish();

            if (EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Fix script compilation errors before building.");

            SaveEnabledBuildScenes();
        }

        private static void SaveEnabledBuildScenes()
        {
            string[] scenes = GetEnabledScenes();
            if (scenes.Length == 0)
                return;

            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                for (int i = 0; i < scenes.Length; i++)
                {
                    string scenePath = scenes[i];
                    if (string.IsNullOrEmpty(scenePath) || !File.Exists(scenePath))
                        continue;

                    Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    if (!scene.IsValid())
                        continue;

                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene))
                        throw new InvalidOperationException("Failed to save scene before build: " + scenePath);

                    LogInfo("[DuoCurtainBuildPackager] Saved build scene: " + ToProjectRelativePath(scenePath));
                }
            }
            finally
            {
                if (previousSetup != null && previousSetup.Length > 0)
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
            }
        }

        private static void WaitForCompilationToFinish()
        {
            const int maxAttempts = 300;
            for (int attempt = 0; attempt < maxAttempts && EditorApplication.isCompiling; attempt++)
                System.Threading.Thread.Sleep(100);

            if (EditorApplication.isCompiling)
                throw new InvalidOperationException("Script compilation did not finish before build.");
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
            ValidatePublishedBuild(spec, platformStagingRoot);
            DuoCurtainBuildArchiveService.ArchiveExistingBuild(
                platformOutputRoot,
                spec.platform.ToString(),
                spec.displayName);
            int publishedCount = PublishStagedBuild(platformStagingRoot, platformOutputRoot);

            bool isDevelopmentBuild = (options & BuildOptions.Development) != 0;
            DuoCurtainBuildArchiveService.WriteLatestManifest(
                platformOutputRoot,
                spec.platform.ToString(),
                spec.displayName,
                isDevelopmentBuild);

            LogInfo(
                "[DuoCurtainBuildPackager] Published " + spec.displayName +
                " build to " + ToProjectRelativePath(platformOutputRoot) +
                " with " + publishedCount + " shipping artifact(s).");
        }

        private static int PublishStagedBuild(string platformStagingRoot, string platformOutputRoot)
        {
            Directory.CreateDirectory(platformOutputRoot);
            string[] stagedEntries = Directory.GetFileSystemEntries(platformStagingRoot);
            if (stagedEntries.Length == 0)
                throw new InvalidOperationException("Build staging folder is empty: " + platformStagingRoot);

            int publishedCount = 0;
            for (int i = 0; i < stagedEntries.Length; i++)
            {
                string entryName = Path.GetFileName(stagedEntries[i]);
                if (ShouldSkipPublishedEntry(entryName))
                {
                    LogInfo("[DuoCurtainBuildPackager] Skipping non-shipping artifact: " + entryName);
                    continue;
                }

                string destination = Path.Combine(platformOutputRoot, entryName);
                if (File.Exists(destination) || Directory.Exists(destination))
                    FileUtil.DeleteFileOrDirectory(destination);
                FileUtil.MoveFileOrDirectory(stagedEntries[i], destination);
                publishedCount++;
            }

            if (publishedCount == 0)
            {
                throw new InvalidOperationException(
                    "No shipping build artifacts were published from staging: " + platformStagingRoot);
            }

            return publishedCount;
        }

        private static bool ShouldSkipPublishedEntry(string entryName)
        {
            if (string.IsNullOrEmpty(entryName))
                return true;

            return entryName.Contains("DoNotShip", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(entryName, ".DS_Store", StringComparison.OrdinalIgnoreCase);
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

        private static BuildOptions NormalizeBuildOptions(BuildOptions options)
        {
            BuildOptions normalized = options | ReliabilityBuildOptions;
            if ((normalized & BuildOptions.CleanBuildCache) != 0)
                LogInfo("[DuoCurtainBuildPackager] Clean build cache is enabled for packaged builds.");
            return normalized;
        }

        private static void ValidatePublishedBuild(PlatformSpec spec, string platformOutputRoot)
        {
            switch (spec.platform)
            {
                case PackagedPlatform.Mac:
                    ValidatePublishedMacBuild(platformOutputRoot);
                    break;
                case PackagedPlatform.Windows:
                    ValidateRequiredFile(
                        Path.Combine(platformOutputRoot, AppName + "_Windows.exe"),
                        1024,
                        "Windows executable");
                    ValidateRequiredDirectory(
                        Path.Combine(platformOutputRoot, AppName + "_Windows_Data"),
                        "Windows Data folder");
                    ValidatePublishedWindowsBuild(platformOutputRoot);
                    break;
                case PackagedPlatform.WebGL:
                    ValidateRequiredFile(
                        Path.Combine(platformOutputRoot, "index.html"),
                        1,
                        "WebGL index.html");
                    ValidateRequiredDirectory(
                        Path.Combine(platformOutputRoot, "Build"),
                        "WebGL Build folder");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(spec.platform), spec.platform, null);
            }
        }

        private static void ValidatePublishedMacBuild(string platformOutputRoot)
        {
            string appRoot = Path.Combine(platformOutputRoot, AppName + "_Mac.app");
            ValidateRequiredDirectory(appRoot, "Mac app bundle");

            string dataRoot = Path.Combine(appRoot, "Contents", "Resources", "Data");
            ValidateRequiredDirectory(dataRoot, "Mac player Data folder");
            ValidateRequiredFile(Path.Combine(dataRoot, "globalgamemanagers"), 1024, "Mac globalgamemanagers");
            ValidateRequiredFile(Path.Combine(dataRoot, "globalgamemanagers.assets"), 1024, "Mac globalgamemanagers.assets");
            ValidateRequiredFile(Path.Combine(dataRoot, "level0"), 1024, "Mac level0");
            ValidateRequiredFile(Path.Combine(dataRoot, "resources.assets"), 1024, "Mac resources.assets");
            ValidateRequiredFile(Path.Combine(dataRoot, "sharedassets0.assets"), 1024, "Mac sharedassets0.assets");
            if (CrossPlatformEditorUtility.CanRunMacPlayerSmokeTest)
                SmokeTestMacPlayerBuild(appRoot);
        }

        private static void ValidatePublishedWindowsBuild(string platformOutputRoot)
        {
            string executablePath = Path.Combine(platformOutputRoot, AppName + "_Windows.exe");
            string dataRoot = Path.Combine(platformOutputRoot, AppName + "_Windows_Data");
            ValidateRequiredFile(Path.Combine(dataRoot, "globalgamemanagers"), 1024, "Windows globalgamemanagers");
            ValidateRequiredFile(Path.Combine(dataRoot, "globalgamemanagers.assets"), 1024, "Windows globalgamemanagers.assets");
            ValidateRequiredFile(Path.Combine(dataRoot, "level0"), 1024, "Windows level0");
            ValidateRequiredFile(Path.Combine(dataRoot, "resources.assets"), 1024, "Windows resources.assets");
            ValidateRequiredFile(Path.Combine(dataRoot, "sharedassets0.assets"), 1024, "Windows sharedassets0.assets");
            if (CrossPlatformEditorUtility.CanRunWindowsPlayerSmokeTest)
                SmokeTestWindowsPlayerBuild(executablePath);
        }

        private static void SmokeTestMacPlayerBuild(string appRoot)
        {
            if (!CrossPlatformEditorUtility.CanRunMacPlayerSmokeTest)
                return;
            string playerBinary = Path.Combine(appRoot, "Contents", "MacOS", AppName.ToLowerInvariant());
            if (!File.Exists(playerBinary))
                throw new InvalidOperationException("Mac player binary is missing after publish: " + playerBinary);

            string logPath = Path.Combine(Path.GetTempPath(), "duocurtain_mac_smoke.log");
            if (File.Exists(logPath))
                File.Delete(logPath);

            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = playerBinary,
                Arguments = "-batchmode -nographics -quit -logFile \"" + logPath + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo);
            if (process == null)
                throw new InvalidOperationException("Failed to launch Mac player smoke test.");

            bool startupValidated = false;
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                    break;

                if (TryReadSmokeLog(logPath, out string liveLog))
                {
                    ThrowIfSmokeLogContainsCorruption(liveLog, logPath);
                    if (liveLog.IndexOf("UnloadTime:", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        startupValidated = true;
                        break;
                    }
                }

                System.Threading.Thread.Sleep(250);
            }

            if (startupValidated && !process.HasExited)
            {
                TryKill(process);
            }

            if (!startupValidated && !process.HasExited)
            {
                TryKill(process);
                throw new InvalidOperationException("Mac player smoke test timed out before player data finished loading.");
            }

            if (startupValidated)
            {
                if (TryReadSmokeLog(logPath, out string validatedLog))
                    ThrowIfSmokeLogContainsCorruption(validatedLog, logPath);

                LogInfo("[DuoCurtainBuildPackager] Mac player smoke test passed.");
                return;
            }

            if (process.ExitCode == 133 || process.ExitCode == 134 || process.ExitCode == 139)
            {
                string logTail = ReadTail(logPath, 40);
                throw new InvalidOperationException(
                    "Mac player smoke test crashed with exit code " + process.ExitCode +
                    ". This usually means sharedassets/level0 serialization is corrupt.\n" + logTail);
            }

            if (!File.Exists(logPath))
                return;

            string logText = File.ReadAllText(logPath);
            ThrowIfSmokeLogContainsCorruption(logText, logPath);

            LogInfo("[DuoCurtainBuildPackager] Mac player smoke test passed.");
        }

        private static void SmokeTestWindowsPlayerBuild(string executablePath)
        {
            if (!CrossPlatformEditorUtility.CanRunWindowsPlayerSmokeTest)
                return;

            if (!File.Exists(executablePath))
                throw new InvalidOperationException("Windows player executable is missing after publish: " + executablePath);

            string logPath = Path.Combine(Path.GetTempPath(), "duocurtain_windows_smoke.log");
            if (File.Exists(logPath))
                File.Delete(logPath);

            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = "-batchmode -nographics -quit -logFile \"" + logPath + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo);
            if (process == null)
                throw new InvalidOperationException("Failed to launch Windows player smoke test.");

            bool startupValidated = false;
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                    break;

                if (TryReadSmokeLog(logPath, out string liveLog))
                {
                    ThrowIfSmokeLogContainsCorruption(liveLog, logPath);
                    if (liveLog.IndexOf("UnloadTime:", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        startupValidated = true;
                        break;
                    }
                }

                System.Threading.Thread.Sleep(250);
            }

            if (startupValidated && !process.HasExited)
                TryKill(process);

            if (!startupValidated && !process.HasExited)
            {
                TryKill(process);
                throw new InvalidOperationException("Windows player smoke test timed out before player data finished loading.");
            }

            if (startupValidated)
            {
                if (TryReadSmokeLog(logPath, out string validatedLog))
                    ThrowIfSmokeLogContainsCorruption(validatedLog, logPath);

                LogInfo("[DuoCurtainBuildPackager] Windows player smoke test passed.");
                return;
            }

            if (process.ExitCode != 0)
            {
                string logTail = ReadTail(logPath, 40);
                throw new InvalidOperationException(
                    "Windows player smoke test failed with exit code " + process.ExitCode + ".\n" + logTail);
            }

            if (!File.Exists(logPath))
                return;

            string logText = File.ReadAllText(logPath);
            ThrowIfSmokeLogContainsCorruption(logText, logPath);
            LogInfo("[DuoCurtainBuildPackager] Windows player smoke test passed.");
        }

        private static bool TryReadSmokeLog(string logPath, out string logText)
        {
            logText = string.Empty;
            if (!File.Exists(logPath))
                return false;

            try
            {
                logText = File.ReadAllText(logPath);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static void ThrowIfSmokeLogContainsCorruption(string logText, string logPath)
        {
            if (logText.IndexOf("is corrupted", StringComparison.OrdinalIgnoreCase) < 0 &&
                logText.IndexOf("Position out of bounds", StringComparison.OrdinalIgnoreCase) < 0 &&
                logText.IndexOf("serialization layout mismatch", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            throw new InvalidOperationException(
                "Player smoke test detected corrupt player data. Re-save RedScene and rebuild.\n" +
                ReadTail(logPath, 40));
        }

        private static void TryKill(System.Diagnostics.Process process)
        {
            try
            {
                process.Kill();
            }
            catch
            {
                // ignored
            }
        }

        private static string ReadTail(string path, int maxLines)
        {
            if (!File.Exists(path))
                return string.Empty;

            string[] lines = File.ReadAllLines(path);
            int start = Mathf.Max(0, lines.Length - maxLines);
            StringBuilder builder = new StringBuilder();
            for (int i = start; i < lines.Length; i++)
            {
                builder.AppendLine(lines[i]);
            }

            return builder.ToString();
        }

        private static void ValidateRequiredDirectory(string path, string label)
        {
            if (!Directory.Exists(path))
                throw new InvalidOperationException(label + " is missing after publish: " + path);
        }

        private static void ValidateRequiredFile(string path, long minimumBytes, string label)
        {
            if (!File.Exists(path))
                throw new InvalidOperationException(label + " is missing after publish: " + path);

            FileInfo fileInfo = new FileInfo(path);
            if (fileInfo.Length < minimumBytes)
            {
                throw new InvalidOperationException(
                    label + " is too small after publish (" + fileInfo.Length + " bytes): " + path);
            }
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
