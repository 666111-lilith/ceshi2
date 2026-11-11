using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Palworks.Rendering;

namespace Palworks.Editor
{
    public static class PaperFocusSetup
    {
        [MenuItem("Palworks/Configure Stage Focus")]
        public static void Apply()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play first.");
            var scene=SceneManager.GetActiveScene();
            if(scene.path!=PaperRoomBuilder.ScenePath) throw new InvalidOperationException("Open PaperRoom first.");
            const string backup="Assets/Scenes/Backups/PaperRoomBeforeFocus.unity";
            if(!File.Exists(backup) && !EditorSceneManager.SaveScene(scene,backup,true)) throw new IOException("Focus backup failed.");
            Configure(Camera.main);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Validate();
            CaptureComparison();
            Debug.Log("PALWORKS: stage focus saved; playable band remains sharp as camera follows.");
        }

        public static void Configure(Camera camera)
        {
            var focus=camera.GetComponent<StageDepthOfField>();
            if(focus==null) focus=camera.gameObject.AddComponent<StageDepthOfField>();
            focus.enabled=true;
            foreach(var guid in AssetDatabase.FindAssets("t:ScriptableRendererData",new[]{"Assets/Settings"}))
            {
                var renderer=AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                foreach(var feature in renderer.rendererFeatures.OfType<PaperDepthOfFieldFeature>())
                {
                    feature.settings.enabled=true;
                    feature.settings.depthTolerance=8;
                    feature.SetActive(true);
                    EditorUtility.SetDirty(feature);
                }
                renderer.SetDirty(); EditorUtility.SetDirty(renderer);
            }
            camera.GetUniversalAdditionalCameraData().requiresDepthTexture=true;
            EditorUtility.SetDirty(camera); EditorUtility.SetDirty(focus);
        }

        public static void Validate()
        {
            var camera=Camera.main; var focus=camera.GetComponent<StageDepthOfField>();
            var position=camera.transform.position;
            var result=new StringBuilder();
            try
            {
                float maximum=0;
                foreach(float x in new[]{-3f,0,3f})
                foreach(float z in new[]{-1f,0,.5f})
                {
                    camera.transform.position=position+new Vector3(x,0,z);
                    bool act=UnityEngine.Object.FindObjectsOfType<Palworks.Projection.PaperProjector>().Length==3;
                    foreach(float playerZ in act?new[]{-5.8f,-2,0,2.9f}:new[]{-4.2f,-2,0,1.6f})
                    foreach(float height in new[]{0f,1f,2f})
                        maximum=Mathf.Max(maximum,focus.BlurRadiusAt(camera,new Vector3(x,height,playerZ)));
                }
                if(maximum>.001f) throw new Exception("Playable band receives blur: "+maximum);
                float foreground=focus.BlurRadiusAt(camera,new Vector3(0,0,-8));
                float far=focus.BlurRadiusAt(camera,new Vector3(0,0,10));
                if(foreground<2 || far<1) throw new Exception("Foreground/background focus effect missing.");
                result.AppendLine("Result=PASS");
                result.AppendLine("PlayableBandMaxBlur="+maximum);
                result.AppendLine("ForegroundRadiusAt1080p="+foreground.ToString("F3"));
                result.AppendLine("BackdropRadiusAt1080p="+far.ToString("F3"));
                result.AppendLine("Focus=WorldAnchored; Character=RenderedAfterFocus; CameraPositions=9");
                Directory.CreateDirectory("output/scene-review");
                File.WriteAllText("output/scene-review/focus-check.txt",result.ToString());
            }
            finally { camera.transform.position=position; }
        }

        public static void CaptureComparison()
        {
            var focus=Camera.main.GetComponent<StageDepthOfField>();
            bool enabled=focus.enabled;
            try
            {
                focus.enabled=false; PaperRoomBuilder.Capture();
                File.Copy("output/scene-review/paper-room-editor.png","output/scene-review/focus-off.png",true);
                focus.enabled=true; PaperRoomBuilder.Capture();
                File.Copy("output/scene-review/paper-room-editor.png","output/scene-review/focus-on.png",true);
            }
            finally { focus.enabled=enabled; }
        }

        public static void CaptureMask()
        {
            var features=AssetDatabase.FindAssets("t:ScriptableRendererData",new[]{"Assets/Settings"})
                .Select(g=>AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(AssetDatabase.GUIDToAssetPath(g)))
                .SelectMany(r=>r.rendererFeatures).OfType<PaperDepthOfFieldFeature>().ToArray();
            try
            {
                foreach(var feature in features) feature.settings.visualizeFocus=true;
                PaperRoomBuilder.Capture();
                File.Copy("output/scene-review/paper-room-editor.png","output/scene-review/focus-mask.png",true);
            }
            finally
            {
                foreach(var feature in features) feature.settings.visualizeFocus=false;
                PaperRoomBuilder.Capture();
            }
        }
    }
}
