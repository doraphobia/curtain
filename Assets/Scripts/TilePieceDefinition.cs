using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class TilePieceDefinition : MonoBehaviour
{
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
}
