using System.Collections.Generic;
using UnityEngine;
using Palworks.Character;
using Palworks.Projection;

namespace Palworks.Performance
{
    [DisallowMultipleComponent, DefaultExecutionOrder(1600)]
    public sealed class PerformanceStation : MonoBehaviour
    {
        public const int ActorLayer = 10;
        public PaperProjector projector;
        public Transform lever;
        public Transform stageActor;
        public PuppetAnimator stagePuppet;
        public Transform key;
        public Collider[] gates = new Collider[0];
        public Renderer[] gateVisuals = new Renderer[0];
        public Collider[] entranceGates = new Collider[0];
        public Renderer[] entranceGateVisuals = new Renderer[0];
        [Tooltip("Trigger volume covering this station's reality area. Entering it closes the entrance until the key is collected.")]
        public BoxCollider activityArea;
        [Min(0)] public float entryInset = .6f;
        [Min(.1f)] public float movementSpeed = 3.4f;
        [Min(.1f)] public float pickupRadius = .5f;
        [Min(0)] public float horizontalPadding = .5f;
        [Min(0)] public float bottomPadding = .12f;
        [Min(.1f)] public float actorHeadroom = 1.85f;
        [Min(0)] public float actorChestHeight = .85f;
        [Tooltip("Optional source-art handle. Its authored rotation is preserved and restored between sessions.")]
        public Transform leverHandle;
        public float leverPullAngle = -32;
        public Vector3 leverPullAxis=Vector3.forward;
        public ShadowCollisionWorld shadowCollision;
        public bool enableShadowCollision = true;
        public float sprintMultiplier=1.45f, dashMultiplier=2.8f, jumpHeight=1.1f;
        [Min(.1f)] public float gravity=18f;
        Vector2 dashDirection=Vector2.right;
        float dashRemaining, dashCooldown, jumpVelocity;
        bool grounded, motorReady, upHeld;
        float supportSurface;
        bool supportCaptured;
        Vector2 motorFoot;
        readonly Dictionary<Material,Vector2[]> bootContours=new Dictionary<Material,Vector2[]>();
        readonly List<Vector2> soleOffsets=new List<Vector2>();
        readonly List<Vector2> projectedBoots=new List<Vector2>();
        readonly List<Vector3> meshVertices=new List<Vector3>(4);
        MaterialPropertyBlock bootProperties;
        static readonly int OpacityProperty=Shader.PropertyToID("_Opacity");
        public Vector2 ActorCollisionHalfSize => new Vector2(.20f/ScreenScale.x, .68f/ScreenScale.y)*ProjectionScale;
        Vector2 ScreenScale => Screen!=null?new Vector2(Mathf.Max(.001f,Mathf.Abs(Screen.lossyScale.x)),Mathf.Max(.001f,Mathf.Abs(Screen.lossyScale.y))):Vector2.one;
        public float GroundHeight => -ScreenSize.y*.5f+bottomPadding/ScreenScale.y;
        public bool IsGrounded => grounded;
        public float VerticalVelocity => jumpVelocity;
        public float SupportHeight => supportCaptured?supportSurface:float.NaN;
        public Vector2 ActorFootPosition => motorReady?motorFoot:(Vector2)Screen.InverseTransformPoint(ProjectWorldPoint(stageActor.position));
        public float VisualFootHeight => ReadBootFoot().y;
        public float GroundContactGap
        {
            get
            {
                var visual=ReadBootFoot();CollectSoleOffsets();
                float gap=float.PositiveInfinity;
                foreach(var offset in soleOffsets)
                {
                    var sole=visual+offset;
                    gap=Mathf.Min(gap,Mathf.Abs(sole.y-GroundHeight));
                    if(shadowCollision!=null && shadowCollision.TryGetSupportHeight(sole.x,sole.y-.3f,sole.y+.03f,out float height))
                        gap=Mathf.Min(gap,Mathf.Abs(sole.y-height));
                }
                return gap;
            }
        }

        [System.NonSerialized] bool completed;
        [System.NonSerialized] bool entranceArmed;
        [System.NonSerialized] bool handleCaptured;
        Quaternion handleRotation;
        float measuredHalfWidth, measuredHeight;
        float casterDistance;
        Vector2 entryFoot, settledFoot;
        bool animationStarted;
        public bool Completed => completed;
        public bool EntranceArmed => entranceArmed;
        public Transform Screen => projector != null ? projector.screen : null;
        public Vector2 ScreenSize => projector != null ? projector.screenSize : new Vector2(12,5.6f);
        public float CasterDistance => casterDistance;
        public float ProjectionScale => projector!=null && projector.lens!=null && Screen!=null
            ? Vector3.Dot(Screen.position-projector.lens.position,Screen.forward)/Mathf.Max(.08f,casterDistance) : 1;
        public Vector3 ProjectedActorFoot => motorReady?Screen.TransformPoint(new Vector3(motorFoot.x,motorFoot.y,0)):ProjectWorldPoint(stageActor.position);
        public Vector3 ProjectedActorChest => ProjectWorldPoint(stageActor.position+stageActor.up*actorChestHeight);

        public void RefreshGates(bool current)
        {
            SetGates(gates,gateVisuals,!completed);
            SetGates(entranceGates,entranceGateVisuals,!completed && current && entranceArmed);
        }

        public void ArmEntranceIfInside(Vector3 worldPosition)
        {
            if(entranceArmed || completed || activityArea==null) return;
            var bounds=activityArea.bounds;
            bounds.Expand(new Vector3(-Mathf.Min(entryInset*2,bounds.size.x*.8f),0,0));
            if(bounds.Contains(worldPosition)) entranceArmed=true;
        }

        public void PrepareStage(PuppetPlayerController worldPlayer)
        {
            if(Screen==null || projector.lens==null || stageActor==null || stagePuppet==null || key==null ||
                worldPlayer==null || worldPlayer.visuals==null || worldPlayer.puppet==null)
                throw new System.InvalidOperationException("Performance station references are incomplete: "+name);
            CaptureHandle();
            if(leverHandle!=null) leverHandle.localRotation=handleRotation*Quaternion.AngleAxis(leverPullAngle,leverPullAxis);
            var source=worldPlayer.visuals;
            stageActor.SetPositionAndRotation(source.position,source.rotation);
            var parentScale=stageActor.parent!=null?stageActor.parent.lossyScale:Vector3.one;
            var sourceScale=source.lossyScale;
            stageActor.localScale=new Vector3(sourceScale.x/parentScale.x,sourceScale.y/parentScale.y,sourceScale.z/parentScale.z);
            foreach(var child in stageActor.GetComponentsInChildren<Transform>(true)) child.gameObject.layer=ActorLayer;
            stagePuppet.enabled=false;
            stageActor.gameObject.SetActive(true);
            stagePuppet.SetMoving(false);
            CopyRenderedPose(worldPlayer.puppet);
            animationStarted=false;
            casterDistance=Vector3.Dot(stageActor.position-projector.lens.position,Screen.forward);
            MeasureActorEnvelope();
            CacheBootContours();
            entryFoot=ReadBootFoot();
            motorFoot=entryFoot;motorReady=true;
            settledFoot=ClampFoot(entryFoot);
            dashRemaining=0;dashCooldown=0;grounded=false;jumpVelocity=0;upHeld=false;supportCaptured=false;
            if(enableShadowCollision && shadowCollision!=null)
            {
                shadowCollision.Rebuild(projector);
                var low=ClampFoot(new Vector2(-100,-100));var high=ClampFoot(new Vector2(100,100));
                settledFoot=shadowCollision.FindNearestFreeFoot(settledFoot,ActorCollisionHalfSize,Rect.MinMaxRect(low.x,low.y,high.x,high.y));
            }
            key.gameObject.SetActive(true);
        }

        // Keep the exact source silhouette at entry, then gently bring off-screen feet into the playable cloth.
        public void AdvanceEntry(float amount)
        {
            if(stageActor==null || amount<=0 || (entryFoot-settledFoot).sqrMagnitude<.000001f) return;
            SetProjectedFoot(Vector2.Lerp(entryFoot,settledFoot,Mathf.Clamp01(amount)));
        }

        public void HideStage()
        {
            motorReady=false;grounded=false;jumpVelocity=0;
            if(stageActor!=null) stageActor.gameObject.SetActive(false);
            if(stagePuppet!=null) stagePuppet.enabled=false;
            if(key!=null) key.gameObject.SetActive(false);
        }

        public void MoveActor(Vector2 input,float deltaTime,bool sprint=false,bool dash=false,bool jump=false)
        {
            if(Screen==null || stageActor==null || !stageActor.gameObject.activeSelf || deltaTime<=0) return;
            if(!motorReady){CacheBootContours();motorFoot=ReadBootFoot();motorReady=true;}
            float horizontal=Mathf.Clamp(input.x,-1,1);
            bool jumpRequested=jump || (input.y>.5f && !upHeld);
            upHeld=input.y>.5f;
            if(Mathf.Abs(horizontal)>.01f)dashDirection=new Vector2(Mathf.Sign(horizontal),0);
            if(dash && dashCooldown<=0)
            {dashRemaining=.24f;dashCooldown=.7f;stagePuppet?.PlayAction("Dash",.24f);animationStarted=true;}
            bool wasGrounded=grounded;
            StabilizeGroundContact();
            grounded=jumpVelocity<=0 && HasSupport(motorFoot,.012f);
            if(jumpRequested && grounded)
            {jumpVelocity=Mathf.Sqrt(2*gravity*jumpHeight);grounded=false;animationStarted=true;}
            // Bounded substeps keep contact and dash sweeps stable during frame hitches.
            float remaining=Mathf.Min(deltaTime,.25f);
            while(remaining>.000001f)
            {
                float dt=Mathf.Min(remaining,1f/120f);remaining-=dt;
                float axis=dashRemaining>0?dashDirection.x:horizontal;
                float speed=movementSpeed*(dashRemaining>0?dashMultiplier:sprint?sprintMultiplier:1);
                dashCooldown=Mathf.Max(0,dashCooldown-dt);dashRemaining=Mathf.Max(0,dashRemaining-dt);
                Vector2 next=motorFoot;
                float dx=axis*speed*dt/ScreenScale.x;
                // Keep torso volume separate from the transparent gap between the boots.
                var bodyFoot=next+Vector2.up*.06f;
                if(enableShadowCollision && shadowCollision!=null)
                    dx=shadowCollision.Move(bodyFoot,new Vector2(dx,0),ActorCollisionHalfSize).x-bodyFoot.x;
                next.x=ClampFoot(next+new Vector2(dx,0)).x;
                // Preserve contact across tiny remainder substeps and shallow descending
                // surfaces. The contact skin must not turn a standing body airborne.
                if(grounded && jumpVelocity<=0 && TryBodySupport(next,.015f,.06f,out float support))
                {
                    next.y=Mathf.Max(GroundHeight,support+.0005f);
                    supportSurface=support;supportCaptured=true;jumpVelocity=0;motorFoot=next;
                    continue;
                }
                jumpVelocity-=gravity*dt;
                float dy=jumpVelocity*dt/ScreenScale.y;
                if(dy>0)
                {
                    float rise=dy;
                    if(enableShadowCollision && shadowCollision!=null)
                        rise=shadowCollision.MoveVertical(next,dy,ActorCollisionHalfSize);
                    next.y+=rise;
                    float ceiling=ClampFoot(next).y;
                    if(rise<dy-.00001f || ceiling<next.y){jumpVelocity=0;next.y=ceiling;}
                    grounded=false;
                }
                else
                {
                    float fall=ResolveFall(next,dy,true);
                    next.y+=fall;
                    grounded=fall>dy+.000001f;
                    if(next.y<=GroundHeight){next.y=GroundHeight;grounded=true;}
                    if(grounded)jumpVelocity=0;
                }
                motorFoot=next;
            }
            if(Mathf.Abs(horizontal)>.01f)
            {
                var actorScale=stageActor.localScale;
                actorScale.x=Mathf.Abs(actorScale.x)*(horizontal<0?-1:1);
                stageActor.localScale=actorScale;
            }
            if(stagePuppet!=null)
            {
                bool moving=Mathf.Abs(horizontal)>.001f;
                if(moving || !grounded || dashRemaining>0) animationStarted=true;
                if(animationStarted)
                {
                    stagePuppet.SetMotion(moving,sprint);
                    if(!grounded && dashRemaining<=0)stagePuppet.SampleAction(moving?"JumpMove":"JumpIdle",Mathf.Clamp01(.5f-jumpVelocity/(2*Mathf.Sqrt(2*gravity*jumpHeight))));
                    else if(grounded && !wasGrounded)stagePuppet.PlayAction(moving?"LandMove":"LandIdle",.13f);
                    stagePuppet.enabled=true;
                }
            }
            AlignVisualFeet();
            StabilizeGroundContact();
        }

        public bool ActorTouchesKey()
        {
            if(Screen==null || stageActor==null || key==null || !key.gameObject.activeSelf) return false;
            Vector3 offset=key.position-ProjectedActorChest;
            float x=Vector3.Dot(offset,Screen.right), y=Vector3.Dot(offset,Screen.up);
            return x*x+y*y<=pickupRadius*pickupRadius;
        }

        public Vector2 ClampFoot(Vector2 position)
        {
            var size=ScreenSize;
            var scale=Screen!=null?Screen.lossyScale:Vector3.one;
            float horizontal=Mathf.Max(horizontalPadding,measuredHalfWidth*ProjectionScale)/Mathf.Max(.001f,Mathf.Abs(scale.x));
            float top=Mathf.Max(actorHeadroom,measuredHeight)*ProjectionScale/Mathf.Max(.001f,Mathf.Abs(scale.y));
            float minX=Mathf.Min(0,-size.x*.5f+horizontal),maxX=Mathf.Max(0,size.x*.5f-horizontal);
            float minY=GroundHeight,maxY=Mathf.Max(minY,size.y*.5f-top);
            return new Vector2(Mathf.Clamp(position.x,minX,maxX),Mathf.Clamp(position.y,minY,maxY));
        }

        public Vector3 ProjectWorldPoint(Vector3 point)
        {
            if(Screen==null || projector.lens==null) return point;
            Vector3 ray=point-projector.lens.position;
            float depth=Vector3.Dot(ray,Screen.forward);
            float distance=Vector3.Dot(Screen.position-projector.lens.position,Screen.forward);
            return projector.lens.position+ray*(distance/Mathf.Max(.08f,depth));
        }

        void SetProjectedFoot(Vector2 local)
        {
            motorFoot=local;motorReady=true;
            AlignVisualFeet();
        }

        void LateUpdate()
        {
            // PuppetAnimator can change a boot mesh after input is sampled. The opaque
            // soles, rather than transparent bitmap margins, stay on the contact plane.
            if(motorReady && stageActor!=null && stageActor.gameObject.activeInHierarchy)
                AlignVisualFeet();
        }

        // The motor's support footprint never depends on the current animation pose.
        // A lifted boot is a visual detail, not an airborne event.
        float SupportHalfWidth => ActorCollisionHalfSize.x*.7f;

        float ResolveFall(Vector2 foot,float distance,bool capture=false)
        {
            float result=Mathf.Max(distance,GroundHeight-foot.y);
            float surface=GroundHeight;
            if(enableShadowCollision && shadowCollision!=null)
                for(int i=0;i<9;i++)
                {
                    float x=foot.x+Mathf.Lerp(-SupportHalfWidth,SupportHalfWidth,i/8f);
                    if(!shadowCollision.TryGetSupportHeight(x,foot.y+distance,foot.y+.0015f,out float height))continue;
                    float contact=Mathf.Min(0,height-foot.y+.0005f);
                    if(contact>result){result=contact;surface=height;}
                }
            if(capture && result>distance+.000001f){supportSurface=surface;supportCaptured=true;}
            return result;
        }

        bool HasSupport(Vector2 foot,float distance)
        {
            if(foot.y<=GroundHeight+distance)return true;
            return ResolveFall(foot,-distance)>-distance+.00001f;
        }

        bool TryBodySupport(Vector2 foot,float above,float below,out float height)
        {
            height=float.NegativeInfinity;
            if(GroundHeight>=foot.y-below && GroundHeight<=foot.y+above)height=GroundHeight;
            if(enableShadowCollision && shadowCollision!=null)
                for(int i=0;i<9;i++)
                {
                    float x=foot.x+Mathf.Lerp(-SupportHalfWidth,SupportHalfWidth,i/8f);
                    if(shadowCollision.TryGetSupportHeight(x,foot.y-below,foot.y+above,out float top))
                        height=Mathf.Max(height,top);
                }
            return !float.IsNegativeInfinity(height);
        }

        void StabilizeGroundContact()
        {
            if(!grounded)return;
            if(!TryBodySupport(motorFoot,.015f,.06f,out float height)){grounded=false;return;}
            motorFoot.y=Mathf.Max(GroundHeight,height+.0005f);
            supportSurface=height;supportCaptured=true;jumpVelocity=0;
        }

        bool TrySoleSupport(float above,float below,out float correction)
        {
            correction=float.NegativeInfinity;
            foreach(var offset in soleOffsets)
            {
                var sole=motorFoot+offset;
                if(GroundHeight>=sole.y-below && GroundHeight<=sole.y+above)
                    correction=Mathf.Max(correction,GroundHeight-sole.y);
                if(enableShadowCollision && shadowCollision!=null && shadowCollision.TryGetSupportHeight(sole.x,sole.y-below,sole.y+above,out float height))
                    correction=Mathf.Max(correction,height-sole.y);
            }
            return !float.IsNegativeInfinity(correction);
        }

        void AlignVisualFeet()
        {
            if(!motorReady || Screen==null || stageActor==null)return;
            // First follow the stable body horizontally. Then place the rendered soles
            // on the same supporting surface, without modifying the motor or velocity.
            var root=Screen.InverseTransformPoint(ProjectWorldPoint(stageActor.position));
            var target=Screen.TransformPoint(new Vector3(motorFoot.x,root.y,0));
            stageActor.position=projector.lens.position+(target-projector.lens.position)/ProjectionScale;
            var visual=ReadBootFoot();
            float correction=motorFoot.y-visual.y;
            if(grounded)
            {
                CollectSoleOffsets();
                float reference=supportCaptured?supportSurface:motorFoot.y;
                float contact=float.NegativeInfinity;
                foreach(var offset in soleOffsets)
                {
                    var point=visual+offset;
                    if(Mathf.Abs(reference-GroundHeight)<.02f)contact=Mathf.Max(contact,GroundHeight-point.y);
                    if(enableShadowCollision && shadowCollision!=null &&
                        shadowCollision.TryGetSupportHeight(point.x,reference-.22f,reference+.12f,out float height))
                        contact=Mathf.Max(contact,height-point.y);
                }
                if(!float.IsNegativeInfinity(contact))correction=contact+.0005f;
            }
            root=Screen.InverseTransformPoint(ProjectWorldPoint(stageActor.position));
            target=Screen.TransformPoint(new Vector3(motorFoot.x,root.y+correction,0));
            stageActor.position=projector.lens.position+(target-projector.lens.position)/ProjectionScale;
        }

        Vector2 ReadBootFoot()
        {
            var root=Screen.InverseTransformPoint(ProjectWorldPoint(stageActor.position));
            ReadProjectedBootContours();
            float lowest=float.PositiveInfinity;
            foreach(var point in projectedBoots)lowest=Mathf.Min(lowest,point.y);
            return new Vector2(root.x,float.IsPositiveInfinity(lowest)?root.y:lowest);
        }

        void CollectSoleOffsets()
        {
            var foot=ReadBootFoot();soleOffsets.Clear();
            if(projectedBoots.Count==0){soleOffsets.Add(Vector2.zero);return;}
            float minX=float.PositiveInfinity,maxX=float.NegativeInfinity;
            foreach(var p in projectedBoots){minX=Mathf.Min(minX,p.x);maxX=Mathf.Max(maxX,p.x);}
            const int bins=32;
            float span=Mathf.Max(.001f,maxX-minX);
            var bottom=new Vector2[bins];var valid=new bool[bins];
            foreach(var point in projectedBoots)
            {
                // Translation in a later station can perturb float subtraction around
                // an exact bin boundary. Quantize well below a texture pixel so the same
                // sole does not switch support samples after a vertical correction.
                float column=Mathf.Round((point.x-minX)/span*bins*100f)/100f;
                int bin=Mathf.Clamp(Mathf.FloorToInt(column),0,bins-1);
                if(!valid[bin] || point.y<bottom[bin].y){bottom[bin]=point;valid[bin]=true;}
            }
            for(int i=0;i<bins;i++)if(valid[i])soleOffsets.Add(bottom[i]-foot);
        }

        void ReadProjectedBootContours()
        {
            projectedBoots.Clear();
            if(stagePuppet==null || stagePuppet.partRenderers==null)return;
            if(bootProperties==null)bootProperties=new MaterialPropertyBlock();
            for(int i=0;i<stagePuppet.partRenderers.Length && i<stagePuppet.partMeshes.Length;i++)
            {
                var renderer=stagePuppet.partRenderers[i];
                if(renderer==null || !renderer.enabled || renderer.sharedMaterial==null ||
                    !bootContours.TryGetValue(renderer.sharedMaterial,out var contour))continue;
                bootProperties.Clear();renderer.GetPropertyBlock(bootProperties);
                if(bootProperties.HasFloat(OpacityProperty) && bootProperties.GetFloat(OpacityProperty)<.5f)continue;
                var filter=stagePuppet.partMeshes[i];
                if(filter==null || filter.sharedMesh==null)continue;
                filter.sharedMesh.GetVertices(meshVertices);
                if(meshVertices.Count!=4)continue;
                foreach(var uv in contour)
                {
                    var point=Vector3.Lerp(Vector3.Lerp(meshVertices[3],meshVertices[2],uv.x),Vector3.Lerp(meshVertices[0],meshVertices[1],uv.x),uv.y);
                    projectedBoots.Add(Screen.InverseTransformPoint(ProjectWorldPoint(filter.transform.TransformPoint(point))));
                }
            }
        }

        void CacheBootContours()
        {
            bootContours.Clear();
            var data=stagePuppet!=null?stagePuppet.Data:null;
            if(data==null || data.bitmaps==null || stagePuppet.bitmapMaterials==null)return;
            for(int i=0;i<data.bitmaps.Length && i<stagePuppet.bitmapMaterials.Length;i++)
            {
                if(string.IsNullOrEmpty(data.bitmaps[i].source) || data.bitmaps[i].source.IndexOf("Boot",System.StringComparison.OrdinalIgnoreCase)<0)continue;
                var material=stagePuppet.bitmapMaterials[i];
                if(material==null || bootContours.ContainsKey(material))continue;
                var texture=(material.HasProperty("_BaseMap")?material.GetTexture("_BaseMap"):material.mainTexture) as Texture2D;
                if(texture==null)continue;
                Color32[] pixels=ReadTexturePixels(texture);
                int width=texture.width,height=texture.height;
                var points=new List<Vector2>();
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    if(pixels[y*width+x].a<128)continue;
                    if(x>0 && x<width-1 && y>0 && y<height-1 && pixels[y*width+x-1].a>=128 &&
                        pixels[y*width+x+1].a>=128 && pixels[(y-1)*width+x].a>=128 && pixels[(y+1)*width+x].a>=128)continue;
                    points.Add(new Vector2((x+.5f)/width,(y+.5f)/height));
                }
                bootContours[material]=points.ToArray();
            }
        }

        static Color32[] ReadTexturePixels(Texture2D texture)
        {
            if(texture.isReadable)return texture.GetPixels32();
            var previous=RenderTexture.active;
            var target=RenderTexture.GetTemporary(texture.width,texture.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            Texture2D copy=null;
            try
            {
                Graphics.Blit(texture,target);RenderTexture.active=target;
                copy=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false,true);
                copy.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);copy.Apply(false,false);
                return copy.GetPixels32();
            }
            finally
            {
                RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
                if(copy!=null){if(Application.isPlaying)Destroy(copy);else DestroyImmediate(copy);}
            }
        }

        void CopyRenderedPose(PuppetAnimator source)
        {
            stagePuppet.bitmapMaterials=(Material[])source.bitmapMaterials.Clone();
            var block=new MaterialPropertyBlock();
            for(int i=0;i<stagePuppet.partMeshes.Length && i<source.partMeshes.Length;i++)
            {
                var from=source.partMeshes[i].sharedMesh;
                var to=stagePuppet.partMeshes[i].sharedMesh;
                to.Clear(); to.vertices=from.vertices; to.uv=from.uv; to.triangles=from.triangles; to.bounds=from.bounds;
            }
            for(int i=0;i<stagePuppet.partRenderers.Length && i<source.partRenderers.Length;i++)
            {
                var from=source.partRenderers[i]; var to=stagePuppet.partRenderers[i];
                if(from==null || to==null) continue;
                to.enabled=from.enabled; to.sharedMaterials=from.sharedMaterials;
                to.sortingLayerID=from.sortingLayerID; to.sortingOrder=from.sortingOrder;
                block.Clear(); from.GetPropertyBlock(block); to.SetPropertyBlock(block);
                to.transform.localPosition=from.transform.localPosition;
                to.transform.localRotation=from.transform.localRotation;
                to.transform.localScale=from.transform.localScale;
            }
        }

        public void MarkComplete()
        {
            completed=true;
            HideStage();
            RefreshGates(false);
        }

        public void ResetSession()
        {
            completed=false; entranceArmed=false;
            HideStage(); CaptureHandle();
            if(leverHandle!=null) leverHandle.localRotation=handleRotation;
        }

        void CaptureHandle()
        {
            if(handleCaptured || leverHandle==null) return;
            handleRotation=leverHandle.localRotation; handleCaptured=true;
        }

        void MeasureActorEnvelope()
        {
            var data=stagePuppet!=null?stagePuppet.Data:null;
            if(data==null || data.clips==null || stageActor==null) return;
            float width=0,height=0;
            foreach(var clip in data.clips) if(clip.name=="Idle" || clip.name=="Walk") foreach(var frame in clip.frames) foreach(var part in frame.parts)
                for(int vertex=0;vertex<part.corners.Length/2;vertex++)
                {
                    width=Mathf.Max(width,Mathf.Abs(part.corners[vertex*2])*data.unitsPerPixel);
                    height=Mathf.Max(height,(data.sourceGroundY-part.corners[vertex*2+1])*data.unitsPerPixel);
                }
            measuredHalfWidth=width*Mathf.Abs(stageActor.lossyScale.x)+.08f;
            measuredHeight=height*Mathf.Abs(stageActor.lossyScale.y)+.08f;
        }

        static void SetGates(Collider[] colliders,Renderer[] visuals,bool closed)
        {
            if(colliders!=null) foreach(var collider in colliders) if(collider!=null) collider.enabled=closed;
            if(visuals!=null) foreach(var visual in visuals) if(visual!=null) visual.enabled=closed;
        }
    }
}

