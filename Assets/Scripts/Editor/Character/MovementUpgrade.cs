using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using Palworks.Character;

namespace Palworks.Editor
{
    public static class MovementUpgrade
    {
        public static void Apply()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Movement authoring requires edit mode.");
            var player=UnityEngine.Object.FindObjectOfType<PuppetPlayerController>();
            if(player==null || player.puppet==null) throw new InvalidOperationException("Scene player is missing.");
            string root="Assets/Art/Characters/Jenn";
            var data=JsonUtility.FromJson<PuppetAnimationData>(player.puppet.animationJson.text);
            if(data==null || Array.Find(data.clips,c=>c.name=="Run")==null || Array.Find(data.clips,c=>c.name=="Dash")==null)
                throw new InvalidOperationException("Export original movement clips with Tools/Character/extend_movement.py first.");
            var materialArray=new Material[data.bitmaps.Length];
            Array.Copy(player.puppet.bitmapMaterials,materialArray,Math.Min(player.puppet.bitmapMaterials.Length,materialArray.Length));
            for(int i=0;i<materialArray.Length;i++)
            {
                if(materialArray[i]!=null) continue;
                string shapePath=root+"/Textures/"+data.bitmaps[i].file;
                ImportTexture(shapePath,false);
                string shadePath=root+"/Textures/Blockout/"+Path.GetFileNameWithoutExtension(data.bitmaps[i].file)+"_regions.png";
                Texture2D shades=null;
                if(File.Exists(shadePath))
                {
                    ImportTexture(shadePath,true);
                    shades=AssetDatabase.LoadAssetAtPath<Texture2D>(shadePath);
                }
                string materialPath=root+"/Materials/Part_"+i.ToString("D3")+".mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if(material==null)
                {
                    // Reuse the current project's character shader and preserve every old material.
                    material=new Material(player.puppet.bitmapMaterials[0]);
                    material.name="Part"+i.ToString("D3");
                    AssetDatabase.CreateAsset(material,materialPath);
                }
                float gray=data.bitmaps[i].source.StartsWith("_ItemBoots") ? .8f : .82f;
                material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(shapePath));
                material.SetTexture("_BlockoutShadeMap",shades);
                material.SetColor("_BaseColor",shades!=null?Color.white:new Color(gray,gray,gray,1));
                material.SetFloat("_Opacity",1);
                material.SetFloat("_TransitionOpacity",1);
                EditorUtility.SetDirty(material);
                materialArray[i]=material;
            }
            string prefabPath=AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(player.gameObject));
            if(!string.IsNullOrEmpty(prefabPath))
            {
                var prefab=PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    foreach(var puppet in prefab.GetComponentsInChildren<PuppetAnimator>(true))
                        Extend(puppet,data,materialArray,root);
                    PrefabUtility.SaveAsPrefabAsset(prefab,prefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(prefab); }
            }
            int count=0;
            foreach(var puppet in Resources.FindObjectsOfTypeAll<PuppetAnimator>())
            {
                if(EditorUtility.IsPersistent(puppet) || !puppet.gameObject.scene.IsValid()) continue;
                Extend(puppet,data,materialArray,root); count++;
            }
            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("output/scene-review");
            File.WriteAllText("output/scene-review/movement-upgrade.txt",
                "Animators="+count+" Parts="+data.parts.Length+" Bitmaps="+data.bitmaps.Length+
                " Clips="+data.clips.Length+" PreservedMoveSpeed="+player.moveSpeed+Environment.NewLine+
                "Sprint=Shift Dash=Control Jump=Space; source files listed in MovementSources.json");
        }

        static void ImportTexture(string path,bool linear)
        {
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer==null) throw new FileNotFoundException(path);
            importer.textureType=TextureImporterType.Default;
            importer.alphaSource=linear?TextureImporterAlphaSource.None:TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency=!linear;
            importer.sRGBTexture=!linear;
            importer.mipmapEnabled=false;
            importer.npotScale=TextureImporterNPOTScale.None;
            importer.wrapMode=TextureWrapMode.Clamp;
            importer.filterMode=FilterMode.Bilinear;
            importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        static void Extend(PuppetAnimator puppet,PuppetAnimationData data,Material[] materials,string root)
        {
            var meshes=new List<MeshFilter>(puppet.partMeshes);
            var renderers=new List<MeshRenderer>(puppet.partRenderers);
            for(int i=meshes.Count;i<data.parts.Length;i++)
            {
                var child=new GameObject(System.Text.RegularExpressions.Regex.Replace(data.parts[i].name,"[^A-Za-z0-9]",""));
                child.transform.SetParent(puppet.transform,false);
                child.layer=renderers.Count>0?renderers[0].gameObject.layer:puppet.gameObject.layer;
                var filter=child.AddComponent<MeshFilter>();
                var renderer=child.AddComponent<MeshRenderer>();
                string path=root+"/Meshes/Part_"+i.ToString("D3")+".asset";
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(mesh==null)
                {
                    mesh=new Mesh {name=data.parts[i].name+"Mesh"};
                    mesh.vertices=new Vector3[4];
                    mesh.triangles=new[]{0,1,2,0,2,3};
                    AssetDatabase.CreateAsset(mesh,path);
                }
                filter.sharedMesh=mesh;
                renderer.sharedMaterial=materials[0];
                renderer.shadowCastingMode=ShadowCastingMode.Off;
                renderer.receiveShadows=false;
                renderer.enabled=false;
                meshes.Add(filter); renderers.Add(renderer);
            }
            puppet.partMeshes=meshes.ToArray();
            puppet.partRenderers=renderers.ToArray();
            puppet.bitmapMaterials=materials;
            puppet.ReloadData();
            EditorUtility.SetDirty(puppet);
            if(PrefabUtility.IsPartOfPrefabInstance(puppet)) PrefabUtility.RecordPrefabInstancePropertyModifications(puppet);
        }
    }
}
