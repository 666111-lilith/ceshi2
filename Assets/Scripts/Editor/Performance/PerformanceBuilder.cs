using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Palworks.Character;
using Palworks.Performance;
using Palworks.Projection;

namespace Palworks.Editor
{
    public static class PerformanceBuilder
    {
        const string Root="Assets/Art/Stage";
        const string Art="Assets/Art/Stage/Textures/";
        static readonly Quaternion CardAngle=Quaternion.Euler(16,0,0);
        static Material frame;
        static Mesh quad;

        [MenuItem("Palworks/Add Performance Layer")]
        public static void Apply()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play before authoring.");
            var scene=SceneManager.GetActiveScene();
            if(scene.path!=PaperRoomBuilder.ScenePath) throw new InvalidOperationException("Open PaperRoom first.");
            if(UnityEngine.Object.FindObjectOfType<PerformanceSequence>()!=null)
                throw new InvalidOperationException("Performance layer already exists. Edit its saved objects directly.");
            if(Shader.Find("Palworks/StageSprite")==null) throw new InvalidOperationException("StageSprite shader must compile first.");
            var softnessType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Palworks.Editor.ProjectionSoftnessReview")).FirstOrDefault(t=>t!=null);
            if(softnessType?.GetMethod("Install")==null) throw new InvalidOperationException("Projection softness installer must compile first.");
            foreach(string asset in new[]{"Key","Projector","LeverShape"})
                if(!File.Exists(Art+asset+".png")) throw new FileNotFoundException("Source artwork not imported: "+asset);
            var projectors=UnityEngine.Object.FindObjectsOfType<PaperProjector>().OrderBy(p=>p.screen.position.x).ToArray();
            if(projectors.Length!=3) throw new InvalidOperationException("Expected the three authored projectors.");
            var player=UnityEngine.Object.FindObjectOfType<PuppetPlayerController>();
            if(player==null) throw new InvalidOperationException("Player is required.");
            string backup="Assets/Scenes/Backups/PaperRoomBeforePerformance"+DateTime.Now.ToString("yyyyMMddHHmmss")+".unity";
            if(!EditorSceneManager.SaveScene(scene,backup,true)) throw new IOException("Could not preserve the current edited scene.");
            var before=Snapshot(scene);
            var allowed=new HashSet<int>();
            ConfigureLayer(); ImportArt(); PrepareMaterials();
            // Narrow only the existing boundary positions, relative to the user's current values.
            var front=GameObject.Find("Colliders/FrontBoundary").GetComponent<BoxCollider>();
            var back=GameObject.Find("Colliders/BackBoundary").GetComponent<BoxCollider>();
            var left=GameObject.Find("Colliders/LeftBoundary").GetComponent<BoxCollider>();
            var right=GameObject.Find("Colliders/RightBoundary").GetComponent<BoxCollider>();
            foreach(var collider in new[]{front,back,left,right}) allowed.Add(collider.transform.GetInstanceID());
            front.transform.position+=Vector3.forward*.25f; back.transform.position-=Vector3.forward*.25f;
            left.transform.position+=Vector3.right*.3f; right.transform.position-=Vector3.right*.3f;
            Physics.SyncTransforms();
            float frontZ=front.bounds.max.z,backZ=back.bounds.min.z;
            var root=new GameObject("Performance").transform;
            var sequence=root.gameObject.AddComponent<PerformanceSequence>();
            sequence.worldPlayer=player; sequence.view=Camera.main;
            sequence.stations=new PerformanceStation[projectors.Length];
            for(int i=0;i<projectors.Length;i++)
            {
                var projector=projectors[i];
                var curtain=projector.screen.parent;
                allowed.Add(curtain.GetInstanceID());
                curtain.localScale=Vector3.Scale(curtain.localScale,new Vector3(1.10f,1.08f,1));
                SetLayer(curtain,9);
                ReplaceProjectorVisual(projector,allowed);
                var stationRoot=Child(root,"Station"+(i+1));
                var station=stationRoot.gameObject.AddComponent<PerformanceStation>();
                station.projector=projector;
                float x=projector.screen.position.x;
                float minX=i==0?left.bounds.max.x:(projectors[i-1].screen.position.x+x)*.5f;
                float maxX=i==projectors.Length-1?right.bounds.min.x:(x+projectors[i+1].screen.position.x)*.5f;
                var area=Child(stationRoot,"Area");
                area.position=new Vector3((minX+maxX)*.5f,1,(frontZ+backZ)*.5f);
                var volume=area.gameObject.AddComponent<BoxCollider>(); volume.isTrigger=true;
                volume.size=new Vector3(maxX-minX,4,backZ-frontZ); station.activityArea=volume;
                var exit=Gate(stationRoot,"ExitGate",maxX,frontZ,backZ);
                station.gates=new Collider[]{exit.GetComponent<BoxCollider>()};
                station.gateVisuals=exit.GetComponentsInChildren<Renderer>();
                var entry=Gate(stationRoot,"EntranceGate",minX,frontZ,backZ);
                station.entranceGates=new Collider[]{entry.GetComponent<BoxCollider>()};
                station.entranceGateVisuals=entry.GetComponentsInChildren<Renderer>();
                foreach(var collider in station.entranceGates) collider.enabled=false;
                foreach(var renderer in station.entranceGateVisuals) renderer.enabled=false;
                MakeLever(station,stationRoot,frontZ,backZ);
                var visual=UnityEngine.Object.Instantiate(player.visuals.gameObject,stationRoot).transform;
                visual.name="Actor"; visual.SetPositionAndRotation(projector.screen.position,projector.screen.rotation);
                visual.localScale=player.visuals.lossyScale;
                SetLayer(visual,9);
                station.stageActor=visual; station.stagePuppet=visual.GetComponent<PuppetAnimator>();
                visual.gameObject.SetActive(false);
                var key=Child(stationRoot,"Key");
                key.position=projector.screen.TransformPoint(new Vector3(projector.screenSize.x*.30f,-.65f,-.22f));
                key.rotation=projector.screen.rotation;
                var keySprite=SpriteCard(key,"Key",.72f,MaterialAsset("Key",false),new Color(.12f,.82f,1));
                keySprite.sortingOrder=500;
                key.gameObject.layer=9; SetLayer(key,9); key.gameObject.SetActive(false);
                station.key=key;
                sequence.stations[i]=station;
                projector.RefreshProjection();
                EditorUtility.SetDirty(projector);
            }
            InstallProjectionSoftness();
            VerifyPreserved(scene,before,allowed);
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save performance layer.");
            AssetDatabase.SaveAssets();
            PaperRoomRefinement.Inspect(); PaperRoomBuilder.Capture();
            Debug.Log("PALWORKS: performance layer saved on top of the current authored scene.");
        }

        static void ConfigureLayer()
        {
            var tags=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var property=tags.FindProperty("layers").GetArrayElementAtIndex(9);
            if(!string.IsNullOrEmpty(property.stringValue) && property.stringValue!="Performance") throw new Exception("Layer 9 is in use.");
            property.stringValue="Performance"; tags.ApplyModifiedProperties();
        }

        static void ImportArt()
        {
            foreach(string file in Directory.GetFiles(Art,"*.png"))
            {
                string path=file.Replace('\\','/'); AssetDatabase.ImportAsset(path);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType=TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Single;
                importer.spritePixelsPerUnit=100; importer.alphaIsTransparency=true;
                importer.npotScale=TextureImporterNPOTScale.None; importer.mipmapEnabled=false;
                importer.textureCompression=TextureImporterCompression.Uncompressed; importer.filterMode=FilterMode.Bilinear;
                var settings=new TextureImporterSettings(); importer.ReadTextureSettings(settings);
                settings.spriteAlignment=(int)SpriteAlignment.Center; settings.spriteMeshType=SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings); importer.SaveAndReimport();
            }
        }

        static Material MaterialAsset(string name,bool neutral)
        {
            string path="Assets/Art/Stage/Materials/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null)
            {
                material=new Material(Shader.Find("Palworks/StageSprite")){name=name};
                material.SetFloat("_Neutral",neutral?1:0);
                AssetDatabase.CreateAsset(material,path);
            }
            return material;
        }

        static void PrepareMaterials()
        {
            Directory.CreateDirectory("Assets/Art/Stage/Materials"); Directory.CreateDirectory("Assets/Art/Stage/Meshes");
            frame=AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Stage/Projection/Materials/Frame.mat");
            string path="Assets/Art/Stage/Meshes/Quad.asset";
            quad=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(quad==null)
            {
                quad=new Mesh{name="Quad",vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)},
                    uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up},triangles=new[]{0,2,1,0,3,2},colors=Enumerable.Repeat(Color.white,4).ToArray()};
                quad.RecalculateNormals(); quad.RecalculateBounds(); AssetDatabase.CreateAsset(quad,path);
            }
        }

        static Transform Child(Transform parent,string name)
        {var child=new GameObject(name).transform;child.SetParent(parent,false);return child;}
        static void SetLayer(Transform root,int layer)
        {foreach(var item in root.GetComponentsInChildren<Transform>(true)) item.gameObject.layer=layer;}

        static SpriteRenderer SpriteCard(Transform target,string asset,float height,Material material,Color color)
        {
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(Art+asset+".png");
            if(sprite==null) throw new Exception("Missing sprite "+asset);
            target.localScale=Vector3.one*(height/sprite.bounds.size.y);
            var renderer=target.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite=sprite; renderer.sharedMaterial=material; renderer.color=color;
            return renderer;
        }

        static void ReplaceProjectorVisual(PaperProjector projector,HashSet<int> allowed)
        {
            foreach(var renderer in projector.GetComponentsInChildren<Renderer>(true))
            {
                allowed.Add(renderer.GetInstanceID()); renderer.enabled=false;
            }
            var visual=Child(projector.transform,"Visual");
            visual.localPosition=new Vector3(0,.70f,-.08f); visual.localRotation=CardAngle;
            SpriteCard(visual,"Projector",1.35f,MaterialAsset("Projector",true),Color.white);
        }

        static Transform Quad(Transform parent,string name,Vector3 position,Vector2 size,Quaternion rotation)
        {
            var target=Child(parent,name); target.localPosition=position; target.localRotation=rotation;
            target.localScale=new Vector3(size.x,size.y,1);
            target.gameObject.AddComponent<MeshFilter>().sharedMesh=quad;
            var renderer=target.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial=frame;
            renderer.shadowCastingMode=ShadowCastingMode.Off;
            return target;
        }

        static Transform Gate(Transform parent,string name,float x,float front,float back)
        {
            var root=Child(parent,name); root.position=new Vector3(x,0,(front+back)*.5f);
            var collider=root.gameObject.AddComponent<BoxCollider>();
            collider.center=new Vector3(0,1.4f,0); collider.size=new Vector3(.20f,3.2f,back-front+.35f);
            for(int i=0;i<5;i++)
                Quad(root,"Post"+(i+1),new Vector3(0,.35f,Mathf.Lerp(front,back,i/4f)-root.position.z),new Vector2(.12f,.7f),CardAngle);
            Quad(root,"Rope",new Vector3(0,.42f,0),new Vector2(.05f,back-front),Quaternion.Euler(90,0,0));
            return root;
        }

        static void MakeLever(PerformanceStation station,Transform parent,float front,float back)
        {
            var screen=station.Screen;
            float halfWidth=station.ScreenSize.x*Mathf.Abs(screen.lossyScale.x)*.5f;
            var root=Child(parent,"Lever");
            root.position=new Vector3(screen.position.x-halfWidth-.70f,.02f,Mathf.Min(back-.70f,screen.position.z-1.45f));
            if(File.Exists("Assets/Art/Stage/Textures/LeverShape.png"))
            {
                var visual=Child(root,"Visual"); visual.localPosition=Vector3.up*.5f; visual.localRotation=CardAngle;
                SpriteCard(visual,"LeverShape",1.0f,MaterialAsset("Lever",true),Color.white);
                station.leverHandle=visual;
            }
            else
            {
                throw new FileNotFoundException("Use an existing source-art control; procedural replacement art is disabled.","Assets/Art/Stage/Textures/LeverShape.png");
            }
            var collider=root.gameObject.AddComponent<BoxCollider>(); collider.center=new Vector3(0,.4f,0); collider.size=new Vector3(.55f,.8f,.45f);
            station.lever=root;
        }

        static void InstallProjectionSoftness()
        {
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Palworks.Editor.ProjectionSoftnessReview")).FirstOrDefault(t=>t!=null);
            var method=type?.GetMethod("Install");
            if(method==null) throw new InvalidOperationException("Projection softness installer is not ready.");
            method.Invoke(null,null);
        }

        static Dictionary<int,KeyValuePair<string,string>> Snapshot(Scene scene)
        {
            var result=new Dictionary<int,KeyValuePair<string,string>>();
            foreach(var root in scene.GetRootGameObjects()) foreach(var t in root.GetComponentsInChildren<Transform>(true))
            {
                string signature=t.name+"|"+(t.parent!=null?t.parent.GetInstanceID():0)+"|"+t.localPosition.ToString("R")+"|"+t.localRotation.ToString("R")+"|"+t.localScale.ToString("R")+"|"+t.gameObject.activeSelf;
                result[t.GetInstanceID()]=new KeyValuePair<string,string>(PathOf(t),signature);
                foreach(var renderer in t.GetComponents<Renderer>())
                {
                    string visual=renderer.enabled+"|"+string.Join(",",renderer.sharedMaterials.Select(AssetDatabase.GetAssetPath));
                    if(renderer is SpriteRenderer sprite) visual+="|"+AssetDatabase.GetAssetPath(sprite.sprite)+"|"+sprite.color.ToString("R");
                    result[renderer.GetInstanceID()]=new KeyValuePair<string,string>(PathOf(t)+"Renderer",visual);
                }
                foreach(var collider in t.GetComponents<BoxCollider>())
                    result[collider.GetInstanceID()]=new KeyValuePair<string,string>(PathOf(t)+"Collider",
                        collider.center.ToString("R")+"|"+collider.size.ToString("R")+"|"+collider.enabled+"|"+collider.isTrigger);
                foreach(var light in t.GetComponents<Light>())
                    result[light.GetInstanceID()]=new KeyValuePair<string,string>(PathOf(t)+"Light",EditorJsonUtility.ToJson(light));
                foreach(var controller in t.GetComponents<PuppetPlayerController>())
                    result[controller.GetInstanceID()]=new KeyValuePair<string,string>(PathOf(t)+"Controller",EditorJsonUtility.ToJson(controller));
                if(t.GetComponent<Camera>()==Camera.main)
                    result[Camera.main.GetInstanceID()]=new KeyValuePair<string,string>(PathOf(t)+"Camera",EditorJsonUtility.ToJson(Camera.main));
            }
            return result;
        }

        static string PathOf(Transform t)=>t.parent!=null?PathOf(t.parent)+"/"+t.name:t.name;

        static void VerifyPreserved(Scene scene,Dictionary<int,KeyValuePair<string,string>> before,HashSet<int> allowed)
        {
            var after=Snapshot(scene); var changed=new List<string>(); int preserved=0;
            foreach(var item in before)
            {
                if(allowed.Contains(item.Key)) continue;
                if(!after.TryGetValue(item.Key,out var current) || current.Value!=item.Value.Value) changed.Add(item.Value.Key);
                else preserved++;
            }
            Directory.CreateDirectory("output/scene-review");
            File.WriteAllText("output/scene-review/performance-preservation.txt","PreservedExistingRecords="+preserved+"\nUnexpectedChanges="+changed.Count+"\n"+string.Join("\n",changed));
            if(changed.Count>0) throw new Exception("Unexpected change to authored objects: "+string.Join(",",changed));
        }
    }
}
