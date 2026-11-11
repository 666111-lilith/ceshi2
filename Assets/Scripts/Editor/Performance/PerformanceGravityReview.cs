using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Palworks.Projection;

namespace Palworks.Performance.Editor
{
    // Runs the actual motor, including substeps and collision sweeps. No free-XY path planner.
    public static class PerformanceGravityReview
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        const float Dt=1f/60f;

        [MenuItem("Tools/Performance/Validate Gravity")]
        public static void ValidatePhysics()
        {
            var report=new StringBuilder();
            var root=new GameObject("GravityReview");
            try
            {
                var screen=new GameObject("Screen").transform;screen.SetParent(root.transform);screen.position=new Vector3(0,0,10);
                var lamp=new GameObject("Lens").transform;lamp.SetParent(root.transform);
                var actor=new GameObject("Actor").transform;actor.SetParent(root.transform);actor.position=new Vector3(-2,0,9);
                var p=root.AddComponent<PaperProjector>();p.screen=screen;p.lens=lamp;p.screenSize=new Vector2(12,8);p.enabled=false;
                var world=root.AddComponent<ShadowCollisionWorld>();world.contactSkin=.001f;
                var station=root.AddComponent<PerformanceStation>();station.projector=p;station.stageActor=actor;station.shadowCollision=world;
                station.bottomPadding=.16f;
                Set(station,"casterDistance",9f);
                void Place(Vector2 foot)
                {
                    Set(station,"motorReady",false);Set(station,"jumpVelocity",0f);Set(station,"grounded",false);Set(station,"upHeld",false);
                    actor.position=lamp.position+(screen.TransformPoint(foot)-lamp.position)/station.ProjectionScale;
                }
                void Step(int count,Vector2 input,bool jump=false,bool dash=false)
                {for(int i=0;i<count;i++)station.MoveActor(input,Dt,false,dash&&i==0,jump&&i==0);}
                void Check(bool pass,string label)
                {report.AppendLine(label+"="+pass+" Foot="+station.ActorFootPosition.ToString("F4")+" Velocity="+station.VerticalVelocity.ToString("F3"));if(!pass)throw new Exception(label);}
                void Box(float x0,float y0,float x1,float y1)
                {typeof(ShadowCollisionWorld).GetMethod("AddPolygon",Private).Invoke(world,new object[]{new[]{new Vector2(x0,y0),new Vector2(x1,y0),new Vector2(x1,y1),new Vector2(x0,y1)}});}

                Place(new Vector2(-2,0));Step(12,Vector2.zero);
                Check(station.ActorFootPosition.y<-.2f && station.VerticalVelocity<0,"GravityStartsWithoutJump");
                Step(120,Vector2.zero);Check(station.IsGrounded && Mathf.Abs(station.ActorFootPosition.y-station.GroundHeight)<.001f,"FallsOntoFloor");
                float floor=station.ActorFootPosition.y;Step(15,Vector2.down);
                Check(Mathf.Abs(station.ActorFootPosition.y-floor)<.001f,"SDoesNotFly");
                Step(1,Vector2.up);Check(station.VerticalVelocity>0 && !station.IsGrounded,"WStartsGroundedJump");
                Step(6,Vector2.zero);float velocity=station.VerticalVelocity;Step(1,Vector2.zero,true);
                Check(station.VerticalVelocity<velocity,"AirborneSecondJumpRejected");
                Step(100,Vector2.zero);Check(station.IsGrounded,"JumpLandsNaturally");

                Box(-1,-3.84f,1,-2.4f);
                Place(new Vector2(0,0));Step(100,Vector2.zero);
                Check(station.IsGrounded && Mathf.Abs(station.ActorFootPosition.y+2.4f)<.003f,"SolidSilhouetteSupportsSoleWithoutGap");
                Step(35,Vector2.right);Check(station.ActorFootPosition.y<-2.7f && !station.IsGrounded,"WalkingOffEdgeFalls");
                Step(100,Vector2.zero);Check(station.IsGrounded,"WalkOffLandsOnFloor");

                Box(2,-1.8f,4,-1.5f);
                Place(new Vector2(3,station.GroundHeight));Step(2,Vector2.zero);Step(9,Vector2.zero,true);
                Check(station.VerticalVelocity<=0 && station.ActorFootPosition.y<-3.3f,"JumpCannotPassThroughCeiling");
                Place(new Vector2(-3,station.GroundHeight));Step(2,Vector2.zero);Step(25,Vector2.right,false,true);
                Check(station.ActorFootPosition.x<-1.15f,"DashCannotTunnelThroughSolid");

                var shape=new GameObject("Caster");shape.transform.SetParent(root.transform);
                var sprite=shape.AddComponent<SpriteRenderer>();var style=shape.AddComponent<ProjectionCasterStyle>();
                style.solid=true;style.opacity=1;style.depthLayer=ProjectionCasterStyle.DepthLayer.Middle;
                Check(!ShadowCollisionWorld.IsSolidCaster(sprite),"MiddleShadowNeverCollides");
                style.depthLayer=ProjectionCasterStyle.DepthLayer.Background;
                Check(!ShadowCollisionWorld.IsSolidCaster(sprite),"BackgroundShadowNeverCollides");
                style.depthLayer=ProjectionCasterStyle.DepthLayer.Solid;style.opacity=.6f;
                Check(!ShadowCollisionWorld.IsSolidCaster(sprite),"TranslucentSolidMarkedShadowNeverCollides");
                style.opacity=1;Check(ShadowCollisionWorld.IsSolidCaster(sprite),"OpaqueSolidCasterEligible");
                Box(-5,-3.8f,-4,-3.4f);Box(-5,-3.4f,-4,-2.5f);
                Check(!world.TryGetSupportHeight(-4.5f,-3.6f,-3.2f,out _),"RasterInternalSeamIsNotSupport");
                Check(world.TryGetSupportHeight(-4.5f,-2.6f,-2.4f,out float support) && Mathf.Abs(support+2.5f)<.0001f,"MergedOpaqueContourHasSingleTop");
                report.AppendLine("Result=PASS");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                Directory.CreateDirectory("output/scene-review");
                File.WriteAllText("output/scene-review/gravity-physics-check.txt",report.ToString());
            }
        }

        [MenuItem("Tools/Performance/Validate Gravity Routes")]
        public static void ValidateStations()=>RunStations(false);

        [MenuItem("Tools/Performance/Plan Key Heights")]
        public static void PlanKeyHeights()=>RunStations(true);

        static void RunStations(bool planKeys)
        {
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("Play mode required.");
            var sequence=UnityEngine.Object.FindObjectOfType<PerformanceSequence>();
            if(sequence==null)throw new InvalidOperationException("No performance sequence.");
            var report=new StringBuilder();
            try
            {
                if(sequence.CurrentMode!=PerformanceSequence.Mode.Reality)throw new InvalidOperationException("Start a fresh Play session in reality.");
                sequence.InitializeSession();
                while(sequence.CurrentStationIndex>=0)
                {
                    int index=sequence.CurrentStationIndex;var station=sequence.stations[index];
                    var controller=sequence.worldPlayer.GetComponent<CharacterController>();bool on=controller!=null && controller.enabled;
                    if(controller!=null)controller.enabled=false;
                    sequence.worldPlayer.transform.position=station.lever.position+Vector3.right*.7f;
                    if(controller!=null)controller.enabled=on;
                    if(!sequence.TryEnter(index,false))throw new Exception("Cannot enter "+station.name);
                    for(int f=0;f<240 && sequence.CurrentMode==PerformanceSequence.Mode.Entering;f++)sequence.Step(Vector2.zero,Dt);
                    var collision=station.shadowCollision;
                    report.AppendLine(station.name+" CollisionSource="+collision.CollisionSource+" CoverageThreshold="+collision.solidCoverageThreshold+" RenderTexture="+station.projector.shadowTexture.width+"x"+station.projector.shadowTexture.height+" CollisionMask="+collision.SolidMaskWidth+"x"+collision.SolidMaskHeight+" SolidCasters="+collision.SolidCasterCount+" Polygons="+collision.PolygonCount);
                    if(collision.CollisionSource!="RenderedUmbra")throw new Exception("Scene collision is not built from rendered umbra.");
                    float lastX=station.ActorFootPosition.x;int stagnant=0,jumps=0,landings=0,settledFrames=0;bool previousGrounded=false,contactCaptured=false;float maxGap=0,maxContactGap=0;
                    bool reached=false;
                    for(int frame=0;frame<2400;frame++)
                    {
                        var foot=station.ActorFootPosition;var keyLocal=(Vector2)station.Screen.InverseTransformPoint(station.key.position);
                        float dx=keyLocal.x-foot.x;
                        float input=Mathf.Abs(dx)<.12f?0:Mathf.Sign(dx);
                        var chest=(Vector2)station.Screen.InverseTransformPoint(station.ProjectedActorChest);
                        bool obstacle=station.shadowCollision!=null && Mathf.Abs(station.shadowCollision.Move(foot+Vector2.up*.08f,new Vector2(input*.45f,0),station.ActorCollisionHalfSize).x-foot.x)<.38f;
                        bool requestJump=station.IsGrounded && (obstacle || stagnant>9 || (!planKeys && Mathf.Abs(dx)<1.6f && keyLocal.y-chest.y>.22f));
                        if(planKeys && Mathf.Abs(dx)<.14f)requestJump=false;
                        if(requestJump)jumps++;
                        SampleGroundAnimation(station,input,frame);
                        station.MoveActor(new Vector2(input,0),Dt,false,false,requestJump);
                        if(station.IsGrounded && !previousGrounded)landings++;
                        previousGrounded=station.IsGrounded;
                        maxGap=Mathf.Max(maxGap,Mathf.Abs(station.VisualFootHeight-station.ActorFootPosition.y));
                        if(station.IsGrounded)
                        {
                            float contact=Mathf.Abs(station.GroundContactGap);maxContactGap=Mathf.Max(maxContactGap,contact);
                            if(contact>.015f)
                            {
                                report.AppendLine("ContactFailure "+station.name+" Frame="+frame+" Foot="+station.ActorFootPosition.ToString("F5")+" Gap="+contact+" Clip="+station.stagePuppet.CurrentClip);
                                CaptureContact(sequence,station,"contactfailure");
                                throw new Exception("No actual opaque sole contact at "+station.name+" gap="+contact);
                            }
                            if(!contactCaptured && station.ActorFootPosition.y>station.GroundHeight+.15f)
                            {CaptureContact(sequence,station,"landing");contactCaptured=true;}
                        }
                        if(Mathf.Abs(station.ActorFootPosition.x-lastX)<.001f && Mathf.Abs(dx)>.3f)stagnant++;else stagnant=0;
                        lastX=station.ActorFootPosition.x;
                        if(planKeys)
                        {
                            settledFrames=station.IsGrounded && Mathf.Abs(dx)<.14f?settledFrames+1:0;
                            if(settledFrames>=45)
                            {
                                var suggestion=station.Screen.InverseTransformPoint(station.ProjectedActorChest);
                                report.AppendLine("KeySuggestion "+station.name+" X="+keyLocal.x.ToString("F4")+" Y="+suggestion.y.ToString("F4")+" FootY="+station.ActorFootPosition.y.ToString("F4"));
                                reached=true;break;
                            }
                        }
                        else if(station.ActorTouchesKey()){reached=true;break;}
                    }
                    CaptureContact(sequence,station,"key");
                    report.AppendLine(station.name+" Reached="+reached+" Jumps="+jumps+" Landings="+landings+" MaxOpaqueFootError="+maxGap.ToString("F5")+" MaxActualSupportGap="+maxContactGap.ToString("F5")+" End="+station.ActorFootPosition.ToString("F3")+" Grounded="+station.IsGrounded+" Velocity="+station.VerticalVelocity+" SupportY="+station.SupportHeight+" Key="+station.Screen.InverseTransformPoint(station.key.position).ToString("F3"));
                    if(!reached)throw new Exception("Gravity route cannot reach key at "+station.name+". See gravity-route-check.txt.");
                    if(maxGap>.012f)throw new Exception("Opaque foot lost contact calibration at "+station.name);
                    if(!sequence.CollectKey())throw new Exception("Key does not return to reality.");
                    for(int f=0;f<240 && sequence.CurrentMode==PerformanceSequence.Mode.Returning;f++)sequence.Step(Vector2.zero,Dt);
                    if(sequence.CurrentMode!=PerformanceSequence.Mode.Reality)throw new Exception("Return did not finish.");
                    foreach(var gate in station.gates)if(gate!=null && gate.enabled)throw new Exception("Completed station exit stays blocked.");
                }
                report.AppendLine(planKeys?"Result=PASS KeyHeightsPlannedWithoutSceneEdits=True":"Result=PASS AllStationsReturnedWithGravity=True");
            }
            finally
            {
                Directory.CreateDirectory("output/scene-review");File.WriteAllText(planKeys?"output/scene-review/gravity-key-plan.txt":"output/scene-review/gravity-route-check.txt",report.ToString());
            }
        }

        static void SampleGroundAnimation(PerformanceStation station,float input,int frame)
        {
            if(!station.IsGrounded || station.stagePuppet==null)return;
            string name=Mathf.Abs(input)>.001f?"Walk":"Idle";
            station.stagePuppet.CancelAction();station.stagePuppet.SetMotion(Mathf.Abs(input)>.001f,false);
            foreach(var clip in station.stagePuppet.Data.clips)if(clip.name==name)
            {station.stagePuppet.ApplyFrame(name,Mathf.FloorToInt(frame*Dt*24)%clip.frames.Length);break;}
        }

        static void CaptureContact(PerformanceSequence sequence,PerformanceStation station,string suffix)
        {
            var camera=sequence.view;var position=camera.transform.position;float fov=camera.fieldOfView;
            try
            {
                Vector3 subject=station.ProjectedActorFoot+station.Screen.up*.7f;
                var offset=subject-camera.transform.position;
                camera.transform.position+=station.Screen.right*Vector3.Dot(offset,station.Screen.right)+station.Screen.up*Vector3.Dot(offset,station.Screen.up);
                camera.fieldOfView=fov*.43f;
                Palworks.Editor.PaperRoomBuilder.Capture();
                string source="output/scene-review/paper-room-play.png";
                if(File.Exists(source))File.Copy(source,"output/scene-review/gravity-"+station.name.ToLowerInvariant()+"-"+suffix+"-contact.png",true);
            }
            finally{camera.transform.position=position;camera.fieldOfView=fov;}
        }

        static void Set(object target,string field,object value)=>target.GetType().GetField(field,Private).SetValue(target,value);
    }
}
