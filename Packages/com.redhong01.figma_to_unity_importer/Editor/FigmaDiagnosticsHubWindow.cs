using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FigmaImporter.Editor
{
    internal sealed class FigmaDiagnosticsHubWindow : EditorWindow
    {
        private const string MenuPath = FigmaImporterMenuPaths.Diagnostics.DiagnosticsHub;
        private const string DefaultIssueOutputRoot = "Assets/FigmaImporter/_Local/IssueHandoff";
        private const double AuthAutoPopupCooldownSeconds = 2.0;

        private enum DiagnosticsPage
        {
            AuthAndApiErrors = 0,
            FallbackResolver = 1,
            ImporterErrorHandoff = 2,
            AutoLayout = 3
        }

        private sealed class AuthIssueSnapshot
        {
            public string Timestamp;
            public long StatusCode;
            public string Stage;
            public string RequestUrl;
            public string FailureReason;
        }

        private static readonly string[] PageLabels =
        {
            "Auth & API Errors",
            "Fallback Resolver",
            "Importer Errors",
            "AutoLayout"
        };

        private static AuthIssueSnapshot _lastAuthIssue;
        private static string _lastAuthIssueFingerprint = string.Empty;
        private static double _lastAuthPopupAt;

        private DiagnosticsPage _activePage = DiagnosticsPage.AuthAndApiErrors;
        private Vector2 _scroll;
        private bool _showOnlyCurrentSession;
        private string _issueOutputRoot = DefaultIssueOutputRoot;
        private string _status = "Ready";
        private string _lastIssuePackFolder = string.Empty;

        [MenuItem(MenuPath)]
        internal static void OpenWindow()
        {
            OpenWithPage(DiagnosticsPage.AuthAndApiErrors);
        }

        internal static void OpenAuthAndApiPage()
        {
            OpenWithPage(DiagnosticsPage.AuthAndApiErrors);
        }

        internal static void OpenFallbackPage()
        {
            OpenWithPage(DiagnosticsPage.FallbackResolver);
        }

        internal static void OpenIssueHandoffPage()
        {
            OpenWithPage(DiagnosticsPage.ImporterErrorHandoff);
        }

        internal static void OpenAutoLayoutPage()
        {
            OpenWithPage(DiagnosticsPage.AutoLayout);
        }

        internal static void ReportAuthApiFailure(long statusCode, string stage, string requestUrl, string failureReason)
        {
            if (statusCode != 401 && statusCode != 403)
            {
                return;
            }

            var snapshot = new AuthIssueSnapshot
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                StatusCode = statusCode,
                Stage = string.IsNullOrWhiteSpace(stage) ? "request" : stage,
                RequestUrl = requestUrl ?? string.Empty,
                FailureReason = failureReason ?? string.Empty
            };

            var fingerprint = $"{snapshot.StatusCode}|{snapshot.Stage}|{snapshot.RequestUrl}|{snapshot.FailureReason}";
            var now = EditorApplication.timeSinceStartup;
            if (string.Equals(fingerprint, _lastAuthIssueFingerprint, StringComparison.Ordinal) &&
                now - _lastAuthPopupAt < AuthAutoPopupCooldownSeconds)
            {
                return;
            }

            _lastAuthIssue = snapshot;
            _lastAuthIssueFingerprint = fingerprint;
            _lastAuthPopupAt = now;
            OpenWithPage(DiagnosticsPage.AuthAndApiErrors, focus: true);
        }

        private static void OpenWithPage(DiagnosticsPage page, bool focus = true)
        {
            var window = GetWindow<FigmaDiagnosticsHubWindow>("Figma Diagnostics");
            window.minSize = new Vector2(700f, 500f);
            window._activePage = page;
            if (focus)
            {
                window.Focus();
            }

            window.Show();
        }

        private void OnEnable()
        {
            _issueOutputRoot = NormalizeIssueOutputRoot(_issueOutputRoot);
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("Figma Diagnostics Hub", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Unified diagnostics pages for authentication/API issues, fallback fixing, importer issue handoff, and auto-layout diagnostics.",
                MessageType.Info);

            var selected = GUILayout.Toolbar((int)_activePage, PageLabels);
            if (selected != (int)_activePage)
            {
                _activePage = (DiagnosticsPage)selected;
            }

            GUILayout.Space(8f);
            switch (_activePage)
            {
                case DiagnosticsPage.AuthAndApiErrors:
                    DrawAuthAndApiPage();
                    break;
                case DiagnosticsPage.FallbackResolver:
                    DrawFallbackPage();
                    break;
                case DiagnosticsPage.ImporterErrorHandoff:
                    DrawIssueHandoffPage();
                    break;
                case DiagnosticsPage.AutoLayout:
                    DrawAutoLayoutPage();
                    break;
            }

            GUILayout.Space(10f);
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(string.IsNullOrWhiteSpace(_status) ? "Ready" : _status, MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        private void DrawAuthAndApiPage()
        {
            EditorGUILayout.LabelField("Auth / API Troubleshooting", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "When OAuth or access permissions change across devices, Figma requests can return HTTP 401/403. Use the quick actions below to recover quickly.",
                MessageType.Warning);

            if (_lastAuthIssue != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"Last Auth Error Time: {_lastAuthIssue.Timestamp}");
                EditorGUILayout.LabelField($"Status Code: {_lastAuthIssue.StatusCode}");
                EditorGUILayout.LabelField($"Stage: {_lastAuthIssue.Stage}");
                if (!string.IsNullOrWhiteSpace(_lastAuthIssue.RequestUrl))
                {
                    EditorGUILayout.LabelField("Request URL", EditorStyles.miniBoldLabel);
                    EditorGUILayout.SelectableLabel(_lastAuthIssue.RequestUrl, EditorStyles.textArea, GUILayout.MinHeight(34f));
                }

                if (!string.IsNullOrWhiteSpace(_lastAuthIssue.FailureReason))
                {
                    EditorGUILayout.LabelField("Failure Reason", EditorStyles.miniBoldLabel);
                    EditorGUILayout.SelectableLabel(_lastAuthIssue.FailureReason, EditorStyles.textArea, GUILayout.MinHeight(48f));
                }
                EditorGUILayout.EndVertical();
            }
            else
            {
                EditorGUILayout.HelpBox("No recent HTTP 401/403 auth issue captured in this Unity session.", MessageType.None);
            }

            EditorGUILayout.LabelField("Quick Fix Steps", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "1) Open Importer and run OpenOauthUrl + GetToken on this device.\n" +
                "2) Verify this Figma account can access the file/node.\n" +
                "3) If you recently authorized from another device, refresh token again here.\n" +
                "4) Retry Fetch Figma Node Data.",
                MessageType.None);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open Importer", GUILayout.Height(24f)))
            {
                var importerWindow = GetWindow<FigmaImporter>("Figma Importer");
                importerWindow.Show();
                importerWindow.Focus();
                _status = "Importer window opened.";
            }

            if (GUILayout.Button("Open OAuth URL Now", GUILayout.Height(24f)))
            {
                var importerWindow = GetWindow<FigmaImporter>("Figma Importer");
                importerWindow.Show();
                importerWindow.OpenOauthUrl();
                _status = "Opened Figma OAuth page in browser.";
            }

            if (GUILayout.Button("Copy Last Error", GUILayout.Height(24f)))
            {
                if (_lastAuthIssue == null)
                {
                    _status = "No auth issue captured yet.";
                }
                else
                {
                    var content =
                        $"[{_lastAuthIssue.Timestamp}] HTTP {_lastAuthIssue.StatusCode} at {_lastAuthIssue.Stage}\n" +
                        $"{_lastAuthIssue.FailureReason}\n" +
                        $"{_lastAuthIssue.RequestUrl}";
                    EditorGUIUtility.systemCopyBuffer = content;
                    _status = "Last auth error copied to clipboard.";
                }
            }
            EditorGUILayout.EndHorizontal();

            var authRelatedEntries = FigmaImporterIssueTracker.GetRecentEntries(160)
                .Where(x => x != null && x.IsErrorLike && IsAuthRelatedMessage(x.Message))
                .Reverse()
                .Take(8)
                .ToList();

            if (authRelatedEntries.Count > 0)
            {
                GUILayout.Space(6f);
                EditorGUILayout.LabelField("Recent Related Console Entries", EditorStyles.boldLabel);
                foreach (var entry in authRelatedEntries)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField($"[{entry.Time}] {entry.Type}", EditorStyles.miniBoldLabel);
                    EditorGUILayout.SelectableLabel(entry.Message ?? string.Empty, EditorStyles.wordWrappedLabel, GUILayout.MinHeight(24f));
                    EditorGUILayout.EndVertical();
                }
            }
        }

        private void DrawFallbackPage()
        {
            var registry = ImportFallbackRegistry.GetOrCreate();
            if (registry == null)
            {
                EditorGUILayout.HelpBox("Fallback registry is unavailable.", MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("Fallback Resolver", EditorStyles.boldLabel);

            _showOnlyCurrentSession = EditorGUILayout.ToggleLeft("Only Current Run", _showOnlyCurrentSession);

            var unresolvedFonts = CountUnresolvedFonts(registry, _showOnlyCurrentSession);
            var unresolvedSvg = CountUnresolvedSvg(registry, _showOnlyCurrentSession);
            var unresolvedSvgToPng = CountUnresolvedSvgToPng(registry, _showOnlyCurrentSession);
            var unresolvedIssues = CountUnresolvedIssues(registry, _showOnlyCurrentSession);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                $"Last Session: {ValueOrDash(registry.LastSessionLabel)}   Started: {ValueOrDash(registry.LastSessionStartedAt)}   Finished: {ValueOrDash(registry.LastSessionFinishedAt)}");
            EditorGUILayout.LabelField(
                $"This run -> Fonts: {registry.LastSessionMissingFonts}   SVG: {registry.LastSessionSvgFallbacks}   SVG->PNG: {registry.LastSessionSvgToPngFallbacks}   Other: {registry.LastSessionMissingIssues}");
            EditorGUILayout.LabelField(
                $"Unresolved -> Fonts: {unresolvedFonts}   SVG: {unresolvedSvg}   SVG->PNG: {unresolvedSvgToPng}   Other: {unresolvedIssues}");
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

            var issues = (registry.MissingIssues ?? new List<MissingIssueEntry>())
                .Where(x => x != null && (!_showOnlyCurrentSession || x.sessionOccurrences > 0))
                .OrderByDescending(x => _showOnlyCurrentSession ? x.sessionOccurrences : x.occurrences)
                .Take(12)
                .ToList();
            if (issues.Count > 0)
            {
                GUILayout.Space(6f);
                EditorGUILayout.LabelField("Top Missing Issues", EditorStyles.boldLabel);
                foreach (var issue in issues)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField($"{issue.category} / {issue.key}", EditorStyles.miniBoldLabel);
                    EditorGUILayout.LabelField(
                        $"Occurrences: {issue.occurrences}   This Run: {issue.sessionOccurrences}   Last Seen: {issue.lastSeenAt}");
                    if (!string.IsNullOrWhiteSpace(issue.lastDetails))
                    {
                        EditorGUILayout.SelectableLabel(issue.lastDetails, EditorStyles.wordWrappedLabel, GUILayout.MinHeight(22f));
                    }
                    EditorGUILayout.EndVertical();
                }
            }
        }

        private void DrawIssueHandoffPage()
        {
            EditorGUILayout.LabelField("Importer Error Handoff", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Collect recent importer errors into a pack and send it to an installed coding agent for quick triage/fix.",
                MessageType.None);

            var captured = FigmaImporterIssueTracker.EntryCount;
            var errorLike = FigmaImporterIssueTracker.ErrorLikeCount;

            EditorGUILayout.LabelField("Captured Entries", captured.ToString(CultureInfo.InvariantCulture));
            EditorGUILayout.LabelField("Error/Exception Entries", errorLike.ToString(CultureInfo.InvariantCulture));

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
                    _status = "Opened issue handoff and started agent flow.";
                }
            }

            if (GUILayout.Button("Open Full Handoff Window", GUILayout.Height(24f)))
            {
                FigmaImporterIssueHandoffWindow.OpenWindow();
                _status = "Opened full issue handoff window.";
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
                if (GUILayout.Button("Ping Last Pack", GUILayout.Height(22f)))
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

        private void DrawAutoLayoutPage()
        {
            EditorGUILayout.LabelField("AutoLayout Diagnostics", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Generate a full auto-layout diagnostics pack (Figma payload, Unity snapshot, fonts/fallback report, markdown report).",
                MessageType.None);

            if (GUILayout.Button("Open AutoLayout Diagnostics Window", GUILayout.Height(28f)))
            {
                AutoLayoutDiagnosticsWindow.OpenWindow();
                _status = "Opened AutoLayout Diagnostics window.";
            }
        }

        private static bool IsAuthRelatedMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            return message.IndexOf("HTTP 401", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("HTTP 403", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("Forbidden", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("unauthorized", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("oauth", StringComparison.OrdinalIgnoreCase) >= 0;
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

        private static string NormalizeIssueOutputRoot(string outputRoot)
        {
            return FigmaPathUtils.NormalizeAssetFolderPath(
                string.IsNullOrWhiteSpace(outputRoot) ? DefaultIssueOutputRoot : outputRoot);
        }
    }
}
