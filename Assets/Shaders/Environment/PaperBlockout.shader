Shader "Palworks/PaperBlockout"
{
    Properties
    {
        [PerRendererData] _MainTex("Shape", 2D) = "white" {}
        _BaseColor("Material Color", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("Renderer Color", Color) = (1,1,1,1)
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _Flip("Flip", Vector) = (1,1,1,1)
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.15
        _DappleTex("Dappled Light",2D)="white"{}
        _DappleStrength("Dapple Strength",Range(0,.3))=0
        [HideInInspector] _TransitionOpacity("Transition Opacity",Float)=1
        [HideInInspector] _ZWrite("Depth Write",Float)=1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="TransparentCutout"
            "Queue"="AlphaTest"
            "CanUseSpriteAtlas"="True"
        }
        Cull Off
        ZWrite [_ZWrite]
        ZTest LEqual
        Blend One Zero

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_DappleTex); SAMPLER(sampler_DappleTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _BaseColor;
            half4 _Color;
            half _Cutoff; half _DappleStrength;
            half _TransitionOpacity;
        CBUFFER_END
        half4 _RendererColor;
        float3 _LightDirection;
        float3 _LightPosition;

        struct Attributes
        {
            float3 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 positionWS : TEXCOORD1;
            half3 normalWS : TEXCOORD2;
            half4 color : COLOR;
            half4 fogAndVertexLight : TEXCOORD3;
            float4 shadowCoord : TEXCOORD4;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        void PrepareSprite(inout Attributes input)
        {
            // The non-instanced SpriteRenderer supplies flipped geometry and vertex color.
            // This follows the instanced path in URP14's native sprite shader.
            #ifdef UNITY_INSTANCING_ENABLED
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteFlip);
                input.normalOS.xy *= unity_SpriteFlip;
                input.color *= unity_SpriteColor;
            #endif
        }

        half4 PaperColor(half4 vertexColor)
        {
            return vertexColor * _Color * _RendererColor * _BaseColor;
        }

        void ClipPaper(float2 uv, half opacity)
        {
            // Every pass samples the same sprite texture's alpha, never its RGB.
            half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a * opacity;
            clip(alpha - _Cutoff);
        }

        half3 PaperNormal(float3 normalOS)
        {
            if (dot(normalOS,normalOS) < 0.0001) normalOS = float3(0,0,-1);
            return TransformObjectToWorldNormal(normalOS);
        }

        Varyings PaperVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input,output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            PrepareSprite(input);
            VertexPositionInputs position = GetVertexPositionInputs(input.positionOS);
            output.positionCS = position.positionCS;
            output.positionWS = position.positionWS;
            output.normalWS = PaperNormal(input.normalOS);
            output.uv = TRANSFORM_TEX(input.uv,_MainTex);
            output.color = PaperColor(input.color);
            output.shadowCoord = GetShadowCoord(position);
            output.fogAndVertexLight = half4(ComputeFogFactor(position.positionCS.z),
                                            VertexLighting(position.positionWS,output.normalWS));
            return output;
        }

        half3 PaperDiffuse(Light light, half3 normalWS)
        {
            return light.color * light.distanceAttenuation * light.shadowAttenuation
                   * saturate(dot(normalWS,light.direction));
        }

        half4 PaperFragment(Varyings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            ClipPaper(input.uv,input.color.a);
            half3 normalWS = normalize(input.normalWS) * IS_FRONT_VFACE(frontFace,1.0h,-1.0h);
            half3 lighting = max(SampleSH(normalWS),0.0h);
            lighting += PaperDiffuse(GetMainLight(input.shadowCoord),normalWS);
            #if defined(_ADDITIONAL_LIGHTS)
                // LIGHT_LOOP_BEGIN uses these fields when Forward+ is enabled.
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                #if USE_FORWARD_PLUS
                    for (uint lightIndex=0;lightIndex<min(URP_FP_DIRECTIONAL_LIGHTS_COUNT,MAX_VISIBLE_LIGHTS);lightIndex++)
                    {
                        FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
                        lighting += PaperDiffuse(GetAdditionalLight(lightIndex,input.positionWS,half4(1,1,1,1)),normalWS);
                    }
                #endif
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    lighting += PaperDiffuse(GetAdditionalLight(lightIndex,input.positionWS,half4(1,1,1,1)),normalWS);
                LIGHT_LOOP_END
            #endif
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                lighting += input.fogAndVertexLight.yzw;
            #endif
            float dapple=SAMPLE_TEXTURE2D(_DappleTex,sampler_DappleTex,input.positionWS.xz*.08+_Time.y*float2(.002,.001)).r;
            half3 color = input.color.rgb * lighting * (1-_DappleStrength*(1-dapple));
            return half4(MixFog(color,input.fogAndVertexLight.x),_TransitionOpacity);
        }

        half PaperDepthFragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            ClipPaper(input.uv,input.color.a);
            return input.positionCS.z;
        }

        half4 PaperDepthNormalsFragment(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            ClipPaper(input.uv,input.color.a);
            float3 normalWS = normalize(input.normalWS) * IS_FRONT_VFACE(face,1.0,-1.0);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                return half4(PackFloat2To888(saturate(octNormalWS*.5+.5)),0);
            #else
                return half4(normalWS,0);
            #endif
        }

        Varyings PaperShadowVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input,output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            PrepareSprite(input);
            float3 positionWS = TransformObjectToWorld(input.positionOS);
            half3 normalWS = PaperNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition-positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
            // The paper is double-sided, so bias the surface toward the casting light.
            normalWS *= dot(normalWS,lightDirectionWS) < 0 ? -1 : 1;
            output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS,normalWS,lightDirectionWS));
            #if UNITY_REVERSED_Z
                output.positionCS.z = min(output.positionCS.z,UNITY_NEAR_CLIP_VALUE);
            #else
                output.positionCS.z = max(output.positionCS.z,UNITY_NEAR_CLIP_VALUE);
            #endif
            output.uv = TRANSFORM_TEX(input.uv,_MainTex);
            output.color = PaperColor(input.color);
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex PaperVertex
            #pragma fragment PaperFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _FORWARD_PLUS
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex PaperVertex
            #pragma fragment PaperDepthFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex PaperVertex
            #pragma fragment PaperDepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex PaperShadowVertex
            #pragma fragment PaperDepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
    }
}
