using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Palworks.Character;
using Palworks.Performance;
using Palworks.Projection;

namespace Palworks.Editor
{
    // Regression evidence only. This never writes scene objects or game artwork.
    public static class ProjectionCharacterQualityReview
    {
        const string Output="output/scene-review/projection-character";
        sealed class MeshState
        {
            public Mesh mesh;
            public Vector3[] vertices,normals;
            public Vector2[] uv;
            public int[] triangles;
            public Color[] colors;
            public Bounds bounds;
            public MeshState(Mesh value)
            {mesh=value;vertices=value.vertices;normals=value.normals;uv=value.uv;triangles=value.triangles;colors=value.colors;bounds=value.bounds;}
            public void Restore()
            {
                mesh.Clear();mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles;
                if(normals.Length==vertices.Length)mesh.normals=normals;
                if(colors.Length==vertices.Length)mesh.colors=colors;
                mesh.bounds=bounds;
            }
        }
        sealed class RendererState
        {
            public Renderer renderer;
            public bool enabled;
            public int layer,sortingLayer,sortingOrder;
            public Material[] materials;
            public MaterialPropertyBlock block;
            public RendererState(Renderer value)
            {
                renderer=value;enabled=value.enabled;layer=value.gameObject.layer;
                sortingLayer=value.sortingLayerID;sortingOrder=value.sortingOrder;
                materials=value.sharedMaterials;block=new MaterialPropertyBlock();value.GetPropertyBlock(block);
            }
            public void Restore()
            {
                renderer.enabled=enabled;renderer.gameObject.layer=layer;renderer.sortingLayerID=sortingLayer;
                renderer.sortingOrder=sortingOrder;renderer.sharedMaterials=materials;renderer.SetPropertyBlock(block);
            }
        }

        public static void Validate()
        {
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("Character projection review requires Play mode to protect authored meshes.");
            var sequence=UnityEngine.Object.FindObjectOfType<PerformanceSequence>();
            if(sequence!=null && sequence.CurrentMode!=PerformanceSequence.Mode.Reality)
                throw new InvalidOperationException("Return to reality before character projection review.");
            var player=UnityEngine.Object.FindObjectOfType<PuppetPlayerController>();
            var projector=UnityEngine.Object.FindObjectsOfType<PaperProjector>().OrderBy(p=>p.screen.position.x).FirstOrDefault();
            if(player==null || player.puppet==null || player.visuals==null || projector==null)
                throw new InvalidOperationException("Missing live player or projector.");
            var puppet=player.puppet;
            var renderers=player.visuals.GetComponentsInChildren<Renderer>(true);
            var actorSet=new HashSet<Renderer>(renderers);
            if(UnityEngine.Object.FindObjectsOfType<Renderer>().Any(r=>r.gameObject.layer==31 && !actorSet.Contains(r)))
                throw new InvalidOperationException("Temporary review layer 31 is already in use.");
            var meshes=puppet.partMeshes.Where(m=>m!=null).Select(m=>m.sharedMesh).Distinct().Select(m=>new MeshState(m)).ToArray();
            var states=renderers.Select(r=>new RendererState(r)).ToArray();
            var motor=player.GetComponent<CharacterController>();
            bool playerEnabled=player.enabled,puppetEnabled=puppet.enabled,motorEnabled=motor!=null && motor.enabled;
            Vector3 playerPosition=player.transform.position,visualPosition=player.visuals.localPosition,visualScale=player.visuals.localScale;
            Quaternion playerRotation=player.transform.rotation,visualRotation=player.visuals.localRotation;
            var camera=Camera.main;
            Vector3 cameraPosition=camera!=null?camera.transform.position:Vector3.zero;
            Quaternion cameraRotation=camera!=null?camera.transform.rotation:Quaternion.identity;
            float radius=projector.lightRadius;bool physical=projector.physicalSoftness;int layers=projector.casterLayers;
            var report=new StringBuilder("Result=PASS\nMethod=Actual actor poses; hard-reference boundary erosion by full geometric penumbra support\n");
            int checks=0;
            try
            {
                Directory.CreateDirectory(Output);
                player.enabled=false;puppet.enabled=false;if(motor!=null)motor.enabled=false;
                foreach(var renderer in renderers)renderer.gameObject.layer=31;
                projector.casterLayers=1<<31;projector.physicalSoftness=true;
                float lightDistance=Vector3.Dot(projector.screen.position-projector.lens.position,projector.screen.forward);
                float emitterRadius=Mathf.Max(.065f,radius);
                string[] clips={"Idle","Walk","Walk","Walk","Walk"};int[] frames={0,0,6,12,18};
                foreach(float fraction in new[]{.28f,.50f,.85f})
                {
                    Vector3 target=projector.lens.position+projector.screen.forward*(lightDistance*fraction);
                    // Keep the authored ground height and card orientation; only the review position changes.
                    target.y=playerPosition.y;target.x=projector.screen.position.x;
                    player.transform.position=target;
                    Color32[] firstWalk=null;
                    for(int pose=0;pose<clips.Length;pose++)
                    {
                        puppet.ApplyFrame(clips[pose],frames[pose]);
                        string label="depth"+Mathf.RoundToInt(fraction*100)+"-"+clips[pose]+frames[pose].ToString("D2");
                        projector.lightRadius=0;projector.RenderPreview();
                        var hard=Read(projector.shadowTexture,label+"-hard");
                        projector.lightRadius=emitterRadius;projector.RenderPreview();
                        var soft=Read(projector.shadowTexture,label+"-soft");
                        int width=projector.shadowTexture.width,height=projector.shadowTexture.height;
                        float minimumDepth=MinimumDepth(renderers,projector);
                        float pixelsPerUnit=Mathf.Max(width/(projector.screenSize.x*Mathf.Abs(projector.screen.lossyScale.x)),
                            height/(projector.screenSize.y*Mathf.Abs(projector.screen.lossyScale.y)));
                        int support=Mathf.CeilToInt(emitterRadius*Mathf.Max(0,lightDistance/Mathf.Max(.08f,minimumDepth)-1)*pixelsPerUnit)+3;
                        var inside=DistanceFromBoundary(hard,width,height,true);
                        var outside=DistanceFromBoundary(hard,width,height,false);
                        int cores=0,badCores=0,clear=0,badClear=0;byte minCore=255,maxClear=0;
                        for(int p=0;p<soft.Length;p++)
                        {
                            if(inside[p]>support)
                            {cores++;minCore=(byte)Mathf.Min(minCore,soft[p].a);if(soft[p].a<248)badCores++;}
                            if(outside[p]>support)
                            {clear++;maxClear=(byte)Mathf.Max(maxClear,soft[p].a);if(soft[p].a>7)badClear++;}
                        }
                        report.AppendLine(label+": SupportPixels="+support+" CorePixels="+cores+" CoreMinAlpha="+minCore+" LeakingCorePixels="+badCores+" ClearPixels="+clear+" ExteriorMaxAlpha="+maxClear+" ExteriorLeaks="+badClear);
                        if(cores<20 || clear<1000)throw new Exception(label+" did not produce measurable opaque/clear regions.");
                        if(badCores>0 || badClear>0)throw new Exception(label+" violates finite-light coverage: opaque core leaks="+badCores+", exterior leaks="+badClear);
                        if(pose==1)firstWalk=soft;
                        if(pose==2 && firstWalk!=null)
                        {
                            long difference=0;for(int p=0;p<soft.Length;p++)difference+=Math.Abs(soft[p].a-firstWalk[p].a);
                            report.AppendLine(label+": PoseChangeAlphaDifference="+difference);
                            if(difference<10000)throw new Exception("Projection cache did not update after a mesh-only animation change.");
                        }
                        checks++;
                    }
                }
                report.AppendLine("ValidatedPoseDepthPairs="+checks);
                File.WriteAllText(Output+"/quality-check.txt",report.ToString());
            }
            catch(Exception ex)
            {
                File.WriteAllText(Output+"/quality-check.txt",report.ToString().Replace("Result=PASS","Result=FAIL")+"\nFailure="+ex.Message);
                throw;
            }
            finally
            {
                foreach(var mesh in meshes)mesh.Restore();foreach(var state in states)state.Restore();
                player.transform.SetPositionAndRotation(playerPosition,playerRotation);
                player.visuals.localPosition=visualPosition;player.visuals.localRotation=visualRotation;player.visuals.localScale=visualScale;
                if(motor!=null)motor.enabled=motorEnabled;
                player.enabled=playerEnabled;puppet.enabled=puppetEnabled;
                projector.lightRadius=radius;projector.physicalSoftness=physical;projector.casterLayers=layers;
                if(camera!=null)camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);
                PaperProjector.RenderAllPreviews();
            }
        }

        static float MinimumDepth(Renderer[] renderers,PaperProjector projector)
        {
            float minimum=float.PositiveInfinity;
            foreach(var renderer in renderers)
            {
                if(!renderer.enabled || !renderer.gameObject.activeInHierarchy)continue;
                var bounds=renderer.bounds;
                foreach(float x in new[]{-1f,1f})foreach(float y in new[]{-1f,1f})foreach(float z in new[]{-1f,1f})
                {
                    var point=bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z));
                    minimum=Mathf.Min(minimum,Vector3.Dot(point-projector.lens.position,projector.screen.forward));
                }
            }
            return minimum;
        }

        static Color32[] Read(RenderTexture texture,string name)
        {
            var old=RenderTexture.active;var image=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false,true);
            try
            {
                RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();
                var pixels=image.GetPixels32();File.WriteAllBytes(Output+"/"+name+"-rgba.png",image.EncodeToPNG());
                var display=new Color32[pixels.Length];
                for(int i=0;i<pixels.Length;i++)
                {
                    byte value=(byte)(238-pixels[i].a*.79f);display[i]=new Color32(value,value,value,255);
                }
                image.SetPixels32(display);image.Apply();File.WriteAllBytes(Output+"/"+name+".png",image.EncodeToPNG());
                return pixels;
            }
            finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);}
        }

        // Eight-neighbor distance is a conservative lower bound on Euclidean
        // distance. Inner leg/arm holes are boundary seeds, never filled in.
        static int[] DistanceFromBoundary(Color32[] pixels,int width,int height,bool opaque)
        {
            const int infinity=100000;
            var distance=new int[pixels.Length];
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                int p=y*width+x;bool inside=opaque?pixels[p].a>=250:pixels[p].a<=4;
                distance[p]=inside?Mathf.Min(infinity,Mathf.Min(Mathf.Min(x+1,width-x),Mathf.Min(y+1,height-y))):0;
            }
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                int p=y*width+x,value=distance[p];
                if(x>0)value=Mathf.Min(value,distance[p-1]+1);
                if(y>0){value=Mathf.Min(value,distance[p-width]+1);if(x>0)value=Mathf.Min(value,distance[p-width-1]+1);if(x+1<width)value=Mathf.Min(value,distance[p-width+1]+1);}
                distance[p]=value;
            }
            for(int y=height-1;y>=0;y--)for(int x=width-1;x>=0;x--)
            {
                int p=y*width+x,value=distance[p];
                if(x+1<width)value=Mathf.Min(value,distance[p+1]+1);
                if(y+1<height){value=Mathf.Min(value,distance[p+width]+1);if(x>0)value=Mathf.Min(value,distance[p+width-1]+1);if(x+1<width)value=Mathf.Min(value,distance[p+width+1]+1);}
                distance[p]=value;
            }
            return distance;
        }
    }
}
