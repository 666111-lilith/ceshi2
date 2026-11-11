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
using Palworks.CameraSystem;
using Palworks.Rendering;

namespace Palworks.Editor
{
    public static class PaperActBuilder
    {
        const string Textures="Assets/Art/Environment/Textures/";
        const string Output="output/scene-review/";
        static readonly string[] Sections={"SectionOne","SectionTwo","SectionThree"};
        static readonly Quaternion CardAngle=Quaternion.Euler(16,0,0);
        const int CasterLayer=8;

        [MenuItem("Palworks/Extend To First Act")]
        public static void Apply()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play first.");
            var scene=SceneManager.GetActiveScene();
            if(scene.path!=PaperRoomBuilder.ScenePath) throw new InvalidOperationException("Open PaperRoom first.");
            if(scene.GetRootGameObjects().Any(r=>r.name=="Projection"))
                throw new InvalidOperationException("Act already exists. Edit its scene objects directly.");
            const string backup="Assets/Scenes/Backups/PaperRoomBeforeAct.unity";
            Directory.CreateDirectory("Assets/Scenes/Backups");
            if(!File.Exists(backup) && !EditorSceneManager.SaveScene(scene,backup,true)) throw new IOException("Act backup failed.");
            ConfigureLayer();
            ExtendEnvironment(scene);
            ConfigureGround(scene);
            ConfigureBounds(scene);
            BuildObstacles(scene);
            BuildProjection();
            var player=UnityEngine.Object.FindObjectOfType<PuppetPlayerController>();
            player.transform.position=new Vector3(-1.7f,.03f,-1.5f);
            foreach(var part in player.GetComponentsInChildren<MeshRenderer>(true))
                if(part.GetComponentInParent<PuppetAnimator>()!=null) part.gameObject.layer=CasterLayer;
            var camera=Camera.main;
            var follow=camera.GetComponent<StageCameraFollow>();
            follow.target=player.transform; follow.minCameraX=-6.5f; follow.maxCameraX=54.5f;
            follow.ResetRuntimeState();
            var focus=camera.GetComponent<StageDepthOfField>();
            focus.nearSharpRange=6.35f; focus.farSharpRange=4.2f;
            focus.nearTransition=2.5f;
            PaperFocusSetup.Configure(camera);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Validate();
            RenderProjectionPreviews();
            PaperRoomBuilder.Capture();
            Debug.Log("PALWORKS: first act saved with three physical projection stations and traversable obstacles.");
        }

        static Transform Root(Scene scene,string name)
        {
            var root=scene.GetRootGameObjects().FirstOrDefault(r=>r.name==name);
            return root!=null?root.transform:new GameObject(name).transform;
        }

        static Transform Child(Transform parent,string name)
        {
            var child=new GameObject(name).transform; child.SetParent(parent,false); return child;
        }

        static void ConfigureLayer()
        {
            var tags=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layer=tags.FindProperty("layers").GetArrayElementAtIndex(CasterLayer);
            if(!string.IsNullOrEmpty(layer.stringValue) && layer.stringValue!="ProjectionCaster")
                throw new InvalidOperationException("Layer 8 is already in use.");
            layer.stringValue="ProjectionCaster"; tags.ApplyModifiedProperties();
        }

        static void ExtendEnvironment(Scene scene)
        {
            foreach(string name in new[]{"Background","Trees","Props","Foreground","Vegetation"})
            {
                var root=Root(scene,name);
                var existing=root.Cast<Transform>().ToArray();
                var first=Child(root,Sections[0]);
                foreach(var item in existing) item.SetParent(first,true);
                for(int i=1;i<3;i++)
                {
                    var section=UnityEngine.Object.Instantiate(first.gameObject,root).transform;
                    section.name=Sections[i]; section.localPosition=new Vector3(24*i,0,0);
                }
            }
            // Duplicate only the local fill lights; the directional key remains unique.
            var lighting=Root(scene,"Lighting");
            var fills=lighting.GetComponentsInChildren<Light>().Where(l=>l.type!=LightType.Directional).ToArray();
            for(int i=1;i<3;i++) foreach(var light in fills)
            {
                var clone=UnityEngine.Object.Instantiate(light.gameObject,lighting);
                clone.name=light.name+(i+1); clone.transform.position+=new Vector3(24*i,0,0);
            }
        }

        static void ConfigureGround(Scene scene)
        {
            var ground=Root(scene,"Ground");
            var floor=ground.Find("Floor");
            floor.position=new Vector3(24,floor.position.y,floor.position.z);
            floor.localScale=new Vector3(88,floor.localScale.y,floor.localScale.z);
            var path=ground.Find("Path"); var edge=ground.Find("PathEdge");
            path.position=new Vector3(0,.006f,-6.1f);
            path.localScale=new Vector3(24.2f,9.5f,path.localScale.z);
            edge.position=new Vector3(0,.012f,-6.23f);
            edge.localScale=new Vector3(24.2f,edge.localScale.y,edge.localScale.z);
            var section=Child(ground,Sections[0]); path.SetParent(section,true); edge.SetParent(section,true);
            for(int i=1;i<3;i++)
            {
                var copy=UnityEngine.Object.Instantiate(section.gameObject,ground).transform;
                copy.name=Sections[i]; copy.localPosition=new Vector3(24*i,0,0);
            }
        }

        static void SetBox(Transform parent,string name,Vector3 position,Vector3 size)
        {
            var found=parent.Find(name); var target=found!=null?found:Child(parent,name);
            target.position=position;
            var box=target.GetComponent<BoxCollider>(); if(box==null) box=target.gameObject.AddComponent<BoxCollider>();
            box.center=Vector3.zero; box.size=size;
        }

        static void ConfigureBounds(Scene scene)
        {
            var root=Root(scene,"Colliders");
            SetBox(root,"FloorCollider",new Vector3(24,-.15f,-1.3f),new Vector3(76,.3f,13));
            SetBox(root,"FrontBoundary",new Vector3(24,1,-6.25f),new Vector3(74,3,.3f));
            SetBox(root,"BackBoundary",new Vector3(24,1,3.35f),new Vector3(74,3,.3f));
            SetBox(root,"LeftBoundary",new Vector3(-12,1,-1.45f),new Vector3(.3f,3,10));
            SetBox(root,"RightBoundary",new Vector3(60,1,-1.45f),new Vector3(.3f,3,10));
        }

        static void BuildObstacles(Scene scene)
        {
            var root=Root(scene,"Obstacles");
            var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Environment/Materials/PaperBlockout.mat");
            for(int i=0;i<3;i++)
            {
                var section=Child(root,Sections[i]); section.position=new Vector3(i*24,0,0);
                float alternate=i%2==0?1:-1;
                Obstacle(section,"Stump","Stump",new Vector3(-3.8f,.02f,.8f*alternate),1.25f,new Vector3(1.3f,1.1f,1.15f),material);
                Obstacle(section,"Rock","RockA",new Vector3(2.7f,.02f,.1f),1.08f,new Vector3(1.45f,.8f,1.0f),material);
                Obstacle(section,"Cart","FlowerCart",new Vector3(6.5f,.02f,-2.5f*alternate-.8f),1.75f,new Vector3(1.65f,1.4f,1.2f),material);
                Obstacle(section,"RockCluster","RockB",new Vector3(-8.2f,.02f,-3.4f),1.08f,new Vector3(1.5f,.8f,1.15f),material);
                Obstacle(section,"StumpSmall","Stump",new Vector3(10.6f,.02f,1.35f),1.25f,new Vector3(1.3f,1.1f,1.15f),material);
                for(int j=0;j<10;j++)
                {
                    float x=-10.7f+j*2.3f;
                    float z=new[]{-4.45f,1.85f,-.75f,-3.65f,2.5f}[j%5];
                    var patch=Child(section,"Grass"+(j+1)); patch.localPosition=new Vector3(x,.025f,z);
                    string source=new[]{"GrassClumpA","GrassClumpB","LeafClump","GrassClumpD"}[(j+i)%4];
                    AddCard(patch,source,source=="LeafClump"?.52f:source=="GrassClumpD"?.68f:.47f,material,new Color(.82f,.84f,.82f));
                }
            }
            // Visible existing props within reach also have physical footprints.
            foreach(var sprite in Root(scene,"Props").GetComponentsInChildren<SpriteRenderer>())
            {
                float z=sprite.transform.position.z;
                if(z< -5.9f || z>3.1f) continue;
                float width=Mathf.Clamp(sprite.bounds.size.x*.65f,.3f,2.3f);
                var collision=Child(sprite.transform,sprite.name+"Collider");
                collision.SetParent(sprite.transform.parent,true);
                collision.position=new Vector3(sprite.transform.position.x,0,z);
                var box=collision.gameObject.AddComponent<BoxCollider>();
                box.center=new Vector3(0,.6f,0); box.size=new Vector3(width,1.2f,.6f);
                sprite.gameObject.layer=CasterLayer;
            }
        }

        static void Obstacle(Transform parent,string name,string sprite,Vector3 position,float height,Vector3 footprint,Material material)
        {
            var root=Child(parent,name); root.localPosition=position;
            AddCard(root,sprite,height,material,new Color(.62f,.64f,.63f));
            root.GetComponentInChildren<SpriteRenderer>().gameObject.layer=CasterLayer;
            var collider=root.gameObject.AddComponent<BoxCollider>();
            collider.center=new Vector3(0,footprint.y*.5f,0); collider.size=footprint;
        }

        static void AddCard(Transform parent,string source,float height,Material material,Color color)
        {
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(Textures+source+".png");
            var card=Child(parent,"Visual"); card.rotation=CardAngle;
            card.localScale=Vector3.one*height/sprite.bounds.size.y;
            var renderer=card.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite=sprite; renderer.sharedMaterial=material; renderer.color=color;
            renderer.shadowCastingMode=ShadowCastingMode.TwoSided;
        }

        static void BuildProjection()
        {
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Palworks.Editor.ProjectionStationFactory")).FirstOrDefault(t=>t!=null);
            if(type==null) throw new InvalidOperationException("Projection factory not compiled.");
            var create=type.GetMethod("Create",new[]{typeof(Transform),typeof(int),typeof(float)});
            if(create==null) throw new InvalidOperationException("Projection factory Create API missing.");
            var root=new GameObject("Projection").transform;
            for(int i=0;i<3;i++) create.Invoke(null,new object[]{root,i+1,24f*i});
        }

        public static void RenderProjectionPreviews()
        {
            Palworks.Projection.PaperProjector.RenderAllPreviews();
        }

        [MenuItem("Palworks/Validate First Act")]
        public static void Validate()
        {
            var player=UnityEngine.Object.FindObjectOfType<PuppetPlayerController>();
            var motor=player.GetComponent<CharacterController>(); bool previous=motor.enabled;
            try
            {
                motor.enabled=false; Physics.SyncTransforms();
                const float grid=.4f; const int width=179,depth=23;
                var free=new bool[width,depth]; var seen=new bool[width,depth];
                Func<int,int,Vector3> position=(x,z)=>new Vector3(-11.6f+x*grid,0,-5.8f+z*grid);
                for(int x=0;x<width;x++) for(int z=0;z<depth;z++)
                {
                    var p=position(x,z);
                    free[x,z]=!Physics.CheckCapsule(p+Vector3.up*.3f,p+Vector3.up*1.5f,.25f,~0,QueryTriggerInteraction.Ignore);
                }
                int startX=Mathf.RoundToInt((player.transform.position.x+11.6f)/grid);
                int startZ=Mathf.RoundToInt((player.transform.position.z+5.8f)/grid);
                if(!free[startX,startZ]) throw new Exception("Player spawn overlaps an obstacle.");
                var queue=new Queue<Vector2Int>(); queue.Enqueue(new Vector2Int(startX,startZ)); seen[startX,startZ]=true;
                int count=0; var directions=new[]{Vector2Int.left,Vector2Int.right,Vector2Int.up,Vector2Int.down};
                while(queue.Count>0)
                {
                    var cell=queue.Dequeue(); count++;
                    foreach(var direction in directions)
                    {
                        var next=cell+direction;
                        if(next.x<0 || next.x>=width || next.y<0 || next.y>=depth || !free[next.x,next.y] || seen[next.x,next.y]) continue;
                        seen[next.x,next.y]=true; queue.Enqueue(next);
                    }
                }
                var report=new StringBuilder("ActWidth=72\nStations=3\nObstacleGroups=15\n");
                for(int i=0;i<3;i++) foreach(float z in new[]{-4.6f,2.2f})
                {
                    int x=Mathf.RoundToInt((i*24+11.6f)/grid), row=Mathf.RoundToInt((z+5.8f)/grid);
                    if(!seen[x,row]) throw new Exception("Cannot walk to station "+(i+1)+" at depth "+z);
                    report.AppendLine("Station"+(i+1)+"Depth"+z+"=Reachable");
                }
                int obstacleCount=UnityEngine.Object.FindObjectsOfType<BoxCollider>().Count(c=>c.transform.root.name=="Obstacles");
                if(obstacleCount<15) throw new Exception("Missing obstacle colliders.");
                report.AppendLine("ReachableGridCells="+count).AppendLine("Result=PASS");
                Directory.CreateDirectory(Output); File.WriteAllText(Output+"act-check.txt",report.ToString());
            }
            finally { motor.enabled=previous; Physics.SyncTransforms(); }
            PaperRoomRefinement.Inspect();
        }

        public static void CaptureStations()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play first.");
            var camera=Camera.main; var original=camera.transform.position;
            try
            {
                RenderProjectionPreviews();
                for(int i=0;i<3;i++)
                {
                    camera.transform.position=new Vector3(i*24,original.y,original.z);
                    PaperRoomBuilder.Capture(); File.Copy(Output+"paper-room-editor.png",Output+"act-station"+(i+1)+".png",true);
                }
            }
            finally { camera.transform.position=original; PaperRoomBuilder.Capture(); }
        }

        public static void FinalizeLayout()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play first.");
            var scene=SceneManager.GetActiveScene();
            var follow=Camera.main.GetComponent<StageCameraFollow>();
            follow.minCameraX=-6.5f; follow.maxCameraX=54.5f; follow.maxCompositionOffset=2.75f;
            follow.ResetRuntimeState();
            EditorUtility.SetDirty(follow);
            foreach(var collider in Root(scene,"Obstacles").GetComponentsInChildren<BoxCollider>())
                foreach(var sprite in collider.GetComponentsInChildren<SpriteRenderer>())
                {
                    sprite.color=new Color(.62f,.64f,.63f);
                    collider.size=new Vector3(Mathf.Max(collider.size.x,sprite.bounds.size.x*.7f),collider.size.y,collider.size.z);
                }
            foreach(var sheet in Root(scene,"Background").GetComponentsInChildren<MeshRenderer>())
                if(sheet.name=="Forestbackdrop")
                    sheet.transform.localScale=new Vector3(42,sheet.transform.localScale.y,sheet.transform.localScale.z);
            foreach(var player in UnityEngine.Object.FindObjectsOfType<PuppetPlayerController>())
                CharacterFactory.ApplyBlockout(player.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Validate(); ProjectionReview.Validate(); ActCameraReview.Validate(); CaptureStations();
        }
    }
}
