using System.Collections.Generic;
using DuoCurtain.Combat;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Outside-room enemy that detects the player through cone vision and open windows,
/// paths to the room exterior door, breaks it, enters, then chases and attacks.
/// </summary>
[DisallowMultipleComponent]
public class EnemyController : MonoBehaviour
{
    public enum EnemyState
    {
        SpawnOutside,
        SearchOutside,
        DetectPlayer,
        MoveToExteriorDoor,
        BreakingDoor,
        EnterRoom,
        ChasePlayer,
        AttackPlayer,
        LostPlayer,
        SearchLastKnownRoom
    }

    [Header("References")]
    public PlayerControl playerControl;
    public Transform playerTarget;
    public PlayerSanityDamageable playerDamageable;
    public SanitySystem playerSanity;
    public EnemyVision vision;
    public EnemyFootprintTrace footprintTrace;

    [Header("Movement")]
    [Min(0f)] public float moveSpeed = 2.5f;
    [Min(0f)] public float doorTargetingSpeedMultiplier = 1.65f;
    [Min(0f)] public float rotationSpeed = 360f;
    [Min(0f)] public float stoppingDistance = 0.6f;
    [Min(0f)] public float doorApproachDistance = 0.4f;

    [Header("Vision")]
    [Min(0f)] public float viewDistance = 8f;
    [Range(1f, 179f)] public float viewAngle = 90f;
    public LayerMask playerLayer;
    public LayerMask wallLayer;
    public LayerMask windowLayer;
    [Min(0f)] public float detectionConfirmTime = 0.5f;

    [Header("Window Vision")]
    public bool requireOpenWindow = true;
    [Min(1)] public int windowVisionSampleCount = 5;
    [Min(0f)] public float windowVisionSamplePadding = 0.05f;
    [Min(0.01f)] public float windowCheckInterval = 0.1f;

    [Header("Room Detection")]
    public bool lockRoomAfterDetection = true;
    [Min(0f)] public float roomMemoryDuration = 5f;
    public bool chaseLastKnownRoom = true;

    [Header("Door Attack")]
    [FormerlySerializedAs("doorBreakSpeed"), Min(0f)] public float doorAttackDamage = 20f;
    [Min(0.01f)] public float doorAttackInterval = 1f;
    [FormerlySerializedAs("doorBreakStartDelay"), Min(0f)] public float doorAttackWindup = 0.25f;
    [Min(0f)] public float doorAttackRecovery = 0.75f;
    [FormerlySerializedAs("doorBreakRange"), Min(0f)] public float doorAttackRange = 0.6f;
    public ImpactFeedbackPreset doorImpactPreset;
    public bool faceDoorWhileBreaking = true;
    public bool stopMovingWhileBreaking = true;

    [Header("Attack")]
    [Min(0f)] public float attackRange = 0.8f;
    [Min(0f)] public float attackDamage = 1f;
    [Min(0f)] public float attackCooldown = 1.5f;
    [Min(0f)] public float attackWindupTime = 0.4f;

    [Header("State Timing")]
    [Min(0.01f)] public float searchInterval = 0.25f;
    [Min(0f)] public float lostSightDelay = 1f;
    [Min(0f)] public float investigateDuration = 2f;
    [Min(0f)] public float enterRoomDelay = 0.4f;

    [Header("Spawn")]
    [Min(0f)] public float spawnNearPlayerMinDistance = 2f;
    [Min(0f)] public float spawnNearPlayerMaxDistance = 12f;
    public bool autoRelocateInvalidSpawn = true;

    [Header("Debug")]
    public bool drawVisionCone = true;
    public bool drawLineOfSight = true;
    public bool drawWindowVisionSamples = true;
    public bool logStateChanges = true;

    [SerializeField] private EnemyState currentState = EnemyState.SpawnOutside;

    private Vector2 facingDirection = Vector2.up;
    private float detectionTimer;
    private float searchTimer;
    private float windowCheckTimer;
    private float attackCooldownTimer;
    private float attackWindupTimer;
    private float enterRoomTimer;
    private float lostSightTimer;
    private float investigateTimer;
    private float roomMemoryTimer;

    private Room targetRoom;
    private BreakableExteriorDoor targetDoor;
    private CombatAttackSource doorAttackSource;
    private bool playerInsideRoom;
    private bool insideTargetRoom;
    private EnemyVision.VisionResult lastVisionResult;

    private readonly List<WindowPortal> windowCache = new List<WindowPortal>();
    private readonly List<Vector2> debugSamplePoints = new List<Vector2>();

    public EnemyState CurrentState => currentState;
    public Vector2 FacingDirection => facingDirection;
    public float CurrentMoveSpeed => GetEffectiveMoveSpeed();
    public bool HasTargetDoor => targetDoor != null;

    void Awake()
    {
        if (vision == null)
            vision = GetComponent<EnemyVision>();
        if (vision == null)
            vision = gameObject.AddComponent<EnemyVision>();

        if (footprintTrace == null)
            footprintTrace = GetComponent<EnemyFootprintTrace>();

        ResolvePlayerReferences();
        EnsureDoorAttackSource();
        RefreshWindowCache();
    }

    void Start()
    {
        EnterState(EnemyState.SpawnOutside);
    }

    void Update()
    {
        if (PauseManager.IsGamePaused)
            return;

        ResolvePlayerReferences();
        UpdateTimers();
        TickCurrentState();
        UpdateFacingFromVelocity();
    }

    private void TickCurrentState()
    {
        switch (currentState)
        {
            case EnemyState.SpawnOutside:
                TickSpawnOutside();
                break;
            case EnemyState.SearchOutside:
                TickSearchOutside();
                break;
            case EnemyState.DetectPlayer:
                TickDetectPlayer();
                break;
            case EnemyState.MoveToExteriorDoor:
                TickMoveToExteriorDoor();
                break;
            case EnemyState.BreakingDoor:
                TickBreakingDoor();
                break;
            case EnemyState.EnterRoom:
                TickEnterRoom();
                break;
            case EnemyState.ChasePlayer:
                TickChasePlayer();
                break;
            case EnemyState.AttackPlayer:
                TickAttackPlayer();
                break;
            case EnemyState.LostPlayer:
                TickLostPlayer();
                break;
            case EnemyState.SearchLastKnownRoom:
                TickSearchLastKnownRoom();
                break;
        }
    }

    private void TickSpawnOutside()
    {
        if (autoRelocateInvalidSpawn && RoomManager.IsInsideAnyRoom(transform.position))
        {
            if (TryFindOutsideSpawnNearPlayer(out Vector3 spawnPosition))
                transform.position = spawnPosition;
        }

        EnterState(EnemyState.SearchOutside);
    }

    private void TickSearchOutside()
    {
        searchTimer -= Time.deltaTime;
        if (searchTimer > 0f)
            return;

        searchTimer = searchInterval;
        windowCheckTimer -= searchInterval;
        if (windowCheckTimer <= 0f)
        {
            windowCheckTimer = windowCheckInterval;
            RefreshWindowCache();
        }

        if (TryEvaluateVision(out EnemyVision.VisionResult visionResult) && visionResult.isVisible)
        {
            lastVisionResult = visionResult;
            if (visionResult.detectedRoom != null)
                targetRoom = visionResult.detectedRoom;

            playerInsideRoom = RoomManager.IsInsideAnyRoom(GetPlayerPosition());
            if (playerInsideRoom || targetRoom != null)
                EnterState(EnemyState.DetectPlayer);
        }
    }

    private void TickDetectPlayer()
    {
        if (lockRoomAfterDetection && targetRoom == null && lastVisionResult.detectedRoom != null)
            targetRoom = lastVisionResult.detectedRoom;

        if (targetRoom == null && playerInsideRoom)
            targetRoom = RoomManager.GetRoomAtPosition(GetPlayerPosition());

        detectionTimer += Time.deltaTime;
        if (detectionTimer < detectionConfirmTime)
            return;

        targetDoor = targetRoom != null ? targetRoom.exteriorDoor : null;
        if (targetDoor == null)
        {
            EnterState(EnemyState.SearchOutside);
            return;
        }

        roomMemoryTimer = roomMemoryDuration;
        EnterState(EnemyState.MoveToExteriorDoor);
    }

    private void TickMoveToExteriorDoor()
    {
        if (targetDoor == null)
        {
            EnterState(EnemyState.SearchOutside);
            return;
        }

        Vector3 approach = targetDoor.OutsideApproachPosition;
        if (MoveTowards(approach, doorApproachDistance))
            EnterState(EnemyState.BreakingDoor);
    }

    private void TickBreakingDoor()
    {
        if (targetDoor == null)
        {
            EnterState(EnemyState.SearchOutside);
            return;
        }

        if (targetDoor.IsOpen)
        {
            targetDoor.StopBreaking();
            EnterState(EnemyState.EnterRoom);
            return;
        }

        if (stopMovingWhileBreaking)
            FaceTowards(targetDoor.transform.position);

        float distance = Vector2.Distance(transform.position, targetDoor.OutsideApproachPosition);
        if (distance > doorAttackRange)
        {
            doorAttackSource?.CancelAttack();
            MoveTowards(targetDoor.OutsideApproachPosition, doorAttackRange * 0.5f);
            return;
        }

        if (faceDoorWhileBreaking)
            FaceTowards(targetDoor.transform.position);

        EnsureDoorAttackSource();
        if (doorAttackSource != null && !doorAttackSource.IsAttacking)
            doorAttackSource.BeginAttack(targetDoor);
    }

    private void TickEnterRoom()
    {
        enterRoomTimer += Time.deltaTime;
        if (enterRoomTimer < enterRoomDelay)
            return;

        if (targetDoor == null)
        {
            insideTargetRoom = targetRoom != null && targetRoom.ContainsWorldPoint(transform.position);
            EnterState(insideTargetRoom ? EnemyState.ChasePlayer : EnemyState.SearchOutside);
            return;
        }

        if (MoveTowards(targetDoor.InsideEntryPosition, stoppingDistance * 0.5f))
        {
            insideTargetRoom = true;
            EnterState(EnemyState.ChasePlayer);
        }
    }

    private void TickChasePlayer()
    {
        if (GetPlayerRoot() == null)
        {
            EnterState(EnemyState.LostPlayer);
            return;
        }

        Vector3 playerPosition = GetPlayerPosition();
        float distance = Vector2.Distance(transform.position, playerPosition);
        if (distance <= attackRange)
        {
            EnterState(EnemyState.AttackPlayer);
            return;
        }

        MoveTowards(playerPosition, stoppingDistance);

        if (!insideTargetRoom && targetRoom != null && targetRoom.ContainsWorldPoint(transform.position))
            insideTargetRoom = true;
    }

    private void TickAttackPlayer()
    {
        if (GetPlayerRoot() == null)
        {
            EnterState(EnemyState.LostPlayer);
            return;
        }

        Vector3 playerPosition = GetPlayerPosition();
        FaceTowards(playerPosition);
        float distance = Vector2.Distance(transform.position, playerPosition);
        if (distance > attackRange * 1.25f)
        {
            EnterState(EnemyState.ChasePlayer);
            return;
        }

        if (attackCooldownTimer > 0f)
            return;

        if (attackWindupTimer <= 0f)
            attackWindupTimer = attackWindupTime;

        attackWindupTimer -= Time.deltaTime;
        if (attackWindupTimer > 0f)
            return;

        ApplyAttackDamage();
        attackCooldownTimer = attackCooldown;
        attackWindupTimer = 0f;
    }

    private void TickLostPlayer()
    {
        lostSightTimer += Time.deltaTime;
        if (lostSightTimer < lostSightDelay)
            return;

        if (chaseLastKnownRoom && targetRoom != null && roomMemoryTimer > 0f)
            EnterState(EnemyState.SearchLastKnownRoom);
        else
            EnterState(EnemyState.SearchOutside);
    }

    private void TickSearchLastKnownRoom()
    {
        roomMemoryTimer -= Time.deltaTime;
        investigateTimer += Time.deltaTime;

        if (targetDoor != null)
            MoveTowards(targetDoor.OutsideApproachPosition, doorApproachDistance);
        else if (targetRoom != null && targetRoom.roomAreaCollider != null)
            MoveTowards(targetRoom.roomAreaCollider.bounds.center, stoppingDistance);

        if (TryEvaluateVision(out EnemyVision.VisionResult visionResult) && visionResult.isVisible)
        {
            lastVisionResult = visionResult;
            EnterState(EnemyState.DetectPlayer);
            return;
        }

        if (investigateTimer >= investigateDuration || roomMemoryTimer <= 0f)
            EnterState(EnemyState.SearchOutside);
    }

    private bool TryEvaluateVision(out EnemyVision.VisionResult result)
    {
        result = default;
        if (GetPlayerRoot() == null || vision == null)
            return false;

        result = vision.EvaluateVisibility(
            transform.position,
            facingDirection,
            GetPlayerPosition(),
            GetPlayerRoot(),
            viewDistance,
            viewAngle,
            playerLayer,
            wallLayer,
            windowLayer,
            requireOpenWindow,
            windowVisionSampleCount,
            windowVisionSamplePadding,
            windowCache);

        if (drawWindowVisionSamples && result.usedWindowPortal)
        {
            debugSamplePoints.Clear();
            debugSamplePoints.Add(result.samplePoint);
        }

        return true;
    }

    private bool MoveTowards(Vector3 targetPosition, float stopDistance)
    {
        Vector3 current = transform.position;
        Vector2 delta = (Vector2)(targetPosition - current);
        float distance = delta.magnitude;
        if (distance <= stopDistance)
            return true;

        Vector2 direction = delta / Mathf.Max(distance, 0.0001f);
        float speed = GetEffectiveMoveSpeed();
        Vector2 next = (Vector2)current + direction * (speed * Time.deltaTime);
        if (vision != null && !vision.HasClearWallLine(current, next, wallLayer, null))
            return false;

        transform.position = new Vector3(next.x, next.y, current.z);
        facingDirection = direction;
        return Vector2.Distance(transform.position, targetPosition) <= stopDistance;
    }

    private float GetEffectiveMoveSpeed()
    {
        if (currentState == EnemyState.MoveToExteriorDoor)
            return moveSpeed * doorTargetingSpeedMultiplier;

        if (currentState == EnemyState.SearchLastKnownRoom && targetDoor != null)
            return moveSpeed * doorTargetingSpeedMultiplier;

        return moveSpeed;
    }

    private void FaceTowards(Vector3 worldPosition)
    {
        Vector2 delta = (Vector2)(worldPosition - transform.position);
        if (delta.sqrMagnitude <= 0.0001f)
            return;

        float targetAngle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f;
        float newAngle = Mathf.MoveTowardsAngle(transform.eulerAngles.z, targetAngle, rotationSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Euler(0f, 0f, newAngle);
        facingDirection = delta.normalized;
    }

    private void ApplyAttackDamage()
    {
        if (playerDamageable != null && playerDamageable.IsAlive)
        {
            playerDamageable.TakeDamage(attackDamage);
            return;
        }

        if (playerSanity != null)
            playerSanity.DrainSanity(attackDamage);
    }

    private void EnterState(EnemyState newState)
    {
        if (currentState == newState)
            return;

        if (currentState == EnemyState.BreakingDoor && targetDoor != null)
        {
            doorAttackSource?.CancelAttack();
            targetDoor.StopBreaking();
        }

        if (logStateChanges)
            Debug.Log("[EnemyController] " + name + ": " + currentState + " -> " + newState, this);

        currentState = newState;

        footprintTrace?.SyncFromEnemyState(currentState);

        switch (newState)
        {
            case EnemyState.DetectPlayer:
                detectionTimer = 0f;
                break;
            case EnemyState.BreakingDoor:
                EnsureDoorAttackSource();
                if (targetDoor != null)
                    doorAttackSource?.BeginAttack(targetDoor);
                break;
            case EnemyState.EnterRoom:
                enterRoomTimer = 0f;
                break;
            case EnemyState.LostPlayer:
                lostSightTimer = 0f;
                break;
            case EnemyState.SearchLastKnownRoom:
                investigateTimer = 0f;
                break;
            case EnemyState.SearchOutside:
                searchTimer = 0f;
                break;
        }
    }

    private void UpdateTimers()
    {
        if (attackCooldownTimer > 0f)
            attackCooldownTimer -= Time.deltaTime;
        if (roomMemoryTimer > 0f && currentState != EnemyState.SearchLastKnownRoom)
            roomMemoryTimer -= Time.deltaTime;
    }

    void OnDestroy()
    {
        UnbindDoorAttackSource();
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
        doorAttackSource.TargetDestroyed -= HandleDoorDestroyed;
        doorAttackSource.TargetDestroyed += HandleDoorDestroyed;
    }

    private void UnbindDoorAttackSource()
    {
        if (doorAttackSource != null)
            doorAttackSource.TargetDestroyed -= HandleDoorDestroyed;
    }

    private void HandleDoorDestroyed(CombatAttackSource source, IDamageReceiver receiver)
    {
        if (targetDoor == null || receiver == null || receiver.ReceiverObject != targetDoor.gameObject)
            return;
        EnterState(EnemyState.EnterRoom);
    }

    private void UpdateFacingFromVelocity()
    {
        if (facingDirection.sqrMagnitude > 0.0001f)
            return;

        facingDirection = Vector2.up;
    }

    private void ResolvePlayerReferences()
    {
        if (playerControl == null)
            playerControl = PlayerControl.Active;

        if (playerTarget == null && playerControl != null)
            playerTarget = playerControl.transform;

        if (playerSanity == null)
            playerSanity = FindFirstObjectByType<SanitySystem>();

        if (playerDamageable == null && playerControl != null)
            playerDamageable = playerControl.GetComponent<PlayerSanityDamageable>();
    }

    private Vector3 GetPlayerPosition()
    {
        if (playerControl != null)
            return playerControl.PlayerWorldPosition;

        if (playerTarget != null)
            return playerTarget.position;

        return PlayerControl.TryGetPlayerWorldPosition(out Vector3 position) ? position : Vector3.zero;
    }

    private Transform GetPlayerRoot()
    {
        if (playerControl != null)
            return playerControl.transform;

        return playerTarget;
    }

    private void RefreshWindowCache()
    {
        windowCache.Clear();
        WindowPortal[] windows = FindObjectsByType<WindowPortal>(FindObjectsSortMode.None);
        for (int i = 0; i < windows.Length; i++)
        {
            if (windows[i] != null)
                windowCache.Add(windows[i]);
        }
    }

    public bool TryFindOutsideSpawnNearPlayer(out Vector3 spawnPosition)
    {
        spawnPosition = transform.position;
        if (!PlayerControl.TryGetPlayerWorldPosition(out Vector3 playerPosition))
            return false;

        for (int attempt = 0; attempt < 16; attempt++)
        {
            float angle = attempt * 45f * Mathf.Deg2Rad;
            float radius = Mathf.Lerp(spawnNearPlayerMinDistance, spawnNearPlayerMaxDistance, (attempt % 4) / 3f);
            Vector3 candidate = playerPosition + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
            if (RoomManager.IsValidEnemySpawnPosition(
                    candidate,
                    spawnNearPlayerMinDistance,
                    spawnNearPlayerMaxDistance))
            {
                spawnPosition = candidate;
                return true;
            }
        }

        return false;
    }

    void OnDrawGizmosSelected()
    {
        if (!drawVisionCone)
            return;

        Vector3 origin = transform.position;
        Vector2 forward = Application.isPlaying ? facingDirection : (Vector2)transform.up;
        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector2.up;

        float halfAngle = viewAngle * 0.5f;
        Vector3 left = Quaternion.Euler(0f, 0f, halfAngle) * forward;
        Vector3 right = Quaternion.Euler(0f, 0f, -halfAngle) * forward;

        Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.35f);
        Gizmos.DrawLine(origin, origin + left.normalized * viewDistance);
        Gizmos.DrawLine(origin, origin + right.normalized * viewDistance);

        if (drawLineOfSight && GetPlayerRoot() != null)
        {
            Gizmos.color = lastVisionResult.isVisible ? Color.green : Color.red;
            Gizmos.DrawLine(origin, GetPlayerPosition());
        }

        if (drawWindowVisionSamples)
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < debugSamplePoints.Count; i++)
                Gizmos.DrawSphere(debugSamplePoints[i], 0.06f);
        }

        if (targetDoor != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(targetDoor.OutsideApproachPosition, 0.08f);
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(targetDoor.InsideEntryPosition, 0.08f);
        }
    }
}
