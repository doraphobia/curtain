using System;
using System.Collections.Generic;
using UnityEngine;

namespace FigmaImporter
{
    [DisallowMultipleComponent]
    public sealed class FigmaFrameSyncBinding : MonoBehaviour
    {
        [Serializable]
        public sealed class NodeSnapshotEntry
        {
            public string nodeId;
            public string parentNodeId;
            public string nodeName;
            public string nodeType;
            public string signature;
        }

        [Serializable]
        public sealed class ChangeEntry
        {
            public bool selected = true;
            public string changeType;
            public string nodeId;
            public string nodeName;
            public string summary;
        }

        [Serializable]
        public sealed class IssueEntry
        {
            public string severity;
            public string category;
            public string message;
            public string timestampUtc;
        }

        [SerializeField] private string figmaUrl = string.Empty;
        [SerializeField] private string fileKey = string.Empty;
        [SerializeField] private string rootNodeId = string.Empty;
        [SerializeField] private string rootNodeName = string.Empty;
        [SerializeField] private string boundObjectPath = string.Empty;
        [SerializeField] private string baselineLabel = string.Empty;
        [SerializeField] private string lastCheckAtUtc = string.Empty;
        [SerializeField] private string lastSyncAtUtc = string.Empty;
        [SerializeField] private string lastStatus = "Idle";
        [SerializeField] private string lastError = string.Empty;
        [SerializeField] private string lastCheckSummary = string.Empty;
        [SerializeField] private bool hasPendingChanges;
        [SerializeField] private List<NodeSnapshotEntry> baselineSnapshot = new List<NodeSnapshotEntry>();
        [SerializeField] private List<NodeSnapshotEntry> stagedSnapshot = new List<NodeSnapshotEntry>();
        [SerializeField] private List<ChangeEntry> pendingChanges = new List<ChangeEntry>();
        [SerializeField] private List<IssueEntry> issues = new List<IssueEntry>();

        public string FigmaUrl => figmaUrl;
        public string FileKey => fileKey;
        public string RootNodeId => rootNodeId;
        public string RootNodeName => rootNodeName;
        public string BoundObjectPath => boundObjectPath;
        public string BaselineLabel => baselineLabel;
        public string LastCheckAtUtc => lastCheckAtUtc;
        public string LastSyncAtUtc => lastSyncAtUtc;
        public string LastStatus => lastStatus;
        public string LastError => lastError;
        public string LastCheckSummary => lastCheckSummary;
        public bool HasPendingChanges => hasPendingChanges;
        public bool HasBaseline => baselineSnapshot != null && baselineSnapshot.Count > 0;
        public IReadOnlyList<NodeSnapshotEntry> BaselineSnapshot => baselineSnapshot;
        public IReadOnlyList<NodeSnapshotEntry> StagedSnapshot => stagedSnapshot;
        public List<ChangeEntry> PendingChanges => pendingChanges;
        public List<IssueEntry> Issues => issues;

        public int SelectedChangeCount
        {
            get
            {
                if (pendingChanges == null || pendingChanges.Count == 0)
                {
                    return 0;
                }

                var selected = 0;
                for (var i = 0; i < pendingChanges.Count; i++)
                {
                    if (pendingChanges[i] != null && pendingChanges[i].selected)
                    {
                        selected++;
                    }
                }

                return selected;
            }
        }

        public void Configure(string sourceFigmaUrl, string sourceFileKey, string sourceRootNodeId, string sourceRootNodeName)
        {
            figmaUrl = sourceFigmaUrl ?? string.Empty;
            fileKey = sourceFileKey ?? string.Empty;
            rootNodeId = sourceRootNodeId ?? string.Empty;
            rootNodeName = sourceRootNodeName ?? string.Empty;
            boundObjectPath = BuildTransformPath(transform);
            if (string.IsNullOrWhiteSpace(lastStatus))
            {
                lastStatus = "Ready";
            }
        }

        public void SetBaselineSnapshot(IList<NodeSnapshotEntry> snapshot, string label)
        {
            baselineSnapshot = CloneSnapshot(snapshot);
            stagedSnapshot.Clear();
            pendingChanges.Clear();
            hasPendingChanges = false;
            baselineLabel = label ?? string.Empty;
            lastSyncAtUtc = DateTime.UtcNow.ToString("u");
            lastCheckSummary = "Baseline snapshot updated.";
            lastStatus = "Synced";
            lastError = string.Empty;
        }

        public void SetPendingChanges(
            IList<ChangeEntry> changes,
            IList<NodeSnapshotEntry> latestSnapshot,
            string summary)
        {
            pendingChanges = CloneChanges(changes);
            stagedSnapshot = CloneSnapshot(latestSnapshot);
            hasPendingChanges = pendingChanges.Count > 0;
            lastCheckAtUtc = DateTime.UtcNow.ToString("u");
            lastCheckSummary = summary ?? string.Empty;
            lastStatus = hasPendingChanges ? "Updates Found" : "Up To Date";
            if (!hasPendingChanges)
            {
                lastError = string.Empty;
            }
        }

        public void CommitStagedSnapshotAsBaseline(string summary)
        {
            if (stagedSnapshot != null && stagedSnapshot.Count > 0)
            {
                baselineSnapshot = CloneSnapshot(stagedSnapshot);
            }

            stagedSnapshot.Clear();
            pendingChanges.Clear();
            hasPendingChanges = false;
            lastSyncAtUtc = DateTime.UtcNow.ToString("u");
            lastStatus = "Synced";
            lastCheckSummary = summary ?? "Applied updates to Unity frame.";
            lastError = string.Empty;
        }

        public void CommitAppliedChanges(IList<string> appliedNodeIds, IList<NodeSnapshotEntry> latestSnapshot, string summary = null)
        {
            if (appliedNodeIds == null || appliedNodeIds.Count == 0)
            {
                return;
            }

            var appliedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < appliedNodeIds.Count; i++)
            {
                var id = appliedNodeIds[i];
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                appliedSet.Add(id);
            }

            if (appliedSet.Count == 0)
            {
                return;
            }

            var baselineMap = BuildSnapshotMap(baselineSnapshot);
            var latestMap = BuildSnapshotMap(latestSnapshot);
            foreach (var appliedId in appliedSet)
            {
                if (latestMap.TryGetValue(appliedId, out var latestEntry))
                {
                    baselineMap[appliedId] = CloneSnapshotEntry(latestEntry);
                }
                else
                {
                    baselineMap.Remove(appliedId);
                }
            }

            var nextBaseline = new List<NodeSnapshotEntry>(baselineMap.Values);
            nextBaseline.Sort((left, right) => string.Compare(left?.nodeId, right?.nodeId, StringComparison.OrdinalIgnoreCase));
            baselineSnapshot = nextBaseline;
            stagedSnapshot = CloneSnapshot(latestSnapshot);

            if (pendingChanges == null)
            {
                pendingChanges = new List<ChangeEntry>();
            }

            pendingChanges.RemoveAll(change =>
                change != null &&
                !string.IsNullOrWhiteSpace(change.nodeId) &&
                appliedSet.Contains(change.nodeId));

            hasPendingChanges = pendingChanges.Count > 0;
            lastSyncAtUtc = DateTime.UtcNow.ToString("u");
            lastStatus = hasPendingChanges ? "Partial Sync Applied" : "Synced";
            lastCheckSummary = summary ?? "Applied selected changes to Unity frame.";
            lastError = string.Empty;
        }

        public void ClearPendingChanges(string summary = null)
        {
            pendingChanges.Clear();
            stagedSnapshot.Clear();
            hasPendingChanges = false;
            lastCheckSummary = summary ?? "Pending changes cleared.";
            if (string.IsNullOrWhiteSpace(lastStatus))
            {
                lastStatus = "Ready";
            }
        }

        public void MarkRegenerated(string summary = null)
        {
            lastSyncAtUtc = DateTime.UtcNow.ToString("u");
            lastStatus = "Regenerated";
            lastError = string.Empty;
            if (!string.IsNullOrWhiteSpace(summary))
            {
                lastCheckSummary = summary;
            }
        }

        public void SetStatus(string status)
        {
            lastStatus = string.IsNullOrWhiteSpace(status) ? "Ready" : status;
        }

        public void SetError(string errorMessage)
        {
            lastError = errorMessage ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(lastError))
            {
                lastStatus = "Error";
            }
        }

        public void ReplaceIssues(IList<IssueEntry> newIssues)
        {
            issues = CloneIssues(newIssues);
        }

        public void ClearIssues()
        {
            issues.Clear();
        }

        public void AddIssue(string severity, string category, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            issues.Add(new IssueEntry
            {
                severity = string.IsNullOrWhiteSpace(severity) ? "Warning" : severity,
                category = string.IsNullOrWhiteSpace(category) ? "General" : category,
                message = message.Trim(),
                timestampUtc = DateTime.UtcNow.ToString("u")
            });
        }

        private static string BuildTransformPath(Transform current)
        {
            if (current == null)
            {
                return string.Empty;
            }

            var segments = new List<string>();
            while (current != null)
            {
                segments.Add(current.name);
                current = current.parent;
            }

            segments.Reverse();
            return string.Join("/", segments);
        }

        private static List<NodeSnapshotEntry> CloneSnapshot(IList<NodeSnapshotEntry> source)
        {
            var result = new List<NodeSnapshotEntry>();
            if (source == null)
            {
                return result;
            }

            for (var i = 0; i < source.Count; i++)
            {
                var item = source[i];
                if (item == null)
                {
                    continue;
                }

                result.Add(new NodeSnapshotEntry
                {
                    nodeId = item.nodeId,
                    parentNodeId = item.parentNodeId,
                    nodeName = item.nodeName,
                    nodeType = item.nodeType,
                    signature = item.signature
                });
            }

            return result;
        }

        private static Dictionary<string, NodeSnapshotEntry> BuildSnapshotMap(IList<NodeSnapshotEntry> source)
        {
            var map = new Dictionary<string, NodeSnapshotEntry>(StringComparer.OrdinalIgnoreCase);
            if (source == null)
            {
                return map;
            }

            for (var i = 0; i < source.Count; i++)
            {
                var entry = source[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.nodeId))
                {
                    continue;
                }

                map[entry.nodeId] = entry;
            }

            return map;
        }

        private static NodeSnapshotEntry CloneSnapshotEntry(NodeSnapshotEntry source)
        {
            if (source == null)
            {
                return null;
            }

            return new NodeSnapshotEntry
            {
                nodeId = source.nodeId,
                parentNodeId = source.parentNodeId,
                nodeName = source.nodeName,
                nodeType = source.nodeType,
                signature = source.signature
            };
        }

        private static List<ChangeEntry> CloneChanges(IList<ChangeEntry> source)
        {
            var result = new List<ChangeEntry>();
            if (source == null)
            {
                return result;
            }

            for (var i = 0; i < source.Count; i++)
            {
                var item = source[i];
                if (item == null)
                {
                    continue;
                }

                result.Add(new ChangeEntry
                {
                    selected = item.selected,
                    changeType = item.changeType,
                    nodeId = item.nodeId,
                    nodeName = item.nodeName,
                    summary = item.summary
                });
            }

            return result;
        }

        private static List<IssueEntry> CloneIssues(IList<IssueEntry> source)
        {
            var result = new List<IssueEntry>();
            if (source == null)
            {
                return result;
            }

            for (var i = 0; i < source.Count; i++)
            {
                var item = source[i];
                if (item == null)
                {
                    continue;
                }

                result.Add(new IssueEntry
                {
                    severity = item.severity,
                    category = item.category,
                    message = item.message,
                    timestampUtc = item.timestampUtc
                });
            }

            return result;
        }
    }
}
