using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace FigmaImporter.Editor
{
    internal class ImportFallbackResolverWindow : EditorWindow
    {
        private Vector2 _fontScroll;
        private Vector2 _svgScroll;
        private Vector2 _issueScroll;
        private ImportFallbackRegistry _registry;
        private bool _showOnlyCurrentSession;

        [MenuItem(FigmaImporterMenuPaths.Diagnostics.FallbackResolver)]
        private static void OpenFromMenu()
        {
            FigmaDiagnosticsHubWindow.OpenFallbackPage();
        }

        public static void OpenWindow()
        {
            var window = GetWindow<ImportFallbackResolverWindow>("Figma Fallback Resolver");
            window.minSize = new Vector2(640, 420);
            window.Show();
        }

        private void OnEnable()
        {
            _registry = ImportFallbackRegistry.GetOrCreate();
        }

        private void OnGUI()
        {
            _registry ??= ImportFallbackRegistry.GetOrCreate();
            DrawToolbar();
            EditorGUILayout.Space(6);
            DrawSessionSummary();
            EditorGUILayout.Space(8);
            DrawFontSection();
            EditorGUILayout.Space(8);
            DrawSvgSection();
            EditorGUILayout.Space(8);
            DrawIssueSection();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                _registry = ImportFallbackRegistry.GetOrCreate();
            }

            if (GUILayout.Button("Auto Match All", EditorStyles.toolbarButton, GUILayout.Width(110)))
            {
                AutoMatchAll();
            }

            if (GUILayout.Button("Auto Match Unresolved", EditorStyles.toolbarButton, GUILayout.Width(145)))
            {
                AutoMatchUnresolved();
            }

            _showOnlyCurrentSession = GUILayout.Toggle(
                _showOnlyCurrentSession,
                "Only Current Run",
                EditorStyles.toolbarButton,
                GUILayout.Width(125));

            if (GUILayout.Button("Apply FontLinks", EditorStyles.toolbarButton, GUILayout.Width(115)))
            {
                ImportFallbackRegistry.ApplyFontAssignmentsToFontLinks(_registry);
                Repaint();
            }

            if (GUILayout.Button("Fix Scene CJK", EditorStyles.toolbarButton, GUILayout.Width(105)))
            {
                FixSceneCjkTexts();
            }

            if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                ImportFallbackRegistry.SaveRegistry(_registry);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSessionSummary()
        {
            EditorGUILayout.BeginVertical("box");
            var title = string.IsNullOrWhiteSpace(_registry.LastSessionLabel)
                ? "Last Generate Session"
                : _registry.LastSessionLabel;
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"Started: {ValueOrDash(_registry.LastSessionStartedAt)}    Finished: {ValueOrDash(_registry.LastSessionFinishedAt)}");
            EditorGUILayout.LabelField(
                $"This run -> Fonts: {_registry.LastSessionMissingFonts}   SVG: {_registry.LastSessionSvgFallbacks}   Other: {_registry.LastSessionMissingIssues}");
            EditorGUILayout.LabelField(
                $"This run -> SVG->PNG: {_registry.LastSessionSvgToPngFallbacks}");
            EditorGUILayout.LabelField(
                $"Total unresolved -> Fonts: {CountUnresolvedFonts()}   SVG: {CountUnresolvedSvg()}   SVG->PNG: {CountUnresolvedSvgToPng()}");
            if (_registry.SessionActive)
            {
                EditorGUILayout.HelpBox("A generate session is currently running. Missing items are being collected in real time.", MessageType.Info);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawFontSection()
        {
            EditorGUILayout.LabelField("Missing Fonts", EditorStyles.boldLabel);
            var fontEntries = (_registry.MissingFonts ?? Enumerable.Empty<MissingFontEntry>())
                .Where(x => x != null);
            if (_showOnlyCurrentSession)
            {
                fontEntries = fontEntries.Where(x => x.sessionOccurrences > 0);
            }

            var orderedEntries = fontEntries
                .OrderByDescending(x => _showOnlyCurrentSession ? x.sessionOccurrences : x.occurrences)
                .ToList();

            if (orderedEntries.Count == 0)
            {
                var message = _showOnlyCurrentSession
                    ? "No missing fonts recorded in the current generate run."
                    : "No missing font records yet.";
                EditorGUILayout.HelpBox(message, MessageType.Info);
                return;
            }

            _fontScroll = EditorGUILayout.BeginScrollView(_fontScroll, GUILayout.MinHeight(180));
            foreach (var entry in orderedEntries)
            {
                EditorGUILayout.BeginVertical("box");
                var candidateLabel = entry.candidateNames != null && entry.candidateNames.Count > 0
                    ? string.Join(", ", entry.candidateNames)
                    : "(unknown)";
                EditorGUILayout.LabelField(candidateLabel, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    $"Occurrences: {entry.occurrences}   This Run: {entry.sessionOccurrences}   Last Seen: {entry.lastSeenAt}");
                if (!string.IsNullOrWhiteSpace(entry.sampleText))
                {
                    EditorGUILayout.LabelField($"Sample: {Truncate(entry.sampleText, 120)}");
                }
                if (!string.IsNullOrWhiteSpace(entry.lastDetails))
                {
                    EditorGUILayout.LabelField($"Last Details: {entry.lastDetails}");
                }

                var previousAssigned = entry.assignedFont;
                entry.assignedFont = (TMP_FontAsset)EditorGUILayout.ObjectField("Assigned Font", entry.assignedFont, typeof(TMP_FontAsset), false);
                if (entry.assignedFont != previousAssigned)
                {
                    ImportFallbackRegistry.SaveRegistry(_registry);
                }

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Auto Match"))
                {
                    ImportFallbackRegistry.TryAutoAssignFont(entry);
                    ImportFallbackRegistry.SaveRegistry(_registry);
                }

                if (GUILayout.Button("Apply Link"))
                {
                    ImportFallbackRegistry.ApplyFontAssignmentsToFontLinks(_registry);
                }

                if (entry.assignedFont != null && GUILayout.Button("Ping Font", GUILayout.Width(90)))
                {
                    EditorGUIUtility.PingObject(entry.assignedFont);
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSvgSection()
        {
            EditorGUILayout.LabelField("SVG Fallbacks", EditorStyles.boldLabel);
            var svgEntries = (_registry.SvgFallbacks ?? Enumerable.Empty<SvgFallbackEntry>())
                .Where(x => x != null);
            if (_showOnlyCurrentSession)
            {
                svgEntries = svgEntries.Where(x => x.sessionOccurrences > 0);
            }

            var orderedEntries = svgEntries
                .OrderByDescending(x => _showOnlyCurrentSession ? x.sessionOccurrences : x.occurrences)
                .ToList();

            if (orderedEntries.Count == 0)
            {
                var message = _showOnlyCurrentSession
                    ? "No SVG fallbacks recorded in the current generate run."
                    : "No SVG fallback records yet.";
                EditorGUILayout.HelpBox(message, MessageType.Info);
                return;
            }

            var svgToPngEntries = orderedEntries
                .Where(ImportFallbackRegistry.IsSvgToPngFallback)
                .ToList();
            var otherSvgEntries = orderedEntries
                .Where(x => !ImportFallbackRegistry.IsSvgToPngFallback(x))
                .ToList();

            _svgScroll = EditorGUILayout.BeginScrollView(_svgScroll, GUILayout.MinHeight(180));
            if (svgToPngEntries.Count > 0)
            {
                EditorGUILayout.LabelField($"SVG -> PNG (Raster payload): {svgToPngEntries.Count}", EditorStyles.boldLabel);
                foreach (var entry in svgToPngEntries)
                {
                    DrawSvgEntry(entry);
                }
            }

            if (otherSvgEntries.Count > 0)
            {
                if (svgToPngEntries.Count > 0)
                {
                    EditorGUILayout.Space(4);
                }
                EditorGUILayout.LabelField($"Other SVG Fallbacks: {otherSvgEntries.Count}", EditorStyles.boldLabel);
                foreach (var entry in otherSvgEntries)
                {
                    DrawSvgEntry(entry);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSvgEntry(SvgFallbackEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"{entry.nodeName} [{entry.nodeId}]", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"Occurrences: {entry.occurrences}   This Run: {entry.sessionOccurrences}   Last Seen: {entry.lastSeenAt}");
            if (!string.IsNullOrWhiteSpace(entry.lastReason))
            {
                EditorGUILayout.LabelField($"Reason: {entry.lastReason}");
            }

            if (!string.IsNullOrWhiteSpace(entry.generatedSpritePath))
            {
                EditorGUILayout.LabelField($"Generated: {entry.generatedSpritePath}");
            }

            var previousAssigned = entry.assignedSprite;
            entry.assignedSprite = (Sprite)EditorGUILayout.ObjectField("Assigned Sprite", entry.assignedSprite, typeof(Sprite), false);
            if (entry.assignedSprite != previousAssigned)
            {
                ImportFallbackRegistry.SaveRegistry(_registry);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Auto Match"))
            {
                ImportFallbackRegistry.TryAutoAssignSvgSprite(entry);
                ImportFallbackRegistry.SaveRegistry(_registry);
            }

            if (GUILayout.Button("Use Generated"))
            {
                if (!string.IsNullOrWhiteSpace(entry.generatedSpritePath))
                {
                    var generated = AssetDatabase.LoadAssetAtPath<Sprite>(entry.generatedSpritePath);
                    if (generated != null)
                    {
                        entry.assignedSprite = generated;
                        ImportFallbackRegistry.SaveRegistry(_registry);
                    }
                }
            }

            if (entry.assignedSprite != null && GUILayout.Button("Ping Sprite", GUILayout.Width(90)))
            {
                EditorGUIUtility.PingObject(entry.assignedSprite);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawIssueSection()
        {
            EditorGUILayout.LabelField("Other Missing Issues", EditorStyles.boldLabel);
            var issueEntries = (_registry.MissingIssues ?? Enumerable.Empty<MissingIssueEntry>())
                .Where(x => x != null);
            if (_showOnlyCurrentSession)
            {
                issueEntries = issueEntries.Where(x => x.sessionOccurrences > 0);
            }

            var orderedEntries = issueEntries
                .OrderByDescending(x => _showOnlyCurrentSession ? x.sessionOccurrences : x.occurrences)
                .ToList();

            if (orderedEntries.Count == 0)
            {
                var message = _showOnlyCurrentSession
                    ? "No other missing issues recorded in the current generate run."
                    : "No additional missing issues recorded.";
                EditorGUILayout.HelpBox(message, MessageType.Info);
                return;
            }

            _issueScroll = EditorGUILayout.BeginScrollView(_issueScroll, GUILayout.MinHeight(120));
            foreach (var entry in orderedEntries)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField($"{entry.category} / {entry.key}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    $"Occurrences: {entry.occurrences}   This Run: {entry.sessionOccurrences}   Last Seen: {entry.lastSeenAt}");
                if (!string.IsNullOrWhiteSpace(entry.nodeId))
                {
                    EditorGUILayout.LabelField($"Node: {entry.nodeName} [{entry.nodeId}]");
                }
                if (!string.IsNullOrWhiteSpace(entry.lastDetails))
                {
                    EditorGUILayout.LabelField($"Details: {Truncate(entry.lastDetails, 200)}");
                }
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        private void AutoMatchAll()
        {
            foreach (var fontEntry in _registry.MissingFonts)
            {
                ImportFallbackRegistry.TryAutoAssignFont(fontEntry);
            }

            foreach (var svgEntry in _registry.SvgFallbacks)
            {
                ImportFallbackRegistry.TryAutoAssignSvgSprite(svgEntry);
            }

            ImportFallbackRegistry.ApplyFontAssignmentsToFontLinks(_registry);
            ImportFallbackRegistry.SaveRegistry(_registry);
            Repaint();
        }

        private void AutoMatchUnresolved()
        {
            foreach (var fontEntry in _registry.MissingFonts)
            {
                if (fontEntry == null || fontEntry.assignedFont != null)
                {
                    continue;
                }

                if (_showOnlyCurrentSession && fontEntry.sessionOccurrences <= 0)
                {
                    continue;
                }

                ImportFallbackRegistry.TryAutoAssignFont(fontEntry);
            }

            foreach (var svgEntry in _registry.SvgFallbacks)
            {
                if (svgEntry == null || svgEntry.assignedSprite != null)
                {
                    continue;
                }

                if (_showOnlyCurrentSession && svgEntry.sessionOccurrences <= 0)
                {
                    continue;
                }

                ImportFallbackRegistry.TryAutoAssignSvgSprite(svgEntry);
            }

            ImportFallbackRegistry.ApplyFontAssignmentsToFontLinks(_registry);
            ImportFallbackRegistry.SaveRegistry(_registry);
            Repaint();
        }

        private void FixSceneCjkTexts()
        {
            var fontLinks = FontAssetResolver.GetOrCreateFontLinksAsset();
            var cjkFont = FontAssetResolver.ResolvePreferredCjkFont(fontLinks, out var details);
            if (cjkFont == null)
            {
                Debug.LogWarning($"[FigmaImporter] Could not resolve CJK font for scene fix. {details}");
                return;
            }

            var defaultFont = TMP_Settings.defaultFontAsset;
            var allTexts = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();
            var updated = 0;
            foreach (var tmp in allTexts)
            {
                if (tmp == null)
                {
                    continue;
                }

                if (EditorUtility.IsPersistent(tmp))
                {
                    continue;
                }

                var scene = tmp.gameObject.scene;
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                if (!FontAssetResolver.ContainsCjkText(tmp.text))
                {
                    continue;
                }

                if (tmp.font != null && tmp.font != defaultFont)
                {
                    FontAssetResolver.EnsureFallbackCoverage(tmp.font, fontLinks, tmp.text, out var coverageInfo);
                    if (!string.IsNullOrWhiteSpace(coverageInfo))
                    {
                        Undo.RecordObject(tmp, "Figma Importer Refresh CJK Fallback");
                        tmp.SetAllDirty();
                        EditorUtility.SetDirty(tmp);
                        updated++;
                    }
                    continue;
                }

                Undo.RecordObject(tmp, "Figma Importer Fix CJK Font");
                tmp.font = cjkFont;
                tmp.SetAllDirty();
                EditorUtility.SetDirty(tmp);
                updated++;
            }

            if (updated > 0)
            {
                Debug.Log($"[FigmaImporter] Scene CJK fix applied to {updated} text objects using '{cjkFont.name}'. {details}");
            }
            else
            {
                Debug.Log($"[FigmaImporter] Scene CJK fix found no objects to update. {details}");
            }
        }

        private static string Truncate(string value, int maxLen)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLen)
            {
                return value;
            }

            return value.Substring(0, maxLen) + "...";
        }

        private int CountUnresolvedFonts()
        {
            if (_registry?.MissingFonts == null)
            {
                return 0;
            }

            return _registry.MissingFonts.Count(x => x != null && x.assignedFont == null);
        }

        private int CountUnresolvedSvg()
        {
            if (_registry?.SvgFallbacks == null)
            {
                return 0;
            }

            return _registry.SvgFallbacks.Count(x => x != null && x.assignedSprite == null);
        }

        private int CountUnresolvedSvgToPng()
        {
            if (_registry?.SvgFallbacks == null)
            {
                return 0;
            }

            return _registry.SvgFallbacks.Count(x =>
                x != null &&
                x.assignedSprite == null &&
                ImportFallbackRegistry.IsSvgToPngFallback(x));
        }

        private static string ValueOrDash(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }
    }
}
