using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Palworks.Character;

namespace Palworks.Editor
{
    public static class PaperRoomRefinement
    {
        const string Root = "Assets/Art/Environment";
        const string Backup = "Assets/Scenes/Backups/PaperRoomBeforeBlockout.unity";
        static readonly Quaternion CardAngle = Quaternion.Euler(16, 0, 0);

        [MenuItem("Palworks/Apply Blockout Refinement")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play before editing.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != PaperRoomBuilder.ScenePath) throw new InvalidOperationException("Open PaperRoom first.");
            Directory.CreateDirectory("Assets/Scenes/Backups");
            if (!File.Exists(Backup) && !EditorSceneManager.SaveScene(scene, Backup, true))
                throw new IOException("Could not preserve the current layout.");
            var roots = scene.GetRootGameObjects();
            foreach (var guide in roots.Where(g => g.GetComponent<RoomGuide>() != null))
                UnityEngine.Object.DestroyImmediate(guide);
            foreach (var go in scene.GetRootGameObjects())
            {
                var name = go.name;
                if (name.Contains("Camera")) go.name = "Camera";
                else if (name.Contains("Ground")) go.name = "Ground";
                else if (name.Contains("Backdrop")) go.name = "Background";
                else if (name.Contains("Trees")) go.name = "Trees";
                else if (name.Contains("Props")) go.name = "Props";
                else if (name.Contains("Foreground")) go.name = "Foreground";
                else if (name.Contains("Lighting")) go.name = "Lighting";
                else if (name.Contains("Character")) go.name = "Player";
                else if (name.Contains("Colliders")) go.name = "Colliders";
            }

            ImportSprites();
            var shader = Shader.Find("Palworks/PaperBlockout");
            if (shader == null) throw new InvalidOperationException("PaperBlockout shader is not ready.");
            var shared = MakeMaterial("PaperBlockout", shader, null, Color.white);
            foreach (var renderer in UnityEngine.Object.FindObjectsOfType<MeshRenderer>().ToArray())
            {
                if (renderer.GetComponentInParent<PuppetAnimator>() != null || renderer.name.Contains("shadow") || renderer.name == "ContactShadow") continue;
                var mat = renderer.sharedMaterial;
                var texture = mat != null ? mat.mainTexture as Texture2D : null;
                var source = texture != null ? AssetDatabase.GetAssetPath(texture) : "";
                if (texture != null && !source.StartsWith("Assets/Art/Environment/Textures/")) continue;
                if (texture != null && (texture.name == "Grass" || texture.name == "GrassSmall"))
                {
                    UnityEngine.Object.DestroyImmediate(renderer.gameObject);
                    continue;
                }
                if (texture != null && renderer.transform.root.name != "Ground")
                    ConvertSprite(renderer, texture, shared);
                else
                {
                    var color = texture == null ? new Color(.66f,.70f,.73f) :
                        texture.name == "Ground" ? new Color(.66f,.69f,.70f) : new Color(.86f,.86f,.83f);
                    var block = MakeMaterial("Blockout" + (texture != null ? texture.name : "Backdrop"), shader, texture, color);
                    renderer.sharedMaterial = block;
                    var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    mesh.colors = Enumerable.Repeat(Color.white, mesh.vertexCount).ToArray();
                    EditorUtility.SetDirty(mesh);
                }
            }
            BuildVegetation(scene, shared);
            ExpandFloor(scene);
            foreach (var player in UnityEngine.Object.FindObjectsOfType<PuppetPlayerController>())
                CharacterFactory.ApplyBlockout(player.gameObject);
            foreach (var root in scene.GetRootGameObjects()) RenameChildren(root.transform);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.73f,.75f,.77f);
            RenderSettings.ambientIntensity = 1;
            foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>())
            {
                light.color = light.type == LightType.Directional ? new Color(1,.98f,.94f) : new Color(1,.89f,.71f);
                light.intensity = light.type == LightType.Directional ? .75f : 1.6f;
                if (light.type == LightType.Directional) light.shadowStrength = .26f;
            }
            Camera.main.backgroundColor = new Color(.72f,.75f,.77f);
            InstallDepthOfField();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save refined scene.");
            AssetDatabase.SaveAssets();
            Inspect(); PaperRoomBuilder.Capture();
            Debug.Log("PALWORKS: blockout refinement saved.");
        }

        static void ImportSprites()
        {
            foreach (var path in Directory.GetFiles("Assets/Art/Environment/Textures", "*.png").Select(p => p.Replace('\\','/')))
            {
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                if (new[] {"Ground", "Soil", "DirtPatch", "Border"}.Contains(Path.GetFileNameWithoutExtension(path))) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100;
                importer.alphaIsTransparency = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.BottomCenter;
                settings.spritePivot = new Vector2(.5f,0);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
        }

        static Material MakeMaterial(string name, Shader shader, Texture texture, Color color)
        {
            string path = "Assets/Art/Environment/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = shader;
            mat.SetTexture("_MainTex", texture != null ? texture : Texture2D.whiteTexture);
            mat.SetColor("_BaseColor",color);
            mat.SetFloat("_Cutoff",.15f);
            mat.renderQueue = 2450;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Color LayerColor(Transform t)
        {
            string root = t.root.name;
            if (root == "Background") return new Color(.69f,.73f,.76f);
            if (root == "Foreground") return new Color(.54f,.60f,.63f);
            if (root == "Trees") return t.name.Contains("Trunk") ? new Color(.78f,.79f,.79f) : new Color(.91f,.92f,.90f);
            return new Color(.97f,.96f,.92f);
        }

        static void ConvertSprite(MeshRenderer renderer, Texture2D texture, Material material)
        {
            var go = renderer.gameObject;
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GetAssetPath(texture));
            if (sprite == null) throw new InvalidOperationException("No sprite for " + texture.name);
            Vector3 oldScale = go.transform.localScale;
            Color color = LayerColor(go.transform);
            var filter = go.GetComponent<MeshFilter>();
            UnityEngine.Object.DestroyImmediate(renderer);
            UnityEngine.Object.DestroyImmediate(filter);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite; sr.sharedMaterial = material; sr.color = color;
            sr.shadowCastingMode = ShadowCastingMode.TwoSided; sr.receiveShadows = true;
            go.transform.localScale = new Vector3(oldScale.x / sprite.bounds.size.x, oldScale.y / sprite.bounds.size.y, oldScale.z);
        }

        static void BuildVegetation(Scene scene, Material material)
        {
            var previous = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Vegetation");
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous);
            var root = new GameObject("Vegetation").transform;
            var back = new GameObject("BackGrass").transform; back.SetParent(root,false);
            var front = new GameObject("FrontGrass").transform; front.SetParent(root,false);
            float[] backX = {-8.8f,-6.1f,-3.6f,-.5f,2.8f,5.5f,8.3f};
            for (int i=0; i<backX.Length; i++)
                GrassCluster(back, i+1, new Vector3(backX[i],.035f,2.5f + (i%3)*.13f), material, false);
            float[] frontX = {-9.0f,-5.5f,-1.7f,2.8f,7.3f};
            for (int i=0; i<frontX.Length; i++)
                GrassCluster(front, i+1, new Vector3(frontX[i],.04f,-7.5f - (i%2)*.8f), material, true);
            GrassCluster(front, 6, new Vector3(-10,.04f,-4.7f),material,true);
            GrassCluster(front, 7, new Vector3(10,.04f,-4.9f),material,true);
        }

        static void GrassCluster(Transform parent, int index, Vector3 position, Material material, bool front)
        {
            var cluster = new GameObject("GrassCluster" + index).transform;
            cluster.SetParent(parent,false); cluster.position = position;
            string[] textures = {"GrassClumpA", "LeafClump", "GrassClumpD", "GrassClumpB", "GrassClumpC"};
            float[] x = {-.76f,-.25f,.25f,.75f,1.13f};
            for (int i=0;i<textures.Length;i++)
            {
                var asset = textures[(i+index)%textures.Length];
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Environment/Textures/" + asset + ".png");
                if (sprite == null) throw new InvalidOperationException("Missing grass sprite: " + asset);
                var go = new GameObject("Grass"+(i+1)); go.transform.SetParent(cluster,false);
                go.transform.localPosition = new Vector3(x[i],0, (i%2)*.18f);
                go.transform.rotation = CardAngle;
                // Each source uses one consistent size. Variety comes from different silhouettes.
                float height = asset == "LeafClump" ? .62f : asset == "GrassClumpD" || asset == "GrassClumpC" ? .84f : .59f;
                if (front) height *= 1.35f;
                go.transform.localScale = Vector3.one * height / sprite.bounds.size.y;
                var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = sprite; sr.sharedMaterial = material;
                sr.color = front ? new Color(.52f,.58f,.61f) : new Color(.88f,.90f,.88f);
                sr.shadowCastingMode = ShadowCastingMode.TwoSided;
            }
        }

        static void ExpandFloor(Scene scene)
        {
            foreach (var renderer in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
            {
                if (renderer.transform.root.name != "Ground") continue;
                if (renderer.name.Contains("土路") || renderer.name == "Path")
                {
                    var t = renderer.transform; t.position = new Vector3(t.position.x,.006f,-4.9f);
                    t.localScale = new Vector3(t.localScale.x,7.7f,t.localScale.z);
                    renderer.name = "Path";
                }
                else if (renderer.name.Contains("碎屑") || renderer.name == "PathEdge")
                {
                    var t=renderer.transform; t.position = new Vector3(t.position.x,.012f,-5.05f);
                    renderer.name = "PathEdge";
                }
                else renderer.name = "Floor";
            }
            foreach (var box in UnityEngine.Object.FindObjectsOfType<BoxCollider>())
            {
                string n = box.name;
                if (n.Contains("Front")) { box.transform.position = new Vector3(0,1,-4.6f); box.name="FrontBoundary"; }
                else if (n.Contains("Back")) { box.transform.position = new Vector3(0,1,2.05f); box.name="BackBoundary"; }
                else if (n.Contains("Left") || n.Contains("Right"))
                {
                    box.transform.position = new Vector3(n.Contains("Left") ? -9.55f : 9.55f,1,-1.275f);
                    box.size = new Vector3(.4f,3,7.1f); box.name = n.Contains("Left") ? "LeftBoundary" : "RightBoundary";
                }
                else if(n.Contains("Floor"))
                { box.transform.position = new Vector3(0,-.15f,-1.3f); box.size = new Vector3(24,.3f,11); box.name = "FloorCollider"; }
            }
        }

        static void RenameChildren(Transform root)
        {
            var used = new System.Collections.Generic.HashSet<string>();
            foreach (Transform child in root)
            {
                string raw = child.name.Split('·')[0];
                raw = Regex.Replace(raw, "[^A-Za-z0-9]", "");
                if (string.IsNullOrEmpty(raw)) raw = "Object";
                string name = raw; int n=2;
                while(!used.Add(name)) name=raw+n++;
                child.name = name;
                if (PrefabUtility.IsPartOfPrefabInstance(child.gameObject))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
                RenameChildren(child);
            }
        }

        static void InstallDepthOfField()
        {
            // The feature is installed by type, keeping this authoring tool independent of its implementation.
            var type = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "PaperDepthOfFieldFeature");
            if (type == null) throw new InvalidOperationException("Depth of field feature is not ready.");
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null) throw new InvalidOperationException("URP is required.");
            var serialized = new SerializedObject(pipeline);
            int index = serialized.FindProperty("m_DefaultRendererIndex").intValue;
            var renderer = serialized.FindProperty("m_RendererDataList").GetArrayElementAtIndex(index).objectReferenceValue as ScriptableRendererData;
            var feature = renderer.rendererFeatures.FirstOrDefault(f => f != null && f.GetType() == type);
            if (feature == null)
            {
                feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
                feature.name = "PaperDepthOfField";
                AssetDatabase.AddObjectToAsset(feature,renderer);
                renderer.rendererFeatures.Add(feature);
            }
            var dof = (Palworks.Rendering.PaperDepthOfFieldFeature)feature;
            dof.settings.shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/PostProcessing/PaperDepthOfField.shader");
            dof.settings.depthTolerance = 8;
            dof.Create();
            dof.settings.enabled = true;
            feature.SetActive(true); EditorUtility.SetDirty(feature); EditorUtility.SetDirty(renderer); renderer.SetDirty();
            if(Camera.main.GetComponent<Palworks.Rendering.StageDepthOfField>()==null)
                Camera.main.gameObject.AddComponent<Palworks.Rendering.StageDepthOfField>();
            Camera.main.GetUniversalAdditionalCameraData().requiresDepthTexture = true;
        }

        public static void Inspect()
        {
            Physics.SyncTransforms();
            var scene=SceneManager.GetActiveScene();
            var b=new StringBuilder();
            b.AppendLine("Scene="+scene.path).AppendLine("Play="+EditorApplication.isPlaying);
            foreach (var root in scene.GetRootGameObjects()) b.AppendLine("Root="+root.name);
            b.AppendLine("Sprites="+UnityEngine.Object.FindObjectsOfType<SpriteRenderer>().Length);
            b.AppendLine("InvalidNames="+scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Count(t=>!Regex.IsMatch(t.name,"^[A-Za-z][A-Za-z0-9]*$")));
            b.AppendLine("MissingScripts="+scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)));
            var bad=UnityEngine.Object.FindObjectsOfType<Renderer>(true).Count(r=>r.sharedMaterial==null || r.sharedMaterial.shader==null || r.sharedMaterial.shader.name.Contains("InternalError"));
            b.AppendLine("MissingMaterials="+bad);
            var player=UnityEngine.Object.FindObjectOfType<PuppetPlayerController>();
            if(player!=null) b.AppendLine("PlayerPosition="+player.transform.position.ToString("F4")).AppendLine("Speed="+player.moveSpeed);
            foreach(var box in UnityEngine.Object.FindObjectsOfType<BoxCollider>()) b.AppendLine(box.name+"="+box.bounds);
            Directory.CreateDirectory("output/scene-review"); File.WriteAllText("output/scene-review/blockout-audit.txt",b.ToString());
        }

        public static void FinalizeView()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play before editing.");
            foreach (var player in UnityEngine.Object.FindObjectsOfType<PuppetPlayerController>())
                CharacterFactory.ApplyBlockout(player.gameObject);
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) RenameChildren(root.transform);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene())) throw new IOException("Could not save scene.");
            Inspect(); PaperRoomBuilder.Capture();
        }
    }
}
