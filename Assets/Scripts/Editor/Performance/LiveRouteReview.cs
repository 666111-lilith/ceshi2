using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Palworks.Performance;

namespace Palworks.Editor
{
    public static class LiveRouteReview
    {
        public static void Start()
        {
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("Fresh Play session required.");
            if(UnityEngine.Object.FindObjectOfType<LiveRouteReviewRunner>()!=null)throw new InvalidOperationException("Live route already running.");
            var sequence=UnityEngine.Object.FindObjectOfType<PerformanceSequence>();
            if(sequence==null)throw new InvalidOperationException("No PerformanceSequence.");
            sequence.InitializeSession();
            if(sequence.CurrentMode!=PerformanceSequence.Mode.Reality || sequence.CurrentStationIndex!=0)throw new InvalidOperationException("Start at Station1 in Reality.");
            var item=new GameObject("LiveRouteReview"){hideFlags=HideFlags.HideAndDontSave};
            item.AddComponent<LiveRouteReviewRunner>().Begin(sequence);
        }
    }

    // Exactly one motor call in each real Update. PuppetAnimator still runs its
    // own Update; PerformanceStation still runs its own LateUpdate and rendering.
    [DefaultExecutionOrder(1500)]
    public sealed class LiveRouteReviewRunner:MonoBehaviour
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        PerformanceSequence sequence;
        PerformanceStation station;
        string phase="Entering",directory;
        StringBuilder csv,report;
        RenderTexture target,oldTarget;
        float oldAspect,phaseStarted,started,initialPlatformY,previousRootY;
        bool oldBackground,oldSequenceEnabled,capturedState,sequenceSuppressed,ready,finishRequested,finished,pass=true;
        bool sawWalkoff,sawDropLanding,sawJumpRise,sawJumpLanding;
        int frames,phaseFrames,idleFlips,idleLandEntries,idleJumpEntries;
        bool previousGrounded;
        string previousClip;
        float idleMaxRootStep,maxSupportGap;

        public void Begin(PerformanceSequence value)
        {
            sequence=value;station=sequence.stations[0];
            oldBackground=Application.runInBackground;Application.runInBackground=true;
            oldSequenceEnabled=sequence.enabled;
            directory="output/flicker-review/route-"+DateTime.Now.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(directory);
            csv=new StringBuilder("frame,time,phase,clip,grounded,velocity,footX,footY,rootY,supportY,contactGap\n");
            report=new StringBuilder("Mode=RealPlayerLoop\nAnimation=UnmodifiedUpdate\nMotor=OneCallPerUpdate\nFramesDirectory="+directory+"\n");
            oldTarget=sequence.view.targetTexture;oldAspect=sequence.view.aspect;
            target=new RenderTexture(960,540,24,RenderTextureFormat.ARGB32){name="LiveRouteCapture",hideFlags=HideFlags.HideAndDontSave};target.Create();
            sequence.view.targetTexture=target;sequence.view.aspect=16f/9f;
            var controller=sequence.worldPlayer.GetComponent<CharacterController>();bool enabled=controller!=null&&controller.enabled;
            if(controller!=null)controller.enabled=false;
            sequence.worldPlayer.transform.position=station.lever.position+Vector3.right*.7f;
            if(controller!=null)controller.enabled=enabled;
            started=Time.unscaledTime;phaseStarted=started;
            if(!sequence.TryEnter(0,false))throw new Exception("Could not enter Station1.");
            RenderPipelineManager.endCameraRendering+=OnRendered;
            EditorApplication.update+=WatchEditor;ready=true;
        }

        void Update()
        {
            if(!ready||finished||finishRequested)return;
            if(Time.unscaledTime-started>30){Fail("Timed out in "+phase);return;}
            if(phase=="Entering")
            {
                if(sequence.CurrentMode!=PerformanceSequence.Mode.Performing)return;
                // OnDisable normally dismantles the performance. Bypass only that
                // cleanup while this temporary test owns input; retain every actor.
                capturedState=(bool)typeof(PerformanceSequence).GetField("captured",Private).GetValue(sequence);
                typeof(PerformanceSequence).GetField("captured",Private).SetValue(sequence,false);
                sequence.enabled=false;
                typeof(PerformanceSequence).GetField("captured",Private).SetValue(sequence,capturedState);
                sequenceSuppressed=true;
                station.stagePuppet.CancelAction();station.stagePuppet.SetMotion(false,false);station.stagePuppet.enabled=true;
                Set("jumpVelocity",0f);Set("grounded",false);Set("supportCaptured",false);Set("upHeld",false);Set("dashRemaining",0f);Set("animationStarted",true);
                typeof(PerformanceStation).GetMethod("SetProjectedFoot",Private).Invoke(station,new object[]{new Vector2(-2.35f,-.25f)});
                Next("DropOntoStump");return;
            }
            if(phase=="Returning")return;
            Vector2 input=Vector2.zero;bool jump=false;
            if(phase=="WalkDownSlope")input=Vector2.right;
            if(phase=="StartJump"){jump=true;Next("JumpFlight");}
            station.MoveActor(input,Time.deltaTime,false,false,jump);
        }

        void OnRendered(ScriptableRenderContext context,Camera camera)
        {
            if(!ready||finished||finishRequested||camera!=sequence.view)return;
            try
            {
                if(phase=="Entering"||phase=="Returning")return;
                frames++;phaseFrames++;
                float now=Time.unscaledTime;
                var foot=station.ActorFootPosition;bool grounded=station.IsGrounded;string clip=station.stagePuppet.CurrentClip;
                float rootY=station.Screen.InverseTransformPoint(station.ProjectWorldPoint(station.stageActor.position)).y;
                float gap=station.GroundContactGap;
                csv.AppendLine(string.Join(",",frames,F(now-started),phase,clip,grounded?1:0,F(station.VerticalVelocity),F(foot.x),F(foot.y),F(rootY),F(station.SupportHeight),F(gap)));
                if(grounded)maxSupportGap=Mathf.Max(maxSupportGap,gap);
                if((phase=="IdleOnStump"||phase=="IdleAfterJump")&&now-phaseStarted>.4f && phaseFrames>1)
                {
                    if(grounded!=previousGrounded)idleFlips++;
                    if(clip!=previousClip){if(clip.StartsWith("Land"))idleLandEntries++;if(clip.StartsWith("Jump"))idleJumpEntries++;}
                    idleMaxRootStep=Mathf.Max(idleMaxRootStep,Mathf.Abs(rootY-previousRootY));
                }
                previousClip=clip;previousGrounded=grounded;previousRootY=rootY;
                if(frames%4==0)CaptureFrame(frames/4);
                float duration=now-phaseStarted;
                if(phase=="DropOntoStump"&&grounded)
                {initialPlatformY=foot.y;Next("IdleOnStump");}
                else if(phase=="IdleOnStump"&&duration>=2f)Next("WalkDownSlope");
                else if(phase=="WalkDownSlope")
                {
                    if(!grounded&&foot.x>-.9f){sawWalkoff=true;Next("FallFromEdge");}
                    else if(foot.x>-.4f){Fail("Walked across expected edge without falling.");}
                    else if(duration>5)Fail("Movement stuck on descending contour.");
                }
                else if(phase=="FallFromEdge"&&grounded)
                {sawDropLanding=foot.y<initialPlatformY-.15f;Next("IdleAfterDrop");}
                else if(phase=="IdleAfterDrop"&&duration>=.5f)Next("StartJump");
                else if(phase=="JumpFlight")
                {
                    if(station.VerticalVelocity>0)sawJumpRise=true;
                    if(sawJumpRise&&grounded){sawJumpLanding=true;Next("IdleAfterJump");}
                }
                else if(phase=="IdleAfterJump"&&duration>=2f)
                {
                    pass=sawWalkoff&&sawDropLanding&&sawJumpRise&&sawJumpLanding&&idleFlips==0&&idleLandEntries==0&&idleJumpEntries==0&&idleMaxRootStep<.12f;
                    finishRequested=true;
                }
            }
            catch(Exception ex){Fail(ex.ToString());}
        }

        void Next(string value)
        {
            report.AppendLine("Phase="+phase+" Duration="+F(Time.unscaledTime-phaseStarted)+" Frames="+phaseFrames);
            phase=value;phaseStarted=Time.unscaledTime;phaseFrames=0;
        }
        void Fail(string reason){pass=false;report.AppendLine("Failure="+reason);finishRequested=true;}
        void WatchEditor()
        {
            if(finished)return;
            if(!EditorApplication.isPlaying){pass=false;report.AppendLine("Interrupted=ExitedPlay");finishRequested=true;}
            if(!finishRequested){EditorApplication.QueuePlayerLoopUpdate();return;}
            Finish();
        }
        void Finish()
        {
            if(finished)return;finished=true;
            RenderPipelineManager.endCameraRendering-=OnRendered;EditorApplication.update-=WatchEditor;
            if(sequence!=null)
            {
                if(sequenceSuppressed)sequence.enabled=oldSequenceEnabled;
                if(sequence.CurrentMode==PerformanceSequence.Mode.Performing)sequence.CollectKey();
                if(sequence.view!=null){sequence.view.targetTexture=oldTarget;sequence.view.aspect=oldAspect;}
            }
            Application.runInBackground=oldBackground;
            AsyncGPUReadback.WaitAllRequests();
            if(target!=null){target.Release();DestroyImmediate(target);target=null;}
            report.AppendLine("WalkedOffEdge="+sawWalkoff+" FellOntoLowerSurface="+sawDropLanding+" JumpRose="+sawJumpRise+" JumpLanded="+sawJumpLanding);
            report.AppendLine("IdleGroundedChanges="+idleFlips+" IdleLandEntries="+idleLandEntries+" IdleJumpEntries="+idleJumpEntries+" IdleMaxRootStep="+F(idleMaxRootStep)+" MaxGroundContactGap="+F(maxSupportGap));
            report.AppendLine("Result="+(pass?"PASS":"FAIL"));
            File.WriteAllText("output/flicker-review/live-route.csv",csv.ToString());File.WriteAllText("output/flicker-review/live-route.txt",report.ToString());
            File.WriteAllText(Path.Combine(directory,"frames.csv"),csv.ToString());
            Destroy(gameObject);
        }
        void CaptureFrame(int index)
        {
            if(!SystemInfo.supportsAsyncGPUReadback)return;
            string path=Path.Combine(directory,"route-"+index.ToString("D4")+".png");
            AsyncGPUReadback.Request(target,0,TextureFormat.RGBA32,request=>
            {
                if(request.hasError)return;
                var image=new Texture2D(960,540,TextureFormat.RGBA32,false);image.LoadRawTextureData(request.GetData<byte>());image.Apply(false,false);
                File.WriteAllBytes(path,image.EncodeToPNG());DestroyImmediate(image);
            });
        }
        void OnDestroy(){if(ready&&!finished){pass=false;report.AppendLine("Interrupted=Destroyed");Finish();}}
        void Set(string field,object value)=>typeof(PerformanceStation).GetField(field,Private).SetValue(station,value);
        static string F(float value)=>value.ToString("F6",CultureInfo.InvariantCulture);
    }
}
