using System.Collections.Generic;
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
        public float closedThickness = 0.25f;
        [Min(0.01f)]
        public float doorLength = 1f;
        [Min(0f)]
        public float toggleCooldown = 0.22f;
        [Range(1f, 179f)]
        public float openAngleDegrees = 90f;
        public Color closedColor = Color.black;
        public Color openColor = new Color(0f, 0f, 0f, 0.82f);

        [Header("Door Animation")]
        public bool animateDoor = true;
        [Min(0f)]
        public float openDuration = 0.25f;
        [Min(0f)]
        public float closeDuration = 0.2f;
        public AnimationCurve swingCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Range(0f, 1f)]
        public float doorwayPassableOpenAmount = 0.82f;
        public bool useEndWobble = true;
        [Min(0f)]
        public float endWobbleDuration = 0.18f;
        [Min(0f)]
        public float endWobbleAmplitudeDegrees = 6f;
        [Min(0.5f)]
        public float endWobbleOscillations = 2.5f;

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
        public Color wallColor = new Color(0.38f, 0.38f, 0.38f, 0.95f);
        [Min(0.005f)]
        public float wallLineWidth = 0.035f;
        public bool useWallContrastOutline = false;
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
        private Material wallRuntimeMaterial;
        private MaterialPropertyBlock propertyBlock;
        private Mesh runtimeMesh;
        private Transform wallVisualRoot;
        private float lastToggleTime = -999f;
        private float currentOpenAmount;
        private float animationStartAmount;
        private float animationTargetAmount;
        private float animationStartTime;
        private float animationDuration;
        private float wobbleStartTime;
        private bool isAnimating;
        private bool isWobbling;
        private readonly HashSet<int> supportedWallVariables = new HashSet<int>();
        private readonly Dictionary<int, Vector2> wallVisualOffsetsByVariable = new Dictionary<int, Vector2>();

        public bool IsOpen => isOpen;
        public Vector2 OpenDirection => openDirection;
        public DoorHingeEnd HingeEnd => hingeEnd;
        public Vector2 HingeWorldPoint => GetHingePoint(hingeEnd);
        public int DoorVariable => wallVariableStart + Mathf.Clamp(doorVariableOffset, 0, Mathf.Max(0, wallCellLength - 1));

        void Awake()
        {
            if (supportedWallVariables.Count == 0)
                CacheDefaultWallSupport();

            currentOpenAmount = isOpen ? 1f : 0f;
            EnsureVisual();
            ApplyVisualState();
        }

        void Update()
        {
            if (UpdateDoorAnimation())
                ApplyVisualState();
        }

        void OnValidate()
        {
            gridSize = Mathf.Max(0.0001f, gridSize);
            closedThickness = Mathf.Max(0.01f, closedThickness);
            doorLength = Mathf.Max(0.01f, doorLength);
            toggleCooldown = Mathf.Max(0f, toggleCooldown);
            openAngleDegrees = Mathf.Clamp(openAngleDegrees, 1f, 179f);
            openDuration = Mathf.Max(0f, openDuration);
            closeDuration = Mathf.Max(0f, closeDuration);
            doorwayPassableOpenAmount = Mathf.Clamp01(doorwayPassableOpenAmount);
            endWobbleDuration = Mathf.Max(0f, endWobbleDuration);
            endWobbleAmplitudeDegrees = Mathf.Max(0f, endWobbleAmplitudeDegrees);
            endWobbleOscillations = Mathf.Max(0.5f, endWobbleOscillations);
            wallCellLength = Mathf.Max(1, wallCellLength);
            doorVariableOffset = Mathf.Clamp(doorVariableOffset, 0, Mathf.Max(0, wallCellLength - 1));
            wallLineWidth = Mathf.Max(0.005f, wallLineWidth);
            wallOutlineWidthMultiplier = Mathf.Max(1f, wallOutlineWidthMultiplier);
            wallOutlineAlpha = Mathf.Clamp01(wallOutlineAlpha);
            wallDashLength = Mathf.Max(0.01f, wallDashLength);
            wallGapLength = Mathf.Max(0.01f, wallGapLength);

            if (Application.isPlaying && panelTransform != null)
            {
                if (supportedWallVariables.Count == 0)
                    CacheDefaultWallSupport();

                if (!isAnimating && !isWobbling)
                    currentOpenAmount = isOpen ? 1f : 0f;
                ApplyVisualState();
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

            if (wallRuntimeMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(wallRuntimeMaterial);
                else
                    DestroyImmediate(wallRuntimeMaterial);
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
            currentOpenAmount = 0f;
            isAnimating = false;
            isWobbling = false;

            CacheDefaultWallSupport();
            EnsureVisual();
            RebuildWallVisual();
            ApplyVisualState();
        }

        public void RefreshWallSpanFromCells(ICollection<Vector2Int> blockCells)
        {
            if (blockCells == null || blockCells.Count == 0)
                return;

            int doorVariable = DoorVariable;
            if (!TryGetExpandedWallSpan(
                    blockCells,
                    doorVariable,
                    out int expandedStart,
                    out int expandedLength,
                    out Dictionary<int, Vector2> visualOffsets))
            {
                return;
            }

            int expandedOffset = doorVariable - expandedStart;
            if (expandedOffset < 0 || expandedOffset >= expandedLength)
                return;

            supportedWallVariables.Clear();
            wallVisualOffsetsByVariable.Clear();
            foreach (KeyValuePair<int, Vector2> pair in visualOffsets)
            {
                supportedWallVariables.Add(pair.Key);
                wallVisualOffsetsByVariable[pair.Key] = pair.Value;
            }

            if (expandedStart == wallVariableStart &&
                expandedLength == wallCellLength &&
                expandedOffset == doorVariableOffset)
            {
                RebuildWallVisual();
                ApplyVisualState();
                return;
            }

            wallVariableStart = expandedStart;
            wallCellLength = Mathf.Max(1, expandedLength);
            doorVariableOffset = Mathf.Clamp(expandedOffset, 0, wallCellLength - 1);
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
            DoorPanelPose panelPose = GetCurrentPanelPose();
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

        public bool IsPointTouchingCurrentPanel(Vector3 worldPoint, float radius)
        {
            DoorPanelPose panelPose = GetCurrentPanelPose();
            return PointTouchesPanel(worldPoint, panelPose, radius);
        }

        public bool TryToggleFromPlayerContact(Vector3 playerWorldPoint, float playerRadius)
        {
            if (!CanToggleNow() || !IsPointTouchingCurrentPanel(playerWorldPoint, playerRadius))
                return false;

            if (isOpen)
            {
                Close();
                return true;
            }

            Vector2 movement = (Vector2)seamCenter - (Vector2)playerWorldPoint;
            if (movement.sqrMagnitude <= 0.0001f)
                movement = openDirection.sqrMagnitude > 0.0001f ? openDirection : Vector2.right;

            OpenToward(movement, playerWorldPoint);
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

                if (!IsDoorwayPassable())
                    return true;
            }

            DoorPanelPose currentPose = GetCurrentPanelPose();
            if (!isOpen && CanToggleNow() &&
                SegmentTouchesPanel(from, to, currentPose, playerRadius, out Vector2 closedHitPoint))
            {
                OpenToward(motion, closedHitPoint);
                return true;
            }

            if (isOpen && CanToggleNow() &&
                SegmentTouchesPanel(from, to, currentPose, playerRadius, out _))
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
            StartDoorTransition(1f, openDuration);
        }

        public void Close()
        {
            isOpen = false;
            lastToggleTime = Time.time;
            StartDoorTransition(0f, closeDuration);
        }

        private bool CanToggleNow()
        {
            return !isAnimating && !isWobbling && Time.time >= lastToggleTime + toggleCooldown;
        }

        private bool IsDoorwayPassable()
        {
            return isOpen && currentOpenAmount >= doorwayPassableOpenAmount;
        }

        private void StartDoorTransition(float targetAmount, float duration)
        {
            targetAmount = Mathf.Clamp01(targetAmount);
            if (!Application.isPlaying || !animateDoor || duration <= 0.0001f)
            {
                currentOpenAmount = targetAmount;
                isAnimating = false;
                isWobbling = false;
                ApplyVisualState();
                return;
            }

            animationStartAmount = Mathf.Clamp01(currentOpenAmount);
            animationTargetAmount = targetAmount;
            animationStartTime = Time.time;
            animationDuration = Mathf.Max(0.0001f, duration);
            isAnimating = true;
            isWobbling = false;
        }

        private bool UpdateDoorAnimation()
        {
            bool changed = false;
            if (isAnimating)
            {
                float normalized = Mathf.Clamp01((Time.time - animationStartTime) / Mathf.Max(0.0001f, animationDuration));
                float eased = swingCurve != null ? swingCurve.Evaluate(normalized) : normalized;
                currentOpenAmount = Mathf.LerpUnclamped(animationStartAmount, animationTargetAmount, eased);
                changed = true;

                if (normalized >= 1f)
                {
                    currentOpenAmount = animationTargetAmount;
                    isAnimating = false;
                    if (useEndWobble && endWobbleDuration > 0.0001f && endWobbleAmplitudeDegrees > 0.0001f)
                    {
                        isWobbling = true;
                        wobbleStartTime = Time.time;
                    }
                }
            }

            if (isWobbling)
            {
                changed = true;
                if (Time.time - wobbleStartTime >= Mathf.Max(0.0001f, endWobbleDuration))
                    isWobbling = false;
            }

            return changed;
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
            if (supportedWallVariables.Count > 0 &&
                !supportedWallVariables.Contains(wallVariableStart + segmentIndex))
            {
                segmentIndex = -1;
                return false;
            }

            return true;
        }

        private float GetWallRunStartWorld()
        {
            return axis == DoorAxis.Vertical
                ? seamCenter.y + GetWallRunStartLocal()
                : seamCenter.x + GetWallRunStartLocal();
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

        private DoorPanelPose GetCurrentPanelPose()
        {
            float amount = Mathf.Clamp01(currentOpenAmount);
            if (!Application.isPlaying && !isAnimating && !isWobbling)
                amount = isOpen ? 1f : 0f;

            Vector2 hinge = GetHingePoint(hingeEnd);
            Vector2 closedVector = GetClosedFreeVector(hingeEnd);
            if (closedVector.sqrMagnitude <= 0.000001f)
                closedVector = GetClosedLengthDirection() * GetWorldDoorLength();

            float signedOpenAngle = GetSignedOpenAngle(hingeEnd, openDirection);
            float wobble = GetCurrentWobbleDegrees();
            Vector2 freeVector = Rotate(closedVector, signedOpenAngle * amount + wobble);
            if (freeVector.sqrMagnitude <= 0.000001f)
                freeVector = closedVector;

            return new DoorPanelPose
            {
                center = hinge + freeVector * 0.5f,
                lengthDirection = freeVector.normalized,
                length = GetWorldDoorLength(),
                thickness = GetWorldDoorThickness()
            };
        }

        private float GetSignedOpenAngle(DoorHingeEnd hinge, Vector2 desiredOpenDirection)
        {
            Vector2 closedVector = GetClosedFreeVector(hinge);
            Vector2 openVector = GetOpenFreeVector(hinge, desiredOpenDirection);
            if (closedVector.sqrMagnitude <= 0.000001f || openVector.sqrMagnitude <= 0.000001f)
                return 0f;

            return Vector2.SignedAngle(closedVector, openVector);
        }

        private float GetCurrentWobbleDegrees()
        {
            if (!isWobbling || endWobbleDuration <= 0.0001f || endWobbleAmplitudeDegrees <= 0.0001f)
                return 0f;

            float normalized = Mathf.Clamp01((Time.time - wobbleStartTime) / Mathf.Max(0.0001f, endWobbleDuration));
            float envelope = 1f - normalized;
            float wave = Mathf.Sin(normalized * Mathf.PI * 2f * Mathf.Max(0.5f, endWobbleOscillations));
            return wave * endWobbleAmplitudeDegrees * envelope;
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
            boxCollider.isTrigger = false;
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

        private Material GetWallMaterial()
        {
            if (wallRuntimeMaterial != null)
                return wallRuntimeMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader == null)
                return GetDoorMaterial();

            wallRuntimeMaterial = new Material(shader);
            wallRuntimeMaterial.name = "Runtime Tile Fusion Wall";
            if (wallRuntimeMaterial.HasProperty("_Surface"))
                wallRuntimeMaterial.SetFloat("_Surface", 0f);
            if (wallRuntimeMaterial.HasProperty("_Cull"))
                wallRuntimeMaterial.SetFloat("_Cull", 0f);
            if (wallRuntimeMaterial.HasProperty("_BaseColor"))
                wallRuntimeMaterial.SetColor("_BaseColor", Color.white);
            if (wallRuntimeMaterial.HasProperty("_Color"))
                wallRuntimeMaterial.SetColor("_Color", Color.white);
            return wallRuntimeMaterial;
        }

        private void ApplyVisualState()
        {
            EnsureVisual();

            DoorPanelPose pose = GetCurrentPanelPose();
            Vector2 localCenter = pose.center - seamCenter;
            float angle = Mathf.Atan2(pose.lengthDirection.y, pose.lengthDirection.x) * Mathf.Rad2Deg;

            panelTransform.localPosition = new Vector3(localCenter.x, localCenter.y, 0f);
            panelTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            panelTransform.localScale = new Vector3(pose.length, pose.thickness, 1f);

            if (propertyBlock == null)
                propertyBlock = new MaterialPropertyBlock();

            Color color = Color.Lerp(closedColor, openColor, Mathf.Clamp01(currentOpenAmount));
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
            float runStart = GetWallRunStartLocal();
            for (int cellOffset = startCellOffset; cellOffset < endCellOffset; cellOffset++)
            {
                int variable = wallVariableStart + cellOffset;
                if (!TryGetWallVisualOffset(variable, out Vector2 visualOffset))
                    continue;

                float segmentStart = runStart + cellOffset * safeGridSize;
                float segmentEnd = segmentStart + safeGridSize;
                float cursor = segmentStart;
                int dashIndex = 0;

                while (cursor < segmentEnd - 0.0001f)
                {
                    float dashEnd = Mathf.Min(segmentEnd, cursor + wallDashLength * safeGridSize);
                    CreateWallDash(cellOffset, cursor, dashEnd, dashIndex, visualOffset, true);
                    if (useWallContrastOutline)
                        CreateWallDash(cellOffset, cursor, dashEnd, dashIndex, visualOffset, false);
                    cursor = dashEnd + wallGapLength * safeGridSize;
                    dashIndex++;
                }
            }
        }

        private void CreateWallDash(
            int startCellOffset,
            float dashStart,
            float dashEnd,
            int dashIndex,
            Vector2 visualOffset,
            bool foreground)
        {
            GameObject dashObject = new GameObject(
                foreground
                    ? "Wall Dash " + startCellOffset + "-" + dashIndex
                    : "Wall Dash Contrast " + startCellOffset + "-" + dashIndex);
            dashObject.transform.SetParent(wallVisualRoot, false);

            LineRenderer line = dashObject.AddComponent<LineRenderer>();
            line.sharedMaterial = GetWallMaterial();
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
                line.SetPosition(0, new Vector3(visualOffset.x, dashStart + visualOffset.y, 0f));
                line.SetPosition(1, new Vector3(visualOffset.x, dashEnd + visualOffset.y, 0f));
            }
            else
            {
                line.SetPosition(0, new Vector3(dashStart + visualOffset.x, visualOffset.y, 0f));
                line.SetPosition(1, new Vector3(dashEnd + visualOffset.x, visualOffset.y, 0f));
            }
        }

        private Color GetContrastWallColor(Color source)
        {
            float luminance = source.r * 0.2126f + source.g * 0.7152f + source.b * 0.0722f;
            Color contrast = luminance > 0.55f ? Color.black : Color.white;
            contrast.a = wallOutlineAlpha;
            return contrast;
        }

        private void CacheDefaultWallSupport()
        {
            supportedWallVariables.Clear();
            wallVisualOffsetsByVariable.Clear();
            for (int i = 0; i < wallCellLength; i++)
            {
                int variable = wallVariableStart + i;
                supportedWallVariables.Add(variable);
                wallVisualOffsetsByVariable[variable] = Vector2.zero;
            }
        }

        private bool TryGetWallVisualOffset(int variable, out Vector2 visualOffset)
        {
            if (wallVisualOffsetsByVariable.TryGetValue(variable, out visualOffset))
                return true;

            if (supportedWallVariables.Count > 0 && !supportedWallVariables.Contains(variable))
                return false;

            visualOffset = Vector2.zero;
            return supportedWallVariables.Count == 0 ||
                   variable >= wallVariableStart &&
                   variable < wallVariableStart + wallCellLength;
        }

        private bool TryGetExpandedWallSpan(
            ICollection<Vector2Int> blockCells,
            int doorVariable,
            out int start,
            out int length,
            out Dictionary<int, Vector2> visualOffsets)
        {
            start = wallVariableStart;
            length = wallCellLength;
            visualOffsets = new Dictionary<int, Vector2>();

            HashSet<Vector2Int> cellLookup = blockCells as HashSet<Vector2Int> ?? new HashSet<Vector2Int>(blockCells);
            if (!TryGetWallLineSegmentSupport(cellLookup, doorVariable, out _))
                return false;

            int expandedStart = doorVariable;
            while (TryGetWallLineSegmentSupport(cellLookup, expandedStart - 1, out _))
                expandedStart--;

            int expandedEnd = doorVariable + 1;
            while (TryGetWallLineSegmentSupport(cellLookup, expandedEnd, out _))
                expandedEnd++;

            for (int variable = expandedStart; variable < expandedEnd; variable++)
            {
                if (TryGetWallLineSegmentSupport(cellLookup, variable, out Vector2 visualOffset))
                    visualOffsets[variable] = visualOffset;
            }

            start = expandedStart;
            length = Mathf.Max(1, expandedEnd - expandedStart);
            return true;
        }

        private bool TryGetWallLineSegmentSupport(
            HashSet<Vector2Int> cellLookup,
            int variable,
            out Vector2 visualOffset)
        {
            visualOffset = Vector2.zero;
            if (cellLookup == null)
                return false;

            float inset = Mathf.Max(0f, wallLineWidth * 0.5f) + 0.001f;
            if (axis == DoorAxis.Vertical)
            {
                bool leftCovered = cellLookup.Contains(new Vector2Int(wallEdgeCoordinate - 1, variable));
                bool rightCovered = cellLookup.Contains(new Vector2Int(wallEdgeCoordinate, variable));
                if (!leftCovered && !rightCovered)
                    return false;

                if (leftCovered != rightCovered)
                    visualOffset = new Vector2(leftCovered ? -inset : inset, 0f);
                return true;
            }

            bool lowerCovered = cellLookup.Contains(new Vector2Int(variable, wallEdgeCoordinate - 1));
            bool upperCovered = cellLookup.Contains(new Vector2Int(variable, wallEdgeCoordinate));
            if (!lowerCovered && !upperCovered)
                return false;

            if (lowerCovered != upperCovered)
                visualOffset = new Vector2(0f, lowerCovered ? -inset : inset);
            return true;
        }

        private float GetWallRunStartLocal()
        {
            float safeGridSize = Mathf.Max(0.0001f, Mathf.Abs(gridSize));
            return -(Mathf.Clamp(doorVariableOffset, 0, Mathf.Max(0, wallCellLength - 1)) + 0.5f) * safeGridSize;
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
