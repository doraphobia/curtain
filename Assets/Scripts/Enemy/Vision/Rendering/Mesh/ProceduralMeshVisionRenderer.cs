using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuoCurtain.Vision
{
    [DisallowMultipleComponent]
    public sealed class ProceduralMeshVisionRenderer : MonoBehaviour, IVisionRenderer
    {
        private const string OutputName = "Procedural Vision Mesh";

        [Header("Output")]
        public Material material;
        public bool createFallbackMaterial = true;
        public bool visible = true;

        private readonly List<Vector3> vertices = new List<Vector3>(258);
        private readonly List<int> triangles = new List<int>(768);
        private readonly List<Vector2> uv0 = new List<Vector2>(258);
        private readonly List<Vector4> uv1 = new List<Vector4>(258);
        private readonly List<Color32> colors = new List<Color32>(258);
        private Transform outputTransform;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh mesh;
        private Material runtimeMaterial;
        private VisionRendererContext rendererContext;
        private bool initialized;

        public void Initialize(VisionRendererContext context)
        {
            rendererContext = context;
            EnsureOutput();
            initialized = true;
        }

        public void Render(VisionSnapshot snapshot, VisionRenderParameters parameters)
        {
            if (!initialized)
                Initialize(rendererContext);
            if (snapshot == null || !snapshot.IsValid || !visible)
            {
                Hide();
                return;
            }

            EnsureOutput();
            BuildMesh(snapshot, parameters ?? new VisionRenderParameters());
            meshRenderer.enabled = true;
        }

        public void Hide()
        {
            if (meshRenderer != null)
                meshRenderer.enabled = false;
        }

        public void Dispose()
        {
            if (mesh != null)
            {
                if (Application.isPlaying)
                    Destroy(mesh);
                else
                    DestroyImmediate(mesh);
                mesh = null;
            }

            if (runtimeMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(runtimeMaterial);
                else
                    DestroyImmediate(runtimeMaterial);
                runtimeMaterial = null;
            }
        }

        void OnDestroy()
        {
            Dispose();
        }

        private void EnsureOutput()
        {
            if (outputTransform == null)
            {
                Transform existing = transform.Find(OutputName);
                if (existing != null)
                {
                    outputTransform = existing;
                }
                else
                {
                    GameObject output = new GameObject(OutputName);
                    output.transform.SetParent(transform, false);
                    outputTransform = output.transform;
                }
            }

            if (meshFilter == null)
            {
                meshFilter = outputTransform.GetComponent<MeshFilter>();
                if (meshFilter == null)
                    meshFilter = outputTransform.gameObject.AddComponent<MeshFilter>();
            }

            if (meshRenderer == null)
            {
                meshRenderer = outputTransform.GetComponent<MeshRenderer>();
                if (meshRenderer == null)
                    meshRenderer = outputTransform.gameObject.AddComponent<MeshRenderer>();
            }

            if (mesh == null)
            {
                mesh = new Mesh
                {
                    name = "Vision Visibility Polygon",
                    indexFormat = IndexFormat.UInt32
                };
                mesh.MarkDynamic();
                meshFilter.sharedMesh = mesh;
            }

            meshRenderer.sharedMaterial = material != null ? material : GetFallbackMaterial();
            meshRenderer.sortingLayerID = rendererContext.sortingLayerId;
            meshRenderer.sortingOrder = rendererContext.sortingOrder;
        }

        private void BuildMesh(VisionSnapshot snapshot, VisionRenderParameters parameters)
        {
            vertices.Clear();
            triangles.Clear();
            uv0.Clear();
            uv1.Clear();
            colors.Clear();

            Vector3 localOrigin = outputTransform.InverseTransformPoint(
                new Vector3(snapshot.origin.x, snapshot.origin.y, rendererContext.zOffset));
            vertices.Add(localOrigin);
            uv0.Add(new Vector2(0.5f, 0.5f));
            uv1.Add(new Vector4(0f, 0.5f, snapshot.origin.x, snapshot.origin.y));
            colors.Add(WithOpacity(parameters.primaryColor, parameters.opacity));

            IReadOnlyList<VisionRaySample> samples = snapshot.RaySamples;
            for (int i = 0; i < samples.Count; i++)
            {
                VisionRaySample sample = samples[i];
                Vector3 localPoint = outputTransform.InverseTransformPoint(
                    new Vector3(sample.point.x, sample.point.y, rendererContext.zOffset));
                vertices.Add(localPoint);
                uv0.Add(BuildUV(sample, snapshot, parameters.uvMode));
                uv1.Add(new Vector4(
                    sample.normalizedDistance,
                    sample.normalizedAngle,
                    sample.point.x,
                    sample.point.y));
                colors.Add(Color32.Lerp(
                    WithOpacity(parameters.primaryColor, parameters.opacity),
                    WithOpacity(parameters.secondaryColor, parameters.opacity),
                    sample.normalizedDistance));
            }

            for (int i = 1; i < vertices.Count - 1; i++)
            {
                triangles.Add(0);
                triangles.Add(i + 1);
                triangles.Add(i);
            }

            mesh.Clear(false);
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetColors(colors);
            mesh.RecalculateBounds();
        }

        private static Vector2 BuildUV(
            VisionRaySample sample,
            VisionSnapshot snapshot,
            VisionUVMode mode)
        {
            switch (mode)
            {
                case VisionUVMode.WorldSpace:
                    return sample.point;
                case VisionUVMode.LocalBounds:
                    Bounds bounds = snapshot.bounds;
                    return new Vector2(
                        Mathf.InverseLerp(bounds.min.x, bounds.max.x, sample.point.x),
                        Mathf.InverseLerp(bounds.min.y, bounds.max.y, sample.point.y));
                default:
                    float radians = sample.normalizedAngle * Mathf.PI * 2f;
                    return new Vector2(
                        0.5f + Mathf.Cos(radians) * sample.normalizedDistance * 0.5f,
                        0.5f + Mathf.Sin(radians) * sample.normalizedDistance * 0.5f);
            }
        }

        private Material GetFallbackMaterial()
        {
            if (!createFallbackMaterial)
                return null;
            if (runtimeMaterial != null)
                return runtimeMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                return null;

            runtimeMaterial = new Material(shader)
            {
                name = "Vision Mesh Runtime Material",
                hideFlags = HideFlags.HideAndDontSave
            };
            return runtimeMaterial;
        }

        private static Color32 WithOpacity(Color color, float opacity)
        {
            color.a *= Mathf.Clamp01(opacity);
            return color;
        }
    }
}
