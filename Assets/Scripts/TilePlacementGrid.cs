using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class TilePlacementGrid : MonoBehaviour
{
    [Header("Grid")]
    public Vector2 cellSize = Vector2.one;
    public Vector2 origin;

    private readonly HashSet<Vector2Int> occupiedCells = new HashSet<Vector2Int>();

    void Start()
    {
        RegisterExistingTiles();
    }

    public Vector2Int WorldToCell(Vector3 worldPosition)
    {
        Vector2 local = (Vector2)worldPosition - origin;
        return new Vector2Int(
            Mathf.RoundToInt(local.x / Mathf.Max(0.0001f, cellSize.x)),
            Mathf.RoundToInt(local.y / Mathf.Max(0.0001f, cellSize.y))
        );
    }

    public Vector3 CellToWorld(Vector2Int cell)
    {
        return new Vector3(
            origin.x + cell.x * cellSize.x,
            origin.y + cell.y * cellSize.y,
            0f
        );
    }

    public bool CanPlace(TilePieceDefinition definition, Vector2Int anchorCell)
    {
        if (definition == null || definition.Cells == null || definition.Cells.Count == 0)
            return false;

        HashSet<Vector2Int> candidateCells = new HashSet<Vector2Int>();
        for (int i = 0; i < definition.Cells.Count; i++)
        {
            candidateCells.Add(anchorCell + definition.Cells[i]);
        }

        if (occupiedCells.Count == 0)
            return true;

        foreach (Vector2Int cell in candidateCells)
        {
            if (occupiedCells.Contains(cell))
                return true;

            if (TouchesOtherOccupiedCell(cell, candidateCells))
                return true;
        }

        return false;
    }

    public bool TryPlace(TilePieceDefinition definition, Vector2Int anchorCell)
    {
        if (!CanPlace(definition, anchorCell))
            return false;

        RegisterPiece(definition, anchorCell);
        return true;
    }

    public void RegisterPiece(TilePieceDefinition definition, Vector2Int anchorCell)
    {
        if (definition == null || definition.Cells == null)
            return;

        for (int i = 0; i < definition.Cells.Count; i++)
            occupiedCells.Add(anchorCell + definition.Cells[i]);
    }

    public int OccupiedCount()
    {
        return occupiedCells.Count;
    }

    private void RegisterExistingTiles()
    {
        TilePieceDefinition[] definitions = FindObjectsByType<TilePieceDefinition>(FindObjectsSortMode.None);
        for (int i = 0; i < definitions.Length; i++)
        {
            TilePieceDefinition definition = definitions[i];
            if (definition == null || !definition.registerOnStart)
                continue;

            Vector2Int anchorCell = WorldToCell(definition.transform.position);
            RegisterPiece(definition, anchorCell);
        }
    }

    private bool TouchesOtherOccupiedCell(Vector2Int cell, HashSet<Vector2Int> candidateCells)
    {
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0)
                    continue;

                Vector2Int neighbor = new Vector2Int(cell.x + x, cell.y + y);
                if (candidateCells.Contains(neighbor))
                    continue;

                if (occupiedCells.Contains(neighbor))
                    return true;
            }
        }

        return false;
    }
}
