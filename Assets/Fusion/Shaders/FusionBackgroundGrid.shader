Shader "Duo Curtain/Fusion Background Grid"
{
    Properties
    {
        _TopColor ("Top Color", Color) = (0.58, 0.59, 0.6, 1)
        _BottomColor ("Bottom Color", Color) = (0.42, 0.43, 0.44, 1)
        _GridColor ("Grid Color", Color) = (0.9, 0.9, 0.9, 0.22)
        _GridCellSize ("Grid Cell Size", Vector) = (1, 5, 0, 0)
        _GridLineWidth ("Grid Line Width", Range(0.001, 0.2)) = 0.012
        _GridOpacity ("Grid Opacity", Range(0, 1)) = 0.24
        _VignetteStrength ("Vignette Strength", Range(0, 1)) = 0.18
        _PulseStrength ("Pulse Strength", Range(0, 1)) = 0.05
        _TimeOffset ("Time Offset", Float) = 0
        _DriftSpeed ("Drift Speed", Vector) = (0.015, -0.006, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Background"
            "RenderType" = "Opaque"
        }

        ZWrite Off
        ZTest Always
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
                float2 positionWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _BottomColor;
                half4 _GridColor;
                float4 _GridCellSize;
                float _GridLineWidth;
                float _GridOpacity;
                float _VignetteStrength;
                float _PulseStrength;
                float _TimeOffset;
                float4 _DriftSpeed;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.uv = input.uv;
                output.positionWS = positionInputs.positionWS.xy;
                return output;
            }

            float GridLine(float2 positionWS)
            {
                float2 cellSize = max(abs(_GridCellSize.xy), float2(0.0001, 0.0001));
                float2 drift = _DriftSpeed.xy * _TimeOffset;
                float2 cellUV = frac((positionWS + drift) / cellSize);
                float2 edgeDistance = min(cellUV, 1.0 - cellUV);
                float lineDistance = min(edgeDistance.x, edgeDistance.y);
                float width = max(_GridLineWidth, 0.0001);
                return 1.0 - smoothstep(width, width * 1.8, lineDistance);
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 baseColor = lerp(_BottomColor.rgb, _TopColor.rgb, saturate(input.uv.y));
                float grid = GridLine(input.positionWS) * saturate(_GridOpacity);

                float2 centered = input.uv - 0.5;
                float vignette = smoothstep(0.25, 0.74, length(centered));
                float pulse = 0.5 + 0.5 * sin(_TimeOffset * 0.75 + input.positionWS.x * 0.17);

                half3 color = lerp(baseColor, _GridColor.rgb, grid * _GridColor.a);
                color *= 1.0 - vignette * saturate(_VignetteStrength);
                color += (pulse - 0.5) * saturate(_PulseStrength);

                return half4(saturate(color), 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
