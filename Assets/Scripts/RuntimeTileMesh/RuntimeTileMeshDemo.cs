using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    [RequireComponent(typeof(RuntimeTileMeshView))]
    [DisallowMultipleComponent]
    public class RuntimeTileMeshDemo : MonoBehaviour
    {
        public enum DemoShape
        {
            Single,
            OneByThree,
            L,
            T,
            Z,
            RingWithHole,
            DiagonalTouch
        }

        public DemoShape shape = DemoShape.L;
        public bool applyShapeOnStart = true;
        public bool rebuildOnValidate = true;

        private RuntimeTileMeshView view;

        void Awake()
        {
            ResolveView();
        }

        void Start()
        {
            if (applyShapeOnStart)
                ApplyShape();
        }

        void OnValidate()
        {
            if (!rebuildOnValidate)
                return;

            ResolveView();
            if (view != null)
                view.tiles = CreateShape(shape);
        }

        [ContextMenu("Apply Demo Shape")]
        public void ApplyShape()
        {
            ResolveView();
            if (view == null)
                return;

            view.tiles = CreateShape(shape);
            view.Rebuild();
        }

        public static List<Vector2Int> CreateShape(DemoShape demoShape)
        {
            switch (demoShape)
            {
                case DemoShape.Single:
                    return new List<Vector2Int> { new Vector2Int(0, 0) };
                case DemoShape.OneByThree:
                    return new List<Vector2Int>
                    {
                        new Vector2Int(0, 0),
                        new Vector2Int(1, 0),
                        new Vector2Int(2, 0)
                    };
                case DemoShape.L:
                    return new List<Vector2Int>
                    {
                        new Vector2Int(0, 0),
                        new Vector2Int(0, 1),
                        new Vector2Int(0, 2),
                        new Vector2Int(1, 0),
                        new Vector2Int(2, 0)
                    };
                case DemoShape.T:
                    return new List<Vector2Int>
                    {
                        new Vector2Int(0, 2),
                        new Vector2Int(1, 2),
                        new Vector2Int(2, 2),
                        new Vector2Int(1, 1),
                        new Vector2Int(1, 0)
                    };
                case DemoShape.Z:
                    return new List<Vector2Int>
                    {
                        new Vector2Int(0, 0),
                        new Vector2Int(0, 1),
                        new Vector2Int(0, 2),
                        new Vector2Int(1, 0),
                        new Vector2Int(2, 0)
                    };
                case DemoShape.RingWithHole:
                    return CreateRing();
                case DemoShape.DiagonalTouch:
                    return new List<Vector2Int>
                    {
                        new Vector2Int(0, 0),
                        new Vector2Int(1, 1)
                    };
                default:
                    return new List<Vector2Int> { new Vector2Int(0, 0) };
            }
        }

        private static List<Vector2Int> CreateRing()
        {
            List<Vector2Int> tiles = new List<Vector2Int>();
            for (int x = 0; x < 3; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    if (x == 1 && y == 1)
                        continue;

                    tiles.Add(new Vector2Int(x, y));
                }
            }

            return tiles;
        }

        private void ResolveView()
        {
            if (view == null)
                view = GetComponent<RuntimeTileMeshView>();
        }
    }
}
