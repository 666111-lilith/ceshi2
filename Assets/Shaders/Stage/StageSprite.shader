Shader "Palworks/StageSprite"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite",2D)="white"{}
        _BaseColor("Color",Color)=(1,1,1,1)
        _Neutral("Neutral Colors",Range(0,1))=0
        [HideInInspector] _TransitionOpacity("Transition Opacity",Float)=1
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="True"}
        Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Tags {"LightMode"="SRPDefaultUnlit"}
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Neutral;
                half _TransitionOpacity;
            CBUFFER_END
            struct A {float4 positionOS:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
            struct V {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
            V Vert(A i) {V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;o.color=i.color;return o;}
            half4 Frag(V i):SV_Target
            {
                half4 source=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv);
                half gray=.15+floor(saturate(dot(source.rgb,half3(.299,.587,.114)))*4+.5)*.17;
                return half4(lerp(source.rgb,gray.xxx,_Neutral)*_BaseColor.rgb*i.color.rgb,source.a*_BaseColor.a*i.color.a*_TransitionOpacity);
            }
            ENDHLSL
        }
    }
}
