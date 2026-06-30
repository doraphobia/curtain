using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public sealed class FusionModeCameraRig : MonoBehaviour
    {
        public enum RigMode
        {
            PlayerFollow,
            ManagementOverview
        }

        [Header("Mode")]
        public RigMode mode = RigMode.PlayerFollow;
        public PlayerControl playerControl;
        public RuntimeTileMeshFusionSandbox fusionSandbox;

        [Header("Shared Camera")]
        public bool forceOrthographic = true;
        [Min(0.01f)]
        public float orthographicSize = 5f;
        public float cameraZ = -10f;
        public bool useUnscaledTime = false;

        [Header("Player Follow")]
        [Min(0f)]
        public float followSmoothTime = 0.28f;
        [Min(0.01f)]
        public float maxFollowSpeed = 24f;
        [Min(0f)]
        public float deadZoneRadius = 0.35f;
        [Min(0f)]
        public float lookAheadDistance = 0.75f;
        [Min(0f)]
        public float lookAheadSmoothTime = 0.18f;
        public bool clampToMapBounds = false;
        public Vector2 boundsPadding = new Vector2(1.5f, 1.5f);

        [Header("Management Overview")]
        [Min(0f)]
        public float overviewSmoothTime = 0.35f;
        [Min(0f)]
        public float overviewPadding = 1.5f;
        [Min(0.01f)]
        public float minOverviewOrthographicSize = 4f;
        [Min(0.01f)]
        public float maxOverviewOrthographicSize = 32f;

        [Header("Mode Transition")]
        [Min(0f)]
        public float defaultTransitionDuration = 0.65f;
        public AnimationCurve transitionCurve;

        private Camera cachedCamera;
        private Vector3 followVelocity;
        private float zoomVelocity;
        private Vector3 previousPlayerPosition;
        private Vector3 lookAheadOffset;
        private Vector3 lookAheadVelocity;
        private bool hasPreviousPlayerPosition;

        private bool blending;
        private float blendDuration;
        private float blendElapsed;
        private Vector3 blendStartPosition;
        private Quaternion blendStartRotation;
        private float blendStartOrthographicSize;

        public Camera Camera
        {
            get
            {
                if (cachedCamera == null)
                    cachedCamera = GetComponent<Camera>();

                return cachedCamera;
            }
        }

        void Reset()
        {
            cachedCamera = GetComponent<Camera>();
            EnsureTransitionCurve();
            ApplyCameraDefaults();
        }

        void Awake()
        {
            EnsureTransitionCurve();
            ResolveReferences();
            ApplyCameraDefaults();
        }

        void OnEnable()
        {
            EnsureTransitionCurve();
            ResolveReferences();
            ApplyCameraDefaults();
        }

        void LateUpdate()
        {
            ResolveReferences();
            Tick(GetDeltaTime());
        }

        public void BeginBlendFrom(Camera sourceCamera, float duration)
        {
            if (sourceCamera == null)
            {
                SnapToDesiredPose();
                return;
            }

            BeginBlendFrom(
                sourceCamera.transform.position,
                sourceCamera.transform.rotation,
                sourceCamera.orthographicSize,
                duration);
        }

        public void BeginBlendFrom(
            Vector3 sourcePosition,
            Quaternion sourceRotation,
            float sourceOrthographicSize,
            float duration)
        {
            Camera camera = Camera;
            camera.transform.position = sourcePosition;
            camera.transform.rotation = sourceRotation;
            camera.orthographicSize = Mathf.Max(0.01f, sourceOrthographicSize);

            blendStartPosition = sourcePosition;
            blendStartRotation = sourceRotation;
            blendStartOrthographicSize = camera.orthographicSize;
            blendDuration = Mathf.Max(0f, duration);
            blendElapsed = 0f;
            blending = blendDuration > 0.0001f;
            followVelocity = Vector3.zero;
            zoomVelocity = 0f;

            if (!blending)
                SnapToDesiredPose();
        }

        public void SnapToDesiredPose()
        {
            if (!TryGetDesiredPose(out Vector3 desiredPosition, out float desiredOrthographicSize))
                return;

            Camera camera = Camera;
            camera.transform.position = desiredPosition;
            camera.orthographicSize = desiredOrthographicSize;
            orthographicSize = desiredOrthographicSize;
            followVelocity = Vector3.zero;
            zoomVelocity = 0f;
            blending = false;
        }

        public bool TryGetDesiredPose(out Vector3 desiredPosition, out float desiredOrthographicSize)
        {
            desiredOrthographicSize = Mathf.Max(0.01f, orthographicSize);
            desiredPosition = transform.position;
            desiredPosition.z = cameraZ;

            if (mode == RigMode.ManagementOverview)
                return TryGetManagementPose(out desiredPosition, out desiredOrthographicSize);

            return TryGetPlayerPose(out desiredPosition, out desiredOrthographicSize);
        }

        private void Tick(float deltaTime)
        {
            Camera camera = Camera;
            if (forceOrthographic)
                camera.orthographic = true;

            if (!TryGetDesiredPose(out Vector3 desiredPosition, out float desiredOrthographicSize))
                return;

            if (blending)
            {
                blendElapsed += deltaTime;
                float t = blendDuration <= 0.0001f ? 1f : Mathf.Clamp01(blendElapsed / blendDuration);
                float eased = transitionCurve != null ? transitionCurve.Evaluate(t) : Mathf.SmoothStep(0f, 1f, t);
                camera.transform.position = Vector3.LerpUnclamped(blendStartPosition, desiredPosition, eased);
                camera.transform.rotation = Quaternion.SlerpUnclamped(blendStartRotation, Quaternion.identity, eased);
                camera.orthographicSize = Mathf.LerpUnclamped(blendStartOrthographicSize, desiredOrthographicSize, eased);

                if (t >= 1f)
                    blending = false;

                return;
            }

            float smoothTime = mode == RigMode.ManagementOverview ? overviewSmoothTime : followSmoothTime;
            camera.transform.position = Vector3.SmoothDamp(
                camera.transform.position,
                desiredPosition,
                ref followVelocity,
                Mathf.Max(0.0001f, smoothTime),
                Mathf.Max(0.01f, maxFollowSpeed),
                deltaTime);
            camera.orthographicSize = Mathf.SmoothDamp(
                camera.orthographicSize,
                desiredOrthographicSize,
                ref zoomVelocity,
                Mathf.Max(0.0001f, smoothTime),
                Mathf.Infinity,
                deltaTime);
        }

        private bool TryGetPlayerPose(out Vector3 desiredPosition, out float desiredOrthographicSize)
        {
            desiredOrthographicSize = Mathf.Max(0.01f, orthographicSize);
            desiredPosition = transform.position;
            desiredPosition.z = cameraZ;

            if (playerControl == null || !playerControl.HasPlayerWorldPosition)
                return true;

            Vector3 playerPosition = playerControl.PlayerWorldPosition;
            Vector3 lookAhead = CalculateLookAhead(playerPosition);
            Vector3 target = playerPosition + lookAhead;
            target.z = cameraZ;

            Vector2 currentPlanar = new Vector2(transform.position.x, transform.position.y);
            Vector2 targetPlanar = new Vector2(target.x, target.y);
            if (Vector2.Distance(currentPlanar, targetPlanar) <= Mathf.Max(0f, deadZoneRadius))
            {
                desiredPosition = new Vector3(transform.position.x, transform.position.y, cameraZ);
            }
            else
            {
                desiredPosition = target;
            }

            if (clampToMapBounds)
                desiredPosition = ClampCameraCenterToMap(desiredPosition, desiredOrthographicSize);

            return true;
        }

        private bool TryGetManagementPose(out Vector3 desiredPosition, out float desiredOrthographicSize)
        {
            desiredPosition = transform.position;
            desiredPosition.z = cameraZ;
            desiredOrthographicSize = Mathf.Clamp(
                Mathf.Max(0.01f, orthographicSize),
                minOverviewOrthographicSize,
                maxOverviewOrthographicSize);

            if (fusionSandbox == null || !fusionSandbox.TryGetWorldBounds(out Bounds bounds))
                return true;

            desiredPosition = bounds.center;
            desiredPosition.z = cameraZ;

            Camera camera = Camera;
            float aspect = Mathf.Max(0.0001f, camera.aspect);
            float heightSize = bounds.size.y * 0.5f + overviewPadding;
            float widthSize = bounds.size.x * 0.5f / aspect + overviewPadding;
            desiredOrthographicSize = Mathf.Clamp(
                Mathf.Max(heightSize, widthSize, minOverviewOrthographicSize),
                minOverviewOrthographicSize,
                maxOverviewOrthographicSize);
            orthographicSize = desiredOrthographicSize;
            return true;
        }

        private Vector3 CalculateLookAhead(Vector3 playerPosition)
        {
            if (!hasPreviousPlayerPosition)
            {
                previousPlayerPosition = playerPosition;
                hasPreviousPlayerPosition = true;
                return Vector3.zero;
            }

            Vector3 delta = playerPosition - previousPlayerPosition;
            previousPlayerPosition = playerPosition;

            Vector3 desiredLookAhead = Vector3.zero;
            if (delta.sqrMagnitude > 0.000001f)
                desiredLookAhead = delta.normalized * Mathf.Max(0f, lookAheadDistance);

            lookAheadOffset = Vector3.SmoothDamp(
                lookAheadOffset,
                desiredLookAhead,
                ref lookAheadVelocity,
                Mathf.Max(0.0001f, lookAheadSmoothTime),
                Mathf.Infinity,
                GetDeltaTime());
            lookAheadOffset.z = 0f;
            return lookAheadOffset;
        }

        private Vector3 ClampCameraCenterToMap(Vector3 desiredPosition, float size)
        {
            if (fusionSandbox == null || !fusionSandbox.TryGetWorldBounds(out Bounds bounds))
                return desiredPosition;

            Camera camera = Camera;
            float halfHeight = Mathf.Max(0.01f, size);
            float halfWidth = halfHeight * Mathf.Max(0.0001f, camera.aspect);
            float minX = bounds.min.x + halfWidth - boundsPadding.x;
            float maxX = bounds.max.x - halfWidth + boundsPadding.x;
            float minY = bounds.min.y + halfHeight - boundsPadding.y;
            float maxY = bounds.max.y - halfHeight + boundsPadding.y;

            if (minX > maxX)
                minX = maxX = bounds.center.x;
            if (minY > maxY)
                minY = maxY = bounds.center.y;

            desiredPosition.x = Mathf.Clamp(desiredPosition.x, minX, maxX);
            desiredPosition.y = Mathf.Clamp(desiredPosition.y, minY, maxY);
            desiredPosition.z = cameraZ;
            return desiredPosition;
        }

        private void ResolveReferences()
        {
            if (playerControl == null)
                playerControl = PlayerControl.Active != null ? PlayerControl.Active : FindFirstObjectByType<PlayerControl>();

            if (fusionSandbox == null)
                fusionSandbox = FindFirstObjectByType<RuntimeTileMeshFusionSandbox>();
        }

        private float GetDeltaTime()
        {
            return useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        }

        private void ApplyCameraDefaults()
        {
            Camera camera = Camera;
            if (camera == null)
                return;

            if (forceOrthographic)
                camera.orthographic = true;

            camera.orthographicSize = Mathf.Max(0.01f, camera.orthographicSize <= 0f ? orthographicSize : camera.orthographicSize);
            Vector3 position = camera.transform.position;
            position.z = cameraZ;
            camera.transform.position = position;
        }

        private void EnsureTransitionCurve()
        {
            if (transitionCurve == null || transitionCurve.length == 0)
                transitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        }
    }
}
