Shader "Hidden/Palworks/ProjectionBlur"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        float _ProjectionSampleWeight;
        float _ProjectionReconstruction;
        float4 _ProjectionTexelSize;
        half4 Accumulate(Varyings input):SV_Target
        {
            float4 coverage=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,input.texcoord);
            return coverage*_ProjectionSampleWeight;
        }
        half4 Reconstruct(Varyings input):SV_Target
        {
            float2 d=_ProjectionTexelSize.xy*_ProjectionReconstruction,uv=input.texcoord;
            float4 alpha=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv)*4;
            alpha+=(SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv+float2(d.x,0))+
                    SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv-float2(d.x,0))+
                    SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv+float2(0,d.y))+
                    SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv-float2(0,d.y)))*2;
            alpha+=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv+d)+
                   SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv-d)+
                   SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv+float2(d.x,-d.y))+
                   SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv+float2(-d.x,d.y));
            return saturate(alpha/16);
        }
        ENDHLSL
        Pass
        {
            Blend One One
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Accumulate
            ENDHLSL
        }
        Pass
        {
            Blend Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Reconstruct
            ENDHLSL
        }
    }
}
