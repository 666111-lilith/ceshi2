Shader "Hidden/Palworks/ProjectionMask"
{
    Properties { _ProjectionShape("Shape",2D)="white"{} _Opacity("Opacity",Float)=1 _ProjectionAlpha("Alpha",Float)=1 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always Blend One One BlendOp Max
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_ProjectionShape); SAMPLER(sampler_ProjectionShape);
            CBUFFER_START(UnityPerMaterial)
                float _Opacity; float _ProjectionAlpha; float4 _ProjectionChannel;
            CBUFFER_END
            float4x4 _ProjectionViewProjection;
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS=mul(_ProjectionViewProjection,float4(TransformObjectToWorld(input.positionOS.xyz),1));
                output.uv=input.uv;return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float alpha=SAMPLE_TEXTURE2D(_ProjectionShape,sampler_ProjectionShape,input.uv).a;
                float coverage=saturate((alpha-.1)/max(fwidth(alpha),.0001)+.5);
                clip(coverage-.001);
                return _ProjectionChannel*(coverage*saturate(_Opacity*_ProjectionAlpha));
            }
            ENDHLSL
        }
    }
}
