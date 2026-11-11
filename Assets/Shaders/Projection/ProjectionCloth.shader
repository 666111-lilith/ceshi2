Shader "Palworks/ProjectionCloth"
{
    Properties
    {
        _ShadowMap("Projection",2D)="black"{}
        _KeyShape("Colored Cutout",2D)="black"{}
        _GroundLine("Stage Floor",Float)=.02
        _BaseColor("Cloth Color",Color)=(.92,.91,.85,1)
        _ShadowColor("Shadow Color",Color)=(.13,.15,.17,1)
        _ShadowOpacity("Shadow Opacity",Range(0,1))=.84
        _PaperGrain("Paper Grain",2D)="gray"{}
        _GrainStrength("Grain Strength",Range(0,.1))=.025
        _LampCenter("Lamp Center",Vector)=(.5,.52,0,0)
        _LampFalloff("Lamp Falloff",Range(0,1))=.6
        _LampColor("Lamp Light",Color)=(1,.81,.48,1)
        _AmbientColor("Cloth Ambient",Color)=(.24,.21,.18,1)
        _EdgeSoftness("Edge Softness",Range(0,3))=.65
        [HideInInspector] _PhysicalSoftness("Physical Softness",Float)=0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        ZWrite On
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_ShadowMap); SAMPLER(sampler_ShadowMap);
        float4 _ShadowMap_TexelSize;
        TEXTURE2D(_KeyShape); SAMPLER(sampler_KeyShape); float4 _KeyShape_TexelSize;
        TEXTURE2D(_PaperGrain); SAMPLER(sampler_PaperGrain);
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _ShadowColor;
            half _ShadowOpacity;
            half _EdgeSoftness;
            half _PhysicalSoftness;
            float4 _KeyUVX, _KeyUVY, _ClothSize;float4 _KeyRect;half4 _KeyPigment;float _KeyVisible;float _GroundLine;
            float4 _LampCenter; half _LampFalloff; half4 _LampColor; half4 _AmbientColor;
            float4 _ProjectorPosition; float4 _ProjectorDirection; half4 _ProjectorLightColor; float _ProjectorIlluminance; half _GrainStrength;
        CBUFFER_END
        struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float3 normalOS:NORMAL; };
        struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 normalWS:TEXCOORD1; float3 positionWS:TEXCOORD2; float2 projectionUV:TEXCOORD3; };
        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
            output.uv=input.uv;
            output.projectionUV=input.positionOS.xy/max(_ClothSize.xy,float2(.001,.001))+.5;
            output.positionWS=TransformObjectToWorld(input.positionOS.xyz);
            output.normalWS=TransformObjectToWorldNormal(input.normalOS);
            return output;
        }
        half4 Frag(Varyings input):SV_Target
        {
            float2 uv=input.projectionUV;float2 clothUV=input.uv;
            half4 layers=SAMPLE_TEXTURE2D(_ShadowMap,sampler_ShadowMap,uv);
            // Each channel is a complete light-integrated silhouette union.
            // Translucent scenery transmits light; opaque playable shadows do not.
            half transmission=(1-saturate(layers.r))*(1-saturate(layers.g))*(1-saturate(layers.a*_ShadowOpacity));
            float3 ray=input.positionWS-_ProjectorPosition.xyz;
            float distanceSquared=max(dot(ray,ray),.1);
            float3 direction=ray*rsqrt(distanceSquared);
            float incidence=abs(dot(normalize(input.normalWS),direction));
            float beam=pow(saturate(dot(direction,_ProjectorDirection.xyz)),3);
            float lamp=_ProjectorIlluminance*incidence*beam/distanceSquared;
            float edge=min(min(uv.x,1-uv.x),min(uv.y,1-uv.y));
            float hem=lerp(.85,1,smoothstep(0,.025,edge));
            float folds=1-.022*(.5+.5*sin(clothUV.x*56+sin(clothUV.y*7)));
            float grain=1+(SAMPLE_TEXTURE2D(_PaperGrain,sampler_PaperGrain,clothUV*float2(17,8)).r-.5)*_GrainStrength;
            half3 ambient=max(SampleSH(-normalize(input.normalWS)),half3(.02,.02,.02));
            half3 illuminated=_BaseColor.rgb*(ambient+_ProjectorLightColor.rgb*lamp)*hem*folds*grain;
            half3 ink=_ShadowColor.rgb*(ambient+.08*_ProjectorLightColor.rgb*lamp);
            transmission*=smoothstep(_GroundLine-_ShadowMap_TexelSize.y*.5,_GroundLine+_ShadowMap_TexelSize.y*.5,uv.y);
            half3 result=lerp(ink,illuminated,saturate(transmission));
            float2 keyUV=float2(dot(float3(uv,1),_KeyUVX.xyz),dot(float3(uv,1),_KeyUVY.xyz));
            float inside=step(0,keyUV.x)*step(keyUV.x,1)*step(0,keyUV.y)*step(keyUV.y,1)*_KeyVisible;
            float2 texel=_KeyShape_TexelSize.xy*.65;
            half4 keySample=SAMPLE_TEXTURE2D(_KeyShape,sampler_KeyShape,keyUV);
            half key=keySample.a*.5;
            key+=(SAMPLE_TEXTURE2D(_KeyShape,sampler_KeyShape,keyUV+float2(texel.x,0)).a+SAMPLE_TEXTURE2D(_KeyShape,sampler_KeyShape,keyUV-float2(texel.x,0)).a+SAMPLE_TEXTURE2D(_KeyShape,sampler_KeyShape,keyUV+float2(0,texel.y)).a+SAMPLE_TEXTURE2D(_KeyShape,sampler_KeyShape,keyUV-float2(0,texel.y)).a)*.125;
            // Colored paper filters this same illuminated cloth; it is not an overlay icon.
            half3 transmitted=illuminated*(.14+_KeyPigment.rgb*.86)*(.95+.05*grain);
            half paperTone=smoothstep(.045,.19,dot(keySample.rgb,half3(.2126,.7152,.0722)));
            half3 cutout=lerp(ink,transmitted,paperTone);
            result=lerp(result,lerp(ink,cutout,saturate(transmission)),saturate(key*inside*.96));
            return half4(result,1);
        }
        half4 Normals(Varyings input, FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
        {
            float3 normal=normalize(input.normalWS)*IS_FRONT_VFACE(face,1.0,-1.0);
            #if defined(_GBUFFER_NORMALS_OCT)
                return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(normal)*.5+.5)),0);
            #else
                return half4(normal,0);
            #endif
        }
        half Depth(Varyings input):SV_Target { return input.positionCS.z; }
        ENDHLSL
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Depth
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Normals
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
}
