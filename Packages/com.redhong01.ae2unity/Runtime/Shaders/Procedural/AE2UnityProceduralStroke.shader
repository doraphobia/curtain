Shader "AE2Unity/Procedural/Stroke Unlit"
{
    Properties
    {
        _AE_Time ("AE Time", Float) = 0
        _AE_CompSize ("AE Comp Size", Vector) = (1920, 1080, 0.0005208333, 0.0009259259)
        _AE_LayerPosition ("AE Layer Position", Vector) = (960, 540, 0, 0)
        _AE_LayerScale ("AE Layer Scale", Vector) = (100, 100, 100, 0)
        _AE_LayerRotation ("AE Layer Rotation", Float) = 0
        _AE_LayerOpacity ("AE Layer Opacity", Range(0, 1)) = 1
        _AE_PathStart ("AE Path Start", Vector) = (-128, 0, 0, 0)
        _AE_PathEnd ("AE Path End", Vector) = (128, 0, 0, 0)
        _AE_StrokeColor ("AE Stroke Color", Color) = (1, 1, 1, 1)
        _AE_StrokeWidth ("AE Stroke Width", Float) = 12
        _AE_TrimStart ("AE Trim Start", Range(0, 1)) = 0
        _AE_TrimEnd ("AE Trim End", Range(0, 1)) = 1
        _AE_TrimOffset ("AE Trim Offset", Float) = 0
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
                float4 _AE_PathStart;
                float4 _AE_PathEnd;
                half4 _AE_StrokeColor;
                float _AE_StrokeWidth;
                float _AE_TrimStart;
                float _AE_TrimEnd;
                float _AE_TrimOffset;
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

            float2 TransformAePoint(float2 localPoint)
            {
                float2 scale = _AE_LayerScale.xy / 100.0;
                return _AE_LayerPosition.xy + Rotate(localPoint * scale, _AE_LayerRotation);
            }

            float DistanceToSegment(float2 p, float2 a, float2 b)
            {
                float2 ab = b - a;
                float lengthSq = max(dot(ab, ab), 0.0001);
                float h = saturate(dot(p - a, ab) / lengthSq);
                return length(p - (a + ab * h));
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 compSize = max(_AE_CompSize.xy, float2(1.0, 1.0));
                float2 pixel = float2(input.uv.x * compSize.x, (1.0 - input.uv.y) * compSize.y);

                float2 sourceA = TransformAePoint(_AE_PathStart.xy);
                float2 sourceB = TransformAePoint(_AE_PathEnd.xy);
                float trimStart = saturate(_AE_TrimStart + _AE_TrimOffset / 360.0);
                float trimEnd = saturate(_AE_TrimEnd + _AE_TrimOffset / 360.0);
                float2 a = lerp(sourceA, sourceB, min(trimStart, trimEnd));
                float2 b = lerp(sourceA, sourceB, max(trimStart, trimEnd));

                float signedDistance = DistanceToSegment(pixel, a, b) - max(_AE_StrokeWidth * 0.5, 0.0001);
                float feather = max(_AE_Feather, 0.5);
                float alpha = 1.0 - smoothstep(0.0, feather, signedDistance);

                half4 color = _AE_StrokeColor;
                color.a *= saturate(_AE_LayerOpacity) * saturate(alpha);
                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
