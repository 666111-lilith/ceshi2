Shader "Hidden/Palworks/PaperDepthOfField"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Paper Focus"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragPaperFocus

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // focus distance, near sharp range, far sharp range, depth rejection distance
            float4 _PaperFocusParams;
            // near transition, far transition, near radius px, far radius px
            float4 _PaperBlurParams;
            float4 _PaperPixelParams;
            float _PaperFocusDebug;

            float EyeDepth(float2 uv)
            {
                float raw = SampleSceneDepth(uv);
                // URP 14's stock DOF only uses the perspective conversion.
                // Orthographic raw depth is already linear between the clip planes.
                return unity_OrthoParams.w > 0.5
                    ? LinearDepthToEyeDepth(raw)
                    : LinearEyeDepth(raw, _ZBufferParams);
            }

            half4 FragPaperFocus(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float depth = EyeDepth(uv);
                float nearEdge = _PaperFocusParams.x - _PaperFocusParams.y;
                float farEdge = _PaperFocusParams.x + _PaperFocusParams.z;
                float nearAmount = smoothstep(0, _PaperBlurParams.x, max(0, nearEdge - depth));
                float farAmount = smoothstep(0, _PaperBlurParams.y, max(0, depth - farEdge));
                if (_PaperFocusDebug > .5) return half4(nearAmount,1-max(nearAmount,farAmount),farAmount,1);
                float radius = max(nearAmount * _PaperBlurParams.z, farAmount * _PaperBlurParams.w);
                if (radius < 0.025) return original;

                static const float2 directions[8] =
                {
                    float2(1, 0), float2(-1, 0), float2(0, 1), float2(0, -1),
                    float2(.70710678, .70710678), float2(-.70710678, .70710678),
                    float2(.70710678, -.70710678), float2(-.70710678, -.70710678)
                };
                half4 accumulated = original * .06;
                float weightTotal = .06;
                float2 halfPixel = _PaperPixelParams.xy * .5;
                float2 minUV = _BlitScaleBias.zw + halfPixel;
                float2 maxUV = _BlitScaleBias.zw + _BlitScaleBias.xy - halfPixel;

                [unroll] for (int ring = 0; ring < 2; ring++)
                {
                    float ringRadius = radius * (ring == 0 ? .5 : 1);
                    float ringWeight = ring == 0 ? .08 : .0375;
                    [unroll] for (int tap = 0; tap < 8; tap++)
                    {
                        float2 tapUV = clamp(uv + directions[tap] * ringRadius * _PaperPixelParams.xy,
                            minUV, maxUV);
                        float sampleDepth = EyeDepth(tapUV);
                        float depthWeight = saturate(1 - abs(sampleDepth - depth) / _PaperFocusParams.w);
                        float weight = ringWeight * depthWeight;
                        accumulated += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, tapUV) * weight;
                        weightTotal += weight;
                    }
                }
                return accumulated / max(weightTotal, .0001);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
