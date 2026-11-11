using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Palworks.Projection;

namespace Palworks.Editor
{
    public static class ProjectionSoftnessReview
    {
        public static void Install()
        {
            var mask=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Projection/ProjectionMask.shader");
            var blur=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Projection/ProjectionBlur.shader");
            if(mask==null || blur==null) throw new InvalidOperationException("Projection shaders must finish importing.");
            foreach(var guid in AssetDatabase.FindAssets("t:ScriptableRendererData",new[]{"Assets/Settings"}))
            {
                var renderer=AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                var feature=renderer.rendererFeatures.OfType<ProjectionSoftShadowFeature>().FirstOrDefault();
                if(feature==null)
                {
                    feature=ScriptableObject.CreateInstance<ProjectionSoftShadowFeature>();
                    feature.name="ProjectionSoftShadows";
                    AssetDatabase.AddObjectToAsset(feature,renderer); renderer.rendererFeatures.Add(feature);
                }
                feature.maskShader=mask; feature.blurShader=blur;
                feature.SetActive(true); feature.Create();
                EditorUtility.SetDirty(feature); renderer.SetDirty(); EditorUtility.SetDirty(renderer);
            }
            AssetDatabase.SaveAssets();
        }

        public static void Validate()
        {
            foreach(string shaderName in new[]{"Hidden/Palworks/ProjectionMask","Hidden/Palworks/ProjectionBlur"})
            {
                var shader=Shader.Find(shaderName);
                if(shader==null) throw new Exception("Missing shader "+shaderName);
                if(ShaderUtil.ShaderHasError(shader))
                    throw new Exception(shaderName+": "+string.Join("\n",ShaderUtil.GetShaderMessages(shader).Select(m=>m.message)));
            }
            var projectors=UnityEngine.Object.FindObjectsOfType<PaperProjector>();
            if(projectors.Length==0) throw new Exception("No projectors found.");
            var marker=GameObject.CreatePrimitive(PrimitiveType.Quad);
            // FindObjectsOfType deliberately excludes DontSave objects; the custom
            // caster pass must be able to see this synchronous temporary marker.
            marker.name="ProjectionSoftnessValidation"; marker.hideFlags=HideFlags.HideInHierarchy; marker.layer=31;
            UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
            var material=new Material(Shader.Find("Universal Render Pipeline/Unlit")) {hideFlags=HideFlags.HideAndDontSave};
            marker.GetComponent<Renderer>().sharedMaterial=material;
            var report=new StringBuilder("Result=PASS\nMethod=Actual GPU alpha profiles; equal projected marker sizes\n");
            try
            {
                foreach(var projector in projectors)
                {
                    int layers=projector.casterLayers; bool physical=projector.physicalSoftness;
                    try
                    {
                        projector.casterLayers=1<<31; projector.physicalSoftness=true;
                        marker.transform.rotation=projector.screen.rotation;
                        int sharp=0,soft=0;
                        foreach(float fraction in new[]{.9f,.35f})
                        {
                            marker.transform.position=Vector3.Lerp(projector.lens.position,projector.screen.position,fraction);
                            marker.transform.localScale=Vector3.one*.7f*fraction;
                            projector.RenderPreview();
                            string label=projector.transform.parent.name+(fraction>.5f?"-near-cloth":"-far-cloth");
                            DumpIntermediate(ProjectionSoftShadowFeature.DiagnosticMask,label+"-mask");
                            DumpIntermediate(ProjectionSoftShadowFeature.DiagnosticHorizontal,label+"-horizontal");
                            File.WriteAllText("output/scene-review/softness-gpu-state.txt",ProjectionSoftShadowFeature.DiagnosticState??"Feature did not execute");
                            int transition=TransitionPixels(projector.shadowTexture,label);
                            if(fraction>.5f) sharp=transition; else soft=transition;
                        }
                        if(soft<=sharp+2) throw new Exception("Caster distance does not change the GPU penumbra: sharp="+sharp+" far="+soft);
                        report.AppendLine(projector.transform.parent.name+": NearClothTransitionPixels="+sharp+" FarFromClothTransitionPixels="+soft);
                    }
                    finally {projector.casterLayers=layers;projector.physicalSoftness=physical;projector.RefreshProjection();}
                }
                Directory.CreateDirectory("output/scene-review"); File.WriteAllText("output/scene-review/projection-softness-check.txt",report.ToString());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(marker); UnityEngine.Object.DestroyImmediate(material);
                PaperProjector.RenderAllPreviews();
            }
        }

        static int TransitionPixels(RenderTexture target,string label)
        {
            var previous=RenderTexture.active;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false,true);
            try
            {
                RenderTexture.active=target; image.ReadPixels(new Rect(0,0,target.width,target.height),0,0); image.Apply();
                var pixels=image.GetPixels32(); int y=target.height/2,transition=0,opaque=0;
                Directory.CreateDirectory("output/scene-review");
                File.WriteAllBytes("output/scene-review/softness-"+label+"-rgba.png",image.EncodeToPNG());
                var strip=new Texture2D(target.width,80,TextureFormat.RGB24,false,true);
                var stripPixels=new Color32[target.width*80];
                for(int x=0;x<target.width;x++) for(int row=0;row<80;row++)
                {
                    byte shade=(byte)(245-pixels[y*target.width+x].a*.8f);
                    stripPixels[row*target.width+x]=new Color32(shade,shade,shade,255);
                }
                strip.SetPixels32(stripPixels);strip.Apply();
                File.WriteAllBytes("output/scene-review/softness-"+label+"-profile.png",strip.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(strip);
                for(int x=0;x<target.width;x++)
                {
                    byte alpha=pixels[y*target.width+x].a;
                    if(alpha>10 && alpha<245) transition++;
                    if(alpha>=245) opaque++;
                }
                if(pixels[0].a>2 || opaque<10)
                {
                    Directory.CreateDirectory("output/scene-review");
                    File.WriteAllBytes("output/scene-review/projection-alpha-debug.png",image.EncodeToPNG());
                    throw new Exception("Projection alpha is empty or opaque: corner="+pixels[0].a+" opaque="+opaque+" transition="+transition);
                }
                return transition;
            }
            finally {RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
        }

        static void DumpIntermediate(RenderTexture target,string label)
        {
            Directory.CreateDirectory("output/scene-review");
            if(target==null) {File.WriteAllText("output/scene-review/"+label+".txt","No intermediate target");return;}
            var old=RenderTexture.active;
            var texture=new Texture2D(target.width,target.height,TextureFormat.RGBAFloat,false,true);
            try
            {
                RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();
                var pixels=texture.GetPixels();
                float min=1e9f,max=-1e9f; int count=0;float radius=0;
                foreach(var pixel in pixels) {min=Mathf.Min(min,pixel.a);max=Mathf.Max(max,pixel.a);if(pixel.a>.01f){count++;radius=Mathf.Max(radius,pixel.r);}}
                File.WriteAllText("output/scene-review/"+label+".txt","AlphaMin="+min+" AlphaMax="+max+" Nonzero="+count+" RadiusMax="+radius);
                for(int i=0;i<pixels.Length;i++) pixels[i]=new Color(pixels[i].a,pixels[i].a,pixels[i].a,1);
                var picture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false,true);
                picture.SetPixels(pixels);picture.Apply();File.WriteAllBytes("output/scene-review/"+label+".png",picture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(picture);
            }
            finally {RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(texture);}
        }
    }
}
