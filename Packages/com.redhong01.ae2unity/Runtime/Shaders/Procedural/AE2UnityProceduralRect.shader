Shader "AE2Unity/Procedural/Rect Unlit"
{
    Properties
    {
        _AE_Time ("AE Time", Float) = 0
        _AE_CompSize ("AE Comp Size", Vector) = (1920, 1080, 0.0005208333, 0.0009259259)
        _AE_LayerPosition ("AE Layer Position", Vector) = (960, 540, 0, 0)
        _AE_LayerScale ("AE Layer Scale", Vector) = (100, 100, 100, 0)
        _AE_LayerRotation ("AE Layer Rotation", Float) = 0
        _AE_LayerOpacity ("AE Layer Opacity", Range(0, 1)) = 1
        _AE_ShapeCenter ("AE Shape Center", Vector) = (0, 0, 0, 0)
        _AE_ShapeSize ("AE Shape Size", Vector) = (256, 256, 0, 0)
        _AE_CornerRadius ("AE Corner Radius", Float) = 0
        _AE_FillColor ("AE Fill Color", Color) = (1, 1, 1, 1)
        _AE_Feather ("AE Feather", Float) = 1.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float _AE_Time;
                float4 _AE_CompSize;
                float4 _AE_LayerPosition;
                float4 _AE_LayerScale;
                float _AE_LayerRotation;
                float _AE_LayerOpacity;
                float4 _AE_ShapeCenter;
                float4 _AE_ShapeSize;
                float _AE_CornerRadius;
                half4 _AE_FillColor;
                float _AE_Feather;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float2 Rotate(float2 value, float degrees)
            {
                float angleRadians = radians(degrees);
                float s = sin(angleRadians);
                float c = cos(angleRadians);
                return float2(c * value.x - s * value.y, s * value.x + c * value.y);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 compSize = max(_AE_CompSize.xy, float2(1.0, 1.0));
                float2 pixel = float2(input.uv.x * compSize.x, (1.0 - input.uv.y) * compSize.y);

                float2 scale = max(abs(_AE_LayerScale.xy) / 100.0, float2(0.0001, 0.0001));
                float2 center = _AE_LayerPosition.xy + _AE_ShapeCenter.xy;
                float2 local = Rotate(pixel - center, -_AE_LayerRotation) / scale;

                float2 halfSize = max(abs(_AE_ShapeSize.xy) * 0.5, float2(0.0001, 0.0001));
                float radius = clamp(_AE_CornerRadius, 0.0, min(halfSize.x, halfSize.y));
                float2 q = abs(local) - halfSize + radius;
                float signedDistance = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;

                float feather = max(_AE_Feather, 0.5);
                float alpha = 1.0 - smoothstep(0.0, feather, signedDistance);

                half4 color = _AE_FillColor;
                color.a *= saturate(_AE_LayerOpacity) * saturate(alpha);
                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
