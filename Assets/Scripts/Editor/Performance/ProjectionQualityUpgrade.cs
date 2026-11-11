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
    public static class ProjectionQualityUpgrade
    {
        public static void Apply()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play before applying source artwork.");
            var scene=SceneManager.GetActiveScene();
            var sequence=UnityEngine.Object.FindObjectOfType<PerformanceSequence>();
            if(scene.path!=PaperRoomBuilder.ScenePath || sequence==null) throw new InvalidOperationException("Open the existing scene.");
            const string path="Assets/Art/Stage/Textures/LeverShape.png";
            if(!File.Exists(path)) throw new FileNotFoundException("Chosen source artwork is missing.",path);
            string backup="Assets/Scenes/Backups/PaperRoomBeforeProjectionQuality"+DateTime.Now.ToString("yyyyMMddHHmmss")+".unity";
            if(!EditorSceneManager.SaveScene(scene,backup,true)) throw new IOException("Could not back up current scene edits.");
            var visuals=sequence.stations.Select(s=>s.lever.GetComponentInChildren<SpriteRenderer>(true)).ToArray();
            if(visuals.Any(v=>v==null || v.sprite==null)) throw new InvalidOperationException("An authored lever visual is missing.");
            var existing=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
            var excluded=new HashSet<int>(visuals.Select(v=>v.transform.GetInstanceID()));
            var before=existing.ToDictionary(t=>t.GetInstanceID(),Signature);
            var previousHeight=visuals.Select(v=>v.sprite.bounds.size.y).ToArray();
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.spritePixelsPerUnit=100;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;
            importer.npotScale=TextureImporterNPOTScale.None;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.filterMode=FilterMode.Bilinear;
            var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);
            settings.spriteAlignment=(int)SpriteAlignment.Center;settings.spriteMeshType=SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);importer.SaveAndReimport();
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path);
            for(int i=0;i<visuals.Length;i++)
            {
                visuals[i].sprite=sprite;
                visuals[i].transform.localScale*=previousHeight[i]/sprite.bounds.size.y;
                sequence.stations[i].leverPullAngle=-28;
                EditorUtility.SetDirty(sequence.stations[i]);
            }
            ProjectionSoftnessReview.Install();
            var changed=existing.Where(t=>!excluded.Contains(t.GetInstanceID()) && before[t.GetInstanceID()]!=Signature(t)).Select(t=>t.name).ToArray();
            Directory.CreateDirectory("output/scene-review");
            File.WriteAllText("output/scene-review/projection-quality-preservation.txt","PreservedTransforms="+(existing.Length-excluded.Count)+"\nUnexpectedChanges="+changed.Length+"\n"+string.Join("\n",changed));
            if(changed.Length!=0) throw new Exception("Unexpected authored scene changes: "+string.Join(", ",changed));
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            PaperRoomBuilder.Capture();
        }
        static string Signature(Transform t)
        {
            string value=t.name+"|"+(t.parent!=null?t.parent.GetInstanceID():0)+"|"+t.localPosition.ToString("R")+"|"+t.localRotation.ToString("R")+"|"+t.localScale.ToString("R")+"|"+t.gameObject.activeSelf;
            foreach(var r in t.GetComponents<Renderer>())
            {
                value+="|"+r.enabled+"|"+string.Join(",",r.sharedMaterials.Select(AssetDatabase.GetAssetPath));
                if(r is SpriteRenderer sr) value+="|"+sr.color.ToString("R")+"|"+AssetDatabase.GetAssetPath(sr.sprite);
            }
            foreach(var collider in t.GetComponents<Collider>()) value+="|"+EditorJsonUtility.ToJson(collider);
            return value;
        }
    }
}
