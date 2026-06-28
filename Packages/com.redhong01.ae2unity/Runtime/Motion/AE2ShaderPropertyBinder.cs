using UnityEngine;

namespace AE2Unity
{
    public static class AE2ShaderPropertyBinder
    {
        public static readonly int TimeId = Shader.PropertyToID("_AE_Time");
        public static readonly int CompSizeId = Shader.PropertyToID("_AE_CompSize");
        public static readonly int LayerPositionId = Shader.PropertyToID("_AE_LayerPosition");
        public static readonly int LayerScaleId = Shader.PropertyToID("_AE_LayerScale");
        public static readonly int LayerRotationId = Shader.PropertyToID("_AE_LayerRotation");
        public static readonly int LayerOpacityId = Shader.PropertyToID("_AE_LayerOpacity");
        public static readonly int ShapeCenterId = Shader.PropertyToID("_AE_ShapeCenter");
        public static readonly int ShapeRadiusId = Shader.PropertyToID("_AE_ShapeRadius");
        public static readonly int ShapeSizeId = Shader.PropertyToID("_AE_ShapeSize");
        public static readonly int CornerRadiusId = Shader.PropertyToID("_AE_CornerRadius");
        public static readonly int PathStartId = Shader.PropertyToID("_AE_PathStart");
        public static readonly int PathEndId = Shader.PropertyToID("_AE_PathEnd");
        public static readonly int FillColorId = Shader.PropertyToID("_AE_FillColor");
        public static readonly int StrokeColorId = Shader.PropertyToID("_AE_StrokeColor");
        public static readonly int StrokeWidthId = Shader.PropertyToID("_AE_StrokeWidth");
        public static readonly int TrimStartId = Shader.PropertyToID("_AE_TrimStart");
        public static readonly int TrimEndId = Shader.PropertyToID("_AE_TrimEnd");
        public static readonly int TrimOffsetId = Shader.PropertyToID("_AE_TrimOffset");

        public static void Bind(
            MaterialPropertyBlock block,
            AE2MotionData motionData,
            AE2MotionEvaluatedLayer layer,
            float time)
        {
            if (block == null)
            {
                return;
            }

            var comp = motionData?.Document?.comp;
            block.SetFloat(TimeId, time);
            block.SetVector(CompSizeId, comp == null
                ? AE2CoordinateConverter.CompSizeVector(1920, 1080)
                : AE2CoordinateConverter.CompSizeVector(comp.width, comp.height));
            block.SetVector(LayerPositionId, new Vector4(layer.position.x, layer.position.y, layer.position.z, 0f));
            block.SetVector(LayerScaleId, new Vector4(layer.scale.x, layer.scale.y, layer.scale.z, 0f));
            block.SetFloat(LayerRotationId, layer.rotation);
            block.SetFloat(LayerOpacityId, layer.opacity01);
            block.SetVector(ShapeCenterId, new Vector4(layer.shapeCenter.x, layer.shapeCenter.y, 0f, 0f));
            block.SetFloat(ShapeRadiusId, layer.shapeRadius);
            block.SetVector(ShapeSizeId, new Vector4(layer.shapeSize.x, layer.shapeSize.y, 0f, 0f));
            block.SetFloat(CornerRadiusId, layer.cornerRadius);
            block.SetVector(PathStartId, new Vector4(layer.pathStart.x, layer.pathStart.y, 0f, 0f));
            block.SetVector(PathEndId, new Vector4(layer.pathEnd.x, layer.pathEnd.y, 0f, 0f));

            var fill = layer.fillColor;
            block.SetColor(FillColorId, fill);
            block.SetColor(StrokeColorId, layer.strokeColor);
            block.SetFloat(StrokeWidthId, layer.strokeWidth);
            block.SetFloat(TrimStartId, layer.trimStart01);
            block.SetFloat(TrimEndId, layer.trimEnd01);
            block.SetFloat(TrimOffsetId, layer.trimOffset);
        }

        public static void Bind(
            Material material,
            AE2MotionData motionData,
            AE2MotionEvaluatedLayer layer,
            float time)
        {
            if (material == null)
            {
                return;
            }

            var comp = motionData?.Document?.comp;
            material.SetFloat(TimeId, time);
            material.SetVector(CompSizeId, comp == null
                ? AE2CoordinateConverter.CompSizeVector(1920, 1080)
                : AE2CoordinateConverter.CompSizeVector(comp.width, comp.height));
            material.SetVector(LayerPositionId, new Vector4(layer.position.x, layer.position.y, layer.position.z, 0f));
            material.SetVector(LayerScaleId, new Vector4(layer.scale.x, layer.scale.y, layer.scale.z, 0f));
            material.SetFloat(LayerRotationId, layer.rotation);
            material.SetFloat(LayerOpacityId, layer.opacity01);
            material.SetVector(ShapeCenterId, new Vector4(layer.shapeCenter.x, layer.shapeCenter.y, 0f, 0f));
            material.SetFloat(ShapeRadiusId, layer.shapeRadius);
            material.SetVector(ShapeSizeId, new Vector4(layer.shapeSize.x, layer.shapeSize.y, 0f, 0f));
            material.SetFloat(CornerRadiusId, layer.cornerRadius);
            material.SetVector(PathStartId, new Vector4(layer.pathStart.x, layer.pathStart.y, 0f, 0f));
            material.SetVector(PathEndId, new Vector4(layer.pathEnd.x, layer.pathEnd.y, 0f, 0f));
            material.SetColor(FillColorId, layer.fillColor);
            material.SetColor(StrokeColorId, layer.strokeColor);
            material.SetFloat(StrokeWidthId, layer.strokeWidth);
            material.SetFloat(TrimStartId, layer.trimStart01);
            material.SetFloat(TrimEndId, layer.trimEnd01);
            material.SetFloat(TrimOffsetId, layer.trimOffset);
        }
    }
}
