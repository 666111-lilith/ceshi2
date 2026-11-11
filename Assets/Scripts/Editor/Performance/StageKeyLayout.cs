using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Palworks.Performance;

namespace Palworks.Editor
{
    public static class StageKeyLayout
    {
        const string Output="output/scene-review/stage-key-layout.json";
        [Serializable] public sealed class Layout { public string scene; public Entry[] entries; }
        [Serializable] public sealed class Entry
        {
            public string station; public int index; public Vector3 originalLocalPosition,localPosition;
            public Vector2 reachableFoot; public int visited; public bool keptOriginal;
        }
        public static void Plan()
        {
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("Plan key positions in a fresh Play session.");
            var sequence=UnityEngine.Object.FindObjectOfType<PerformanceSequence>();
            if(sequence==null || sequence.worldPlayer==null)throw new InvalidOperationException("Missing performance sequence.");
            sequence.InitializeSession();
            if(sequence.CurrentMode!=PerformanceSequence.Mode.Reality || sequence.CurrentStationIndex!=0)
                throw new InvalidOperationException("Restart Play before planning all three keys.");
            var originalPlayer=sequence.worldPlayer.transform.position;
            var originalRotation=sequence.worldPlayer.transform.rotation;
            var keys=sequence.stations.Select(s=>s.key.localPosition).ToArray();
            var entries=new List<Entry>();
            try
            {
                for(int index=0;index<sequence.stations.Length;index++)
                {
                    var station=sequence.stations[index];
                    var position=station.lever.position-station.Screen.forward*.65f;
                    position.y=originalPlayer.y;MovePlayer(sequence,position);
                    sequence.RefreshProgress();
                    if(!sequence.TryEnter(index,false))throw new InvalidOperationException("Cannot enter "+station.name);
                    Advance(sequence);
                    if(sequence.CurrentMode!=PerformanceSequence.Mode.Performing)throw new InvalidOperationException("Entry did not complete.");
                    entries.Add(Find(station,index));
                    if(!sequence.CollectKey())throw new InvalidOperationException("Cannot return from planning "+station.name);
                    Advance(sequence);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(Output));
                File.WriteAllText(Output,JsonUtility.ToJson(new Layout {scene=SceneManager.GetActiveScene().path,entries=entries.ToArray()},true));
            }
            finally
            {
                // Restore the reality state even when geometry leaves no viable target.
                if(sequence.CurrentMode==PerformanceSequence.Mode.Entering)Advance(sequence);
                if(sequence.CurrentMode==PerformanceSequence.Mode.Performing)sequence.CollectKey();
                if(sequence.CurrentMode==PerformanceSequence.Mode.Returning)Advance(sequence);
                for(int i=0;i<keys.Length;i++)sequence.stations[i].key.localPosition=keys[i];
                MovePlayer(sequence,originalPlayer);sequence.worldPlayer.transform.rotation=originalRotation;
            }
        }
        static Entry Find(PerformanceStation station,int index)
        {
            var screen=station.Screen;var world=station.shadowCollision;
            if(world==null || !station.enableShadowCollision)throw new InvalidOperationException("Solid shadow collisions are missing.");
            Vector2 start=screen.InverseTransformPoint(station.ProjectedActorFoot);
            Vector2 chestOffset=(Vector2)screen.InverseTransformPoint(station.ProjectedActorChest)-start;
            Vector3 originalScreen=screen.InverseTransformPoint(station.key.position);
            Vector2 target=originalScreen;
            Vector2 scale=new Vector2(Mathf.Abs(screen.lossyScale.x),Mathf.Abs(screen.lossyScale.y));
            Vector2 half=station.ActorCollisionHalfSize+Vector2.one*.04f;
            var keyRenderer=station.key.GetComponentInChildren<Renderer>(true);
            var keyExtents=keyRenderer!=null?keyRenderer.bounds.extents:new Vector3(.3f,.2f,0);
            Vector2 keyHalf=new Vector2(Mathf.Max(.08f,keyExtents.x/scale.x),Mathf.Max(.08f,keyExtents.y/scale.y))+Vector2.one*.035f;
            bool ClearKey(Vector2 center)=>!world.Overlaps(center-new Vector2(0,keyHalf.y),keyHalf)
                && center.x-keyHalf.x>-station.ScreenSize.x*.5f && center.x+keyHalf.x<station.ScreenSize.x*.5f
                && center.y-keyHalf.y>-station.ScreenSize.y*.5f && center.y+keyHalf.y<station.ScreenSize.y*.5f;
            float Distance(Vector2 a,Vector2 b)=>Vector2.Scale(a-b,scale).sqrMagnitude;
            const float cell=.16f;
            Vector2 min=station.ClampFoot(new Vector2(-100,-100)),max=station.ClampFoot(new Vector2(100,100));
            int width=Mathf.CeilToInt((max.x-min.x)/cell)+1,height=Mathf.CeilToInt((max.y-min.y)/cell)+1;
            int Id(Vector2 point)=>Mathf.Clamp(Mathf.RoundToInt((point.y-min.y)/cell),0,height-1)*width+Mathf.Clamp(Mathf.RoundToInt((point.x-min.x)/cell),0,width-1);
            Vector2 Point(int id)=>station.ClampFoot(min+new Vector2(id%width,id/width)*cell);
            int first=Id(start);var queue=new Queue<int>();var visited=new HashSet<int>();queue.Enqueue(first);visited.Add(first);
            Vector2 best=default;float bestDistance=float.PositiveInfinity;bool keep=false;
            Vector2 startChest=start+chestOffset;
            var steps=new[]{Vector2Int.left,Vector2Int.right,Vector2Int.up,Vector2Int.down};
            float pickupMargin=Mathf.Max(.05f,station.pickupRadius-.10f);
            bool originalClear=ClearKey(target);
            while(queue.Count>0)
            {
                int id=queue.Dequeue();Vector2 here=id==first?start:Point(id);Vector2 chest=here+chestOffset;
                if(originalClear && Distance(chest,target)<=pickupMargin*pickupMargin && !world.Overlaps(here,half))
                {best=here;keep=true;break;}
                // Avoid an automatic pickup at the entrance; keep room for a deliberate approach.
                if(ClearKey(chest) && !world.Overlaps(here,half) && Distance(chest,startChest)>Mathf.Pow(station.pickupRadius+.25f,2))
                {
                    float score=Distance(chest,target);
                    if(score<bestDistance){bestDistance=score;best=here;}
                }
                foreach(var step in steps)
                {
                    int x=id%width+step.x,y=id/width+step.y;
                    if(x<0||x>=width||y<0||y>=height)continue;
                    int next=y*width+x;if(visited.Contains(next))continue;
                    var point=Point(next);
                    if(world.Overlaps(point,half))continue;
                    if(Vector2.Distance(world.Move(here,point-here,half),point)>.012f)continue;
                    visited.Add(next);queue.Enqueue(next);
                }
            }
            if(!keep && float.IsPositiveInfinity(bestDistance))throw new InvalidOperationException("No visible reachable key position in "+station.name);
            Vector3 chosenWorld=keep?station.key.position:screen.TransformPoint(new Vector3(best.x+chestOffset.x,best.y+chestOffset.y,originalScreen.z));
            return new Entry {station=station.name,index=index,originalLocalPosition=station.key.localPosition,
                localPosition=station.key.parent!=null?station.key.parent.InverseTransformPoint(chosenWorld):chosenWorld,
                reachableFoot=best,visited=visited.Count,keptOriginal=keep};
        }
        public static void Apply()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Apply key layout only in Edit mode.");
            var layout=JsonUtility.FromJson<Layout>(File.ReadAllText(Output));
            var scene=SceneManager.GetActiveScene();
            var sequence=UnityEngine.Object.FindObjectOfType<PerformanceSequence>();
            if(layout.scene!=scene.path || sequence==null || layout.entries.Length!=sequence.stations.Length)
                throw new InvalidOperationException("Key layout does not match the active scene.");
            foreach(var entry in layout.entries)
            {
                var station=sequence.stations[entry.index];
                if(station.name!=entry.station || Vector3.Distance(station.key.localPosition,entry.originalLocalPosition)>.001f)
                    throw new InvalidOperationException("The authored key changed after planning: "+entry.station);
            }
            EditorSceneManager.SaveScene(scene,"Assets/Scenes/Backups/BeforeKeyLayout"+DateTime.Now.ToString("yyyyMMddHHmmss")+".unity",true);
            Undo.RecordObjects(sequence.stations.Select(s=>(UnityEngine.Object)s.key).ToArray(),"Place Reachable Keys");
            foreach(var entry in layout.entries)sequence.stations[entry.index].key.localPosition=entry.localPosition;
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        }
        static void MovePlayer(PerformanceSequence sequence,Vector3 position)
        {
            var motor=sequence.worldPlayer.GetComponent<CharacterController>();bool active=motor!=null && motor.enabled;
            if(motor!=null)motor.enabled=false;sequence.worldPlayer.transform.position=position;
            if(motor!=null)motor.enabled=active;Physics.SyncTransforms();
        }
        static void Advance(PerformanceSequence sequence)
        {
            int limit=Mathf.CeilToInt(sequence.transitionDuration*60)+5;
            for(int i=0;i<limit && (sequence.CurrentMode==PerformanceSequence.Mode.Entering || sequence.CurrentMode==PerformanceSequence.Mode.Returning);i++)sequence.Step(Vector2.zero,1f/60f);
        }
    }
}
