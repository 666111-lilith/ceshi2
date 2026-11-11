using System;
using System.IO;
using System.Text;
using UnityEngine;
using Palworks.Projection;

namespace Palworks.Editor
{
    public static class ProjectionReview
    {
        struct MaskBounds
        {
            public int pixels;
            public float width,height,centerX,centerY;
        }

        public static void Validate()
        {
            var projectors=UnityEngine.Object.FindObjectsOfType<PaperProjector>();
            if(projectors.Length!=3) throw new Exception("Expected three projector stations.");
            var marker=GameObject.CreatePrimitive(PrimitiveType.Quad);
            marker.name="ProjectionValidation"; marker.hideFlags=HideFlags.HideInHierarchy;
            marker.layer=31;
            UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
            var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.hideFlags=HideFlags.HideAndDontSave;
            material.SetColor("_BaseColor",Color.white);
            marker.GetComponent<Renderer>().sharedMaterial=material;
            marker.transform.localScale=Vector3.one*.35f;
            var report=new StringBuilder("Result=PASS\n");
            try
            {
                foreach(var projector in projectors)
                {
                    int layers=projector.casterLayers;
                    try
                    {
                        projector.casterLayers=1<<31;
                        var distance=Vector3.Dot(projector.screen.position-projector.lens.position,projector.screen.forward);
                        marker.transform.rotation=projector.screen.rotation;
                        MaskBounds near=new MaskBounds(),far=new MaskBounds();
                        foreach(float fraction in new[]{.38f,.72f})
                        {
                            marker.transform.position=projector.lens.position+
                                projector.screen.forward*(distance*fraction)+projector.screen.right*.4f+projector.screen.up*1.45f;
                            projector.RenderPreview();
                            var bounds=Read(projector.shadowTexture);
                            Vector3 viewport=projector.projectionCamera.WorldToViewportPoint(marker.transform.position);
                            float expectedWidth=.35f/fraction/projector.screenSize.x*projector.shadowTexture.width;
                            float expectedHeight=.35f/fraction/projector.screenSize.y*projector.shadowTexture.height;
                            if(Mathf.Abs(bounds.width-expectedWidth)>3 || Mathf.Abs(bounds.height-expectedHeight)>3)
                                throw new Exception("Projection scale is wrong: "+projector.transform.parent.name);
                            if(Mathf.Abs(bounds.centerX/projector.shadowTexture.width-viewport.x)>.006f ||
                                Mathf.Abs(bounds.centerY/projector.shadowTexture.height-viewport.y)>.006f)
                                throw new Exception("Projection texture orientation or offset is wrong: "+projector.transform.parent.name);
                            if(fraction<.5f) near=bounds; else far=bounds;
                        }
                        float ratio=near.width/far.width;
                        if(ratio<1.8f || ratio>2.0f) throw new Exception("Moving nearer the lens does not enlarge the shadow correctly.");
                        report.AppendLine(projector.transform.parent.name+": Alpha=PASS Orientation=PASS NearFarWidthRatio="+ratio.ToString("F3"));
                    }
                    finally { projector.casterLayers=layers; projector.RefreshProjection(); }
                }
                Directory.CreateDirectory("output/scene-review");
                File.WriteAllText("output/scene-review/projection-check.txt",report.ToString());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(marker);
                UnityEngine.Object.DestroyImmediate(material);
                PaperProjector.RenderAllPreviews();
            }
        }

        static MaskBounds Read(RenderTexture target)
        {
            var active=RenderTexture.active;
            var texture=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false,true);
            try
            {
                RenderTexture.active=target;
                texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0); texture.Apply();
                var pixels=texture.GetPixels32();
                int left=target.width,right=-1,bottom=target.height,top=-1,count=0;
                for(int y=0;y<target.height;y++) for(int x=0;x<target.width;x++)
                {
                    if(pixels[y*target.width+x].a<128) continue;
                    count++; left=Mathf.Min(left,x); right=Mathf.Max(right,x);
                    bottom=Mathf.Min(bottom,y); top=Mathf.Max(top,y);
                }
                if(count<10 || count>pixels.Length*.1f) throw new Exception("Projection RenderTexture alpha is empty or opaque across its background.");
                return new MaskBounds { pixels=count,width=right-left+1,height=top-bottom+1,
                    centerX=(left+right+1)*.5f,centerY=(bottom+top+1)*.5f };
            }
            finally { RenderTexture.active=active; UnityEngine.Object.DestroyImmediate(texture); }
        }
    }
}
