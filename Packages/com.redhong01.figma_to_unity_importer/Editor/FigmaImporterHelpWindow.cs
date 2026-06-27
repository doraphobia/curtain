using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FigmaImporter.Editor
{
    internal sealed class FigmaImporterHelpWindow : EditorWindow
    {
        private const string WindowTitle = "Figma Importer Flow";
        private const string GitDownloadUrl = "https://git-scm.com/downloads";
        private const string DefaultIssueOutputRoot = "Assets/FigmaImporter/_Local/IssueHandoff";
        private static readonly string[] PackageNames =
        {
            "com.redhong01.figma_to_unity_importer",
            "com.manakhovn.figma_to_unity_importer"
        };

        private enum FlowStage
        {
            Environment = 0,
            OAuth = 1,
            Import = 2,
            Fallback = 3,
            Diagnostics = 4,
            AutoLayout = 5
        }

        private static readonly FlowStage[] StageOrder =
        {
            FlowStage.Environment,
            FlowStage.OAuth,
            FlowStage.Import,
            FlowStage.Fallback,
            FlowStage.Diagnostics,
            FlowStage.AutoLayout
        };

        private static readonly string[] StageLabels =
        {
            "Environment",
            "OAuth",
            "Import",
            "Fallback",
            "Diagnostics",
            "AutoLayout"
        };

        private Vector2 _scrollPosition;
        private FlowStage _activeStage = FlowStage.Environment;
        private string _status = "Ready";
        private bool _showOnlyCurrentSession = true;
        private bool _autoInitializeDependencies;
        private string _issueOutputRoot = DefaultIssueOutputRoot;
        private string _lastIssuePackFolder = string.Empty;
        private string _gitExecutablePath = string.Empty;
        private bool _gitExecutableAvailable;

        [MenuItem(FigmaImporterMenuPaths.Help.QuickStartTutorial)]
        internal static void OpenWindow()
        {
            OpenWithStage(FlowStage.Environment);
        }

        [MenuItem(FigmaImporterMenuPaths.Help.OpenReadme)]
        private static void OpenReadmeMenu()
        {
            OpenReadme();
        }

        [MenuItem(FigmaImporterMenuPaths.Help.OpenDiagnosticsHub)]
        private static void OpenDiagnosticsHubMenu()
        {
            OpenWithStage(FlowStage.Diagnostics);
        }

        private static void OpenWithStage(FlowStage stage)
        {
            var window = GetWindow<FigmaImporterHelpWindow>(WindowTitle);
            window.minSize = new Vector2(900f, 640f);
            window._activeStage = stage;
            window.RefreshEnvironmentState();
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            _issueOutputRoot = NormalizeIssueOutputRoot(_issueOutputRoot);
            RefreshEnvironmentState();
        }

        private void OnGUI()
        {
            var settings = FigmaImporterSettings.GetInstance();

            DrawHeader(settings);
            DrawStageSelector(settings);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            GUILayout.Space(4f);

            switch (_activeStage)
            {
                case FlowStage.Environment:
                    DrawEnvironmentStage();
                    break;
                case FlowStage.OAuth:
                    DrawOAuthStage(settings);
                    break;
                case FlowStage.Import:
                    DrawImportStage(settings);
                    break;
                case FlowStage.Fallback:
                    DrawFallbackStage();
                    break;
                case FlowStage.Diagnostics:
                    DrawDiagnosticsStage();
                    break;
                case FlowStage.AutoLayout:
                    DrawAutoLayoutStage();
                    break;
            }

            GUILayout.Space(12f);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField("Flow Status", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(string.IsNullOrWhiteSpace(_status) ? "Ready" : _status, MessageType.None);
        }

        private void DrawHeader(FigmaImporterSettings settings)
        {
            EditorGUILayout.LabelField("Figma Importer End-to-End Flow", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This window is the primary control surface for setup, auth, import, fallback resolution, diagnostics, and auto-layout handoff.",
                MessageType.Info);

            var completion = BuildCompletionSummary(settings);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Flow Completion Snapshot", EditorStyles.boldLabel);
            for (var i = 0; i < completion.Count; i++)
            {
                EditorGUILayout.LabelField(completion[i], EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();
            GUILayout.Space(8f);
        }

        private void DrawStageSelector(FigmaImporterSettings settings)
        {
            EditorGUILayout.LabelField("Flow Stages", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            for (var i = 0; i < StageOrder.Length; i++)
            {
                var stage = StageOrder[i];
                var active = _activeStage == stage;
                var complete = IsStageComplete(stage, settings);
                var label = $"{i + 1}. {StageLabels[i]} {(complete ? "✓" : "○")}";

                var cachedColor = GUI.color;
                GUI.color = active
                    ? new UnityEngine.Color(0.74f, 0.90f, 1f, 1f)
                    : UnityEngine.Color.white;
                if (GUILayout.Button(label, GUILayout.Height(28f)))
                {
                    _activeStage = stage;
                }
                GUI.color = cachedColor;
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8f);
        }

        private void DrawEnvironmentStage()
        {
            EditorGUILayout.LabelField("1) Environment Setup", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "All collaborators need Git available in PATH for Unity 'Install package from git URL'. Dependencies should be initialized before the first import run.",
                MessageType.None);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Git Executable", EditorStyles.boldLabel);
            if (_gitExecutableAvailable)
            {
                EditorGUILayout.HelpBox($"Detected: {_gitExecutablePath}", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("Git executable not detected in PATH.", MessageType.Error);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Environment", GUILayout.Height(24f)))
            {
                RefreshEnvironmentState();
                _status = _gitExecutableAvailable ? "Environment refreshed." : "Environment refreshed. Git not detected.";
            }

            if (GUILayout.Button("Download Git", GUILayout.Height(24f)))
            {
                Application.OpenURL(GitDownloadUrl);
                _status = "Opened Git downloads page.";
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6f);

            var newAutoInitialize = EditorGUILayout.ToggleLeft(
                "Auto initialize dependencies on editor startup",
                _autoInitializeDependencies);
            if (newAutoInitialize != _autoInitializeDependencies)
            {
                _autoInitializeDependencies = newAutoInitialize;
                FigmaPackageBootstrapper.SetAutoInitializeEnabled(newAutoInitialize);
                _status = $"Auto dependency initialization {(newAutoInitialize ? "enabled" : "disabled")}.";
            }

            if (GUILayout.Button("Initialize Dependencies Now", GUILayout.Height(28f)))
            {
                FigmaPackageBootstrapper.InitializeDependencies(force: true);
                _status = "Dependency initialization started.";
            }
        }

        private void DrawOAuthStage(FigmaImporterSettings settings)
        {
            EditorGUILayout.LabelField("2) OAuth Setup", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Configure authorization on this machine. When collaboration spans devices, each device should refresh OAuth locally.",
                MessageType.None);

            settings.ClientCode = EditorGUILayout.TextField("Client Code", settings.ClientCode);
            settings.State = EditorGUILayout.TextField("State", settings.State);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Token (masked)", EditorStyles.miniBoldLabel);
            EditorGUILayout.SelectableLabel(
                FigmaImporter.MaskTokenForDisplay(settings.Token),
                EditorStyles.textField,
                GUILayout.Height(18f));
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open OAuth URL", GUILayout.Height(24f)))
            {
                FigmaImporter.OpenOAuthUrlFromFlow();
                _status = "Opened Figma OAuth page.";
            }

            if (GUILayout.Button("Get Token", GUILayout.Height(24f)))
            {
                var token = FigmaImporter.RequestOAuthTokenFromFlow();
                _status = string.IsNullOrWhiteSpace(token)
                    ? "Token request finished, but no token was returned."
                    : "OAuth token updated.";
            }

            if (GUILayout.Button("Copy Token", GUILayout.Height(24f)))
            {
                if (string.IsNullOrWhiteSpace(settings.Token))
                {
                    _status = "Token is empty.";
                }
                else
                {
                    EditorGUIUtility.systemCopyBuffer = settings.Token;
                    _status = "Token copied to clipboard.";
                }
            }

            if (GUILayout.Button("Clear Token", GUILayout.Height(24f)))
            {
                settings.Token = string.Empty;
                _status = "Token cleared.";
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawImportStage(FigmaImporterSettings settings)
        {
            EditorGUILayout.LabelField("3) Import Flow", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Use this stage to drive GetNodes and GenerateNodes directly without leaving this flow window.",
                MessageType.None);

            settings.Url = EditorGUILayout.TextField("Figma URL", settings.Url);
            settings.RendersPath = EditorGUILayout.TextField("Renders Path", settings.RendersPath);

            var rootObject = FigmaImporter.GetSelectedRootObjectForFlow();
            var newRootObject = (GameObject)EditorGUILayout.ObjectField("Root Object", rootObject, typeof(GameObject), true);
            if (newRootObject != rootObject)
            {
                FigmaImporter.SetSelectedRootObjectForFlow(newRootObject);
            }

            EditorGUILayout.BeginHorizontal();
            settings.RootObjectPickerCanvasOnly = EditorGUILayout.ToggleLeft(
                "Filter Canvas Related Objects",
                settings.RootObjectPickerCanvasOnly);

            if (GUILayout.Button("Pick Root Object", GUILayout.Width(140f)))
            {
                FigmaRootObjectPickerWindow.Open(
                    FigmaImporter.GetSelectedRootObjectForFlow(),
                    settings.RootObjectPickerCanvasOnly,
                    selected =>
                    {
                        FigmaImporter.SetSelectedRootObjectForFlow(selected);
                        Repaint();
                    },
                    canvasOnly =>
                    {
                        settings.RootObjectPickerCanvasOnly = canvasOnly;
                        Repaint();
                    });
            }

            if (GUILayout.Button("None", GUILayout.Width(60f)))
            {
                FigmaImporter.SetSelectedRootObjectForFlow(null);
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4f);
            var isGenerating = FigmaImporter.IsGenerationRunningForFlow();
            var hasNodes = FigmaImporter.HasLoadedNodeDataForFlow();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(isGenerating || string.IsNullOrWhiteSpace(settings.Url)))
            {
                if (GUILayout.Button("Fetch Figma Node Data", GUILayout.Height(28f)))
                {
                    FigmaImporter.FetchNodeDataFromFlow();
                    _status = "Fetch started.";
                }
            }

            using (new EditorGUI.DisabledScope(isGenerating || !hasNodes))
            {
                if (GUILayout.Button("Apply Selected Import Modes", GUILayout.Height(28f)))
                {
                    FigmaImporter.GenerateNodesFromFlow();
                    _status = "Generate started.";
                }
            }

            if (GUILayout.Button("Open Advanced Importer Window", GUILayout.Height(28f)))
            {
                FigmaImporter.OpenOrCreate(focus: true);
                _status = "Opened advanced importer window.";
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                $"Node Cache: {FigmaImporter.GetLoadedNodeCountForFlow()}  |  Generation: {FigmaImporter.GetGenerationStatusTextForFlow()}",
                FigmaImporter.GetGenerationStatusTypeForFlow());
        }

        private void DrawFallbackStage()
        {
            EditorGUILayout.LabelField("4) Fallback Resolution", EditorStyles.boldLabel);
            var registry = ImportFallbackRegistry.GetOrCreate();
            if (registry == null)
            {
                EditorGUILayout.HelpBox("Fallback registry is unavailable.", MessageType.Error);
                return;
            }

            _showOnlyCurrentSession = EditorGUILayout.ToggleLeft("Only Current Run", _showOnlyCurrentSession);

            var unresolvedFonts = CountUnresolvedFonts(registry, _showOnlyCurrentSession);
            var unresolvedSvg = CountUnresolvedSvg(registry, _showOnlyCurrentSession);
            var unresolvedSvgToPng = CountUnresolvedSvgToPng(registry, _showOnlyCurrentSession);
            var unresolvedIssues = CountUnresolvedIssues(registry, _showOnlyCurrentSession);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                $"Last Session: {ValueOrDash(registry.LastSessionLabel)}  |  Started: {ValueOrDash(registry.LastSessionStartedAt)}  |  Finished: {ValueOrDash(registry.LastSessionFinishedAt)}");
            EditorGUILayout.LabelField(
                $"Unresolved -> Fonts: {unresolvedFonts}  SVG: {unresolvedSvg}  SVG->PNG: {unresolvedSvgToPng}  Other: {unresolvedIssues}");
            if (registry.SessionActive)
            {
                EditorGUILayout.HelpBox("A generate session is currently running.", MessageType.Info);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Auto Match Unresolved", GUILayout.Height(24f)))
            {
                var updated = AutoMatchFallbacks(registry, unresolvedOnly: true, currentSessionOnly: _showOnlyCurrentSession);
                _status = $"Auto match finished. Updated entries: {updated}.";
            }

            if (GUILayout.Button("Apply FontLinks", GUILayout.Height(24f)))
            {
                ImportFallbackRegistry.ApplyFontAssignmentsToFontLinks(registry);
                ImportFallbackRegistry.SaveRegistry(registry);
                _status = "Applied assigned fonts into FontLinks.";
            }

            if (GUILayout.Button("Open Full Resolver", GUILayout.Height(24f)))
            {
                ImportFallbackResolverWindow.OpenWindow();
                _status = "Opened full fallback resolver window.";
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDiagnosticsStage()
        {
            EditorGUILayout.LabelField("5) Diagnostics + Handoff", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Build issue packs and trigger agent-assisted fixes directly from this stage.",
                MessageType.None);

            var captured = FigmaImporterIssueTracker.EntryCount;
            var errorLike = FigmaImporterIssueTracker.ErrorLikeCount;

            EditorGUILayout.LabelField("Captured Entries", captured.ToString());
            EditorGUILayout.LabelField("Error/Exception Entries", errorLike.ToString());

            _issueOutputRoot = EditorGUILayout.TextField("Issue Output Folder", _issueOutputRoot);
            _issueOutputRoot = NormalizeIssueOutputRoot(_issueOutputRoot);

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(captured == 0))
            {
                if (GUILayout.Button("Build Issue Pack", GUILayout.Height(24f)))
                {
                    try
                    {
                        _lastIssuePackFolder = FigmaImporterIssueTracker.CreateIssuePack(_issueOutputRoot);
                        _status = $"Issue pack created: {_lastIssuePackFolder}";
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        _status = $"Build issue pack failed: {e.Message}";
                    }
                }
            }

            using (new EditorGUI.DisabledScope(captured == 0))
            {
                if (GUILayout.Button("Analyze + Fix With Agent", GUILayout.Height(24f)))
                {
                    FigmaImporterIssueHandoffWindow.OpenAndRun();
                    _status = "Started agent handoff flow.";
                }
            }

            if (GUILayout.Button("Open Diagnostics Hub", GUILayout.Height(24f)))
            {
                FigmaDiagnosticsHubWindow.OpenWindow();
                _status = "Opened diagnostics hub.";
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(captured == 0))
            {
                if (GUILayout.Button("Clear Captured Entries", GUILayout.Height(22f)))
                {
                    FigmaImporterIssueTracker.Clear();
                    _status = "Captured issue entries cleared.";
                }
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_lastIssuePackFolder) || !Directory.Exists(_lastIssuePackFolder)))
            {
                if (GUILayout.Button("Reveal Last Pack", GUILayout.Height(22f)))
                {
                    EditorUtility.RevealInFinder(_lastIssuePackFolder);
                }
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(_lastIssuePackFolder))
            {
                EditorGUILayout.LabelField("Last Issue Pack", _lastIssuePackFolder);
            }
        }

        private void DrawAutoLayoutStage()
        {
            EditorGUILayout.LabelField("6) AutoLayout + Frame Sync", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Run auto-layout diagnostics and frame-level verification after imports. Use this stage as final QA before release.",
                MessageType.None);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open AutoLayout Diagnostics", GUILayout.Height(28f)))
            {
                AutoLayoutDiagnosticsWindow.OpenWindow();
                _status = "Opened AutoLayout diagnostics window.";
            }

            if (GUILayout.Button("Open Importer Error Handoff", GUILayout.Height(28f)))
            {
                FigmaImporterIssueHandoffWindow.OpenWindow();
                _status = "Opened importer error handoff window.";
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Frame Sync Checklist", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("1) Select GameObject with FigmaFrameSyncBinding.", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("2) Run 'Check Figma Updates'.", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("3) Apply selected changes or regenerate current frame.", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("4) Re-run diagnostics before publishing package update.", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            GUILayout.Space(6f);
            if (GUILayout.Button("Open Package README", GUILayout.Height(24f)))
            {
                OpenReadme();
                _status = "Opened package README.";
            }
        }

        private void RefreshEnvironmentState()
        {
            _autoInitializeDependencies = FigmaPackageBootstrapper.GetAutoInitializeEnabled();
            _gitExecutablePath = FindExecutableInPath("git");
            _gitExecutableAvailable = !string.IsNullOrWhiteSpace(_gitExecutablePath);
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

        private List<string> BuildCompletionSummary(FigmaImporterSettings settings)
        {
            var result = new List<string>();
            result.Add($"Environment: {(_gitExecutableAvailable ? "Ready" : "Missing Git")}");
            result.Add($"OAuth: {(settings != null && !string.IsNullOrWhiteSpace(settings.Token) ? "Token Present" : "Token Missing")}");

            var importReady = settings != null && !string.IsNullOrWhiteSpace(settings.Url);
            result.Add($"Import Input: {(importReady ? "Figma URL Set" : "Figma URL Missing")}");
            result.Add($"Node Cache: {FigmaImporter.GetLoadedNodeCountForFlow()}");

            var registry = ImportFallbackRegistry.GetOrCreate();
            var unresolved = registry == null
                ? 0
                : CountUnresolvedFonts(registry, false) + CountUnresolvedSvg(registry, false) + CountUnresolvedIssues(registry, false);
            result.Add($"Fallback: {(unresolved == 0 ? "Resolved" : $"{unresolved} unresolved items")}");
            result.Add($"Diagnostics: {(FigmaImporterIssueTracker.HasEntries ? "Entries captured" : "No captured entries yet")}");
            return result;
        }

        private bool IsStageComplete(FlowStage stage, FigmaImporterSettings settings)
        {
            switch (stage)
            {
                case FlowStage.Environment:
                    return _gitExecutableAvailable;
                case FlowStage.OAuth:
                    return settings != null && !string.IsNullOrWhiteSpace(settings.Token);
                case FlowStage.Import:
                    return FigmaImporter.GetLoadedNodeCountForFlow() > 0;
                case FlowStage.Fallback:
                {
                    var registry = ImportFallbackRegistry.GetOrCreate();
                    if (registry == null)
                    {
                        return false;
                    }

                    var unresolved = CountUnresolvedFonts(registry, false) +
                                     CountUnresolvedSvg(registry, false) +
                                     CountUnresolvedIssues(registry, false);
                    return unresolved == 0;
                }
                case FlowStage.Diagnostics:
                    return FigmaImporterIssueTracker.HasEntries;
                case FlowStage.AutoLayout:
                    return false;
                default:
                    return false;
            }
        }

        private static int CountUnresolvedFonts(ImportFallbackRegistry registry, bool currentSessionOnly)
        {
            if (registry == null || registry.MissingFonts == null)
            {
                return 0;
            }

            return registry.MissingFonts.Count(x =>
                x != null &&
                x.assignedFont == null &&
                (!currentSessionOnly || x.sessionOccurrences > 0));
        }

        private static int CountUnresolvedSvg(ImportFallbackRegistry registry, bool currentSessionOnly)
        {
            if (registry == null || registry.SvgFallbacks == null)
            {
                return 0;
            }

            return registry.SvgFallbacks.Count(x =>
                x != null &&
                x.assignedSprite == null &&
                (!currentSessionOnly || x.sessionOccurrences > 0));
        }

        private static int CountUnresolvedSvgToPng(ImportFallbackRegistry registry, bool currentSessionOnly)
        {
            if (registry == null || registry.SvgFallbacks == null)
            {
                return 0;
            }

            return registry.SvgFallbacks.Count(x =>
                x != null &&
                x.assignedSprite == null &&
                ImportFallbackRegistry.IsSvgToPngFallback(x) &&
                (!currentSessionOnly || x.sessionOccurrences > 0));
        }

        private static int CountUnresolvedIssues(ImportFallbackRegistry registry, bool currentSessionOnly)
        {
            if (registry == null || registry.MissingIssues == null)
            {
                return 0;
            }

            return registry.MissingIssues.Count(x =>
                x != null &&
                (!currentSessionOnly || x.sessionOccurrences > 0));
        }

        private static int AutoMatchFallbacks(ImportFallbackRegistry registry, bool unresolvedOnly, bool currentSessionOnly)
        {
            if (registry == null)
            {
                return 0;
            }

            var updated = 0;
            if (registry.MissingFonts != null)
            {
                foreach (var fontEntry in registry.MissingFonts)
                {
                    if (fontEntry == null)
                    {
                        continue;
                    }

                    if (currentSessionOnly && fontEntry.sessionOccurrences <= 0)
                    {
                        continue;
                    }

                    if (unresolvedOnly && fontEntry.assignedFont != null)
                    {
                        continue;
                    }

                    var before = fontEntry.assignedFont;
                    ImportFallbackRegistry.TryAutoAssignFont(fontEntry);
                    if (before != fontEntry.assignedFont)
                    {
                        updated++;
                    }
                }
            }

            if (registry.SvgFallbacks != null)
            {
                foreach (var svgEntry in registry.SvgFallbacks)
                {
                    if (svgEntry == null)
                    {
                        continue;
                    }

                    if (currentSessionOnly && svgEntry.sessionOccurrences <= 0)
                    {
                        continue;
                    }

                    if (unresolvedOnly && svgEntry.assignedSprite != null)
                    {
                        continue;
                    }

                    var before = svgEntry.assignedSprite;
                    ImportFallbackRegistry.TryAutoAssignSvgSprite(svgEntry);
                    if (before != svgEntry.assignedSprite)
                    {
                        updated++;
                    }
                }
            }

            ImportFallbackRegistry.ApplyFontAssignmentsToFontLinks(registry);
            ImportFallbackRegistry.SaveRegistry(registry);
            return updated;
        }

        private static string ValueOrDash(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        private static string NormalizeIssueOutputRoot(string value)
        {
            try
            {
                return FigmaPathUtils.NormalizeAssetFolderPath(
                    string.IsNullOrWhiteSpace(value) ? DefaultIssueOutputRoot : value);
            }
            catch
            {
                return DefaultIssueOutputRoot;
            }
        }

        private static void OpenReadme()
        {
            var readmePath = ResolveReadmePath();
            if (string.IsNullOrEmpty(readmePath) || !File.Exists(readmePath))
            {
                Debug.LogWarning("[FigmaImporter] README not found for Flow window.");
                return;
            }

            EditorUtility.OpenWithDefaultApp(readmePath);
        }

        private static string ResolveReadmePath()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(FigmaImporterHelpWindow).Assembly);
            if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath))
            {
                return Path.Combine(packageInfo.resolvedPath, "README.md");
            }

            for (var i = 0; i < PackageNames.Length; i++)
            {
                var packageName = PackageNames[i];
                var infoByName = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + packageName + "/package.json");
                if (infoByName != null && !string.IsNullOrEmpty(infoByName.resolvedPath))
                {
                    return Path.Combine(infoByName.resolvedPath, "README.md");
                }

                var projectPackagePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", packageName, "README.md"));
                if (File.Exists(projectPackagePath))
                {
                    return projectPackagePath;
                }
            }

            return string.Empty;
        }
    }
}
