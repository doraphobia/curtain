using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    public static class TileConnectivity
    {
        private static readonly Vector2Int[] FourNeighbors =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1)
        };

        public static List<List<Vector2Int>> SplitIntoConnectedComponents(IEnumerable<Vector2Int> tiles)
        {
            HashSet<Vector2Int> remaining = TileOccupancy.ToSet(tiles);
            List<List<Vector2Int>> components = new List<List<Vector2Int>>();
            Queue<Vector2Int> queue = new Queue<Vector2Int>();

            while (remaining.Count > 0)
            {
                Vector2Int start = default(Vector2Int);
                foreach (Vector2Int tile in remaining)
                {
                    start = tile;
                    break;
                }

                List<Vector2Int> component = new List<Vector2Int>();
                remaining.Remove(start);
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    Vector2Int tile = queue.Dequeue();
                    component.Add(tile);

                    for (int i = 0; i < FourNeighbors.Length; i++)
                    {
                        Vector2Int neighbor = tile + FourNeighbors[i];
                        if (!remaining.Remove(neighbor))
                            continue;

                        queue.Enqueue(neighbor);
                    }
                }

                components.Add(component);
            }

            return components;
        }
    }
}
