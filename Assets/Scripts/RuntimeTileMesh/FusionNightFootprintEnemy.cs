using UnityEngine;
using DuoCurtain.Combat;
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
        [Min(0f)]
        public float enemyCollisionRadius = 0.22f;
        [Min(0.1f)]
        public float waypointStopDistance = 0.2f;
        [Min(0.5f)]
        public float outsideWanderRadius = 4f;
        [Min(0.1f)]
        public float waypointRetargetInterval = 2f;

        [Header("Visibility Detection")]
        [Min(0.1f)]
        public float windowDetectionDistance = 12f;
        [Min(0.01f)]
        public float windowCheckInterval = 0.2f;
        public bool targetFusionDoorAfterWindowDetection = true;

        [Header("Door Attack")]
        [Min(0.01f)]
        public float doorTargetStopDistance = 0.25f;
        [Min(0f)] public float doorAttackDamage = 20f;
        [Min(0.01f)] public float doorAttackInterval = 1f;
        [Min(0f)] public float doorAttackWindup = 0.25f;
        [Min(0f)] public float doorAttackRecovery = 0.75f;
        [Min(0f)] public float doorAttackRange = 0.6f;
        public ImpactFeedbackPreset doorImpactPreset;
        [Min(0f)]
        public float enterRoomDelay = 0.25f;

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

        [Header("Boot World")]
        public bool suppressTrackingDuringBootWorld = true;
        public bool suppressVisionAlertDuringBootWorld = true;

        [Header("Vision Debug")]
        public bool drawVisionConeInEditor = true;
        public bool drawLineOfSightInEditor = true;

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

        [Header("Vision Alert Feedback")]
        public bool useVisionAlertColorProgress = true;
        [Min(0.05f)]
        public float visionAlertConfirmSeconds = 1f;
        [Min(0.01f)]
        public float visionAlertFadeSeconds = 0.45f;
        public Color visionAlertColor = new Color(1f, 0.12f, 0.06f, 0.62f);

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
        private float enterRoomTimer;
        private bool confirmedPlayerByVision;
        private RuntimeTileMeshFusionDoor targetDoor;
        private Vector3 lastValidOutdoorPosition;
        private Vector3 lastKnownPlayerPosition;
        private Material runtimeVisionMaterial;
        private Transform visionVisualRoot;
        private LineRenderer visionPrimaryLine;
        private LineRenderer visionWindowProjectionLine;
        private CombatAttackSource doorAttackSource;
        private bool windowSanityDamageApplied;
        private float lastContactDamageTime = -999f;
        private Vector2 lastMoveDirection = Vector2.up;
        private float visionAlertProgress;
        private VisionDetectionSource lastDetectionSource;
        private int visionSampleFrame = -1;
        private VisionSnapshot cachedVisionSnapshot;

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
            EnsureDoorAttackSource();
            EnsureVisionSystem();
            PickOutsideWaypoint();
        }

        void Update()
        {
            if (PauseManager.IsGamePaused)
                return;

            ResolveReferences();
            if (IsBootWorldPassive())
            {
                TickBootWorldPassive();
                return;
            }

            TickWindowDetection();
            TickMovement();
            UpdateRuntimeVisionVisual();
        }

        void OnDestroy()
        {
            UnbindDoorAttackSource();
            if (runtimeVisionMaterial == null)
                return;

            if (Application.isPlaying)
                Destroy(runtimeVisionMaterial);
            else
                DestroyImmediate(runtimeVisionMaterial);

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
            EnsureDoorAttackSource();
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
            if (IsBootWorldPassive())
                return;

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
            bool detected = CanDetectPlayerThroughVisibilityWorld();
            if (detected)
            {
                confirmedPlayerByVision = true;
                TryApplyWindowDetectionSanityDamage();
                if (debugEnemyFlow)
                {
                    Debug.Log(
                        "[EnemyVision] CanSeePlayer=true Source=" + lastDetectionSource,
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
                lastDetectionSource = VisionDetectionSource.None;
                windowSanityDamageApplied = false;
                SetState(TraceEnemyState.WanderOutside);
            }
        }

        private void TickBootWorldPassive()
        {
            if (currentState != TraceEnemyState.WanderOutside)
            {
                doorAttackSource?.CancelAttack();
                targetDoor = null;
                confirmedPlayerByVision = false;
                lastDetectionSource = VisionDetectionSource.None;
                windowSanityDamageApplied = false;
                SetState(TraceEnemyState.WanderOutside);
            }

            TickMovement();
            if (suppressVisionAlertDuringBootWorld)
                visionAlertProgress = 0f;

            UpdateRuntimeVisionVisual();
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
                    if (targetDoor == null)
                    {
                        doorAttackSource?.CancelAttack();
                        SetState(TraceEnemyState.WanderOutside);
                        return;
                    }

                    if (targetDoor.IsDestroyed || targetDoor.IsOpen)
                    {
                        doorAttackSource?.CancelAttack();
                        SetState(TraceEnemyState.EnteredRoom);
                        return;
                    }

                    float distanceToDoor = Vector2.Distance(transform.position, targetDoor.transform.position);
                    if (distanceToDoor > Mathf.Max(doorAttackRange, doorTargetStopDistance))
                    {
                        doorAttackSource?.CancelAttack();
                        SetState(TraceEnemyState.TargetingDoor);
                        return;
                    }

                    EnsureDoorAttackSource();
                    if (doorAttackSource != null && !doorAttackSource.IsAttacking)
                        doorAttackSource.BeginAttack(targetDoor);
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

        private bool CanDetectPlayerThroughVisibilityWorld()
        {
            lastDetectionSource = VisionDetectionSource.None;
            if (IsBootWorldPassive())
                return false;

            if (playerControl == null || !playerControl.HasPlayerWorldPosition)
                return false;

            Vector2 playerPosition = playerControl.PlayerWorldPosition;
            lastKnownPlayerPosition = playerPosition;

            if (!TrySampleVisionThisFrame(out VisionSnapshot snapshot))
                return false;

            if (!snapshot.TryGetDetectionSource(playerPosition, out VisionDetectionSource detectionSource))
            {
                return false;
            }

            lastDetectionSource = detectionSource;
            return true;
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
            if (allowIndoor && fusionSandbox != null)
            {
                nextPosition = fusionSandbox.ResolvePlayerWorldPoint(
                    nextPosition,
                    transform.position,
                    enemyCollisionRadius,
                    true);
            }

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

            if (currentState == TraceEnemyState.BreakingDoor && state != TraceEnemyState.BreakingDoor)
                doorAttackSource?.CancelAttack();

            if (debugEnemyFlow)
                Debug.Log("[EnemyFlow] State=" + currentState + " -> " + state, this);

            currentState = state;
            if (footprintTrace == null)
                return;

            switch (currentState)
            {
                case TraceEnemyState.WatchingWindow:
                    footprintTrace.SetTraceState(EnemyTraceState.Watching);
                    break;
                case TraceEnemyState.TargetingDoor:
                    footprintTrace.SetTraceState(EnemyTraceState.TargetingDoor);
                    break;
                case TraceEnemyState.BreakingDoor:
                    EnsureDoorAttackSource();
                    if (targetDoor != null)
                        doorAttackSource?.BeginAttack(targetDoor);
                    footprintTrace.SetTraceState(EnemyTraceState.BreakingDoor);
                    break;
                case TraceEnemyState.EnteredRoom:
                    enterRoomTimer = 0f;
                    footprintTrace.SetTraceState(EnemyTraceState.ChasingPlayer);
                    break;
                case TraceEnemyState.ChasingPlayer:
                    footprintTrace.SetTraceState(EnemyTraceState.ChasingPlayer);
                    break;
                default:
                    footprintTrace.SetTraceState(EnemyTraceState.NormalMoving);
                    break;
            }
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

        private void EnsureDoorAttackSource()
        {
            if (doorAttackSource == null)
                doorAttackSource = GetComponent<CombatAttackSource>();
            if (doorAttackSource == null)
                doorAttackSource = gameObject.AddComponent<CombatAttackSource>();

            doorAttackSource.attackDamage = Mathf.Max(0f, doorAttackDamage);
            doorAttackSource.attackInterval = Mathf.Max(0.01f, doorAttackInterval);
            doorAttackSource.windupDuration = Mathf.Max(0f, doorAttackWindup);
            doorAttackSource.recoveryDuration = Mathf.Max(0f, doorAttackRecovery);
            doorAttackSource.attackRange = Mathf.Max(0f, doorAttackRange);
            doorAttackSource.impactPreset = doorImpactPreset;
            doorAttackSource.Impacted -= HandleDoorAttackImpact;
            doorAttackSource.TargetDestroyed -= HandleDoorDestroyed;
            doorAttackSource.Impacted += HandleDoorAttackImpact;
            doorAttackSource.TargetDestroyed += HandleDoorDestroyed;
        }

        private void UnbindDoorAttackSource()
        {
            if (doorAttackSource == null)
                return;
            doorAttackSource.Impacted -= HandleDoorAttackImpact;
            doorAttackSource.TargetDestroyed -= HandleDoorDestroyed;
        }

        private void HandleDoorAttackImpact(CombatAttackSource source, DamageResult result)
        {
            if (debugEnemyFlow)
            {
                Debug.Log(
                    "[EnemyCombat] phase=Impact hp=" + result.currentHealth.ToString("0.0") +
                    " destroyed=" + result.destroyed,
                    this);
            }
        }

        private void HandleDoorDestroyed(CombatAttackSource source, IDamageReceiver receiver)
        {
            if (targetDoor == null || receiver == null || receiver.ReceiverObject != targetDoor.gameObject)
                return;
            SetState(TraceEnemyState.EnteredRoom);
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

            if (!TrySampleVisionThisFrame(out VisionSnapshot snapshot))
                return;

            bool seesPlayer = CanSeePlayerWithRuntimeVision();
            UpdateVisionAlertProgress(seesPlayer);

            Color primary;
            Color secondary;
            if (useVisionAlertColorProgress)
            {
                primary = Color.Lerp(searchingVisionColor, visionAlertColor, visionAlertProgress);
                secondary = new Color(primary.r, primary.g, primary.b, Mathf.Min(1f, primary.a * 1.45f));
            }
            else
            {
                primary = confirmedPlayerByVision ? detectedVisionColor : searchingVisionColor;
                secondary = confirmedPlayerByVision
                    ? new Color(detectedVisionColor.r, detectedVisionColor.g, detectedVisionColor.b, Mathf.Min(1f, detectedVisionColor.a * 1.35f))
                    : new Color(searchingVisionColor.r, searchingVisionColor.g, searchingVisionColor.b, Mathf.Min(1f, searchingVisionColor.a * 1.6f));
            }

            visionRenderController.renderParameters.primaryColor = primary;
            visionRenderController.renderParameters.secondaryColor = secondary;
            visionRenderController.renderParameters.portalColor = new Color(
                primary.r,
                primary.g,
                primary.b,
                Mathf.Clamp01(primary.a * 0.72f));
            visionRenderController.Render(snapshot);
        }

        private bool CanSeePlayerWithRuntimeVision()
        {
            if (IsBootWorldPassive())
                return false;

            if (visionSensor == null || playerControl == null || !playerControl.HasPlayerWorldPosition)
                return false;

            VisionSnapshot snapshot = cachedVisionSnapshot != null ? cachedVisionSnapshot : visionSensor.LatestSnapshot;
            return snapshot != null && snapshot.ContainsWorldPoint(playerControl.PlayerWorldPosition);
        }

        private bool TrySampleVisionThisFrame(out VisionSnapshot snapshot)
        {
            EnsureVisionSystem();
            snapshot = null;
            if (visionSensor == null)
                return false;

            visionSensor.SetForward(lastMoveDirection);
            if (visionSampleFrame != Time.frameCount || cachedVisionSnapshot == null)
            {
                visionSensor.ForceSample();
                cachedVisionSnapshot = visionSensor.LatestSnapshot;
                visionSampleFrame = Time.frameCount;
            }

            snapshot = cachedVisionSnapshot;
            return snapshot != null;
        }

        private void UpdateVisionAlertProgress(bool seesPlayer)
        {
            if (IsBootWorldPassive() && suppressVisionAlertDuringBootWorld)
            {
                visionAlertProgress = 0f;
                return;
            }

            bool lockedAlert =
                confirmedPlayerByVision ||
                currentState == TraceEnemyState.TargetingDoor ||
                currentState == TraceEnemyState.BreakingDoor ||
                currentState == TraceEnemyState.EnteredRoom ||
                currentState == TraceEnemyState.ChasingPlayer;

            if (lockedAlert)
            {
                visionAlertProgress = 1f;
                return;
            }

            if (seesPlayer)
            {
                float duration = Mathf.Max(0.05f, visionAlertConfirmSeconds);
                visionAlertProgress = Mathf.Clamp01(visionAlertProgress + Time.deltaTime / duration);
                return;
            }

            float fadeDuration = Mathf.Max(0.01f, visionAlertFadeSeconds);
            visionAlertProgress = Mathf.Clamp01(visionAlertProgress - Time.deltaTime / fadeDuration);
        }

        private bool IsBootWorldPassive()
        {
            return suppressTrackingDuringBootWorld && BootWorldStateController.IsBootWorldActiveGlobally;
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

            if (targetDoor != null)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawSphere(targetDoor.transform.position, 0.1f);
            }
        }
    }
}
