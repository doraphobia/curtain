using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace FigmaImporter.Editor
{
    internal sealed class FigmaImporterIssueHandoffWindow : EditorWindow
    {
        private const string MenuPath = FigmaImporterMenuPaths.Diagnostics.ImporterErrorHandoff;
        private const string DefaultOutputRoot = "Assets/FigmaImporter/_Local/IssueHandoff";
        private const int AgentTimeoutSeconds = 600;
        private const string AgentPromptFileName = "agent_issue_handoff_prompt.md";
        private const string AgentCodexOutputFileName = "agent_codex_last_message.md";
        private const string AgentCodexLogFileName = "agent_codex_exec.log";
        private const double ToolDetectionRefreshSeconds = 2.0;

        private string _outputRoot = DefaultOutputRoot;
        private bool _clearCapturedAfterPack;
        private bool _isRunning;
        private string _status = "Ready";
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
        private static void OpenFromMenu()
        {
            FigmaDiagnosticsHubWindow.OpenIssueHandoffPage();
        }

        internal static void OpenWindow()
        {
            var window = GetWindow<FigmaImporterIssueHandoffWindow>("Importer Error Handoff");
            window.minSize = new Vector2(660f, 460f);
            window.Show();
        }

        [MenuItem(FigmaImporterMenuPaths.Diagnostics.AnalyzeWithAgent)]
        internal static void OpenAndRunFromMenu()
        {
            OpenAndRun();
        }

        internal static void OpenAndRun()
        {
            var window = GetWindow<FigmaImporterIssueHandoffWindow>("Importer Error Handoff");
            window.minSize = new Vector2(660f, 460f);
            window.Show();
            _ = window.RunIssueHandoffAsync();
        }

        private void OnEnable()
        {
            _outputRoot = NormalizeOutputRoot(_outputRoot);
            RefreshToolDetectionIfNeeded(true);
        }

        private void OnGUI()
        {
            RefreshToolDetectionIfNeeded();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("One-Click Importer Error Handoff", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Collects recent FigmaImporter errors/warnings from Unity console callbacks, generates a unified issue pack, and sends it to Codex/Cursor.",
                MessageType.Info);

            var captured = FigmaImporterIssueTracker.EntryCount;
            var errorLike = FigmaImporterIssueTracker.ErrorLikeCount;
            EditorGUILayout.LabelField("Captured Entries", captured.ToString(CultureInfo.InvariantCulture));
            EditorGUILayout.LabelField("Error/Exception Entries", errorLike.ToString(CultureInfo.InvariantCulture));
            EditorGUILayout.LabelField("Detected Tools", BuildToolDetectionSummary());

            EditorGUI.BeginDisabledGroup(_isRunning);
            _outputRoot = EditorGUILayout.TextField("Output Folder", _outputRoot);
            _clearCapturedAfterPack = EditorGUILayout.ToggleLeft("Clear Captured Entries After Pack", _clearCapturedAfterPack);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Build Issue Pack", GUILayout.Height(26f)))
            {
                BuildIssuePackOnly();
            }

            using (new EditorGUI.DisabledScope(captured == 0))
            {
                if (GUILayout.Button("Analyze + Fix With Installed Agent", GUILayout.Height(26f)))
                {
                    _ = RunIssueHandoffAsync();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(captured == 0))
            {
                if (GUILayout.Button("Clear Captured Entries", GUILayout.Height(22f)))
                {
                    FigmaImporterIssueTracker.Clear();
                    _status = "Captured entries cleared.";
                }
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_lastOutputFolder) || !Directory.Exists(_lastOutputFolder)))
            {
                if (GUILayout.Button("Ping Last Pack", GUILayout.Height(22f)))
                {
                    EditorUtility.RevealInFinder(_lastOutputFolder);
                }
            }
            EditorGUILayout.EndHorizontal();
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

            GUILayout.Space(8f);
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(_status, MessageType.None);
            if (!string.IsNullOrWhiteSpace(_lastOutputFolder))
            {
                EditorGUILayout.LabelField("Last Pack", _lastOutputFolder);
            }

            EditorGUILayout.EndScrollView();
        }

        private void BuildIssuePackOnly()
        {
            try
            {
                _outputRoot = NormalizeOutputRoot(_outputRoot);
                _lastOutputFolder = FigmaImporterIssueTracker.CreateIssuePack(_outputRoot);
                if (_clearCapturedAfterPack)
                {
                    FigmaImporterIssueTracker.Clear();
                }

                _status = $"Issue pack generated: {_lastOutputFolder}";
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _status = $"Build issue pack failed: {e.Message}";
            }
            finally
            {
                Repaint();
            }
        }

        private async Task RunIssueHandoffAsync()
        {
            if (_isRunning)
            {
                return;
            }

            var flowChainId = FigmaImporterEventFlow.Start("ImporterErrorHandoff", "Analyze + Fix With Installed Agent");
            var flowResult = "Completed";
            var flowDetails = string.Empty;
            if (!FigmaImporterIssueTracker.HasEntries)
            {
                _status = "No importer errors/warnings captured yet.";
                flowResult = "Skipped";
                flowDetails = "No issue tracker entries";
                FigmaImporterEventFlow.End("ImporterErrorHandoff", flowChainId, flowResult, flowDetails);
                Repaint();
                return;
            }

            RefreshToolDetectionIfNeeded(true);
            var hasCodex = FigmaAgentToolLocator.IsExecutableAvailable(_codexExecutable);
            var hasCursorAgent = FigmaAgentToolLocator.IsExecutableAvailable(_cursorAgentExecutable);
            var hasCursor = FigmaAgentToolLocator.IsExecutableAvailable(_cursorExecutable);
            if (!hasCodex && !hasCursorAgent && !hasCursor)
            {
                _status = "No supported agent executable found. Install Codex CLI or Cursor CLI.";
                flowResult = "Skipped";
                flowDetails = "No supported agent executable found";
                FigmaImporterEventFlow.End("ImporterErrorHandoff", flowChainId, flowResult, flowDetails);
                Repaint();
                return;
            }

            _isRunning = true;
            _status = "Building issue pack...";
            Repaint();

            try
            {
                _outputRoot = NormalizeOutputRoot(_outputRoot);
                _lastOutputFolder = FigmaImporterIssueTracker.CreateIssuePack(_outputRoot);
                FigmaImporterEventFlow.Step("ImporterErrorHandoff", flowChainId, "IssuePackCreated", _lastOutputFolder);
                if (_clearCapturedAfterPack)
                {
                    FigmaImporterIssueTracker.Clear();
                }

                var promptPath = WriteAgentPrompt(_lastOutputFolder);
                _lastAgentPromptFile = promptPath;
                FigmaImporterEventFlow.Step("ImporterErrorHandoff", flowChainId, "PromptPrepared", promptPath);
                AssetDatabase.Refresh();

                if (hasCodex)
                {
                    _status = "Running Codex agent chain...";
                    Repaint();
                    var processResult = await RunCodexAgentAsync(promptPath, _lastOutputFolder);
                    _lastAgentResultFile = processResult.ResultPath;
                    _lastAgentLogFile = processResult.LogPath;
                    if (processResult.ExitCode == 0)
                    {
                        _status = $"Codex finished. Result saved to: {processResult.ResultPath}";
                        flowDetails = $"agent=codex; result={processResult.ResultPath}";
                    }
                    else if (processResult.TimedOut)
                    {
                        _status = $"Codex timed out after {AgentTimeoutSeconds}s. See log: {processResult.LogPath}";
                        flowResult = "TimedOut";
                        flowDetails = $"agent=codex; log={processResult.LogPath}";
                    }
                    else
                    {
                        _status = $"Codex exited with code {processResult.ExitCode}. See log: {processResult.LogPath}";
                        flowResult = "Failed";
                        flowDetails = $"agent=codex; exitCode={processResult.ExitCode}; log={processResult.LogPath}";
                    }

                    return;
                }

                if (hasCursorAgent)
                {
                    _status = "Running Cursor agent chain...";
                    Repaint();
                    var processResult = await RunCursorAgentAsync(promptPath, _lastOutputFolder);
                    _lastAgentResultFile = processResult.ResultPath;
                    _lastAgentLogFile = processResult.LogPath;
                    if (processResult.ExitCode == 0)
                    {
                        _status = $"Cursor agent finished. Result saved to: {processResult.ResultPath}";
                        flowDetails = $"agent=cursor-agent; result={processResult.ResultPath}";
                    }
                    else if (processResult.TimedOut)
                    {
                        _status = $"Cursor agent timed out after {AgentTimeoutSeconds}s. See log: {processResult.LogPath}";
                        flowResult = "TimedOut";
                        flowDetails = $"agent=cursor-agent; log={processResult.LogPath}";
                    }
                    else
                    {
                        _status = $"Cursor agent exited with code {processResult.ExitCode}. See log: {processResult.LogPath}";
                        flowResult = "Failed";
                        flowDetails = $"agent=cursor-agent; exitCode={processResult.ExitCode}; log={processResult.LogPath}";
                    }

                    return;
                }

                var args = $"{QuoteArg(Directory.GetCurrentDirectory())} {QuoteArg(promptPath)}";
                TryLaunchDetachedProcess(_cursorExecutable, args, Directory.GetCurrentDirectory(), out var launchError);
                if (string.IsNullOrWhiteSpace(launchError))
                {
                    _status = "Opened Cursor with workspace and issue prompt. Continue in Cursor Agent/Chat to apply fixes.";
                    flowResult = "Delegated";
                    flowDetails = $"agent=cursor-cli; prompt={promptPath}";
                }
                else
                {
                    _status = $"Cursor launch failed: {launchError}";
                    flowResult = "Failed";
                    flowDetails = launchError;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _status = $"Issue handoff failed: {e.Message}";
                flowResult = "Failed";
                flowDetails = e.Message;
            }
            finally
            {
                _isRunning = false;
                AssetDatabase.Refresh();
                FigmaImporterEventFlow.End("ImporterErrorHandoff", flowChainId, flowResult, flowDetails);
                Repaint();
            }
        }

        private static string WriteAgentPrompt(string outputFolder)
        {
            var promptPath = Path.Combine(outputFolder, AgentPromptFileName);
            var projectRoot = Directory.GetCurrentDirectory();

            var sb = new StringBuilder();
            sb.AppendLine("# Figma Importer Error Handoff");
            sb.AppendLine();
            sb.AppendLine($"Generated: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"ProjectRoot: {projectRoot}");
            sb.AppendLine($"IssuePack: {outputFolder}");
            sb.AppendLine();
            sb.AppendLine("## Read These Files First");
            sb.AppendLine("- importer_issue_report.md");
            sb.AppendLine("- importer_issue_console.log");
            sb.AppendLine("- importer_issue_entries.json");
            sb.AppendLine("- importer_settings_snapshot.json");
            sb.AppendLine("- fallback_registry_snapshot.json");
            sb.AppendLine();
            sb.AppendLine("## Tasks");
            sb.AppendLine("1. Determine root cause(s) for current Figma importer failures.");
            sb.AppendLine("2. Apply minimal, targeted fixes in this repository.");
            sb.AppendLine("3. Keep security safe: never print or commit full tokens/credentials.");
            sb.AppendLine("4. Return changed files, verification steps, and residual risks.");

            File.WriteAllText(promptPath, sb.ToString());
            return promptPath;
        }

        private async Task<ProcessChainResult> RunCodexAgentAsync(string promptPath, string outputFolder)
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var resultPath = Path.Combine(outputFolder, AgentCodexOutputFileName);
            var logPath = Path.Combine(outputFolder, AgentCodexLogFileName);
            var launchPrompt =
                $"Read and execute instructions in '{promptPath}'. Analyze issue pack in '{outputFolder}', then apply fixes in this repository and summarize.";
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
            return new ProcessChainResult
            {
                ResultPath = resultPath,
                LogPath = logPath,
                ExitCode = runResult.ExitCode,
                TimedOut = runResult.TimedOut
            };
        }

        private async Task<ProcessChainResult> RunCursorAgentAsync(string promptPath, string outputFolder)
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var resultPath = Path.Combine(outputFolder, "agent_cursor_last_message.md");
            var logPath = Path.Combine(outputFolder, "agent_cursor_exec.log");
            var launchPrompt =
                $"Read and execute instructions in '{promptPath}'. Analyze issue pack in '{outputFolder}', apply fixes in the repository, and write final summary to '{resultPath}'.";
            var args = QuoteArg(launchPrompt);

            var runResult = await RunProcessAsync(_cursorAgentExecutable, args, projectRoot, AgentTimeoutSeconds);
            WriteProcessLog(logPath, _cursorAgentExecutable, args, runResult);
            return new ProcessChainResult
            {
                ResultPath = resultPath,
                LogPath = logPath,
                ExitCode = runResult.ExitCode,
                TimedOut = runResult.TimedOut
            };
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
                    }
                }

                await Task.WhenAll(stdoutTask, stderrTask);
                return new ProcessRunResult
                {
                    ExitCode = timedOut ? -1 : process.ExitCode,
                    TimedOut = timedOut,
                    StdOut = stdoutTask.Result ?? string.Empty,
                    StdErr = stderrTask.Result ?? string.Empty
                };
            }
        }

        private static void WriteProcessLog(string logPath, string executablePath, string arguments, ProcessRunResult result)
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

        private static string QuoteArg(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "\"\"";
            }

            return "\"" + value.Replace("\"", "\\\"") + "\"";
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
    }
}
