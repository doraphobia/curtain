using System;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    public enum RuntimeTileMeshProjectionMode
    {
        StretchToBounds = 0,
        ObjectSpace = 1,
        WorldTile = 2,
        AnchoredTile = 3
    }

    [Serializable]
    public sealed class RuntimeTileMeshVisualState
    {
        public Material material;
        public RuntimeTileMeshProjectionMode projectionMode = RuntimeTileMeshProjectionMode.WorldTile;
        public Vector2 cellSize = Vector2.one;
        public Vector2 motionTileSize = new Vector2(3f, 3f);
        public Vector2 patternOffset = Vector2.zero;
        [Min(0.0001f)]
        public float patternScale = 1f;
        public float timeOffset;
        [Range(0f, 1f)]
        public float transition = 1f;
        [Range(0f, 1f)]
        public float patternIntensity = 0.35f;
        [Range(0.001f, 0.5f)]
        public float lineWidth = 0.055f;
        public Vector2 anchorWorldPosition = Vector2.zero;

        public void Sanitize()
        {
            cellSize = SanitizeVector(cellSize, Vector2.one);
            motionTileSize = SanitizeVector(motionTileSize, new Vector2(3f, 3f));
            patternScale = Mathf.Max(0.0001f, patternScale);
            transition = Mathf.Clamp01(transition);
            patternIntensity = Mathf.Clamp01(patternIntensity);
            lineWidth = Mathf.Clamp(lineWidth, 0.001f, 0.5f);
        }

        private static Vector2 SanitizeVector(Vector2 value, Vector2 fallback)
        {
            if (Mathf.Abs(value.x) <= 0.0001f)
                value.x = fallback.x;
            if (Mathf.Abs(value.y) <= 0.0001f)
                value.y = fallback.y;

            value.x = Mathf.Abs(value.x);
            value.y = Mathf.Abs(value.y);
            return value;
        }
    }
}
