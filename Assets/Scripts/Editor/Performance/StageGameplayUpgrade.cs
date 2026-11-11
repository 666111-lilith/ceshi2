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
using Palworks.Projection;
using Palworks.Performance;

namespace Palworks.Editor
{
    public static class StageGameplayUpgrade
    {
        const string Env="Assets/Art/Environment";
        public static void Audit()
        {
            Physics.SyncTransforms();
            var b=new StringBuilder();
            foreach(var c in UnityEngine.Object.FindObjectsOfType<Collider>(true))
            {
                b.AppendLine(PathOf(c.transform)+" | "+c.GetType().Name+" enabled="+c.enabled+" trigger="+c.isTrigger+" bounds="+c.bounds);
                var root=c.transform.parent!=null?c.transform.parent:c.transform;
                foreach(var r in root.GetComponentsInChildren<Renderer>(true))
                    if(Vector3.Distance(r.bounds.center,c.bounds.center)<5)
                        b.AppendLine("  "+PathOf(r.transform)+" enabled="+r.enabled+" bounds="+r.bounds);
            }
            Directory.CreateDirectory("output/scene-review");File.WriteAllText("output/scene-review/stage-colliders.txt",b.ToString());
        }
        static string PathOf(Transform t)=>t.parent!=null?PathOf(t.parent)+"/"+t.name:t.name;
        public static void Apply()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play before authoring.");
            var scene=SceneManager.GetActiveScene();
            if(scene.path!=PaperRoomBuilder.ScenePath)throw new InvalidOperationException("Open the current room.");
            if(GameObject.Find("ProjectionScenery")!=null)throw new InvalidOperationException("Upgrade already applied; edit its authored objects instead.");
            EditorSceneManager.SaveScene(scene,"Assets/Scenes/Backups/BeforeGameplay"+DateTime.Now.ToString("yyyyMMddHHmmss")+".unity",true);
            var originals=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToDictionary(t=>t,t=>t.localPosition.ToString("R")+t.localRotation.ToString("R")+t.localScale.ToString("R"));
            var sequence=UnityEngine.Object.FindObjectOfType<PerformanceSequence>();
            foreach(var sprite in UnityEngine.Object.FindObjectsOfType<SpriteRenderer>())
            {
                if(sprite.gameObject.layer!=8 || sprite.GetComponentInParent<ProjectionCasterStyle>()!=null)continue;
                string rootName=sprite.transform.root.name;
                if(rootName=="Player" || rootName=="Performance")continue;
                string label=sprite.name.ToLowerInvariant();
                bool solid=rootName=="Obstacles" || label.Contains("rock") || label.Contains("stump") || label.Contains("cart") || label.Contains("candle") || label.Contains("lantern");
                var style=sprite.gameObject.AddComponent<ProjectionCasterStyle>();
                style.solid=solid;style.depthLayer=solid?ProjectionCasterStyle.DepthLayer.Solid:ProjectionCasterStyle.DepthLayer.Middle;
                style.opacity=solid?1:.5f;style.softnessScale=solid?1:1.8f;
            }
            var group=new GameObject("ProjectionScenery").transform;
            foreach(var station in sequence.stations)
            {
                var p=station.projector;
                p.shadowOpacity=.96f;
                var material=p.screenRenderer.sharedMaterial;
                material.SetColor("_BaseColor",new Color(.88f,.88f,.88f));
                material.SetColor("_ShadowColor",new Color(.035f,.043f,.052f));
                var grain=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Environment/Textures/Lighting/SmoothNoise.png");
                if(grain!=null)material.SetTexture("_PaperGrain",grain);
                material.SetColor("_AmbientColor",new Color(.19f,.16f,.12f));
                material.SetFloat("_LampFalloff",.7f);EditorUtility.SetDirty(material);
                var root=new GameObject(station.name).transform;root.SetParent(group,false);
                var back=new GameObject("Background").transform;back.SetParent(root,false);
                var middle=new GameObject("Middle").transform;middle.SetParent(root,false);
                var solid=new GameObject("Solid").transform;solid.SetParent(root,false);
                // Reuse source cards at different physical distances from this lamp.
                Card("TreeLeft",back,p,"TreeCanopy",new Vector2(-4.9f,-2.65f),6.1f,.37f,ProjectionCasterStyle.DepthLayer.Background,.32f,3.5f,false);
                Card("TreeRight",back,p,"TreeCanopySmall",new Vector2(4.6f,-2.7f),5.8f,.40f,ProjectionCasterStyle.DepthLayer.Background,.32f,3.5f,false);
                Card("TreeCenter",back,p,"TreeCanopySmall",new Vector2(.7f,-2.7f),4.7f,.42f,ProjectionCasterStyle.DepthLayer.Background,.24f,3.1f,false);
                Card("LeftGrowth",middle,p,"Sapling",new Vector2(-4.8f,-2.7f),3.9f,.65f,ProjectionCasterStyle.DepthLayer.Middle,.56f,1.8f,false);
                Card("RightGrowth",middle,p,"TreeCanopy",new Vector2(5.4f,-2.7f),5.8f,.67f,ProjectionCasterStyle.DepthLayer.Middle,.5f,1.8f,false);
                Card("LowGrowth",middle,p,"LeafClump",new Vector2(.6f,-2.7f),1.5f,.6f,ProjectionCasterStyle.DepthLayer.Middle,.42f,2.1f,false);
                Card("LeftPlatform",solid,p,"Stump",new Vector2(-2.35f,-2.67f),1.15f,.88f,ProjectionCasterStyle.DepthLayer.Solid,1,1,true);
                Card("RightPlatform",solid,p,"RockA",new Vector2(1.5f,-2.67f),.85f,.86f,ProjectionCasterStyle.DepthLayer.Solid,1,1,true);
                var collision=station.GetComponent<ShadowCollisionWorld>();
                if(collision==null)collision=station.gameObject.AddComponent<ShadowCollisionWorld>();
                collision.projector=p;collision.Rebuild();
                station.shadowCollision=collision;
                // Keep W/S screen movement while introducing independent jumps and solidity.
                station.enableShadowCollision=true;
                EditorUtility.SetDirty(station);EditorUtility.SetDirty(p);
            }
            RepairColliders();
            SourceLeverUpgrade.ApplyTo(sequence);
            SetupLighting(sequence);
            ProjectionSoftnessReview.Install();
            string[] moved=originals.Where(k=>k.Key!=null && k.Value!=k.Key.localPosition.ToString("R")+k.Key.localRotation.ToString("R")+k.Key.localScale.ToString("R")).Select(k=>PathOf(k.Key)).ToArray();
            File.WriteAllText("output/scene-review/gameplay-preservation.txt","OriginalTransforms="+originals.Count+"\nChangedExistingTransforms="+moved.Length+"\n"+string.Join("\n",moved));
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            Audit();PaperRoomBuilder.Capture();
        }
        static void Card(string name,Transform parent,PaperProjector p,string source,Vector2 foot,float height,float depth,ProjectionCasterStyle.DepthLayer layer,float opacity,float softness,bool isSolid)
        {
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Environment/Textures/"+source+".png");
            if(sprite==null)throw new InvalidOperationException("Missing source sprite "+source);
            var go=new GameObject(name);go.layer=8;go.transform.SetParent(parent,false);
            var r=go.AddComponent<SpriteRenderer>();r.sprite=sprite;
            r.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Environment/Materials/PaperWhite.mat");
            if(r.sharedMaterial==null)r.sharedMaterial=UnityEngine.Object.FindObjectsOfType<SpriteRenderer>().First(x=>x.sharedMaterial!=null && x.sharedMaterial.shader.name=="Palworks/PaperBlockout").sharedMaterial;
            r.color=isSolid?new Color(.65f,.65f,.65f):new Color(.48f,.48f,.48f);
            r.shadowCastingMode=ShadowCastingMode.TwoSided;r.receiveShadows=true;
            float worldHeight=height*Mathf.Abs(p.screen.lossyScale.y)*depth;
            float scale=worldHeight/sprite.bounds.size.y;
            go.transform.rotation=p.screen.rotation;go.transform.localScale=Vector3.one*scale;
            var target=p.screen.TransformPoint(new Vector3(foot.x,foot.y,0));
            var baseline=Vector3.Lerp(p.lens.position,target,depth);
            go.transform.position=baseline-go.transform.up*(sprite.bounds.min.y*scale);
            var style=go.AddComponent<ProjectionCasterStyle>();style.depthLayer=layer;style.opacity=opacity;style.softnessScale=softness;style.solid=isSolid;
            // Scenery is made from cards; only foreground props occupy the floor physically.
            if(isSolid)
            {
                var box=go.AddComponent<BoxCollider>();
                box.center=new Vector3(sprite.bounds.center.x,sprite.bounds.center.y,0);
                box.size=new Vector3(sprite.bounds.size.x*.75f,sprite.bounds.size.y*.8f,.35f/scale);
            }
        }
        static void RepairColliders()
        {
            var report=new StringBuilder();
            foreach(var box in UnityEngine.Object.FindObjectsOfType<BoxCollider>())
            {
                if(box.transform.root.name!="Obstacles" && box.transform.root.name!="Props")continue;
                var visual=box.GetComponentInChildren<SpriteRenderer>();
                if(visual==null && box.name.EndsWith("Collider") && box.transform.parent!=null)
                {
                    var t=box.transform.parent.Find(box.name.Substring(0,box.name.Length-8));
                    if(t!=null)visual=t.GetComponentInChildren<SpriteRenderer>();
                }
                if(visual==null || !visual.enabled || !visual.gameObject.activeInHierarchy)
                {box.enabled=false;report.AppendLine("Disabled orphan "+PathOf(box.transform));continue;}
                var link=box.GetComponent<Palworks.PhysicsSystem.CardObstacleCollider>();
                if(link==null)link=box.gameObject.AddComponent<Palworks.PhysicsSystem.CardObstacleCollider>();
                link.body=box;link.visual=visual;link.Refresh();
                report.AppendLine("Bound "+PathOf(box.transform)+" to "+PathOf(visual.transform));
            }
            File.WriteAllText("output/scene-review/collider-repair.txt",report.ToString());
        }
        static void SetupLighting(PerformanceSequence sequence)
        {
            var dapple=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Environment/Textures/Lighting/Overlay_MottledLight_A.png");
            foreach(var guid in AssetDatabase.FindAssets("t:Material",new[]{Env}))
            {
                var mat=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if(mat.shader.name=="Palworks/PaperBlockout") {mat.SetTexture("_DappleTex",dapple);mat.SetFloat("_DappleStrength",.09f);EditorUtility.SetDirty(mat);}
            }
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.14f,.18f,.26f);RenderSettings.ambientIntensity=1;
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=new Color(.055f,.075f,.12f);RenderSettings.fogStartDistance=25;RenderSettings.fogEndDistance=65;
            foreach(var light in UnityEngine.Object.FindObjectsOfType<Light>())
            {
                if(light.type==LightType.Directional){light.color=new Color(.66f,.78f,1);light.intensity=.6f;light.shadows=LightShadows.Soft;light.shadowStrength=.8f;}
                else {light.color=new Color(1,.65f,.31f);light.intensity=3;light.range=6;}
            }
            var lighting=GameObject.Find("Lighting").transform;
            foreach(var station in sequence.stations)
            {
                var lamp=new GameObject(station.name+"Light");lamp.transform.SetParent(lighting,false);
                lamp.transform.position=station.projector.lens.position+new Vector3(0,1.6f,1.1f);
                var light=lamp.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.88f,.70f);light.intensity=5;station.projector.illuminationLight=light;light.range=9;light.shadows=LightShadows.Soft;light.shadowStrength=.55f;
            }
            foreach(var r in UnityEngine.Object.FindObjectsOfType<SpriteRenderer>())
                if(r.gameObject.layer!=9){r.shadowCastingMode=ShadowCastingMode.TwoSided;r.receiveShadows=true;}
            sequence.view.backgroundColor=new Color(.035f,.05f,.085f);
            sequence.view.allowHDR=true;sequence.view.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            foreach(var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset",new[]{"Assets/Settings"}))
            {
                var asset=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                var serialized=new SerializedObject(asset); serialized.FindProperty("m_AdditionalLightsRenderingMode").intValue=1; serialized.FindProperty("m_AdditionalLightsPerObjectLimit").intValue=8; serialized.FindProperty("m_AdditionalLightShadowsSupported").boolValue=true; serialized.FindProperty("m_SoftShadowsSupported").boolValue=true; serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(asset);
            }
            var profile=ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom=profile.Add<Bloom>(true);bloom.intensity.Override(.18f);bloom.threshold.Override(1.1f);bloom.scatter.Override(.55f);
            var vignette=profile.Add<Vignette>(true);vignette.intensity.Override(.16f);vignette.smoothness.Override(.65f);
            AssetDatabase.CreateAsset(profile,"Assets/Settings/ForestNight.asset");
            foreach(var component in profile.components)AssetDatabase.AddObjectToAsset(component,profile);
            var volume=new GameObject("Atmosphere").AddComponent<Volume>();volume.transform.SetParent(lighting,false);volume.isGlobal=true;volume.priority=5;volume.sharedProfile=profile;
        }
    }
}

