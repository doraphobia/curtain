using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DuoCurtain.GameplayVisuals
{
    [DisallowMultipleComponent]
    public sealed class GameplayVisualRenderer : MonoBehaviour
    {
        public enum VertexColorMode
        {
            Auto,
            ForceOff,
            ForceOn
        }

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int UseVertexColorId = Shader.PropertyToID("_UseVertexColor");
        private static readonly int PrimaryColorId = Shader.PropertyToID("_PrimaryColor");
        private static readonly int SecondaryColorId = Shader.PropertyToID("_SecondaryColor");
        private static readonly int ContrastStrengthId = Shader.PropertyToID("_ContrastStrength");
        private static readonly int ContrastCurveId = Shader.PropertyToID("_ContrastCurve");
        private static readonly int BrightnessBiasId = Shader.PropertyToID("_BrightnessBias");
        private static readonly int EdgeContrastId = Shader.PropertyToID("_EdgeContrast");
        private static readonly int OutlineStrengthId = Shader.PropertyToID("_OutlineStrength");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int HaloStrengthId = Shader.PropertyToID("_HaloStrength");
        private static readonly int PriorityId = Shader.PropertyToID("_Priority");
        private static readonly int AdaptiveBlendId = Shader.PropertyToID("_AdaptiveBlend");
        private static readonly int DebugModeId = Shader.PropertyToID("_DebugMode");

        [Header("Targets")]
        public bool collectTargetsAutomatically = true;
        public Renderer[] renderers;
        public Graphic[] graphics;

        [Header("Compatibility")]
        [Tooltip("Off by default so the accessibility layer does not replace authored visual materials.")]
        public bool replaceOriginalMaterials;

        [Header("Style")]
        public GameplayVisualProfile profile;
        public bool enableAdaptiveContrast = true;
        [Range(0f, 2f)] public float contrastStrength = 1f;
        [Range(0.1f, 8f)] public float contrastCurve = 2f;
        [Range(-0.5f, 0.5f)] public float brightnessBias;
        public Color primaryColor = Color.white;
        public Color secondaryColor = Color.black;
        [Range(0f, 1f)] public float adaptiveBlend = 1f;
        [Min(0f)] public float adaptiveBlendSpeed = 8f;
        public GameplayVisualPriority priority = GameplayVisualPriority.Interaction;
        public VertexColorMode vertexColorMode = VertexColorMode.Auto;

        [Header("Edges")]
        [Range(0f, 2f)] public float edgeContrast = 0.35f;
        public bool enableOutline;
        [Range(0f, 8f)] public float outlineWidth = 1f;
        [Range(0f, 1f)] public float outlineStrength = 0.85f;
        public Color outlineColor = Color.white;
        public bool enableHalo;
        [Range(0f, 2f)] public float haloStrength = 0.25f;

        [Header("Debug")]
        public GameplayVisualDebugMode debugMode;

        private readonly Dictionary<Renderer, Material[]> originalRendererMaterials =
            new Dictionary<Renderer, Material[]>();
        private readonly Dictionary<Graphic, Material> originalGraphicMaterials =
            new Dictionary<Graphic, Material>();
        private readonly Dictionary<Renderer, Material[]> adaptiveRendererMaterials =
            new Dictionary<Renderer, Material[]>();
        private readonly List<Material> runtimeMaterials = new List<Material>();
        private Material sharedRuntimeMaterial;
        private MaterialPropertyBlock propertyBlock;
        private bool applied;
        private float runtimeAdaptiveBlend = -1f;

        public static GameplayVisualRenderer Ensure(GameObject target, GameplayVisualPriority visualPriority)
        {
            if (target == null)
                return null;

            GameplayVisualRenderer visual = target.GetComponent<GameplayVisualRenderer>();
            if (visual == null)
                visual = target.AddComponent<GameplayVisualRenderer>();
            visual.priority = visualPriority;
            visual.Refresh();
            return visual;
        }

        public static GameplayVisualRenderer Ensure(Renderer target, GameplayVisualPriority visualPriority)
        {
            if (target == null)
                return null;

            GameplayVisualRenderer visual = target.gameObject.GetComponent<GameplayVisualRenderer>();
            if (visual == null)
                visual = target.gameObject.AddComponent<GameplayVisualRenderer>();
            visual.renderers = new[] { target };
            visual.graphics = null;
            visual.collectTargetsAutomatically = false;
            visual.priority = visualPriority;
            visual.Refresh();
            return visual;
        }

        public static GameplayVisualRenderer Ensure(Graphic target, GameplayVisualPriority visualPriority)
        {
            if (target == null)
                return null;

            GameplayVisualRenderer visual = target.gameObject.GetComponent<GameplayVisualRenderer>();
            if (visual == null)
                visual = target.gameObject.AddComponent<GameplayVisualRenderer>();
            visual.graphics = new[] { target };
            visual.renderers = null;
            visual.collectTargetsAutomatically = false;
            visual.priority = visualPriority;
            visual.Refresh();
            return visual;
        }

        void OnEnable()
        {
            Refresh();
        }

        void Update()
        {
            if (!replaceOriginalMaterials)
                return;

            float targetBlend = profile != null ? profile.adaptiveBlend : adaptiveBlend;
            float blendSpeed = profile != null ? profile.adaptiveBlendSpeed : adaptiveBlendSpeed;
            if (runtimeAdaptiveBlend < 0f)
                runtimeAdaptiveBlend = targetBlend;

            float nextBlend = blendSpeed <= 0f
                ? targetBlend
                : Mathf.Lerp(
                    runtimeAdaptiveBlend,
                    targetBlend,
                    1f - Mathf.Exp(-blendSpeed * Time.unscaledDeltaTime));
            if (Mathf.Abs(nextBlend - runtimeAdaptiveBlend) <= 0.0001f)
                return;

            runtimeAdaptiveBlend = nextBlend;
            ApplyProperties();
        }

        void OnDisable()
        {
            RestoreOriginalMaterials();
        }

        void OnDestroy()
        {
            RestoreOriginalMaterials();
            DestroyRuntimeMaterials();
        }

        void OnValidate()
        {
            contrastStrength = Mathf.Max(0f, contrastStrength);
            contrastCurve = Mathf.Max(0.1f, contrastCurve);
            adaptiveBlendSpeed = Mathf.Max(0f, adaptiveBlendSpeed);
            outlineWidth = Mathf.Max(0f, outlineWidth);
            if (isActiveAndEnabled)
                Refresh();
        }

        public void Refresh()
        {
            if (!replaceOriginalMaterials)
            {
                RestoreOriginalMaterials();
                return;
            }

            Shader shader = GameplayVisualSystem.FindAdaptiveShader();
            if (shader == null)
                return;

            if (collectTargetsAutomatically)
                CollectMissingTargets();

            float targetBlend = profile != null ? profile.adaptiveBlend : adaptiveBlend;
            if (runtimeAdaptiveBlend < 0f || !Application.isPlaying)
                runtimeAdaptiveBlend = targetBlend;

            EnsureSharedMaterial(shader);
            ApplyRendererMaterials(shader);
            ApplyGraphicMaterials(shader);
            ApplyProperties();
            applied = true;
        }

        public void SetPalette(Color brightBackgroundVariant, Color darkBackgroundVariant)
        {
            primaryColor = brightBackgroundVariant;
            secondaryColor = darkBackgroundVariant;
            if (!replaceOriginalMaterials)
                return;
            ApplyProperties();
        }

        private void CollectMissingTargets()
        {
            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>(true);
            if (graphics == null || graphics.Length == 0)
                graphics = GetComponentsInChildren<Graphic>(true);
        }

        private void EnsureSharedMaterial(Shader shader)
        {
            if (sharedRuntimeMaterial != null && sharedRuntimeMaterial.shader == shader)
                return;

            if (sharedRuntimeMaterial != null)
                DestroyRuntimeMaterial(sharedRuntimeMaterial);

            sharedRuntimeMaterial = new Material(shader)
            {
                name = "Gameplay Visual Adaptive Contrast (Runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
            runtimeMaterials.Add(sharedRuntimeMaterial);
        }

        private void ApplyRendererMaterials(Shader shader)
        {
            if (renderers == null || shader == null)
                return;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer target = renderers[i];
                if (target == null)
                    continue;

                if (!originalRendererMaterials.ContainsKey(target))
                    originalRendererMaterials.Add(target, target.sharedMaterials);

                Material[] source = originalRendererMaterials[target];
                int materialCount = Mathf.Max(1, source != null ? source.Length : 0);
                Material[] adaptive = GetOrCreateAdaptiveRendererMaterials(target, source, materialCount, shader);
                SyncRendererMaterialInputs(target, source, adaptive);
                target.sharedMaterials = adaptive;
            }
        }

        private void ApplyGraphicMaterials(Shader shader)
        {
            if (graphics == null)
                return;

            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic target = graphics[i];
                if (target == null)
                    continue;

                if (!originalGraphicMaterials.ContainsKey(target))
                    originalGraphicMaterials.Add(target, target.material);

                Material material = target.material;
                if (material != null && material.shader == shader && runtimeMaterials.Contains(material))
                {
                    SyncGraphicMaterialInputs(target, material, originalGraphicMaterials[target]);
                    continue;
                }

                Material adaptive = new Material(shader)
                {
                    name = target.name + " Adaptive Contrast (Runtime)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                CopyCommonMaterialInputs(originalGraphicMaterials[target], adaptive);
                SyncGraphicMaterialInputs(target, adaptive, originalGraphicMaterials[target]);
                runtimeMaterials.Add(adaptive);
                target.material = adaptive;
            }
        }

        private void ApplyProperties()
        {
            VisualValues values = ResolveValues();
            if (propertyBlock == null)
                propertyBlock = new MaterialPropertyBlock();

            if (renderers != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer target = renderers[i];
                    if (target == null)
                        continue;
                    target.GetPropertyBlock(propertyBlock);
                    WriteProperties(propertyBlock, values);
                    target.SetPropertyBlock(propertyBlock);
                }
            }

            if (graphics == null)
                return;
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic target = graphics[i];
                if (target == null || target.material == null)
                    continue;
                WriteProperties(target.material, values);
                target.SetMaterialDirty();
            }
        }

        private VisualValues ResolveValues()
        {
            if (profile == null)
            {
                return new VisualValues(
                    enableAdaptiveContrast, contrastStrength, contrastCurve, brightnessBias,
                    primaryColor, secondaryColor, runtimeAdaptiveBlend, priority, edgeContrast,
                    enableOutline ? outlineStrength : 0f, outlineWidth, outlineColor,
                    enableHalo ? haloStrength : 0f, debugMode);
            }

            return new VisualValues(
                profile.enableAdaptiveContrast, profile.contrastStrength, profile.contrastCurve,
                profile.brightnessBias, profile.primaryColor, profile.secondaryColor,
                runtimeAdaptiveBlend, profile.priority, profile.edgeContrast,
                profile.enableOutline ? profile.outlineStrength : 0f, profile.outlineWidth,
                profile.outlineColor, profile.enableHalo ? profile.haloStrength : 0f,
                profile.debugMode);
        }

        private static void WriteProperties(MaterialPropertyBlock block, VisualValues values)
        {
            block.SetColor(PrimaryColorId, values.primaryColor);
            block.SetColor(SecondaryColorId, values.secondaryColor);
            block.SetFloat(ContrastStrengthId, values.enabled ? values.contrastStrength : 0f);
            block.SetFloat(ContrastCurveId, values.contrastCurve);
            block.SetFloat(BrightnessBiasId, values.brightnessBias);
            block.SetFloat(EdgeContrastId, values.edgeContrast);
            block.SetFloat(OutlineStrengthId, values.outlineStrength);
            block.SetFloat(OutlineWidthId, values.outlineWidth);
            block.SetColor(OutlineColorId, values.outlineColor);
            block.SetFloat(HaloStrengthId, values.haloStrength);
            block.SetFloat(PriorityId, (float)values.priority);
            block.SetFloat(AdaptiveBlendId, values.adaptiveBlend);
            block.SetFloat(DebugModeId, (float)values.debugMode);
        }

        private static void WriteProperties(Material material, VisualValues values)
        {
            material.SetColor(PrimaryColorId, values.primaryColor);
            material.SetColor(SecondaryColorId, values.secondaryColor);
            material.SetFloat(ContrastStrengthId, values.enabled ? values.contrastStrength : 0f);
            material.SetFloat(ContrastCurveId, values.contrastCurve);
            material.SetFloat(BrightnessBiasId, values.brightnessBias);
            material.SetFloat(EdgeContrastId, values.edgeContrast);
            material.SetFloat(OutlineStrengthId, values.outlineStrength);
            material.SetFloat(OutlineWidthId, values.outlineWidth);
            material.SetColor(OutlineColorId, values.outlineColor);
            material.SetFloat(HaloStrengthId, values.haloStrength);
            material.SetFloat(PriorityId, (float)values.priority);
            material.SetFloat(AdaptiveBlendId, values.adaptiveBlend);
            material.SetFloat(DebugModeId, (float)values.debugMode);
        }

        private Material[] GetOrCreateAdaptiveRendererMaterials(
            Renderer target,
            Material[] source,
            int materialCount,
            Shader shader)
        {
            if (adaptiveRendererMaterials.TryGetValue(target, out Material[] existing) &&
                existing != null &&
                existing.Length == materialCount &&
                AllUseShader(existing, shader))
            {
                return existing;
            }

            if (existing != null)
            {
                for (int i = 0; i < existing.Length; i++)
                {
                    runtimeMaterials.Remove(existing[i]);
                    DestroyRuntimeMaterial(existing[i]);
                }
            }

            Material[] adaptive = new Material[materialCount];
            for (int i = 0; i < materialCount; i++)
            {
                Material original = source != null && i < source.Length ? source[i] : null;
                adaptive[i] = new Material(shader)
                {
                    name = target.name + " Adaptive Contrast " + i + " (Runtime)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                CopyCommonMaterialInputs(original, adaptive[i]);
                runtimeMaterials.Add(adaptive[i]);
            }

            adaptiveRendererMaterials[target] = adaptive;
            return adaptive;
        }

        private static bool AllUseShader(Material[] materials, Shader shader)
        {
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null || materials[i].shader != shader)
                    return false;
            }

            return true;
        }

        private void SyncRendererMaterialInputs(
            Renderer target,
            Material[] source,
            Material[] adaptive)
        {
            if (adaptive == null)
                return;

            Texture spriteTexture = null;
            Color rendererColor = Color.white;
            bool useRendererVertexColor = ResolveRendererVertexColorUsage(target);
            if (target is SpriteRenderer spriteRenderer)
            {
                spriteTexture = spriteRenderer.sprite != null ? spriteRenderer.sprite.texture : null;
                rendererColor = spriteRenderer.color;
            }

            for (int i = 0; i < adaptive.Length; i++)
            {
                Material material = adaptive[i];
                if (material == null)
                    continue;

                Material original = source != null && i < source.Length ? source[i] : null;
                if (spriteTexture != null && material.HasProperty(MainTexId))
                    material.SetTexture(MainTexId, spriteTexture);
                else
                    CopyTexture(original, material, MainTexId, BaseMapId);

                if (!useRendererVertexColor)
                    CopyColor(original, material, ColorId, BaseColorId, rendererColor);
                else
                    SetColorIfPresent(material, ColorId, Color.white);
                SetFloatIfPresent(material, UseVertexColorId, useRendererVertexColor ? 1f : 0f);
            }
        }

        private static void SyncGraphicMaterialInputs(Graphic target, Material adaptive, Material original)
        {
            if (target == null || adaptive == null)
                return;

            Texture texture = target.mainTexture;
            if (texture != null && adaptive.HasProperty(MainTexId))
                adaptive.SetTexture(MainTexId, texture);
            else
                CopyTexture(original, adaptive, MainTexId, BaseMapId);

            SetColorIfPresent(adaptive, ColorId, Color.white);
            SetColorIfPresent(adaptive, BaseColorId, Color.white);
            SetFloatIfPresent(adaptive, UseVertexColorId, 1f);
        }

        private bool ResolveRendererVertexColorUsage(Renderer target)
        {
            if (vertexColorMode == VertexColorMode.ForceOn)
                return true;
            if (vertexColorMode == VertexColorMode.ForceOff)
                return false;
            if (target is SpriteRenderer)
                return true;

            MeshFilter meshFilter = target != null ? target.GetComponent<MeshFilter>() : null;
            Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            if (mesh == null || mesh.vertexCount <= 0)
                return false;

            Color[] colors = mesh.colors;
            if (colors != null && colors.Length == mesh.vertexCount)
                return true;
            Color32[] colors32 = mesh.colors32;
            return colors32 != null && colors32.Length == mesh.vertexCount;
        }

        private static void CopyCommonMaterialInputs(Material source, Material destination)
        {
            if (destination == null)
                return;

            CopyTexture(source, destination, MainTexId, BaseMapId);
            CopyTexture(source, destination, BaseMapId, MainTexId);
            CopyColor(source, destination, ColorId, BaseColorId, Color.white);
            CopyColor(source, destination, BaseColorId, ColorId, Color.white);

            CopyFloat(source, destination, Shader.PropertyToID("_StencilComp"));
            CopyFloat(source, destination, Shader.PropertyToID("_Stencil"));
            CopyFloat(source, destination, Shader.PropertyToID("_StencilOp"));
            CopyFloat(source, destination, Shader.PropertyToID("_StencilWriteMask"));
            CopyFloat(source, destination, Shader.PropertyToID("_StencilReadMask"));
            CopyFloat(source, destination, Shader.PropertyToID("_ColorMask"));
        }

        private static void CopyTexture(Material source, Material destination, int preferredSourceId, int fallbackSourceId)
        {
            if (destination == null || !destination.HasProperty(MainTexId))
                return;

            Texture texture = null;
            if (source != null)
            {
                if (source.HasProperty(preferredSourceId))
                    texture = source.GetTexture(preferredSourceId);
                if (texture == null && source.HasProperty(fallbackSourceId))
                    texture = source.GetTexture(fallbackSourceId);
            }

            if (texture != null)
                destination.SetTexture(MainTexId, texture);
        }

        private static void CopyColor(Material source, Material destination, int preferredSourceId, int fallbackSourceId, Color fallback)
        {
            if (destination == null)
                return;

            Color color = fallback;
            if (source != null)
            {
                if (source.HasProperty(preferredSourceId))
                    color = source.GetColor(preferredSourceId);
                else if (source.HasProperty(fallbackSourceId))
                    color = source.GetColor(fallbackSourceId);
            }

            SetColorIfPresent(destination, ColorId, color);
            SetColorIfPresent(destination, BaseColorId, color);
        }

        private static void SetColorIfPresent(Material material, int id, Color color)
        {
            if (material != null && material.HasProperty(id))
                material.SetColor(id, color);
        }

        private static void SetFloatIfPresent(Material material, int id, float value)
        {
            if (material != null && material.HasProperty(id))
                material.SetFloat(id, value);
        }

        private static void CopyFloat(Material source, Material destination, int id)
        {
            if (source == null || destination == null || !source.HasProperty(id) || !destination.HasProperty(id))
                return;

            destination.SetFloat(id, source.GetFloat(id));
        }

        private void RestoreOriginalMaterials()
        {
            if (!applied)
                return;

            foreach (KeyValuePair<Renderer, Material[]> pair in originalRendererMaterials)
            {
                if (pair.Key != null)
                    pair.Key.sharedMaterials = pair.Value;
            }
            foreach (KeyValuePair<Graphic, Material> pair in originalGraphicMaterials)
            {
                if (pair.Key != null)
                    pair.Key.material = pair.Value;
            }

            originalRendererMaterials.Clear();
            originalGraphicMaterials.Clear();
            adaptiveRendererMaterials.Clear();
            applied = false;
        }

        private void DestroyRuntimeMaterials()
        {
            for (int i = 0; i < runtimeMaterials.Count; i++)
                DestroyRuntimeMaterial(runtimeMaterials[i]);
            runtimeMaterials.Clear();
            sharedRuntimeMaterial = null;
        }

        private static void DestroyRuntimeMaterial(Material material)
        {
            if (material == null)
                return;
            if (Application.isPlaying)
                Destroy(material);
            else
                DestroyImmediate(material);
        }

        private readonly struct VisualValues
        {
            public readonly bool enabled;
            public readonly float contrastStrength;
            public readonly float contrastCurve;
            public readonly float brightnessBias;
            public readonly Color primaryColor;
            public readonly Color secondaryColor;
            public readonly float adaptiveBlend;
            public readonly GameplayVisualPriority priority;
            public readonly float edgeContrast;
            public readonly float outlineStrength;
            public readonly float outlineWidth;
            public readonly Color outlineColor;
            public readonly float haloStrength;
            public readonly GameplayVisualDebugMode debugMode;

            public VisualValues(
                bool enabled, float contrastStrength, float contrastCurve, float brightnessBias,
                Color primaryColor, Color secondaryColor, float adaptiveBlend,
                GameplayVisualPriority priority, float edgeContrast, float outlineStrength,
                float outlineWidth, Color outlineColor, float haloStrength,
                GameplayVisualDebugMode debugMode)
            {
                this.enabled = enabled;
                this.contrastStrength = contrastStrength;
                this.contrastCurve = contrastCurve;
                this.brightnessBias = brightnessBias;
                this.primaryColor = primaryColor;
                this.secondaryColor = secondaryColor;
                this.adaptiveBlend = adaptiveBlend;
                this.priority = priority;
                this.edgeContrast = edgeContrast;
                this.outlineStrength = outlineStrength;
                this.outlineWidth = outlineWidth;
                this.outlineColor = outlineColor;
                this.haloStrength = haloStrength;
                this.debugMode = debugMode;
            }
        }
    }
}
