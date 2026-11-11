using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Palworks.Editor
{
    /// <summary>Authoring tool. It saves normal editable scene objects, never builds at runtime.</summary>
    [InitializeOnLoad]
    public static class PaperRoomBuilder
    {
        public const string ScenePath = "Assets/Scenes/Palworks_PaperRoom.unity";
        const string Root = "Assets/Art/Environment";
        const string Request = "Temp/PalworksScene.request";
        static readonly Quaternion CardRotation = Quaternion.Euler(16, 0, 0);
        static Mesh cardMesh;
        static double nextPoll;

        static PaperRoomBuilder() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            if (!File.Exists(Request)) return;
            var command = File.ReadAllText(Request).Trim();
            File.Delete(Request);
            try
            {
                if (command == "build") Build();
                else if (command == "build-environment") BuildRoom(true);
                else if (command == "capture") Capture();
                else if (command == "audit") Audit();
                else if (command == "finish") FinishRoom();
                else if (command == "refresh-character") FinishRoom(true);
                else if (command == "refine") PaperRoomRefinement.Apply();
                else if (command == "polish") PaperRoomRefinement.FinalizeView();
                else if (command == "camera") PaperCameraSetup.Apply();
                else if (command == "camera-check") PaperCameraSetup.ValidateFollow();
                else if (command == "camera-views") PaperCameraSetup.CaptureViews();
                else if (command == "focus") PaperFocusSetup.Apply();
                else if (command == "focus-check") { PaperFocusSetup.Validate(); PaperFocusSetup.CaptureComparison(); }
                else if (command == "focus-mask") PaperFocusSetup.CaptureMask();
                else if (command == "walk-check") PaperCharacterReview.CaptureWalk();
                else if (command == "act") PaperActBuilder.Apply();
                else if (command == "act-check") PaperActBuilder.Validate();
                else if (command == "act-views") PaperActBuilder.CaptureStations();
                else if (command == "act-polish") PaperActBuilder.FinalizeLayout();
                else if (command == "projection-check") ProjectionReview.Validate();
                else if (command == "softness-check") ProjectionSoftnessReview.Validate();
                else if (command == "movement-check") ActMovementReview.Validate();
                else if (command == "act-camera-check") ActCameraReview.Validate();
                else if (command == "performance") PerformanceBuilder.Apply();
                else if (command == "continuity") PerformanceContinuityRepair.Apply();
                else if (command == "projection-quality") ProjectionQualityUpgrade.Apply();
                else if (command == "projection-character-check") ProjectionCharacterQualityReview.Validate();
                else if (command == "performance-preview") { PerformanceReview.EnterFirstPreview(); Capture(); }
                else if (command == "performance-check") PerformanceReview.ValidateAll();
                else if (command == "entry-motion") PerformanceReview.CaptureMotion();
                else if (command == "return-motion") PerformanceReview.CaptureReturnMotion();
                else if (command == "asset-plan") AssetOrganization.Plan();
                else if (command == "asset-organize") AssetOrganization.Apply();
                else if (command == "asset-check") AssetOrganization.Validate();
                else if (command == "keys-plan") StageKeyLayout.Plan();
                else if (command == "keys-apply") StageKeyLayout.Apply();
                else if (command == "stage-polish") StageFinalPolish.Apply();
                else if (command == "shadow-jump-check") StageFinalPolish.ValidateJump();
                else if (command == "clean-names") StageFinalPolish.CleanNames();
                else if (command == "shader-check") StageFinalPolish.VerifyShaders();
                else if (command == "flat-lever") FlatLeverRepair.Apply();
                else if (command == "flat-lever-capture") FlatLeverCaptureReview.Capture();
                else if (command == "stability-check") PerformanceStabilityReview.Start();
                else if (command == "live-route-check") LiveRouteReview.Start();
                else if (command == "gravity-check") Palworks.Performance.Editor.PerformanceGravityReview.ValidatePhysics();
                else if (command == "gravity-key-plan") Palworks.Performance.Editor.PerformanceGravityReview.PlanKeyHeights();
                else if (command == "gravity-routes") Palworks.Performance.Editor.PerformanceGravityReview.ValidateStations();
                else if (command == "screen-polish") ScreenStageReview.Polish();
                else if (command == "gravity-key-apply") ScreenStageReview.ApplyKeyPlan();
                else if (command == "screen-inspect") ScreenStageReview.Inspect();
                else if (command == "screen-preview") ScreenStageReview.Preview();
                else if (command == "screen-upgrade") ScreenStageUpgrade.Apply();
                else if (command == "stage-upgrade") StageGameplayUpgrade.Apply();
                else if (command == "stage-audit") StageGameplayUpgrade.Audit();
                else if (command == "shadow-collision-check") Palworks.Performance.Editor.ShadowCollisionReview.Validate();
                else if (command == "movement-upgrade") MovementUpgrade.Apply();
                else if (command == "actions-check") MovementReview.Validate();
                else if (command == "refresh") AssetDatabase.Refresh();
                else if (command == "inspect") PaperRoomRefinement.Inspect();
                else if (command == "save") { EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); Audit(); Capture(); }
                else if (command == "play") EditorApplication.isPlaying = true;
                else if (command == "stop") EditorApplication.isPlaying = false;
                else throw new ArgumentException("Unknown authoring request: " + command);
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory("output/scene-review");
                File.WriteAllText("output/scene-review/error.txt", ex.ToString());
                Debug.LogException(ex);
            }
        }

        [MenuItem("Palworks/Open Paper Room")]
        public static void Open()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Palworks/Rebuild Paper Room (reset layout)")]
        public static void Build() { BuildRoom(false); PaperRoomRefinement.Apply(); PaperCameraSetup.Apply(); }

        static void BuildRoom(bool environmentOnly)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before authoring.");
            if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save the current scene before rebuilding.");
            Directory.CreateDirectory("Assets/Art/Environment/Materials");
            Directory.CreateDirectory("Assets/Art/Environment/Meshes");
            Directory.CreateDirectory("output/scene-review");
            AssetDatabase.Refresh();
            var factory = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Palworks.Editor.CharacterFactory"))
                .FirstOrDefault(t => t != null);
            if (factory == null && !environmentOnly) throw new InvalidOperationException("CharacterFactory is not ready.");
            if (!File.Exists("Assets/Art/Environment/Textures/TreeCanopy.png")) throw new FileNotFoundException("Environment textures are not ready.");
            ImportTextures();
            cardMesh = MakeCardMesh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var guide = new GameObject("00 · 场景说明 / README").AddComponent<RoomGuide>();
            var cameraRoot = Group("01 · 镜头 / Camera");
            var floor = Group("02 · 地面卡片 / Ground");
            var far = Group("03 · 远景卡片 / Backdrop");
            var trees = Group("04 · 树木卡片 / Trees");
            var props = Group("05 · 道具卡片 / Props");
            var foreground = Group("06 · 前景卡片 / Foreground");
            var lights = Group("07 · 灯光 / Lighting");
            var actors = Group("08 · 人物 / Character");
            var collisions = Group("09 · 碰撞边界 / Colliders");

            var cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.transform.SetParent(cameraRoot);
            cam.tag = "MainCamera";
            cam.orthographic = true;
            cam.orthographicSize = 5.45f;
            cam.nearClipPlane = .1f; cam.farClipPlane = 80;
            cam.backgroundColor = new Color(.105f, .16f, .16f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.transform.rotation = CardRotation;
            cam.transform.position = new Vector3(0, 1.8f, .2f) - cam.transform.forward * 24;
            cam.allowHDR = true;
            cam.AdditionalCameraData().renderPostProcessing = false;
            cam.gameObject.AddComponent<AudioListener>();

            // A planar ground sheet and layered dirt cards: no environment 3D models.
            Card("Ground · 草地平面", floor, "Ground", new Vector3(0, -.015f, -18), 40, 32,
                new Color(.72f, .8f, .75f), Quaternion.Euler(90, 0, 0), new Vector2(12, 10));
            Card("Path · 土路平面", floor, "DirtPatch", new Vector3(0, .003f, -2.4f), 21.8f, 5.2f,
                new Color(.82f, .85f, .82f), Quaternion.Euler(90, 0, 0));
            Card("Path · 前缘碎屑", floor, "Border", new Vector3(0, .012f, -2.55f), 22, .6f,
                new Color(.81f, .85f, .81f), Quaternion.Euler(90, 0, 0));

            var backMat = Material("Backdrop_Solid", null, new Color(.15f, .24f, .24f), false);
            Plane("Forest backdrop · 背景色卡", far, backMat, new Vector3(0, -1, 10), 38, 15, CardRotation);
            for (int i = 0; i < 11; i++)
            {
                float x = -16 + i * 3.2f;
                Tree("Far pine " + (i + 1), far, x, 6.2f + (i % 2) * .6f, 7.6f + (i % 3) * .5f,
                    new Color(.38f, .56f, .55f), i % 2 == 0);
            }
            Tree("Pine · 左侧", trees, -8.8f, 2.8f, 6.5f, Color.white, false);
            Tree("Pine · 右侧", trees, 8.8f, 3.1f, 6.8f, Color.white, true);
            Tree("Pine · 后排左", trees, -4.2f, 4.0f, 5.3f, new Color(.76f, .87f, .84f), true);
            Tree("Pine · 后排右", trees, 4.0f, 4.5f, 6.1f, new Color(.69f, .81f, .79f), false);
            Tree("Foreground pine · 左边框", foreground, -11.2f, -3.8f, 8.5f, new Color(.48f,.65f,.6f), false);
            Tree("Foreground pine · 右边框", foreground, 11.4f, -3.5f, 8.3f, new Color(.43f,.6f,.56f), true);

            Prop("Stump · 后排树桩", props, "Stump", -1.5f, 2.35f, 1.2f);
            Prop("Rock · 左后", props, "RockA", -6.2f, 2.2f, 1.05f);
            Prop("Rock · 右后", props, "RockB", 6.4f, 2.4f, 1.1f);
            Prop("Sapling · 右后", props, "Sapling", 7.25f, 2.8f, 2.0f);
            Prop("Flower cart · 花车", props, "FlowerCart", 4.2f, 2.05f, 1.65f);
            Prop("Mushrooms · 蘑菇", props, "Mushrooms", -7.0f, 2.15f, .7f);
            Prop("Lantern · 左灯", props, "Lamp", -6.4f, 1.85f, 2.65f);
            Prop("Lantern · 右灯", props, "Lamp", 6.45f, 1.9f, 2.65f);
            Prop("Candle · 左烛", props, "Candle", -3.3f, 2.0f, .9f);
            Prop("Candle · 右烛", props, "Candle", 2.5f, 2.05f, .7f);
            for (int i = 0; i < 16; i++)
            {
                var x = -10.1f + i * 1.35f;
                Prop("Grass · 后沿 " + (i + 1), props, i % 3 == 0 ? "GrassSmall" : "Grass", x, 2.6f + (i % 2) * .2f, .35f + (i % 3) * .12f);
            }
            for (int i = 0; i < 10; i++)
                Prop("Grass · 前沿 " + (i + 1), foreground, i % 2 == 0 ? "Grass" : "GrassSmall", -10 + i * 2.2f, -2.85f, .45f + (i % 3) * .13f);
            Prop("Rock · 前景左", foreground, "RockB", -8.4f, -3.0f, .95f);
            Prop("Rock · 前景右", foreground, "RockA", 8.2f, -3.1f, .8f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.63f, .72f, .72f);
            RenderSettings.ambientIntensity = 1;
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            var sun = Light("Key · 暖色主光", lights, LightType.Directional, new Color(1,.89f,.72f), .95f, Vector3.zero, 0);
            sun.transform.rotation = Quaternion.Euler(48, -25, 0);
            sun.shadows = LightShadows.Soft; sun.shadowStrength = .38f; sun.shadowBias = .03f; sun.shadowNormalBias = .12f;
            RenderSettings.sun = sun;
            Light("Lantern fill · 左", lights, LightType.Point, new Color(1,.63f,.26f), 2.5f, new Vector3(-6.4f, 2.0f, .9f), 4.0f);
            Light("Lantern fill · 右", lights, LightType.Point, new Color(1,.63f,.26f), 2.5f, new Vector3(6.45f, 2.0f, .95f), 4.0f);

            Box("Floor collision", collisions, new Vector3(0, -.15f, 0), new Vector3(24,.3f,8));
            Box("Left limit", collisions, new Vector3(-9.8f, 1, 0), new Vector3(.4f,3,4.6f));
            Box("Right limit", collisions, new Vector3(9.8f, 1, 0), new Vector3(.4f,3,4.6f));
            Box("Back limit", collisions, new Vector3(0, 1, 2.05f), new Vector3(20,3,.4f));
            Box("Front limit", collisions, new Vector3(0, 1, -2.05f), new Vector3(20,3,.4f));
            if (factory != null && !environmentOnly)
            {
                var player = (GameObject)factory.GetMethod("CreateCharacter", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
                player.transform.SetParent(actors);
                player.transform.position = new Vector3(-1.2f, .03f, -.15f);
                AddContactShadow(player.transform);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save " + ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }
                .Concat(EditorBuildSettings.scenes.Where(s => s.path != ScenePath)).ToArray();
            AssetDatabase.SaveAssets();
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.LookAt(new Vector3(0,1.8f,.2f), CardRotation, 12, true);
                SceneView.lastActiveSceneView.Repaint();
            }
            Selection.activeGameObject = guide.gameObject;
            Audit();
            Capture();
            if (File.Exists("output/scene-review/error.txt")) File.Delete("output/scene-review/error.txt");
            Debug.Log("PALWORKS: saved editable PaperRoom, ready for Play.");
        }

        static UniversalAdditionalCameraData AdditionalCameraData(this Camera c) => c.GetUniversalAdditionalCameraData();
        static Transform Group(string name) => new GameObject(name).transform;

        // Complete the authored room without rebuilding or changing the user's object positions.
        static void FinishRoom(bool refreshCharacter = false)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before authoring.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) throw new InvalidOperationException("Open PaperRoom first.");
            if (!refreshCharacter)
            {
                ImportTextures();
                foreach (var renderer in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
                {
                    var tex = renderer.sharedMaterial != null ? renderer.sharedMaterial.mainTexture : null;
                    if (tex == null) continue;
                    var folder = AssetDatabase.GetAssetPath(tex);
                    if (!folder.StartsWith("Assets/Art/Environment/Textures/")) continue;
                    var t = renderer.transform;
                    if (t.name.Contains("Ground ·"))
                    {
                        t.position = new Vector3(0, -.015f, -18);
                        t.localScale = new Vector3(40, 32, 1);
                        renderer.sharedMaterial.SetTextureScale("_BaseMap", new Vector2(12, 10));
                    }
                    else if (!t.name.StartsWith("Path ·"))
                        t.localScale = new Vector3(t.localScale.y * tex.width / tex.height, t.localScale.y, t.localScale.z);
                }
            }
            var actors = scene.GetRootGameObjects().First(g => g.name == "Player" || g.name.StartsWith("08 ·")).transform;
            var previous = actors.GetComponentInChildren<CharacterController>();
            if (previous == null || refreshCharacter)
            {
                var factory = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Palworks.Editor.CharacterFactory")).FirstOrDefault(t => t != null);
                if (factory != null)
                {
                    if (previous != null)
                        factory.GetMethod("RefreshCharacter", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { previous.gameObject });
                    else
                    {
                        var player = (GameObject)factory.GetMethod("CreateCharacter", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
                        player.transform.SetParent(actors);
                        player.transform.position = new Vector3(-1.2f, .03f, -.15f);
                        AddContactShadow(player.transform);
                    }
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save PaperRoom.");
            AssetDatabase.SaveAssets();
            Audit(); Capture();
            if (File.Exists("output/scene-review/error.txt")) File.Delete("output/scene-review/error.txt");
        }
        static void ImportTextures()
        {
            foreach (var file in Directory.GetFiles("Assets/Art/Environment/Textures", "*.png"))
            {
                var path = file.Replace('\\','/');
                AssetDatabase.ImportAsset(path);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.textureType = TextureImporterType.Default;
                ti.alphaIsTransparency = true; ti.mipmapEnabled = false;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.maxTextureSize = 2048; ti.filterMode = FilterMode.Bilinear;
                ti.wrapMode = Path.GetFileNameWithoutExtension(path) == "Ground" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
        }
        static Mesh MakeCardMesh()
        {
            var path = "Assets/Art/Environment/Meshes/FootPivotCard.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            mesh = new Mesh { name = "FootPivotCard" };
            mesh.vertices = new[] { new Vector3(-.5f,0,0),new Vector3(.5f,0,0),new Vector3(-.5f,1,0),new Vector3(.5f,1,0) };
            mesh.uv = new[] { new Vector2(0,0),new Vector2(1,0),new Vector2(0,1),new Vector2(1,1) };
            mesh.triangles = new[] { 0,2,1,2,3,1 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        static Material Material(string name, Texture texture, Color tint, bool clipped = true)
        {
            string path = "Assets/Art/Environment/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Simple Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetTexture("_BaseMap", texture); mat.SetColor("_BaseColor", tint);
            mat.SetFloat("_Surface",0); mat.SetFloat("_Cull",0); mat.SetFloat("_ZWrite",1);
            mat.SetFloat("_AlphaClip",clipped ? 1 : 0); mat.SetFloat("_Cutoff",.2f);
            mat.SetFloat("_SrcBlend",1); mat.SetFloat("_DstBlend",0);
            mat.SetColor("_SpecColor",Color.black); mat.SetFloat("_Smoothness",0);
            mat.SetFloat("_SpecularHighlights",0);
            if(clipped) mat.EnableKeyword("_ALPHATEST_ON"); else mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = clipped ? 2450 : 2000;
            mat.SetOverrideTag("RenderType",clipped ? "TransparentCutout" : "Opaque");
            EditorUtility.SetDirty(mat);
            return mat;
        }
        static GameObject Plane(string name, Transform parent, Material mat, Vector3 pos, float w, float h, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent); go.transform.position = pos; go.transform.rotation = rotation;
            go.transform.localScale = new Vector3(w,h,1);
            go.AddComponent<MeshFilter>().sharedMesh = cardMesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
            return go;
        }
        static GameObject Card(string name, Transform parent, string image, Vector3 pos, float w, float h,
            Color tint, Quaternion? rotation = null, Vector2? tiling = null)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Environment/Textures/" + image + ".png");
            if (texture == null) throw new FileNotFoundException("Missing card: " + image);
            string variant = image + "_" + ColorUtility.ToHtmlStringRGB(tint);
            var mat = Material(variant, texture, tint);
            if (tiling.HasValue) mat.SetTextureScale("_BaseMap",tiling.Value);
            return Plane(name, parent, mat, pos, w, h, rotation ?? CardRotation);
        }
        static void Prop(string name, Transform parent, string image, float x, float z, float h)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Environment/Textures/" + image + ".png");
            if(tex == null) throw new FileNotFoundException(image);
            Card(name,parent,image,new Vector3(x,.02f,z),h * tex.width / tex.height,h,Color.white);
        }
        static void Tree(string name, Transform parent, float x, float z, float h, Color tint, bool small)
        {
            var tree = new GameObject(name).transform; tree.SetParent(parent); tree.position = new Vector3(x,0,z);
            var trunk = small ? "TreeTrunkSmall" : "TreeTrunk";
            var canopy = small ? "TreeCanopySmall" : "TreeCanopy";
            var trunkTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Environment/Textures/" + trunk + ".png");
            var canopyTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Environment/Textures/" + canopy + ".png");
            Card("Trunk · 树干",tree,trunk,new Vector3(x,0,z),h * .55f * trunkTex.width / trunkTex.height,h * .55f,tint);
            Card("Canopy · 树冠",tree,canopy,new Vector3(x,h * .28f,z-.035f),h * .78f * canopyTex.width / canopyTex.height,h * .78f,tint);
        }
        static Light Light(string name, Transform parent, LightType type, Color color, float intensity, Vector3 position, float range)
        {
            var go = new GameObject(name); go.transform.SetParent(parent); go.transform.position = position;
            var light = go.AddComponent<Light>(); light.type = type; light.color = color; light.intensity = intensity; light.range = range;
            return light;
        }
        static void Box(string name, Transform parent, Vector3 pos, Vector3 size)
        {
            var go = new GameObject(name); go.transform.SetParent(parent); go.transform.position = pos;
            go.AddComponent<BoxCollider>().size = size;
        }
        static void AddContactShadow(Transform player)
        {
            var meshPath = "Assets/Art/Environment/Meshes/ContactShadow.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = "Flat contact shadow" };
                var vertices = new Vector3[33]; var tris = new int[32 * 3];
                for (int i=0; i<32; i++)
                {
                    float a = i * Mathf.PI * 2 / 32;
                    vertices[i+1] = new Vector3(Mathf.Cos(a)*.43f,0,Mathf.Sin(a)*.2f);
                    tris[i*3]=0; tris[i*3+1]=(i+1)%32+1; tris[i*3+2]=i+1;
                }
                mesh.vertices=vertices; mesh.triangles=tris; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh,meshPath);
            }
            var matPath = "Assets/Art/Environment/Materials/ContactShadow.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                mat.SetColor("_BaseColor",new Color(.10f,.14f,.13f,.22f));
                mat.SetFloat("_Surface",1); mat.SetFloat("_Blend",0); mat.SetFloat("_SrcBlend",5); mat.SetFloat("_DstBlend",10);
                mat.SetFloat("_ZWrite",0); mat.SetFloat("_Cull",0); mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue=3000; mat.SetOverrideTag("RenderType","Transparent");
                AssetDatabase.CreateAsset(mat,matPath);
            }
            var shadow=new GameObject("Contact shadow · 脚底影子卡片");
            shadow.transform.SetParent(player,false); shadow.transform.localPosition=new Vector3(0,.08f,0);
            shadow.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=shadow.AddComponent<MeshRenderer>(); renderer.sharedMaterial=mat;
            renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
        }
        [MenuItem("Palworks/Validate Paper Room")]
        public static void Audit()
        {
            var scene = SceneManager.GetActiveScene();
            var renderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>(true);
            int bad = renderers.Count(r => r.sharedMaterial == null || r.sharedMaterial.shader == null || r.sharedMaterial.shader.name.Contains("InternalError"));
            var cam = Camera.main;
            string report = "scene=" + scene.path + "\nrootGroups=" + scene.rootCount + "\nrenderers=" + renderers.Length +
                "\nmissingMaterials=" + bad + "\northographic=" + (cam != null && cam.orthographic) +
                "\ncameraPitch=" + (cam != null ? cam.transform.eulerAngles.x : 0) +
                "\ncharacterControllers=" + UnityEngine.Object.FindObjectsOfType<CharacterController>().Length +
                "\nplayerPosition=" + (UnityEngine.Object.FindObjectOfType<CharacterController>() != null ? UnityEngine.Object.FindObjectOfType<CharacterController>().transform.position.ToString("F4") : "missing") +
                "\nplayMode=" + EditorApplication.isPlaying + "\n";
            Directory.CreateDirectory("output/scene-review");
            File.WriteAllText("output/scene-review/audit.txt",report);
            if (bad != 0 || cam == null) throw new InvalidOperationException(report);
        }
        [MenuItem("Palworks/Capture Paper Room")]
        public static void Capture()
        {
            Palworks.Projection.PaperProjector.RenderAllPreviews();
            var cam = Camera.main;
            if (cam == null) throw new InvalidOperationException("No Main Camera");
            var rt = RenderTexture.GetTemporary(1600,900,24,RenderTextureFormat.ARGB32);
            var oldTarget = cam.targetTexture; var oldActive = RenderTexture.active;
            try
            {
                RenderPipeline.SubmitRenderRequest(cam, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderTexture.active = rt;
                var image = new Texture2D(1600,900,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,1600,900),0,0); image.Apply();
                Directory.CreateDirectory("output/scene-review");
                File.WriteAllBytes("output/scene-review/" + (EditorApplication.isPlaying ? "paper-room-play.png" : "paper-room-editor.png"), image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
            finally { cam.targetTexture = oldTarget; RenderTexture.active = oldActive; RenderTexture.ReleaseTemporary(rt); }
        }
    }
}




