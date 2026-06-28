#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DuoCurtain.Editor
{
    public static class GridIntegerNormalizationUtility
    {
        private const int TileUnit = TilePlacementGrid.DefaultTileUnit;
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
            changed |= SetVector2(ref definition.childCellSize, new Vector2(TileUnit, TileUnit));

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
            if (grid.tileUnit != TileUnit)
            {
                grid.tileUnit = TileUnit;
                changed = true;
            }

            changed |= SetVector2(ref grid.cellSize, new Vector2(TileUnit, TileUnit));
            changed |= SetVector2(ref grid.origin, new Vector2(
                TilePlacementGrid.SnapToTileMultiple(grid.origin.x, TileUnit),
                TilePlacementGrid.SnapToTileMultiple(grid.origin.y, TileUnit)
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
                TilePlacementGrid.SnapPositiveToTileMultiple(currentWorldX, TileUnit),
                TilePlacementGrid.SnapPositiveToTileMultiple(currentWorldY, TileUnit)
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
            position.x = Mathf.Round(position.x);
            position.y = Mathf.Round(position.y);
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
            int width = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(worldScale.x) / TileUnit));
            int height = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(worldScale.y) / TileUnit));

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
                scale.x = Mathf.Sign(scale.x == 0f ? 1f : scale.x) * TilePlacementGrid.SnapPositiveToTileMultiple(Mathf.Abs(scale.x), TileUnit);
                scale.y = Mathf.Sign(scale.y == 0f ? 1f : scale.y) * TilePlacementGrid.SnapPositiveToTileMultiple(Mathf.Abs(scale.y), TileUnit);
                if (!Approximately(transform.localScale, scale))
                {
                    transform.localScale = scale;
                    rendererChanged = true;
                }

                Vector3 position = transform.localPosition;
                position.x = Mathf.Round(position.x);
                position.y = Mathf.Round(position.y);
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
}
#endif
