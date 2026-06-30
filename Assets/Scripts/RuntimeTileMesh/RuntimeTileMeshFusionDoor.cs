using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    [DisallowMultipleComponent]
    public class RuntimeTileMeshFusionDoor : MonoBehaviour
    {
        private const string DoorPanelName = "Door Panel";
        private const string WallVisualName = "Wall Visual";

        public enum DoorAxis
        {
            Vertical,
            Horizontal
        }

        [Header("Door")]
        public DoorAxis axis = DoorAxis.Vertical;
        public Vector2 seamCenter = Vector2.zero;
        public string doorKey;
        [Min(0.0001f)]
        public float gridSize = 1f;
        [Min(0.01f)]
        public float closedThickness = 0.5f;
        [Min(0.01f)]
        public float doorLength = 1f;
        [Min(0f)]
        public float toggleCooldown = 0.22f;
        public Color closedColor = Color.black;
        public Color openColor = new Color(0f, 0f, 0f, 0.82f);

        [Header("Wall Edge")]
        public int wallEdgeCoordinate;
        public int wallVariableStart;
        [Min(1)]
        public int wallCellLength = 3;
        [Min(0)]
        public int doorVariableOffset = 1;

        [Header("Wall Visual")]
        public bool useDefaultWallDebugVisual = true;
        public GameObject wallVisualPrefab;
        public Color wallColor = new Color(0f, 0f, 0f, 0.9f);
        [Min(0.005f)]
        public float wallLineWidth = 0.08f;
        [Min(0.01f)]
        public float wallDashLength = 0.28f;
        [Min(0.01f)]
        public float wallGapLength = 0.16f;

        [SerializeField]
        private bool isOpen;
        [SerializeField]
        private Vector2 openDirection = Vector2.right;

        private Transform panelTransform;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private BoxCollider2D boxCollider;
        private Material runtimeMaterial;
        private MaterialPropertyBlock propertyBlock;
        private Mesh runtimeMesh;
        private Transform wallVisualRoot;
        private float lastToggleTime = -999f;

        public bool IsOpen => isOpen;
        public Vector2 OpenDirection => openDirection;
        public int DoorVariable => wallVariableStart + Mathf.Clamp(doorVariableOffset, 0, Mathf.Max(0, wallCellLength - 1));

        void Awake()
        {
            EnsureVisual();
            ApplyVisualState();
        }

        void OnValidate()
        {
            gridSize = Mathf.Max(0.0001f, gridSize);
            closedThickness = Mathf.Max(0.01f, closedThickness);
            doorLength = Mathf.Max(0.01f, doorLength);
            toggleCooldown = Mathf.Max(0f, toggleCooldown);
            wallCellLength = Mathf.Max(1, wallCellLength);
            doorVariableOffset = Mathf.Clamp(doorVariableOffset, 0, Mathf.Max(0, wallCellLength - 1));
            wallLineWidth = Mathf.Max(0.005f, wallLineWidth);
            wallDashLength = Mathf.Max(0.01f, wallDashLength);
            wallGapLength = Mathf.Max(0.01f, wallGapLength);
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

            if (runtimeMesh != null)
            {
                if (Application.isPlaying)
                    Destroy(runtimeMesh);
                else
                    DestroyImmediate(runtimeMesh);
            }
        }

        public void Configure(
            DoorAxis doorAxis,
            Vector2 worldSeamCenter,
            float worldGridSize,
            string key,
            float thickness,
            Color closedDoorColor,
            int edgeCoordinate,
            int variableStart,
            int edgeCellLength,
            GameObject customWallVisualPrefab,
            Color debugWallColor,
            float debugWallLineWidth)
        {
            axis = doorAxis;
            seamCenter = worldSeamCenter;
            gridSize = Mathf.Max(0.0001f, worldGridSize);
            doorKey = key;
            closedThickness = Mathf.Max(0.01f, thickness);
            closedColor = closedDoorColor;
            wallEdgeCoordinate = edgeCoordinate;
            wallVariableStart = variableStart;
            wallCellLength = Mathf.Max(1, edgeCellLength);
            doorVariableOffset = Mathf.Clamp(wallCellLength / 2, 0, wallCellLength - 1);
            wallVisualPrefab = customWallVisualPrefab;
            wallColor = debugWallColor;
            wallLineWidth = Mathf.Max(0.005f, debugWallLineWidth);
            isOpen = false;
            openDirection = axis == DoorAxis.Vertical ? Vector2.right : Vector2.up;

            EnsureVisual();
            RebuildWallVisual();
            ApplyVisualState();
        }

        public bool IsSameDoor(DoorAxis otherAxis, Vector2 otherSeamCenter, float epsilon)
        {
            return axis == otherAxis && Vector2.Distance(seamCenter, otherSeamCenter) <= Mathf.Max(0.0001f, epsilon);
        }

        public bool TryToggleFromMovement(Vector3 fromWorld, Vector3 toWorld, float playerRadius)
        {
            return TryBlockMovement(fromWorld, toWorld, playerRadius);
        }

        public bool TryInteract(Vector3 interactionWorldPoint, Vector3 playerWorldPoint, float interactionRadius)
        {
            if (!CanToggleNow())
                return false;

            Vector2 point = interactionWorldPoint;
            Rect panelRect = isOpen ? GetOpenPanelRect() : GetClosedPanelRect();
            if (!PointTouchesRect(point, panelRect, interactionRadius))
                return false;

            if (isOpen)
            {
                Close();
                return true;
            }

            Vector2 movement = (Vector2)seamCenter - (Vector2)playerWorldPoint;
            if (movement.sqrMagnitude <= 0.0001f)
                movement = openDirection.sqrMagnitude > 0.0001f ? openDirection : Vector2.right;

            OpenToward(movement);
            return true;
        }

        public bool TryBlockMovement(Vector3 fromWorld, Vector3 toWorld, float playerRadius)
        {
            Vector2 from = fromWorld;
            Vector2 to = toWorld;
            Vector2 motion = to - from;
            if (motion.sqrMagnitude <= 0.000001f)
                return false;

            if (TryGetWallCrossingAlongCoordinate(from, to, out float alongCoordinate) &&
                TryGetWallSegmentIndex(alongCoordinate, out int segmentIndex))
            {
                if (segmentIndex != doorVariableOffset)
                    return true;

                if (isOpen)
                    return false;

                if (CanToggleNow())
                    OpenToward(motion);

                return true;
            }

            if (!isOpen && CanToggleNow() && SegmentTouchesRect(from, to, GetClosedPanelRect(), playerRadius))
            {
                OpenToward(motion);
                return true;
            }

            if (isOpen && CanToggleNow() && SegmentTouchesRect(from, to, GetOpenPanelRect(), playerRadius))
            {
                Close();
                return true;
            }

            return false;
        }

        public void OpenToward(Vector2 movement)
        {
            Vector2 direction = GetAxisOpenDirection(movement);
            if (direction.sqrMagnitude > 0.0001f)
                openDirection = direction.normalized;

            isOpen = true;
            lastToggleTime = Time.time;
            ApplyVisualState();
        }

        public void Close()
        {
            isOpen = false;
            lastToggleTime = Time.time;
            ApplyVisualState();
        }

        private bool CanToggleNow()
        {
            return Time.time >= lastToggleTime + toggleCooldown;
        }

        private Vector2 GetAxisOpenDirection(Vector2 movement)
        {
            if (axis == DoorAxis.Vertical)
                return movement.x >= 0f ? Vector2.right : Vector2.left;

            return movement.y >= 0f ? Vector2.up : Vector2.down;
        }

        private bool TryGetWallCrossingAlongCoordinate(Vector2 from, Vector2 to, out float alongCoordinate)
        {
            alongCoordinate = 0f;
            if (axis == DoorAxis.Vertical)
            {
                float dx = to.x - from.x;
                if (Mathf.Abs(dx) <= 0.000001f)
                    return false;

                float line = seamCenter.x;
                float fromSide = from.x - line;
                float toSide = to.x - line;
                if (fromSide * toSide > 0f)
                    return false;

                float t = Mathf.Clamp01((line - from.x) / dx);
                alongCoordinate = Mathf.Lerp(from.y, to.y, t);
                return true;
            }

            float dy = to.y - from.y;
            if (Mathf.Abs(dy) <= 0.000001f)
                return false;

            float horizontalLine = seamCenter.y;
            float horizontalFromSide = from.y - horizontalLine;
            float horizontalToSide = to.y - horizontalLine;
            if (horizontalFromSide * horizontalToSide > 0f)
                return false;

            float horizontalT = Mathf.Clamp01((horizontalLine - from.y) / dy);
            alongCoordinate = Mathf.Lerp(from.x, to.x, horizontalT);
            return true;
        }

        private bool TryGetWallSegmentIndex(float alongCoordinate, out int segmentIndex)
        {
            float safeGridSize = Mathf.Max(0.0001f, Mathf.Abs(gridSize));
            float runStartWorld = GetWallRunStartWorld();
            float local = alongCoordinate - runStartWorld;
            float epsilon = safeGridSize * 0.001f;

            if (local < -epsilon || local > wallCellLength * safeGridSize + epsilon)
            {
                segmentIndex = -1;
                return false;
            }

            segmentIndex = Mathf.Clamp(Mathf.FloorToInt(local / safeGridSize), 0, wallCellLength - 1);
            return true;
        }

        private float GetWallRunStartWorld()
        {
            float safeGridSize = Mathf.Max(0.0001f, Mathf.Abs(gridSize));
            return axis == DoorAxis.Vertical
                ? seamCenter.y - wallCellLength * safeGridSize * 0.5f
                : seamCenter.x - wallCellLength * safeGridSize * 0.5f;
        }

        private Rect GetClosedPanelRect()
        {
            Vector2 size = GetClosedPanelSize();
            return RectFromCenter(seamCenter, size);
        }

        private Rect GetOpenPanelRect()
        {
            Vector2 size = GetOpenPanelSize();
            Vector2 center = seamCenter + openDirection.normalized * (doorLength * gridSize * 0.5f);
            return RectFromCenter(center, size);
        }

        private Vector2 GetClosedPanelSize()
        {
            float thickness = closedThickness * gridSize;
            float length = doorLength * gridSize;
            return axis == DoorAxis.Vertical
                ? new Vector2(thickness, length)
                : new Vector2(length, thickness);
        }

        private Vector2 GetOpenPanelSize()
        {
            float thickness = closedThickness * gridSize;
            float length = doorLength * gridSize;
            return axis == DoorAxis.Vertical
                ? new Vector2(length, thickness)
                : new Vector2(thickness, length);
        }

        private static Rect RectFromCenter(Vector2 center, Vector2 size)
        {
            return new Rect(center - size * 0.5f, size);
        }

        private static bool SegmentTouchesRect(Vector2 from, Vector2 to, Rect rect, float inflate)
        {
            inflate = Mathf.Max(0f, inflate);
            rect.xMin -= inflate;
            rect.xMax += inflate;
            rect.yMin -= inflate;
            rect.yMax += inflate;

            if (rect.Contains(from) || rect.Contains(to))
                return true;

            float distance = Vector2.Distance(from, to);
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance / 0.05f));
            for (int i = 1; i <= steps; i++)
            {
                Vector2 sample = Vector2.Lerp(from, to, i / (float)steps);
                if (rect.Contains(sample))
                    return true;
            }

            return false;
        }

        private static bool PointTouchesRect(Vector2 point, Rect rect, float inflate)
        {
            inflate = Mathf.Max(0f, inflate);
            rect.xMin -= inflate;
            rect.xMax += inflate;
            rect.yMin -= inflate;
            rect.yMax += inflate;
            return rect.Contains(point);
        }

        private void EnsureVisual()
        {
            transform.position = new Vector3(seamCenter.x, seamCenter.y, -0.15f);
            EnsureDoorPanel();

            if (runtimeMesh == null)
                runtimeMesh = CreateQuadMesh();
            meshFilter.sharedMesh = runtimeMesh;

            meshRenderer.sharedMaterial = GetDoorMaterial();
            meshRenderer.sortingOrder = 30;
            boxCollider.isTrigger = true;
            boxCollider.size = Vector2.one;
            boxCollider.offset = Vector2.zero;
        }

        private void EnsureDoorPanel()
        {
            if (panelTransform == null)
            {
                Transform existingPanel = transform.Find(DoorPanelName);
                if (existingPanel != null)
                {
                    panelTransform = existingPanel;
                }
                else
                {
                    GameObject panelObject = new GameObject(DoorPanelName);
                    panelTransform = panelObject.transform;
                    panelTransform.SetParent(transform, false);
                }
            }

            meshFilter = panelTransform.GetComponent<MeshFilter>();
            if (meshFilter == null)
                meshFilter = panelTransform.gameObject.AddComponent<MeshFilter>();

            meshRenderer = panelTransform.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
                meshRenderer = panelTransform.gameObject.AddComponent<MeshRenderer>();

            boxCollider = panelTransform.GetComponent<BoxCollider2D>();
            if (boxCollider == null)
                boxCollider = panelTransform.gameObject.AddComponent<BoxCollider2D>();
        }

        private Mesh CreateQuadMesh()
        {
            Mesh mesh = new Mesh();
            mesh.name = "Runtime Tile Fusion Door Quad";
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f)
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private Material GetDoorMaterial()
        {
            if (runtimeMaterial != null)
                return runtimeMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader == null)
                return null;

            runtimeMaterial = new Material(shader);
            runtimeMaterial.name = "Runtime Tile Fusion Door";
            if (runtimeMaterial.HasProperty("_Surface"))
                runtimeMaterial.SetFloat("_Surface", 0f);
            if (runtimeMaterial.HasProperty("_Cull"))
                runtimeMaterial.SetFloat("_Cull", 0f);
            return runtimeMaterial;
        }

        private void ApplyVisualState()
        {
            EnsureVisual();

            Vector2 size = isOpen ? GetOpenPanelSize() : GetClosedPanelSize();
            Vector2 localCenter = isOpen
                ? openDirection.normalized * (doorLength * gridSize * 0.5f)
                : Vector2.zero;

            panelTransform.localPosition = new Vector3(localCenter.x, localCenter.y, 0f);
            panelTransform.localRotation = Quaternion.identity;
            panelTransform.localScale = new Vector3(size.x, size.y, 1f);

            if (propertyBlock == null)
                propertyBlock = new MaterialPropertyBlock();

            Color color = isOpen ? openColor : closedColor;
            meshRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor("_BaseColor", color);
            propertyBlock.SetColor("_Color", color);
            meshRenderer.SetPropertyBlock(propertyBlock);
        }

        private void RebuildWallVisual()
        {
            DestroyWallVisualRoot();

            GameObject rootObject = new GameObject(WallVisualName);
            wallVisualRoot = rootObject.transform;
            wallVisualRoot.SetParent(transform, false);
            wallVisualRoot.localPosition = Vector3.zero;
            wallVisualRoot.localRotation = Quaternion.identity;
            wallVisualRoot.localScale = Vector3.one;

            if (wallVisualPrefab != null)
            {
                GameObject instance = Instantiate(wallVisualPrefab, wallVisualRoot);
                instance.name = wallVisualPrefab.name;
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                return;
            }

            if (!useDefaultWallDebugVisual)
                return;

            BuildDashedWallSegment(0, doorVariableOffset);
            BuildDashedWallSegment(doorVariableOffset + 1, wallCellLength);
        }

        private void DestroyWallVisualRoot()
        {
            Transform existingRoot = wallVisualRoot != null ? wallVisualRoot : transform.Find(WallVisualName);
            if (existingRoot == null)
                return;

            if (Application.isPlaying)
                Destroy(existingRoot.gameObject);
            else
                DestroyImmediate(existingRoot.gameObject);

            wallVisualRoot = null;
        }

        private void BuildDashedWallSegment(int startCellOffset, int endCellOffset)
        {
            if (wallVisualRoot == null || endCellOffset <= startCellOffset)
                return;

            float safeGridSize = Mathf.Max(0.0001f, Mathf.Abs(gridSize));
            float runStart = -wallCellLength * safeGridSize * 0.5f;
            float segmentStart = runStart + startCellOffset * safeGridSize;
            float segmentEnd = runStart + endCellOffset * safeGridSize;
            float cursor = segmentStart;
            int dashIndex = 0;

            while (cursor < segmentEnd - 0.0001f)
            {
                float dashEnd = Mathf.Min(segmentEnd, cursor + wallDashLength * safeGridSize);
                CreateWallDash(startCellOffset, cursor, dashEnd, dashIndex);
                cursor = dashEnd + wallGapLength * safeGridSize;
                dashIndex++;
            }
        }

        private void CreateWallDash(
            int startCellOffset,
            float dashStart,
            float dashEnd,
            int dashIndex)
        {
            GameObject dashObject = new GameObject("Wall Dash " + startCellOffset + "-" + dashIndex);
            dashObject.transform.SetParent(wallVisualRoot, false);

            LineRenderer line = dashObject.AddComponent<LineRenderer>();
            line.sharedMaterial = GetDoorMaterial();
            line.positionCount = 2;
            line.useWorldSpace = false;
            line.widthMultiplier = wallLineWidth;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.startColor = wallColor;
            line.endColor = wallColor;
            line.sortingOrder = 28;

            if (axis == DoorAxis.Vertical)
            {
                line.SetPosition(0, new Vector3(0f, dashStart, 0f));
                line.SetPosition(1, new Vector3(0f, dashEnd, 0f));
            }
            else
            {
                line.SetPosition(0, new Vector3(dashStart, 0f, 0f));
                line.SetPosition(1, new Vector3(dashEnd, 0f, 0f));
            }
        }
    }
}
