using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Palworks.Projection
{
    public sealed class ProjectionSoftShadowFeature : ScriptableRendererFeature
    {
        public Shader maskShader, blurShader;
        SoftPass pass;
        public static RenderTexture DiagnosticMask { get; private set; }
        public static RenderTexture DiagnosticHorizontal { get; private set; }
        public static string DiagnosticState { get; private set; }
        public override void Create()
        {
            pass?.Dispose();
            pass=new SoftPass(maskShader!=null?maskShader:Shader.Find("Hidden/Palworks/ProjectionMask"),
                blurShader!=null?blurShader:Shader.Find("Hidden/Palworks/ProjectionBlur"));
        }
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data)
        {
            if(pass==null)return;
            foreach(var source in Object.FindObjectsOfType<PaperProjector>())
                if(source.isActiveAndEnabled && source.physicalSoftness && source.projectionCamera==data.cameraData.camera)
                {pass.projector=source;renderer.EnqueuePass(pass);break;}
        }
        protected override void Dispose(bool disposing){pass?.Dispose();}
        sealed class SoftPass : ScriptableRenderPass
        {
            public PaperProjector projector;
            readonly Shader maskShader;
            readonly Material integration;
            readonly Dictionary<int,Material> masks=new Dictionary<int,Material>();
            readonly Dictionary<int,RTHandle> outputs=new Dictionary<int,RTHandle>();
            readonly Dictionary<int,int> cachedStates=new Dictionary<int,int>();
            readonly List<Renderer> casters=new List<Renderer>();
            readonly List<Material> casterMaterials=new List<Material>();
            readonly List<float> casterSoftness=new List<float>();
            readonly List<Vector3> vertices=new List<Vector3>();
            readonly List<Vector2> uvs=new List<Vector2>();
            readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
            RTHandle mask,horizontal;
            public SoftPass(Shader shape,Shader accumulate)
            {
                maskShader=shape;
                if(accumulate!=null)integration=CoreUtils.CreateEngineMaterial(accumulate);
                renderPassEvent=RenderPassEvent.AfterRenderingTransparents;
            }
            public override void OnCameraSetup(CommandBuffer cmd,ref RenderingData data)
            {
                if(projector==null || projector.shadowTexture==null)return;
                var descriptor=new RenderTextureDescriptor(projector.shadowTexture.width,projector.shadowTexture.height,RenderTextureFormat.ARGBHalf,0);
                descriptor.msaaSamples=1;descriptor.sRGB=false;descriptor.useMipMap=false;descriptor.autoGenerateMips=false;
                RenderingUtils.ReAllocateIfNeeded(ref horizontal,descriptor,FilterMode.Bilinear,TextureWrapMode.Clamp,name:"ProjectionAreaIntegration");
                int scale=Mathf.Clamp(projector.silhouetteSupersampling,1,3);
                descriptor.width*=scale;descriptor.height*=scale;
                RenderingUtils.ReAllocateIfNeeded(ref mask,descriptor,FilterMode.Bilinear,TextureWrapMode.Clamp,name:"ProjectionSilhouetteUnion");
            }
            static int Mix(int hash,int value){unchecked{return hash*16777619^value;}}
            static int Mix(int hash,float value)=>Mix(hash,value.GetHashCode());
            static int Mix(int hash,Matrix4x4 value){for(int i=0;i<16;i++)hash=Mix(hash,value[i]);return hash;}
            int Gather(Camera camera,out float largestPenumbra)
            {
                casters.Clear();casterMaterials.Clear();casterSoftness.Clear();largestPenumbra=0;
                int hash=Mix(17,projector.casterLayers.value);
                hash=Mix(hash,projector.renderRevision);
                hash=Mix(hash,projector.shadowTexture.GetNativeTexturePtr().GetHashCode());
                hash=Mix(hash,camera.worldToCameraMatrix);hash=Mix(hash,camera.projectionMatrix);
                hash=Mix(hash,projector.lightRadius);hash=Mix(hash,projector.edgeSoftness);
                hash=Mix(hash,projector.lightSamples);hash=Mix(hash,projector.silhouetteSupersampling);
                hash=Mix(hash,projector.shadowTexture.GetInstanceID());hash=Mix(hash,projector.shadowTexture.width);hash=Mix(hash,projector.shadowTexture.height);
                var planes=GeometryUtility.CalculateFrustumPlanes(camera);
                foreach(var renderer in Object.FindObjectsOfType<Renderer>())
                {
                    if(!renderer.enabled || !renderer.gameObject.activeInHierarchy || (projector.casterLayers.value&(1<<renderer.gameObject.layer))==0)continue;
                    var bounds=renderer.bounds;bounds.Expand(projector.lightRadius*2);
                    if(!GeometryUtility.TestPlanesAABB(planes,bounds))continue;
                    var original=renderer.sharedMaterial;if(original==null)continue;
                    Texture shape=null;
                    float alpha=original.HasProperty("_BaseColor")?original.GetColor("_BaseColor").a:1;
                    if(renderer is SpriteRenderer sprite && sprite.sprite!=null)
                    {
                        shape=sprite.sprite.texture;alpha*=sprite.color.a;
                        hash=Mix(hash,sprite.sprite.GetInstanceID());hash=Mix(hash,sprite.flipX?1:0);hash=Mix(hash,sprite.flipY?1:0);
                    }
                    else if(original.HasProperty("_BaseMap"))shape=original.GetTexture("_BaseMap");
                    else if(original.HasProperty("_MainTex"))shape=original.GetTexture("_MainTex");
                    if(shape==null)shape=Texture2D.whiteTexture;
                    var style=renderer.GetComponentInParent<ProjectionCasterStyle>();
                    float softness=style!=null?style.softnessScale:1;
                    if(style!=null)alpha*=style.opacity;
                    var channel=style==null || style.depthLayer==ProjectionCasterStyle.DepthLayer.Solid
                        ? new Vector4(0,0,0,1) : style.depthLayer==ProjectionCasterStyle.DepthLayer.Background
                        ? new Vector4(1,0,0,0) : new Vector4(0,1,0,0);
                    float opacity=original.HasProperty("_Opacity")?original.GetFloat("_Opacity"):1;
                    properties.Clear();renderer.GetPropertyBlock(properties);
                    if(properties.HasFloat("_Opacity"))opacity=properties.GetFloat("_Opacity");
                    int id=renderer.GetInstanceID();
                    if(!masks.TryGetValue(id,out var material)){material=CoreUtils.CreateEngineMaterial(maskShader);masks.Add(id,material);}
                    material.SetTexture("_ProjectionShape",shape);material.SetFloat("_Opacity",opacity);material.SetFloat("_ProjectionAlpha",alpha);
                    material.SetVector("_ProjectionChannel",channel);
                    casters.Add(renderer);casterMaterials.Add(material);
                    casterSoftness.Add(softness);hash=Mix(hash,softness);hash=Mix(hash,channel.GetHashCode());
                    hash=Mix(hash,id);hash=Mix(hash,renderer.localToWorldMatrix);hash=Mix(hash,shape.GetInstanceID());
                    hash=Mix(hash,unchecked((int)shape.updateCount));hash=Mix(hash,opacity);hash=Mix(hash,alpha);
                    Mesh mesh=null;
                    if(renderer is SkinnedMeshRenderer skinned)mesh=skinned.sharedMesh;
                    else if(renderer.TryGetComponent<MeshFilter>(out var filter))mesh=filter.sharedMesh;
                    if(mesh!=null)
                    {
                        hash=Mix(hash,mesh.GetInstanceID());
                        if(mesh.isReadable)
                        {
                            mesh.GetVertices(vertices);foreach(var vertex in vertices)hash=Mix(hash,vertex.GetHashCode());
                            mesh.GetUVs(0,uvs);foreach(var uv in uvs)hash=Mix(hash,uv.GetHashCode());
                        }
                        if(renderer is SkinnedMeshRenderer animated)
                        {
                            foreach(var bone in animated.bones)if(bone!=null)hash=Mix(hash,bone.localToWorldMatrix);
                            for(int i=0;i<mesh.blendShapeCount;i++)hash=Mix(hash,animated.GetBlendShapeWeight(i));
                        }
                    }
                    float depth=Vector3.Dot(renderer.bounds.center-projector.lens.position,projector.screen.forward);
                    largestPenumbra=Mathf.Max(largestPenumbra,projector.PenumbraPixels(depth));
                }
                return hash;
            }
            public override void Execute(ScriptableRenderContext context,ref RenderingData data)
            {
                if(projector==null || integration==null || maskShader==null || mask==null || projector.shadowTexture==null)return;
                var camera=data.cameraData.camera;
                int hash=Gather(camera,out float largestPenumbra),outputId=projector.shadowTexture.GetInstanceID();
                if(cachedStates.TryGetValue(outputId,out int previous) && hash==previous && projector.shadowTexture.IsCreated())return;
                int samples=projector.lightRadius<=.00001f?1:Mathf.Clamp(projector.lightSamples,16,96);
                if(samples>1){samples=Mathf.Min(96,samples+(samples&1));if(largestPenumbra>24)samples=Mathf.Min(96,samples*2);}
                if(!outputs.TryGetValue(outputId,out var output))
                {output=RTHandles.Alloc(new RenderTargetIdentifier(projector.shadowTexture),name:"ProjectionShadowOutput");outputs.Add(outputId,output);}
                var cmd=CommandBufferPool.Get("Projection Area Light");
                try
                {
                    CoreUtils.SetRenderTarget(cmd,horizontal,ClearFlag.Color,Color.clear);
                    cmd.SetGlobalFloat("_ProjectionSampleWeight",1f/samples);
                    Vector3 center=camera.transform.InverseTransformPoint(projector.screen.position);
                    float width=projector.screenSize.x*Mathf.Abs(projector.screen.lossyScale.x),height=projector.screenSize.y*Mathf.Abs(projector.screen.lossyScale.y);
                    float near=camera.nearClipPlane;
                    for(int sampleIndex=0;sampleIndex<samples;sampleIndex++)
                    {
                        int pair=sampleIndex/2;
                        float radius=samples==1?0:Mathf.Sqrt((pair+.5f)/(samples/2f))*projector.lightRadius;
                        float angle=pair*2.39996323f,sign=(sampleIndex&1)==0?1:-1;
                        float x=Mathf.Cos(angle)*radius*sign,y=Mathf.Sin(angle)*radius*sign;
                        Vector3 offset=projector.screen.right*x+projector.screen.up*y;
                        var view=camera.worldToCameraMatrix*Matrix4x4.Translate(-offset);
                        var projection=Matrix4x4.Frustum((center.x-x-width*.5f)*near/center.z,(center.x-x+width*.5f)*near/center.z,
                            (center.y-y-height*.5f)*near/center.z,(center.y-y+height*.5f)*near/center.z,near,camera.farClipPlane);
                        cmd.SetGlobalMatrix("_ProjectionViewProjection",GL.GetGPUProjectionMatrix(projection,true)*view);
                        CoreUtils.SetRenderTarget(cmd,mask,ClearFlag.Color,Color.clear);
                        for(int i=0;i<casters.Count;i++)
                        {
                            float spread=casterSoftness[i];
                            if(spread!=1)
                            {
                                var shifted=camera.worldToCameraMatrix*Matrix4x4.Translate(-offset*spread);
                                var frustum=Matrix4x4.Frustum((center.x-x*spread-width*.5f)*near/center.z,(center.x-x*spread+width*.5f)*near/center.z,
                                    (center.y-y*spread-height*.5f)*near/center.z,(center.y-y*spread+height*.5f)*near/center.z,near,camera.farClipPlane);
                                cmd.SetGlobalMatrix("_ProjectionViewProjection",GL.GetGPUProjectionMatrix(frustum,true)*shifted);
                            }
                            else cmd.SetGlobalMatrix("_ProjectionViewProjection",GL.GetGPUProjectionMatrix(projection,true)*view);
                            cmd.DrawRenderer(casters[i],casterMaterials[i],0,0);
                        }
                        Blitter.BlitCameraTexture(cmd,mask,horizontal,RenderBufferLoadAction.Load,RenderBufferStoreAction.Store,integration,0);
                    }
                    cmd.SetGlobalVector("_ProjectionTexelSize",new Vector4(1f/horizontal.rt.width,1f/horizontal.rt.height,horizontal.rt.width,horizontal.rt.height));
                    cmd.SetGlobalFloat("_ProjectionReconstruction",Mathf.Clamp(projector.edgeSoftness,0,1));
                    Blitter.BlitCameraTexture(cmd,horizontal,output,integration,1);
                    CoreUtils.SetRenderTarget(cmd,data.cameraData.renderer.cameraColorTargetHandle,data.cameraData.renderer.cameraDepthTargetHandle);
                    cmd.SetViewport(camera.pixelRect);context.ExecuteCommandBuffer(cmd);cachedStates[outputId]=hash;
                    DiagnosticMask=mask.rt;DiagnosticHorizontal=horizontal.rt;
                    DiagnosticState="Camera="+camera.name+" Draws="+casters.Count+" Samples="+samples+" Supersampling="+projector.silhouetteSupersampling+
                        " Penumbra="+largestPenumbra.ToString("F2")+" Mask="+mask.rt.graphicsFormat+" Horizontal="+horizontal.rt.graphicsFormat+" Output="+projector.shadowTexture.graphicsFormat;
                }
                finally{CommandBufferPool.Release(cmd);}
            }
            public void Dispose()
            {
                mask?.Release();horizontal?.Release();CoreUtils.Destroy(integration);
                foreach(var material in masks.Values)CoreUtils.Destroy(material);
                foreach(var output in outputs.Values)output.Release();
                masks.Clear();outputs.Clear();cachedStates.Clear();
            }
        }
    }
}
