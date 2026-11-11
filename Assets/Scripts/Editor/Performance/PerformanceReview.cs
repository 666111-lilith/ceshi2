using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Palworks.CameraSystem;
using Palworks.Rendering;
using Palworks.Performance;
using Palworks.Projection;

namespace Palworks.Editor
{
    public static class PerformanceReview
    {
        static Snapshot previewSnapshot;
        static PerformanceStation previewStation;
        static bool hasPreviewSnapshot;

        struct Snapshot
        {
            public Vector3 playerPosition,cameraPosition;
            public Quaternion playerRotation,cameraRotation;
            public float fov,size;
            public bool orthographic,followEnabled,focusEnabled,controllerEnabled,visualsActive;
            public int culling;
            public CameraClearFlags clearFlags;
            public Color background;
            public int casterLayers;
            public int[] actorLayers;
            public Color32[] shadows,propShadows;
        }

        public static void EnterFirstPreview()
        {
            var sequence=Sequence();
            if(sequence.CurrentMode!=PerformanceSequence.Mode.Reality)
                throw new InvalidOperationException("Return to reality before preparing a preview.");
            int index=sequence.CurrentStationIndex;
            if(index<0) throw new InvalidOperationException("All stations completed; start a new Play session.");
            PrepareAtLever(sequence,sequence.stations[index]);
            previewStation=sequence.stations[index]; previewSnapshot=Capture(sequence,previewStation); hasPreviewSnapshot=true;
            if(!sequence.TryEnter(index)) throw new Exception("Lever did not start the performance.");
            ValidateEntry(sequence,previewStation,previewSnapshot);
            AdvanceTransition(sequence);
            ValidatePerformanceView(sequence,previewStation);
        }

        public static void CaptureMotion()
        {
            var sequence=Sequence();
            if(sequence.CurrentMode!=PerformanceSequence.Mode.Reality)
                throw new InvalidOperationException("Start motion capture from reality in a fresh Play session.");
            int index=sequence.CurrentStationIndex;
            if(index<0) throw new InvalidOperationException("All stations completed; start a new Play session.");
            PrepareAtLever(sequence,sequence.stations[index]);
            var follow=sequence.view.GetComponent<StageCameraFollow>();
            if(follow!=null && follow.enabled)
                for(int frame=0;frame<180;frame++) follow.Tick(1f/60f);
            previewStation=sequence.stations[index];
            previewSnapshot=Capture(sequence,previewStation); hasPreviewSnapshot=true;
            CaptureMotionFrame("entry-before");
            if(!sequence.TryEnter(index)) throw new Exception("Lever did not start the performance.");
            CaptureMotionFrame("entry-000");
            CaptureTransitionFrames(sequence,"entry");
            ValidatePerformanceView(sequence,previewStation);
        }

        public static void CaptureReturnMotion()
        {
            var sequence=Sequence();
            if(sequence.CurrentMode!=PerformanceSequence.Mode.Performing || !hasPreviewSnapshot || previewStation!=sequence.ActiveStation)
                throw new InvalidOperationException("Capture the entry motion first.");
            CaptureMotionFrame("return-before");
            if(!sequence.CollectKey()) throw new Exception("Performance did not start its return transition.");
            CaptureMotionFrame("return-000");
            CaptureTransitionFrames(sequence,"return");
            AssertRestored(sequence,previewStation,previewSnapshot);
            hasPreviewSnapshot=false;
        }

        static void CaptureTransitionFrames(PerformanceSequence sequence,string prefix)
        {
            const int samples=32;
            float step=sequence.transitionDuration/samples;
            for(int frame=1;frame<=samples;frame++)
            {
                sequence.Step(Vector2.zero,step+(frame==samples?.0001f:0));
                CaptureMotionFrame(prefix+"-"+frame.ToString("D3"));
            }
        }

        static void CaptureMotionFrame(string name)
        {
            Directory.CreateDirectory("output/scene-review/motion");
            PaperRoomBuilder.Capture();
            File.Copy("output/scene-review/paper-room-play.png","output/scene-review/motion/"+name+".png",true);
        }

        public static void ValidateAll()
        {
            var sequence=Sequence();
            var report=new StringBuilder("Result=PASS\n");
            int completedCount=0;
            if(sequence.CurrentMode==PerformanceSequence.Mode.Performing)
            {
                if(!hasPreviewSnapshot || previewStation!=sequence.ActiveStation)
                    throw new InvalidOperationException("Start review from reality or from EnterFirstPreview.");
                RunPickupAndReturn(sequence,previewStation,previewSnapshot,report);
                hasPreviewSnapshot=false; completedCount++;
            }
            if(sequence.CurrentMode!=PerformanceSequence.Mode.Reality)
                throw new InvalidOperationException("Wait for the camera transition before validation.");
            while(sequence.CurrentStationIndex>=0)
            {
                int index=sequence.CurrentStationIndex;
                var station=sequence.stations[index];
                var initialPosition=sequence.worldPlayer.transform.position;
                MoveRealityPlayer(sequence,station.lever.position+Vector3.right*30);
                if(sequence.TryEnter(index)) throw new Exception("Lever can be pulled from outside interaction range.");
                MoveRealityPlayer(sequence,initialPosition);
                PrepareAtLever(sequence,station);
                if(!station.EntranceArmed) throw new Exception("Station entrance does not latch behind the player.");
                if(!station.gates.Any(c=>c!=null && c.enabled) || !station.entranceGates.Any(c=>c!=null && c.enabled))
                    throw new Exception("Current station is not gated on both sides.");
                if(sequence.TryEnter(index+1,false)) throw new Exception("Future station can be skipped.");
                var snapshot=Capture(sequence,station);
                if(!sequence.TryEnter(index)) throw new Exception("Lever interaction did not enter the current station.");
                ValidateEntry(sequence,station,snapshot);
                AdvanceTransition(sequence);
                RunPickupAndReturn(sequence,station,snapshot,report);
                completedCount++;
            }
            report.AppendLine("CompletedStations="+completedCount);
            report.AppendLine("AllExitGatesOpen="+sequence.stations.All(s=>s.gates.All(c=>c==null || !c.enabled)));
            report.AppendLine("Transition=Continuous camera and world fade; Actor=Original projection caster; Camera=Frontal");
            Directory.CreateDirectory("output/scene-review");
            File.WriteAllText("output/scene-review/performance-check.txt",report.ToString());
            Debug.Log("PALWORKS: performance key / return / gate cycle passed for "+completedCount+" stations.");
        }

        static void RunPickupAndReturn(PerformanceSequence sequence,PerformanceStation station,Snapshot snapshot,StringBuilder report)
        {
            float width=ValidatePerformanceView(sequence,station);
            if(sequence.worldPlayer.enabled || sequence.worldPlayer.visuals.gameObject.activeSelf)
                throw new Exception("Reality player is still active during the performance.");
            if(!station.key.gameObject.activeSelf || !station.stageActor.gameObject.activeSelf)
                throw new Exception("Performance caster or key is not active.");
            if((sequence.view.cullingMask & (1<<PerformanceStation.ActorLayer))!=0)
                throw new Exception("The shadow caster is rendered directly by the performance camera.");
            AssertSameShadows(snapshot.propShadows,ReadShadowsWithoutActors(sequence,station),"Static prop shadows changed in performance.");
            if(!station.gates.Any(c=>c!=null && c.enabled)) throw new Exception("Exit opened before collecting the key.");
            var original=station.ProjectedActorFoot;
            float depth=station.CasterDistance;
            sequence.Step(Vector2.up,1f/60f);
            if(Vector3.Dot(station.ProjectedActorFoot-original,station.Screen.up)<=0)
                throw new Exception("W does not move along the cloth's vertical axis.");
            if(Mathf.Abs(Vector3.Dot(station.stageActor.position-station.projector.lens.position,station.Screen.forward)-depth)>.001f)
                throw new Exception("Performance movement changed the caster depth and penumbra.");
            foreach(var waypoint in FindRoute(station))
            {
                for(int frame=0;frame<900 && sequence.CurrentMode==PerformanceSequence.Mode.Performing;frame++)
                {
                    var foot=(Vector2)station.Screen.InverseTransformPoint(station.ProjectedActorFoot);
                    var offset=waypoint-foot;
                    if(offset.magnitude<.002f)break;
                    var scaled=Vector2.Scale(offset,new Vector2(Mathf.Abs(station.Screen.lossyScale.x),Mathf.Abs(station.Screen.lossyScale.y)));
                    sequence.Step(scaled.normalized,Mathf.Min(1f/60f,scaled.magnitude/Mathf.Max(.1f,station.movementSpeed)));
                }
            }
            if(sequence.CurrentMode!=PerformanceSequence.Mode.Returning || !station.Completed)
                throw new Exception("The key is unreachable: "+station.name+" foot="+station.Screen.InverseTransformPoint(station.ProjectedActorFoot)+" chest="+station.ProjectedActorChest+" key="+station.key.position);
            if(station.key.gameObject.activeSelf) throw new Exception("Collected key remains visible.");
            AdvanceTransition(sequence);
            if(sequence.CurrentMode!=PerformanceSequence.Mode.Reality) throw new Exception("Return did not reach reality mode.");
            AssertRestored(sequence,station,snapshot);
            if(station.gates.Any(c=>c!=null && c.enabled) || station.entranceGates.Any(c=>c!=null && c.enabled))
                throw new Exception("Completed station remains blocked.");
            report.AppendLine(station.name+": Key=Reachable Return=Exact Gates=Open OriginalShadow=Exact Props=Stable ScreenWidth="+width.ToString("F3"));
        }

        static List<Vector2> FindRoute(PerformanceStation station)
        {
            var start=(Vector2)station.Screen.InverseTransformPoint(station.ProjectedActorFoot);
            var planningSize=station.ActorCollisionHalfSize+Vector2.one*.03f;
            var chest=(Vector2)station.Screen.InverseTransformPoint(station.ProjectedActorChest)-start;
            var target=(Vector2)station.Screen.InverseTransformPoint(station.key.position)-chest;
            if(!station.enableShadowCollision || station.shadowCollision==null)return new List<Vector2>{target};
            const float cell=.16f;
            var min=station.ClampFoot(new Vector2(-100,-100));var max=station.ClampFoot(new Vector2(100,100));
            int width=Mathf.CeilToInt((max.x-min.x)/cell)+1,height=Mathf.CeilToInt((max.y-min.y)/cell)+1;
            int Id(Vector2 value)=>Mathf.Clamp(Mathf.RoundToInt((value.y-min.y)/cell),0,height-1)*width+Mathf.Clamp(Mathf.RoundToInt((value.x-min.x)/cell),0,width-1);
            Vector2 Point(int id)=>station.ClampFoot(min+new Vector2(id%width,id/width)*cell);
            int first=Id(start),last=-1;var queue=new Queue<int>();var parents=new Dictionary<int,int>();queue.Enqueue(first);parents[first]=-1;
            while(queue.Count>0)
            {
                int id=queue.Dequeue();var here=id==first?start:Point(id);
                var worldFoot=station.projector.lens.position+(station.Screen.TransformPoint(new Vector3(here.x,here.y,0))-station.projector.lens.position)/station.ProjectionScale;
                var projectedChest=station.ProjectWorldPoint(worldFoot+station.stageActor.up*station.actorChestHeight);
                var keyOffset=station.key.position-projectedChest;
                var pickupOffset=new Vector2(Vector3.Dot(keyOffset,station.Screen.right),Vector3.Dot(keyOffset,station.Screen.up));
                if(pickupOffset.magnitude<station.pickupRadius*.65f){last=id;break;}
                foreach(var step in new[]{Vector2Int.left,Vector2Int.right,Vector2Int.up,Vector2Int.down})
                {
                    int x=id%width+step.x,y=id/width+step.y;if(x<0||x>=width||y<0||y>=height)continue;
                    int next=y*width+x;if(parents.ContainsKey(next))continue;var point=Point(next);
                    if(station.shadowCollision.Overlaps(point,planningSize))continue;
                    if(Vector2.Distance(station.shadowCollision.Move(here,point-here,planningSize),point)>.02f)continue;
                    parents[next]=id;queue.Enqueue(next);
                }
            }
            if(last<0)throw new Exception("No collision-free route to key in "+station.name);
            var route=new List<Vector2>();for(int id=last;id!=first;id=parents[id])route.Add(Point(id));route.Reverse();return route;
        }

        static PerformanceSequence Sequence()
        {
            if(!EditorApplication.isPlaying) throw new InvalidOperationException("Performance review runs only in Play mode.");
            var sequence=UnityEngine.Object.FindObjectOfType<PerformanceSequence>();
            if(sequence==null) throw new InvalidOperationException("No PerformanceSequence in the scene.");
            sequence.InitializeSession(); return sequence;
        }

        static void PrepareAtLever(PerformanceSequence sequence,PerformanceStation station)
        {
            if(station.lever==null || station.activityArea==null) throw new Exception("Lever or activity area missing.");
            // Arm the region through its actual containment rule, then stand by the lever.
            MoveRealityPlayer(sequence,new Vector3(station.activityArea.bounds.center.x,sequence.worldPlayer.transform.position.y,station.activityArea.bounds.center.z));
            sequence.RefreshProgress();
            var leverPoint=station.lever.position-station.Screen.forward*.65f;
            leverPoint.y=sequence.worldPlayer.transform.position.y;
            MoveRealityPlayer(sequence,leverPoint); sequence.RefreshProgress();
        }

        static void MoveRealityPlayer(PerformanceSequence sequence,Vector3 position)
        {
            var motor=sequence.worldPlayer.GetComponent<CharacterController>();
            bool enabled=motor.enabled; motor.enabled=false;
            sequence.worldPlayer.transform.position=position;
            motor.enabled=enabled; Physics.SyncTransforms();
        }

        static void AdvanceTransition(PerformanceSequence sequence)
        {
            int limit=Mathf.CeilToInt(sequence.transitionDuration*60)+5;
            for(int i=0;i<limit && (sequence.CurrentMode==PerformanceSequence.Mode.Entering || sequence.CurrentMode==PerformanceSequence.Mode.Returning);i++)
                sequence.Step(Vector2.zero,1f/60f);
        }

        static float ValidatePerformanceView(PerformanceSequence sequence,PerformanceStation station)
        {
            if(sequence.CurrentMode!=PerformanceSequence.Mode.Performing) throw new Exception("Entry transition did not finish.");
            if(Vector3.Dot(sequence.view.transform.forward,station.Screen.forward)<.99999f)
                throw new Exception("Performance view is not frontal to the cloth.");
            var size=station.ScreenSize;
            float minX=1,maxX=0,minY=1,maxY=0;
            foreach(float x in new[]{-.5f,.5f}) foreach(float y in new[]{-.5f,.5f})
            {
                var point=sequence.view.WorldToViewportPoint(station.Screen.TransformPoint(new Vector3(x*size.x,y*size.y,0)));
                minX=Mathf.Min(minX,point.x); maxX=Mathf.Max(maxX,point.x);
                minY=Mathf.Min(minY,point.y); maxY=Mathf.Max(maxY,point.y);
            }
            if(maxX-minX<.95f || maxY-minY<.95f || minX<.001f || maxX>.999f || minY<.001f || maxY>.999f)
                throw new Exception("Curtain is cropped or does not fill enough of the view.");
            return maxX-minX;
        }

        static Snapshot Capture(PerformanceSequence sequence,PerformanceStation station)
        {
            var camera=sequence.view;
            var follow=camera.GetComponent<StageCameraFollow>(); var focus=camera.GetComponent<StageDepthOfField>();
            return new Snapshot {playerPosition=sequence.worldPlayer.transform.position,playerRotation=sequence.worldPlayer.transform.rotation,
                cameraPosition=camera.transform.position,cameraRotation=camera.transform.rotation,fov=camera.fieldOfView,
                size=camera.orthographicSize,orthographic=camera.orthographic,culling=camera.cullingMask,
                clearFlags=camera.clearFlags,background=camera.backgroundColor,
                followEnabled=follow!=null && follow.enabled,focusEnabled=focus!=null && focus.enabled,
                controllerEnabled=sequence.worldPlayer.enabled,visualsActive=sequence.worldPlayer.visuals.gameObject.activeSelf,
                casterLayers=station.projector.casterLayers,
                actorLayers=sequence.worldPlayer.visuals.GetComponentsInChildren<Renderer>(true).Select(r=>r.gameObject.layer).ToArray(),
                shadows=ReadShadows(station.projector),propShadows=ReadShadowsWithoutActors(sequence,station)};
        }

        static void AssertRestored(PerformanceSequence sequence,PerformanceStation station,Snapshot expected)
        {
            var camera=sequence.view;
            if(Vector3.Distance(sequence.worldPlayer.transform.position,expected.playerPosition)>.001f ||
                Quaternion.Angle(sequence.worldPlayer.transform.rotation,expected.playerRotation)>.001f ||
                Vector3.Distance(camera.transform.position,expected.cameraPosition)>.001f ||
                Quaternion.Angle(camera.transform.rotation,expected.cameraRotation)>.001f ||
                Mathf.Abs(camera.fieldOfView-expected.fov)>.001f || camera.orthographic!=expected.orthographic ||
                Mathf.Abs(camera.orthographicSize-expected.size)>.001f || camera.cullingMask!=expected.culling ||
                camera.clearFlags!=expected.clearFlags || camera.backgroundColor!=expected.background)
                throw new Exception("Reality camera or player authored state was not restored exactly.");
            var follow=camera.GetComponent<StageCameraFollow>(); var focus=camera.GetComponent<StageDepthOfField>();
            if((follow!=null && follow.enabled)!=expected.followEnabled || (focus!=null && focus.enabled)!=expected.focusEnabled ||
                sequence.worldPlayer.enabled!=expected.controllerEnabled || sequence.worldPlayer.visuals.gameObject.activeSelf!=expected.visualsActive)
                throw new Exception("Reality controls, focus, or actor visibility were not restored.");
            var layers=sequence.worldPlayer.visuals.GetComponentsInChildren<Renderer>(true).Select(r=>r.gameObject.layer);
            if(!layers.SequenceEqual(expected.actorLayers) || (int)station.projector.casterLayers!=expected.casterLayers)
                throw new Exception("Original actor layers or projector caster mask were not restored.");
        }

        static void ValidateEntry(PerformanceSequence sequence,PerformanceStation station,Snapshot expected)
        {
            if(sequence.CurrentMode!=PerformanceSequence.Mode.Entering || !sequence.worldPlayer.visuals.gameObject.activeSelf)
                throw new Exception("The real player disappeared before the camera approach.");
            if(Vector3.Distance(sequence.view.transform.position,expected.cameraPosition)>.0001f ||
                Quaternion.Angle(sequence.view.transform.rotation,expected.cameraRotation)>.001f ||
                sequence.view.backgroundColor!=expected.background || sequence.view.clearFlags!=expected.clearFlags ||
                sequence.view.cullingMask!=(expected.culling & ~(1<<PerformanceStation.ActorLayer)))
                throw new Exception("Entry changes the camera or background abruptly.");
            var source=sequence.worldPlayer.puppet;
            var clone=station.stagePuppet;
            for(int part=0;part<source.partRenderers.Length;part++)
            {
                if(source.partRenderers[part].enabled!=clone.partRenderers[part].enabled)
                    throw new Exception("Projected actor lost a visible character part.");
                var a=source.partMeshes[part]; var b=clone.partMeshes[part];
                var av=a.sharedMesh.vertices; var bv=b.sharedMesh.vertices;
                if(av.Length!=bv.Length) throw new Exception("Projected actor has a different pose mesh.");
                for(int vertex=0;vertex<av.Length;vertex++)
                    if(Vector3.Distance(a.transform.TransformPoint(av[vertex]),b.transform.TransformPoint(bv[vertex]))>.0001f)
                        throw new Exception("Projected actor does not start at the exact original silhouette pose.");
                if(clone.partRenderers[part].gameObject.layer!=PerformanceStation.ActorLayer)
                    throw new Exception("A stage actor part is not on its isolated caster layer.");
            }
            AssertSameShadows(expected.shadows,ReadShadows(station.projector),"Normal projection changed at performance entry.");
            sequence.Step(Vector2.zero,sequence.transitionDuration*.5f);
            if(sequence.CurrentMode!=PerformanceSequence.Mode.Entering || !sequence.worldPlayer.visuals.gameObject.activeSelf ||
                sequence.view.cullingMask!=(expected.culling & ~(1<<PerformanceStation.ActorLayer)) ||
                sequence.view.backgroundColor!=expected.background)
                throw new Exception("World visibility is switched before the approach completes.");
            AssertSameShadows(expected.propShadows,ReadShadowsWithoutActors(sequence,station),"World fade changed static prop projection.");
        }

        static Color32[] ReadShadows(PaperProjector projector)
        {
            projector.RenderPreview();
            var previous=RenderTexture.active;
            var target=projector.shadowTexture;
            var pixels=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false,true);
            try
            {
                RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0); pixels.Apply();
                return pixels.GetPixels32();
            }
            finally { RenderTexture.active=previous; UnityEngine.Object.DestroyImmediate(pixels); }
        }

        static Color32[] ReadShadowsWithoutActors(PerformanceSequence sequence,PerformanceStation station)
        {
            var real=sequence.worldPlayer.visuals.gameObject;
            var actor=station.stageActor.gameObject;
            bool realActive=real.activeSelf,actorActive=actor.activeSelf;
            try { real.SetActive(false); actor.SetActive(false); return ReadShadows(station.projector); }
            finally { real.SetActive(realActive); actor.SetActive(actorActive); station.projector.RenderPreview(); }
        }

        static void AssertSameShadows(Color32[] expected,Color32[] actual,string message)
        {
            if(expected.Length!=actual.Length) throw new Exception(message);
            long delta=0;
            for(int i=0;i<expected.Length;i++)
                delta+=Math.Abs(expected[i].r-actual[i].r)+Math.Abs(expected[i].g-actual[i].g)+
                    Math.Abs(expected[i].b-actual[i].b)+Math.Abs(expected[i].a-actual[i].a);
            if(delta>expected.Length*.05f) throw new Exception(message+" Mean byte error="+(delta/(double)expected.Length).ToString("F4"));
        }
    }
}
