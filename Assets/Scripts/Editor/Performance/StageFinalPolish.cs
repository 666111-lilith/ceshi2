using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Palworks.Projection;
using Palworks.Performance;
namespace Palworks.Editor
{
    public static class StageFinalPolish
    {
        public static void Apply()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play first.");
            foreach(var station in UnityEngine.Object.FindObjectOfType<PerformanceSequence>().stations)
            {
                var p=station.projector;
                var root=GameObject.Find("ProjectionScenery").transform.Find(station.name);
                foreach(var group in new[]{root.Find("Background"),root.Find("Middle")})
                    foreach(Transform t in group)
                    {
                        float original=Vector3.Dot(t.position-p.lens.position,p.screen.forward)/Vector3.Dot(p.screen.position-p.lens.position,p.screen.forward);
                        float depth=group.name=="Background"?.69f:.82f;
                        float ratio=depth/original;
                        t.position=p.lens.position+(t.position-p.lens.position)*ratio;t.localScale*=ratio;
                        var style=t.GetComponent<ProjectionCasterStyle>();style.softnessScale=group.name=="Background"?6:3;
                    }
                p.screenRenderer.sharedMaterial.SetFloat("_GrainStrength",.05f);
                EditorUtility.SetDirty(p.screenRenderer.sharedMaterial);
                station.shadowCollision.Rebuild();
            }
            AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());PaperRoomBuilder.Capture();
        }
        public static void ValidateJump()
        {
            if(!EditorApplication.isPlaying)throw new InvalidOperationException("Play required.");
            var sequence=UnityEngine.Object.FindObjectOfType<PerformanceSequence>();
            if(sequence.CurrentMode==PerformanceSequence.Mode.Reality)PerformanceReview.EnterFirstPreview();
            var station=sequence.ActiveStation;
            int samples=0;float maxStep=0;
            foreach(var input in new[]{Vector2.zero,Vector2.down*.25f})
            {
                for(int frame=0;frame<120;frame++)
                {
                    var before=(Vector2)station.Screen.InverseTransformPoint(station.ProjectedActorFoot);
                    station.MoveActor(input,1f/60f,false,false,frame==0);
                    var after=(Vector2)station.Screen.InverseTransformPoint(station.ProjectedActorFoot);
                    maxStep=Mathf.Max(maxStep,Vector2.Distance(before,after));samples++;
                    if(Vector2.Distance(before,after)>.3f)throw new Exception("Jump teleported through cloth space.");
                    if(station.shadowCollision.Overlaps(after,station.ActorCollisionHalfSize))throw new Exception("Jump overlaps a solid shadow.");
                }
            }
            File.WriteAllText("output/scene-review/shadow-jump-check.txt","PASS Frames="+samples+" MaxFrameDistance="+maxStep+" StandingJumpAndDownInput=True");
        }
        public static void CleanNames()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit mode required.");
            foreach(var puppet in UnityEngine.Object.FindObjectsOfType<Palworks.Character.PuppetAnimator>(true))
                foreach(var t in puppet.GetComponentsInChildren<Transform>(true))t.name=System.Text.RegularExpressions.Regex.Replace(t.name,"[^A-Za-z0-9]","");
            const string path="Assets/Prefabs/Characters/JennPlayer.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach(var t in root.GetComponentsInChildren<Transform>(true))t.name=System.Text.RegularExpressions.Regex.Replace(t.name,"[^A-Za-z0-9]","");
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());AssetDatabase.SaveAssets();
            PaperRoomRefinement.Inspect();
        }
        public static void VerifyShaders()
        {
            var errors=new System.Text.StringBuilder();
            foreach(var guid in AssetDatabase.FindAssets("t:Shader",new[]{"Assets"}))
            {
                var path=AssetDatabase.GUIDToAssetPath(guid);var shader=AssetDatabase.LoadAssetAtPath<Shader>(path);
                if(ShaderUtil.ShaderHasError(shader)) foreach(var m in ShaderUtil.GetShaderMessages(shader)) errors.AppendLine(path+" "+m.message);
            }
            File.WriteAllText("output/scene-review/shader-check.txt",errors.Length==0?"PASS":errors.ToString());
            if(errors.Length>0)throw new Exception(errors.ToString());
        }
    }
}
