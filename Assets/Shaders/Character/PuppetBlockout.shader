Shader "Palworks/PuppetBlockout"
{
    Properties
    {
        _BaseMap("Shape", 2D) = "white" {}
        _BlockoutShadeMap("Blockout Regions", 2D) = "white" {}
        _BaseColor("Part Color", Color) = (0.82,0.82,0.82,1)
        _Opacity("Animation Opacity", Range(0,1)) = 1
        [HideInInspector] _TransitionOpacity("Transition Opacity",Float)=1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "Blockout"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BlockoutShadeMap);
            SAMPLER(sampler_BlockoutShadeMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Opacity;
                half _TransitionOpacity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                // Source RGB is unused. Optional neutral regions preserve a baked
                // head's hair/skin and eyes' white/iris/lash separation. All face
                // maps share one neutral palette regardless of animation packing.
                half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;
                half shade = SAMPLE_TEXTURE2D(_BlockoutShadeMap, sampler_BlockoutShadeMap, input.uv).r;
                half3 normal=half3(0,.276,-.961);
                half3 light=max(SampleSH(normal),half3(.09,.09,.09));
                Light main=GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                light+=main.color*main.shadowAttenuation*saturate(dot(normal,main.direction));
                #if defined(_ADDITIONAL_LIGHTS)
                uint count=GetAdditionalLightsCount();
                for(uint i=0;i<count;i++) {Light local=GetAdditionalLight(i,input.positionWS);light+=local.color*local.distanceAttenuation*saturate(dot(normal,local.direction));}
                #endif
                return half4(_BaseColor.rgb * shade * light, alpha * _BaseColor.a * _Opacity * _TransitionOpacity);
            }
            ENDHLSL
        }
    }
}
