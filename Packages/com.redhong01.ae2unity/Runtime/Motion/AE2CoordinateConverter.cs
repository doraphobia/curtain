using UnityEngine;

namespace AE2Unity
{
    public static class AE2CoordinateConverter
    {
        public static Vector2 AeTopLeftPixelsToShaderPixels(Vector2 aePosition)
        {
            return aePosition;
        }

        public static Vector4 CompSizeVector(int width, int height)
        {
            var safeWidth = Mathf.Max(1, width);
            var safeHeight = Mathf.Max(1, height);
            return new Vector4(safeWidth, safeHeight, 1f / safeWidth, 1f / safeHeight);
        }
    }
}
