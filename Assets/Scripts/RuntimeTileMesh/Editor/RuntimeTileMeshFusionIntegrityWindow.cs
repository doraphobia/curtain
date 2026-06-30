#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh.Editor
{
    public sealed class RuntimeTileMeshFusionIntegrityWindow : EditorWindow
    {
        private RuntimeTileMeshFusionIntegrityMonitor monitor;
        private Vector2 scroll;
        private int selectedReportIndex = -1;
        private bool showOnlyIssues = true;

        [MenuItem("Tools/Duo Curtain/Runtime Tile Mesh/Fusion Integrity Monitor")]
        public static void Open()
        {
            RuntimeTileMeshFusionIntegrityWindow window = GetWindow<RuntimeTileMeshFusionIntegrityWindow>(
                false,
                "Fusion Integrity",
                true);
            window.minSize = new Vector2(520f, 360f);
            window.Show();
        }

        void OnEnable()
        {
            ResolveMonitor();
            EditorApplication.update += RepaintIfPlaying;
        }

        void OnDisable()
        {
            EditorApplication.update -= RepaintIfPlaying;
        }

        void OnGUI()
        {
            DrawToolbar();

            if (monitor == null)
            {
                EditorGUILayout.HelpBox(
                    "Add RuntimeTileMeshFusionIntegrityMonitor to the Fusion Sandbox object in the scene, " +
                    "or press Find Monitor.",
                    MessageType.Info);
                return;
            }

            DrawMonitorSettings();
            EditorGUILayout.Space(8f);
            DrawReportList();
            EditorGUILayout.Space(8f);
            DrawSelectedReportDetails();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Find Monitor", GUILayout.Width(110f)))
                ResolveMonitor();

            EditorGUI.BeginDisabledGroup(!Application.isPlaying || monitor == null);
            if (GUILayout.Button("Run Audit", GUILayout.Width(90f)))
                monitor.RunManualAudit("Editor Manual Audit");

            if (GUILayout.Button("Clear", GUILayout.Width(70f)))
                monitor.ClearReports();
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("Export Log", GUILayout.Width(90f)))
                ExportReports();

            GUILayout.FlexibleSpace();
            showOnlyIssues = EditorGUILayout.ToggleLeft("Issues Only", showOnlyIssues, GUILayout.Width(100f));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawMonitorSettings()
        {
            EditorGUILayout.LabelField("Monitor", EditorStyles.boldLabel);
            monitor = (RuntimeTileMeshFusionIntegrityMonitor)EditorGUILayout.ObjectField(
                "Target",
                monitor,
                typeof(RuntimeTileMeshFusionIntegrityMonitor),
                true);

            if (monitor == null)
                return;

            EditorGUI.BeginChangeCheck();
            monitor.monitorEnabled = EditorGUILayout.Toggle("Enabled", monitor.monitorEnabled);
            monitor.monitorMergeGroups = EditorGUILayout.Toggle("Monitor Merge Groups", monitor.monitorMergeGroups);
            monitor.monitorEveryRebuild = EditorGUILayout.Toggle("Monitor Every Rebuild", monitor.monitorEveryRebuild);
            monitor.logIssuesToConsole = EditorGUILayout.Toggle("Log Issues To Console", monitor.logIssuesToConsole);
            monitor.logSuccessfulOperations = EditorGUILayout.Toggle("Log Successful Ops", monitor.logSuccessfulOperations);
            monitor.maxStoredReports = EditorGUILayout.IntField("Max Stored Reports", monitor.maxStoredReports);
            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(monitor);

            EditorGUILayout.LabelField(
                "Reports",
                monitor.TotalReportCount + " total | " + monitor.IssueReportCount + " with issues");
        }

        private void DrawReportList()
        {
            EditorGUILayout.LabelField("Recorded Events", EditorStyles.boldLabel);
            if (monitor.Reports.Count == 0)
            {
                EditorGUILayout.HelpBox("No reports yet. Play the scene and merge blocks to collect data.", MessageType.None);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(160f));
            for (int i = monitor.Reports.Count - 1; i >= 0; i--)
            {
                FusionIntegrityReport report = monitor.Reports[i];
                if (report == null)
                    continue;

                if (showOnlyIssues && !report.HasIssues)
                    continue;

                GUIStyle style = report.HasIssues ? EditorStyles.helpBox : EditorStyles.label;
                Color old = GUI.backgroundColor;
                if (report.HasIssues)
                    GUI.backgroundColor = new Color(1f, 0.72f, 0.72f);

                if (GUILayout.Toggle(selectedReportIndex == i, BuildReportLine(report), style))
                    selectedReportIndex = i;
                else if (selectedReportIndex == i)
                    selectedReportIndex = -1;

                GUI.backgroundColor = old;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSelectedReportDetails()
        {
            EditorGUILayout.LabelField("Details", EditorStyles.boldLabel);
            if (selectedReportIndex < 0 || selectedReportIndex >= monitor.Reports.Count)
            {
                EditorGUILayout.HelpBox("Select a report to inspect snapshots, missing tiles, and issue codes.", MessageType.None);
                return;
            }

            FusionIntegrityReport report = monitor.Reports[selectedReportIndex];
            EditorGUILayout.TextArea(RuntimeTileMeshFusionIntegrityAnalyzer.FormatReport(report), GUILayout.MinHeight(120f));

            if (report.beforeBlocks.Count > 0)
            {
                EditorGUILayout.LabelField("Before Blocks", EditorStyles.miniBoldLabel);
                for (int i = 0; i < report.beforeBlocks.Count; i++)
                    EditorGUILayout.LabelField(BuildSnapshotLine(report.beforeBlocks[i]));
            }

            if (report.afterBlocks.Count > 0)
            {
                EditorGUILayout.LabelField("After Blocks", EditorStyles.miniBoldLabel);
                for (int i = 0; i < report.afterBlocks.Count; i++)
                    EditorGUILayout.LabelField(BuildSnapshotLine(report.afterBlocks[i]));
            }

            if (report.issues.Count > 0)
            {
                EditorGUILayout.LabelField("Issues", EditorStyles.miniBoldLabel);
                for (int i = 0; i < report.issues.Count; i++)
                {
                    FusionIntegrityIssue issue = report.issues[i];
                    EditorGUILayout.LabelField("[" + issue.code + "] " + issue.message);
                    if (issue.affectedTiles != null && issue.affectedTiles.Count > 0)
                    {
                        EditorGUILayout.LabelField(
                            "  tiles: " + FormatTiles(issue.affectedTiles),
                            EditorStyles.miniLabel);
                    }
                }
            }

            DrawTileAccounting(report.tileAccounting);
        }

        private static void DrawTileAccounting(FusionIntegrityTileAccounting accounting)
        {
            if (accounting == null)
                return;

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Tile Accounting", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Trigger: " + accounting.triggerBlockName + " (" + accounting.triggerBlockTileCount + " tiles)");
            if (accounting.triggerTiles.Count > 0)
            {
                EditorGUILayout.LabelField(
                    "  triggerTiles: " + FormatTiles(accounting.triggerTiles),
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.LabelField(
                "Existing group: " + accounting.existingGroupBlockCount + " block(s), " +
                accounting.existingGroupTileSumRaw + " raw tile(s)");
            if (accounting.existingGroupTiles.Count > 0)
            {
                EditorGUILayout.LabelField(
                    "  existingGroupTiles: " + FormatTiles(accounting.existingGroupTiles),
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.LabelField("Overlap within group (raw - union): " + accounting.overlapWithinGroupCount);
            EditorGUILayout.LabelField(
                "Expected union: " + accounting.expectedUnionTileCount +
                " | Actual merged: " + accounting.actualMergedTileCount +
                " | Extra: " + accounting.extraGeneratedTileCount +
                " | Missing: " + accounting.missingTileCount);

            if (accounting.expectedUnionTiles.Count > 0)
            {
                EditorGUILayout.LabelField(
                    "  expectedUnionTiles: " + FormatTiles(accounting.expectedUnionTiles),
                    EditorStyles.miniLabel);
            }

            if (accounting.extraTiles.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "Extra generated tiles detected: " + FormatTiles(accounting.extraTiles),
                    MessageType.Warning);
            }

            if (accounting.missingTiles.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "Missing tiles after merge: " + FormatTiles(accounting.missingTiles),
                    MessageType.Error);
            }

            EditorGUILayout.LabelField(
                "Sandbox tiles: before=" + accounting.sandboxBeforeTileCount +
                " after=" + accounting.sandboxAfterTileCount +
                " outsideGroupBefore=" + accounting.sandboxOutsideGroupBeforeCount);

            if (accounting.duplicateLogicalTileEntries > 0)
            {
                EditorGUILayout.HelpBox(
                    "Seed carries " + accounting.duplicateLogicalTileEntries +
                    " duplicate local tile list entries after merge.",
                    MessageType.Warning);
            }
        }

        private static string BuildReportLine(FusionIntegrityReport report)
        {
            return report.frameLabel + " | " + report.operation + " | " + report.contextLabel +
                   (report.HasIssues ? " | ISSUES=" + report.issueCount : " | OK");
        }

        private static string BuildSnapshotLine(FusionIntegrityBlockSnapshot snapshot)
        {
            return snapshot.blockName +
                   " | worldTiles=" + snapshot.worldTileCount +
                   " | verts=" + snapshot.meshVertexCount +
                   " | tris=" + snapshot.meshTriangleCount +
                   " | boundaryEdges=" + snapshot.boundaryEdgeCount +
                   " | unconsumed=" + snapshot.unconsumedBoundaryEdges +
                   " | coversAll=" + snapshot.meshCoversAllTiles;
        }

        private static string FormatTiles(System.Collections.Generic.List<Vector2Int> tiles)
        {
            if (tiles == null || tiles.Count == 0)
                return "[]";

            StringBuilder builder = new StringBuilder();
            int count = Mathf.Min(16, tiles.Count);
            builder.Append('[');
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                    builder.Append(", ");
                builder.Append('(').Append(tiles[i].x).Append(',').Append(tiles[i].y).Append(')');
            }

            if (tiles.Count > count)
                builder.Append(", ...+").Append(tiles.Count - count);
            builder.Append(']');
            return builder.ToString();
        }

        private void ExportReports()
        {
            if (monitor == null || monitor.Reports.Count == 0)
                return;

            string path = EditorUtility.SaveFilePanel(
                "Export Fusion Integrity Log",
                Application.dataPath,
                "fusion-integrity-log.txt",
                "txt");
            if (string.IsNullOrEmpty(path))
                return;

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < monitor.Reports.Count; i++)
            {
                builder.Append(RuntimeTileMeshFusionIntegrityAnalyzer.FormatReport(monitor.Reports[i]));
                builder.Append("\n\n");
            }

            File.WriteAllText(path, builder.ToString());
            AssetDatabase.Refresh();
            Debug.Log("[FusionIntegrity] Exported " + monitor.Reports.Count + " report(s) to " + path);
        }

        private void ResolveMonitor()
        {
            monitor = FindFirstObjectByType<RuntimeTileMeshFusionIntegrityMonitor>();
        }

        private void RepaintIfPlaying()
        {
            if (Application.isPlaying)
                Repaint();
        }
    }
}
#endif
