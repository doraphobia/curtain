using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    /// <summary>
    /// Renders merged-shape exterior boundaries in Game View with player-distance reveal.
    /// This is a visual indicator only and stays separate from interior dashed walls.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ExteriorBoundaryRevealRenderer : MonoBehaviour
    {
        private const float BoundaryZ = -0.02f;

        [Header("Sources")]
        [SerializeField] private RuntimeTileMeshFusionSandbox sandbox;
        [SerializeField] private Transform player;

        [Header("Reveal")]
        [SerializeField] private bool showInGameView = true;
        [Min(0f)] [SerializeField] private float innerRevealRadiusCells = 1f;
        [Min(0f)] [SerializeField] private float outerRevealRadiusCells = 4f;
        [Range(0f, 1f)] [SerializeField] private float minAlpha = 0f;
        [Range(0f, 1f)] [SerializeField] private float maxAlpha = 0.85f;

        [Header("Appearance")]
        [SerializeField] private Color boundaryColor = new Color(0.6f, 0.92f, 1f, 1f);
        [Min(0.001f)] [SerializeField] private float lineWidth = 0.045f;
        [SerializeField] private int sortingOrder = 15;
        [SerializeField] private Material boundaryMaterial;

        [Header("Debug")]
        [SerializeField] private bool showDebugGizmos = true;
        [SerializeField] private bool logMissingPlayerWarning = true;

        [Header("Future")]
        [SerializeField] private bool useShaderReveal;

        private readonly List<ExteriorBoundarySegment> segments = new List<ExteriorBoundarySegment>();
        private readonly List<LineRenderer> segmentLines = new List<LineRenderer>();
        private Transform segmentRoot;
        private Material runtimeMaterial;
        private bool warnedMissingPlayer;

        public bool ShowInGameView
        {
            get => showInGameView;
            set => showInGameView = value;
        }

        void Awake()
        {
            ResolveSandboxReference();
            EnsureSegmentRoot();
        }

        void LateUpdate()
        {
            if (!showInGameView || segments.Count == 0)
                return;

            UpdateSegmentAlphas();
        }

        void OnDrawGizmosSelected()
        {
            if (!showDebugGizmos)
                return;

            DrawDebugGizmos();
        }

        public void BindSandbox(RuntimeTileMeshFusionSandbox sourceSandbox)
        {
            sandbox = sourceSandbox;
        }

        public void BindPlayer(Transform playerTransform)
        {
            player = playerTransform;
            warnedMissingPlayer = false;
        }

        public void Rebuild(IEnumerable<RuntimeTileMeshDraggableBlock> blocks)
        {
            segments.Clear();

            if (sandbox == null)
                ResolveSandboxReference();

            Vector2 origin = sandbox != null ? sandbox.gridOrigin : Vector2.zero;
            float gridSize = sandbox != null ? sandbox.gridSize : 1f;
            ExteriorBoundaryExtractor.ExtractFromActiveBlocks(blocks, origin, gridSize, segments);
            RebuildLineRenderers();
        }

        public void RebuildFromSandboxBlocks()
        {
            if (sandbox == null)
                ResolveSandboxReference();

            if (sandbox == null)
                return;

            sandbox.RefreshBlocks();
            Rebuild(sandbox.GetActiveBlocksForBoundaryVisual());
        }

        private void ResolveSandboxReference()
        {
            if (sandbox != null)
                return;

            sandbox = GetComponentInParent<RuntimeTileMeshFusionSandbox>();
        }

        private void ResolvePlayerReference()
        {
            if (player != null)
                return;

            if (PlayerControl.Active != null)
                player = PlayerControl.Active.transform;
            else
            {
                PlayerControl control = FindFirstObjectByType<PlayerControl>();
                if (control != null)
                    player = control.transform;
            }
        }

        private void EnsureSegmentRoot()
        {
            if (segmentRoot != null)
                return;

            GameObject root = new GameObject("Exterior Boundary Segments");
            root.transform.SetParent(transform, false);
            segmentRoot = root.transform;
        }

        private void RebuildLineRenderers()
        {
            EnsureSegmentRoot();

            for (int i = 0; i < segments.Count; i++)
            {
                LineRenderer line = GetSegmentLine(i);
                ExteriorBoundarySegment segment = segments[i];
                ConfigureSegmentLine(line, segment.start, segment.end, maxAlpha);
            }

            for (int i = segments.Count; i < segmentLines.Count; i++)
            {
                if (segmentLines[i] != null)
                    segmentLines[i].gameObject.SetActive(false);
            }
        }

        private LineRenderer GetSegmentLine(int index)
        {
            while (segmentLines.Count <= index)
            {
                GameObject lineObject = new GameObject("Exterior Boundary Line " + segmentLines.Count);
                lineObject.transform.SetParent(segmentRoot, false);
                LineRenderer line = lineObject.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.numCapVertices = 2;
                line.numCornerVertices = 0;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.allowOcclusionWhenDynamic = false;
                segmentLines.Add(line);
            }

            LineRenderer renderer = segmentLines[index];
            renderer.gameObject.SetActive(showInGameView && segments.Count > 0);
            return renderer;
        }

        private void ConfigureSegmentLine(LineRenderer line, Vector2 start, Vector2 end, float alpha)
        {
            if (line == null)
                return;

            line.sharedMaterial = GetBoundaryMaterial();
            line.widthMultiplier = Mathf.Max(0.001f, lineWidth);
            line.sortingOrder = sortingOrder;
            line.SetPosition(0, new Vector3(start.x, start.y, BoundaryZ));
            line.SetPosition(1, new Vector3(end.x, end.y, BoundaryZ));

            Color color = boundaryColor;
            color.a = alpha;
            line.startColor = color;
            line.endColor = color;
        }

        private void UpdateSegmentAlphas()
        {
            if (useShaderReveal)
            {
                // TODO: drive a shader reveal mask instead of per-segment CPU alpha.
                return;
            }

            ResolvePlayerReference();
            if (player == null)
            {
                if (logMissingPlayerWarning && !warnedMissingPlayer)
                {
                    Debug.LogWarning(
                        "[ExteriorBoundaryRevealRenderer] Player transform is not assigned; boundary reveal alpha will stay hidden.",
                        this);
                    warnedMissingPlayer = true;
                }

                return;
            }

            float gridSize = sandbox != null ? Mathf.Max(0.0001f, Mathf.Abs(sandbox.gridSize)) : 1f;
            float innerRadius = Mathf.Max(0f, innerRevealRadiusCells) * gridSize;
            float outerRadius = Mathf.Max(innerRadius + gridSize * 0.01f, outerRevealRadiusCells * gridSize);
            Vector2 playerPosition = player.position;

            for (int i = 0; i < segments.Count; i++)
            {
                if (i >= segmentLines.Count || segmentLines[i] == null)
                    continue;

                ExteriorBoundarySegment segment = segments[i];
                float distance = DistancePointToSegment(playerPosition, segment.start, segment.end);
                float alpha = EvaluateRevealAlpha(distance, innerRadius, outerRadius, minAlpha, maxAlpha);
                Color color = boundaryColor;
                color.a = alpha;
                segmentLines[i].startColor = color;
                segmentLines[i].endColor = color;
            }
        }

        internal static float DistancePointToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float denom = Vector2.Dot(ab, ab);
            if (denom <= 1e-8f)
                return Vector2.Distance(point, a);

            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / denom);
            Vector2 closest = a + ab * t;
            return Vector2.Distance(point, closest);
        }

        internal static float EvaluateRevealAlpha(
            float distance,
            float innerRadius,
            float outerRadius,
            float minAlphaValue,
            float maxAlphaValue)
        {
            float t = Mathf.InverseLerp(outerRadius, innerRadius, distance);
            float fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            return Mathf.Lerp(minAlphaValue, maxAlphaValue, fade);
        }

        private Material GetBoundaryMaterial()
        {
            if (boundaryMaterial != null)
                return boundaryMaterial;

            if (runtimeMaterial != null)
                return runtimeMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader == null)
                return null;

            runtimeMaterial = new Material(shader);
            runtimeMaterial.name = "Exterior Boundary Reveal";
            runtimeMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            if (runtimeMaterial.HasProperty("_BaseColor"))
                runtimeMaterial.SetColor("_BaseColor", Color.white);
            if (runtimeMaterial.HasProperty("_Color"))
                runtimeMaterial.SetColor("_Color", Color.white);
            return runtimeMaterial;
        }

        private void DrawDebugGizmos()
        {
            if (segments.Count == 0)
                return;

            Gizmos.color = new Color(0.25f, 0.35f, 0.55f, 0.65f);
            for (int i = 0; i < segments.Count; i++)
            {
                ExteriorBoundarySegment segment = segments[i];
                Gizmos.DrawLine(
                    new Vector3(segment.start.x, segment.start.y, 0f),
                    new Vector3(segment.end.x, segment.end.y, 0f));
            }

            ResolvePlayerReference();
            if (player == null)
                return;

            float gridSize = sandbox != null ? Mathf.Max(0.0001f, Mathf.Abs(sandbox.gridSize)) : 1f;
            float innerRadius = Mathf.Max(0f, innerRevealRadiusCells) * gridSize;
            float outerRadius = Mathf.Max(innerRadius + gridSize * 0.01f, outerRevealRadiusCells * gridSize);
            Vector3 center = player.position;

            DrawRadiusCircle(center, innerRadius, new Color(0.2f, 0.95f, 0.35f, 0.85f));
            DrawRadiusCircle(center, outerRadius, new Color(0.25f, 0.55f, 1f, 0.75f));

            Gizmos.color = new Color(0.55f, 0.95f, 1f, 0.95f);
            for (int i = 0; i < segments.Count; i++)
            {
                ExteriorBoundarySegment segment = segments[i];
                float distance = DistancePointToSegment(player.position, segment.start, segment.end);
                float alpha = EvaluateRevealAlpha(distance, innerRadius, outerRadius, minAlpha, maxAlpha);
                if (alpha <= minAlpha + 0.01f)
                    continue;

                Gizmos.DrawLine(
                    new Vector3(segment.start.x, segment.start.y, 0f),
                    new Vector3(segment.end.x, segment.end.y, 0f));
            }
        }

        private static void DrawRadiusCircle(Vector3 center, float radius, Color color)
        {
            if (radius <= 0f)
                return;

            const int segments = 48;
            Gizmos.color = color;
            Vector3 previous = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }

        void OnDestroy()
        {
            if (runtimeMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(runtimeMaterial);
                else
                    DestroyImmediate(runtimeMaterial);
            }
        }

        void OnValidate()
        {
            if (outerRevealRadiusCells < innerRevealRadiusCells)
                outerRevealRadiusCells = innerRevealRadiusCells + 0.5f;
        }
    }
}
