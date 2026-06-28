using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class TilePieceDefinition : MonoBehaviour
{
    private const string GeneratedVisualRootName = "__Generated Tile Cell Visuals";
    private const string GeneratedVisualName = "Visual";

    public enum PlacementLayer
    {
        Tile,
        Window
    }

    [System.Serializable]
    public class ShopData
    {
        public string displayName = "Tile";
        [Min(0)]
        public int price = 1;
    }

    [Header("Footprint")]
    [Tooltip("以根物体为原点的格子偏移。Vector2Int.zero 表示根格。")]
    public List<Vector2Int> cells = new List<Vector2Int> { Vector2Int.zero };
    [Tooltip("开启后，自动根据子物体的本地位置生成 cells。适合由多个方块子物体组成的 prefab。")]
    public bool autoGenerateCellsFromChildren = true;
    [Tooltip("自动生成时使用的格子尺寸。子物体本地坐标会按这个尺寸换算成格子坐标。")]
    public Vector2 childCellSize = Vector2.one;
    [Tooltip("自动生成时，是否把根物体所在格子 (0,0) 也加入 cells。")]
    public bool includeRootCell = true;

    [Header("Shop")]
    public ShopData shopData = new ShopData();

    [Header("Placement")]
    [Tooltip("当前只作为分类标签使用；放置规则由 TilePlacementGrid 统一决定。")]
    public PlacementLayer placementLayer = PlacementLayer.Tile;

    [Header("Scene Registration")]
    [Tooltip("场景里已经摆好的地图块，游戏开始时是否自动注册到网格中。")]
    public bool registerOnStart = true;

    [Header("Cell Visuals")]
    [Tooltip("运行时用 cells 生成 5x5 的房间视觉 tile。房间画面与程序 footprint 共用同一份 cells。")]
    public bool buildVisualsFromCells = true;
    [Tooltip("生成 cell 视觉后隐藏根物体上的旧 SpriteRenderer，避免旧大图和真实格子边缘不一致。")]
    public bool hideRootRoomSpriteRenderers = true;
    [Tooltip("生成每个 cell 的 BoxCollider2D，让物理边缘也按 5x5 tile 对齐。")]
    public bool buildCellColliders = true;
    [Tooltip("生成 cell collider 后禁用根物体上的旧 BoxCollider2D，避免旧矩形覆盖房间之间的空隙。")]
    public bool disableRootRoomColliders = true;
    [Tooltip("未指定时会复用根物体 SpriteRenderer 的 sprite。")]
    public Sprite cellVisualSprite;
    public Color cellVisualColor = Color.white;
    public Vector3 cellVisualLocalOffset = Vector3.zero;

    public IReadOnlyList<Vector2Int> Cells => cells;

    public IEnumerable<Vector2Int> GetOccupiedCells(Vector2Int anchorCell)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            yield return anchorCell + cells[i];
        }
    }

    void OnValidate()
    {
        childCellSize = new Vector2(
            TilePlacementGrid.SnapPositiveToTileMultiple(childCellSize.x),
            TilePlacementGrid.SnapPositiveToTileMultiple(childCellSize.y)
        );

        if (autoGenerateCellsFromChildren)
            RegenerateCellsFromChildren();

        if (cells == null)
        {
            cells = new List<Vector2Int> { Vector2Int.zero };
            return;
        }

        if (cells.Count == 0)
            cells.Add(Vector2Int.zero);

        HashSet<Vector2Int> seen = new HashSet<Vector2Int>();
        for (int i = cells.Count - 1; i >= 0; i--)
        {
            if (!seen.Add(cells[i]))
                cells.RemoveAt(i);
        }
    }

    void Awake()
    {
        RebuildCellVisuals();
    }

    [ContextMenu("Rebuild Cell Visuals")]
    public void RebuildCellVisuals()
    {
        if (!ShouldBuildCellVisuals())
            return;

        SpriteRenderer templateRenderer = GetRootSpriteRenderer();
        Sprite sprite = cellVisualSprite != null ? cellVisualSprite : templateRenderer != null ? templateRenderer.sprite : null;
        Transform existingRoot = FindGeneratedVisualRoot();

        if (CellVisualsAreCurrent(existingRoot, sprite, templateRenderer))
        {
            ApplySourceRendererVisibility();
            ApplyRootColliderVisibility();
            return;
        }

        DestroyGeneratedVisualRoot(existingRoot);
        Transform visualRoot = CreateGeneratedVisualRoot();

        for (int i = 0; i < cells.Count; i++)
            CreateGeneratedCell(visualRoot, cells[i], sprite, templateRenderer);

        ApplySourceRendererVisibility();
        ApplyRootColliderVisibility();
    }

    [ContextMenu("Regenerate Cells From Children")]
    public void RegenerateCellsFromChildren()
    {
        if (cells == null)
            cells = new List<Vector2Int>();
        else
            cells.Clear();

        float sizeX = Mathf.Max(0.0001f, childCellSize.x);
        float sizeY = Mathf.Max(0.0001f, childCellSize.y);

        if (includeRootCell)
            cells.Add(Vector2Int.zero);

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            Vector3 localPosition = child.localPosition;

            Vector2Int cell = new Vector2Int(
                Mathf.RoundToInt(localPosition.x / sizeX),
                Mathf.RoundToInt(localPosition.y / sizeY)
            );

            cells.Add(cell);
        }
    }

    private bool ShouldBuildCellVisuals()
    {
        return buildVisualsFromCells &&
               placementLayer == PlacementLayer.Tile &&
               cells != null &&
               cells.Count > 0;
    }

    private Transform CreateGeneratedVisualRoot()
    {
        GameObject visualRootObject = new GameObject(GeneratedVisualRootName);
        Transform visualRoot = visualRootObject.transform;
        visualRoot.SetParent(transform, false);
        visualRoot.localPosition = Vector3.zero;
        visualRoot.localRotation = Quaternion.identity;
        visualRoot.localScale = GetInverseLocalScale(transform.localScale);
        return visualRoot;
    }

    private void CreateGeneratedCell(
        Transform visualRoot,
        Vector2Int cell,
        Sprite sprite,
        SpriteRenderer templateRenderer)
    {
        GameObject cellObject = new GameObject(GetGeneratedCellName(cell));
        Transform cellTransform = cellObject.transform;
        cellTransform.SetParent(visualRoot, false);
        cellTransform.localPosition = new Vector3(
            cell.x * childCellSize.x,
            cell.y * childCellSize.y,
            0f
        ) + cellVisualLocalOffset;
        cellTransform.localRotation = Quaternion.identity;
        cellTransform.localScale = Vector3.one;

        if (buildCellColliders)
        {
            BoxCollider2D collider = cellObject.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(childCellSize.x, childCellSize.y);
            collider.offset = Vector2.zero;
        }

        if (sprite == null)
            return;

        GameObject visualObject = new GameObject(GeneratedVisualName);
        Transform visualTransform = visualObject.transform;
        visualTransform.SetParent(cellTransform, false);
        visualTransform.localPosition = Vector3.zero;
        visualTransform.localRotation = Quaternion.identity;
        visualTransform.localScale = GetSpriteScaleForCell(sprite);

        SpriteRenderer renderer = visualObject.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = cellVisualColor;

        if (templateRenderer == null)
            return;

        renderer.sharedMaterial = templateRenderer.sharedMaterial;
        renderer.sortingLayerID = templateRenderer.sortingLayerID;
        renderer.sortingOrder = templateRenderer.sortingOrder;
        renderer.maskInteraction = templateRenderer.maskInteraction;
        renderer.flipX = templateRenderer.flipX;
        renderer.flipY = templateRenderer.flipY;
    }

    private bool CellVisualsAreCurrent(Transform visualRoot, Sprite sprite, SpriteRenderer templateRenderer)
    {
        if (visualRoot == null || visualRoot.childCount != cells.Count)
            return false;

        if (!Approximately(visualRoot.localScale, GetInverseLocalScale(transform.localScale)))
            return false;

        for (int i = 0; i < cells.Count; i++)
        {
            Vector2Int cell = cells[i];
            Transform cellTransform = visualRoot.Find(GetGeneratedCellName(cell));
            if (cellTransform == null)
                return false;

            Vector3 expectedPosition = new Vector3(
                cell.x * childCellSize.x,
                cell.y * childCellSize.y,
                0f
            ) + cellVisualLocalOffset;
            if (!Approximately(cellTransform.localPosition, expectedPosition))
                return false;

            if (buildCellColliders)
            {
                BoxCollider2D collider = cellTransform.GetComponent<BoxCollider2D>();
                if (collider == null ||
                    !Approximately(collider.size.x, childCellSize.x) ||
                    !Approximately(collider.size.y, childCellSize.y))
                {
                    return false;
                }
            }

            Transform visualTransform = cellTransform.Find(GeneratedVisualName);
            if (sprite == null)
            {
                if (visualTransform != null)
                    return false;

                continue;
            }

            if (visualTransform == null)
                return false;

            SpriteRenderer renderer = visualTransform.GetComponent<SpriteRenderer>();
            if (renderer == null || renderer.sprite != sprite)
                return false;

            if (!Approximately(visualTransform.localScale, GetSpriteScaleForCell(sprite)))
                return false;

            if (templateRenderer != null &&
                (renderer.sortingLayerID != templateRenderer.sortingLayerID ||
                 renderer.sortingOrder != templateRenderer.sortingOrder))
            {
                return false;
            }
        }

        return true;
    }

    private Transform FindGeneratedVisualRoot()
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child != null && child.name == GeneratedVisualRootName)
                return child;
        }

        return null;
    }

    private SpriteRenderer GetRootSpriteRenderer()
    {
        SpriteRenderer[] renderers = GetComponents<SpriteRenderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                return renderers[i];
        }

        return null;
    }

    private void ApplySourceRendererVisibility()
    {
        if (!hideRootRoomSpriteRenderers)
            return;

        SpriteRenderer[] renderers = GetComponents<SpriteRenderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = false;
        }
    }

    private void ApplyRootColliderVisibility()
    {
        if (!disableRootRoomColliders)
            return;

        BoxCollider2D[] colliders = GetComponents<BoxCollider2D>();
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }
    }

    private void DestroyGeneratedVisualRoot(Transform visualRoot)
    {
        if (visualRoot == null)
            return;

        if (Application.isPlaying)
            Destroy(visualRoot.gameObject);
        else
            DestroyImmediate(visualRoot.gameObject);
    }

    private Vector3 GetSpriteScaleForCell(Sprite sprite)
    {
        if (sprite == null)
            return Vector3.one;

        Vector2 spriteSize = sprite.bounds.size;
        float scaleX = Mathf.Abs(spriteSize.x) <= 0.0001f ? 1f : childCellSize.x / spriteSize.x;
        float scaleY = Mathf.Abs(spriteSize.y) <= 0.0001f ? 1f : childCellSize.y / spriteSize.y;
        return new Vector3(scaleX, scaleY, 1f);
    }

    private static Vector3 GetInverseLocalScale(Vector3 scale)
    {
        return new Vector3(
            Mathf.Abs(scale.x) <= 0.0001f ? 1f : 1f / scale.x,
            Mathf.Abs(scale.y) <= 0.0001f ? 1f : 1f / scale.y,
            Mathf.Abs(scale.z) <= 0.0001f ? 1f : 1f / scale.z
        );
    }

    private static string GetGeneratedCellName(Vector2Int cell)
    {
        return "Tile Cell " + cell.x + "," + cell.y;
    }

    private static bool Approximately(Vector3 a, Vector3 b)
    {
        return Approximately(a.x, b.x) &&
               Approximately(a.y, b.y) &&
               Approximately(a.z, b.z);
    }

    private static bool Approximately(float a, float b)
    {
        return Mathf.Abs(a - b) <= 0.0001f;
    }
}
