#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DuoCurtain.Editor
{
    public static class GridIntegerNormalizationUtility
    {
        private const int CellWidth = TilePlacementGrid.DefaultCellWidth;
        private const int CellHeight = TilePlacementGrid.DefaultCellHeight;
        private const string MenuPath = "Tools/Duo Curtain/Grid/Normalize Integer Tile Grid";
        private const string RunOnceMarkerPath = "Temp/DuoCurtainRunGridNormalizationOnce.flag";

        [InitializeOnLoadMethod]
        private static void RunPendingNormalizationOnce()
        {
            string markerPath = Path.Combine(Directory.GetCurrentDirectory(), RunOnceMarkerPath);
            if (!File.Exists(markerPath))
                return;

            File.Delete(markerPath);
            Debug.Log("[GridIntegerNormalizationUtility] Running pending one-shot normalization.");
            EditorApplication.delayCall += NormalizeIntegerTileGrid;
        }

        [MenuItem(MenuPath)]
        public static void NormalizeIntegerTileGrid()
        {
            string activeScenePath = SceneManager.GetActiveScene().path;
            int changedPrefabs = NormalizePrefabs();
            int changedScenes = NormalizeScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (!string.IsNullOrWhiteSpace(activeScenePath) && File.Exists(activeScenePath))
                EditorSceneManager.OpenScene(activeScenePath, OpenSceneMode.Single);

            Debug.Log("[GridIntegerNormalizationUtility] Normalized " + changedPrefabs + " prefab(s) and " + changedScenes + " scene(s).");
        }

        public static void NormalizeProjectAssets()
        {
            NormalizeIntegerTileGrid();
        }

        private static int NormalizePrefabs()
        {
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            int changedCount = 0;

            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);
                bool changed = false;

                try
                {
                    changed |= NormalizeTilePieces(prefabRoot);
                    changed |= NormalizeHoverInteractions(prefabRoot);
                    changed |= NormalizeCurtainAudioSource(prefabRoot, true);

                    if (changed)
                    {
                        PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                        changedCount++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }

            return changedCount;
        }

        private static int NormalizeScenes()
        {
            int changedCount = 0;

            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
            for (int i = 0; i < buildScenes.Length; i++)
            {
                EditorBuildSettingsScene buildScene = buildScenes[i];
                if (buildScene == null || !buildScene.enabled || string.IsNullOrWhiteSpace(buildScene.path))
                    continue;

                string path = buildScene.path;
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                bool changed = false;

                TilePlacementGrid[] grids = Object.FindObjectsByType<TilePlacementGrid>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                if (grids.Length == 0)
                    continue;

                for (int gridIndex = 0; gridIndex < grids.Length; gridIndex++)
                    changed |= NormalizeGrid(grids[gridIndex]);

                changed |= NormalizeTilePiecesInOpenScene();
                changed |= NormalizeFallbackFloorplanRenderers();
                changed |= NormalizeHoverInteractionsInOpenScene();
                changed |= NormalizeCurtainAudioSourcesInOpenScene();

                if (!changed)
                    continue;

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                changedCount++;
            }

            return changedCount;
        }

        private static bool NormalizeTilePiecesInOpenScene()
        {
            TilePieceDefinition[] definitions = Object.FindObjectsByType<TilePieceDefinition>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            bool changed = false;

            for (int i = 0; i < definitions.Length; i++)
                changed |= NormalizeTilePiece(definitions[i]);

            return changed;
        }

        private static bool NormalizeTilePieces(GameObject root)
        {
            TilePieceDefinition[] definitions = root.GetComponentsInChildren<TilePieceDefinition>(true);
            bool changed = false;

            for (int i = 0; i < definitions.Length; i++)
                changed |= NormalizeTilePiece(definitions[i]);

            return changed;
        }

        private static bool NormalizeTilePiece(TilePieceDefinition definition)
        {
            if (definition == null)
                return false;

            bool changed = false;
            changed |= SetVector2(ref definition.childCellSize, TilePlacementGrid.DefaultCellSize);

            if (definition.autoGenerateCellsFromChildren)
            {
                definition.autoGenerateCellsFromChildren = false;
                changed = true;
            }

            bool nestedTile = definition.placementLayer == TilePieceDefinition.PlacementLayer.Tile &&
                              HasAncestorTilePieceDefinition(definition.transform);
            if (!nestedTile)
            {
                changed |= NormalizeTileTransform(definition);
                changed |= NormalizeCellsFromSnappedSize(definition);
            }
            else if (definition.registerOnStart)
            {
                definition.registerOnStart = false;
                changed = true;
            }

            changed |= NormalizeCells(definition);
            changed |= NormalizeRootColliders(definition);

            if (changed)
                EditorUtility.SetDirty(definition);

            return changed;
        }

        private static bool NormalizeGrid(TilePlacementGrid grid)
        {
            if (grid == null)
                return false;

            bool changed = false;
            if (grid.tileUnit != CellWidth)
            {
                grid.tileUnit = CellWidth;
                changed = true;
            }

            changed |= SetVector2(ref grid.cellSize, TilePlacementGrid.DefaultCellSize);
            changed |= SetVector2(ref grid.origin, new Vector2(
                TilePlacementGrid.SnapToTileMultiple(grid.origin.x, CellWidth),
                TilePlacementGrid.SnapToTileMultiple(grid.origin.y, CellHeight)
            ));

            if (changed)
                EditorUtility.SetDirty(grid);

            return changed;
        }

        private static bool NormalizeTileTransform(TilePieceDefinition definition)
        {
            Transform transform = definition.transform;
            bool changed = false;
            Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            float safeParentX = Mathf.Abs(parentScale.x) > 0.0001f ? parentScale.x : 1f;
            float safeParentY = Mathf.Abs(parentScale.y) > 0.0001f ? parentScale.y : 1f;
            float currentWorldX = Mathf.Abs(transform.localScale.x * safeParentX);
            float currentWorldY = Mathf.Abs(transform.localScale.y * safeParentY);
            Vector2 targetWorldSize = new Vector2(
                TilePlacementGrid.SnapPositiveToTileMultiple(currentWorldX, CellWidth),
                TilePlacementGrid.SnapPositiveToTileMultiple(currentWorldY, CellHeight)
            );

            Vector3 targetLocalScale = transform.localScale;
            targetLocalScale.x = Mathf.Sign(transform.localScale.x == 0f ? 1f : transform.localScale.x) * targetWorldSize.x / Mathf.Abs(safeParentX);
            targetLocalScale.y = Mathf.Sign(transform.localScale.y == 0f ? 1f : transform.localScale.y) * targetWorldSize.y / Mathf.Abs(safeParentY);

            if (!Approximately(transform.localScale, targetLocalScale))
            {
                transform.localScale = targetLocalScale;
                changed = true;
            }

            Vector3 position = transform.localPosition;
            position.x = TilePlacementGrid.SnapToTileMultiple(position.x, CellWidth);
            position.y = TilePlacementGrid.SnapToTileMultiple(position.y, CellHeight);
            position.z = Mathf.Round(position.z);
            if (!Approximately(transform.localPosition, position))
            {
                transform.localPosition = position;
                changed = true;
            }

            if (changed)
                EditorUtility.SetDirty(transform);

            return changed;
        }

        private static bool NormalizeCellsFromSnappedSize(TilePieceDefinition definition)
        {
            if (definition == null)
                return false;

            Vector3 worldScale = definition.transform.lossyScale;
            int width = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(worldScale.x) / CellWidth));
            int height = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(worldScale.y) / CellHeight));

            List<Vector2Int> targetCells = new List<Vector2Int>(width * height);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    targetCells.Add(new Vector2Int(x, y));
            }

            if (CellsMatch(definition.cells, targetCells))
                return false;

            definition.cells = targetCells;
            return true;
        }

        private static bool NormalizeRootColliders(TilePieceDefinition definition)
        {
            bool changed = false;
            BoxCollider2D box = definition.GetComponent<BoxCollider2D>();
            if (box != null)
            {
                Vector2 size = Vector2.one;
                Vector2 offset = Vector2.zero;
                if (box.size != size)
                {
                    box.size = size;
                    changed = true;
                }

                if (box.offset != offset)
                {
                    box.offset = offset;
                    changed = true;
                }

                if (changed)
                    EditorUtility.SetDirty(box);
            }

            return changed;
        }

        private static bool NormalizeCells(TilePieceDefinition definition)
        {
            if (definition.cells == null)
            {
                definition.cells = new List<Vector2Int> { Vector2Int.zero };
                return true;
            }

            bool changed = false;
            HashSet<Vector2Int> seen = new HashSet<Vector2Int>();
            for (int i = definition.cells.Count - 1; i >= 0; i--)
            {
                if (seen.Add(definition.cells[i]))
                    continue;

                definition.cells.RemoveAt(i);
                changed = true;
            }

            if (definition.cells.Count == 0)
            {
                definition.cells.Add(Vector2Int.zero);
                changed = true;
            }

            definition.cells.Sort((a, b) =>
            {
                int yCompare = a.y.CompareTo(b.y);
                return yCompare != 0 ? yCompare : a.x.CompareTo(b.x);
            });

            return changed;
        }

        private static bool NormalizeFallbackFloorplanRenderers()
        {
            SpriteRenderer[] renderers = Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            bool changed = false;

            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer renderer = renderers[i];
                if (renderer == null || !renderer.gameObject.name.Contains("Floorplan"))
                    continue;

                if (renderer.GetComponentInParent<TilePieceDefinition>() != null)
                    continue;

                bool rendererChanged = false;
                Transform transform = renderer.transform;
                Vector3 scale = transform.localScale;
                scale.x = Mathf.Sign(scale.x == 0f ? 1f : scale.x) * TilePlacementGrid.SnapPositiveToTileMultiple(Mathf.Abs(scale.x), CellWidth);
                scale.y = Mathf.Sign(scale.y == 0f ? 1f : scale.y) * TilePlacementGrid.SnapPositiveToTileMultiple(Mathf.Abs(scale.y), CellHeight);
                if (!Approximately(transform.localScale, scale))
                {
                    transform.localScale = scale;
                    rendererChanged = true;
                }

                Vector3 position = transform.localPosition;
                position.x = TilePlacementGrid.SnapToTileMultiple(position.x, CellWidth);
                position.y = TilePlacementGrid.SnapToTileMultiple(position.y, CellHeight);
                position.z = Mathf.Round(position.z);
                if (!Approximately(transform.localPosition, position))
                {
                    transform.localPosition = position;
                    rendererChanged = true;
                }

                if (rendererChanged)
                {
                    EditorUtility.SetDirty(transform);
                    changed = true;
                }
            }

            return changed;
        }

        private static bool NormalizeHoverInteractions(GameObject root)
        {
            HoverScrollColorLerp2D[] hovers = root.GetComponentsInChildren<HoverScrollColorLerp2D>(true);
            bool changed = false;

            for (int i = 0; i < hovers.Length; i++)
                changed |= NormalizeHoverInteraction(hovers[i]);

            return changed;
        }

        private static bool NormalizeHoverInteractionsInOpenScene()
        {
            HoverScrollColorLerp2D[] hovers = Object.FindObjectsByType<HoverScrollColorLerp2D>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            bool changed = false;

            for (int i = 0; i < hovers.Length; i++)
                changed |= NormalizeHoverInteraction(hovers[i]);

            return changed;
        }

        private static bool NormalizeHoverInteraction(HoverScrollColorLerp2D hover)
        {
            if (hover == null || hover.useLogicalCursorHover)
                return false;

            hover.useLogicalCursorHover = true;
            EditorUtility.SetDirty(hover);
            return true;
        }

        private static bool NormalizeCurtainAudioSourcesInOpenScene()
        {
            AudioSource[] audioSources = Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            bool changed = false;

            for (int i = 0; i < audioSources.Length; i++)
                changed |= NormalizeCurtainAudioSource(audioSources[i].gameObject, false);

            return changed;
        }

        private static bool NormalizeCurtainAudioSource(GameObject root, bool prefabAsset)
        {
            if (root == null || root.GetComponent<AudioSource>() == null || !LooksLikeCurtainAudio(root.name))
                return false;

            Transform transform = root.transform;
            Vector3 targetPosition = prefabAsset ? Vector3.zero : new Vector3(
                Mathf.Round(transform.localPosition.x),
                Mathf.Round(transform.localPosition.y),
                Mathf.Round(transform.localPosition.z)
            );

            if (Approximately(transform.localPosition, targetPosition))
                return false;

            transform.localPosition = targetPosition;
            EditorUtility.SetDirty(transform);
            return true;
        }

        private static bool LooksLikeCurtainAudio(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return false;

            string normalized = objectName.ToLowerInvariant();
            return normalized.Contains("curtain") || normalized.Contains("curtian");
        }

        private static bool CellsMatch(List<Vector2Int> current, List<Vector2Int> target)
        {
            if (current == null || target == null || current.Count != target.Count)
                return false;

            for (int i = 0; i < current.Count; i++)
            {
                if (current[i] != target[i])
                    return false;
            }

            return true;
        }

        private static bool HasAncestorTilePieceDefinition(Transform transform)
        {
            Transform parent = transform.parent;
            while (parent != null)
            {
                if (parent.GetComponent<TilePieceDefinition>() != null)
                    return true;

                parent = parent.parent;
            }

            return false;
        }

        private static bool SetVector2(ref Vector2 target, Vector2 value)
        {
            if (Approximately(target.x, value.x) && Approximately(target.y, value.y))
                return false;

            target = value;
            return true;
        }

        private static bool Approximately(float a, float b)
        {
            return Mathf.Abs(a - b) <= 0.0001f;
        }

        private static bool Approximately(Vector3 a, Vector3 b)
        {
            return Approximately(a.x, b.x) && Approximately(a.y, b.y) && Approximately(a.z, b.z);
        }
    }

    [InitializeOnLoad]
    internal static class DuoCurtainAutoReload
    {
        private const string EnabledKey = "DuoCurtain.AutoReload.Enabled";
        private const string ToggleMenuPath = "Tools/Duo Curtain/Auto Reload/Enabled";
        private const string ReloadNowMenuPath = "Tools/Duo Curtain/Auto Reload/Reload Project Now";
        private const double DebounceSeconds = 0.75d;
        private const double BusyRetrySeconds = 1.5d;
        private const double WatcherSuppressionSeconds = 2d;

        private static readonly object PendingLock = new object();
        private static readonly HashSet<string> PendingPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<FileSystemWatcher> Watchers = new List<FileSystemWatcher>();

        private static long lastChangeUtcTicks;
        private static long suppressWatcherUntilUtcTicks;
        private static bool forceFullReload;
        private static bool initialized;
        private static volatile bool autoReloadEnabled;
        private static bool ownsAutoRefreshLock;

        static DuoCurtainAutoReload()
        {
            if (Application.isBatchMode)
                return;

            autoReloadEnabled = EditorPrefs.GetBool(EnabledKey, true);
            AcquireAutoRefreshLock();
            EditorApplication.update += Tick;
            EditorApplication.quitting += Shutdown;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            EditorApplication.delayCall += Initialize;
        }

        [MenuItem(ToggleMenuPath)]
        private static void ToggleAutoReload()
        {
            bool enabled = !IsEnabled;
            EditorPrefs.SetBool(EnabledKey, enabled);
            autoReloadEnabled = enabled;

            if (enabled)
            {
                AcquireAutoRefreshLock();
                Initialize();
                QueueFullReload();
            }
            else
            {
                DisposeWatchers();
                ClearPendingChanges();
                initialized = false;
                ReleaseAutoRefreshLock();
            }

            Debug.Log("[DuoCurtain Auto Reload] " + (enabled ? "Enabled." : "Disabled."));
        }

        [MenuItem(ToggleMenuPath, true)]
        private static bool ValidateToggleAutoReload()
        {
            Menu.SetChecked(ToggleMenuPath, IsEnabled);
            return true;
        }

        [MenuItem(ReloadNowMenuPath)]
        private static void ReloadProjectNow()
        {
            QueueFullReload();
        }

        private static bool IsEnabled
        {
            get { return autoReloadEnabled; }
        }

        private static string ProjectRoot
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); }
        }

        private static void Initialize()
        {
            if (initialized || !IsEnabled)
                return;

            AcquireAutoRefreshLock();
            initialized = true;
            CreateWatcher("Assets");
            CreateWatcher("Packages");
            CreateWatcher("ProjectSettings");
            Debug.Log("[DuoCurtain Auto Reload] Watching Assets, Packages, and ProjectSettings.");
        }

        private static void CreateWatcher(string projectRelativeDirectory)
        {
            string absolutePath = Path.Combine(ProjectRoot, projectRelativeDirectory);
            if (!Directory.Exists(absolutePath))
                return;

            try
            {
                FileSystemWatcher watcher = new FileSystemWatcher(absolutePath);
                watcher.IncludeSubdirectories = true;
                watcher.NotifyFilter =
                    NotifyFilters.FileName |
                    NotifyFilters.DirectoryName |
                    NotifyFilters.LastWrite |
                    NotifyFilters.Size;
                watcher.InternalBufferSize = 64 * 1024;
                watcher.Changed += OnFileChanged;
                watcher.Created += OnFileChanged;
                watcher.Deleted += OnFileChanged;
                watcher.Renamed += OnFileRenamed;
                watcher.Error += OnWatcherError;
                watcher.EnableRaisingEvents = true;
                Watchers.Add(watcher);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[DuoCurtain Auto Reload] Could not watch " +
                    projectRelativeDirectory +
                    ": " +
                    exception.Message);
            }
        }

        private static void OnFileChanged(object sender, FileSystemEventArgs args)
        {
            QueueChangedPath(args.FullPath);
        }

        private static void OnFileRenamed(object sender, RenamedEventArgs args)
        {
            QueueChangedPath(args.OldFullPath);
            QueueChangedPath(args.FullPath);
        }

        private static void OnWatcherError(object sender, ErrorEventArgs args)
        {
            lock (PendingLock)
            {
                forceFullReload = true;
                lastChangeUtcTicks = DateTime.UtcNow.Ticks;
            }
        }

        private static void QueueChangedPath(string fullPath)
        {
            if (!IsEnabled || IsWatcherSuppressed() || ShouldIgnorePath(fullPath))
                return;

            lock (PendingLock)
            {
                PendingPaths.Add(NormalizePath(fullPath));
                lastChangeUtcTicks = DateTime.UtcNow.Ticks;
            }
        }

        private static void QueueFullReload()
        {
            lock (PendingLock)
            {
                forceFullReload = true;
                lastChangeUtcTicks = 0;
            }
        }

        private static void Tick()
        {
            if (!IsEnabled)
                return;

            if (!initialized)
                Initialize();

            if (IsEditorBusy())
                return;

            List<string> changedPaths;
            bool fullReload;
            long queuedAtTicks;

            lock (PendingLock)
            {
                fullReload = forceFullReload;
                if (!fullReload && PendingPaths.Count == 0)
                    return;

                queuedAtTicks = lastChangeUtcTicks;
                double elapsedSeconds = queuedAtTicks == 0
                    ? DebounceSeconds
                    : new TimeSpan(DateTime.UtcNow.Ticks - queuedAtTicks).TotalSeconds;
                if (elapsedSeconds < DebounceSeconds)
                    return;

                changedPaths = new List<string>(PendingPaths);
                PendingPaths.Clear();
                forceFullReload = false;
            }

            if (!PrepareOpenScenesForExternalReload(changedPaths))
            {
                Requeue(changedPaths, fullReload, BusyRetrySeconds);
                return;
            }

            SuppressWatcherEvents(WatcherSuppressionSeconds);

            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
            }
            catch (Exception exception)
            {
                Debug.LogError("[DuoCurtain Auto Reload] Reload failed: " + exception);
                Requeue(changedPaths, fullReload, BusyRetrySeconds);
                return;
            }

            string scope = fullReload
                ? "the project"
                : changedPaths.Count + " changed path(s)";
            Debug.Log(
                "[DuoCurtain Auto Reload] Reloaded " +
                scope +
                " without a confirmation dialog.");
        }

        private static bool PrepareOpenScenesForExternalReload(List<string> changedPaths)
        {
            HashSet<string> changedScenePaths = CollectChangedSceneAssetPaths(changedPaths);
            if (changedScenePaths.Count == 0)
                return true;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return true;

            SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
            bool reloadOpenScenes = false;
            for (int i = 0; i < setup.Length; i++)
            {
                if (setup[i].isLoaded &&
                    changedScenePaths.Contains(NormalizePath(setup[i].path)))
                {
                    reloadOpenScenes = true;
                    break;
                }
            }

            if (!reloadOpenScenes)
                return true;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() ||
                    !scene.isLoaded ||
                    !scene.isDirty ||
                    string.IsNullOrWhiteSpace(scene.path))
                {
                    continue;
                }

                string scenePath = NormalizePath(scene.path);
                if (changedScenePaths.Contains(scenePath))
                {
                    if (!BackupDirtySceneBeforeExternalReload(scene))
                        return false;
                }
                else if (!EditorSceneManager.SaveScene(scene))
                {
                    Debug.LogError(
                        "[DuoCurtain Auto Reload] Could not save dirty scene before reload: " +
                        scene.path);
                    return false;
                }
            }

            try
            {
                SuppressWatcherEvents(WatcherSuppressionSeconds);

                int loadedSceneCount = 0;
                string singleChangedScenePath = null;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    if (!scene.IsValid() ||
                        !scene.isLoaded ||
                        string.IsNullOrWhiteSpace(scene.path))
                    {
                        continue;
                    }

                    loadedSceneCount++;
                    if (changedScenePaths.Contains(NormalizePath(scene.path)))
                        singleChangedScenePath = scene.path;
                }

                if (loadedSceneCount == 1 && !string.IsNullOrWhiteSpace(singleChangedScenePath))
                {
                    EditorSceneManager.OpenScene(singleChangedScenePath, OpenSceneMode.Single);
                }
                else
                {
                    for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
                    {
                        Scene scene = SceneManager.GetSceneAt(i);
                        if (!scene.IsValid() ||
                            !scene.isLoaded ||
                            string.IsNullOrWhiteSpace(scene.path))
                        {
                            continue;
                        }

                        if (changedScenePaths.Contains(NormalizePath(scene.path)))
                            EditorSceneManager.CloseScene(scene, true);
                    }

                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                }

                SceneView.RepaintAll();
                Debug.Log(
                    "[DuoCurtain Auto Reload] Reloaded externally changed open scene(s) from disk.");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[DuoCurtain Auto Reload] Could not reload the open scene setup: " +
                    exception);
                return false;
            }
        }

        private static bool BackupDirtySceneBeforeExternalReload(Scene scene)
        {
            string sceneAbsolutePath = Path.Combine(ProjectRoot, scene.path);
            if (!File.Exists(sceneAbsolutePath))
                return false;

            string backupDirectory =
                Path.Combine(ProjectRoot, "Temp", "DuoCurtainAutoReloadBackups");
            Directory.CreateDirectory(backupDirectory);

            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string sceneName = Path.GetFileNameWithoutExtension(scene.path);
            string externalSnapshotPath = Path.Combine(
                backupDirectory,
                sceneName + "-ExternalSnapshot-" + Guid.NewGuid().ToString("N") + ".unity");
            string editorBackupPath = Path.Combine(
                backupDirectory,
                sceneName + "-EditorBackup-" + timestamp + ".unity");
            bool diskWasOverwritten = false;
            bool externalRestored = false;

            try
            {
                SuppressWatcherEvents(WatcherSuppressionSeconds);
                File.Copy(sceneAbsolutePath, externalSnapshotPath, true);

                if (!EditorSceneManager.SaveScene(scene))
                {
                    Debug.LogError(
                        "[DuoCurtain Auto Reload] Could not preserve dirty scene: " +
                        scene.path);
                    return false;
                }

                diskWasOverwritten = true;
                File.Copy(sceneAbsolutePath, editorBackupPath, true);
                File.Copy(externalSnapshotPath, sceneAbsolutePath, true);
                externalRestored = true;
                Debug.LogWarning(
                    "[DuoCurtain Auto Reload] Preserved unsaved editor scene changes at: " +
                    editorBackupPath);
                return true;
            }
            catch (Exception exception)
            {
                if (diskWasOverwritten && !externalRestored)
                {
                    try
                    {
                        File.Copy(externalSnapshotPath, sceneAbsolutePath, true);
                        externalRestored = true;
                        EditorSceneManager.MarkSceneDirty(scene);
                    }
                    catch (Exception restoreException)
                    {
                        Debug.LogError(
                            "[DuoCurtain Auto Reload] External scene snapshot remains at " +
                            externalSnapshotPath +
                            " because restoring it failed: " +
                            restoreException);
                    }
                }

                Debug.LogError(
                    "[DuoCurtain Auto Reload] Scene backup failed for " +
                    scene.path +
                    ": " +
                    exception);
                return false;
            }
            finally
            {
                if (!diskWasOverwritten || externalRestored)
                    TryDeleteFile(externalSnapshotPath);
            }
        }

        private static HashSet<string> CollectChangedSceneAssetPaths(List<string> changedPaths)
        {
            HashSet<string> result =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < changedPaths.Count; i++)
            {
                string assetPath = ToProjectRelativePath(changedPaths[i]);
                if (!string.IsNullOrWhiteSpace(assetPath) &&
                    assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(changedPaths[i]))
                {
                    result.Add(NormalizePath(assetPath));
                }
            }

            return result;
        }

        private static void OnSceneSaved(Scene scene)
        {
            SuppressWatcherEvents(WatcherSuppressionSeconds);
        }

        private static bool IsEditorBusy()
        {
            return EditorApplication.isCompiling ||
                   EditorApplication.isUpdating ||
                   EditorApplication.isPlayingOrWillChangePlaymode ||
                   BuildPipeline.isBuildingPlayer;
        }

        private static bool IsWatcherSuppressed()
        {
            return Interlocked.Read(ref suppressWatcherUntilUtcTicks) > DateTime.UtcNow.Ticks;
        }

        private static void SuppressWatcherEvents(double seconds)
        {
            long targetTicks = DateTime.UtcNow.AddSeconds(seconds).Ticks;
            Interlocked.Exchange(ref suppressWatcherUntilUtcTicks, targetTicks);
        }

        private static void Requeue(
            List<string> changedPaths,
            bool fullReload,
            double delaySeconds)
        {
            lock (PendingLock)
            {
                for (int i = 0; i < changedPaths.Count; i++)
                    PendingPaths.Add(changedPaths[i]);

                forceFullReload |= fullReload;
                lastChangeUtcTicks =
                    DateTime.UtcNow.AddSeconds(delaySeconds - DebounceSeconds).Ticks;
            }
        }

        private static void ClearPendingChanges()
        {
            lock (PendingLock)
            {
                PendingPaths.Clear();
                forceFullReload = false;
                lastChangeUtcTicks = 0;
            }
        }

        private static bool ShouldIgnorePath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return true;

            string normalizedPath = NormalizePath(fullPath);
            if (normalizedPath.Contains("/Library/", StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.Contains("/Temp/", StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.Contains("/Logs/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string fileName = Path.GetFileName(fullPath);
            if (IsMacOsDuplicateCopy(fileName))
                return true;

            return fileName.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith("~", StringComparison.Ordinal) ||
                   fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith(".swp", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith(".lock", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMacOsDuplicateCopy(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            return fileName.EndsWith(" 2", StringComparison.Ordinal) ||
                   fileName.EndsWith(" 2.meta", StringComparison.Ordinal) ||
                   fileName.Contains(" 2.", StringComparison.Ordinal);
        }

        private static string ToProjectRelativePath(string fullPath)
        {
            string normalizedRoot = NormalizePath(ProjectRoot).TrimEnd('/');
            string normalizedFullPath = NormalizePath(Path.GetFullPath(fullPath));
            string prefix = normalizedRoot + "/";

            if (!normalizedFullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;

            return normalizedFullPath.Substring(prefix.Length);
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Replace('\\', '/');
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Temp backups may be cleaned up on the next editor launch.
            }
        }

        private static void Shutdown()
        {
            EditorApplication.update -= Tick;
            EditorApplication.quitting -= Shutdown;
            AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            EditorSceneManager.sceneSaved -= OnSceneSaved;
            DisposeWatchers();
            ReleaseAutoRefreshLock();
            initialized = false;
        }

        private static void AcquireAutoRefreshLock()
        {
            if (!IsEnabled || ownsAutoRefreshLock)
                return;

            // The project watcher performs refreshes after open scenes are prepared.
            AssetDatabase.DisallowAutoRefresh();
            ownsAutoRefreshLock = true;
        }

        private static void ReleaseAutoRefreshLock()
        {
            if (!ownsAutoRefreshLock)
                return;

            AssetDatabase.AllowAutoRefresh();
            ownsAutoRefreshLock = false;
        }

        private static void DisposeWatchers()
        {
            for (int i = 0; i < Watchers.Count; i++)
            {
                FileSystemWatcher watcher = Watchers[i];
                if (watcher == null)
                    continue;

                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                catch
                {
                    // The watcher may already be disposed during an assembly reload.
                }
            }

            Watchers.Clear();
        }
    }
}
#endif
