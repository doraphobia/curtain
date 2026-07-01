Shader "Duo Curtain/Gameplay Visual Adaptive Contrast"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Renderer Color", Color) = (1,1,1,1)
        _BaseColor ("Renderer Base Color", Color) = (1,1,1,1)
        _PrimaryColor ("Primary Color", Color) = (1,1,1,1)
        _SecondaryColor ("Secondary Color", Color) = (0,0,0,1)
        _ContrastStrength ("Contrast Strength", Range(0,2)) = 1
        _ContrastCurve ("Contrast Curve", Range(0.1,8)) = 2
        _BrightnessBias ("Brightness Bias", Range(-0.5,0.5)) = 0
        _EdgeContrast ("Edge Contrast", Range(0,2)) = 0.35
        _OutlineStrength ("Outline Strength", Range(0,1)) = 0
        _OutlineWidth ("Outline Width", Range(0,8)) = 1
        _OutlineColor ("Outline Color", Color) = (1,1,1,1)
        _HaloStrength ("Halo Strength", Range(0,2)) = 0
        _Priority ("Priority", Range(0,100)) = 60
        _AdaptiveBlend ("Adaptive Blend", Range(0,1)) = 1
        _DebugMode ("Debug Mode", Range(0,4)) = 0
        [HideInInspector] _UseVertexColor ("Use Vertex Color", Float) = 0
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+100"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "GameplayVisualAdaptiveContrast"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "GameplayAdaptiveContrast.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float4 screenPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                half4 _PrimaryColor;
                half4 _SecondaryColor;
                half4 _OutlineColor;
                half4 _Color;
                half4 _BaseColor;
                float _ContrastStrength;
                float _ContrastCurve;
                float _BrightnessBias;
                float _EdgeContrast;
                float _OutlineStrength;
                float _OutlineWidth;
                float _HaloStrength;
                float _Priority;
                float _AdaptiveBlend;
                float _DebugMode;
                float _UseVertexColor;
            CBUFFER_END

            float _GameplayVisualGlobalDebugMode;

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.screenPosition = ComputeScreenPos(output.positionCS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenUV = input.screenPosition.xy / max(input.screenPosition.w, 0.00001);
                float3 background = SampleSceneColor(screenUV);

                half4 textureColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 vertexTint = lerp(half4(1.0, 1.0, 1.0, 1.0), input.color, saturate(_UseVertexColor));
                half4 source = textureColor * vertexTint * _Color;

                float2 sceneTexel = 1.0 / max(_ScaledScreenParams.xy, float2(1.0, 1.0));
                float luminanceCenter = DuoCurtainLuminance(background);
                float luminanceX = DuoCurtainLuminance(SampleSceneColor(screenUV + float2(sceneTexel.x, 0.0)));
                float luminanceY = DuoCurtainLuminance(SampleSceneColor(screenUV + float2(0.0, sceneTexel.y)));
                float edgeFactor = saturate(max(abs(luminanceX - luminanceCenter), abs(luminanceY - luminanceCenter))) * _EdgeContrast;

                float backgroundLuminance;
                float contrastMap;
                float3 adaptiveColor = DuoCurtainAdaptiveContrast(
                    source.rgb,
                    background,
                    _PrimaryColor.rgb,
                    _SecondaryColor.rgb,
                    _ContrastStrength,
                    _ContrastCurve,
                    _BrightnessBias,
                    _AdaptiveBlend,
                    _Priority,
                    edgeFactor,
                    backgroundLuminance,
                    contrastMap);

                float2 outlineOffset = _MainTex_TexelSize.xy * max(0.0, _OutlineWidth);
                float neighborAlpha = 0.0;
                neighborAlpha = max(neighborAlpha, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(outlineOffset.x, 0)).a);
                neighborAlpha = max(neighborAlpha, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - float2(outlineOffset.x, 0)).a);
                neighborAlpha = max(neighborAlpha, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + float2(0, outlineOffset.y)).a);
                neighborAlpha = max(neighborAlpha, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - float2(0, outlineOffset.y)).a);
                float outlineAlpha = saturate(neighborAlpha - textureColor.a) * _OutlineStrength;
                float haloAlpha = saturate(neighborAlpha - textureColor.a) * _HaloStrength * 0.5;

                float outlineLuminance;
                float outlineContrastMap;
                float3 adaptiveOutline = DuoCurtainAdaptiveContrast(
                    _OutlineColor.rgb,
                    background,
                    _PrimaryColor.rgb,
                    _SecondaryColor.rgb,
                    _ContrastStrength,
                    _ContrastCurve,
                    _BrightnessBias,
                    _AdaptiveBlend,
                    _Priority,
                    edgeFactor,
                    outlineLuminance,
                    outlineContrastMap);

                float finalAlpha = saturate(source.a + outlineAlpha + haloAlpha);
                float3 finalColor = lerp(adaptiveColor, adaptiveOutline, saturate(outlineAlpha + haloAlpha));
                float debugMode = max(_DebugMode, _GameplayVisualGlobalDebugMode);
                if (debugMode > 3.5)
                    return half4(_Priority / 100.0, 0.0, 1.0 - _Priority / 100.0, finalAlpha);
                if (debugMode > 2.5)
                    return half4(_AdaptiveBlend.xxx, finalAlpha);
                if (debugMode > 1.5)
                    return half4(contrastMap, 1.0 - contrastMap, 0.0, finalAlpha);
                if (debugMode > 0.5)
                    return half4(backgroundLuminance.xxx, finalAlpha);

                return half4(saturate(finalColor), finalAlpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
