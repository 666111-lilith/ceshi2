using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using Palworks.Performance;
using Palworks.Projection;
namespace Palworks.Editor
{
    public static class ScreenStageUpgrade
    {
        const string TextureRoot="Assets/Art/Environment/Textures/";
        const int SceneryLayer=11;
        public static void Apply()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play first.");
            var seq=UnityEngine.Object.FindObjectOfType<PerformanceSequence>();
            if(seq.stations.Any(s=>s.projector.GetComponent<ClothKeyProjection>()!=null))throw new InvalidOperationException("Screen upgrade already applied.");
            var scene=SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene,"Assets/Scenes/Backups/BeforeScreenPhysics"+DateTime.Now.ToString("yyyyMMddHHmmss")+".unity",true);
            var preserved=scene.GetRootGameObjects().Where(g=>g.name=="Props"||g.name=="Obstacles").SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToDictionary(t=>t,t=>t.localPosition.ToString("R")+t.localRotation.ToString("R")+t.localScale.ToString("R"));
            var tags=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);tags.FindProperty("layers").GetArrayElementAtIndex(SceneryLayer).stringValue="ScreenScenery";tags.ApplyModifiedPropertiesWithoutUndo();
            seq.view.cullingMask&=~(1<<SceneryLayer);seq.framingMargin=1.015f;
            foreach(var station in seq.stations)
            {
                var p=station.projector;Resize(p);
                p.casterLayers|=1<<SceneryLayer;
                var authored=GameObject.Find("ProjectionScenery").transform.Find(station.name);
                foreach(var name in new[]{"Background","Middle"})
                {
                    var old=authored.Find(name);
                    if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
                }
                var backdrop=new GameObject("Backdrop").transform;backdrop.SetParent(authored,false);
                float floor=-p.screenSize.y*.5f+.07f;
                // Virtual cutouts exist only in this lamp feed, keeping the arrangement area clear.
                float[] xs={-5.5f,-3.45f,-1.25f,1.2f,3.6f,5.7f};
                float[] hs={5.6f,4.7f,5.2f,4.5f,5.6f,6.2f};
                for(int i=0;i<xs.Length;i++)
                {
                    var tree=new GameObject("DistantTree"+(i+1)).transform;tree.SetParent(backdrop,false);
                    Card("Trunk",tree,p,i%2==0?"TreeTrunk":"TreeTrunkSmall",new Vector2(xs[i],floor),hs[i]*.48f,.5f,.26f,1.15f,ProjectionCasterStyle.DepthLayer.Background);
                    Card("Canopy",tree,p,i%2==0?"TreeCanopy":"TreeCanopySmall",new Vector2(xs[i],floor+hs[i]*.25f),hs[i]*.75f,.5f,.26f,1.15f,ProjectionCasterStyle.DepthLayer.Background);
                }
                Card("LeftSapling",backdrop,p,"Sapling",new Vector2(-4.8f,floor),4.1f,.66f,.54f,1.15f,ProjectionCasterStyle.DepthLayer.Middle);
                Card("RightSapling",backdrop,p,"Sapling",new Vector2(4.95f,floor),4.8f,.66f,.5f,1.15f,ProjectionCasterStyle.DepthLayer.Middle);
                foreach(float x in new[]{-5.2f,-2.8f,.3f,3.15f,5.5f})
                    Card("Undergrowth"+(int)((x+6)*10),backdrop,p,"GrassClumpB",new Vector2(x,floor),1.15f,.66f,.45f,1.1f,ProjectionCasterStyle.DepthLayer.Middle);
                var key=station.key.GetComponentInChildren<SpriteRenderer>(true);
                key.transform.localScale*=.75f;
                var color=p.gameObject.AddComponent<ClothKeyProjection>();color.projector=p;color.source=key;color.floorInset=.16f;
                station.bottomPadding=.16f;station.jumpHeight=1.65f;
                station.shadowCollision.silhouetteResolution=256;station.shadowCollision.contactSkin=.001f;station.shadowCollision.alphaThreshold=.5f;
                station.shadowCollision.Rebuild();
                // Initial grounded target; gravity review checks it with actual movement.
                station.key.position=p.screen.TransformPoint(new Vector3(4.15f,floor+1.75f,-.02f));
                p.RefreshProjection();color.Refresh();EditorUtility.SetDirty(p);EditorUtility.SetDirty(station);
            }
            int changed=preserved.Count(k=>k.Key==null||k.Value!=k.Key.localPosition.ToString("R")+k.Key.localRotation.ToString("R")+k.Key.localScale.ToString("R"));
            File.WriteAllText("output/scene-review/screen-upgrade-preservation.txt","PreservedPropsAndObstacles="+preserved.Count+"\nUnexpectedTransformChanges="+changed);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();PaperRoomBuilder.Capture();
        }
        static void Resize(PaperProjector p)
        {
            float width=p.screenSize.x*Mathf.Abs(p.screen.lossyScale.x),oldHeight=p.screenSize.y*Mathf.Abs(p.screen.lossyScale.y);
            float height=width/(16f/9),delta=height-oldHeight,ratio=height/oldHeight;
            var filter=p.screen.GetComponent<MeshFilter>();var mesh=UnityEngine.Object.Instantiate(filter.sharedMesh);
            var verts=mesh.vertices;for(int i=0;i<verts.Length;i++)verts[i].y*=ratio;mesh.vertices=verts;mesh.RecalculateBounds();mesh.RecalculateNormals();mesh.name="TallCloth";
            string path="Assets/Art/Stage/Projection/Meshes/TallCloth.asset";
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(existing==null){AssetDatabase.CreateAsset(mesh,path);existing=mesh;}else UnityEngine.Object.DestroyImmediate(mesh);
            filter.sharedMesh=existing;p.screen.position+=p.screen.up*delta*.5f;
            p.screenSize=new Vector2(p.screenSize.x,p.screenSize.y*ratio);
            var box=p.screen.GetComponent<BoxCollider>();if(box!=null)box.size=new Vector3(box.size.x,box.size.y*ratio,box.size.z);
            var curtain=p.screen.parent;
            curtain.Find("TopRail").position+=p.screen.up*delta;
            foreach(string name in new[]{"LeftPost","RightPost"})
            {
                var t=curtain.Find(name);t.position+=p.screen.up*delta*.5f;var scale=t.localScale;scale.y*=ratio;t.localScale=scale;
            }
            p.shadowTexture.Release();p.shadowTexture.width=1280;p.shadowTexture.height=720;p.shadowTexture.Create();EditorUtility.SetDirty(p.shadowTexture);
        }
        static void Card(string name,Transform parent,PaperProjector p,string source,Vector2 foot,float height,float depth,float opacity,float softness,ProjectionCasterStyle.DepthLayer layer)
        {
            var go=new GameObject(name);go.layer=SceneryLayer;go.transform.SetParent(parent,false);
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(TextureRoot+source+".png");if(sprite==null)throw new Exception(source);
            var r=go.AddComponent<SpriteRenderer>();r.sprite=sprite;
            r.sharedMaterial=UnityEngine.Object.FindObjectsOfType<SpriteRenderer>().First(x=>x.sharedMaterial!=null&&x.sharedMaterial.shader.name=="Palworks/PaperBlockout").sharedMaterial;
            r.color=Color.white;r.shadowCastingMode=ShadowCastingMode.Off;
            float scale=height*Mathf.Abs(p.screen.lossyScale.y)*depth/sprite.bounds.size.y;
            go.transform.rotation=p.screen.rotation;go.transform.localScale=Vector3.one*scale;
            go.transform.position=Vector3.Lerp(p.lens.position,p.screen.TransformPoint(new Vector3(foot.x,foot.y,0)),depth)-go.transform.up*sprite.bounds.min.y*scale;
            var style=go.AddComponent<ProjectionCasterStyle>();style.solid=false;style.depthLayer=layer;style.opacity=opacity;style.softnessScale=softness;
        }
    }
}
