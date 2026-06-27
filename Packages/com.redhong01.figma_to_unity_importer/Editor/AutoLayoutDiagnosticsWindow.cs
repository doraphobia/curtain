using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FigmaImporter.Editor.EditorTree.TreeData;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace FigmaImporter.Editor
{
    internal sealed class AutoLayoutDiagnosticsWindow : EditorWindow
    {
        private const string MenuPath = FigmaImporterMenuPaths.Diagnostics.AutoLayoutDiagnostics;
        private const string DefaultOutputRoot = "Assets/FigmaImporter/_Local/Diagnostics";
        private const int RequestTimeoutSeconds = 60;
        private const int AgentTimeoutSeconds = 600;
        private const string IsoTimestampFormat = "yyyy-MM-dd HH:mm:ss";
        private const string AgentPromptFileName = "agent_handoff_prompt.md";
        private const string AgentCodexOutputFileName = "agent_codex_last_message.md";
        private const string AgentCodexLogFileName = "agent_codex_exec.log";
        private const double ToolDetectionRefreshSeconds = 2.0;

        private string _figmaUrl = string.Empty;
        private string _outputRoot = DefaultOutputRoot;
        private GameObject _rootObject;
        private bool _runImportBeforeAnalysis;
        private bool _captureConsoleLogs = true;
        private bool _includeRawNodePayload = true;
        private bool _isRunning;
        private bool _isRunningAgent;
        private string _status = "Ready";
        private string _agentStatus = "Idle";
        private string _lastOutputFolder = string.Empty;
        private string _lastAgentPromptFile = string.Empty;
        private string _lastAgentResultFile = string.Empty;
        private string _lastAgentLogFile = string.Empty;
        private string _codexExecutable = string.Empty;
        private string _cursorExecutable = string.Empty;
        private string _cursorAgentExecutable = string.Empty;
        private double _lastToolDetectionAt;
        private Vector2 _scroll;

        [MenuItem(MenuPath)]
        internal static void OpenWindow()
        {
            var window = EditorWindow.GetWindow<AutoLayoutDiagnosticsWindow>("AutoLayout Diagnostics");
            window.minSize = new Vector2(640f, 500f);
            window.Show();
        }

        private void OnEnable()
        {
            var settings = FigmaImporterSettings.GetInstance();
            if (settings != null)
            {
                _figmaUrl = string.IsNullOrWhiteSpace(_figmaUrl) ? settings.Url : _figmaUrl;
            }

            if (_rootObject == null)
            {
                _rootObject = TryGetImporterRootObject();
            }

            _outputRoot = NormalizeOutputRoot(_outputRoot);
            RefreshToolDetectionIfNeeded(true);
        }

        private void OnGUI()
        {
            RefreshToolDetectionIfNeeded();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("One-Click Auto Layout Diagnostic Pack", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Collects Figma raw node payload, auto-layout key fields, Unity imported snapshots, fonts/fallback status, and a markdown diff report.",
                MessageType.Info);

            EditorGUI.BeginDisabledGroup(_isRunning || _isRunningAgent);
            _figmaUrl = EditorGUILayout.TextField("Figma URL", _figmaUrl);
            _outputRoot = EditorGUILayout.TextField("Output Folder", _outputRoot);
            _rootObject = (GameObject)EditorGUILayout.ObjectField("Root Object", _rootObject, typeof(GameObject), true);
            _runImportBeforeAnalysis = EditorGUILayout.ToggleLeft("Run Import Pass Before Analysis (optional)", _runImportBeforeAnalysis);
            _captureConsoleLogs = EditorGUILayout.ToggleLeft("Capture Console Logs During Run", _captureConsoleLogs);
            _includeRawNodePayload = EditorGUILayout.ToggleLeft("Save Raw Figma JSON Payloads", _includeRawNodePayload);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Run One-Click Diagnostic", GUILayout.Height(28f)))
            {
                _ = RunDiagnosticAsync();
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_lastOutputFolder)))
            {
                if (GUILayout.Button("Ping Output Folder", GUILayout.Height(28f)))
                {
                    EditorUtility.RevealInFinder(_lastOutputFolder);
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(10f);
            EditorGUILayout.LabelField("Agent Handoff", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "One click can hand this diagnostic pack (or recent importer error pack when no diagnostics pack exists) to an installed coding agent. Codex runs directly; Cursor fallback opens workspace + prompt.",
                MessageType.None);

            EditorGUILayout.LabelField("Detected Tools", BuildToolDetectionSummary());

            var canRunAgentHandoff = !string.IsNullOrWhiteSpace(_lastOutputFolder) || FigmaImporterIssueTracker.HasEntries;
            EditorGUI.BeginDisabledGroup(_isRunning || _isRunningAgent || !canRunAgentHandoff);
            if (GUILayout.Button("Analyze + Fix With Installed Agent", GUILayout.Height(24f)))
            {
                _ = RunAgentHandoffAsync();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_lastAgentPromptFile) || !File.Exists(_lastAgentPromptFile)))
            {
                if (GUILayout.Button("Ping Agent Prompt", GUILayout.Height(22f)))
                {
                    EditorUtility.RevealInFinder(_lastAgentPromptFile);
                }
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_lastAgentResultFile) || !File.Exists(_lastAgentResultFile)))
            {
                if (GUILayout.Button("Ping Agent Result", GUILayout.Height(22f)))
                {
                    EditorUtility.RevealInFinder(_lastAgentResultFile);
                }
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_lastAgentLogFile) || !File.Exists(_lastAgentLogFile)))
            {
                if (GUILayout.Button("Ping Agent Log", GUILayout.Height(22f)))
                {
                    EditorUtility.RevealInFinder(_lastAgentLogFile);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(_agentStatus, MessageType.None);

            GUILayout.Space(10f);
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(_status, MessageType.None);

            if (!string.IsNullOrWhiteSpace(_lastOutputFolder))
            {
                EditorGUILayout.LabelField("Last Pack", _lastOutputFolder);
            }

            EditorGUILayout.EndScrollView();
        }

        private async Task RunDiagnosticAsync()
        {
            if (_isRunning)
            {
                return;
            }

            var flowChainId = FigmaImporterEventFlow.Start(
                "Diagnostics",
                "Run One-Click Diagnostic",
                $"runImportBeforeAnalysis={_runImportBeforeAnalysis}; captureConsole={_captureConsoleLogs}");
            var flowResult = "Completed";
            var flowDetails = string.Empty;
            _isRunning = true;
            _status = "Validating input...";
            Repaint();

            var logCollector = new ConsoleLogCollector();
            try
            {
                FigmaImporterEventFlow.Step("Diagnostics", flowChainId, "ValidateInput");
                var settings = FigmaImporterSettings.GetInstance();
                if (settings == null)
                {
                    throw new InvalidOperationException("FigmaImporterSettings is not available.");
                }

                if (string.IsNullOrWhiteSpace(settings.Token))
                {
                    throw new InvalidOperationException("Figma token is empty. Open importer and set a valid token first.");
                }

                if (string.IsNullOrWhiteSpace(_figmaUrl))
                {
                    _figmaUrl = settings.Url;
                }

                if (string.IsNullOrWhiteSpace(_figmaUrl))
                {
                    throw new InvalidOperationException("Figma URL is empty.");
                }

                if (_captureConsoleLogs)
                {
                    logCollector.Start();
                }

                string fileKey;
                string requestedNodeId;
                bool isDesignPath;
                string apiUrl = BuildApiUrl(_figmaUrl, out fileKey, out requestedNodeId, out isDesignPath);
                if (string.IsNullOrWhiteSpace(apiUrl))
                {
                    throw new InvalidOperationException("Could not build Figma API URL from the provided link.");
                }

                _outputRoot = NormalizeOutputRoot(_outputRoot);
                var outputDirectory = CreateDiagnosticOutputDirectory(_outputRoot);
                _lastOutputFolder = outputDirectory;
                FigmaImporterEventFlow.Step("Diagnostics", flowChainId, "OutputDirectoryReady", outputDirectory);

                _status = "Fetching Figma node payload...";
                Repaint();
                FigmaImporterEventFlow.Step("Diagnostics", flowChainId, "FetchPrimaryPayload");
                string primaryPayload = await GetJsonAsync(apiUrl, settings.Token);
                if (_includeRawNodePayload)
                {
                    WriteText(outputDirectory, "figma_primary_payload.json", primaryPayload);
                }

                var parser = new FigmaParser();
                var roots = parser.ParseResult(primaryPayload);
                var allNodes = FlattenNodes(roots).ToList();
                if (allNodes.Count == 0)
                {
                    throw new InvalidOperationException("No nodes parsed from Figma payload.");
                }

                if (_runImportBeforeAnalysis)
                {
                    _status = "Running importer pass for diagnostic capture...";
                    Repaint();
                    FigmaImporterEventFlow.Step("Diagnostics", flowChainId, "RunImportPass");
                    await RunImportPassForDiagnosticsAsync(apiUrl, roots);
                }

                _status = "Selecting auto-layout samples...";
                Repaint();
                var samples = PickAutoLayoutSamples(allNodes);
                if (samples.Count == 0)
                {
                    throw new InvalidOperationException("No auto-layout nodes found in the fetched payload.");
                }

                var sampleIds = samples
                    .Select(x => x.Node?.id)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var sampleApiUrl = BuildNodesApiUrl(fileKey, sampleIds);

                _status = "Fetching raw payload for selected samples...";
                Repaint();
                FigmaImporterEventFlow.Step("Diagnostics", flowChainId, "FetchSamplePayload");
                string samplePayload = await GetJsonAsync(sampleApiUrl, settings.Token);
                if (_includeRawNodePayload)
                {
                    WriteText(outputDirectory, "figma_samples_payload.json", samplePayload);
                }

                var sampleRoots = parser.ParseResult(samplePayload);
                var sampleNodeLookup = FlattenNodes(sampleRoots)
                    .Where(x => !string.IsNullOrWhiteSpace(x.id))
                    .GroupBy(x => x.id, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

                _status = "Collecting Unity snapshots...";
                Repaint();
                var unitySnapshot = BuildUnitySnapshots(samples);

                _status = "Collecting font/fallback report...";
                Repaint();
                var fontsReport = BuildFontsReport(samples, sampleNodeLookup);

                var figmaFields = BuildFigmaFieldMatrix(samples, sampleNodeLookup, fileKey, isDesignPath);
                var importerSettings = BuildImporterSettingsSnapshot(settings, fileKey, requestedNodeId);
                var fallbackSummary = BuildFallbackRegistrySnapshot();
                var consoleLog = _captureConsoleLogs ? logCollector.ExportAsText() : string.Empty;

                WriteJson(outputDirectory, "figma_field_matrix.json", figmaFields);
                WriteJson(outputDirectory, "unity_snapshot.json", unitySnapshot);
                WriteJson(outputDirectory, "fonts_report.json", fontsReport);
                WriteJson(outputDirectory, "importer_settings.json", importerSettings);
                WriteJson(outputDirectory, "fallback_registry_snapshot.json", fallbackSummary);
                WriteText(outputDirectory, "console_capture.log", consoleLog);

                var reportMarkdown = BuildMarkdownReport(
                    samples,
                    figmaFields,
                    unitySnapshot,
                    fontsReport,
                    importerSettings,
                    fallbackSummary,
                    consoleLog,
                    outputDirectory);
                WriteText(outputDirectory, "autolayout_report.md", reportMarkdown);
                WriteText(outputDirectory, "manual_capture_checklist.md", BuildManualChecklist(samples));

                AssetDatabase.Refresh();
                _status = $"Diagnostic pack generated: {outputDirectory}";
                flowDetails = $"output={outputDirectory}; samples={samples.Count}";
                FigmaImporterEventFlow.Step("Diagnostics", flowChainId, "DiagnosticPackGenerated", flowDetails);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _status = $"Diagnostic failed: {e.Message}";
                flowResult = "Failed";
                flowDetails = e.Message;
            }
            finally
            {
                logCollector.Stop();
                _isRunning = false;
                FigmaImporterEventFlow.End("Diagnostics", flowChainId, flowResult, flowDetails);
                Repaint();
            }
        }

        private async Task RunAgentHandoffAsync()
        {
            if (_isRunningAgent || _isRunning)
            {
                return;
            }

            var flowChainId = FigmaImporterEventFlow.Start("DiagnosticsAgentHandoff", "Analyze + Fix With Installed Agent");
            var flowResult = "Completed";
            var flowDetails = string.Empty;
            if (string.IsNullOrWhiteSpace(_lastOutputFolder) || !Directory.Exists(_lastOutputFolder))
            {
                if (!FigmaImporterIssueTracker.HasEntries)
                {
                    _agentStatus = "No diagnostic pack found and no recent importer errors captured.";
                    flowResult = "Skipped";
                    flowDetails = "No diagnostic pack and no issue tracker entries";
                    FigmaImporterEventFlow.End("DiagnosticsAgentHandoff", flowChainId, flowResult, flowDetails);
                    Repaint();
                    return;
                }

                _lastOutputFolder = FigmaImporterIssueTracker.CreateIssuePack("Assets/FigmaImporter/_Local/IssueHandoff");
                FigmaImporterEventFlow.Step("DiagnosticsAgentHandoff", flowChainId, "CreatedIssuePackFallback", _lastOutputFolder);
            }

            RefreshToolDetectionIfNeeded(true);
            var hasCodex = FigmaAgentToolLocator.IsExecutableAvailable(_codexExecutable);
            var hasCursorAgent = FigmaAgentToolLocator.IsExecutableAvailable(_cursorAgentExecutable);
            var hasCursor = FigmaAgentToolLocator.IsExecutableAvailable(_cursorExecutable);

            if (!hasCodex && !hasCursorAgent && !hasCursor)
            {
                _agentStatus = "No supported agent executable found. Install Codex CLI or Cursor CLI.";
                flowResult = "Skipped";
                flowDetails = "No supported agent executable found";
                FigmaImporterEventFlow.End("DiagnosticsAgentHandoff", flowChainId, flowResult, flowDetails);
                Repaint();
                return;
            }

            _isRunningAgent = true;
            _agentStatus = "Preparing diagnostic handoff prompt...";
            Repaint();

            try
            {
                var promptPath = WriteAgentHandoffPrompt(_lastOutputFolder);
                _lastAgentPromptFile = promptPath;
                FigmaImporterEventFlow.Step("DiagnosticsAgentHandoff", flowChainId, "PromptPrepared", promptPath);
                AssetDatabase.Refresh();

                if (hasCodex)
                {
                    _agentStatus = "Running Codex agent chain...";
                    Repaint();
                    var processResult = await RunCodexAgentAsync(promptPath, _lastOutputFolder);
                    _lastAgentLogFile = processResult.LogPath;
                    _lastAgentResultFile = processResult.ResultPath;

                    if (processResult.ExitCode == 0)
                    {
                        _agentStatus = $"Codex finished. Result saved to: {processResult.ResultPath}";
                        flowDetails = $"agent=codex; result={processResult.ResultPath}";
                    }
                    else if (processResult.TimedOut)
                    {
                        _agentStatus = $"Codex timed out after {AgentTimeoutSeconds}s. See log: {processResult.LogPath}";
                        flowResult = "TimedOut";
                        flowDetails = $"agent=codex; log={processResult.LogPath}";
                    }
                    else
                    {
                        _agentStatus = $"Codex exited with code {processResult.ExitCode}. See log: {processResult.LogPath}";
                        flowResult = "Failed";
                        flowDetails = $"agent=codex; exitCode={processResult.ExitCode}; log={processResult.LogPath}";
                    }

                    return;
                }

                if (hasCursorAgent)
                {
                    _agentStatus = "Running Cursor agent chain...";
                    Repaint();
                    var processResult = await RunCursorAgentAsync(promptPath, _lastOutputFolder);
                    _lastAgentLogFile = processResult.LogPath;
                    _lastAgentResultFile = processResult.ResultPath;

                    if (processResult.ExitCode == 0)
                    {
                        _agentStatus = $"Cursor agent finished. Result saved to: {processResult.ResultPath}";
                        flowDetails = $"agent=cursor-agent; result={processResult.ResultPath}";
                    }
                    else if (processResult.TimedOut)
                    {
                        _agentStatus = $"Cursor agent timed out after {AgentTimeoutSeconds}s. See log: {processResult.LogPath}";
                        flowResult = "TimedOut";
                        flowDetails = $"agent=cursor-agent; log={processResult.LogPath}";
                    }
                    else
                    {
                        _agentStatus = $"Cursor agent exited with code {processResult.ExitCode}. See log: {processResult.LogPath}";
                        flowResult = "Failed";
                        flowDetails = $"agent=cursor-agent; exitCode={processResult.ExitCode}; log={processResult.LogPath}";
                    }

                    return;
                }

                var cursorArgs = $"{QuoteArg(Directory.GetCurrentDirectory())} {QuoteArg(promptPath)}";
                TryLaunchDetachedProcess(_cursorExecutable, cursorArgs, Directory.GetCurrentDirectory(), out var launchError);
                if (string.IsNullOrWhiteSpace(launchError))
                {
                    _agentStatus = "Opened Cursor with workspace and handoff prompt. Continue in Cursor Agent/Chat to apply fixes.";
                    flowResult = "Delegated";
                    flowDetails = $"agent=cursor-cli; prompt={promptPath}";
                }
                else
                {
                    _agentStatus = $"Cursor launch failed: {launchError}";
                    flowResult = "Failed";
                    flowDetails = launchError;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _agentStatus = $"Agent handoff failed: {e.Message}";
                flowResult = "Failed";
                flowDetails = e.Message;
            }
            finally
            {
                _isRunningAgent = false;
                AssetDatabase.Refresh();
                FigmaImporterEventFlow.End("DiagnosticsAgentHandoff", flowChainId, flowResult, flowDetails);
                Repaint();
            }
        }

        private ProcessChainResult CreateProcessResult(string resultPath, string logPath, ProcessRunResult runResult)
        {
            return new ProcessChainResult
            {
                ResultPath = resultPath ?? string.Empty,
                LogPath = logPath ?? string.Empty,
                ExitCode = runResult.ExitCode,
                TimedOut = runResult.TimedOut
            };
        }

        private async Task<ProcessChainResult> RunCodexAgentAsync(string promptPath, string outputFolder)
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var resultPath = Path.Combine(outputFolder, AgentCodexOutputFileName);
            var logPath = Path.Combine(outputFolder, AgentCodexLogFileName);
            var launchPrompt = BuildCodexLaunchPrompt(promptPath, outputFolder);
            var args = string.Join(" ",
                "exec",
                "--full-auto",
                "--skip-git-repo-check",
                "-C",
                QuoteArg(projectRoot),
                "-o",
                QuoteArg(resultPath),
                QuoteArg(launchPrompt));

            var runResult = await RunProcessAsync(_codexExecutable, args, projectRoot, AgentTimeoutSeconds);
            WriteProcessLog(logPath, _codexExecutable, args, runResult);
            return CreateProcessResult(resultPath, logPath, runResult);
        }

        private async Task<ProcessChainResult> RunCursorAgentAsync(string promptPath, string outputFolder)
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var resultPath = Path.Combine(outputFolder, "agent_cursor_last_message.md");
            var logPath = Path.Combine(outputFolder, "agent_cursor_exec.log");
            var launchPrompt = BuildCursorAgentLaunchPrompt(promptPath, outputFolder, resultPath);
            var args = QuoteArg(launchPrompt);

            var runResult = await RunProcessAsync(_cursorAgentExecutable, args, projectRoot, AgentTimeoutSeconds);
            WriteProcessLog(logPath, _cursorAgentExecutable, args, runResult);
            return CreateProcessResult(resultPath, logPath, runResult);
        }

        private static string WriteAgentHandoffPrompt(string outputFolder)
        {
            var promptPath = Path.Combine(outputFolder, AgentPromptFileName);
            var projectRoot = Directory.GetCurrentDirectory();
            var isImporterIssuePack = File.Exists(Path.Combine(outputFolder, "importer_issue_report.md"));

            var sb = new StringBuilder();
            sb.AppendLine(isImporterIssuePack ? "# Figma Importer Issue Agent Handoff" : "# AutoLayout Diagnostics Agent Handoff");
            sb.AppendLine();
            sb.AppendLine($"Generated: {DateTime.Now.ToString(IsoTimestampFormat, CultureInfo.InvariantCulture)}");
            sb.AppendLine($"ProjectRoot: {projectRoot}");
            sb.AppendLine(isImporterIssuePack ? $"IssuePack: {outputFolder}" : $"DiagnosticPack: {outputFolder}");
            sb.AppendLine();
            sb.AppendLine("## Read These Files First");
            if (isImporterIssuePack)
            {
                sb.AppendLine("- importer_issue_report.md");
                sb.AppendLine("- importer_issue_console.log");
                sb.AppendLine("- importer_issue_entries.json");
                sb.AppendLine("- importer_settings_snapshot.json");
                sb.AppendLine("- fallback_registry_snapshot.json");
            }
            else
            {
                sb.AppendLine("- autolayout_report.md");
                sb.AppendLine("- figma_field_matrix.json");
                sb.AppendLine("- unity_snapshot.json");
                sb.AppendLine("- fonts_report.json");
                sb.AppendLine("- fallback_registry_snapshot.json");
                sb.AppendLine("- importer_settings.json");
                sb.AppendLine("- console_capture.log");
            }
            sb.AppendLine();
            sb.AppendLine("## Tasks");
            sb.AppendLine(isImporterIssuePack
                ? "1. Identify root causes for current Figma importer failures from captured errors."
                : "1. Identify root causes for Figma vs Unity auto-layout mismatch.");
            sb.AppendLine("2. Apply code fixes inside this repository when root causes are confirmed.");
            sb.AppendLine("3. Keep security safe: never print or commit full tokens/credentials.");
            sb.AppendLine("4. Provide changed file list, validation steps, and any residual risks.");
            sb.AppendLine();
            sb.AppendLine("## Constraints");
            sb.AppendLine("- Prefer minimal, targeted edits.");
            sb.AppendLine("- Do not overwrite unrelated user changes.");
            sb.AppendLine("- If no code change is needed, explain why with evidence.");

            File.WriteAllText(promptPath, sb.ToString());
            return promptPath;
        }

        private static string BuildCodexLaunchPrompt(string promptPath, string outputFolder)
        {
            return
                $"Read and execute the instructions in '{promptPath}'. " +
                $"Analyze diagnostic files under '{outputFolder}', apply fixes in this repository, and provide a concise final summary.";
        }

        private static string BuildCursorAgentLaunchPrompt(string promptPath, string outputFolder, string resultPath)
        {
            return
                $"Read and execute instructions in '{promptPath}'. " +
                $"Analyze diagnostics in '{outputFolder}', apply fixes in the current repository, and write final summary to '{resultPath}'.";
        }

        private static async Task<ProcessRunResult> RunProcessAsync(
            string executablePath,
            string arguments,
            string workingDirectory,
            int timeoutSeconds)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var process = new Process())
            {
                process.StartInfo = startInfo;
                process.EnableRaisingEvents = true;

                var exited = new TaskCompletionSource<bool>();
                process.Exited += (_, __) => exited.TrySetResult(true);

                process.Start();

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
                var completed = await Task.WhenAny(exited.Task, timeoutTask);

                var timedOut = completed == timeoutTask;
                if (timedOut)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        // Ignore kill failures because process may already be gone.
                    }
                }

                await Task.WhenAll(stdoutTask, stderrTask);
                var exitCode = timedOut ? -1 : process.ExitCode;

                return new ProcessRunResult
                {
                    ExitCode = exitCode,
                    TimedOut = timedOut,
                    StdOut = stdoutTask.Result ?? string.Empty,
                    StdErr = stderrTask.Result ?? string.Empty
                };
            }
        }

        private static void WriteProcessLog(
            string logPath,
            string executablePath,
            string arguments,
            ProcessRunResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"command: {executablePath} {arguments}");
            sb.AppendLine($"exitCode: {result.ExitCode}");
            sb.AppendLine($"timedOut: {result.TimedOut}");
            sb.AppendLine();
            sb.AppendLine("stdout:");
            sb.AppendLine(result.StdOut ?? string.Empty);
            sb.AppendLine();
            sb.AppendLine("stderr:");
            sb.AppendLine(result.StdErr ?? string.Empty);
            File.WriteAllText(logPath, sb.ToString());
        }

        private static bool TryLaunchDetachedProcess(string executablePath, string arguments, string workingDirectory, out string error)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(startInfo);
                error = string.Empty;
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        private static string QuoteArg(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private void RefreshToolDetectionIfNeeded(bool force = false)
        {
            var now = EditorApplication.timeSinceStartup;
            if (!force && now - _lastToolDetectionAt < ToolDetectionRefreshSeconds)
            {
                return;
            }

            _lastToolDetectionAt = now;
            var detection = FigmaAgentToolLocator.Detect();
            _codexExecutable = detection.CodexExecutable;
            _cursorExecutable = detection.CursorExecutable;
            _cursorAgentExecutable = detection.CursorAgentExecutable;
        }

        private string BuildToolDetectionSummary()
        {
            return FigmaAgentToolLocator.BuildSummary(_codexExecutable, _cursorAgentExecutable, _cursorExecutable);
        }

        private async Task RunImportPassForDiagnosticsAsync(string apiUrl, IList<Node> parsedRoots)
        {
            if (_rootObject == null)
            {
                throw new InvalidOperationException("Root Object is required when 'Run Import Pass Before Analysis' is enabled.");
            }

            var importer = EditorWindow.GetWindow<FigmaImporter>();
            if (importer == null)
            {
                throw new InvalidOperationException("FigmaImporter window could not be initialized.");
            }

            var settings = FigmaImporterSettings.GetInstance();
            settings.Url = _figmaUrl;

            SetImporterRootObject(_rootObject);
            await importer.GetNodes(apiUrl, origin: "Diagnostics Import Pass");

            var importedNodes = GetImporterNodes();
            if (importedNodes == null || importedNodes.Count == 0)
            {
                importedNodes = parsedRoots != null ? new List<Node>(parsedRoots) : new List<Node>();
            }

            if (importedNodes.Count == 0)
            {
                throw new InvalidOperationException("Import pass aborted: no nodes available for generation.");
            }

            var treeElements = BuildTreeElements(importedNodes);
            NodesAnalyzer.AnalyzeRenderMode(importedNodes, treeElements);
            NodesAnalyzer.CheckActions(importedNodes, treeElements);

            ImportFallbackRegistry.BeginGenerationSession("AutoLayout Diagnostic Pass");
            try
            {
                var generator = new FigmaNodeGenerator(importer, CancellationToken.None);
                FigmaNodesProgressInfo.CurrentNode = 0;
                FigmaNodesProgressInfo.NodesCount = treeElements.Count;
                foreach (var node in importedNodes)
                {
                    await generator.GenerateNode(node, _rootObject, treeElements);
                }
            }
            finally
            {
                ImportFallbackRegistry.EndGenerationSession();
                FigmaNodesProgressInfo.HideProgress();
            }
        }

        private static List<NodeTreeElement> BuildTreeElements(IList<Node> nodes)
        {
            var result = new List<NodeTreeElement>();
            var idCounter = 0;
            result.Add(new NodeTreeElement("Root", "Root", ActionType.None, null, -1, idCounter));
            idCounter++;
            AddNodeElementsRecursive(nodes, 0, ref idCounter, result);
            return result;
        }

        private static void AddNodeElementsRecursive(IList<Node> nodes, int depth, ref int idCounter, List<NodeTreeElement> result)
        {
            if (nodes == null)
            {
                return;
            }

            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null)
                {
                    continue;
                }

                result.Add(new NodeTreeElement(node.name ?? string.Empty, node.id ?? string.Empty, ActionType.None, null, depth, idCounter));
                idCounter++;
                AddNodeElementsRecursive(node.children, depth + 1, ref idCounter, result);
            }
        }

        private static string BuildApiUrl(string figmaUrl, out string fileKey, out string nodeId, out bool isDesignPath)
        {
            fileKey = string.Empty;
            nodeId = string.Empty;
            isDesignPath = true;

            if (string.IsNullOrWhiteSpace(figmaUrl))
            {
                return string.Empty;
            }

            if (!Uri.TryCreate(figmaUrl, UriKind.Absolute, out var uri))
            {
                return string.Empty;
            }

            var parts = uri.AbsolutePath.Trim('/').Split('/');
            var keyIndex = Array.FindIndex(parts, x =>
                x.Equals("design", StringComparison.OrdinalIgnoreCase) ||
                x.Equals("file", StringComparison.OrdinalIgnoreCase));
            if (keyIndex < 0 || keyIndex + 1 >= parts.Length)
            {
                return string.Empty;
            }

            isDesignPath = parts[keyIndex].Equals("design", StringComparison.OrdinalIgnoreCase);
            fileKey = parts[keyIndex + 1];
            var query = ParseQuery(uri.Query);
            if (query.TryGetValue("node-id", out var nodeToken) && !string.IsNullOrWhiteSpace(nodeToken))
            {
                nodeId = nodeToken.Replace("-", ":");
            }

            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return $"https://api.figma.com/v1/files/{fileKey}";
            }

            return $"https://api.figma.com/v1/files/{fileKey}/nodes?ids={UnityWebRequest.EscapeURL(nodeId)}";
        }

        private static string BuildNodesApiUrl(string fileKey, IList<string> nodeIds)
        {
            var encoded = string.Join(",", nodeIds.Select(UnityWebRequest.EscapeURL));
            return $"https://api.figma.com/v1/files/{fileKey}/nodes?ids={encoded}";
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(query))
            {
                return result;
            }

            var parts = query.TrimStart('?').Split('&');
            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part))
                {
                    continue;
                }

                var separatorIndex = part.IndexOf('=');
                if (separatorIndex < 0)
                {
                    result[Uri.UnescapeDataString(part)] = string.Empty;
                    continue;
                }

                var key = Uri.UnescapeDataString(part.Substring(0, separatorIndex));
                var value = Uri.UnescapeDataString(part.Substring(separatorIndex + 1));
                result[key] = value;
            }

            return result;
        }

        private static async Task<string> GetJsonAsync(string requestUrl, string token)
        {
            using (var request = UnityWebRequest.Get(requestUrl))
            {
                request.timeout = RequestTimeoutSeconds;
                request.SetRequestHeader("Authorization", $"Bearer {token}");
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Delay(100);
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new InvalidOperationException(
                        $"Figma request failed ({request.responseCode}): {request.error}\nURL: {requestUrl}");
                }

                return request.downloadHandler.text ?? string.Empty;
            }
        }

        private static IEnumerable<Node> FlattenNodes(IEnumerable<Node> roots)
        {
            if (roots == null)
            {
                yield break;
            }

            var stack = new Stack<Node>(roots.Where(x => x != null).Reverse());
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                yield return node;
                if (node.children == null || node.children.Length == 0)
                {
                    continue;
                }

                for (var i = node.children.Length - 1; i >= 0; i--)
                {
                    var child = node.children[i];
                    if (child != null)
                    {
                        stack.Push(child);
                    }
                }
            }
        }

        private static List<SampleNodeChoice> PickAutoLayoutSamples(IList<Node> allNodes)
        {
            var candidates = allNodes
                .Where(AutoLayoutUtils.IsAutoLayoutContainer)
                .GroupBy(x => x.id ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();
            if (candidates.Count == 0)
            {
                return new List<SampleNodeChoice>();
            }

            var scored = candidates
                .Select(node =>
                {
                    var scoreInfo = ScoreNode(node);
                    return new SampleNodeChoice
                    {
                        Node = node,
                        Score = scoreInfo.Score,
                        Reason = scoreInfo.Reason
                    };
                })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Node != null && x.Node.children != null ? x.Node.children.Length : 0)
                .ToList();

            var chosen = new List<SampleNodeChoice>();

            var complex = scored.FirstOrDefault();
            if (complex != null)
            {
                complex.Category = "complex_failure_candidate";
                chosen.Add(complex);
            }

            var simple = scored
                .OrderBy(x => x.Score)
                .ThenBy(x => x.Node != null && x.Node.children != null ? x.Node.children.Length : 0)
                .FirstOrDefault(x => !ContainsNode(chosen, x.Node));
            if (simple != null)
            {
                simple.Category = "simple_failure_candidate";
                chosen.Add(simple);
            }

            var medianScore = scored[scored.Count / 2].Score;
            var normal = scored
                .OrderBy(x => Mathf.Abs(x.Score - medianScore))
                .ThenByDescending(x => x.Score)
                .FirstOrDefault(x => !ContainsNode(chosen, x.Node));
            if (normal != null)
            {
                normal.Category = "normal_sample_candidate";
                chosen.Add(normal);
            }

            foreach (var remaining in scored)
            {
                if (chosen.Count >= 3)
                {
                    break;
                }

                if (ContainsNode(chosen, remaining.Node))
                {
                    continue;
                }

                remaining.Category = $"additional_sample_{chosen.Count + 1}";
                chosen.Add(remaining);
            }

            return chosen;
        }

        private static bool ContainsNode(IList<SampleNodeChoice> selected, Node node)
        {
            if (node == null)
            {
                return false;
            }

            return selected.Any(x =>
                x != null &&
                x.Node != null &&
                string.Equals(x.Node.id, node.id, StringComparison.OrdinalIgnoreCase));
        }

        private static ScoreInfo ScoreNode(Node node)
        {
            var score = 0;
            var reasons = new List<string>();

            var rotation = Mathf.Abs(TransformUtils.ResolveNodeRotation(node));
            if (rotation > 0.1f)
            {
                score += 3;
                reasons.Add($"rotation={rotation.ToString("0.###", CultureInfo.InvariantCulture)}");
            }

            if (!string.IsNullOrWhiteSpace(node.layoutWrap) &&
                node.layoutWrap.Equals("WRAP", StringComparison.OrdinalIgnoreCase))
            {
                score += 3;
                reasons.Add("wrap");
            }

            if (!string.IsNullOrWhiteSpace(node.primaryAxisAlignItems) &&
                node.primaryAxisAlignItems.Equals("SPACE_BETWEEN", StringComparison.OrdinalIgnoreCase))
            {
                score += 1;
                reasons.Add("space-between");
            }

            if (!string.IsNullOrWhiteSpace(node.counterAxisAlignItems) &&
                node.counterAxisAlignItems.Equals("BASELINE", StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
                reasons.Add("baseline");
            }

            var children = node.children ?? Array.Empty<Node>();
            if (children.Length >= 6)
            {
                score += 1;
                reasons.Add($"children={children.Length}");
            }

            if (children.Any(AutoLayoutUtils.IsAutoLayoutContainer))
            {
                score += 2;
                reasons.Add("nested-auto-layout");
            }

            if (children.Any(AutoLayoutUtils.IsAbsolutePositionedInAutoLayout))
            {
                score += 2;
                reasons.Add("absolute-child-in-auto-layout");
            }

            if (children.Any(x => x != null && string.Equals(x.type, "TEXT", StringComparison.OrdinalIgnoreCase)))
            {
                score += 1;
                reasons.Add("text-children");
            }

            return new ScoreInfo
            {
                Score = score,
                Reason = reasons.Count == 0 ? "baseline-auto-layout" : string.Join(", ", reasons)
            };
        }

        private static JArray BuildFigmaFieldMatrix(
            IList<SampleNodeChoice> samples,
            IDictionary<string, Node> sampleLookup,
            string fileKey,
            bool isDesignPath)
        {
            var result = new JArray();
            foreach (var sample in samples)
            {
                var id = sample.Node != null ? sample.Node.id : string.Empty;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var node = sampleLookup.TryGetValue(id, out var resolved) ? resolved : sample.Node;
                var entry = BuildNodeFieldEntry(node);
                entry["category"] = sample.Category ?? string.Empty;
                entry["selectionScore"] = sample.Score;
                entry["selectionReason"] = sample.Reason ?? string.Empty;
                entry["figmaLink"] = BuildNodeLink(fileKey, id, isDesignPath);
                result.Add(entry);
            }

            return result;
        }

        private static JObject BuildNodeFieldEntry(Node node)
        {
            var json = new JObject();
            if (node == null)
            {
                return json;
            }

            json["id"] = node.id ?? string.Empty;
            json["name"] = node.name ?? string.Empty;
            json["type"] = node.type ?? string.Empty;
            json["layoutMode"] = node.layoutMode ?? string.Empty;
            json["clipsContent"] = node.clipsContent;
            json["isMask"] = node.isMask;
            json["maskType"] = node.maskType ?? string.Empty;
            json["itemSpacing"] = node.itemSpacing;
            json["counterAxisSpacing"] = node.counterAxisSpacing;
            json["paddingLeft"] = node.paddingLeft;
            json["paddingRight"] = node.paddingRight;
            json["paddingTop"] = node.paddingTop;
            json["paddingBottom"] = node.paddingBottom;
            json["primaryAxisAlignItems"] = node.primaryAxisAlignItems ?? string.Empty;
            json["counterAxisAlignItems"] = node.counterAxisAlignItems ?? string.Empty;
            json["layoutSizingHorizontal"] = node.layoutSizingHorizontal ?? string.Empty;
            json["layoutSizingVertical"] = node.layoutSizingVertical ?? string.Empty;
            json["layoutGrow"] = node.layoutGrow;
            json["layoutPositioning"] = node.layoutPositioning ?? string.Empty;
            json["rotation"] = TransformUtils.ResolveNodeRotation(node);
            var resolvedSize = TransformUtils.ResolveNodeSize(node, node.absoluteBoundingBox);
            json["width"] = resolvedSize.x;
            json["height"] = resolvedSize.y;
            json["absoluteBoundingBox"] = BuildBoundingBox(node.absoluteBoundingBox);
            json["relativeTransform"] = BuildRelativeTransform(node.relativeTransform);

            var children = new JArray();
            if (node.children != null)
            {
                for (var i = 0; i < node.children.Length; i++)
                {
                    var child = node.children[i];
                    if (child == null)
                    {
                        continue;
                    }

                    children.Add(BuildNodeFieldEntryForChild(child));
                }
            }

            json["directChildren"] = children;
            return json;
        }

        private static JObject BuildNodeFieldEntryForChild(Node node)
        {
            var json = new JObject
            {
                ["id"] = node.id ?? string.Empty,
                ["name"] = node.name ?? string.Empty,
                ["type"] = node.type ?? string.Empty,
                ["layoutMode"] = node.layoutMode ?? string.Empty,
                ["clipsContent"] = node.clipsContent,
                ["isMask"] = node.isMask,
                ["maskType"] = node.maskType ?? string.Empty,
                ["layoutPositioning"] = node.layoutPositioning ?? string.Empty,
                ["layoutSizingHorizontal"] = node.layoutSizingHorizontal ?? string.Empty,
                ["layoutSizingVertical"] = node.layoutSizingVertical ?? string.Empty,
                ["layoutGrow"] = node.layoutGrow,
                ["rotation"] = TransformUtils.ResolveNodeRotation(node)
            };
            var resolvedSize = TransformUtils.ResolveNodeSize(node, node.absoluteBoundingBox);
            json["width"] = resolvedSize.x;
            json["height"] = resolvedSize.y;
            json["absoluteBoundingBox"] = BuildBoundingBox(node.absoluteBoundingBox);
            return json;
        }

        private static JObject BuildBoundingBox(AbsoluteBoundingBox box)
        {
            if (box == null)
            {
                return new JObject();
            }

            return new JObject
            {
                ["x"] = box.x,
                ["y"] = box.y,
                ["width"] = box.width,
                ["height"] = box.height
            };
        }

        private static JArray BuildRelativeTransform(float[][] matrix)
        {
            var array = new JArray();
            if (matrix == null)
            {
                return array;
            }

            for (var row = 0; row < matrix.Length; row++)
            {
                var rowArray = new JArray();
                var rowValues = matrix[row];
                if (rowValues != null)
                {
                    for (var col = 0; col < rowValues.Length; col++)
                    {
                        rowArray.Add(rowValues[col]);
                    }
                }

                array.Add(rowArray);
            }

            return array;
        }

        private static JArray BuildUnitySnapshots(IList<SampleNodeChoice> samples)
        {
            var result = new JArray();
            foreach (var sample in samples)
            {
                var nodeId = sample.Node != null ? sample.Node.id : string.Empty;
                var nodeName = sample.Node != null ? sample.Node.name : string.Empty;
                var found = FindGameObjectByNodeId(nodeId);
                var entry = new JObject
                {
                    ["category"] = sample.Category ?? string.Empty,
                    ["figmaNodeId"] = nodeId,
                    ["figmaNodeName"] = nodeName,
                    ["foundInScene"] = found != null
                };

                if (found != null)
                {
                    entry["gameObjectPath"] = GetHierarchyPath(found.transform);
                    entry["self"] = CaptureGameObjectSnapshot(found);
                    var children = new JArray();
                    for (var i = 0; i < found.transform.childCount; i++)
                    {
                        var child = found.transform.GetChild(i);
                        if (child == null)
                        {
                            continue;
                        }

                        children.Add(CaptureGameObjectSnapshot(child.gameObject));
                    }
                    entry["directChildren"] = children;
                }

                result.Add(entry);
            }

            return result;
        }

        private static JObject CaptureGameObjectSnapshot(GameObject go)
        {
            var result = new JObject
            {
                ["name"] = go != null ? go.name : string.Empty,
                ["nodeId"] = go != null ? ExtractNodeIdFromObjectName(go.name) : string.Empty
            };

            if (go == null)
            {
                return result;
            }

            var rectTransform = go.transform as RectTransform;
            if (rectTransform != null)
            {
                result["rectTransform"] = new JObject
                {
                    ["anchoredPosition"] = ToVector2(rectTransform.anchoredPosition),
                    ["sizeDelta"] = ToVector2(rectTransform.sizeDelta),
                    ["anchorMin"] = ToVector2(rectTransform.anchorMin),
                    ["anchorMax"] = ToVector2(rectTransform.anchorMax),
                    ["pivot"] = ToVector2(rectTransform.pivot),
                    ["offsetMin"] = ToVector2(rectTransform.offsetMin),
                    ["offsetMax"] = ToVector2(rectTransform.offsetMax),
                    ["rectWidth"] = rectTransform.rect.width,
                    ["rectHeight"] = rectTransform.rect.height,
                    ["rotationZ"] = rectTransform.localEulerAngles.z
                };
            }

            if (go.TryGetComponent<HorizontalLayoutGroup>(out var horizontal) && horizontal != null)
            {
                result["horizontalLayoutGroup"] = CaptureLayoutGroup(horizontal);
            }

            if (go.TryGetComponent<VerticalLayoutGroup>(out var vertical) && vertical != null)
            {
                result["verticalLayoutGroup"] = CaptureLayoutGroup(vertical);
            }

            if (go.TryGetComponent<ContentSizeFitter>(out var fitter) && fitter != null)
            {
                result["contentSizeFitter"] = new JObject
                {
                    ["horizontalFit"] = fitter.horizontalFit.ToString(),
                    ["verticalFit"] = fitter.verticalFit.ToString()
                };
            }

            if (go.TryGetComponent<LayoutElement>(out var element) && element != null)
            {
                result["layoutElement"] = new JObject
                {
                    ["ignoreLayout"] = element.ignoreLayout,
                    ["minWidth"] = element.minWidth,
                    ["minHeight"] = element.minHeight,
                    ["preferredWidth"] = element.preferredWidth,
                    ["preferredHeight"] = element.preferredHeight,
                    ["flexibleWidth"] = element.flexibleWidth,
                    ["flexibleHeight"] = element.flexibleHeight
                };
            }

            if (go.TryGetComponent<TextMeshProUGUI>(out var tmp) && tmp != null)
            {
                result["tmp"] = new JObject
                {
                    ["fontAsset"] = tmp.font != null ? tmp.font.name : string.Empty,
                    ["fontSize"] = tmp.fontSize,
                    ["textLength"] = tmp.text != null ? tmp.text.Length : 0,
                    ["textPreview"] = TrimPreview(tmp.text, 80)
                };
            }

            return result;
        }

        private static JObject CaptureLayoutGroup(HorizontalOrVerticalLayoutGroup group)
        {
            return new JObject
            {
                ["spacing"] = group.spacing,
                ["paddingLeft"] = group.padding.left,
                ["paddingRight"] = group.padding.right,
                ["paddingTop"] = group.padding.top,
                ["paddingBottom"] = group.padding.bottom,
                ["childAlignment"] = group.childAlignment.ToString(),
                ["childControlWidth"] = group.childControlWidth,
                ["childControlHeight"] = group.childControlHeight,
                ["childScaleWidth"] = group.childScaleWidth,
                ["childScaleHeight"] = group.childScaleHeight,
                ["childForceExpandWidth"] = group.childForceExpandWidth,
                ["childForceExpandHeight"] = group.childForceExpandHeight
            };
        }

        private static JObject BuildFontsReport(IList<SampleNodeChoice> samples, IDictionary<string, Node> sampleNodeLookup)
        {
            var textNodes = new List<Node>();
            foreach (var sample in samples)
            {
                if (sample?.Node == null || string.IsNullOrWhiteSpace(sample.Node.id))
                {
                    continue;
                }

                if (!sampleNodeLookup.TryGetValue(sample.Node.id, out var root))
                {
                    root = sample.Node;
                }

                textNodes.AddRange(FlattenNodes(new[] { root }).Where(x =>
                    x != null &&
                    string.Equals(x.type, "TEXT", StringComparison.OrdinalIgnoreCase) &&
                    x.style != null));
            }

            var requestedFonts = new List<FontRequestInfo>();
            foreach (var textNode in textNodes)
            {
                var style = textNode.style;
                requestedFonts.Add(new FontRequestInfo
                {
                    FontFamily = style.fontFamily ?? string.Empty,
                    FontPostScriptName = style.fontPostScriptName ?? string.Empty,
                    FontWeight = style.fontWeight
                });
            }

            var uniqueRequested = requestedFonts
                .GroupBy(x => NormalizeFontKey(x.FontPostScriptName, x.FontFamily, x.FontWeight))
                .Select(x => x.First())
                .ToList();

            var projectFontGuids = AssetDatabase.FindAssets("t:TMP_FontAsset");
            var projectFonts = new List<string>();
            for (var i = 0; i < projectFontGuids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(projectFontGuids[i]);
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font != null)
                {
                    projectFonts.Add(font.name);
                }
            }

            string[] osFontNames;
            try
            {
                osFontNames = Font.GetOSInstalledFontNames() ?? Array.Empty<string>();
            }
            catch
            {
                osFontNames = Array.Empty<string>();
            }

            var missingRegistry = ImportFallbackRegistry.GetOrCreate();
            var missingFontEntries = missingRegistry.MissingFonts ?? new List<MissingFontEntry>();

            var requestedArray = new JArray();
            foreach (var fontRequest in uniqueRequested)
            {
                var familyKey = Normalize(fontRequest.FontFamily);
                var postScriptKey = Normalize(fontRequest.FontPostScriptName);

                var inProject = projectFonts.Any(x =>
                {
                    var key = Normalize(x);
                    return (!string.IsNullOrWhiteSpace(familyKey) && key.Contains(familyKey)) ||
                           (!string.IsNullOrWhiteSpace(postScriptKey) && key.Contains(postScriptKey));
                });

                var inSystem = osFontNames.Any(x =>
                {
                    var key = Normalize(x);
                    return (!string.IsNullOrWhiteSpace(familyKey) && key.Contains(familyKey)) ||
                           (!string.IsNullOrWhiteSpace(postScriptKey) && key.Contains(postScriptKey));
                });

                var fallbackTriggered = missingFontEntries.Any(entry =>
                {
                    if (entry == null || entry.candidateNames == null)
                    {
                        return false;
                    }

                    return entry.candidateNames.Any(candidate =>
                    {
                        var candidateKey = Normalize(candidate);
                        return (!string.IsNullOrWhiteSpace(familyKey) && candidateKey.Contains(familyKey)) ||
                               (!string.IsNullOrWhiteSpace(postScriptKey) && candidateKey.Contains(postScriptKey));
                    });
                });

                requestedArray.Add(new JObject
                {
                    ["fontFamily"] = fontRequest.FontFamily,
                    ["fontPostScriptName"] = fontRequest.FontPostScriptName,
                    ["fontWeight"] = fontRequest.FontWeight,
                    ["foundInProjectTmpAssets"] = inProject,
                    ["foundInCurrentOsFonts"] = inSystem,
                    ["fallbackTriggeredInRegistry"] = fallbackTriggered
                });
            }

            var report = new JObject
            {
                ["requestedFonts"] = requestedArray,
                ["projectTmpFontCount"] = projectFonts.Count,
                ["osInstalledFontCount"] = osFontNames.Length,
                ["missingFontRegistryCount"] = missingFontEntries.Count
            };
            return report;
        }

        private static JObject BuildImporterSettingsSnapshot(FigmaImporterSettings settings, string fileKey, string requestedNodeId)
        {
            var importerWindow = EditorWindow.HasOpenInstances<FigmaImporter>()
                ? EditorWindow.GetWindow<FigmaImporter>()
                : null;
            var scale = importerWindow != null ? GetImporterScale(importerWindow) : 1f;
            var rootObject = TryGetImporterRootObject();

            return new JObject
            {
                ["url"] = settings != null ? settings.Url : string.Empty,
                ["fileKey"] = fileKey ?? string.Empty,
                ["requestedNodeId"] = requestedNodeId ?? string.Empty,
                ["rendersPath"] = settings != null ? settings.RendersPath : string.Empty,
                ["tokenPresent"] = settings != null && !string.IsNullOrWhiteSpace(settings.Token),
                ["scale"] = scale,
                ["rootObjectName"] = rootObject != null ? rootObject.name : string.Empty,
                ["rootObjectScenePath"] = rootObject != null && rootObject.scene.IsValid() ? rootObject.scene.path : string.Empty,
                ["vectorGraphicsDefineEnabled"] = IsVectorGraphicsDefineEnabled()
            };
        }

        private static JObject BuildFallbackRegistrySnapshot()
        {
            var registry = ImportFallbackRegistry.GetOrCreate();
            return new JObject
            {
                ["sessionActive"] = registry.SessionActive,
                ["lastSessionLabel"] = registry.LastSessionLabel,
                ["lastSessionStartedAt"] = registry.LastSessionStartedAt,
                ["lastSessionFinishedAt"] = registry.LastSessionFinishedAt,
                ["lastSessionMissingFonts"] = registry.LastSessionMissingFonts,
                ["lastSessionSvgFallbacks"] = registry.LastSessionSvgFallbacks,
                ["lastSessionSvgToPngFallbacks"] = registry.LastSessionSvgToPngFallbacks,
                ["lastSessionMissingIssues"] = registry.LastSessionMissingIssues,
                ["missingFontsTotal"] = registry.MissingFonts != null ? registry.MissingFonts.Count : 0,
                ["svgFallbacksTotal"] = registry.SvgFallbacks != null ? registry.SvgFallbacks.Count : 0,
                ["missingIssuesTotal"] = registry.MissingIssues != null ? registry.MissingIssues.Count : 0
            };
        }

        private static string BuildMarkdownReport(
            IList<SampleNodeChoice> samples,
            JArray figmaFields,
            JArray unitySnapshots,
            JObject fontsReport,
            JObject importerSettings,
            JObject fallbackSummary,
            string consoleLog,
            string outputDirectory)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Auto Layout Diagnostic Report");
            sb.AppendLine();
            sb.AppendLine($"Generated: {DateTime.Now.ToString(IsoTimestampFormat, CultureInfo.InvariantCulture)}");
            sb.AppendLine($"Output: `{outputDirectory}`");
            sb.AppendLine();

            sb.AppendLine("## Selected Sample Nodes");
            sb.AppendLine();
            sb.AppendLine("| Category | Node | Score | Link |");
            sb.AppendLine("| --- | --- | --- | --- |");
            foreach (var sample in samples)
            {
                var node = sample.Node;
                var nodeName = node != null ? node.name : string.Empty;
                var nodeId = node != null ? node.id : string.Empty;
                var link = figmaFields
                    .FirstOrDefault(x => string.Equals((string)x["id"], nodeId, StringComparison.OrdinalIgnoreCase))?["figmaLink"]?.ToString() ?? string.Empty;
                sb.AppendLine($"| {sample.Category} | {EscapePipe(nodeName)} [{EscapePipe(nodeId)}] | {sample.Score} | {EscapePipe(link)} |");
            }
            sb.AppendLine();

            sb.AppendLine("## What This Pack Includes");
            sb.AppendLine();
            sb.AppendLine("- `figma_field_matrix.json` with required auto-layout fields and direct-child fields.");
            sb.AppendLine("- `unity_snapshot.json` with RectTransform/LayoutGroup/ContentSizeFitter/LayoutElement snapshots.");
            sb.AppendLine("- `fonts_report.json` with requested font mapping and fallback flags.");
            sb.AppendLine("- `importer_settings.json` and `fallback_registry_snapshot.json` for environment context.");
            sb.AppendLine("- `console_capture.log` for this diagnostic run.");
            sb.AppendLine();

            var consoleLines = string.IsNullOrWhiteSpace(consoleLog) ? 0 : consoleLog.Split('\n').Length;
            sb.AppendLine($"Console log lines captured: {consoleLines}");
            sb.AppendLine($"Unity snapshots captured: {unitySnapshots.Count}");
            sb.AppendLine($"Figma sample records captured: {figmaFields.Count}");
            sb.AppendLine($"Requested font groups captured: {fontsReport?["requestedFonts"]?.Count() ?? 0}");
            sb.AppendLine();

            sb.AppendLine("## Quick Notes");
            sb.AppendLine();
            sb.AppendLine("- Compare each sample's `width/height/rotation` between `figma_field_matrix.json` and `unity_snapshot.json` first.");
            sb.AppendLine("- For auto-layout drift, inspect `layoutPositioning`, `layoutGrow`, and per-child `layoutSizing*` values.");
            sb.AppendLine("- For text drift, inspect `fonts_report.json` and fallback counters.");
            sb.AppendLine($"- Current fallback snapshot: fonts={fallbackSummary?["lastSessionMissingFonts"]}, svg={fallbackSummary?["lastSessionSvgFallbacks"]}, svg->png={fallbackSummary?["lastSessionSvgToPngFallbacks"]}, other={fallbackSummary?["lastSessionMissingIssues"]}.");
            sb.AppendLine("- Non-automated evidence checklist is exported in `manual_capture_checklist.md`.");

            return sb.ToString();
        }

        private static string BuildManualChecklist(IList<SampleNodeChoice> samples)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Manual Capture Checklist (Not Fully Automatable)");
            sb.AppendLine();
            sb.AppendLine("The tool already exported structured JSON snapshots. Add these manual screenshots for visual confirmation:");
            sb.AppendLine();
            foreach (var sample in samples)
            {
                var nodeName = sample.Node != null ? sample.Node.name : string.Empty;
                var nodeId = sample.Node != null ? sample.Node.id : string.Empty;
                sb.AppendLine($"## {sample.Category}: {nodeName} [{nodeId}]");
                sb.AppendLine();
                sb.AppendLine("- Figma Inspect screenshot with: layoutMode, itemSpacing, padding, primary/counter axis align, layoutSizingHorizontal/Vertical, layoutGrow, layoutPositioning, rotation, width, height.");
                sb.AppendLine("- Unity Inspector screenshot of imported node with: RectTransform, LayoutGroup, ContentSizeFitter, LayoutElement.");
                sb.AppendLine("- Unity Inspector screenshot of direct children of this node.");
                sb.AppendLine();
            }

            sb.AppendLine("Cross-machine check (Windows/Mac):");
            sb.AppendLine();
            sb.AppendLine("- Run the same diagnostic tool on both OS, compare `fonts_report.json` and fallback results.");
            sb.AppendLine("- If differences remain, attach the two machine screenshots side-by-side for the same sample node.");
            return sb.ToString();
        }

        private static string EscapePipe(string value)
        {
            return (value ?? string.Empty).Replace("|", "\\|");
        }

        private static JObject ToVector2(Vector2 value)
        {
            return new JObject
            {
                ["x"] = value.x,
                ["y"] = value.y
            };
        }

        private static string TrimPreview(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            {
                return text ?? string.Empty;
            }

            return text.Substring(0, maxLength) + "...";
        }

        private static string BuildNodeLink(string fileKey, string nodeId, bool isDesignPath)
        {
            if (string.IsNullOrWhiteSpace(fileKey) || string.IsNullOrWhiteSpace(nodeId))
            {
                return string.Empty;
            }

            var nodeToken = nodeId.Replace(":", "-");
            var modePath = isDesignPath ? "design" : "file";
            return $"https://www.figma.com/{modePath}/{fileKey}/AutoLayout-Diagnostic?node-id={nodeToken}";
        }

        private static string NormalizeFontKey(string postScriptName, string family, int weight)
        {
            if (!string.IsNullOrWhiteSpace(postScriptName))
            {
                return Normalize(postScriptName);
            }

            if (string.IsNullOrWhiteSpace(family))
            {
                return string.Empty;
            }

            return Normalize($"{family}-{weight}");
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : Regex.Replace(value, @"[\s\-_]+", string.Empty).ToLowerInvariant();
        }

        private static string CreateDiagnosticOutputDirectory(string outputRoot)
        {
            var normalizedRoot = NormalizeOutputRoot(outputRoot);

            var rootAbsolutePath = FigmaPathUtils.ToAbsolutePathFromAssetPath(normalizedRoot);
            Directory.CreateDirectory(rootAbsolutePath);

            var runFolderName = $"Run_{DateTime.Now:yyyyMMdd_HHmmss}";
            var runAbsolutePath = Path.Combine(rootAbsolutePath, runFolderName);
            Directory.CreateDirectory(runAbsolutePath);
            return runAbsolutePath;
        }

        private static string NormalizeOutputRoot(string outputRoot)
        {
            var normalizedRoot = (outputRoot ?? string.Empty).Trim().Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalizedRoot))
            {
                normalizedRoot = DefaultOutputRoot;
            }

            if (!normalizedRoot.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                normalizedRoot = $"Assets/{normalizedRoot.TrimStart('/')}";
            }

            return FigmaPathUtils.NormalizeAssetFolderPath(normalizedRoot);
        }

        private static void WriteJson(string outputDirectory, string fileName, JToken content)
        {
            var fullPath = Path.Combine(outputDirectory, fileName);
            File.WriteAllText(fullPath, JsonConvert.SerializeObject(content, Formatting.Indented));
        }

        private static void WriteText(string outputDirectory, string fileName, string content)
        {
            var fullPath = Path.Combine(outputDirectory, fileName);
            File.WriteAllText(fullPath, content ?? string.Empty);
        }

        private static List<Node> GetImporterNodes()
        {
            var field = typeof(FigmaImporter).GetField("_nodes", BindingFlags.Static | BindingFlags.NonPublic);
            var value = field != null ? field.GetValue(null) : null;
            return value as List<Node>;
        }

        private static void SetImporterRootObject(GameObject rootObject)
        {
            var field = typeof(FigmaImporter).GetField("_rootObject", BindingFlags.Static | BindingFlags.NonPublic);
            field?.SetValue(null, rootObject);
        }

        private static GameObject TryGetImporterRootObject()
        {
            var field = typeof(FigmaImporter).GetField("_rootObject", BindingFlags.Static | BindingFlags.NonPublic);
            return field != null ? field.GetValue(null) as GameObject : null;
        }

        private static float GetImporterScale(FigmaImporter importer)
        {
            if (importer == null)
            {
                return 1f;
            }

            var field = typeof(FigmaImporter).GetField("_scale", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                return 1f;
            }

            var value = field.GetValue(importer);
            return value is float f ? f : 1f;
        }

        private static bool IsVectorGraphicsDefineEnabled()
        {
#if VECTOR_GRAHICS_IMPORTED
            return true;
#else
            return false;
#endif
        }

        private static GameObject FindGameObjectByNodeId(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return null;
            }

            for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                var roots = scene.GetRootGameObjects();
                for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                {
                    var found = FindNodeInHierarchy(roots[rootIndex].transform, nodeId);
                    if (found != null)
                    {
                        return found.gameObject;
                    }
                }
            }

            return null;
        }

        private static Transform FindNodeInHierarchy(Transform root, string nodeId)
        {
            if (root == null)
            {
                return null;
            }

            var extracted = ExtractNodeIdFromObjectName(root.name);
            if (string.Equals(extracted, nodeId, StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindNodeInHierarchy(root.GetChild(i), nodeId);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static string ExtractNodeIdFromObjectName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return string.Empty;
            }

            var openBracket = objectName.LastIndexOf('[');
            var closeBracket = objectName.LastIndexOf(']');
            if (openBracket < 0 || closeBracket < 0 || closeBracket <= openBracket + 1)
            {
                return string.Empty;
            }

            return objectName.Substring(openBracket + 1, closeBracket - openBracket - 1).Trim();
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            var stack = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                stack.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", stack.ToArray());
        }

        private sealed class ConsoleLogCollector
        {
            private readonly List<ConsoleLogEntry> _entries = new List<ConsoleLogEntry>();
            private bool _started;

            public void Start()
            {
                if (_started)
                {
                    return;
                }

                _started = true;
                Application.logMessageReceived += OnLogMessage;
            }

            public void Stop()
            {
                if (!_started)
                {
                    return;
                }

                _started = false;
                Application.logMessageReceived -= OnLogMessage;
            }

            public string ExportAsText()
            {
                if (_entries.Count == 0)
                {
                    return string.Empty;
                }

                var sb = new StringBuilder();
                for (var i = 0; i < _entries.Count; i++)
                {
                    var entry = _entries[i];
                    sb.Append('[');
                    sb.Append(entry.Time);
                    sb.Append("] ");
                    sb.Append(entry.Type);
                    sb.Append(": ");
                    sb.AppendLine(entry.Message);
                    if (!string.IsNullOrWhiteSpace(entry.StackTrace))
                    {
                        sb.AppendLine(entry.StackTrace);
                    }
                }

                return sb.ToString();
            }

            private void OnLogMessage(string condition, string stackTrace, LogType type)
            {
                _entries.Add(new ConsoleLogEntry
                {
                    Time = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    Type = type.ToString(),
                    Message = condition ?? string.Empty,
                    StackTrace = stackTrace ?? string.Empty
                });
            }
        }

        private sealed class ConsoleLogEntry
        {
            public string Time;
            public string Type;
            public string Message;
            public string StackTrace;
        }

        private sealed class ProcessRunResult
        {
            public int ExitCode;
            public bool TimedOut;
            public string StdOut;
            public string StdErr;
        }

        private sealed class ProcessChainResult
        {
            public string ResultPath;
            public string LogPath;
            public int ExitCode;
            public bool TimedOut;
        }

        private sealed class SampleNodeChoice
        {
            public string Category;
            public Node Node;
            public int Score;
            public string Reason;
        }

        private sealed class ScoreInfo
        {
            public int Score;
            public string Reason;
        }

        private sealed class FontRequestInfo
        {
            public string FontFamily;
            public string FontPostScriptName;
            public int FontWeight;
        }
    }
}
