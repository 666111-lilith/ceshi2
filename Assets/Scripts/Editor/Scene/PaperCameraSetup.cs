using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Palworks.CameraSystem;
using Palworks.Character;
using Palworks.Rendering;

namespace Palworks.Editor
{
    public static class PaperCameraSetup
    {
        [MenuItem("Palworks/Configure Room Camera")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play before camera authoring.");
            var scene=SceneManager.GetActiveScene();
            if(scene.path!=PaperRoomBuilder.ScenePath) throw new InvalidOperationException("Open PaperRoom first.");
            const string backup="Assets/Scenes/Backups/PaperRoomBeforeCamera.unity";
            Directory.CreateDirectory("Assets/Scenes/Backups");
            if(!File.Exists(backup) && !EditorSceneManager.SaveScene(scene,backup,true)) throw new IOException("Camera backup failed.");
            var player=UnityEngine.Object.FindObjectOfType<PuppetPlayerController>();
            var camera=Camera.main;
            if(player==null || camera==null) throw new InvalidOperationException("Camera and player are required.");

            // All known presets are perspective; 30 degrees is an adaptation of the source
            // MainCamera preset, not a claim that every original combat camera has this FOV.
            if(camera.orthographic)
            {
                float sourceDepth=Vector3.Dot(player.transform.position-camera.transform.position,camera.transform.forward);
                Vector3 focusPlaneCenter=camera.transform.position+camera.transform.forward*sourceDepth;
                float distance=camera.orthographicSize/Mathf.Tan(15*Mathf.Deg2Rad);
                camera.orthographic=false; camera.fieldOfView=30;
                camera.transform.position=focusPlaneCenter-camera.transform.forward*distance;
            }
            var follow=camera.GetComponent<StageCameraFollow>();
            if(follow==null) follow=camera.gameObject.AddComponent<StageCameraFollow>();
            follow.target=player.transform;
            follow.horizontalInfluence=.72f;
            follow.deadZoneWidth=.04f;
            follow.lookAheadDistance=0;
            follow.smoothTime=.42f;
            bool act=UnityEngine.Object.FindObjectsOfType<Palworks.Projection.PaperProjector>().Length==3;
            follow.minCameraX=act?-6.5f:-3; follow.maxCameraX=act?54.5f:3;
            follow.depthInfluence=.25f; follow.depthSmoothTime=.4f;
            follow.ResetRuntimeState();

            PaperFocusSetup.Configure(camera);
            // The ground overlays and the backing sheet should receive light, not cast
            // rectangular shadows across the playable stage as the shadow cascades move.
            foreach(var sheet in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
                if(sheet.transform.root.name=="Ground" || sheet.transform.root.name=="Background")
                    sheet.shadowCastingMode=ShadowCastingMode.Off;
            EditorUtility.SetDirty(camera); EditorUtility.SetDirty(follow);
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save room camera.");
            AssetDatabase.SaveAssets();
            ValidateFollow();
            PaperRoomBuilder.Capture();
            Debug.Log("PALWORKS: clear perspective camera with damped room follow saved.");
        }

        [MenuItem("Palworks/Validate Room Camera")]
        public static void ValidateFollow()
        {
            if(UnityEngine.Object.FindObjectsOfType<Palworks.Projection.PaperProjector>().Length==3)
            { ActCameraReview.Validate(); return; }
            var source=Camera.main;
            var sourceFollow=source!=null?source.GetComponent<StageCameraFollow>():null;
            if(sourceFollow==null || sourceFollow.target==null) throw new InvalidOperationException("StageCameraFollow is not configured.");
            var testObject=new GameObject("CameraValidation") {hideFlags=HideFlags.HideAndDontSave};
            var targetObject=new GameObject("TargetValidation") {hideFlags=HideFlags.HideAndDontSave};
            try
            {
                var camera=testObject.AddComponent<Camera>(); camera.CopyFrom(source); camera.enabled=false; camera.aspect=16f/9f;
                testObject.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);
                targetObject.transform.position=sourceFollow.target.position;
                var follow=testObject.AddComponent<StageCameraFollow>();
                EditorUtility.CopySerialized(sourceFollow,follow); follow.target=targetObject.transform; follow.enabled=false;
                follow.ResetRuntimeState();
                var startTarget=targetObject.transform.position; var startCamera=testObject.transform.position;
                const float dt=1f/60f;
                for(int i=0;i<120;i++)
                    if(Vector3.Distance(follow.Evaluate(startTarget,dt),startCamera)>.0001f) throw new Exception("Camera drifts at rest.");
                var csv=new StringBuilder("time,playerX,playerZ,cameraX,cameraZ,viewportX\n");
                Vector3 target=startTarget, position=startCamera;
                float time=0, largestStep=0, minViewport=1, maxViewport=0, firstFollow=float.NaN;
                Action<Vector3> step=next=>
                {
                    var previous=position; target=next; position=follow.Evaluate(target,dt); time+=dt;
                    testObject.transform.position=position;
                    float viewport=camera.WorldToViewportPoint(target).x;
                    if(Mathf.Abs(position.x-startCamera.x)>.03f && float.IsNaN(firstFollow)) firstFollow=time;
                    largestStep=Mathf.Max(largestStep,Mathf.Abs(position.x-previous.x));
                    minViewport=Mathf.Min(minViewport,viewport); maxViewport=Mathf.Max(maxViewport,viewport);
                    if(position.x<follow.minCameraX-.001f || position.x>follow.maxCameraX+.001f) throw new Exception("Camera exceeded room bounds.");
                    if(Mathf.Abs(position.y-startCamera.y)>.0001f) throw new Exception("Camera height changed.");
                    if(float.IsNaN(position.x) || float.IsInfinity(position.x)) throw new Exception("Invalid camera position.");
                    csv.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0:F4},{1:F4},{2:F4},{3:F4},{4:F4},{5:F4}",time,target.x,target.z,position.x,position.z,viewport));
                };
                while(target.x<8.9f) step(new Vector3(Mathf.Min(8.9f,target.x+4.8f*dt),target.y,target.z));
                for(int i=0;i<60;i++) step(target);
                while(target.x> -8.9f) step(new Vector3(Mathf.Max(-8.9f,target.x-4.8f*dt),target.y,target.z));
                for(int i=0;i<120;i++) step(target);
                float settledX=position.x;
                for(int i=0;i<120;i++) step(target);
                if(Mathf.Abs(position.x-settledX)>.01f) throw new Exception("Camera does not settle after stopping.");
                if(largestStep>.15f) throw new Exception("Camera jump detected.");
                if(firstFollow>.65f) throw new Exception("Follow starts too late.");
                if(maxViewport-minViewport<.25f) throw new Exception("Player is locked to one screen position.");
                if(minViewport<.1f || maxViewport>.9f) throw new Exception("Player is pushed against a screen edge.");
                Vector3 depthTarget=target; depthTarget.z=startTarget.z-4;
                for(int i=0;i<180;i++) step(depthTarget);
                float depthError=Mathf.Abs(position.z-(startCamera.z-4*follow.depthInfluence));
                if(depthError>.01f) throw new Exception("Depth follow ratio is wrong.");
                Directory.CreateDirectory("output/scene-review");
                File.WriteAllText("output/scene-review/camera-motion.csv",csv.ToString());
                File.WriteAllText("output/scene-review/camera-check.txt",
                    "Result=PASS\nProjection="+(source.orthographic?"Orthographic":"Perspective")+"\nFOV="+source.fieldOfView+
                    "\nFollowStartsSeconds="+firstFollow.ToString("F3")+"\nMaxStepAt60FPS="+largestStep.ToString("F4")+
                    "\nPlayerViewportRange="+minViewport.ToString("F3")+".."+maxViewport.ToString("F3")+
                    "\nCameraXBounds="+follow.minCameraX+".."+follow.maxCameraX+"\nDepthError="+depthError.ToString("F6")+"\n");
            }
            finally {UnityEngine.Object.DestroyImmediate(testObject); UnityEngine.Object.DestroyImmediate(targetObject);}
        }

        [MenuItem("Palworks/Capture Camera Positions")]
        public static void CaptureViews()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play first.");
            var camera=Camera.main; var follow=camera.GetComponent<StageCameraFollow>();
            var target=follow.target; Vector3 cameraPosition=camera.transform.position,targetPosition=target.position;
            try
            {
                foreach(float x in new[]{-8.9f,0f,8.9f})
                {
                    camera.transform.position=cameraPosition; target.position=targetPosition; follow.ResetRuntimeState();
                    Vector3 destination=new Vector3(x,targetPosition.y,targetPosition.z);
                    Vector3 current=targetPosition;
                    while(Mathf.Abs(current.x-x)>.001f)
                    {
                        current.x=Mathf.MoveTowards(current.x,x,4.8f/60f);
                        camera.transform.position=follow.Evaluate(current,1f/60f);
                    }
                    for(int i=0;i<90;i++) camera.transform.position=follow.Evaluate(destination,1f/60f);
                    target.position=destination;
                    PaperRoomBuilder.Capture();
                    File.Copy("output/scene-review/paper-room-editor.png","output/scene-review/camera-"+(x<0?"left":x>0?"right":"center")+".png",true);
                }
            }
            finally
            {
                camera.transform.position=cameraPosition; target.position=targetPosition; follow.ResetRuntimeState();
                PaperRoomBuilder.Capture();
            }
        }
    }
}
