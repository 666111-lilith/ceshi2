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
    // Observe complete real player-loop frames. Never replaces animation frames or
    // simulates several movement steps inside a single editor callback.
    public static class PerformanceStabilityReview
    {
        const string Output = "output/flicker-review";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly float[] Positions = { 1.3f, -2.35f };
        static PerformanceSequence sequence;
        static PerformanceStation station;
        static RenderTexture capture, previousTarget;
        static float previousAspect;
        static bool previousRunInBackground;
        static StringBuilder csv, report;
        static bool running, probing, done, anyFailure, pendingEnter, pendingFinish;
        static int stationIndex, probeIndex, frame, measured, switches, groundedChanges, landingEntries, jumpEntries, airborneFrames, imageCount, pendingImages;
        static float began, lastRootY, lastFootY, maximumRootStep, maximumFootStep;
        static bool previousGrounded;
        static string previousClip;
        static string runDirectory;

        public static void Start()
        {
            if(running)throw new InvalidOperationException("A live stability review is already running.");
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("Start a fresh Play session first.");
            sequence=UnityEngine.Object.FindObjectOfType<PerformanceSequence>();
            if(sequence==null)throw new InvalidOperationException("No performance sequence.");
            previousRunInBackground=Application.runInBackground;Application.runInBackground=true;
            sequence.InitializeSession();
            if(sequence.CurrentMode!=PerformanceSequence.Mode.Reality || sequence.CurrentStationIndex!=0)
                throw new InvalidOperationException("Stability review requires a fresh Reality session at Station1.");
            Directory.CreateDirectory(Output);
            runDirectory=Path.Combine(Output,"live-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(runDirectory);
            csv=new StringBuilder("station,probe,frame,time,clip,grounded,velocity,footX,footY,rootY,supportY,contactGap\n");
            report=new StringBuilder("Mode=RealPlayerLoop\nAnimation=UnmodifiedUpdate\nObserve=EndCameraRendering\nFramesDirectory="+runDirectory+"\n");
            previousTarget=sequence.view.targetTexture;previousAspect=sequence.view.aspect;
            capture=new RenderTexture(960,540,24,RenderTextureFormat.ARGB32){name="StabilityCapture",hideFlags=HideFlags.HideAndDontSave};
            capture.Create();sequence.view.targetTexture=capture;sequence.view.aspect=16f/9f;
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            running=true;probing=false;done=false;anyFailure=false;pendingEnter=false;pendingFinish=false;stationIndex=0;probeIndex=0;pendingImages=0;
            RenderPipelineManager.endCameraRendering+=OnCameraRendered;
            EditorApplication.update+=WatchEditor;
            EnterStation();
        }

        static void EnterStation()
        {
            station=sequence.stations[stationIndex];
            var controller=sequence.worldPlayer.GetComponent<CharacterController>();
            bool enabled=controller!=null&&controller.enabled;
            if(controller!=null)controller.enabled=false;
            sequence.worldPlayer.transform.position=station.lever.position+Vector3.right*.7f;
            if(controller!=null)controller.enabled=enabled;
            if(!sequence.TryEnter(stationIndex,false))throw new Exception("Cannot enter "+station.name);
            probing=false;
        }

        static void BeginProbe()
        {
            // Position once, then allow the normal animation, gravity and landing
            // logic to run entirely unassisted for the rest of the recording.
            station.stagePuppet.CancelAction();station.stagePuppet.SetMotion(false,false);
            station.stagePuppet.enabled=true;
            Set("jumpVelocity",0f);Set("grounded",false);Set("upHeld",false);Set("supportCaptured",false);
            Set("dashRemaining",0f);Set("animationStarted",true);
            typeof(PerformanceStation).GetMethod("SetProjectedFoot",Private).Invoke(station,new object[]{new Vector2(Positions[probeIndex],-.25f)});
            began=Time.unscaledTime;frame=0;measured=0;switches=0;groundedChanges=0;landingEntries=0;jumpEntries=0;airborneFrames=0;
            imageCount=0;maximumRootStep=0;maximumFootStep=0;previousClip=null;
            lastRootY=station.Screen.InverseTransformPoint(station.ProjectWorldPoint(station.stageActor.position)).y;
            lastFootY=station.ActorFootPosition.y;previousGrounded=station.IsGrounded;
            probing=true;
        }

        static void OnCameraRendered(ScriptableRenderContext context,Camera camera)
        {
            if(!running||camera!=sequence.view)return;
            try
            {
                if(sequence.CurrentMode==PerformanceSequence.Mode.Entering)return;
                if(sequence.CurrentMode==PerformanceSequence.Mode.Returning)return;
                if(sequence.CurrentMode==PerformanceSequence.Mode.Reality)
                {
                    if(done)pendingFinish=true;else pendingEnter=true;return;
                }
                if(!probing){BeginProbe();return;}
                float elapsed=Time.unscaledTime-began;
                var foot=station.ActorFootPosition;
                float rootY=station.Screen.InverseTransformPoint(station.ProjectWorldPoint(station.stageActor.position)).y;
                string clip=station.stagePuppet.CurrentClip;bool grounded=station.IsGrounded;
                float gap=station.GroundContactGap;
                csv.AppendLine(string.Join(",",station.name,probeIndex,frame,F(elapsed),clip,grounded?1:0,F(station.VerticalVelocity),F(foot.x),F(foot.y),F(rootY),F(station.SupportHeight),F(gap)));
                if(frame>=120)
                {
                    if(measured>0)
                    {
                        if(clip!=previousClip){switches++;if(clip.StartsWith("Land"))landingEntries++;if(clip.StartsWith("Jump"))jumpEntries++;}
                        if(grounded!=previousGrounded)groundedChanges++;
                        maximumRootStep=Mathf.Max(maximumRootStep,Mathf.Abs(rootY-lastRootY));
                        maximumFootStep=Mathf.Max(maximumFootStep,Mathf.Abs(foot.y-lastFootY));
                    }
                    if(!grounded)airborneFrames++;
                    measured++;
                    if(stationIndex==0&&probeIndex==0&&imageCount<60&&frame%2==0)
                        CaptureFrame("station1-right-"+(imageCount++).ToString("D3")+".png");
                }
                previousClip=clip;previousGrounded=grounded;lastRootY=rootY;lastFootY=foot.y;frame++;
                if(measured<180)return;
                bool pass=measured>=20 && groundedChanges==0 && landingEntries==0 && jumpEntries==0 && airborneFrames==0 && maximumRootStep<.12f;
                anyFailure|=!pass;
                report.AppendLine(station.name+" X="+F(Positions[probeIndex])+" Frames="+frame+" StableWindowFrames="+measured+" GroundedChanges="+groundedChanges+" LandEntries="+landingEntries+" JumpEntries="+jumpEntries+" AirborneFrames="+airborneFrames+" ClipChanges="+switches+" MaxRootStep="+F(maximumRootStep)+" MaxFootStep="+F(maximumFootStep)+" Result="+(pass?"PASS":"FAIL"));
                Flush(false);
                CaptureFrame(station.name+"-probe"+probeIndex+"-final.png");
                probeIndex++;probing=false;
                if(probeIndex<Positions.Length)return;
                probeIndex=0;stationIndex++;done=stationIndex>=sequence.stations.Length;
                if(!sequence.CollectKey())throw new Exception("Failed to return after live review.");
            }
            catch(Exception ex){report.AppendLine("Exception="+ex);anyFailure=true;pendingFinish=true;Debug.LogException(ex);}
        }

        static void CaptureFrame(string filename)
        {
            if(!SystemInfo.supportsAsyncGPUReadback)return;
            string path=Path.Combine(runDirectory,filename);pendingImages++;
            AsyncGPUReadback.Request(capture,0,TextureFormat.RGBA32,request=>
            {
                try
                {
                    if(request.hasError)return;
                    var image=new Texture2D(960,540,TextureFormat.RGBA32,false);
                    image.LoadRawTextureData(request.GetData<byte>());image.Apply(false,false);
                    File.WriteAllBytes(path,image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
                }
                finally{pendingImages--;}
            });
        }

        static void WatchEditor()
        {
            if(!running)return;
            if(pendingFinish){pendingFinish=false;Finish();return;}
            if(pendingEnter){pendingEnter=false;try{EnterStation();}catch(Exception ex){report.AppendLine("Exception="+ex);anyFailure=true;Finish();}return;}
            if(!EditorApplication.isPlaying){report.AppendLine("Interrupted=ExitedPlay");anyFailure=true;Finish();}
        }
        static void Finish()
        {
            if(!running)return;
            Application.runInBackground=previousRunInBackground;
            running=false;RenderPipelineManager.endCameraRendering-=OnCameraRendered;EditorApplication.update-=WatchEditor;
            if(sequence!=null && sequence.view!=null){sequence.view.targetTexture=previousTarget;sequence.view.aspect=previousAspect;}
            // Readbacks refer to this target until their callbacks finish.
            AsyncGPUReadback.WaitAllRequests();
            if(capture!=null){capture.Release();UnityEngine.Object.DestroyImmediate(capture);capture=null;}
            Flush(true);
        }
        static void Flush(bool final)
        {
            File.WriteAllText(Path.Combine(Output,"live-stability.csv"),csv.ToString());
            File.WriteAllText(Path.Combine(Output,"live-stability.txt"),report.ToString()+(final?"Result="+(anyFailure?"FAIL":"PASS")+"\n":"Status=Running\n"));
            File.WriteAllText(Path.Combine(runDirectory,"frames.csv"),csv.ToString());
        }
        static string F(float value)=>value.ToString("F6",CultureInfo.InvariantCulture);
        static void Set(string field,object value)=>typeof(PerformanceStation).GetField(field,Private).SetValue(station,value);
    }
}

