using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FigmaImporter.Editor.EditorTree.TreeData;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace FigmaImporter.Editor
{
    [CustomEditor(typeof(global::FigmaImporter.FigmaFrameSyncBinding))]
    internal sealed class FigmaFrameSyncBindingEditor : UnityEditor.Editor
    {
        private global::FigmaImporter.FigmaFrameSyncBinding _binding;
        private bool _isChecking;
        private bool _isApplying;
        private bool _isRegenerating;
        private Vector2 _changesScroll;
        private Vector2 _issuesScroll;
        private CancellationTokenSource _operationCts;
        private bool IsBusy => _isChecking || _isApplying || _isRegenerating;

        private void OnEnable()
        {
            _binding = target as global::FigmaImporter.FigmaFrameSyncBinding;
        }

        private void OnDisable()
        {
            CancelActiveOperation();
        }

        public override void OnInspectorGUI()
        {
            if (_binding == null)
            {
                EditorGUILayout.HelpBox("Binding component is missing.", MessageType.Error);
                return;
            }

            serializedObject.Update();
            DrawBindingHeader();
            EditorGUILayout.Space(6f);
            DrawSyncActions();
            EditorGUILayout.Space(6f);
            DrawChangesSection();
            EditorGUILayout.Space(6f);
            DrawIssuesSection();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawBindingHeader()
        {
            EditorGUILayout.LabelField("Figma Frame Sync", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("figmaUrl"), new GUIContent("Figma URL"));

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("fileKey"), new GUIContent("File Key"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("rootNodeId"), new GUIContent("Root Node ID"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("rootNodeName"), new GUIContent("Root Node Name"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("boundObjectPath"), new GUIContent("Bound Object"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("baselineLabel"), new GUIContent("Baseline"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("lastSyncAtUtc"), new GUIContent("Last Sync (UTC)"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("lastCheckAtUtc"), new GUIContent("Last Check (UTC)"));
            }

            var status = string.IsNullOrWhiteSpace(_binding.LastStatus) ? "Idle" : _binding.LastStatus;
            var statusType = ResolveStatusMessageType(_binding.LastStatus, _binding.LastError);
            EditorGUILayout.HelpBox($"Status: {status}", statusType);

            if (!string.IsNullOrWhiteSpace(_binding.LastCheckSummary))
            {
                EditorGUILayout.HelpBox(_binding.LastCheckSummary, MessageType.None);
            }

            if (!string.IsNullOrWhiteSpace(_binding.LastError))
            {
                EditorGUILayout.HelpBox(_binding.LastError, MessageType.Error);
            }
        }

        private void DrawSyncActions()
        {
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            var isBusy = IsBusy;
            using (new EditorGUI.DisabledScope(isBusy))
            {
                if (GUILayout.Button("Check Figma Updates", GUILayout.Height(24f)))
                {
                    _ = CheckForUpdatesAsync();
                }
            }

            using (new EditorGUI.DisabledScope(
                       isBusy ||
                       !_binding.HasPendingChanges ||
                       _binding.PendingChanges == null ||
                       _binding.PendingChanges.Count == 0))
            {
                if (GUILayout.Button("Apply Selected Changes To Unity Frame", GUILayout.Height(24f)))
                {
                    _ = ApplySelectedChangesAsync();
                }
            }

            using (new EditorGUI.DisabledScope(isBusy))
            {
                if (GUILayout.Button("Regenerate Current Frame", GUILayout.Height(24f)))
                {
                    _ = RegenerateCurrentFrameAsync();
                }
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(isBusy))
            {
                if (GUILayout.Button("Clear Pending Changes", GUILayout.Height(22f)))
                {
                    Undo.RecordObject(_binding, "Clear Figma Pending Changes");
                    _binding.ClearPendingChanges("Pending changes manually cleared.");
                    EditorUtility.SetDirty(_binding);
                }
            }

            if (GUILayout.Button("Open Fallback Resolver", GUILayout.Height(22f)))
            {
                FigmaDiagnosticsHubWindow.OpenFallbackPage();
            }

            EditorGUILayout.EndHorizontal();

            if (_isChecking)
            {
                EditorGUILayout.HelpBox("Checking remote frame changes...", MessageType.Info);
            }
            else if (_isApplying)
            {
                EditorGUILayout.HelpBox("Applying selected changes to Unity frame...", MessageType.Info);
            }
            else if (_isRegenerating)
            {
                EditorGUILayout.HelpBox("Regenerating this frame with current importer pipeline...", MessageType.Info);
            }
        }

        private void DrawChangesSection()
        {
            EditorGUILayout.LabelField("Detected Changes", EditorStyles.boldLabel);
            if (_binding.PendingChanges == null || _binding.PendingChanges.Count == 0)
            {
                EditorGUILayout.HelpBox("No pending changes. Click 'Check Figma Updates' first.", MessageType.None);
                return;
            }

            EditorGUILayout.HelpBox(
                $"Total: {_binding.PendingChanges.Count}, Selected: {_binding.SelectedChangeCount}",
                MessageType.Info);

            _changesScroll = EditorGUILayout.BeginScrollView(_changesScroll, GUILayout.MinHeight(120f));
            for (var i = 0; i < _binding.PendingChanges.Count; i++)
            {
                var change = _binding.PendingChanges[i];
                if (change == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                var rowLabel = $"{change.changeType}: {change.nodeName} [{change.nodeId}]";
                var selected = EditorGUILayout.ToggleLeft(rowLabel, change.selected);
                if (selected != change.selected)
                {
                    Undo.RecordObject(_binding, "Toggle Figma Change Selection");
                    change.selected = selected;
                    EditorUtility.SetDirty(_binding);
                }

                if (!string.IsNullOrWhiteSpace(change.summary))
                {
                    EditorGUILayout.LabelField(change.summary, EditorStyles.miniLabel);
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawIssuesSection()
        {
            EditorGUILayout.LabelField("Issues / Fallback", EditorStyles.boldLabel);
            if (_binding.Issues == null || _binding.Issues.Count == 0)
            {
                EditorGUILayout.HelpBox("No issue records on this frame.", MessageType.None);
                return;
            }

            _issuesScroll = EditorGUILayout.BeginScrollView(_issuesScroll, GUILayout.MinHeight(100f));
            for (var i = 0; i < _binding.Issues.Count; i++)
            {
                var issue = _binding.Issues[i];
                if (issue == null)
                {
                    continue;
                }

                var severity = string.IsNullOrWhiteSpace(issue.severity) ? "Warning" : issue.severity;
                var category = string.IsNullOrWhiteSpace(issue.category) ? "General" : issue.category;
                var timestamp = string.IsNullOrWhiteSpace(issue.timestampUtc) ? "n/a" : issue.timestampUtc;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"{severity} | {category}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(issue.message ?? string.Empty, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField($"UTC: {timestamp}", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        private async Task CheckForUpdatesAsync()
        {
            if (_binding == null)
            {
                return;
            }

            if (IsBusy)
            {
                return;
            }

            var flowChainId = FigmaImporterEventFlow.Start(
                "FrameSyncCheck",
                "Check Figma Updates",
                $"object={_binding.gameObject.name}");
            var flowResult = "Completed";
            var flowDetails = string.Empty;
            _isChecking = true;
            CreateOperationCts();
            Repaint();
            try
            {
                FigmaImporterEventFlow.Step("FrameSyncCheck", flowChainId, "Begin");
                Undo.RecordObject(_binding, "Check Figma Frame Updates");
                _binding.SetError(string.Empty);
                _binding.SetStatus("Checking");
                _binding.ClearIssues();
                EditorUtility.SetDirty(_binding);

                if (!TryBuildRequestContext(out var fileKey, out var nodeId, out var requestUrl, out var normalizedUrl, out var reason))
                {
                    flowResult = "Failed";
                    flowDetails = reason;
                    ReportError("Check updates failed", reason);
                    return;
                }

                FigmaImporterEventFlow.Step(
                    "FrameSyncCheck",
                    flowChainId,
                    "RequestContextBuilt",
                    $"fileKey={fileKey}; nodeId={nodeId}");

                var settings = FigmaImporterSettings.GetInstance();
                if (settings == null || string.IsNullOrWhiteSpace(settings.Token))
                {
                    flowResult = "Failed";
                    flowDetails = "Figma token is empty";
                    ReportError("Check updates failed", "Figma token is empty. Please run GetToken in FigmaImporter window.");
                    return;
                }

                _binding.Configure(normalizedUrl, fileKey, nodeId, _binding.RootNodeName);
                var payload = await GetJsonAsync(requestUrl, settings.Token, _operationCts.Token);
                FigmaImporterEventFlow.Step("FrameSyncCheck", flowChainId, "PayloadFetched");
                var parser = new FigmaParser();
                var parsed = parser.ParseResult(payload);
                var remoteRoot = parsed != null && parsed.Count > 0 ? parsed[0] : null;
                if (remoteRoot == null)
                {
                    flowResult = "Failed";
                    flowDetails = "Unable to parse root node";
                    ReportError("Check updates failed", "Unable to parse root node from Figma response.");
                    return;
                }

                _binding.Configure(normalizedUrl, fileKey, remoteRoot.id, remoteRoot.name);
                var latestSnapshot = FigmaFrameSyncDiffUtility.BuildSnapshot(remoteRoot);
                if (!_binding.HasBaseline)
                {
                    _binding.SetBaselineSnapshot(latestSnapshot, "Initialized by first sync check");
                    _binding.AddIssue(
                        "Info",
                        "Baseline",
                        "No previous baseline was found. Baseline has been initialized from current Figma state.");
                    _binding.SetStatus("Baseline initialized");
                    EditorUtility.SetDirty(_binding);
                    flowResult = "InitializedBaseline";
                    flowDetails = $"nodeId={remoteRoot.id}";
                    Debug.LogWarning(
                        $"[FigmaImporter] Baseline initialized for '{_binding.gameObject.name}'. Run check again after Figma updates.");
                    return;
                }

                var diff = FigmaFrameSyncDiffUtility.ComputeDiff(
                    _binding.BaselineSnapshot.ToList(),
                    latestSnapshot);
                _binding.SetPendingChanges(diff.Changes, latestSnapshot, diff.Summary);
                RefreshIssuesFromRegistry(remoteRoot.id, latestSnapshot);
                _binding.SetError(string.Empty);
                _binding.SetStatus(diff.Changes.Count > 0 ? "Updates Found" : "Up To Date");
                EditorUtility.SetDirty(_binding);
                flowDetails = $"changes={diff.Changes.Count}";
                FigmaImporterEventFlow.Step("FrameSyncCheck", flowChainId, "DiffComputed", flowDetails);

                if (diff.Changes.Count > 0)
                {
                    Debug.Log($"[FigmaImporter] {_binding.gameObject.name}: {diff.Summary}");
                }
                else
                {
                    Debug.Log($"[FigmaImporter] {_binding.gameObject.name}: no updates detected.");
                }
            }
            catch (OperationCanceledException)
            {
                flowResult = "Canceled";
                flowDetails = "Operation canceled";
                ReportCanceled("Check updates canceled", "Operation canceled.");
            }
            catch (Exception e)
            {
                flowResult = "Failed";
                flowDetails = e.Message;
                ReportError("Check updates failed", e.Message);
                Debug.LogException(e);
            }
            finally
            {
                _isChecking = false;
                CancelActiveOperation();
                FigmaImporterEventFlow.End("FrameSyncCheck", flowChainId, flowResult, flowDetails);
                Repaint();
            }
        }

        private async Task ApplySelectedChangesAsync()
        {
            if (_binding == null)
            {
                return;
            }

            if (IsBusy)
            {
                return;
            }

            var flowChainId = FigmaImporterEventFlow.Start(
                "FrameSyncApply",
                "Apply Selected Changes To Unity Frame",
                $"object={_binding.gameObject.name}");
            var flowResult = "Completed";
            var flowDetails = string.Empty;
            var selectedCount = _binding.SelectedChangeCount;
            if (selectedCount <= 0)
            {
                flowResult = "Skipped";
                flowDetails = "No selected changes";
                FigmaImporterEventFlow.End("FrameSyncApply", flowChainId, flowResult, flowDetails);
                ReportBlocked("Apply changes blocked", "No changes selected. Please select at least one change.");
                return;
            }

            _isApplying = true;
            CreateOperationCts();
            Repaint();
            try
            {
                FigmaImporterEventFlow.Step("FrameSyncApply", flowChainId, "SelectionValidated", $"selected={selectedCount}");
                if (!TryBuildRequestContext(
                        out var fileKey,
                        out var nodeId,
                        out var requestUrl,
                        out var normalizedUrl,
                        out var contextError))
                {
                    flowResult = "Failed";
                    flowDetails = contextError;
                    ReportError("Apply changes failed", contextError);
                    return;
                }

                var settings = FigmaImporterSettings.GetInstance();
                if (settings == null || string.IsNullOrWhiteSpace(settings.Token))
                {
                    flowResult = "Failed";
                    flowDetails = "Figma token is empty";
                    ReportError("Apply changes failed", "Figma token is empty. Please run GetToken in FigmaImporter window.");
                    return;
                }

                _binding.Configure(normalizedUrl, fileKey, nodeId, _binding.RootNodeName);
                var payload = await GetJsonAsync(requestUrl, settings.Token, _operationCts.Token);
                var parser = new FigmaParser();
                var remoteRoots = parser.ParseResult(payload);
                if (remoteRoots == null || remoteRoots.Count == 0)
                {
                    flowResult = "Failed";
                    flowDetails = "Remote node parse returned no nodes";
                    ReportError("Apply changes failed", "Unable to parse remote Figma node tree.");
                    return;
                }

                var remoteRoot = remoteRoots[0];
                _binding.Configure(normalizedUrl, fileKey, remoteRoot.id, remoteRoot.name);
                var latestSnapshot = FigmaFrameSyncDiffUtility.BuildSnapshot(remoteRoot);

                var selectedChanges = _binding.PendingChanges
                    .Where(change =>
                        change != null &&
                        change.selected &&
                        !string.IsNullOrWhiteSpace(change.nodeId))
                    .ToList();

                if (selectedChanges.Count == 0)
                {
                    flowResult = "Skipped";
                    flowDetails = "No valid selected changes";
                    ReportBlocked("Apply changes blocked", "No valid selected changes to apply.");
                    return;
                }

                var remoteNodeById = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
                var remoteParentById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var rootIndex = 0; rootIndex < remoteRoots.Count; rootIndex++)
                {
                    CollectNodeLookup(remoteRoots[rootIndex], null, remoteNodeById, remoteParentById);
                }

                var parentByIdForPending = BuildParentLookup(_binding.BaselineSnapshot, latestSnapshot);
                var plannedChanges = FilterChangesForIncrementalApply(selectedChanges, parentByIdForPending);
                if (plannedChanges.Count == 0)
                {
                    flowResult = "Skipped";
                    flowDetails = "No effective selected changes";
                    ReportBlocked("Apply changes blocked", "Selected changes are already covered by parent selections.");
                    return;
                }

                FigmaImporterEventFlow.Step(
                    "FrameSyncApply",
                    flowChainId,
                    "OperationPlanBuilt",
                    $"selected={selectedChanges.Count}; planned={plannedChanges.Count}");

                await FigmaPackageBootstrapper.EnsureDependenciesInstalledForImportAsync();
                var importerWindow = EditorWindow.GetWindow<FigmaImporter>();
                var generator = new FigmaNodeGenerator(importerWindow, _operationCts.Token);

                var treeElements = BuildDefaultNodeTreeElements(remoteRoots);
                NodesAnalyzer.AnalyzeRenderMode(remoteRoots, treeElements);

                var appliedOperationNodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var subtreeAppliedNodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var skippedOperations = 0;
                var appliedRemoved = 0;
                var appliedUpserts = 0;
                for (var i = 0; i < plannedChanges.Count; i++)
                {
                    _operationCts.Token.ThrowIfCancellationRequested();
                    var change = plannedChanges[i];
                    var changeType = NormalizeChangeType(change.changeType);
                    if (string.Equals(changeType, "Removed", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryRemoveNodeGameObject(change.nodeId, out var removeReason))
                        {
                            appliedOperationNodeIds.Add(change.nodeId);
                            subtreeAppliedNodeIds.Add(change.nodeId);
                            appliedRemoved++;
                        }
                        else
                        {
                            skippedOperations++;
                            if (!string.IsNullOrWhiteSpace(removeReason))
                            {
                                Debug.LogWarning($"[FigmaImporter] Apply selected change skipped ({change.nodeId}): {removeReason}");
                            }
                        }

                        continue;
                    }

                    if (!remoteNodeById.TryGetValue(change.nodeId, out var remoteNode))
                    {
                        skippedOperations++;
                        Debug.LogWarning($"[FigmaImporter] Apply selected change skipped ({change.nodeId}): node not found in latest payload.");
                        continue;
                    }

                    remoteParentById.TryGetValue(remoteNode.id, out var remoteParentId);
                    Node remoteParentNode = null;
                    if (!string.IsNullOrWhiteSpace(remoteParentId))
                    {
                        remoteNodeById.TryGetValue(remoteParentId, out remoteParentNode);
                    }

                    var unityParent = ResolveUnityParentForChange(
                        change.nodeId,
                        remoteParentId,
                        _binding.gameObject,
                        _binding.RootNodeId);

                    if (unityParent == null)
                    {
                        skippedOperations++;
                        Debug.LogWarning($"[FigmaImporter] Apply selected change skipped ({change.nodeId}): parent object not found in Unity hierarchy.");
                        continue;
                    }

                    var isImportRoot = string.Equals(remoteNode.id, _binding.RootNodeId, StringComparison.OrdinalIgnoreCase);
                    var includeChildren =
                        string.Equals(changeType, "Added", StringComparison.OrdinalIgnoreCase);
                    await generator.GenerateNodeForSync(
                        remoteNode,
                        unityParent,
                        treeElements,
                        remoteParentNode,
                        isImportRoot,
                        includeChildren);
                    appliedOperationNodeIds.Add(change.nodeId);
                    if (includeChildren)
                    {
                        subtreeAppliedNodeIds.Add(change.nodeId);
                    }
                    appliedUpserts++;
                }

                if (appliedOperationNodeIds.Count == 0)
                {
                    flowResult = "Skipped";
                    flowDetails = $"No operations applied; skipped={skippedOperations}";
                    ReportBlocked("Apply changes blocked", "No selected changes could be applied. See warnings for details.");
                    return;
                }

                var effectiveAppliedNodeIds = ExpandCoveredPendingNodeIds(
                    _binding.PendingChanges,
                    parentByIdForPending,
                    appliedOperationNodeIds,
                    subtreeAppliedNodeIds);

                Undo.RecordObject(_binding, "Apply Figma Frame Changes");
                _binding.CommitAppliedChanges(
                    effectiveAppliedNodeIds.ToList(),
                    latestSnapshot,
                    $"Applied {appliedOperationNodeIds.Count}/{selectedChanges.Count} selected changes. " +
                    $"Node updates: {appliedUpserts}, removals: {appliedRemoved}, skipped: {skippedOperations}.");

                RefreshIssuesFromRegistry(_binding.RootNodeId, latestSnapshot);
                _binding.SetError(string.Empty);
                EditorUtility.SetDirty(_binding);
                flowDetails =
                    $"selected={selectedChanges.Count}; applied={appliedOperationNodeIds.Count}; " +
                    $"effectiveApplied={effectiveAppliedNodeIds.Count}; upserts={appliedUpserts}; removed={appliedRemoved}; skipped={skippedOperations}";
                FigmaImporterEventFlow.Step("FrameSyncApply", flowChainId, "Applied", flowDetails);
                Debug.Log(
                    $"[FigmaImporter] Applied selected frame updates on '{_binding.gameObject.name}'. " +
                    $"Applied: {appliedOperationNodeIds.Count}, skipped: {skippedOperations}.");
            }
            catch (OperationCanceledException)
            {
                flowResult = "Canceled";
                flowDetails = "Operation canceled";
                ReportCanceled("Apply changes canceled", "Operation canceled.");
            }
            catch (Exception e)
            {
                flowResult = "Failed";
                flowDetails = e.Message;
                ReportError("Apply changes failed", e.Message);
                Debug.LogException(e);
            }
            finally
            {
                _isApplying = false;
                CancelActiveOperation();
                FigmaImporterEventFlow.End("FrameSyncApply", flowChainId, flowResult, flowDetails);
                Repaint();
            }
        }

        private async Task RegenerateCurrentFrameAsync()
        {
            if (_binding == null)
            {
                return;
            }

            if (IsBusy)
            {
                return;
            }

            var flowChainId = FigmaImporterEventFlow.Start(
                "FrameSyncRegenerate",
                "Regenerate Current Frame",
                $"object={_binding.gameObject.name}");
            var flowResult = "Completed";
            var flowDetails = string.Empty;
            _isRegenerating = true;
            CreateOperationCts();
            Repaint();

            try
            {
                FigmaImporterEventFlow.Step("FrameSyncRegenerate", flowChainId, "Begin");
                if (!TryBuildRequestContext(out _, out var nodeId, out _, out var normalizedUrl, out var reason))
                {
                    flowResult = "Failed";
                    flowDetails = reason;
                    ReportError("Regenerate failed", reason);
                    return;
                }

                var syncUrl = EnsureNodeIdOnUrl(normalizedUrl, nodeId);
                if (string.IsNullOrWhiteSpace(syncUrl))
                {
                    flowResult = "Failed";
                    flowDetails = "Invalid Figma URL";
                    ReportError("Regenerate failed", "Figma URL is invalid.");
                    return;
                }

                Undo.RecordObject(_binding, "Regenerate Figma Frame");
                _binding.SetError(string.Empty);
                _binding.SetStatus("Regenerating");
                EditorUtility.SetDirty(_binding);

                var importerWindow = EditorWindow.GetWindow<FigmaImporter>();
                var success = await importerWindow.ApplySyncToBoundFrameAsync(_binding.gameObject, syncUrl);
                if (!success)
                {
                    flowResult = "Failed";
                    flowDetails = "Importer failed to regenerate frame";
                    ReportError("Regenerate failed", "Importer failed to regenerate this frame.");
                    return;
                }

                Undo.RecordObject(_binding, "Regenerate Figma Frame");
                _binding.MarkRegenerated("Frame regenerated from current Figma node using latest importer logic.");
                EditorUtility.SetDirty(_binding);
                flowDetails = $"nodeId={nodeId}";
                FigmaImporterEventFlow.Step("FrameSyncRegenerate", flowChainId, "Regenerated", flowDetails);
                Debug.Log($"[FigmaImporter] Regenerated frame '{_binding.gameObject.name}' from current Figma node.");
            }
            catch (OperationCanceledException)
            {
                flowResult = "Canceled";
                flowDetails = "Operation canceled";
                ReportCanceled("Regenerate canceled", "Operation canceled.");
            }
            catch (Exception e)
            {
                flowResult = "Failed";
                flowDetails = e.Message;
                ReportError("Regenerate failed", e.Message);
                Debug.LogException(e);
            }
            finally
            {
                _isRegenerating = false;
                CancelActiveOperation();
                FigmaImporterEventFlow.End("FrameSyncRegenerate", flowChainId, flowResult, flowDetails);
                Repaint();
            }
        }

        private bool TryBuildRequestContext(
            out string fileKey,
            out string nodeId,
            out string requestUrl,
            out string normalizedUrl,
            out string error)
        {
            fileKey = _binding.FileKey;
            nodeId = _binding.RootNodeId;
            requestUrl = string.Empty;
            normalizedUrl = _binding.FigmaUrl;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(_binding.FigmaUrl))
            {
                error = "Figma URL is empty.";
                return false;
            }

            if (!Uri.TryCreate(_binding.FigmaUrl, UriKind.Absolute, out var uri))
            {
                error = $"Invalid Figma URL: {_binding.FigmaUrl}";
                return false;
            }

            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (string.IsNullOrWhiteSpace(fileKey))
            {
                var markerIndex = Array.FindIndex(segments, x =>
                    x.Equals("file", StringComparison.OrdinalIgnoreCase) ||
                    x.Equals("design", StringComparison.OrdinalIgnoreCase));
                if (markerIndex < 0 || markerIndex + 1 >= segments.Length)
                {
                    error = "Could not extract Figma file key from URL.";
                    return false;
                }

                fileKey = segments[markerIndex + 1];
            }

            var query = ParseQuery(uri.Query);
            if (string.IsNullOrWhiteSpace(nodeId) &&
                query.TryGetValue("node-id", out var nodeToken) &&
                !string.IsNullOrWhiteSpace(nodeToken))
            {
                nodeId = nodeToken.Replace("-", ":");
            }

            if (string.IsNullOrWhiteSpace(nodeId))
            {
                error = "Root node id is missing. This component needs a frame-level node-id.";
                return false;
            }

            requestUrl = $"https://api.figma.com/v1/files/{fileKey}/nodes?ids={UnityWebRequest.EscapeURL(nodeId)}";
            normalizedUrl = EnsureNodeIdOnUrl(_binding.FigmaUrl, nodeId);
            return true;
        }

        private static async Task<string> GetJsonAsync(string requestUrl, string token, CancellationToken cancellationToken)
        {
            using (var request = UnityWebRequest.Get(requestUrl))
            {
                request.timeout = 60;
                request.SetRequestHeader("Authorization", $"Bearer {token}");
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(100, cancellationToken);
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new InvalidOperationException(
                        $"Request failed (HTTP {request.responseCode}): {request.error}");
                }

                return request.downloadHandler.text;
            }
        }

        private static List<NodeTreeElement> BuildDefaultNodeTreeElements(IList<Node> nodes)
        {
            var result = new List<NodeTreeElement>();
            var idCounter = 0;
            result.Add(new NodeTreeElement("Root", "Root", ActionType.None, null, -1, idCounter++));
            AppendNodeElementsRecursive(nodes, result, ref idCounter, 0);
            return result;
        }

        private static void AppendNodeElementsRecursive(
            IList<Node> nodes,
            ICollection<NodeTreeElement> result,
            ref int idCounter,
            int depth)
        {
            if (nodes == null)
            {
                return;
            }

            foreach (var node in nodes)
            {
                if (node == null)
                {
                    continue;
                }

                result.Add(new NodeTreeElement(node.name, node.id, ActionType.None, null, depth, idCounter++));
                if (node.children != null && node.children.Length > 0)
                {
                    AppendNodeElementsRecursive(node.children, result, ref idCounter, depth + 1);
                }
            }
        }

        private static void CollectNodeLookup(
            Node node,
            string parentNodeId,
            IDictionary<string, Node> nodeById,
            IDictionary<string, string> parentById)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.id))
            {
                return;
            }

            nodeById[node.id] = node;
            parentById[node.id] = parentNodeId ?? string.Empty;
            if (node.children == null || node.children.Length == 0)
            {
                return;
            }

            for (var i = 0; i < node.children.Length; i++)
            {
                CollectNodeLookup(node.children[i], node.id, nodeById, parentById);
            }
        }

        private static Dictionary<string, string> BuildParentLookup(
            IReadOnlyList<global::FigmaImporter.FigmaFrameSyncBinding.NodeSnapshotEntry> baselineSnapshot,
            IList<global::FigmaImporter.FigmaFrameSyncBinding.NodeSnapshotEntry> latestSnapshot)
        {
            var parentById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (baselineSnapshot != null)
            {
                for (var i = 0; i < baselineSnapshot.Count; i++)
                {
                    var entry = baselineSnapshot[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.nodeId))
                    {
                        continue;
                    }

                    parentById[entry.nodeId] = entry.parentNodeId ?? string.Empty;
                }
            }

            if (latestSnapshot != null)
            {
                for (var i = 0; i < latestSnapshot.Count; i++)
                {
                    var entry = latestSnapshot[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.nodeId))
                    {
                        continue;
                    }

                    parentById[entry.nodeId] = entry.parentNodeId ?? string.Empty;
                }
            }

            return parentById;
        }

        private static List<global::FigmaImporter.FigmaFrameSyncBinding.ChangeEntry> FilterChangesForIncrementalApply(
            IList<global::FigmaImporter.FigmaFrameSyncBinding.ChangeEntry> selectedChanges,
            IReadOnlyDictionary<string, string> parentById)
        {
            var result = new List<global::FigmaImporter.FigmaFrameSyncBinding.ChangeEntry>();
            if (selectedChanges == null || selectedChanges.Count == 0)
            {
                return result;
            }

            var selectedChangeByNodeId = new Dictionary<string, global::FigmaImporter.FigmaFrameSyncBinding.ChangeEntry>(
                StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < selectedChanges.Count; i++)
            {
                var entry = selectedChanges[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.nodeId))
                {
                    continue;
                }

                selectedChangeByNodeId[entry.nodeId] = entry;
            }

            for (var i = 0; i < selectedChanges.Count; i++)
            {
                var change = selectedChanges[i];
                if (change == null || string.IsNullOrWhiteSpace(change.nodeId))
                {
                    continue;
                }

                if (HasCoveringAncestor(change.nodeId, selectedChangeByNodeId, parentById))
                {
                    continue;
                }

                result.Add(change);
            }

            return result;
        }

        private static HashSet<string> ExpandCoveredPendingNodeIds(
            IList<global::FigmaImporter.FigmaFrameSyncBinding.ChangeEntry> pendingChanges,
            IReadOnlyDictionary<string, string> parentById,
            ISet<string> appliedNodeIds,
            ISet<string> subtreeAppliedNodeIds)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (appliedNodeIds != null)
            {
                foreach (var nodeId in appliedNodeIds)
                {
                    if (!string.IsNullOrWhiteSpace(nodeId))
                    {
                        result.Add(nodeId);
                    }
                }
            }

            if (pendingChanges == null || pendingChanges.Count == 0)
            {
                return result;
            }

            if (subtreeAppliedNodeIds == null || subtreeAppliedNodeIds.Count == 0)
            {
                return result;
            }

            for (var i = 0; i < pendingChanges.Count; i++)
            {
                var change = pendingChanges[i];
                if (change == null || string.IsNullOrWhiteSpace(change.nodeId))
                {
                    continue;
                }

                if (result.Contains(change.nodeId))
                {
                    continue;
                }

                if (HasAncestorInSet(change.nodeId, subtreeAppliedNodeIds, parentById))
                {
                    result.Add(change.nodeId);
                }
            }

            return result;
        }

        private static bool HasCoveringAncestor(
            string nodeId,
            IReadOnlyDictionary<string, global::FigmaImporter.FigmaFrameSyncBinding.ChangeEntry> selectedChangeByNodeId,
            IReadOnlyDictionary<string, string> parentById)
        {
            if (string.IsNullOrWhiteSpace(nodeId) ||
                selectedChangeByNodeId == null ||
                selectedChangeByNodeId.Count == 0 ||
                parentById == null)
            {
                return false;
            }

            var visited = 0;
            var currentNodeId = nodeId;
            while (visited++ < 4096 &&
                   parentById.TryGetValue(currentNodeId, out var parentId) &&
                   !string.IsNullOrWhiteSpace(parentId))
            {
                if (selectedChangeByNodeId.TryGetValue(parentId, out var ancestorChange))
                {
                    var ancestorType = NormalizeChangeType(ancestorChange != null ? ancestorChange.changeType : string.Empty);
                    if (string.Equals(ancestorType, "Added", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(ancestorType, "Removed", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                currentNodeId = parentId;
            }

            return false;
        }

        private static bool HasAncestorInSet(
            string nodeId,
            ISet<string> ancestorSet,
            IReadOnlyDictionary<string, string> parentById)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || ancestorSet == null || ancestorSet.Count == 0 || parentById == null)
            {
                return false;
            }

            var visited = 0;
            var currentNodeId = nodeId;
            while (visited++ < 4096 &&
                   parentById.TryGetValue(currentNodeId, out var parentId) &&
                   !string.IsNullOrWhiteSpace(parentId))
            {
                if (ancestorSet.Contains(parentId))
                {
                    return true;
                }

                currentNodeId = parentId;
            }

            return false;
        }

        private bool TryRemoveNodeGameObject(string nodeId, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                reason = "Node id is empty.";
                return false;
            }

            var targetNodeObject = TransformUtils.TryToFindPreviouslyCreatedObject(_binding.gameObject, nodeId);
            if (targetNodeObject == null)
            {
                reason = "Target node was not found in Unity hierarchy.";
                return false;
            }

            if (targetNodeObject == _binding.gameObject)
            {
                reason = "Root frame object cannot be removed by partial apply.";
                return false;
            }

            Undo.DestroyObjectImmediate(targetNodeObject);
            return true;
        }

        private static GameObject ResolveUnityParentForChange(
            string nodeId,
            string parentNodeId,
            GameObject frameRootObject,
            string frameRootNodeId)
        {
            if (frameRootObject == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(nodeId) &&
                !string.IsNullOrWhiteSpace(frameRootNodeId) &&
                string.Equals(nodeId, frameRootNodeId, StringComparison.OrdinalIgnoreCase))
            {
                return frameRootObject;
            }

            if (string.IsNullOrWhiteSpace(parentNodeId) ||
                (!string.IsNullOrWhiteSpace(frameRootNodeId) &&
                 string.Equals(parentNodeId, frameRootNodeId, StringComparison.OrdinalIgnoreCase)))
            {
                return frameRootObject;
            }

            return TransformUtils.TryToFindPreviouslyCreatedObject(frameRootObject, parentNodeId);
        }

        private static string NormalizeChangeType(string changeType)
        {
            if (string.IsNullOrWhiteSpace(changeType))
            {
                return "Changed";
            }

            return changeType.Trim();
        }

        private void RefreshIssuesFromRegistry(
            string rootNodeId,
            IList<global::FigmaImporter.FigmaFrameSyncBinding.NodeSnapshotEntry> latestSnapshot)
        {
            var registry = ImportFallbackRegistry.GetOrCreate();
            var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(rootNodeId))
            {
                nodeIds.Add(rootNodeId);
            }

            if (latestSnapshot != null)
            {
                for (var i = 0; i < latestSnapshot.Count; i++)
                {
                    var entry = latestSnapshot[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.nodeId))
                    {
                        continue;
                    }

                    nodeIds.Add(entry.nodeId);
                }
            }

            var issues = new List<global::FigmaImporter.FigmaFrameSyncBinding.IssueEntry>();
            if (registry.MissingIssues != null)
            {
                foreach (var entry in registry.MissingIssues)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.lastDetails))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(entry.nodeId) && !nodeIds.Contains(entry.nodeId))
                    {
                        continue;
                    }

                    issues.Add(new global::FigmaImporter.FigmaFrameSyncBinding.IssueEntry
                    {
                        severity = "Warning",
                        category = $"Fallback/{entry.category}",
                        message = entry.lastDetails,
                        timestampUtc = entry.lastSeenAt
                    });
                }
            }

            if (registry.SvgFallbacks != null)
            {
                foreach (var entry in registry.SvgFallbacks)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.lastReason))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(entry.nodeId) && !nodeIds.Contains(entry.nodeId))
                    {
                        continue;
                    }

                    issues.Add(new global::FigmaImporter.FigmaFrameSyncBinding.IssueEntry
                    {
                        severity = "Warning",
                        category = "Fallback/SVG",
                        message = entry.lastReason,
                        timestampUtc = entry.lastSeenAt
                    });
                }
            }

            if (registry.LastSessionMissingFonts > 0)
            {
                issues.Add(new global::FigmaImporter.FigmaFrameSyncBinding.IssueEntry
                {
                    severity = "Warning",
                    category = "Fallback/Fonts",
                    message = $"Last import session reported {registry.LastSessionMissingFonts} missing-font events.",
                    timestampUtc = DateTime.UtcNow.ToString("u")
                });
            }

            _binding.ReplaceIssues(issues);
            EditorUtility.SetDirty(_binding);
        }

        private void ReportError(string title, string message)
        {
            var safeTitle = string.IsNullOrWhiteSpace(title) ? "Figma Frame Sync" : title.Trim();
            var safeMessage = string.IsNullOrWhiteSpace(message) ? "Unknown error." : message.Trim();
            var fullMessage = $"[FigmaImporter] {safeTitle}: {safeMessage}";
            Debug.LogError(fullMessage, _binding);
            if (_binding != null)
            {
                Undo.RecordObject(_binding, safeTitle);
                _binding.SetError(safeMessage);
                _binding.AddIssue("Error", "Frame Sync", safeMessage);
                EditorUtility.SetDirty(_binding);
            }
        }

        private void ReportCanceled(string title, string message)
        {
            var safeTitle = string.IsNullOrWhiteSpace(title) ? "Figma Frame Sync" : title.Trim();
            var safeMessage = string.IsNullOrWhiteSpace(message) ? "Operation canceled." : message.Trim();
            Debug.LogWarning($"[FigmaImporter] {safeTitle}: {safeMessage}", _binding);
            if (_binding != null)
            {
                Undo.RecordObject(_binding, safeTitle);
                _binding.SetError(string.Empty);
                _binding.SetStatus("Canceled");
                _binding.AddIssue("Info", "Frame Sync", safeMessage);
                EditorUtility.SetDirty(_binding);
            }
        }

        private void ReportBlocked(string title, string message)
        {
            var safeTitle = string.IsNullOrWhiteSpace(title) ? "Figma Frame Sync" : title.Trim();
            var safeMessage = string.IsNullOrWhiteSpace(message) ? "Action blocked." : message.Trim();
            Debug.LogWarning($"[FigmaImporter] {safeTitle}: {safeMessage}", _binding);
            if (_binding != null)
            {
                Undo.RecordObject(_binding, safeTitle);
                _binding.SetError(string.Empty);
                _binding.SetStatus("Blocked");
                _binding.AddIssue("Info", "Frame Sync", safeMessage);
                EditorUtility.SetDirty(_binding);
            }
        }

        private void CreateOperationCts()
        {
            CancelActiveOperation();
            _operationCts = new CancellationTokenSource();
        }

        private void CancelActiveOperation()
        {
            var cts = _operationCts;
            if (cts == null)
            {
                return;
            }

            _operationCts = null;

            try
            {
                if (!cts.IsCancellationRequested)
                {
                    cts.Cancel();
                }
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                cts.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static MessageType ResolveStatusMessageType(string status, string error)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                return MessageType.Error;
            }

            if (string.IsNullOrWhiteSpace(status))
            {
                return MessageType.None;
            }

            if (status.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return MessageType.Error;
            }

            if (status.IndexOf("update", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return MessageType.Warning;
            }

            return MessageType.Info;
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(query))
            {
                return map;
            }

            var pairs = query.TrimStart('?').Split('&');
            for (var i = 0; i < pairs.Length; i++)
            {
                var pair = pairs[i];
                if (string.IsNullOrWhiteSpace(pair))
                {
                    continue;
                }

                var separator = pair.IndexOf('=');
                if (separator < 0)
                {
                    map[Uri.UnescapeDataString(pair)] = string.Empty;
                    continue;
                }

                var key = Uri.UnescapeDataString(pair.Substring(0, separator));
                var value = Uri.UnescapeDataString(pair.Substring(separator + 1));
                map[key] = value;
            }

            return map;
        }

        private static string EnsureNodeIdOnUrl(string url, string nodeId)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return url;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return string.Empty;
            }

            var query = ParseQuery(uri.Query);
            query["node-id"] = nodeId.Replace(":", "-");
            var queryItems = query.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value ?? string.Empty)}");
            var queryString = string.Join("&", queryItems);
            var builder = new UriBuilder(uri)
            {
                Query = queryString
            };
            return builder.Uri.ToString();
        }
    }
}
