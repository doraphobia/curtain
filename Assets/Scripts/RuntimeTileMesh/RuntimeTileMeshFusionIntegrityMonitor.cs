using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    [DisallowMultipleComponent]
    public sealed class RuntimeTileMeshFusionIntegrityMonitor : MonoBehaviour
    {
        public static RuntimeTileMeshFusionIntegrityMonitor Instance { get; private set; }

        [Header("Monitoring")]
        public bool monitorEnabled = true;
        public bool monitorMergeGroups = true;
        public bool monitorEveryRebuild = false;
        public bool logIssuesToConsole = true;
        public bool logSuccessfulOperations = false;
        public bool appendReportsToPlayLog = true;
        [Min(1)]
        public int maxStoredReports = 256;

        [Header("References")]
        public RuntimeTileMeshFusionSandbox fusionSandbox;

        private readonly List<FusionIntegrityReport> reports = new List<FusionIntegrityReport>();

        public IReadOnlyList<FusionIntegrityReport> Reports => reports;
        public int TotalReportCount => reports.Count;
        public int IssueReportCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < reports.Count; i++)
                {
                    if (reports[i] != null && reports[i].HasIssues)
                        count++;
                }

                return count;
            }
        }

        public event Action<FusionIntegrityReport> ReportRecorded;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            if (fusionSandbox == null)
                fusionSandbox = GetComponent<RuntimeTileMeshFusionSandbox>();

            if (Application.isPlaying)
                BeginPlayLogSession();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            if (!Application.isPlaying || !appendReportsToPlayLog || !monitorEnabled)
                return;

            AppendSessionEndAudit();
        }

        public void SetMonitoringEnabled(bool enabled)
        {
            monitorEnabled = enabled;
        }

        public void ClearReports()
        {
            reports.Clear();
        }

        public FusionIntegrityReport RecordReport(FusionIntegrityReport report)
        {
            if (report == null)
                return null;

            FusionIntegrityReport stored = report.Clone();
            reports.Add(stored);
            TrimReports();

            if (logIssuesToConsole && stored.HasIssues)
                Debug.LogWarning(RuntimeTileMeshFusionIntegrityAnalyzer.FormatReport(stored), this);
            else if (logSuccessfulOperations && !stored.HasIssues)
                Debug.Log(RuntimeTileMeshFusionIntegrityAnalyzer.FormatReport(stored), this);

            ReportRecorded?.Invoke(stored);
            AppendReportToPlayLog(stored);
            return stored;
        }

        public FusionIntegrityReport RunManualAudit(string contextLabel = "Manual Audit")
        {
            ResolveSandbox();
            FusionIntegrityReport report = RuntimeTileMeshFusionIntegrityAnalyzer.AnalyzeSandbox(
                fusionSandbox,
                contextLabel);
            return RecordReport(report);
        }

        public void RecordMergeGroup(
            IList<RuntimeTileMeshDraggableBlock> groupBlocks,
            RuntimeTileMeshDraggableBlock seed,
            HashSet<Vector2Int> mergedCells,
            RuntimeTileMeshBuildResult seedBuildResult,
            string contextLabel,
            FusionIntegrityMergeContext mergeContext = null)
        {
            if (!monitorEnabled || !monitorMergeGroups)
                return;

            ResolveSandbox();
            float gridSize = fusionSandbox != null ? fusionSandbox.gridSize : 1f;
            Vector2 gridOrigin = fusionSandbox != null ? fusionSandbox.gridOrigin : Vector2.zero;
            FusionIntegrityReport report = RuntimeTileMeshFusionIntegrityAnalyzer.AnalyzeMergeGroup(
                contextLabel,
                groupBlocks,
                seed,
                mergedCells,
                gridSize,
                gridOrigin,
                seedBuildResult,
                mergeContext);
            RecordReport(report);
        }

        public void RecordBlockRebuild(
            RuntimeTileMeshDraggableBlock block,
            RuntimeTileMeshBuildResult buildResult,
            string contextLabel)
        {
            if (!monitorEnabled || !monitorEveryRebuild || block == null)
                return;

            ResolveSandbox();
            float gridSize = fusionSandbox != null ? fusionSandbox.gridSize : 1f;
            Vector2 gridOrigin = fusionSandbox != null ? fusionSandbox.gridOrigin : Vector2.zero;
            FusionIntegrityReport report = RuntimeTileMeshFusionIntegrityAnalyzer.AnalyzeRebuild(
                block,
                buildResult,
                gridSize,
                gridOrigin,
                contextLabel);
            RecordReport(report);
        }

        public bool TryGetLatestIssueReport(out FusionIntegrityReport report)
        {
            for (int i = reports.Count - 1; i >= 0; i--)
            {
                FusionIntegrityReport candidate = reports[i];
                if (candidate != null && candidate.HasIssues)
                {
                    report = candidate;
                    return true;
                }
            }

            report = null;
            return false;
        }

        public static void TryRecordViewRebuild(RuntimeTileMeshView view, RuntimeTileMeshBuildResult buildResult)
        {
            if (view == null)
                return;

            RuntimeTileMeshFusionIntegrityMonitor monitor = Instance;
            if (monitor == null)
                monitor = FindFirstObjectByType<RuntimeTileMeshFusionIntegrityMonitor>();
            if (monitor == null || !monitor.monitorEnabled || !monitor.monitorEveryRebuild)
                return;

            RuntimeTileMeshDraggableBlock block = view.GetComponent<RuntimeTileMeshDraggableBlock>();
            if (block == null)
                return;

            monitor.RecordBlockRebuild(
                block,
                buildResult,
                "Rebuild -> " + block.name);
        }

        private void TrimReports()
        {
            int limit = Mathf.Max(1, maxStoredReports);
            while (reports.Count > limit)
                reports.RemoveAt(0);
        }

        private void ResolveSandbox()
        {
            if (fusionSandbox == null)
                fusionSandbox = GetComponent<RuntimeTileMeshFusionSandbox>();

            if (fusionSandbox == null)
                fusionSandbox = FindFirstObjectByType<RuntimeTileMeshFusionSandbox>();
        }

        public static string PlayLogPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "FusionIntegrityPlayLog.txt"));

        private void BeginPlayLogSession()
        {
            if (!appendReportsToPlayLog)
                return;

            try
            {
                StringBuilder builder = new StringBuilder();
                builder.Append("[FusionIntegrity] Play session started at t=")
                    .Append(Time.time.ToString("0.000"))
                    .Append(" scene=")
                    .Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)
                    .AppendLine();
                File.WriteAllText(PlayLogPath, builder.ToString());
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[FusionIntegrity] Failed to initialize play log: " + exception.Message, this);
            }
        }

        private void AppendReportToPlayLog(FusionIntegrityReport report)
        {
            if (!appendReportsToPlayLog || report == null)
                return;

            try
            {
                File.AppendAllText(
                    PlayLogPath,
                    RuntimeTileMeshFusionIntegrityAnalyzer.FormatReport(report) + Environment.NewLine + Environment.NewLine);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[FusionIntegrity] Failed to append play log: " + exception.Message, this);
            }
        }

        private void AppendSessionEndAudit()
        {
            ResolveSandbox();
            if (fusionSandbox == null)
                return;

            try
            {
                FusionIntegrityReport report = RunManualAudit("Play Session End Audit");
                StringBuilder builder = new StringBuilder();
                builder.Append("[FusionIntegrity] Play session ended at t=")
                    .Append(Time.time.ToString("0.000"))
                    .Append(" | activeBlocks=")
                    .Append(report.afterBlocks.Count)
                    .Append(" | worldTiles=")
                    .Append(report.actualTileCount)
                    .AppendLine();
                builder.Append(RuntimeTileMeshFusionIntegrityAnalyzer.FormatReport(report));
                builder.AppendLine();
                File.AppendAllText(PlayLogPath, builder.ToString());
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[FusionIntegrity] Failed to append session-end audit: " + exception.Message, this);
            }
        }
    }
}
