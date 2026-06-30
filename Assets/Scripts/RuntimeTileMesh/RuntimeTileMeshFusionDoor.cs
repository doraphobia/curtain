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

        public enum DoorHingeEnd
        {
            Negative,
            Positive
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
        [Range(1f, 179f)]
        public float openAngleDegrees = 90f;
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
        public bool useWallContrastOutline = true;
        [Min(1f)]
        public float wallOutlineWidthMultiplier = 2.35f;
        [Range(0f, 1f)]
        public float wallOutlineAlpha = 0.95f;
        [Min(0.01f)]
        public float wallDashLength = 0.28f;
        [Min(0.01f)]
        public float wallGapLength = 0.16f;

        [SerializeField]
        private bool isOpen;
        [SerializeField]
        private Vector2 openDirection = Vector2.right;
        [SerializeField]
        private DoorHingeEnd hingeEnd = DoorHingeEnd.Negative;

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
        public DoorHingeEnd HingeEnd => hingeEnd;
        public Vector2 HingeWorldPoint => GetHingePoint(hingeEnd);
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
            openAngleDegrees = Mathf.Clamp(openAngleDegrees, 1f, 179f);
            wallCellLength = Mathf.Max(1, wallCellLength);
            doorVariableOffset = Mathf.Clamp(doorVariableOffset, 0, Mathf.Max(0, wallCellLength - 1));
            wallLineWidth = Mathf.Max(0.005f, wallLineWidth);
            wallOutlineWidthMultiplier = Mathf.Max(1f, wallOutlineWidthMultiplier);
            wallOutlineAlpha = Mathf.Clamp01(wallOutlineAlpha);
            wallDashLength = Mathf.Max(0.01f, wallDashLength);
            wallGapLength = Mathf.Max(0.01f, wallGapLength);

            if (Application.isPlaying && panelTransform != null)
                ApplyVisualState();
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
            float openAngle,
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
            openAngleDegrees = Mathf.Clamp(openAngle, 1f, 179f);
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
            hingeEnd = DoorHingeEnd.Negative;

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
            DoorPanelPose panelPose = isOpen ? GetOpenPanelPose() : GetClosedPanelPose();
            if (!PointTouchesPanel(point, panelPose, interactionRadius))
                return false;

            if (isOpen)
            {
                Close();
                return true;
            }

            Vector2 movement = (Vector2)seamCenter - (Vector2)playerWorldPoint;
            if (movement.sqrMagnitude <= 0.0001f)
                movement = openDirection.sqrMagnitude > 0.0001f ? openDirection : Vector2.right;

            OpenToward(movement, point);
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

                if (!isOpen)
                {
                    if (CanToggleNow())
                        OpenToward(motion, GetPointOnWallRun(alongCoordinate));

                    return true;
                }
            }

            if (!isOpen && CanToggleNow() &&
                SegmentTouchesPanel(from, to, GetClosedPanelPose(), playerRadius, out Vector2 closedHitPoint))
            {
                OpenToward(motion, closedHitPoint);
                return true;
            }

            if (isOpen && CanToggleNow() &&
                SegmentTouchesPanel(from, to, GetOpenPanelPose(), playerRadius, out _))
            {
                Close();
                return true;
            }

            return false;
        }

        public void OpenToward(Vector2 movement)
        {
            OpenToward(movement, seamCenter);
        }

        public void OpenToward(Vector2 movement, Vector2 impactPoint)
        {
            Vector2 direction = GetAxisOpenDirection(movement);
            if (direction.sqrMagnitude > 0.0001f)
                openDirection = direction.normalized;

            hingeEnd = GetHingeEndFarthestFrom(impactPoint);
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

        private Vector2 GetPointOnWallRun(float alongCoordinate)
        {
            return axis == DoorAxis.Vertical
                ? new Vector2(seamCenter.x, alongCoordinate)
                : new Vector2(alongCoordinate, seamCenter.y);
        }

        private DoorPanelPose GetClosedPanelPose()
        {
            return new DoorPanelPose
            {
                center = seamCenter,
                lengthDirection = GetClosedLengthDirection(),
                length = GetWorldDoorLength(),
                thickness = GetWorldDoorThickness()
            };
        }

        private DoorPanelPose GetOpenPanelPose()
        {
            Vector2 hinge = GetHingePoint(hingeEnd);
            Vector2 freeVector = GetOpenFreeVector(hingeEnd, openDirection);
            float length = GetWorldDoorLength();

            if (freeVector.sqrMagnitude <= 0.000001f)
                freeVector = GetClosedFreeVector(hingeEnd);

            return new DoorPanelPose
            {
                center = hinge + freeVector * 0.5f,
                lengthDirection = freeVector.normalized,
                length = length,
                thickness = GetWorldDoorThickness()
            };
        }

        private float GetWorldDoorLength()
        {
            return Mathf.Max(0.01f, doorLength) * Mathf.Max(0.0001f, Mathf.Abs(gridSize));
        }

        private float GetWorldDoorThickness()
        {
            return Mathf.Max(0.01f, closedThickness) * Mathf.Max(0.0001f, Mathf.Abs(gridSize));
        }

        private Vector2 GetClosedLengthDirection()
        {
            return axis == DoorAxis.Vertical ? Vector2.up : Vector2.right;
        }

        private Vector2 GetHingePoint(DoorHingeEnd end)
        {
            Vector2 half = GetClosedLengthDirection() * (GetWorldDoorLength() * 0.5f);
            return end == DoorHingeEnd.Negative
                ? seamCenter - half
                : seamCenter + half;
        }

        private Vector2 GetClosedFreeVector(DoorHingeEnd hinge)
        {
            Vector2 closedVector = GetClosedLengthDirection() * GetWorldDoorLength();
            return hinge == DoorHingeEnd.Negative ? closedVector : -closedVector;
        }

        private DoorHingeEnd GetHingeEndFarthestFrom(Vector2 impactPoint)
        {
            float negativeDistance = (impactPoint - GetHingePoint(DoorHingeEnd.Negative)).sqrMagnitude;
            float positiveDistance = (impactPoint - GetHingePoint(DoorHingeEnd.Positive)).sqrMagnitude;

            if (Mathf.Abs(negativeDistance - positiveDistance) <= 0.000001f)
                return hingeEnd;

            return negativeDistance > positiveDistance ? DoorHingeEnd.Negative : DoorHingeEnd.Positive;
        }

        private Vector2 GetOpenFreeVector(DoorHingeEnd hinge, Vector2 desiredOpenDirection)
        {
            Vector2 closedVector = GetClosedFreeVector(hinge);
            float angle = Mathf.Clamp(openAngleDegrees, 1f, 179f);
            Vector2 clockwise = Rotate(closedVector, -angle);
            Vector2 counterClockwise = Rotate(closedVector, angle);
            Vector2 desired = desiredOpenDirection.sqrMagnitude > 0.0001f
                ? desiredOpenDirection.normalized
                : GetAxisOpenDirection(openDirection);

            return Vector2.Dot(clockwise.normalized, desired) >= Vector2.Dot(counterClockwise.normalized, desired)
                ? clockwise
                : counterClockwise;
        }

        private static Vector2 Rotate(Vector2 value, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            return new Vector2(value.x * cos - value.y * sin, value.x * sin + value.y * cos);
        }

        private static bool SegmentTouchesPanel(
            Vector2 from,
            Vector2 to,
            DoorPanelPose pose,
            float inflate,
            out Vector2 hitPoint)
        {
            if (PointTouchesPanel(from, pose, inflate))
            {
                hitPoint = from;
                return true;
            }

            if (PointTouchesPanel(to, pose, inflate))
            {
                hitPoint = to;
                return true;
            }

            float distance = Vector2.Distance(from, to);
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance / 0.05f));
            for (int i = 1; i <= steps; i++)
            {
                Vector2 sample = Vector2.Lerp(from, to, i / (float)steps);
                if (PointTouchesPanel(sample, pose, inflate))
                {
                    hitPoint = sample;
                    return true;
                }
            }

            hitPoint = to;
            return false;
        }

        private static bool PointTouchesPanel(Vector2 point, DoorPanelPose pose, float inflate)
        {
            inflate = Mathf.Max(0f, inflate);
            Vector2 direction = pose.lengthDirection.sqrMagnitude > 0.0001f
                ? pose.lengthDirection.normalized
                : Vector2.right;
            Vector2 normal = new Vector2(-direction.y, direction.x);
            Vector2 delta = point - pose.center;
            float along = Mathf.Abs(Vector2.Dot(delta, direction));
            float across = Mathf.Abs(Vector2.Dot(delta, normal));

            return along <= pose.length * 0.5f + inflate &&
                   across <= pose.thickness * 0.5f + inflate;
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

            DoorPanelPose pose = isOpen ? GetOpenPanelPose() : GetClosedPanelPose();
            Vector2 localCenter = pose.center - seamCenter;
            float angle = Mathf.Atan2(pose.lengthDirection.y, pose.lengthDirection.x) * Mathf.Rad2Deg;

            panelTransform.localPosition = new Vector3(localCenter.x, localCenter.y, 0f);
            panelTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            panelTransform.localScale = new Vector3(pose.length, pose.thickness, 1f);

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
                CreateWallDash(startCellOffset, cursor, dashEnd, dashIndex, true);
                if (useWallContrastOutline)
                    CreateWallDash(startCellOffset, cursor, dashEnd, dashIndex, false);
                cursor = dashEnd + wallGapLength * safeGridSize;
                dashIndex++;
            }
        }

        private void CreateWallDash(
            int startCellOffset,
            float dashStart,
            float dashEnd,
            int dashIndex,
            bool foreground)
        {
            GameObject dashObject = new GameObject(
                foreground
                    ? "Wall Dash " + startCellOffset + "-" + dashIndex
                    : "Wall Dash Contrast " + startCellOffset + "-" + dashIndex);
            dashObject.transform.SetParent(wallVisualRoot, false);

            LineRenderer line = dashObject.AddComponent<LineRenderer>();
            line.sharedMaterial = GetDoorMaterial();
            line.positionCount = 2;
            line.useWorldSpace = false;
            line.widthMultiplier = foreground
                ? wallLineWidth
                : wallLineWidth * Mathf.Max(1f, wallOutlineWidthMultiplier);
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            Color color = foreground ? wallColor : GetContrastWallColor(wallColor);
            line.startColor = color;
            line.endColor = color;
            line.sortingOrder = foreground ? 29 : 28;

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

        private Color GetContrastWallColor(Color source)
        {
            float luminance = source.r * 0.2126f + source.g * 0.7152f + source.b * 0.0722f;
            Color contrast = luminance > 0.55f ? Color.black : Color.white;
            contrast.a = wallOutlineAlpha;
            return contrast;
        }

        private struct DoorPanelPose
        {
            public Vector2 center;
            public Vector2 lengthDirection;
            public float length;
            public float thickness;
        }
    }
}
