using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    [ExecuteAlways]
    [RequireComponent(typeof(RuntimeTileMeshView))]
    [DisallowMultipleComponent]
    public sealed class RuntimeTileMeshProjectionRenderer : MonoBehaviour
    {
        private static readonly int ProjectionModeId = Shader.PropertyToID("_ProjectionMode");
        private static readonly int CellSizeId = Shader.PropertyToID("_PatternCellSize");
        private static readonly int MotionTileSizeId = Shader.PropertyToID("_MotionTileSize");
        private static readonly int PatternOffsetId = Shader.PropertyToID("_PatternOffset");
        private static readonly int PatternScaleId = Shader.PropertyToID("_PatternScale");
        private static readonly int PatternAnchorId = Shader.PropertyToID("_PatternAnchor");
        private static readonly int PatternTimeId = Shader.PropertyToID("_PatternTime");
        private static readonly int TransitionId = Shader.PropertyToID("_PatternTransition");
        private static readonly int PatternIntensityId = Shader.PropertyToID("_PatternIntensity");
        private static readonly int PatternLineWidthId = Shader.PropertyToID("_PatternLineWidth");

        [Header("State")]
        public RuntimeTileMeshVisualState visualState = new RuntimeTileMeshVisualState();
        public bool captureAnchorOnEnable = true;
        public bool animateInEditMode;
        public bool animateInPlayMode = true;
        public bool useUnscaledTime;

        private readonly List<Renderer> renderers = new List<Renderer>();
        private RuntimeTileMeshView view;
        private MaterialPropertyBlock propertyBlock;

        void Reset()
        {
            ResolveView();
            if (view != null)
            {
                visualState.material = view.material;
                visualState.cellSize = view.tileSize;
            }

            CaptureAnchor();
        }

        void OnEnable()
        {
            ResolveView();
            if (captureAnchorOnEnable)
                CaptureAnchor();

            if (view != null)
                view.Rebuilt += OnViewRebuilt;

            Apply();
        }

        void OnDisable()
        {
            if (view != null)
                view.Rebuilt -= OnViewRebuilt;
        }

        void OnValidate()
        {
            ResolveView();
            visualState ??= new RuntimeTileMeshVisualState();
            visualState.Sanitize();
            Apply();
        }

        void Update()
        {
            if (!ShouldAnimate())
                return;

            Apply();
        }

        public void CaptureAnchor()
        {
            visualState ??= new RuntimeTileMeshVisualState();
            visualState.anchorWorldPosition = transform.position;
        }

        public void Apply()
        {
            ResolveView();
            if (view == null)
                return;

            visualState ??= new RuntimeTileMeshVisualState();
            visualState.Sanitize();

            view.CollectGeneratedRenderers(renderers);
            if (renderers.Count == 0)
                GetComponentsInChildren(true, renderers);

            propertyBlock ??= new MaterialPropertyBlock();
            float time = GetProjectionTime();

            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                if (visualState.material != null && renderer.sharedMaterial != visualState.material)
                    renderer.sharedMaterial = visualState.material;

                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetFloat(ProjectionModeId, (float)visualState.projectionMode);
                propertyBlock.SetVector(CellSizeId, new Vector4(visualState.cellSize.x, visualState.cellSize.y, 0f, 0f));
                propertyBlock.SetVector(MotionTileSizeId, new Vector4(visualState.motionTileSize.x, visualState.motionTileSize.y, 0f, 0f));
                propertyBlock.SetVector(PatternOffsetId, new Vector4(visualState.patternOffset.x, visualState.patternOffset.y, 0f, 0f));
                propertyBlock.SetFloat(PatternScaleId, visualState.patternScale);
                propertyBlock.SetVector(PatternAnchorId, new Vector4(visualState.anchorWorldPosition.x, visualState.anchorWorldPosition.y, 0f, 0f));
                propertyBlock.SetFloat(PatternTimeId, time);
                propertyBlock.SetFloat(TransitionId, visualState.transition);
                propertyBlock.SetFloat(PatternIntensityId, visualState.patternIntensity);
                propertyBlock.SetFloat(PatternLineWidthId, visualState.lineWidth);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        private void OnViewRebuilt(RuntimeTileMeshView rebuiltView)
        {
            Apply();
        }

        private bool ShouldAnimate()
        {
            return Application.isPlaying ? animateInPlayMode : animateInEditMode;
        }

        private float GetProjectionTime()
        {
            float time = 0f;
            if (Application.isPlaying)
                time = useUnscaledTime ? Time.unscaledTime : Time.time;
#if UNITY_EDITOR
            else
                time = (float)UnityEditor.EditorApplication.timeSinceStartup;
#endif
            return time + visualState.timeOffset;
        }

        private void ResolveView()
        {
            if (view == null)
                view = GetComponent<RuntimeTileMeshView>();
        }
    }
}
