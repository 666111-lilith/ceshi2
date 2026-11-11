using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Palworks.Performance;
using Palworks.Projection;
namespace Palworks.Editor {
 public static class ScreenStageReview {
  public static void ApplyKeyPlan(){
   if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play first.");
   var s=UnityEngine.Object.FindObjectOfType<PerformanceSequence>();var data=File.ReadAllText("output/scene-review/gravity-key-plan.txt");
   if(!data.Contains("Result=PASS"))throw new Exception("Complete the gravity key plan first.");
   foreach(var station in s.stations){var m=System.Text.RegularExpressions.Regex.Match(data,"KeySuggestion "+station.name+@" X=([-0-9.]+) Y=([-0-9.]+)");if(!m.Success)throw new Exception(station.name);float x=float.Parse(m.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture),y=float.Parse(m.Groups[2].Value,System.Globalization.CultureInfo.InvariantCulture);station.key.position=station.Screen.TransformPoint(new Vector3(x,y,-.02f));EditorUtility.SetDirty(station.key);station.projector.GetComponent<ClothKeyProjection>()?.Refresh();}
   EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
  }
  public static void Polish(){
   if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play first.");
   var s=UnityEngine.Object.FindObjectOfType<PerformanceSequence>();
   foreach(var station in s.stations){var k=station.projector.GetComponent<ClothKeyProjection>();k.pigment=new Color(1,.66f,.11f,1);k.Refresh();EditorUtility.SetDirty(k);station.shadowCollision.Rebuild();}
   var camera=s.view;float top=0;foreach(var station in s.stations)top=Mathf.Max(top,station.Screen.position.y+station.ScreenSize.y*Mathf.Abs(station.Screen.lossyScale.y)*.5f);
   var point=new Vector3(camera.transform.position.x,top+.2f,s.stations[0].Screen.position.z);var local=camera.transform.InverseTransformPoint(point);float required=2*Mathf.Atan(Mathf.Abs(local.y)/local.z/.94f)*Mathf.Rad2Deg;camera.fieldOfView=Mathf.Max(camera.fieldOfView,required);EditorUtility.SetDirty(camera);
   EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();PaperRoomBuilder.Capture();
  }
  public static void Inspect(){
   var s=UnityEngine.Object.FindObjectOfType<PerformanceSequence>();var report=new StringBuilder();
   report.AppendLine("Mode="+s.CurrentMode+" Camera="+s.view.transform.position+" Fov="+s.view.fieldOfView);
   if(s.ActiveStation!=null){var a=s.ActiveStation;report.AppendLine("MotorFoot="+a.ActorFootPosition.ToString("F5")+" Grounded="+a.IsGrounded+" VerticalVelocity="+a.VerticalVelocity+" ContactGap="+a.GroundContactGap+" Clip="+a.stagePuppet.CurrentClip);}
   foreach(var station in s.stations){
    var p=station.projector;var key=p.GetComponent<ClothKeyProjection>();
    key?.Refresh();var block=new MaterialPropertyBlock();p.screenRenderer.GetPropertyBlock(block);
    report.AppendLine(station.name+" KeyComponent="+(key!=null)+" KeyActive="+station.key.gameObject.activeInHierarchy+" Source="+(key!=null&&key.source!=null?key.source.name:"None")+" Rect="+block.GetVector("_KeyRect")+" Visible="+block.GetFloat("_KeyVisible")+" Texture="+block.GetTexture("_KeyShape")+" KeyWorld="+station.key.position+" KeyLocal="+station.Screen.InverseTransformPoint(station.key.position)+" Screen="+p.screenSize+" Bodies="+station.shadowCollision.BodyCount);
   }
   File.WriteAllText("output/scene-review/screen-bindings.txt",report.ToString());
  }
  public static void Preview(){
   var s=UnityEngine.Object.FindObjectOfType<PerformanceSequence>();
   if(s.CurrentMode==PerformanceSequence.Mode.Reality)PerformanceReview.EnterFirstPreview();
   for(int i=0;i<180;i++)s.ActiveStation.MoveActor(Vector2.zero,1f/60f);
   Inspect();PaperRoomBuilder.Capture();
  }
 }
}
