using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    public static class TileOccupancy
    {
        public static List<Vector2Int> FromBoolGrid(bool[,] occupied)
        {
            List<Vector2Int> tiles = new List<Vector2Int>();
            if (occupied == null)
                return tiles;

            int width = occupied.GetLength(0);
            int height = occupied.GetLength(1);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (occupied[x, y])
                        tiles.Add(new Vector2Int(x, y));
                }
            }

            return tiles;
        }

        public static HashSet<Vector2Int> ToSet(IEnumerable<Vector2Int> tiles)
        {
            HashSet<Vector2Int> set = new HashSet<Vector2Int>();
            if (tiles == null)
                return set;

            foreach (Vector2Int tile in tiles)
                set.Add(tile);

            return set;
        }
    }
}
