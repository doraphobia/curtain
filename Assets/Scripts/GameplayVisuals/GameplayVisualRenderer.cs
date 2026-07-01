using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DuoCurtain.GameplayVisuals
{
    [DisallowMultipleComponent]
    public sealed class GameplayVisualRenderer : MonoBehaviour
    {
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

            GameplayVisualRenderer visual = Ensure(target.gameObject, visualPriority);
            visual.renderers = new[] { target };
            visual.graphics = null;
            visual.Refresh();
            return visual;
        }

        public static GameplayVisualRenderer Ensure(Graphic target, GameplayVisualPriority visualPriority)
        {
            if (target == null)
                return null;

            GameplayVisualRenderer visual = Ensure(target.gameObject, visualPriority);
            visual.graphics = new[] { target };
            visual.renderers = null;
            visual.Refresh();
            return visual;
        }

        void OnEnable()
        {
            Refresh();
        }

        void Update()
        {
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
            Shader shader = GameplayVisualSystem.FindAdaptiveShader();
            if (shader == null)
                return;

            if (collectTargetsAutomatically)
                CollectMissingTargets();

            float targetBlend = profile != null ? profile.adaptiveBlend : adaptiveBlend;
            if (runtimeAdaptiveBlend < 0f || !Application.isPlaying)
                runtimeAdaptiveBlend = targetBlend;

            EnsureSharedMaterial(shader);
            ApplyRendererMaterials();
            ApplyGraphicMaterials(shader);
            ApplyProperties();
            applied = true;
        }

        public void SetPalette(Color brightBackgroundVariant, Color darkBackgroundVariant)
        {
            primaryColor = brightBackgroundVariant;
            secondaryColor = darkBackgroundVariant;
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

        private void ApplyRendererMaterials()
        {
            if (renderers == null || sharedRuntimeMaterial == null)
                return;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer target = renderers[i];
                if (target == null)
                    continue;

                if (!originalRendererMaterials.ContainsKey(target))
                    originalRendererMaterials.Add(target, target.sharedMaterials);

                Material[] source = target.sharedMaterials;
                int materialCount = Mathf.Max(1, source != null ? source.Length : 0);
                Material[] adaptive = new Material[materialCount];
                for (int m = 0; m < adaptive.Length; m++)
                    adaptive[m] = sharedRuntimeMaterial;
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
                    continue;

                Material adaptive = new Material(shader)
                {
                    name = target.name + " Adaptive Contrast (Runtime)",
                    hideFlags = HideFlags.HideAndDontSave
                };
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
