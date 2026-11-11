using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Palworks.Performance;

namespace Palworks.Editor
{
    public static class PerformanceContinuityRepair
    {
        const string Art="Assets/Art/Stage/";
        public static void Apply()
        {
            if(EditorApplication.isPlaying) throw new Exception("Exit Play before repairing the authored scene.");
            var scene=SceneManager.GetActiveScene();
            var sequence=UnityEngine.Object.FindObjectOfType<PerformanceSequence>();
            if(scene.path!=PaperRoomBuilder.ScenePath || sequence==null) throw new Exception("Open the existing performance scene.");
            string backup="Assets/Scenes/Backups/PaperRoomBeforeContinuity"+DateTime.Now.ToString("yyyyMMddHHmmss")+".unity";
            if(!EditorSceneManager.SaveScene(scene,backup,true)) throw new Exception("Scene backup failed.");
            var excluded=new HashSet<int>(sequence.stations.SelectMany(s=>s.lever.GetComponentsInChildren<Transform>(true)).Where(t=>t.parent!=null && sequence.stations.All(s=>s.lever!=t)).Select(t=>t.GetInstanceID()));
            var before=Snapshot(scene,excluded);
            var tags=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layer=tags.FindProperty("layers").GetArrayElementAtIndex(10);
            if(!string.IsNullOrEmpty(layer.stringValue) && layer.stringValue!="PerformanceCaster") throw new Exception("Layer 10 is already in use.");
            layer.stringValue="PerformanceCaster"; tags.ApplyModifiedProperties();
            string path="Assets/Art/Stage/Textures/Lever.png";
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null) throw new Exception("Original mechanical switch artwork is missing.");
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.spritePixelsPerUnit=100;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.npotScale=TextureImporterNPOTScale.None;
            var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);
            settings.spriteAlignment=(int)SpriteAlignment.Center;settings.spriteMeshType=SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);importer.SaveAndReimport();
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path);
            var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Stage/Materials/Lever.mat");
            if(material==null)
            {
                material=new Material(Shader.Find("Palworks/StageSprite")){name="Lever"};material.SetFloat("_Neutral",1);
                AssetDatabase.CreateAsset(material,"Assets/Art/Stage/Materials/Lever.mat");
            }
            sequence.transitionDuration=1.6f;
            foreach(var station in sequence.stations)
            {
                // Preserve the authored root, its collider and all surrounding props.
                foreach(var child in station.lever.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(child.gameObject);
                var visual=new GameObject("Visual").transform;visual.SetParent(station.lever,false);
                visual.localPosition=new Vector3(0,.56f,0);visual.localRotation=Quaternion.Euler(16,0,0);
                visual.localScale=Vector3.one*(1.12f/sprite.bounds.size.y);
                var renderer=visual.gameObject.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.sharedMaterial=material;
                station.leverHandle=visual;station.leverPullAngle=-5;
                foreach(var t in station.stageActor.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=10;
                EditorUtility.SetDirty(station);
            }
            ProjectionSoftnessReview.Install();
            var after=Snapshot(scene,excluded);var changed=before.Where(k=>!after.TryGetValue(k.Key,out var value)||value!=k.Value).Select(k=>k.Value).ToArray();
            Directory.CreateDirectory("output/scene-review");
            File.WriteAllText("output/scene-review/continuity-preservation.txt","PreservedExistingRecords="+(before.Count-changed.Length)+"\nUnexpectedChanges="+changed.Length+"\n"+string.Join("\n",changed));
            if(changed.Length>0) throw new Exception("An authored transform, material, or color unexpectedly changed.");
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            PaperRoomBuilder.Capture();
        }
        static Dictionary<int,string> Snapshot(Scene scene,HashSet<int> excluded)
        {
            var map=new Dictionary<int,string>();
            foreach(var root in scene.GetRootGameObjects()) foreach(var t in root.GetComponentsInChildren<Transform>(true))
            {
                if(excluded.Contains(t.GetInstanceID())) continue;
                string value=t.name+"|"+t.localPosition.ToString("R")+"|"+t.localRotation.ToString("R")+"|"+t.localScale.ToString("R")+"|"+t.gameObject.activeSelf;
                foreach(var r in t.GetComponents<Renderer>())
                {
                    value+="|"+r.enabled+"|"+string.Join(",",r.sharedMaterials.Select(AssetDatabase.GetAssetPath));
                    if(r is SpriteRenderer sr) value+="|"+sr.color.ToString("R")+"|"+AssetDatabase.GetAssetPath(sr.sprite);
                }
                foreach(var c in t.GetComponents<Collider>()) value+="|"+EditorJsonUtility.ToJson(c);
                map[t.GetInstanceID()]=value;
            }
            return map;
        }
    }
}
