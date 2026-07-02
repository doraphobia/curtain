using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class FusionBackgroundShaderController : MonoBehaviour
{
    private const string ShaderName = "Duo Curtain/Fusion Background Grid";
    private const string BackgroundObjectName = "Fusion Background Plane";
    private const string ResourcesBackgroundMaterialPath = "Materials/Fusion Background Grid";

    private static readonly int TopColorId = Shader.PropertyToID("_TopColor");
    private static readonly int BottomColorId = Shader.PropertyToID("_BottomColor");
    private static readonly int GridColorId = Shader.PropertyToID("_GridColor");
    private static readonly int GridCellSizeId = Shader.PropertyToID("_GridCellSize");
    private static readonly int GridLineWidthId = Shader.PropertyToID("_GridLineWidth");
    private static readonly int GridOpacityId = Shader.PropertyToID("_GridOpacity");
    private static readonly int VignetteStrengthId = Shader.PropertyToID("_VignetteStrength");
    private static readonly int PulseStrengthId = Shader.PropertyToID("_PulseStrength");
    private static readonly int TimeOffsetId = Shader.PropertyToID("_TimeOffset");
    private static readonly int DriftSpeedId = Shader.PropertyToID("_DriftSpeed");

    [Serializable]
    public class StageBackgroundStyle
    {
        public string stageId = StageIds.DayTop;
        public Color topColor = new Color(0.68f, 0.69f, 0.7f, 1f);
        public Color bottomColor = new Color(0.46f, 0.47f, 0.48f, 1f);
        public Color gridColor = new Color(1f, 1f, 1f, 0.2f);
        [Range(0f, 1f)]
        public float gridOpacity = 0.22f;
        [Range(0f, 1f)]
        public float pulseStrength = 0.035f;
    }

    [Header("Target")]
    public Camera targetCamera;
    public StageCycleController stageController;
    public Material backgroundMaterial;

    [Header("Plane")]
    [Min(1f)]
    public float planeDistance = 80f;
    [Min(1f)]
    public float sizePadding = 1.08f;
    public int sortingOrder = -32000;

    [Header("Grid")]
    public Vector2 gridCellSize = new Vector2(1f, 5f);
    [Min(0.001f)]
    public float gridLineWidth = 0.012f;
    [Range(0f, 1f)]
    public float vignetteStrength = 0.16f;
    public Vector2 driftSpeed = new Vector2(0.015f, -0.006f);

    [Header("Stages")]
    public List<StageBackgroundStyle> stageStyles = new List<StageBackgroundStyle>
    {
        new StageBackgroundStyle
        {
            stageId = StageIds.DayTop,
            topColor = new Color(0.68f, 0.69f, 0.7f, 1f),
            bottomColor = new Color(0.49f, 0.5f, 0.51f, 1f),
            gridColor = new Color(1f, 1f, 1f, 0.2f),
            gridOpacity = 0.23f,
            pulseStrength = 0.03f
        },
        new StageBackgroundStyle
        {
            stageId = StageIds.DayBottom,
            topColor = new Color(0.54f, 0.54f, 0.55f, 1f),
            bottomColor = new Color(0.36f, 0.37f, 0.38f, 1f),
            gridColor = new Color(0.95f, 0.95f, 0.95f, 0.18f),
            gridOpacity = 0.2f,
            pulseStrength = 0.025f
        },
        new StageBackgroundStyle
        {
            stageId = StageIds.BeforeNight,
            topColor = new Color(0.13f, 0.13f, 0.16f, 1f),
            bottomColor = new Color(0.06f, 0.06f, 0.08f, 1f),
            gridColor = new Color(0.48f, 0.5f, 0.58f, 0.22f),
            gridOpacity = 0.16f,
            pulseStrength = 0.02f
        },
        new StageBackgroundStyle
        {
            stageId = StageIds.Night,
            topColor = new Color(0.025f, 0.027f, 0.04f, 1f),
            bottomColor = new Color(0.005f, 0.006f, 0.012f, 1f),
            gridColor = new Color(0.16f, 0.2f, 0.32f, 0.18f),
            gridOpacity = 0.12f,
            pulseStrength = 0.012f
        }
    };

    private GameObject backgroundObject;
    private MeshRenderer backgroundRenderer;
    private MeshFilter backgroundFilter;
    private Mesh runtimeMesh;
    private Material runtimeMaterial;
    private MaterialPropertyBlock propertyBlock;

    void OnEnable()
    {
        ResolveReferences();
        EnsureBackgroundPlane();
        ApplyBackgroundState();
    }

    void OnDisable()
    {
        DestroyRuntimeObjects();
    }

    void OnDestroy()
    {
        DestroyRuntimeObjects();
    }

    void OnValidate()
    {
        planeDistance = Mathf.Max(1f, planeDistance);
        sizePadding = Mathf.Max(1f, sizePadding);
        gridLineWidth = Mathf.Max(0.001f, gridLineWidth);

        if (backgroundRenderer != null)
            ApplyBackgroundState();
    }

    void LateUpdate()
    {
        ResolveReferences();
        EnsureBackgroundPlane();
        ApplyBackgroundState();
    }

    private void ResolveReferences()
    {
        if (targetCamera == null)
            targetCamera = GetComponent<Camera>();

        if (stageController == null)
            stageController = FindFirstObjectByType<StageCycleController>();
    }

    private void EnsureBackgroundPlane()
    {
        if (targetCamera == null)
            return;

        if (backgroundObject == null)
        {
            Transform existing = targetCamera.transform.Find(BackgroundObjectName);
            backgroundObject = existing != null ? existing.gameObject : new GameObject(BackgroundObjectName);
            backgroundObject.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            backgroundObject.transform.SetParent(targetCamera.transform, false);
        }

        if (backgroundFilter == null)
            backgroundFilter = backgroundObject.GetComponent<MeshFilter>() ?? backgroundObject.AddComponent<MeshFilter>();

        if (backgroundFilter == null)
            return;

        if (backgroundRenderer == null)
            backgroundRenderer = backgroundObject.GetComponent<MeshRenderer>() ?? backgroundObject.AddComponent<MeshRenderer>();

        if (backgroundRenderer == null)
            return;

        if (runtimeMesh == null)
            runtimeMesh = CreateQuadMesh();

        backgroundFilter.sharedMesh = runtimeMesh;
        backgroundRenderer.sharedMaterial = ResolveMaterial();
        backgroundRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        backgroundRenderer.receiveShadows = false;
        backgroundRenderer.sortingOrder = sortingOrder;

        float height = targetCamera.orthographic
            ? targetCamera.orthographicSize * 2f
            : 2f * Mathf.Tan(targetCamera.fieldOfView * Mathf.Deg2Rad * 0.5f) * planeDistance;
        float width = height * Mathf.Max(0.01f, targetCamera.aspect);

        Transform plane = backgroundObject.transform;
        plane.localPosition = new Vector3(0f, 0f, planeDistance);
        plane.localRotation = Quaternion.identity;
        plane.localScale = new Vector3(width * sizePadding, height * sizePadding, 1f);
    }

    private Material ResolveMaterial()
    {
        if (backgroundMaterial == null)
            backgroundMaterial = Resources.Load<Material>(ResourcesBackgroundMaterialPath);

        if (backgroundMaterial != null)
            return backgroundMaterial;

        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        if (runtimeMaterial == null || runtimeMaterial.shader != shader)
        {
            if (runtimeMaterial != null)
                DestroyImmediateSafe(runtimeMaterial);

            runtimeMaterial = new Material(shader);
            runtimeMaterial.name = "Fusion Background Runtime";
            runtimeMaterial.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        }

        return runtimeMaterial;
    }

    private void ApplyBackgroundState()
    {
        if (backgroundRenderer == null)
            return;

        StageBackgroundStyle current = ResolveStageStyle(stageController != null ? stageController.CurrentStageId : StageIds.DayTop);
        StageBackgroundStyle next = ResolveStageStyle(stageController != null ? stageController.NextStageId : StageIds.DayBottom);
        float blend = stageController != null && stageController.IsTransitioning
            ? stageController.TransitionProgress
            : 0f;

        Color topColor = Color.Lerp(current.topColor, next.topColor, blend);
        Color bottomColor = Color.Lerp(current.bottomColor, next.bottomColor, blend);
        Color gridColor = Color.Lerp(current.gridColor, next.gridColor, blend);
        float gridOpacity = Mathf.Lerp(current.gridOpacity, next.gridOpacity, blend);
        float pulseStrength = Mathf.Lerp(current.pulseStrength, next.pulseStrength, blend);

        propertyBlock ??= new MaterialPropertyBlock();
        backgroundRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(TopColorId, topColor);
        propertyBlock.SetColor(BottomColorId, bottomColor);
        propertyBlock.SetColor(GridColorId, gridColor);
        propertyBlock.SetVector(GridCellSizeId, new Vector4(gridCellSize.x, gridCellSize.y, 0f, 0f));
        propertyBlock.SetFloat(GridLineWidthId, gridLineWidth);
        propertyBlock.SetFloat(GridOpacityId, gridOpacity);
        propertyBlock.SetFloat(VignetteStrengthId, vignetteStrength);
        propertyBlock.SetFloat(PulseStrengthId, pulseStrength);
        propertyBlock.SetFloat(TimeOffsetId, GetPreviewTime());
        propertyBlock.SetVector(DriftSpeedId, new Vector4(driftSpeed.x, driftSpeed.y, 0f, 0f));
        backgroundRenderer.SetPropertyBlock(propertyBlock);
    }

    private StageBackgroundStyle ResolveStageStyle(string stageId)
    {
        if (stageStyles != null)
        {
            for (int i = 0; i < stageStyles.Count; i++)
            {
                StageBackgroundStyle style = stageStyles[i];
                if (style != null && StageIds.Matches(style.stageId, stageId))
                    return style;
            }
        }

        return new StageBackgroundStyle { stageId = stageId ?? StageIds.DayTop };
    }

    private static Mesh CreateQuadMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "Fusion Background Quad";
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateBounds();
        return mesh;
    }

    private static float GetPreviewTime()
    {
        if (Application.isPlaying)
            return Time.time;

#if UNITY_EDITOR
        return (float)UnityEditor.EditorApplication.timeSinceStartup;
#else
        return Time.realtimeSinceStartup;
#endif
    }

    private void DestroyRuntimeObjects()
    {
        if (backgroundObject != null)
        {
            DestroyImmediateSafe(backgroundObject);
            backgroundObject = null;
            backgroundRenderer = null;
            backgroundFilter = null;
        }

        if (runtimeMesh != null)
        {
            DestroyImmediateSafe(runtimeMesh);
            runtimeMesh = null;
        }

        if (runtimeMaterial != null)
        {
            DestroyImmediateSafe(runtimeMaterial);
            runtimeMaterial = null;
        }
    }

    private static void DestroyImmediateSafe(UnityEngine.Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }
}
