using UnityEngine;

namespace DuoCurtain.Vision
{
    [DisallowMultipleComponent]
    public sealed class VisionRenderController : MonoBehaviour
    {
        public enum RendererBackend
        {
            ProceduralMesh
        }

        [Header("References")]
        public VisionSensor2D sensor;
        public ProceduralMeshVisionRenderer proceduralMeshRenderer;

        [Header("Backend")]
        public RendererBackend rendererBackend = RendererBackend.ProceduralMesh;
        public VisionRenderParameters renderParameters = new VisionRenderParameters();
        public int sortingLayerId;
        public int sortingOrder = 52;
        public float zOffset = -0.2f;
        public bool renderEnabled = true;

        private IVisionRenderer activeRenderer;

        void Awake()
        {
            ResolveReferences();
            InitializeRenderer();
        }

        void OnEnable()
        {
            ResolveReferences();
            InitializeRenderer();
            if (sensor != null)
                sensor.SnapshotUpdated += HandleSnapshotUpdated;
        }

        void OnDisable()
        {
            if (sensor != null)
                sensor.SnapshotUpdated -= HandleSnapshotUpdated;
            activeRenderer?.Hide();
        }

        public void SetVisible(bool value)
        {
            renderEnabled = value;
            if (!value)
                activeRenderer?.Hide();
            else if (sensor != null)
                Render(sensor.LatestSnapshot);
        }

        public void Render(VisionSnapshot snapshot)
        {
            if (!renderEnabled || snapshot == null)
            {
                activeRenderer?.Hide();
                return;
            }

            if (activeRenderer == null)
                InitializeRenderer();
            activeRenderer?.Render(snapshot, renderParameters);
        }

        private void HandleSnapshotUpdated(VisionSnapshot snapshot)
        {
            Render(snapshot);
        }

        private void ResolveReferences()
        {
            if (sensor == null)
                sensor = GetComponent<VisionSensor2D>();
            if (sensor == null)
                sensor = gameObject.AddComponent<VisionSensor2D>();

            if (proceduralMeshRenderer == null)
                proceduralMeshRenderer = GetComponent<ProceduralMeshVisionRenderer>();
            if (proceduralMeshRenderer == null)
                proceduralMeshRenderer = gameObject.AddComponent<ProceduralMeshVisionRenderer>();
        }

        private void InitializeRenderer()
        {
            ResolveReferences();
            switch (rendererBackend)
            {
                default:
                    activeRenderer = proceduralMeshRenderer;
                    break;
            }

            activeRenderer?.Initialize(new VisionRendererContext(
                gameObject,
                transform,
                sortingLayerId,
                sortingOrder,
                zOffset));
        }
    }
}
