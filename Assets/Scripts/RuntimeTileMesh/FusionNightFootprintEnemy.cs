using UnityEngine;
using DuoCurtain.Vision;

namespace DuoCurtain.RuntimeTileMesh
{
    [DisallowMultipleComponent]
    public sealed class FusionNightFootprintEnemy : MonoBehaviour
    {
        public enum TraceEnemyState
        {
            WanderOutside,
            WatchingWindow,
            TargetingDoor,
            BreakingDoor,
            EnteredRoom,
            ChasingPlayer
        }

        [Header("References")]
        public RuntimeTileMeshFusionSandbox fusionSandbox;
        public PlayerControl playerControl;
        public EnemyFootprintTrace footprintTrace;

        [Header("Movement")]
        [Min(0f)]
        public float moveSpeed = 2.1f;
        [Min(0.1f)]
        public float waypointStopDistance = 0.2f;
        [Min(0.5f)]
        public float outsideWanderRadius = 4f;
        [Min(0.1f)]
        public float waypointRetargetInterval = 2f;

        [Header("Window Detection")]
        public bool requireOpenWindow = true;
        [Min(0.1f)]
        public float windowDetectionDistance = 12f;
        [Min(0.1f)]
        public float playerWindowDistance = 8f;
        [Min(0.01f)]
        public float windowCheckInterval = 0.2f;
        public bool targetFusionDoorAfterWindowDetection = true;

        [Header("Door Flow")]
        [Min(0.01f)]
        public float doorTargetStopDistance = 0.25f;
        [Min(0f)]
        public float doorBreakDuration = 1.2f;
        [Min(0f)]
        public float enterRoomDelay = 0.25f;
        public bool openFusionDoorAfterBreak = true;
        public bool showDoorBreakProgress = true;
        public Vector3 doorBreakProgressOffset = new Vector3(0f, 0.75f, 0f);
        [Min(0.01f)]
        public float doorBreakProgressSmoothSpeed = 8f;

        [Header("Sanity Pressure")]
        public bool damageSanityOnWindowDetection = true;
        [Min(0f)]
        public float windowDetectionSanityDamage = 8f;
        public bool damageSanityOnPlayerContact = true;
        [Min(0f)]
        public float contactSanityDamage = 10f;
        [Min(0.01f)]
        public float contactDamageCooldown = 1f;
        [Min(0f)]
        public float contactDamageDistance = 0.55f;

        [Header("Debug")]
        public bool drawDebug;
        public bool debugEnemyFlow;

        [Header("Vision Debug")]
        public bool drawVisionConeInEditor = true;
        public bool drawLineOfSightInEditor = true;
        public bool drawWindowSamplesInEditor = true;

        [Header("Runtime Vision Visual")]
        public bool showVisionInGame = true;
        public VisionSensor2D visionSensor;
        public VisionRenderController visionRenderController;
        [Range(1f, 360f)]
        public float runtimeVisionAngle = 95f;
        [Range(2, 512)]
        public int runtimeVisionRayCount = 72;
        [Range(2, 1024)]
        public int runtimeVisionMaxRayCount = 256;
        [Range(0, 8)]
        public int runtimeVisionEdgeRefinement = 3;
        [Min(0f)]
        public float runtimeVisionEdgeThreshold = 0.35f;
        public LayerMask runtimeVisionObstacleMask = ~0;
        public int runtimeVisionSortingOrder = 52;
        public float runtimeVisionZOffset = -0.2f;

        [Header("Legacy Vision Line Fallback")]
        public bool showWindowProjection = true;
        public Material visionLineMaterial;
        public Color searchingVisionColor = new Color(1f, 0.82f, 0.15f, 0.32f);
        public Color detectedVisionColor = new Color(0.15f, 1f, 0.45f, 0.85f);
        public Color blockedVisionColor = new Color(1f, 0.15f, 0.1f, 0.45f);
        [Min(0.001f)]
        public float visionLineWidth = 0.025f;

        [SerializeField]
        private TraceEnemyState currentState = TraceEnemyState.WanderOutside;

        private Vector3 waypoint;
        private float waypointTimer;
        private float windowCheckTimer;
        private float doorBreakTimer;
        private float enterRoomTimer;
        private bool confirmedPlayerByVision;
        private RuntimeTileMeshFusionDoor targetDoor;
        private Vector3 lastValidOutdoorPosition;
        private WindowPortal lastUsedWindow;
        private Vector3 lastKnownPlayerPosition;
        private Material runtimeVisionMaterial;
        private Transform visionVisualRoot;
        private LineRenderer visionPrimaryLine;
        private LineRenderer visionWindowProjectionLine;
        private RuntimeTileMeshFusionDoor progressBarDoor;
        private DoorBreakProgressBar doorBreakProgressBar;
        private bool windowSanityDamageApplied;
        private float lastContactDamageTime = -999f;
        private Vector2 lastMoveDirection = Vector2.up;

        public TraceEnemyState CurrentState => currentState;

        void Awake()
        {
            ResolveReferences();
            if (footprintTrace == null)
                footprintTrace = GetComponent<EnemyFootprintTrace>();
            if (footprintTrace == null)
                footprintTrace = gameObject.AddComponent<EnemyFootprintTrace>();

            footprintTrace.SetTraceState(EnemyTraceState.NormalMoving);
            lastValidOutdoorPosition = transform.position;
            EnsureVisionSystem();
            PickOutsideWaypoint();
        }

        void Update()
        {
            if (PauseManager.IsGamePaused)
                return;

            ResolveReferences();
            TickWindowDetection();
            TickMovement();
            UpdateRuntimeVisionVisual();
        }

        void OnDestroy()
        {
            if (runtimeVisionMaterial == null)
            {
                DestroyDoorBreakProgressBar();
                return;
            }

            if (Application.isPlaying)
                Destroy(runtimeVisionMaterial);
            else
                DestroyImmediate(runtimeVisionMaterial);

            DestroyDoorBreakProgressBar();
        }

        private void DestroyDoorBreakProgressBar()
        {
            if (doorBreakProgressBar == null)
                return;

            if (Application.isPlaying)
                Destroy(doorBreakProgressBar.gameObject);
            else
                DestroyImmediate(doorBreakProgressBar.gameObject);

            doorBreakProgressBar = null;
            progressBarDoor = null;
        }

        public void Configure(
            RuntimeTileMeshFusionSandbox sandbox,
            PlayerControl player,
            GameObject leftFootprintPrefab,
            GameObject rightFootprintPrefab,
            Transform footprintParent)
        {
            fusionSandbox = sandbox;
            playerControl = player;
            if (footprintTrace == null)
                footprintTrace = GetComponent<EnemyFootprintTrace>();
            if (footprintTrace == null)
                footprintTrace = gameObject.AddComponent<EnemyFootprintTrace>();

            footprintTrace.ConfigureFootprintPrefabs(leftFootprintPrefab, rightFootprintPrefab, footprintParent);
            footprintTrace.SetTraceState(EnemyTraceState.NormalMoving);
            lastValidOutdoorPosition = transform.position;
            EnsureVisionSystem();
        }

        public bool HasEnemyEnteredRoom()
        {
            return currentState == TraceEnemyState.EnteredRoom ||
                   currentState == TraceEnemyState.ChasingPlayer;
        }

        public bool HasEnemyConfirmedPlayerByVision()
        {
            return confirmedPlayerByVision;
        }

        public bool IsInsideAnyFusionRoom(Vector3 worldPosition)
        {
            if (RoomManager.IsInsideAnyRoom(worldPosition))
                return true;

            return fusionSandbox != null && fusionSandbox.ContainsWorldPoint(worldPosition, 0f);
        }

        private void TickWindowDetection()
        {
            if (currentState == TraceEnemyState.TargetingDoor ||
                currentState == TraceEnemyState.BreakingDoor ||
                currentState == TraceEnemyState.EnteredRoom ||
                currentState == TraceEnemyState.ChasingPlayer)
            {
                return;
            }

            windowCheckTimer -= Time.deltaTime;
            if (windowCheckTimer > 0f)
                return;

            windowCheckTimer = Mathf.Max(0.01f, windowCheckInterval);
            bool detected = CanDetectPlayerThroughOpenWindow(out WindowPortal usedWindow);
            if (detected)
            {
                confirmedPlayerByVision = true;
                lastUsedWindow = usedWindow;
                TryApplyWindowDetectionSanityDamage();
                if (debugEnemyFlow)
                {
                    Debug.Log(
                        "[EnemyVision] CanSeePlayer=true Source=OpenWindow Window=" +
                        (usedWindow != null ? usedWindow.name : "unknown"),
                        this);
                }

                if (targetFusionDoorAfterWindowDetection && TrySelectNearestFusionDoor(out targetDoor))
                {
                    SetState(TraceEnemyState.TargetingDoor);
                    return;
                }

                SetState(TraceEnemyState.WatchingWindow);
                return;
            }

            if (debugEnemyFlow)
                Debug.Log("[EnemyVision] CanSeePlayer=false Source=None", this);

            if (currentState != TraceEnemyState.WanderOutside)
            {
                confirmedPlayerByVision = false;
                lastUsedWindow = null;
                windowSanityDamageApplied = false;
                SetState(TraceEnemyState.WanderOutside);
            }
        }

        private void TryApplyWindowDetectionSanityDamage()
        {
            if (!damageSanityOnWindowDetection || windowSanityDamageApplied || windowDetectionSanityDamage <= 0f)
                return;

            FusionSanityController sanity = FusionSanityController.Active != null
                ? FusionSanityController.Active
                : FindFirstObjectByType<FusionSanityController>();
            if (sanity == null)
                return;

            sanity.DrainSanity(windowDetectionSanityDamage);
            windowSanityDamageApplied = true;
        }

        private void TickMovement()
        {
            Vector3 target = waypoint;
            switch (currentState)
            {
                case TraceEnemyState.TargetingDoor:
                    if (targetDoor == null)
                    {
                        SetState(TraceEnemyState.WatchingWindow);
                        return;
                    }

                    target = targetDoor.transform.position;
                    if (MoveTowards(target, false) || Vector2.Distance(transform.position, target) <= doorTargetStopDistance)
                        SetState(TraceEnemyState.BreakingDoor);
                    return;

                case TraceEnemyState.BreakingDoor:
                    doorBreakTimer += Time.deltaTime;
                    UpdateDoorBreakProgress();
                    if (doorBreakTimer >= doorBreakDuration)
                    {
                        if (openFusionDoorAfterBreak && targetDoor != null)
                            targetDoor.OpenToward((Vector2)(targetDoor.transform.position - transform.position));
                        SetState(TraceEnemyState.EnteredRoom);
                    }
                    return;

                case TraceEnemyState.EnteredRoom:
                    enterRoomTimer += Time.deltaTime;
                    if (enterRoomTimer >= enterRoomDelay)
                        SetState(TraceEnemyState.ChasingPlayer);
                    return;

                case TraceEnemyState.ChasingPlayer:
                    if (playerControl != null && playerControl.HasPlayerWorldPosition)
                    {
                        MoveTowards(playerControl.PlayerWorldPosition, true);
                        TryApplyContactSanityDamage();
                    }
                    return;

                default:
                    waypointTimer -= Time.deltaTime;
                    if (waypointTimer <= 0f || Vector2.Distance(transform.position, waypoint) <= waypointStopDistance)
                        PickOutsideWaypoint();
                    break;
            }

            MoveTowards(target, false);
        }

        private void TryApplyContactSanityDamage()
        {
            if (!damageSanityOnPlayerContact ||
                contactSanityDamage <= 0f ||
                playerControl == null ||
                !playerControl.HasPlayerWorldPosition ||
                Time.time < lastContactDamageTime + contactDamageCooldown)
            {
                return;
            }

            if (Vector2.Distance(transform.position, playerControl.PlayerWorldPosition) > contactDamageDistance)
                return;

            FusionSanityController sanity = FusionSanityController.Active != null
                ? FusionSanityController.Active
                : FindFirstObjectByType<FusionSanityController>();
            if (sanity == null)
                return;

            sanity.DrainSanity(contactSanityDamage);
            lastContactDamageTime = Time.time;
        }

        private bool CanDetectPlayerThroughOpenWindow(out WindowPortal usedWindow)
        {
            usedWindow = null;
            if (playerControl == null || !playerControl.HasPlayerWorldPosition)
                return false;

            Vector2 playerPosition = playerControl.PlayerWorldPosition;
            lastKnownPlayerPosition = playerPosition;
            WindowPortal[] windows = FindObjectsByType<WindowPortal>(FindObjectsSortMode.None);
            for (int i = 0; i < windows.Length; i++)
            {
                WindowPortal window = windows[i];
                if (window == null || !window.isActiveAndEnabled)
                    continue;

                if (requireOpenWindow && !window.IsOpen)
                    continue;

                Vector2 windowPosition = window.transform.position;
                if (Vector2.Distance(transform.position, windowPosition) > windowDetectionDistance)
                    continue;

                if (Vector2.Distance(windowPosition, playerPosition) > playerWindowDistance)
                    continue;

                usedWindow = window;
                return true;
            }

            return false;
        }

        private void PickOutsideWaypoint()
        {
            waypointTimer = waypointRetargetInterval;
            Vector3 center = transform.position;
            Bounds bounds;
            if (fusionSandbox != null && fusionSandbox.TryGetWorldBounds(out bounds))
                center = bounds.center;

            float radius = Mathf.Max(0.5f, outsideWanderRadius);
            Vector2 random = Random.insideUnitCircle.normalized * Random.Range(radius * 0.5f, radius);
            if (random.sqrMagnitude <= 0.001f)
                random = Vector2.right * radius;

            waypoint = new Vector3(center.x + random.x, center.y + random.y, transform.position.z);
            if (IsInsideAnyFusionRoom(waypoint))
            {
                waypoint = lastValidOutdoorPosition;
                waypoint += new Vector3(random.x, random.y, 0f).normalized * Mathf.Max(1f, radius);
            }
        }

        private bool MoveTowards(Vector3 target, bool allowIndoor)
        {
            Vector2 delta = (Vector2)(target - transform.position);
            float distance = delta.magnitude;
            if (distance <= waypointStopDistance)
                return true;

            Vector2 step = delta.normalized * (moveSpeed * Time.deltaTime);
            lastMoveDirection = delta.normalized;
            if (step.magnitude > distance)
                step = delta;

            Vector3 nextPosition = transform.position + new Vector3(step.x, step.y, 0f);
            if (!allowIndoor && IsInsideAnyFusionRoom(nextPosition))
            {
                transform.position = lastValidOutdoorPosition;
                PickOutsideWaypoint();
                if (debugEnemyFlow)
                    Debug.Log("[EnemyFlow] Outdoor movement clamped before entering room. Position=" + nextPosition, this);
                return false;
            }

            transform.position = nextPosition;
            if (!IsInsideAnyFusionRoom(transform.position))
                lastValidOutdoorPosition = transform.position;

            return Vector2.Distance(transform.position, target) <= waypointStopDistance;
        }

        private void SetState(TraceEnemyState state)
        {
            if (currentState == state)
                return;

            if (debugEnemyFlow)
                Debug.Log("[EnemyFlow] State=" + currentState + " -> " + state, this);

            currentState = state;
            if (footprintTrace == null)
                return;

            switch (currentState)
            {
                case TraceEnemyState.WatchingWindow:
                    HideDoorBreakProgress();
                    footprintTrace.SetTraceState(EnemyTraceState.Watching);
                    break;
                case TraceEnemyState.TargetingDoor:
                    HideDoorBreakProgress();
                    footprintTrace.SetTraceState(EnemyTraceState.TargetingDoor);
                    break;
                case TraceEnemyState.BreakingDoor:
                    doorBreakTimer = 0f;
                    EnsureDoorBreakProgress();
                    if (doorBreakProgressBar != null)
                        doorBreakProgressBar.SetProgress(0f, true);
                    footprintTrace.SetTraceState(EnemyTraceState.BreakingDoor);
                    break;
                case TraceEnemyState.EnteredRoom:
                    enterRoomTimer = 0f;
                    if (doorBreakProgressBar != null)
                        doorBreakProgressBar.SetProgress(1f, true);
                    footprintTrace.SetTraceState(EnemyTraceState.ChasingPlayer);
                    break;
                case TraceEnemyState.ChasingPlayer:
                    HideDoorBreakProgress();
                    footprintTrace.SetTraceState(EnemyTraceState.ChasingPlayer);
                    break;
                default:
                    HideDoorBreakProgress();
                    footprintTrace.SetTraceState(EnemyTraceState.NormalMoving);
                    break;
            }
        }

        private void EnsureDoorBreakProgress()
        {
            if (!showDoorBreakProgress || targetDoor == null)
                return;

            if (doorBreakProgressBar != null && progressBarDoor == targetDoor)
                return;

            if (doorBreakProgressBar != null)
                Destroy(doorBreakProgressBar.gameObject);

            progressBarDoor = targetDoor;
            doorBreakProgressBar = DoorBreakProgressBar.CreateDefault(targetDoor.transform, doorBreakProgressOffset);
            doorBreakProgressBar.smoothSpeed = Mathf.Max(0.01f, doorBreakProgressSmoothSpeed);
        }

        private void UpdateDoorBreakProgress()
        {
            if (!showDoorBreakProgress)
                return;

            EnsureDoorBreakProgress();
            if (doorBreakProgressBar == null)
                return;

            float normalized = doorBreakDuration > 0.0001f
                ? Mathf.Clamp01(doorBreakTimer / doorBreakDuration)
                : 1f;
            doorBreakProgressBar.SetProgress(normalized, true);
        }

        private void HideDoorBreakProgress()
        {
            if (doorBreakProgressBar != null)
                doorBreakProgressBar.SetVisible(false);
        }

        private bool TrySelectNearestFusionDoor(out RuntimeTileMeshFusionDoor nearestDoor)
        {
            nearestDoor = null;
            RuntimeTileMeshFusionDoor[] doors = FindObjectsByType<RuntimeTileMeshFusionDoor>(FindObjectsSortMode.None);
            float bestDistanceSqr = float.MaxValue;
            for (int i = 0; i < doors.Length; i++)
            {
                RuntimeTileMeshFusionDoor door = doors[i];
                if (door == null || !door.isActiveAndEnabled || IsHiddenPreviewDoor(door))
                    continue;

                float distanceSqr = ((Vector2)door.transform.position - (Vector2)transform.position).sqrMagnitude;
                if (distanceSqr >= bestDistanceSqr)
                    continue;

                bestDistanceSqr = distanceSqr;
                nearestDoor = door;
            }

            if (debugEnemyFlow)
            {
                Debug.Log(
                    nearestDoor != null
                        ? "[EnemyFlow] Target door=" + nearestDoor.name
                        : "[EnemyFlow] No fusion door available after vision confirmation.",
                    this);
            }

            return nearestDoor != null;
        }

        private static bool IsHiddenPreviewDoor(RuntimeTileMeshFusionDoor door)
        {
            HideFlags flags = door.gameObject.hideFlags;
            return (flags & HideFlags.HideAndDontSave) == HideFlags.HideAndDontSave ||
                   (flags & HideFlags.DontSaveInEditor) == HideFlags.DontSaveInEditor ||
                   (flags & HideFlags.DontSaveInBuild) == HideFlags.DontSaveInBuild ||
                   !door.gameObject.scene.IsValid();
        }

        private void ResolveReferences()
        {
            if (fusionSandbox == null)
                fusionSandbox = FindFirstObjectByType<RuntimeTileMeshFusionSandbox>();
            if (playerControl == null)
                playerControl = PlayerControl.Active != null ? PlayerControl.Active : FindFirstObjectByType<PlayerControl>();
        }

        private void UpdateRuntimeVisionVisual()
        {
            EnsureVisionSystem();
            SetVisionLinesVisible(false);

            if (visionRenderController == null || visionSensor == null)
            {
                return;
            }

            visionRenderController.SetVisible(showVisionInGame);
            if (!showVisionInGame)
            {
                return;
            }

            Color primary = confirmedPlayerByVision ? detectedVisionColor : searchingVisionColor;
            Color secondary = confirmedPlayerByVision
                ? new Color(detectedVisionColor.r, detectedVisionColor.g, detectedVisionColor.b, Mathf.Min(1f, detectedVisionColor.a * 1.35f))
                : new Color(searchingVisionColor.r, searchingVisionColor.g, searchingVisionColor.b, Mathf.Min(1f, searchingVisionColor.a * 1.6f));
            visionRenderController.renderParameters.primaryColor = primary;
            visionRenderController.renderParameters.secondaryColor = secondary;
            visionSensor.SetForward(lastMoveDirection);
            visionSensor.ForceSample();
        }

        private void EnsureVisionSystem()
        {
            if (visionSensor == null)
                visionSensor = GetComponent<VisionSensor2D>();
            if (visionSensor == null)
                visionSensor = gameObject.AddComponent<VisionSensor2D>();

            visionSensor.forwardSource = VisionSensor2D.ForwardSource.Manual;
            visionSensor.sampleAutomatically = false;
            visionSensor.viewDistance = Mathf.Max(0.1f, windowDetectionDistance);
            visionSensor.viewAngle = Mathf.Clamp(runtimeVisionAngle, 1f, 360f);
            visionSensor.rayCount = Mathf.Clamp(runtimeVisionRayCount, 2, 512);
            visionSensor.maxRayCount = Mathf.Clamp(runtimeVisionMaxRayCount, visionSensor.rayCount, 1024);
            visionSensor.edgeRefinementIterations = Mathf.Clamp(runtimeVisionEdgeRefinement, 0, 8);
            visionSensor.edgeDistanceThreshold = Mathf.Max(0f, runtimeVisionEdgeThreshold);
            visionSensor.obstacleMask = runtimeVisionObstacleMask;
            visionSensor.hitTriggers = false;
            visionSensor.SetForward(lastMoveDirection);

            if (visionRenderController == null)
                visionRenderController = GetComponent<VisionRenderController>();
            if (visionRenderController == null)
                visionRenderController = gameObject.AddComponent<VisionRenderController>();

            visionRenderController.sensor = visionSensor;
            visionRenderController.sortingOrder = runtimeVisionSortingOrder;
            visionRenderController.zOffset = runtimeVisionZOffset;
            visionRenderController.renderEnabled = showVisionInGame;
        }

        private void EnsureVisionLines()
        {
            if (visionVisualRoot == null)
            {
                GameObject root = new GameObject("Fusion Enemy Vision Visuals");
                root.transform.SetParent(transform, false);
                visionVisualRoot = root.transform;
            }

            if (visionPrimaryLine == null)
                visionPrimaryLine = CreateVisionLine("Vision Primary Line");
            if (visionWindowProjectionLine == null)
                visionWindowProjectionLine = CreateVisionLine("Vision Window Projection");
        }

        private LineRenderer CreateVisionLine(string lineName)
        {
            GameObject lineObject = new GameObject(lineName);
            lineObject.transform.SetParent(visionVisualRoot, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.sharedMaterial = GetVisionMaterial();
            line.sortingOrder = 55;
            return line;
        }

        private void ConfigureVisionLine(LineRenderer line, Vector3 start, Vector3 end, Color color)
        {
            if (line == null)
                return;

            line.enabled = true;
            line.sharedMaterial = GetVisionMaterial();
            line.widthMultiplier = Mathf.Max(0.001f, visionLineWidth);
            line.startColor = color;
            line.endColor = color;
            line.SetPosition(0, new Vector3(start.x, start.y, -0.25f));
            line.SetPosition(1, new Vector3(end.x, end.y, -0.25f));
        }

        private void SetVisionLinesVisible(bool visible)
        {
            if (visionPrimaryLine != null)
                visionPrimaryLine.enabled = visible;
            if (visionWindowProjectionLine != null)
                visionWindowProjectionLine.enabled = visible && showWindowProjection;
        }

        private Material GetVisionMaterial()
        {
            if (visionLineMaterial != null)
                return visionLineMaterial;
            if (runtimeVisionMaterial != null)
                return runtimeVisionMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader == null)
                return null;

            runtimeVisionMaterial = new Material(shader)
            {
                name = "Fusion Enemy Vision Line",
                hideFlags = HideFlags.HideAndDontSave
            };
            return runtimeVisionMaterial;
        }

        void OnDrawGizmosSelected()
        {
            if (!drawDebug)
                return;

            if (drawVisionConeInEditor)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(transform.position, windowDetectionDistance);
            }

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, waypoint);

            if (drawLineOfSightInEditor)
            {
                Gizmos.color = confirmedPlayerByVision ? Color.green : Color.red;
                Gizmos.DrawLine(transform.position, lastKnownPlayerPosition);
            }

            if (drawWindowSamplesInEditor && lastUsedWindow != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawSphere(lastUsedWindow.transform.position, 0.08f);
                Gizmos.DrawLine(transform.position, lastUsedWindow.transform.position);
                Gizmos.DrawLine(lastUsedWindow.transform.position, lastKnownPlayerPosition);
            }

            if (targetDoor != null)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawSphere(targetDoor.transform.position, 0.1f);
            }
        }
    }
}
