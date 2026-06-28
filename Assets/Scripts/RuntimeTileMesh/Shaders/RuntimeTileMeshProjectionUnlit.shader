Shader "Duo Curtain/Runtime Tile Projection Unlit"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _ProjectionMode ("Projection Mode", Float) = 2
        _PatternCellSize ("Pattern Cell Size", Vector) = (1, 1, 0, 0)
        _MotionTileSize ("Motion Tile Size", Vector) = (3, 3, 0, 0)
        _PatternOffset ("Pattern Offset", Vector) = (0, 0, 0, 0)
        _PatternScale ("Pattern Scale", Float) = 1
        _PatternAnchor ("Pattern Anchor", Vector) = (0, 0, 0, 0)
        _PatternTime ("Pattern Time", Float) = 0
        _PatternTransition ("Pattern Transition", Range(0, 1)) = 1
        _PatternIntensity ("Pattern Intensity", Range(0, 1)) = 0.35
        _PatternLineWidth ("Pattern Line Width", Range(0.001, 0.5)) = 0.055
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
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
                float2 positionOS : TEXCOORD1;
                float2 positionWS : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _Color;
                float _ProjectionMode;
                float4 _PatternCellSize;
                float4 _MotionTileSize;
                float4 _PatternOffset;
                float _PatternScale;
                float4 _PatternAnchor;
                float _PatternTime;
                float _PatternTransition;
                float _PatternIntensity;
                float _PatternLineWidth;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.uv = input.uv;
                output.positionOS = input.positionOS.xy;
                output.positionWS = positionInputs.positionWS.xy;
                return output;
            }

            float2 SafeAbs2(float2 value, float2 fallback)
            {
                value = abs(value);
                value.x = value.x <= 0.0001 ? fallback.x : value.x;
                value.y = value.y <= 0.0001 ? fallback.y : value.y;
                return value;
            }

            float2 BuildPatternCoordinate(Varyings input)
            {
                float mode = round(_ProjectionMode);
                float2 cellSize = SafeAbs2(_PatternCellSize.xy, float2(1.0, 1.0));

                if (mode < 0.5)
                {
                    return input.uv * _MotionTileSize.xy;
                }

                if (mode < 1.5)
                {
                    return input.positionOS / cellSize;
                }

                if (mode < 2.5)
                {
                    return input.positionWS / cellSize;
                }

                return (input.positionWS - _PatternAnchor.xy) / cellSize;
            }

            float2 BuildTileUV(Varyings input)
            {
                float2 tileSize = SafeAbs2(_MotionTileSize.xy, float2(3.0, 3.0));
                float scale = max(abs(_PatternScale), 0.0001);
                float2 patternCoordinate = (BuildPatternCoordinate(input) + _PatternOffset.xy) * scale;
                return frac(patternCoordinate / tileSize);
            }

            half4 SampleProceduralMotionTile(float2 tileUV)
            {
                float time = _PatternTime;
                float2 centered = tileUV - 0.5;

                float sweep = frac(tileUV.x + time * 0.11);
                float wave = 0.5 + 0.5 * sin((tileUV.y * 6.28318) + time * 1.7);
                float scan = smoothstep(0.0, _PatternLineWidth, abs(sweep - wave));
                scan = 1.0 - scan;

                float2 dotGrid = frac(tileUV * 3.0 + float2(time * 0.08, -time * 0.05)) - 0.5;
                float dots = 1.0 - smoothstep(0.15, 0.23, length(dotGrid));

                float crossLine = min(abs(centered.x), abs(centered.y));
                float graphicLine = 1.0 - smoothstep(_PatternLineWidth, _PatternLineWidth * 1.8, crossLine);

                float pattern = saturate(max(max(scan * 0.8, dots * 0.55), graphicLine * 0.42));
                half shade = (half)lerp(1.0, 0.18 + pattern * 0.82, saturate(_PatternIntensity) * saturate(_PatternTransition));
                return half4(shade, shade, shade, 1.0);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 tileUV = BuildTileUV(input);
                half4 motion = SampleProceduralMotionTile(tileUV);
                half4 tint = _BaseColor * _Color;
                return half4(tint.rgb * motion.rgb, tint.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
