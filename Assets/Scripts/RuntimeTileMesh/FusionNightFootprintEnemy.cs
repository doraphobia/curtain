using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    [DisallowMultipleComponent]
    public sealed class FusionNightFootprintEnemy : MonoBehaviour
    {
        public enum TraceEnemyState
        {
            WanderOutside,
            WatchingWindow,
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
        public bool chasePlayerAfterWindowDetection = true;

        [Header("Debug")]
        public bool drawDebug;

        [SerializeField]
        private TraceEnemyState currentState = TraceEnemyState.WanderOutside;

        private Vector3 waypoint;
        private float waypointTimer;
        private float windowCheckTimer;

        public TraceEnemyState CurrentState => currentState;

        void Awake()
        {
            ResolveReferences();
            if (footprintTrace == null)
                footprintTrace = GetComponent<EnemyFootprintTrace>();
            if (footprintTrace == null)
                footprintTrace = gameObject.AddComponent<EnemyFootprintTrace>();

            footprintTrace.SetTraceState(EnemyTraceState.NormalMoving);
            PickOutsideWaypoint();
        }

        void Update()
        {
            if (PauseManager.IsGamePaused)
                return;

            ResolveReferences();
            TickWindowDetection();
            TickMovement();
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
        }

        private void TickWindowDetection()
        {
            windowCheckTimer -= Time.deltaTime;
            if (windowCheckTimer > 0f)
                return;

            windowCheckTimer = Mathf.Max(0.01f, windowCheckInterval);
            bool detected = CanDetectPlayerThroughOpenWindow();
            if (detected && chasePlayerAfterWindowDetection)
            {
                SetState(TraceEnemyState.ChasingPlayer);
                return;
            }

            if (detected)
            {
                SetState(TraceEnemyState.WatchingWindow);
                return;
            }

            if (currentState != TraceEnemyState.WanderOutside)
                SetState(TraceEnemyState.WanderOutside);
        }

        private void TickMovement()
        {
            Vector3 target = waypoint;
            if (currentState == TraceEnemyState.ChasingPlayer && playerControl != null && playerControl.HasPlayerWorldPosition)
            {
                target = playerControl.PlayerWorldPosition;
            }
            else
            {
                waypointTimer -= Time.deltaTime;
                if (waypointTimer <= 0f || Vector2.Distance(transform.position, waypoint) <= waypointStopDistance)
                    PickOutsideWaypoint();
            }

            MoveTowards(target);
        }

        private bool CanDetectPlayerThroughOpenWindow()
        {
            if (playerControl == null || !playerControl.HasPlayerWorldPosition)
                return false;

            Vector2 playerPosition = playerControl.PlayerWorldPosition;
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
        }

        private void MoveTowards(Vector3 target)
        {
            Vector2 delta = (Vector2)(target - transform.position);
            float distance = delta.magnitude;
            if (distance <= waypointStopDistance)
                return;

            Vector2 step = delta.normalized * (moveSpeed * Time.deltaTime);
            if (step.magnitude > distance)
                step = delta;

            transform.position += new Vector3(step.x, step.y, 0f);
        }

        private void SetState(TraceEnemyState state)
        {
            if (currentState == state)
                return;

            currentState = state;
            if (footprintTrace == null)
                return;

            switch (currentState)
            {
                case TraceEnemyState.WatchingWindow:
                    footprintTrace.SetTraceState(EnemyTraceState.Watching);
                    break;
                case TraceEnemyState.ChasingPlayer:
                    footprintTrace.SetTraceState(EnemyTraceState.ChasingPlayer);
                    break;
                default:
                    footprintTrace.SetTraceState(EnemyTraceState.NormalMoving);
                    break;
            }
        }

        private void ResolveReferences()
        {
            if (fusionSandbox == null)
                fusionSandbox = FindFirstObjectByType<RuntimeTileMeshFusionSandbox>();
            if (playerControl == null)
                playerControl = PlayerControl.Active != null ? PlayerControl.Active : FindFirstObjectByType<PlayerControl>();
        }

        void OnDrawGizmosSelected()
        {
            if (!drawDebug)
                return;

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, windowDetectionDistance);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, waypoint);
        }
    }
}
