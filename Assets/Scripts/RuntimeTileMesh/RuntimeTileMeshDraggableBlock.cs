using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    [RequireComponent(typeof(RuntimeTileMeshView))]
    [DisallowMultipleComponent]
    public class RuntimeTileMeshDraggableBlock : MonoBehaviour
    {
        public Color placedColor = Color.white;
        public Color hoverColor = new Color(1f, 0.08f, 0.04f, 1f);
        public Color selectedColor = new Color(0.1f, 0.38f, 1f, 1f);
        [Min(0.01f)]
        public float colorLerpSpeed = 7f;

        private readonly List<Renderer> renderers = new List<Renderer>();
        private MaterialPropertyBlock propertyBlock;

        private RuntimeTileMeshView view;
        private Color currentColor;
        private bool hovered;
        private bool selected;

        public RuntimeTileMeshView View
        {
            get
            {
                ResolveView();
                return view;
            }
        }

        void Awake()
        {
            ResolveView();
            currentColor = placedColor;
            RefreshRenderers();
            ApplyColor(currentColor);
        }

        void Start()
        {
            RefreshRenderers();
            ApplyColor(currentColor);
        }

        void Update()
        {
            Color target = selected ? selectedColor : hovered ? hoverColor : placedColor;
            float t = 1f - Mathf.Exp(-colorLerpSpeed * Time.deltaTime);
            currentColor = Color.Lerp(currentColor, target, t);
            ApplyColor(currentColor);
        }

        public void SetHovered(bool value)
        {
            hovered = value;
        }

        public void SetSelected(bool value)
        {
            selected = value;
            if (selected)
                hovered = false;
        }

        public void SetSortingOrder(int sortingOrder)
        {
            ResolveView();
            if (view != null)
                view.sortingOrder = sortingOrder;

            RefreshRenderers();
            for (int i = 0; i < renderers.Count; i++)
                renderers[i].sortingOrder = sortingOrder;
        }

        public void RebuildAndRefresh()
        {
            ResolveView();
            if (view == null)
                return;

            view.Rebuild();
            RefreshRenderers();
            ApplyColor(currentColor);
        }

        public bool OverlapsOrSharesEdgeWith(RuntimeTileMeshDraggableBlock other, float gridSize, Vector2 gridOrigin)
        {
            if (other == null || other == this)
                return false;

            HashSet<Vector2Int> ownCells = GetWorldCells(gridSize, gridOrigin);
            HashSet<Vector2Int> otherCells = other.GetWorldCells(gridSize, gridOrigin);
            return CellSetsOverlapOrShareEdge(ownCells, otherCells);
        }

        public static bool CellSetsOverlapOrShareEdge(
            ICollection<Vector2Int> ownCells,
            ICollection<Vector2Int> otherCells)
        {
            if (ownCells == null || otherCells == null || ownCells.Count == 0 || otherCells.Count == 0)
                return false;

            HashSet<Vector2Int> ownLookup = ownCells as HashSet<Vector2Int> ?? new HashSet<Vector2Int>(ownCells);
            foreach (Vector2Int cell in otherCells)
            {
                if (ownLookup.Contains(cell))
                    return true;

                if (ownLookup.Contains(new Vector2Int(cell.x - 1, cell.y)) ||
                    ownLookup.Contains(new Vector2Int(cell.x + 1, cell.y)) ||
                    ownLookup.Contains(new Vector2Int(cell.x, cell.y - 1)) ||
                    ownLookup.Contains(new Vector2Int(cell.x, cell.y + 1)))
                    return true;
            }

            return false;
        }

        public void Absorb(RuntimeTileMeshDraggableBlock other, float gridSize, Vector2 gridOrigin)
        {
            if (other == null || other == this)
                return;

            HashSet<Vector2Int> mergedCells = GetWorldCells(gridSize, gridOrigin);
            foreach (Vector2Int cell in other.GetWorldCells(gridSize, gridOrigin))
                mergedCells.Add(cell);

            ApplyWorldCells(mergedCells, gridSize, gridOrigin);
        }

        public HashSet<Vector2Int> GetWorldCells(float gridSize, Vector2 gridOrigin)
        {
            ResolveView();
            HashSet<Vector2Int> worldCells = new HashSet<Vector2Int>();
            if (view == null)
                return worldCells;

            Vector2Int rootCell = WorldToCell(transform.position, gridSize, gridOrigin);
            for (int i = 0; i < view.tiles.Count; i++)
                worldCells.Add(rootCell + view.tiles[i]);

            return worldCells;
        }

        public void ApplyWorldCells(HashSet<Vector2Int> worldCells, float gridSize, Vector2 gridOrigin)
        {
            ResolveView();
            if (view == null || worldCells == null || worldCells.Count == 0)
                return;

            Vector2Int min = FindMinimumCell(worldCells);
            List<Vector2Int> localCells = new List<Vector2Int>(worldCells.Count);
            foreach (Vector2Int cell in worldCells)
                localCells.Add(cell - min);

            localCells.Sort(CompareCells);
            view.tiles = localCells;
            transform.position = CellToWorld(min, gridSize, gridOrigin, transform.position.z);
            RebuildAndRefresh();
        }

        private void RefreshRenderers()
        {
            renderers.Clear();
            GetComponentsInChildren(true, renderers);
        }

        private void ApplyColor(Color color)
        {
            if (propertyBlock == null)
                propertyBlock = new MaterialPropertyBlock();

            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor("_BaseColor", color);
                propertyBlock.SetColor("_Color", color);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        private void ResolveView()
        {
            if (view == null)
                view = GetComponent<RuntimeTileMeshView>();
        }

        private static Vector2Int FindMinimumCell(HashSet<Vector2Int> cells)
        {
            bool hasValue = false;
            Vector2Int minimum = Vector2Int.zero;
            foreach (Vector2Int cell in cells)
            {
                if (!hasValue)
                {
                    minimum = cell;
                    hasValue = true;
                    continue;
                }

                minimum = new Vector2Int(
                    Mathf.Min(minimum.x, cell.x),
                    Mathf.Min(minimum.y, cell.y));
            }

            return minimum;
        }

        private static int CompareCells(Vector2Int a, Vector2Int b)
        {
            int yCompare = a.y.CompareTo(b.y);
            return yCompare != 0 ? yCompare : a.x.CompareTo(b.x);
        }

        public static Vector2Int WorldToCell(Vector3 worldPosition, float gridSize, Vector2 gridOrigin)
        {
            float safeGridSize = Mathf.Max(0.0001f, Mathf.Abs(gridSize));
            return new Vector2Int(
                Mathf.RoundToInt((worldPosition.x - gridOrigin.x) / safeGridSize),
                Mathf.RoundToInt((worldPosition.y - gridOrigin.y) / safeGridSize));
        }

        public static Vector3 CellToWorld(Vector2Int cell, float gridSize, Vector2 gridOrigin, float z)
        {
            float safeGridSize = Mathf.Max(0.0001f, Mathf.Abs(gridSize));
            return new Vector3(
                gridOrigin.x + cell.x * safeGridSize,
                gridOrigin.y + cell.y * safeGridSize,
                z);
        }
    }
}
