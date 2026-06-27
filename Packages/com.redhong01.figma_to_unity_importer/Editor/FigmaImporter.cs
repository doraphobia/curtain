using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Threading;
using FigmaImporter.Editor.EditorTree;
using FigmaImporter.Editor.EditorTree.TreeData;
using TMPro;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Networking;
using System.Linq;
using Random = UnityEngine.Random;

namespace FigmaImporter.Editor
{
    public class FigmaImporter : EditorWindow
    {
        internal const string WindowTitle = "Figma Importer";

        [MenuItem(FigmaImporterMenuPaths.Importer.OpenWindow)]
        static void Init()
        {
            OpenOrCreate(focus: true);
        }

        private static FigmaImporterSettings _settings = null;
        private static GameObject _rootObject;
        private static List<Node> _nodes = null;
        private MultiColumnLayout _treeView;
        private string _lastClickedNode = String.Empty;

        private static string _fileName;
        private static string _nodeId;
        private float _scale = 1f;
        private bool _isGenerating;
        private bool _isPaused;
        private bool _isRateLimited;
        private CancellationTokenSource _generationCts;
        private readonly SemaphoreSlim _requestSemaphore = new SemaphoreSlim(1, 1);
        private const int MaxRequestRetries = 3;
        private const int RetryDelayBaseMs = 600;
        private const int SequentialRequestGapMs = 220;
        private const int RequestTimeoutSeconds = 60;
        private const int RequestStallThresholdSeconds = 20;
        private const int RequestStallThresholdSecondsWindows = 45;
        private const int GenerationStallThresholdSeconds = 45;
        private const float RequestProgressEpsilon = 0.0005f;
        private const string VectorGraphicsPackageName = "com.unity.vectorgraphics";
        private const string VectorGraphicsPackagePinnedVersion = "com.unity.vectorgraphics@2.0.0-preview.25";
        private const int PackageRequestTimeoutSeconds = 90;
        private bool _generationStallHandled;
        private bool _cancelRequested;
        private bool _cancelRequestedByUser;
        private FigmaTextRenderingDriver _textRenderingDriver;
        private UnityWebRequest _activeRequest;
        private string _activeRequestStage;
        private string _generationStatusText = "Idle";
        private MessageType _generationStatusType = MessageType.None;
        private int _generationRunIdCounter;
        private int _activeGenerationRunId;
        private string _activeGenerateChainId = string.Empty;

        Dictionary<string, Texture2D> _texturesCache = new Dictionary<string, Texture2D>();

        public float Scale => _scale;
        internal string CurrentFigmaUrl => _settings != null ? _settings.Url : string.Empty;
        internal string CurrentFileKey => _fileName;
        internal static string MaskTokenForDisplay(string token) => MaskToken(token);
        internal GameObject SelectedRootObject
        {
            get => _rootObject;
            set => _rootObject = value;
        }
        internal int LoadedNodeCount => _nodes != null ? _nodes.Count : 0;
        internal bool HasLoadedNodeData => _nodes != null && _nodes.Count > 0;
        internal bool IsGenerationRunning => _isGenerating;
        internal string GenerationStatusText => _generationStatusText;
        internal MessageType GenerationStatusType => _generationStatusType;

        internal static FigmaImporter OpenOrCreate(bool focus)
        {
            var window = GetWindow<FigmaImporter>(WindowTitle);
            window.Show();
            if (focus)
            {
                window.Focus();
            }

            return window;
        }

        internal static FigmaImporter FindOpenInstance()
        {
            return Resources.FindObjectsOfTypeAll<FigmaImporter>().FirstOrDefault();
        }

        internal static GameObject GetSelectedRootObjectForFlow()
        {
            return _rootObject;
        }

        internal static void SetSelectedRootObjectForFlow(GameObject rootObject)
        {
            _rootObject = rootObject;
        }

        internal static int GetLoadedNodeCountForFlow()
        {
            return _nodes != null ? _nodes.Count : 0;
        }

        internal static bool HasLoadedNodeDataForFlow()
        {
            return _nodes != null && _nodes.Count > 0;
        }

        internal static string GetGenerationStatusTextForFlow()
        {
            var instance = FindOpenInstance();
            return instance != null ? instance._generationStatusText : "Idle";
        }

        internal static MessageType GetGenerationStatusTypeForFlow()
        {
            var instance = FindOpenInstance();
            return instance != null ? instance._generationStatusType : MessageType.None;
        }

        internal static bool IsGenerationRunningForFlow()
        {
            var instance = FindOpenInstance();
            return instance != null && instance._isGenerating;
        }

        internal static void OpenOAuthUrlFromFlow()
        {
            OpenOrCreate(focus: false).OpenOauthUrl();
        }

        internal static string RequestOAuthTokenFromFlow()
        {
            return OpenOrCreate(focus: false).RequestOAuthTokenForFlow();
        }

        internal static void FetchNodeDataFromFlow()
        {
            OpenOrCreate(focus: false).FetchNodeDataForFlow();
        }

        internal static void GenerateNodesFromFlow()
        {
            OpenOrCreate(focus: false).GenerateNodesForFlow();
        }

        internal string RequestOAuthTokenForFlow()
        {
            if (_settings == null)
            {
                _settings = FigmaImporterSettings.GetInstance();
            }

            _settings.Token = GetOAuthToken();
            Repaint();
            return _settings.Token;
        }

        internal void FetchNodeDataForFlow()
        {
            if (_settings == null)
            {
                _settings = FigmaImporterSettings.GetInstance();
            }

            if (_isGenerating)
            {
                return;
            }

            var apiUrl = ConvertToApiUrl(_settings.Url);
            if (string.IsNullOrEmpty(apiUrl))
            {
                return;
            }

            SetGenerationStatus("Loading node data...", MessageType.Info);
            RunBackgroundTask(GetNodes(apiUrl, origin: "Flow Get Node Data"), "Flow Get Node Data");
            Repaint();
        }

        internal void GenerateNodesForFlow()
        {
            if (_settings == null)
            {
                _settings = FigmaImporterSettings.GetInstance();
            }

            if (_isGenerating)
            {
                return;
            }

            TriggerGenerateNodes();
            Repaint();
        }

        internal void ApplyTextRenderingPipeline(TextMeshProUGUI tmp, Node node)
        {
            if (tmp == null || node == null)
            {
                return;
            }

            if (_settings == null)
            {
                _settings = FigmaImporterSettings.GetInstance();
            }

            if (_textRenderingDriver == null)
            {
                _textRenderingDriver = FigmaTextRenderingDriver.CreateDefault();
            }

            var context = new FigmaTextRenderContext(_scale, _settings);
            _textRenderingDriver.Apply(tmp, node, context);
        }

        void OnGUI()
        {
            RecoverStaleGenerationStateIfNeeded();

            if (_settings == null)
                _settings = FigmaImporterSettings.GetInstance();

            if (GUILayout.Button("OpenOauthUrl"))
            {
                OpenOauthUrl();
            }

            _settings.ClientCode = EditorGUILayout.TextField("ClientCode", _settings.ClientCode);
            _settings.State = EditorGUILayout.TextField("State", _settings.State);

            if (GUILayout.Button("GetToken"))
            {
                _settings.Token = GetOAuthToken();
            }

            GUILayout.TextArea("Token (masked): " + MaskToken(_settings.Token));
            _settings.Url = EditorGUILayout.TextField("Url", _settings.Url);
            _settings.RendersPath = EditorGUILayout.TextField("RendersPath", _settings.RendersPath);
            if (GUILayout.Button("Initialize Dependencies"))
            {
                FigmaPackageBootstrapper.InitializeDependencies(force: true);
            }

            DrawRootObjectSelection();

            _scale = EditorGUILayout.Slider("Scale", _scale, 0.01f, 4f);
            DrawTypographyModuleSettings();

            var redStyle = new GUIStyle(EditorStyles.label);

            redStyle.normal.textColor = UnityEngine.Color.yellow;
            EditorGUILayout.LabelField(
                "Preview on the right side loaded via Figma API. It doesn't represent the final result!!!!", redStyle);

            using (new EditorGUI.DisabledScope(_isGenerating))
            {
                if (GUILayout.Button("Fetch Figma Node Data"))
                {
                    string apiUrl = ConvertToApiUrl(_settings.Url);
                    if (string.IsNullOrEmpty(apiUrl))
                    {
                        return;
                    }
                    SetGenerationStatus("Loading node data...", MessageType.Info);
                    RunBackgroundTask(GetNodes(apiUrl, origin: "Manual Get Node Data"), "Manual Get Node Data");
                }
            }

            using (new EditorGUI.DisabledScope(_nodes == null || _isGenerating))
            {
                if (GUILayout.Button(_isGenerating ? "Apply Selected Import Modes (Running...)" : "Apply Selected Import Modes"))
                {
                    TriggerGenerateNodes();
                }
            }

            EditorGUILayout.HelpBox($"Generation Status: {_generationStatusText}", _generationStatusType);

            if (_isGenerating)
            {
                var runStateInfo = _cancelRequested
                    ? "Cancel requested. Waiting for current task to stop..."
                    : _isPaused
                        ? "Generate is currently paused. Use Continue or Cancel."
                        : "Generate is currently running. Use Pause or Cancel at the bottom.";
                EditorGUILayout.HelpBox(runStateInfo, MessageType.Info);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(_isPaused ? "Continue Generate" : "Pause Generate"))
                {
                    FigmaNodesProgressInfo.TogglePauseRequest();
                }

                if (GUILayout.Button("Cancel Generate"))
                {
                    FigmaNodesProgressInfo.RequestCancel();
                }

                EditorGUILayout.EndHorizontal();
            }

            if (_nodes != null)
            {
                DrawAdditionalButtons();
                DrawNodeTree();
                DrawPreview();
            }
        }

        private void DrawAdditionalButtons()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Auto", "Re-run the default analyzer. Text and containers stay native; leaf graphics prefer SVG when possible, otherwise PNG.")))
                SwitchNodesToAuto();
            if (GUILayout.Button(new GUIContent("To PNG", "Set leaf graphics to raster PNG render while keeping text and containers native.")))
                SwitchNodesToPng();
            if (GUILayout.Button(new GUIContent("To Native", "Set all nodes to Native Generate so Unity rebuilds text, fills, and child hierarchy.")))
                SwitchNodesToNativeGenerate();
            if (GUILayout.Button(new GUIContent("To Transform", "Set all nodes to Transform Only so Unity only rebuilds the RectTransform hierarchy.")))
                SwitchNodesToTransform();
#if VECTOR_GRAHICS_IMPORTED
            if (GUILayout.Button(new GUIContent("To SVG", "Set leaf graphics to SVG render when possible and keep masks or collapsed stroke lines on PNG.")))
                SwitchNodesToSvg();
#endif
            if (GUILayout.Button("Fallback Resolver"))
                FigmaDiagnosticsHubWindow.OpenFallbackPage();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "Import mode shortcuts: Auto = default mixed import analysis. To PNG = raster sprite output for leaf graphics. To Native = editable Unity UI generation. To Transform = RectTransform hierarchy only. To SVG = vector sprite output where Unity Vector Graphics can support it.",
                MessageType.Info);
        }

        private void DrawRootObjectSelection()
        {
            _rootObject =
                (GameObject)EditorGUILayout.ObjectField("Root Object", _rootObject, typeof(GameObject), true);

            if (_settings == null)
            {
                return;
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUIUtility.labelWidth);
            _settings.RootObjectPickerCanvasOnly = EditorGUILayout.ToggleLeft(
                "Filter Canvas Related Objects",
                _settings.RootObjectPickerCanvasOnly);

            if (GUILayout.Button("Pick Root Object", GUILayout.Width(140f)))
            {
                FigmaRootObjectPickerWindow.Open(
                    _rootObject,
                    _settings.RootObjectPickerCanvasOnly,
                    selected =>
                    {
                        _rootObject = selected;
                        Repaint();
                    },
                    canvasOnly =>
                    {
                        _settings.RootObjectPickerCanvasOnly = canvasOnly;
                        Repaint();
                    });
            }

            if (GUILayout.Button("None", GUILayout.Width(60f)))
            {
                _rootObject = null;
            }

            EditorGUILayout.EndHorizontal();

            if (_settings.RootObjectPickerCanvasOnly &&
                _rootObject != null &&
                !FigmaRootObjectFilterUtils.IsCanvasRelated(_rootObject))
            {
                EditorGUILayout.HelpBox(
                    "Current Root Object is outside Canvas hierarchy. Canvas filter is ON in picker.",
                    MessageType.Warning);
            }

            if (_settings.RootObjectPickerCanvasOnly)
            {
                EditorGUILayout.HelpBox(
                    "Use 'Pick Root Object' for filtered selection. It shows only Canvas objects or their children.",
                    MessageType.Info);
            }
        }

        private void DrawTypographyModuleSettings()
        {
            if (_settings == null)
            {
                return;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Typography Modules", EditorStyles.boldLabel);

            _settings.EnableTypographyAdapter = EditorGUILayout.ToggleLeft(
                "Enable Figma -> TMP Adapter",
                _settings.EnableTypographyAdapter);

            using (new EditorGUI.DisabledScope(!_settings.EnableTypographyAdapter))
            {
                _settings.EnableTypographyScaleCorrection = EditorGUILayout.ToggleLeft(
                    "Enable Auto-size Metrics Correction",
                    _settings.EnableTypographyScaleCorrection);
                _settings.EscapeTypographyInputText = EditorGUILayout.ToggleLeft(
                    "Escape Input Text For Rich Tags",
                    _settings.EscapeTypographyInputText);
                _settings.EnableTypographyDebugLog = EditorGUILayout.ToggleLeft(
                    "Enable Typography Debug Log",
                    _settings.EnableTypographyDebugLog);
            }
        }

        private void SwitchNodesToSvg()
        {
            var nodesTreeElements = _treeView.TreeView.treeModel.Data;
            NodesAnalyzer.AnalyzeSVGMode(_nodes, nodesTreeElements);
        }

        private void SwitchNodesToTransform()
        {
            var nodesTreeElements = _treeView.TreeView.treeModel.Data;
            NodesAnalyzer.AnalyzeTransformMode(_nodes, nodesTreeElements);
        }

        private void SwitchNodesToNativeGenerate()
        {
            var nodesTreeElements = _treeView.TreeView.treeModel.Data;
            NodesAnalyzer.AnalyzeGenerateMode(_nodes, nodesTreeElements);
        }

        private void SwitchNodesToPng()
        {
            var nodesTreeElements = _treeView.TreeView.treeModel.Data;
            NodesAnalyzer.AnalyzePngMode(_nodes, nodesTreeElements);
        }

        private void SwitchNodesToAuto()
        {
            var nodesTreeElements = _treeView.TreeView.treeModel.Data;
            NodesAnalyzer.AnalyzeRenderMode(_nodes, nodesTreeElements);
        }

        private void DrawPreview()
        {
            var lastRect = GUILayoutUtility.GetLastRect();
            var widthMax = position.width / 2f;
            var heightMax = this.position.height - lastRect.yMax - 50;
            var height = heightMax;
            var width = widthMax;
            _texturesCache.TryGetValue(_lastClickedNode, out var lastLoadedPreview);
            if (lastLoadedPreview != null)
            {
                CalculatePreviewSize(lastLoadedPreview, widthMax, heightMax, out width, out height);
            }

            var previewRect = new Rect(position.width / 2f, lastRect.yMax + 20, width, height);
            if (lastLoadedPreview != null)
                GUI.DrawTexture(previewRect, lastLoadedPreview);
        }

        private void CalculatePreviewSize(Texture2D lastLoadedPreview, float widthMax, float heightMax, out float width,
            out float height)
        {
            if (lastLoadedPreview.width < widthMax && lastLoadedPreview.height < heightMax)
            {
                width = lastLoadedPreview.width;
                height = lastLoadedPreview.height;
            }
            else
            {
                width = widthMax;
                height = widthMax * lastLoadedPreview.height / lastLoadedPreview.width;
                if (height > heightMax)
                {
                    height = heightMax;
                    width = heightMax * lastLoadedPreview.width / lastLoadedPreview.height;
                }
            }
        }

        private void ClearLoadedData()
        {
            if (_treeView != null && _treeView.TreeView != null)
                _treeView.TreeView.OnItemClick -= ItemClicked;
            _treeView = null;
            _nodes = null;
            foreach (var texture in _texturesCache)
            {
                DestroyImmediate(texture.Value);
            }

            _texturesCache.Clear();
        }

        private void OnDestroy()
        {
            DisposeGenerationCts(cancel: true, dispose: false);
            _isGenerating = false;
            _isPaused = false;
            FigmaNodesProgressInfo.ClearGenerationControls();
            FigmaNodesProgressInfo.HideProgress();
            if (!string.IsNullOrWhiteSpace(_activeGenerateChainId))
            {
                FigmaImporterEventFlow.End(
                    "GenerateNodes",
                    _activeGenerateChainId,
                    "WindowDestroyed",
                    "Importer window destroyed while run was active");
            }
            ClearLoadedData();
            _cancelRequestedByUser = false;
            _generationStatusText = "Idle";
            _generationStatusType = MessageType.None;
            _activeGenerateChainId = string.Empty;
        }

        private void DisposeGenerationCts(bool cancel, bool dispose = true, CancellationTokenSource expected = null)
        {
            CancellationTokenSource cts;
            if (expected == null)
            {
                cts = Interlocked.Exchange(ref _generationCts, null);
            }
            else
            {
                var current = Interlocked.CompareExchange(ref _generationCts, null, expected);
                if (!ReferenceEquals(current, expected))
                {
                    return;
                }

                cts = current;
            }

            DisposeCancellationTokenSource(cts, cancel, dispose);
        }

        private static void DisposeCancellationTokenSource(CancellationTokenSource cts, bool cancel, bool dispose = true)
        {
            if (cts == null)
            {
                return;
            }

            if (cancel)
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            if (dispose)
            {
                try
                {
                    cts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        private void DrawNodeTree()
        {
            bool justCreated = false;
            if (_treeView == null)
            {
                _treeView = new MultiColumnLayout();
                justCreated = true;
            }

            var lastRect = GUILayoutUtility.GetLastRect();
            var width = position.width / 2f;
            var treeRect = new Rect(0, lastRect.yMax + 20, width, this.position.height - lastRect.yMax - 50);
            _treeView.OnGUI(treeRect, _nodes);
            var nodesTreeElements = _treeView.TreeView.treeModel.Data;
            if (justCreated)
            {
                _treeView.TreeView.OnItemClick += ItemClicked;
                NodesAnalyzer.AnalyzeRenderMode(_nodes, nodesTreeElements);
                LoadInitialRender(nodesTreeElements);
            }

            NodesAnalyzer.CheckActions(_nodes, nodesTreeElements);
        }

        private async void LoadInitialRender(IList<NodeTreeElement> nodesTreeElements)
        {
            if (nodesTreeElements == null || nodesTreeElements.Count == 0)
                return;
            if (_isRateLimited)
                return;

            // Avoid sending dozens of parallel preview requests that trigger Figma API rate limits.
            string firstRenderableNodeId = null;
            foreach (var element in nodesTreeElements)
            {
                if (IsLikelyFigmaNodeId(element?.figmaId))
                {
                    firstRenderableNodeId = element.figmaId;
                    break;
                }
            }

            if (string.IsNullOrEmpty(firstRenderableNodeId))
            {
                return;
            }

            _lastClickedNode = firstRenderableNodeId;
            await GetImage(_lastClickedNode, false);
            Repaint();
        }

        private async void ItemClicked(string obj)
        {
            Debug.Log($"[FigmaImporter] {obj} clicked");
            if (!IsLikelyFigmaNodeId(obj))
            {
                return;
            }
            _lastClickedNode = obj;
            if (!_texturesCache.TryGetValue(obj, out var tex))
            {
                await GetImage(obj, false);
            }

            Repaint();
        }

        private void TriggerGenerateNodes()
        {
            string apiUrl = ConvertToApiUrl(_settings.Url);
            if (string.IsNullOrEmpty(apiUrl))
            {
                return;
            }

            SetGenerationStatus("Running...", MessageType.Info);
            RunBackgroundTask(GetFileAsync(apiUrl, "Generate Nodes"), "Generate Nodes");
        }

        private async void RunBackgroundTask(Task task, string context)
        {
            if (task == null)
            {
                return;
            }

            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                SetGenerationStatus("Canceled.", MessageType.Warning);
                Debug.Log($"[FigmaImporter] {context} canceled.");
            }
            catch (Exception e)
            {
                SetGenerationStatus($"Failed: {e.Message}", MessageType.Error);
                Debug.LogException(e);
            }
            finally
            {
                Repaint();
            }
        }

        private void RecoverStaleGenerationStateIfNeeded()
        {
            if (!_isGenerating)
            {
                return;
            }

            // If generation flag is true but no active token/request exists, state is stale.
            if (_generationCts == null && _activeRequest == null)
            {
                ForceResetGenerationState("Recovered stale generation state.");
                return;
            }

            var secondsSinceLastActivity =
                EditorApplication.timeSinceStartup - FigmaNodesProgressInfo.LastProgressUpdateTime;
            var staleThresholdSeconds = GenerationStallThresholdSeconds * 2;
            if (secondsSinceLastActivity >= staleThresholdSeconds)
            {
                ForceResetGenerationState(
                    $"Recovered stuck generation state after {Math.Round(secondsSinceLastActivity, 1)}s without activity.");
            }
        }

        private void ForceResetGenerationState(string reason)
        {
            FigmaImporterEventFlow.Step(
                "GenerateNodes",
                _activeGenerateChainId,
                "ForceResetGenerationState",
                reason,
                allowDuplicate: true);
            FigmaImporterEventFlow.End("GenerateNodes", _activeGenerateChainId, "ForceReset", reason);
            _activeGenerateChainId = string.Empty;
            _cancelRequestedByUser = false;
            AbortActiveRequest("force reset requested");
            CleanupGenerationRuntimeState(cancelCts: true, expectedRunCts: null);
            ImportFallbackRegistry.EndGenerationSession();
            SetGenerationStatus("Canceled (state reset).", MessageType.Warning);
            Debug.LogWarning($"[FigmaImporter] {reason}");
            Repaint();
        }

        public async Task GetNodes(
            string url,
            CancellationToken cancellationToken = default,
            bool resetControlFlags = true,
            string origin = "Get Node Data")
        {
            var flowChainId = FigmaImporterEventFlow.Start(
                "GetNodes",
                origin,
                $"resetControlFlags={resetControlFlags}");
            var flowResult = "Completed";
            var flowDetails = string.Empty;

            try
            {
                if (resetControlFlags)
                {
                    // Standalone "Get Node Data" should not inherit stale cancel/pause state from previous generation.
                    _cancelRequested = false;
                    FigmaNodesProgressInfo.SetCancelRequested(false);
                    FigmaNodesProgressInfo.SetPauseRequested(false);
                    FigmaImporterEventFlow.Step("GetNodes", flowChainId, "ResetControlFlags");
                }

                ClearLoadedData();
                _isRateLimited = false;
                FigmaNodesProgressInfo.NodesCount = 0;
                FigmaNodesProgressInfo.CurrentNode = 0;
                FigmaNodesProgressInfo.CurrentTitle = "Loading node data";
                FigmaImporterEventFlow.Step("GetNodes", flowChainId, "RequestNodeInfo");
                _nodes = await GetNodeInfo(url, cancellationToken);
                if (_nodes == null)
                {
                    throw new InvalidOperationException("Failed to load node data from Figma.");
                }

                if (_nodes.Count == 0)
                {
                    throw new InvalidOperationException("Figma response did not contain any parsable nodes.");
                }

                flowDetails = $"nodes={_nodes?.Count ?? 0}";
                FigmaImporterEventFlow.Step("GetNodes", flowChainId, "NodeInfoLoaded", flowDetails);
            }
            catch (OperationCanceledException)
            {
                flowResult = "Canceled";
                flowDetails = "Operation canceled";
                throw;
            }
            catch (Exception e)
            {
                flowResult = "Failed";
                flowDetails = e.Message;
                throw;
            }
            finally
            {
                FigmaNodesProgressInfo.HideProgress();
                FigmaImporterEventFlow.End("GetNodes", flowChainId, flowResult, flowDetails);
            }
        }

        private string ConvertToApiUrl(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                Debug.LogError("[FigmaImporter] Figma URL is empty.");
                return string.Empty;
            }

            if (!Uri.TryCreate(s, UriKind.Absolute, out var uri))
            {
                Debug.LogError($"[FigmaImporter] Invalid Figma URL: {s}");
                return string.Empty;
            }

            var pathParts = uri.AbsolutePath.Trim('/').Split('/');
            var fileOrDesignIndex = Array.FindIndex(pathParts, x =>
                x.Equals("file", StringComparison.OrdinalIgnoreCase) ||
                x.Equals("design", StringComparison.OrdinalIgnoreCase));
            if (fileOrDesignIndex < 0 || fileOrDesignIndex + 1 >= pathParts.Length)
            {
                Debug.LogError($"[FigmaImporter] Could not extract file key from URL: {s}");
                return string.Empty;
            }

            _fileName = pathParts[fileOrDesignIndex + 1];
            var query = ParseQuery(uri.Query);
            if (!query.TryGetValue("node-id", out var nodeId) || string.IsNullOrWhiteSpace(nodeId))
            {
                return $"https://api.figma.com/v1/files/{_fileName}";
            }

            _nodeId = nodeId.Replace("-", ":");
            return $"https://api.figma.com/v1/files/{_fileName}/nodes?ids={UnityWebRequest.EscapeURL(_nodeId)}";
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

        private const string ApplicationKey = "msRpeIqxmc8a7a6U0Z4Jg6";
        private const string RedirectURI = "https://manakhovn.github.io/figmaImporter";

        private const string OAuthUrl =
            "https://www.figma.com/oauth?client_id={0}&redirect_uri={1}&scope=file_content:read&state={2}&response_type=code";

        public void OpenOauthUrl()
        {
            var state = Random.Range(0, Int32.MaxValue);
            string formattedOauthUrl = String.Format(OAuthUrl, ApplicationKey, RedirectURI, state.ToString());
            Application.OpenURL(formattedOauthUrl);
        }

        private const string ClientSecret = "VlyvMwuA4aVOm4dxcJgOvxbdWsmOJE";

        private const string AuthUrl = "https://api.figma.com/v1/oauth/token";

        private string GetOAuthToken()
        {
            WWWForm form = new WWWForm();
            
            form.AddField("redirect_uri", RedirectURI);
            form.AddField("code", _settings.ClientCode);
            form.AddField("grant_type", "authorization_code");
            using (UnityWebRequest www = UnityWebRequest.Post(AuthUrl, form))
            {
                var encodedClientData =
                    Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{ApplicationKey}:{ClientSecret}"));
                www.SetRequestHeader("Authorization", $"Basic {encodedClientData}");
                www.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
                _ = www.SendWebRequest();

                while (!www.isDone)
                {
                }

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.Log(www.error);
                }
                else
                {
                    var result = www.downloadHandler.text;
                    Debug.Log("[FigmaImporter] OAuth token received.");
                    return JsonUtility.FromJson<AuthResult>(result).access_token;
                }
            }

            return "";
        }

        internal async Task WaitWhilePausedAsync(CancellationToken cancellationToken)
        {
            SyncControlRequests();
            while (_isGenerating)
            {
                ThrowIfStopRequested(cancellationToken);
                if (!_isPaused)
                {
                    break;
                }

                FigmaNodesProgressInfo.CurrentInfo = "Node generation paused";
                FigmaNodesProgressInfo.ShowProgress(0f);
                await Task.Delay(100, cancellationToken);
                SyncControlRequests();
            }
        }

        private async Task GetFileAsync(string fileUrl, string triggerOrigin = "Generate Nodes")
        {
            if (_isGenerating)
            {
                Debug.LogWarning("[FigmaImporter] Node generation is already in progress.");
                return;
            }

            var flowChainId = FigmaImporterEventFlow.Start(
                "GenerateNodes",
                string.IsNullOrWhiteSpace(triggerOrigin) ? "Generate Nodes" : triggerOrigin,
                $"url={TrimForLog(fileUrl)}");
            _activeGenerateChainId = flowChainId;
            var runId = Interlocked.Increment(ref _generationRunIdCounter);
            _activeGenerationRunId = runId;
            DisposeGenerationCts(cancel: false);
            _generationCts = new CancellationTokenSource();
            var runCts = _generationCts;
            var flowResult = "Completed";
            var flowDetails = $"runId={runId}";
            FigmaNodesProgressInfo.ClearGenerationControls();
            _isGenerating = true;
            _isPaused = false;
            _isRateLimited = false;
            _generationStallHandled = false;
            _cancelRequested = false;
            _cancelRequestedByUser = false;
            var completedNormally = false;
            var canceledByUser = false;
            FigmaImporterEventFlow.Step("GenerateNodes", flowChainId, "RunCreated", $"runId={runId}");
            FigmaNodesProgressInfo.SetGenerationControls(
                () => _isPaused,
                TogglePauseGeneration,
                CancelGeneration);
            FigmaNodesProgressInfo.SetPauseRequested(false);
            FigmaNodesProgressInfo.SetCancelRequested(false);
            FigmaNodesProgressInfo.MarkActivity("Starting generation");
            EditorApplication.update -= MonitorGenerationStall;
            EditorApplication.update += MonitorGenerationStall;
            FigmaImporterEventFlow.Step("GenerateNodes", flowChainId, "GenerationControlsBound");

            try
            {
                FigmaImporterEventFlow.Step("GenerateNodes", flowChainId, "GetFileInternalStarted");
                await GetFileInternal(fileUrl, runCts.Token);
                var canceledAfterRun = _cancelRequested || _cancelRequestedByUser;
                if (canceledAfterRun)
                {
                    completedNormally = false;
                    canceledByUser = _cancelRequestedByUser;
                    flowResult = "Canceled";
                    flowDetails = canceledByUser
                        ? $"runId={runId}; canceledByUser=true"
                        : $"runId={runId}; canceledByControlState=true";
                }
                else
                {
                    completedNormally = true;
                    flowDetails = $"runId={runId}; completedNormally=true";
                }
            }
            catch (OperationCanceledException)
            {
                canceledByUser = _cancelRequestedByUser;
                SetGenerationStatus("Canceled.", MessageType.Warning);
                Debug.Log("[FigmaImporter] Node generation canceled.");
                flowResult = "Canceled";
                flowDetails = canceledByUser
                    ? $"runId={runId}; canceledByUser=true"
                    : $"runId={runId}; canceledByToken=true";
            }
            catch (Exception e)
            {
                SetGenerationStatus($"Failed: {e.Message}", MessageType.Error);
                Debug.LogException(e);
                flowResult = "Failed";
                flowDetails = $"runId={runId}; error={e.Message}";
            }
            finally
            {
                var isLatestRun = _activeGenerationRunId == runId;
                if (isLatestRun)
                {
                    ImportFallbackRegistry.EndGenerationSession();
                    var fallbackRegistry = ImportFallbackRegistry.GetOrCreate();
                    if (fallbackRegistry.LastSessionMissingFonts > 0 ||
                        fallbackRegistry.LastSessionSvgFallbacks > 0 ||
                        fallbackRegistry.LastSessionMissingIssues > 0)
                    {
                        Debug.LogWarning(
                            $"[FigmaImporter] Missing items detected in this run -> Fonts: {fallbackRegistry.LastSessionMissingFonts}, SVG: {fallbackRegistry.LastSessionSvgFallbacks}, SVG->PNG: {fallbackRegistry.LastSessionSvgToPngFallbacks}, Other: {fallbackRegistry.LastSessionMissingIssues}. Open {FigmaImporterMenuPaths.Diagnostics.FallbackResolver} to auto/manual resolve.");
                    }

                    CleanupGenerationRuntimeState(cancelCts: false, expectedRunCts: runCts);
                    if (completedNormally)
                    {
                        SetGenerationStatus("Completed.", MessageType.Info);
                    }
                    else if (string.Equals(flowResult, "Canceled", StringComparison.Ordinal))
                    {
                        SetGenerationStatus("Canceled.", MessageType.Warning);
                    }

                    FigmaImporterEventFlow.End("GenerateNodes", flowChainId, flowResult, flowDetails);
                    _activeGenerateChainId = string.Empty;
                    Repaint();
                }
                else
                {
                    DisposeCancellationTokenSource(runCts, cancel: false);
                    FigmaImporterEventFlow.End("GenerateNodes", flowChainId, "Superseded", $"runId={runId}");
                }
            }
        }

        private async Task GetFileInternal(string fileUrl, CancellationToken cancellationToken)
        {
            if (_rootObject == null)
            {
                throw new InvalidOperationException(
                    "[FigmaImporter] Root object is null. Please add reference to a Canvas or previous version of the object.");
            }

            ImportFallbackRegistry.BeginGenerationSession("Generate nodes");

            var repairedFontAssets = FontAssetResolver.RepairImportedFontAssets();
            var repairedTextComponents = TMPUtils.RepairBrokenFontsInOpenScenes();
            if (repairedFontAssets > 0 || repairedTextComponents > 0)
            {
                Debug.Log(
                    $"[FigmaImporter] Preflight font repair completed. Font assets repaired: {repairedFontAssets}, scene text components repaired: {repairedTextComponents}.");
            }

            if (_nodes == null)
            {
                FigmaNodesProgressInfo.CurrentNode = FigmaNodesProgressInfo.NodesCount = 0;
                FigmaNodesProgressInfo.CurrentTitle = "Loading nodes info";
                await GetNodes(
                    fileUrl,
                    cancellationToken,
                    resetControlFlags: false,
                    origin: "Generate Nodes preflight");
                if (_nodes == null || _nodes.Count == 0)
                {
                    throw new InvalidOperationException("Node generation aborted because no Figma nodes were loaded.");
                }
            }

            await FigmaPackageBootstrapper.EnsureDependenciesInstalledForImportAsync();
            await EnsureVectorGraphicsInstalledIfNeeded(cancellationToken);

            var nodeTreeElements = ResolveNodeTreeElementsForGeneration();
            if (nodeTreeElements == null)
            {
                throw new InvalidOperationException("Node tree is not initialized. Click 'Get Node Data' first.");
            }

            FigmaNodesProgressInfo.CurrentNode = 0;
            FigmaNodesProgressInfo.NodesCount = nodeTreeElements.Count;
            FigmaNodeGenerator generator = new FigmaNodeGenerator(this, cancellationToken);
            foreach (var node in _nodes)
            {
                if (_isRateLimited)
                {
                    throw new InvalidOperationException(
                        "Node generation stopped due to Figma API rate limit (HTTP 429). Please wait and retry.");
                }
                ThrowIfStopRequested(cancellationToken);
                await WaitWhilePausedAsync(cancellationToken);
                await generator.GenerateNode(node, _rootObject, nodeTreeElements);
            }
        }

        internal async Task<bool> ApplySyncToBoundFrameAsync(GameObject boundFrameRoot, string figmaUrl)
        {
            if (boundFrameRoot == null)
            {
                Debug.LogError("[FigmaImporter] Frame sync failed: bound frame root is null.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(figmaUrl))
            {
                Debug.LogError("[FigmaImporter] Frame sync failed: Figma URL is empty.");
                return false;
            }

            if (_isGenerating)
            {
                Debug.LogWarning("[FigmaImporter] Frame sync skipped: generation is already running.");
                return false;
            }

            if (_settings == null)
            {
                _settings = FigmaImporterSettings.GetInstance();
            }

            var previousRootObject = _rootObject;
            var previousUrl = _settings.Url;
            var previousFileName = _fileName;
            var previousNodeId = _nodeId;
            var previousNodes = _nodes;
            var previousLastClickedNode = _lastClickedNode;

            try
            {
                _rootObject = boundFrameRoot;
                _settings.Url = figmaUrl;
                var apiUrl = ConvertToApiUrl(figmaUrl);
                if (string.IsNullOrEmpty(apiUrl))
                {
                    return false;
                }

                await GetNodes(apiUrl, resetControlFlags: true, origin: "Frame Sync Get Node Data");
                var nodeTreeElements = ResolveNodeTreeElementsForGeneration();
                if (nodeTreeElements == null)
                {
                    Debug.LogError("[FigmaImporter] Frame sync failed: unable to build node tree for generation.");
                    return false;
                }

                NodesAnalyzer.AnalyzeRenderMode(_nodes, nodeTreeElements);
                SetGenerationStatus("Running...", MessageType.Info);
                await GetFileAsync(apiUrl, "Frame Sync Apply");
                return _generationStatusType != MessageType.Error;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                SetGenerationStatus($"Failed: {e.Message}", MessageType.Error);
                return false;
            }
            finally
            {
                _rootObject = previousRootObject;
                _settings.Url = previousUrl;
                _fileName = previousFileName;
                _nodeId = previousNodeId;
                _nodes = previousNodes;
                _treeView = null;
                _lastClickedNode = previousLastClickedNode;
                Repaint();
            }
        }

        private IList<NodeTreeElement> ResolveNodeTreeElementsForGeneration()
        {
            var fromTreeView = _treeView?.TreeView?.treeModel?.Data;
            if (fromTreeView != null && fromTreeView.Count > 0)
            {
                return fromTreeView;
            }

            if (_nodes == null || _nodes.Count == 0)
            {
                return null;
            }

            var fallbackTree = BuildDefaultNodeTreeElements(_nodes);
            NodesAnalyzer.AnalyzeRenderMode(_nodes, fallbackTree);
            return fallbackTree;
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

        private async Task<List<Node>> GetNodeInfo(string nodeUrl, CancellationToken cancellationToken = default)
        {
            if (_isRateLimited)
            {
                return null;
            }

            for (var attempt = 0; attempt <= MaxRequestRetries; attempt++)
            {
                await _requestSemaphore.WaitAsync(cancellationToken);
                try
                {
                    using (UnityWebRequest www = UnityWebRequest.Get(nodeUrl))
                    {
                        www.timeout = RequestTimeoutSeconds;
                        www.SetRequestHeader("Authorization", $"Bearer {_settings.Token}");
                        _ = www.SendWebRequest();
                        var stallReason = await WaitForRequestCompletion(
                            www,
                            "Loading nodes info",
                            true,
                            cancellationToken);

                        FigmaNodesProgressInfo.HideProgress();

                        if (!string.IsNullOrEmpty(stallReason))
                        {
                            var reason = BuildRequestFailureReason(www, "loading node data", stallReason);
                            ReportMissingIssue("Request", "GetNodeInfo", reason);
                            NotifyDiagnosticsForRequestFailure(www, "loading node data", reason);
                            Debug.LogError($"[FigmaImporter] {reason}");
                            return null;
                        }

                        if (www.result == UnityWebRequest.Result.Success)
                        {
                            var result = www.downloadHandler.text;
                            FigmaParser parser = new FigmaParser();
                            return parser.ParseResult(result, (loaded, total, info) =>
                            {
                                FigmaNodesProgressInfo.ShowNodeDataProgress(loaded, total, info);
                            });
                        }

                        if (ShouldRetry(www, attempt))
                        {
                            var delay = RetryDelayBaseMs * (attempt + 1);
                            Debug.LogWarning(
                                $"[FigmaImporter] Nodes request throttled ({www.responseCode}). Retrying in {delay}ms.");
                            await Task.Delay(delay, cancellationToken);
                            continue;
                        }

                        if (www.responseCode == 429)
                        {
                            _isRateLimited = true;
                        }

                        var requestFailure = BuildRequestFailureReason(www, "loading node data");
                        ReportMissingIssue("Request", "GetNodeInfo", requestFailure);
                        NotifyDiagnosticsForRequestFailure(www, "loading node data", requestFailure);
                        Debug.LogError($"[FigmaImporter] {requestFailure}");
                    }
                }
                finally
                {
                    _requestSemaphore.Release();
                    await Task.Delay(SequentialRequestGapMs);
                }
            }

            FigmaNodesProgressInfo.HideProgress();
            return null;
        }

        private const string ImagesUrl =
            "https://api.figma.com/v1/images/{0}?ids={1}&svg_include_id=true&format=png&scale={2}";

        public async Task<Texture2D> GetImage(string nodeId, bool showProgress = true, CancellationToken cancellationToken = default)
        {
            if (!IsLikelyFigmaNodeId(nodeId))
            {
                return null;
            }

            if (_texturesCache.TryGetValue(nodeId, out var tex))
            {
                if (tex != null)
                {
                    return tex;
                }

                // A previous attempt can fail and leave a null cache entry.
                // Remove it so later retries can recover.
                _texturesCache.Remove(nodeId);
            }

            string request = string.Format(CultureInfo.InvariantCulture, ImagesUrl, _fileName, UnityWebRequest.EscapeURL(nodeId), _scale);
            var requestResult = await MakeRequest<string>(request, showProgress, true, cancellationToken);
            if (string.IsNullOrEmpty(requestResult))
            {
                return null;
            }

            var substrs = requestResult.Split('"');
            FigmaNodesProgressInfo.CurrentInfo = "Loading node texture";
            foreach (var s in substrs)
            {
                ThrowIfStopRequested(cancellationToken);
                if (s.Contains("http"))
                {
                    var texture = await LoadTextureByUrl(s, showProgress, cancellationToken);
                    if (texture != null)
                    {
                        _texturesCache[nodeId] = texture;
                    }
                    return texture;
                }
            }

            return null;
        }

#if VECTOR_GRAHICS_IMPORTED
        private const string SvgImagesUrl = "https://api.figma.com/v1/images/{0}?ids={1}&format=svg";
        public async Task<byte[]> GetSvgImage(string nodeId, bool showProgress = true, CancellationToken cancellationToken = default)
        {
            if (!IsLikelyFigmaNodeId(nodeId))
            {
                return null;
            }

            string request = string.Format(CultureInfo.InvariantCulture, SvgImagesUrl, _fileName, UnityWebRequest.EscapeURL(nodeId));
            var svgInfoRequest = await MakeRequest<string>(request, showProgress, true, cancellationToken);
            if (string.IsNullOrEmpty(svgInfoRequest))
            {
                return null;
            }

            var substrs = svgInfoRequest.Split('"');
            foreach (var str in substrs)
            {
                ThrowIfStopRequested(cancellationToken);
                if (str.Contains("https"))
                {
                    var svgData = await MakeRequest<byte[]>(str, showProgress, false, cancellationToken);
                    return svgData;
                }
            }

            return null;
        }
#endif

        private async Task<T> MakeRequest<T>(string request, bool showProgress, bool appendBearerToken = true, CancellationToken cancellationToken = default)
            where T : class
        {
            if (_isRateLimited)
            {
                return null;
            }

            for (var attempt = 0; attempt <= MaxRequestRetries; attempt++)
            {
                await _requestSemaphore.WaitAsync(cancellationToken);
                try
                {
                    using (UnityWebRequest www = UnityWebRequest.Get(request))
                    {
                        www.timeout = RequestTimeoutSeconds;
                        if (appendBearerToken)
                        {
                            www.SetRequestHeader("Authorization", $"Bearer {_settings.Token}");
                        }

                        _ = www.SendWebRequest();
                        var stallReason = await WaitForRequestCompletion(
                            www,
                            "Getting node image info",
                            showProgress,
                            cancellationToken);

                        FigmaNodesProgressInfo.HideProgress();

                        if (!string.IsNullOrEmpty(stallReason))
                        {
                            var reason = BuildRequestFailureReason(www, "request", stallReason);
                            ReportMissingIssue("Request", "MakeRequest", reason);
                            NotifyDiagnosticsForRequestFailure(www, "request", reason);
                            Debug.LogError($"[FigmaImporter] {reason}");
                            return null;
                        }

                        if (www.result == UnityWebRequest.Result.Success)
                        {
                            if (typeof(T) == typeof(string))
                                return www.downloadHandler.text as T;
                            return www.downloadHandler.data as T;
                        }

                        if (ShouldRetry(www, attempt))
                        {
                            var delay = RetryDelayBaseMs * (attempt + 1);
                            Debug.LogWarning(
                                $"[FigmaImporter] Request throttled ({www.responseCode}). Retrying in {delay}ms.");
                            await Task.Delay(delay, cancellationToken);
                            continue;
                        }

                        if (www.responseCode == 429)
                        {
                            _isRateLimited = true;
                        }

                        var requestFailure = BuildRequestFailureReason(www, "request");
                        ReportMissingIssue("Request", "MakeRequest", requestFailure);
                        NotifyDiagnosticsForRequestFailure(www, "request", requestFailure);
                        Debug.LogError($"[FigmaImporter] {requestFailure}");
                        return null;
                    }
                }
                finally
                {
                    _requestSemaphore.Release();
                    await Task.Delay(SequentialRequestGapMs);
                }
            }

            return null;
        }

        private static bool ShouldRetry(UnityWebRequest request, int attempt)
        {
            if (attempt >= MaxRequestRetries)
            {
                return false;
            }

            return request.responseCode == 429 ||
                   request.responseCode == 500 ||
                   request.responseCode == 502 ||
                   request.responseCode == 503 ||
                   request.responseCode == 504;
        }

        private static bool IsLikelyFigmaNodeId(string nodeId)
        {
            return !string.IsNullOrWhiteSpace(nodeId) &&
                   !nodeId.Equals("Root", StringComparison.OrdinalIgnoreCase) &&
                   nodeId.Contains(":");
        }

        public string GetRendersFolderPath()
        {
            if (_settings == null)
            {
                _settings = FigmaImporterSettings.GetInstance();
            }

            return FigmaPathUtils.NormalizeRendersFolder(_settings != null ? _settings.RendersPath : null);
        }

        private async Task<Texture2D> LoadTextureByUrl(string url, bool showProgress = true, CancellationToken cancellationToken = default)
        {
            if (_isRateLimited)
            {
                return null;
            }

            await _requestSemaphore.WaitAsync(cancellationToken);
            try
            {
                using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
                {
                    request.timeout = RequestTimeoutSeconds;
                    _ = request.SendWebRequest();
                    var stallReason = await WaitForRequestCompletion(
                        request,
                        "Loading node texture",
                        showProgress,
                        cancellationToken);
                    FigmaNodesProgressInfo.HideProgress();

                    if (!string.IsNullOrEmpty(stallReason))
                    {
                        var reason = BuildRequestFailureReason(request, "loading node texture", stallReason);
                        ReportMissingIssue("Request", "LoadTextureByUrl", reason);
                        NotifyDiagnosticsForRequestFailure(request, "loading node texture", reason);
                        Debug.LogError($"[FigmaImporter] {reason}");
                        return null;
                    }

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        if (request.responseCode == 429)
                        {
                            _isRateLimited = true;
                        }
                        var requestFailure = BuildRequestFailureReason(request, "loading node texture");
                        ReportMissingIssue("Request", "LoadTextureByUrl", requestFailure);
                        NotifyDiagnosticsForRequestFailure(request, "loading node texture", requestFailure);
                        Debug.LogWarning($"[FigmaImporter] {requestFailure}");
                        return null;
                    }
                    var data = request.downloadHandler.data;
                    Texture2D t = new Texture2D(0, 0);
                    t.LoadImage(data);
                    FigmaNodesProgressInfo.HideProgress();
                    return t;
                }
            }
            finally
            {
                _requestSemaphore.Release();
                await Task.Delay(SequentialRequestGapMs);
            }
        }

        private async Task<string> WaitForRequestCompletion(
            UnityWebRequest request,
            string progressInfo,
            bool showProgress,
            CancellationToken cancellationToken)
        {
            SetActiveRequest(request, progressInfo);
            var stallThresholdSeconds = GetRequestStallThresholdSeconds();
            var lastProgress = 0f;
            ulong lastDownloadedBytes = 0;
            var lastActivityUtc = DateTime.UtcNow;
            try
            {
                while (!request.isDone)
                {
                    ThrowIfStopRequested(cancellationToken);
                    await WaitWhilePausedAsync(cancellationToken);

                    var progress = request.downloadProgress >= 0f ? request.downloadProgress : 0f;
                    var downloadedBytes = request.downloadedBytes;
                    if (progress > lastProgress + RequestProgressEpsilon || downloadedBytes > lastDownloadedBytes)
                    {
                        lastProgress = progress;
                        lastDownloadedBytes = downloadedBytes;
                        lastActivityUtc = DateTime.UtcNow;
                    }
                    else if ((DateTime.UtcNow - lastActivityUtc).TotalSeconds >= stallThresholdSeconds)
                    {
                        try
                        {
                            request.Abort();
                        }
                        catch (Exception)
                        {
                        }

                        var stallReason =
                            $"Request stalled while {progressInfo} (no progress for {stallThresholdSeconds}s).";
                        FigmaNodesProgressInfo.CurrentInfo = stallReason;
                        if (showProgress)
                        {
                            FigmaNodesProgressInfo.ShowProgress(progress);
                        }
                        return stallReason;
                    }

                    FigmaNodesProgressInfo.CurrentInfo = progressInfo;
                    if (showProgress)
                    {
                        FigmaNodesProgressInfo.ShowProgress(progress);
                    }
                    await Task.Delay(100, cancellationToken);
                }
            }
            finally
            {
                ClearActiveRequest(request);
            }

            return null;
        }

        private static int GetRequestStallThresholdSeconds()
        {
            return Application.platform == RuntimePlatform.WindowsEditor
                ? RequestStallThresholdSecondsWindows
                : RequestStallThresholdSeconds;
        }

        private string BuildRequestFailureReason(UnityWebRequest request, string stage, string stallReason = null)
        {
            if (!string.IsNullOrWhiteSpace(stallReason))
            {
                return $"{stallReason} URL: {TrimForLog(request?.url)}";
            }

            var statusCode = request?.responseCode ?? 0;
            var error = request?.error ?? "Unknown error";
            var lowerError = error.ToLowerInvariant();

            if (statusCode == 429)
            {
                return $"Figma API rate limit (HTTP 429) while {stage}. URL: {TrimForLog(request?.url)}";
            }

            if (statusCode == 400)
            {
                return
                    $"HTTP 400 while {stage}. Usually this means file key / node-id is invalid for this request. URL: {TrimForLog(request?.url)}";
            }

            if (statusCode == 401)
            {
                return
                    $"Request unauthorized (HTTP 401) while {stage}. Token is invalid or expired. Re-run OpenOauthUrl + GetToken on this device, then retry. Diagnostics: {FigmaImporterMenuPaths.Diagnostics.DiagnosticsHub}. URL: {TrimForLog(request?.url)}";
            }

            if (statusCode == 403)
            {
                return
                    $"Request forbidden (HTTP 403) while {stage}. Token may be superseded by OAuth on another device, expired, or this account lacks access to the target file/node. Re-run OpenOauthUrl + GetToken on this device and verify file permissions. Diagnostics: {FigmaImporterMenuPaths.Diagnostics.DiagnosticsHub}. URL: {TrimForLog(request?.url)}";
            }

            if (lowerError.Contains("timed out") || lowerError.Contains("timeout"))
            {
                return
                    $"Request timed out after {RequestTimeoutSeconds}s while {stage}. URL: {TrimForLog(request?.url)}";
            }

            if (statusCode > 0)
            {
                return $"Request failed while {stage} (HTTP {statusCode}): {error}. URL: {TrimForLog(request?.url)}";
            }

            return $"Request failed while {stage}: {error}. URL: {TrimForLog(request?.url)}";
        }

        private static void ReportMissingIssue(
            string category,
            string key,
            string details,
            string nodeId = null,
            string nodeName = null)
        {
            ImportFallbackRegistry.ReportMissingIssue(category, key, details, nodeId, nodeName);
        }

        private static void NotifyDiagnosticsForRequestFailure(UnityWebRequest request, string stage, string reason)
        {
            if (request == null)
            {
                return;
            }

            var statusCode = request.responseCode;
            if (statusCode != 401 && statusCode != 403)
            {
                return;
            }

            FigmaDiagnosticsHubWindow.ReportAuthApiFailure(
                statusCode,
                string.IsNullOrWhiteSpace(stage) ? "request" : stage,
                request.url,
                reason ?? string.Empty);
        }

        private static string TrimForLog(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "(unknown)";
            }

            const int maxLength = 240;
            if (value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength) + "...";
        }

        private Task EnsureVectorGraphicsInstalledIfNeeded(CancellationToken cancellationToken)
        {
            if (_nodes == null || !ContainsSvgCandidateNodes(_nodes))
            {
                return Task.CompletedTask;
            }

#if VECTOR_GRAHICS_IMPORTED
            return Task.CompletedTask;
#else
            return EnsureVectorGraphicsInstalledIfNeededInternal(cancellationToken);
#endif
        }

#if !VECTOR_GRAHICS_IMPORTED
        private async Task EnsureVectorGraphicsInstalledIfNeededInternal(CancellationToken cancellationToken)
        {
            var alreadyInstalled = await IsPackageInstalled(VectorGraphicsPackageName, cancellationToken);
            if (alreadyInstalled)
            {
                return;
            }

            FigmaNodesProgressInfo.CurrentInfo = "Installing com.unity.vectorgraphics package";
            FigmaNodesProgressInfo.ShowProgress(0f);
            var installed = await TryInstallVectorGraphicsPackage(cancellationToken);
            FigmaNodesProgressInfo.HideProgress();

            if (installed)
            {
                Debug.Log(
                    "[FigmaImporter] Installed com.unity.vectorgraphics. Unity may recompile scripts; re-run import to enable SVG rendering path.");
                return;
            }

            Debug.LogError(
                "[FigmaImporter] Failed to install com.unity.vectorgraphics. Falling back to raster rendering for this run.");
            ReportMissingIssue(
                "Package",
                VectorGraphicsPackageName,
                "Failed to install com.unity.vectorgraphics. SVG nodes will use raster fallback.");
        }
#endif

        private static bool ContainsSvgCandidateNodes(IList<Node> nodes)
        {
            if (nodes == null)
            {
                return false;
            }

            foreach (var node in nodes)
            {
                if (node == null)
                {
                    continue;
                }

                var isLeaf = node.children == null || node.children.Length == 0;
                if (isLeaf && !string.Equals(node.type, "TEXT", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (ContainsSvgCandidateNodes(node.children))
                {
                    return true;
                }
            }

            return false;
        }

#if !VECTOR_GRAHICS_IMPORTED
        private async Task<bool> IsPackageInstalled(string packageName, CancellationToken cancellationToken)
        {
            ListRequest listRequest;
            try
            {
                listRequest = Client.List(true, true);
            }
            catch (Exception e)
            {
                ReportMissingIssue("Package", packageName, $"Failed to query Package Manager: {e.Message}");
                Debug.LogError($"[FigmaImporter] Failed to query Package Manager: {e.Message}");
                return false;
            }

            var start = DateTime.UtcNow;
            while (!listRequest.IsCompleted)
            {
                ThrowIfStopRequested(cancellationToken);
                await WaitWhilePausedAsync(cancellationToken);
                FigmaNodesProgressInfo.CurrentInfo = "Checking Unity packages";
                FigmaNodesProgressInfo.ShowProgress(0f);
                if ((DateTime.UtcNow - start).TotalSeconds > PackageRequestTimeoutSeconds)
                {
                    ReportMissingIssue("Package", packageName, "Package list request timed out.");
                    Debug.LogError("[FigmaImporter] Package list request timed out.");
                    return false;
                }

                await Task.Delay(100, cancellationToken);
            }

            if (listRequest.Status != StatusCode.Success || listRequest.Result == null)
            {
                var errorText = listRequest.Error?.message ?? "Unknown package list error";
                ReportMissingIssue("Package", packageName, $"Failed to list packages: {errorText}");
                Debug.LogError($"[FigmaImporter] Failed to list packages: {errorText}");
                return false;
            }

            foreach (var packageInfo in listRequest.Result)
            {
                if (string.Equals(packageInfo.name, packageName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private async Task<bool> TryInstallVectorGraphicsPackage(CancellationToken cancellationToken)
        {
            if (await TryAddPackage(VectorGraphicsPackagePinnedVersion, cancellationToken))
            {
                return true;
            }

            return await TryAddPackage(VectorGraphicsPackageName, cancellationToken);
        }

        private async Task<bool> TryAddPackage(string packageId, CancellationToken cancellationToken)
        {
            AddRequest addRequest;
            try
            {
                addRequest = Client.Add(packageId);
            }
            catch (Exception e)
            {
                ReportMissingIssue("Package", packageId, $"Failed to start install: {e.Message}");
                Debug.LogError($"[FigmaImporter] Failed to start install for {packageId}: {e.Message}");
                return false;
            }

            var start = DateTime.UtcNow;
            while (!addRequest.IsCompleted)
            {
                ThrowIfStopRequested(cancellationToken);
                await WaitWhilePausedAsync(cancellationToken);
                FigmaNodesProgressInfo.CurrentInfo = $"Installing {packageId}";
                FigmaNodesProgressInfo.ShowProgress(0f);
                if ((DateTime.UtcNow - start).TotalSeconds > PackageRequestTimeoutSeconds)
                {
                    ReportMissingIssue("Package", packageId, $"Installing {packageId} timed out.");
                    Debug.LogError($"[FigmaImporter] Installing {packageId} timed out.");
                    return false;
                }

                await Task.Delay(200, cancellationToken);
            }

            if (addRequest.Status == StatusCode.Success)
            {
                Debug.Log($"[FigmaImporter] Installed package: {addRequest.Result.name} {addRequest.Result.version}");
                return true;
            }

            var errorText = addRequest.Error?.message ?? "Unknown install error";
            ReportMissingIssue("Package", packageId, $"Could not install {packageId}: {errorText}");
            Debug.LogWarning($"[FigmaImporter] Could not install {packageId}: {errorText}");
            return false;
        }
#endif

        private void SyncControlRequests()
        {
            // Cancellation and pause controls are scoped to the active Generate run only.
            // This prevents stale cancel flags from blocking independent operations such as Get Node Data.
            if (!_isGenerating)
            {
                _cancelRequested = false;
                _isPaused = false;
                return;
            }

            _cancelRequested = FigmaNodesProgressInfo.CancelRequested;
            _isPaused = FigmaNodesProgressInfo.PauseRequested;
        }

        private void ThrowIfStopRequested(CancellationToken cancellationToken)
        {
            SyncControlRequests();
            if (_isGenerating && _cancelRequested)
            {
                FigmaImporterEventFlow.Step(
                    "GenerateNodes",
                    _activeGenerateChainId,
                    "ThrowIfStopRequested.Cancel",
                    "Cancellation requested while running");
                AbortActiveRequest("cancel requested");
                if (_generationCts != null && !_generationCts.IsCancellationRequested)
                {
                    try
                    {
                        _generationCts.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }

                throw new OperationCanceledException("Node generation cancelled by user.", cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        private void SetActiveRequest(UnityWebRequest request, string stage)
        {
            _activeRequest = request;
            _activeRequestStage = stage;
        }

        private void ClearActiveRequest(UnityWebRequest request)
        {
            if (!ReferenceEquals(_activeRequest, request))
            {
                return;
            }

            _activeRequest = null;
            _activeRequestStage = null;
        }

        private void AbortActiveRequest(string reason)
        {
            var request = _activeRequest;
            if (request == null)
            {
                return;
            }

            try
            {
                request.Abort();
            }
            catch (Exception)
            {
            }

            var message =
                $"[FigmaImporter] Aborted active request ({_activeRequestStage ?? "unknown stage"}) due to {reason}.";
            if (string.Equals(reason, "cancel requested", StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log(message);
                return;
            }

            Debug.LogWarning(message);
        }

        private void TogglePauseGeneration()
        {
            if (!_isGenerating)
            {
                return;
            }

            if (_cancelRequested)
            {
                return;
            }

            _isPaused = !_isPaused;
            FigmaNodesProgressInfo.SetPauseRequested(_isPaused);
            FigmaImporterEventFlow.Step(
                "GenerateNodes",
                _activeGenerateChainId,
                _isPaused ? "PauseRequested" : "ContinueRequested");
            SetGenerationStatus(_isPaused ? "Paused." : "Running...", MessageType.Info);
            Repaint();
        }

        private void CancelGeneration()
        {
            CancelGeneration(requestedByUser: true, reasonDetails: "User requested cancel");
        }

        private void CancelGeneration(bool requestedByUser, string reasonDetails)
        {
            if (!_isGenerating)
            {
                return;
            }

            if (_cancelRequested)
            {
                return;
            }

            _cancelRequested = true;
            _cancelRequestedByUser = requestedByUser;
            FigmaImporterEventFlow.Step(
                "GenerateNodes",
                _activeGenerateChainId,
                "CancelRequested",
                string.IsNullOrWhiteSpace(reasonDetails) ? "-" : reasonDetails);
            SetGenerationStatus(
                requestedByUser
                    ? "Cancel requested. Stopping now..."
                    : "Canceled automatically. Stopping now...",
                MessageType.Warning);
            FigmaNodesProgressInfo.SetCancelRequested(true);
            FigmaNodesProgressInfo.CurrentInfo = requestedByUser
                ? "Cancel requested. Stopping current task..."
                : "Canceled automatically. Stopping current task...";
            FigmaNodesProgressInfo.MarkActivity(requestedByUser ? "Cancel requested" : "Canceled automatically");
            AbortActiveRequest("cancel requested");
            try
            {
                _generationCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            Repaint();
        }

        private void MonitorGenerationStall()
        {
            if (!_isGenerating || _isPaused || _generationStallHandled)
            {
                return;
            }

            var elapsed = EditorApplication.timeSinceStartup - FigmaNodesProgressInfo.LastProgressUpdateTime;
            if (elapsed < GenerationStallThresholdSeconds)
            {
                return;
            }

            _generationStallHandled = true;
            var stage = string.IsNullOrWhiteSpace(FigmaNodesProgressInfo.CurrentInfo)
                ? "unknown stage"
                : FigmaNodesProgressInfo.CurrentInfo;
            FigmaImporterEventFlow.Step(
                "GenerateNodes",
                _activeGenerateChainId,
                "GenerationStallDetected",
                $"stage={stage}");
            Debug.LogError(
                $"[FigmaImporter] Generation stalled for {GenerationStallThresholdSeconds}s at '{stage}'. Generation cancelled automatically.");
            CancelGeneration(requestedByUser: false, reasonDetails: "Auto cancel due to generation stall");
        }

        private void CleanupGenerationRuntimeState(bool cancelCts, CancellationTokenSource expectedRunCts)
        {
            _isGenerating = false;
            _isPaused = false;
            _generationStallHandled = false;
            _cancelRequested = false;
            _activeRequest = null;
            _activeRequestStage = null;
            _activeGenerationRunId = 0;
            EditorApplication.update -= MonitorGenerationStall;
            FigmaNodesProgressInfo.ClearGenerationControls();
            FigmaNodesProgressInfo.HideProgress();
            DisposeGenerationCts(cancel: cancelCts, expected: expectedRunCts);
        }

        private void SetGenerationStatus(string text, MessageType type)
        {
            _generationStatusText = string.IsNullOrWhiteSpace(text) ? "Idle" : text;
            _generationStatusType = type;
        }

        private static string MaskToken(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return "(empty)";
            }

            const int visibleChars = 4;
            if (token.Length <= visibleChars * 2)
            {
                return new string('*', token.Length);
            }

            return $"{token.Substring(0, visibleChars)}...{token.Substring(token.Length - visibleChars)}";
        }


        [Serializable]
        public class AuthResult
        {
            [SerializeField] public string access_token;
            [SerializeField] public string expires_in;
            [SerializeField] public string refresh_token;
        }
    }
}
