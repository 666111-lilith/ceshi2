using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using Palworks.Character;

namespace Palworks.Editor
{
    public static class CharacterFactory
    {
        const string Root = "Assets/Art/Characters/Jenn";
        const string PrefabPath = "Assets/Prefabs/Characters/JennPlayer.prefab";

        static Color PartColor(string part)
        {
            float value = .9f;
            if (part == "Head") value = .52f;
            else if (part.StartsWith("Head_Detail"))
            {
                int detail;
                if (int.TryParse(part.Substring("Head_Detail".Length),out detail))
                {
                    float[] shades = {.52f,.82f,.72f,.52f,.75f,.97f,.97f,.22f,
                                      .22f,.72f,.72f,.30f,.30f,.62f,.58f,.30f};
                    value = detail < shades.Length ? shades[detail] : .9f;
                }
            }
            else if (part.Contains("FullEyes") || part.Contains("Mouth")) value = .30f;
            else if (part.Contains("HeadBack")) value = .82f;
            else if (part.Contains("Hair")) value = .52f;
            else if (part.Contains("PantsTibia")) value = .48f;
            else if (part.Contains("Pants")) value = .58f;
            else if (part.Contains("ShirtChest") || part.Contains("ShirtBelly")) value = .68f;
            else if (part.Contains("Shirt")) value = .72f;
            else if (part.Contains("Foot") || part.Contains("Boot")) value = .80f;
            else if (part.Contains("Glove")) value = .82f;
            return new Color(value,value,value,1);
        }

        public static void ApplyBlockout(GameObject existing)
        {
            if (existing == null) return;
            var animator = existing.GetComponentInChildren<PuppetAnimator>(true);
            var controller = existing.GetComponent<PuppetPlayerController>();
            var shader = Shader.Find("Palworks/PuppetBlockout");
            if (animator == null || shader == null)
                throw new System.InvalidOperationException("Character or PuppetBlockout shader is missing.");
            existing.name = "Jenn";
            animator.gameObject.name = "Visuals";
            foreach (Transform child in existing.transform)
                if (child.name.IndexOf("shadow",System.StringComparison.OrdinalIgnoreCase) >= 0)
                    child.name = "ContactShadow";
            var colors = new Color[animator.bitmapMaterials.Length];
            for (int i=0;i<colors.Length;i++) colors[i] = new Color(.9f,.9f,.9f,1);
            // The nested runtime cache can be altered by Unity's hot-reload serialization.
            // Palette authoring always reads the authoritative TextAsset afresh.
            var data = JsonUtility.FromJson<PuppetAnimationData>(animator.animationJson.text);
            if (data == null || data.clips == null || data.clips.Length == 0 ||
                data.parts == null || data.parts.Length == 0)
                throw new System.InvalidOperationException("Character palette source has no clips or named parts.");
            var mapped = new bool[colors.Length];
            int drawCount = 0;
            foreach (var clip in data.clips)
                foreach (var frame in clip.frames)
                    foreach (var draw in frame.parts)
                    {
                        colors[draw.bitmap] = PartColor(data.parts[draw.part].name);
                        mapped[draw.bitmap] = true;
                        drawCount++;
                    }
            int mappedCount = 0;
            for (int i=0;i<mapped.Length;i++) if (mapped[i]) mappedCount++;
            if (mappedCount != colors.Length)
                throw new System.InvalidOperationException("Character palette mapped "+mappedCount+
                    " of "+colors.Length+" bitmaps; refusing to apply an incomplete palette.");
            Debug.Log("CharacterPalette source="+AssetDatabase.GetAssetPath(animator.animationJson)+
                      " clips="+data.clips.Length+" draws="+drawCount+" bitmaps="+mappedCount+
                      " firstPart="+data.parts[0].name+" firstGray="+colors[0].r.ToString("F2"));
            for (int i=0;i<animator.bitmapMaterials.Length;i++)
            {
                var material = animator.bitmapMaterials[i];
                if (material == null) continue;
                var texture = material.GetTexture("_BaseMap");
                material.shader = shader;
                material.shaderKeywords = new string[0];
                material.SetTexture("_BaseMap",texture);
                string regionPath = "Assets/Art/Characters/Jenn/Textures/Blockout/"+
                    Path.GetFileNameWithoutExtension(data.bitmaps[i].file)+"_regions.png";
                var regionImporter = AssetImporter.GetAtPath(regionPath) as TextureImporter;
                Texture2D regions = null;
                if (regionImporter != null)
                {
                    regionImporter.textureType = TextureImporterType.Default;
                    regionImporter.sRGBTexture = false;
                    regionImporter.alphaSource = TextureImporterAlphaSource.None;
                    regionImporter.mipmapEnabled = false;
                    regionImporter.npotScale = TextureImporterNPOTScale.None;
                    regionImporter.wrapMode = TextureWrapMode.Clamp;
                    regionImporter.filterMode = FilterMode.Bilinear;
                    regionImporter.textureCompression = TextureImporterCompression.Uncompressed;
                    regionImporter.SaveAndReimport();
                    regions = AssetDatabase.LoadAssetAtPath<Texture2D>(regionPath);
                }
                material.SetTexture("_BlockoutShadeMap",regions);
                // A composite face contains several authored regions. Its map is
                // absolute neutral gray; applying a second per-part tint would
                // make eye whites dark and give Idle/Walk different hair colors.
                material.SetColor("_BaseColor",regions != null ? Color.white : colors[i]);
                material.SetFloat("_Opacity",1);
                material.renderQueue = (int)RenderQueue.Transparent;
                EditorUtility.SetDirty(material);
            }
            animator.ClearColorOverrides();
            if (controller != null)
            {
                controller.moveSpeed = 4.8f;
                EditorUtility.SetDirty(controller);
                if (PrefabUtility.IsPartOfPrefabInstance(controller))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
            }
            foreach (var filter in animator.partMeshes)
                if (filter != null && filter.sharedMesh != null)
                {
                    filter.sharedMesh.name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(filter.sharedMesh));
                    EditorUtility.SetDirty(filter.sharedMesh);
                }
            EditorUtility.SetDirty(existing);
            EditorUtility.SetDirty(animator.gameObject);
            if (PrefabUtility.IsPartOfPrefabInstance(existing))
                PrefabUtility.RecordPrefabInstancePropertyModifications(existing);
            if (PrefabUtility.IsPartOfPrefabInstance(animator.gameObject))
                PrefabUtility.RecordPrefabInstancePropertyModifications(animator.gameObject);
            AssetDatabase.SaveAssets();
        }

        public static GameObject RefreshCharacter(GameObject existing)
        {
            if (existing == null) return CreateCharacter();
            var parent = existing.transform.parent;
            var position = existing.transform.localPosition;
            var rotation = existing.transform.localRotation;
            var scale = existing.transform.localScale;
            var characterName = existing.name;
            var replacement = CreateCharacter();
            replacement.transform.SetParent(parent,false);
            replacement.transform.localPosition = position;
            replacement.transform.localRotation = rotation;
            replacement.transform.localScale = scale;
            replacement.name = characterName;
            for (int i=existing.transform.childCount-1;i>=0;i--)
            {
                var child = existing.transform.GetChild(i);
                if (child.GetComponent<PuppetAnimator>() == null)
                    child.SetParent(replacement.transform,false);
            }
            Object.DestroyImmediate(existing);
            return replacement;
        }

        [MenuItem("Palworks/Character/Create Jenn Player")]
        public static GameObject CreateCharacter()
        {
            Directory.CreateDirectory("Assets/Art/Characters/Jenn/Materials");
            Directory.CreateDirectory("Assets/Art/Characters/Jenn/Meshes");
            Directory.CreateDirectory("Assets/Prefabs/Characters");
            AssetDatabase.Refresh();
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Animations/Jenn/Data/JennAnimations.json");
            if (json == null) throw new FileNotFoundException("Run Tools/Character/export_character.py first.");
            var data = JsonUtility.FromJson<PuppetAnimationData>(json.text);
            var materials = new Material[data.bitmaps.Length];
            for (int i=0;i<materials.Length;i++)
            {
                string texturePath = "Assets/Art/Characters/Jenn/Textures/"+data.bitmaps[i].file;
                var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                importer.textureType = TextureImporterType.Default;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                string path = "Assets/Art/Characters/Jenn/Materials/Part_"+i.ToString("D3")+".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(Shader.Find("Palworks/PuppetBlockout"));
                    AssetDatabase.CreateAsset(material,path);
                }
                material.SetTexture("_BaseMap",texture);
                material.SetColor("_BaseColor",Color.white);
                material.renderQueue = (int)RenderQueue.Transparent;
                EditorUtility.SetDirty(material);
                materials[i] = material;
            }

            var player = new GameObject("Jenn");
            var visualObject = new GameObject("Visuals");
            visualObject.transform.SetParent(player.transform,false);
            visualObject.transform.localRotation = Quaternion.Euler(16,0,0);
            var animator = visualObject.AddComponent<PuppetAnimator>();
            animator.animationJson = json;
            animator.bitmapMaterials = materials;
            animator.partMeshes = new MeshFilter[data.parts.Length];
            animator.partRenderers = new MeshRenderer[data.parts.Length];
            for (int i=0;i<data.parts.Length;i++)
            {
                var part = new GameObject(data.parts[i].name);
                part.transform.SetParent(visualObject.transform,false);
                var filter = part.AddComponent<MeshFilter>();
                var renderer = part.AddComponent<MeshRenderer>();
                string meshPath = "Assets/Art/Characters/Jenn/Meshes/Part_"+i.ToString("D3")+".asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (mesh == null)
                {
                    mesh = new Mesh {name=data.parts[i].name+"Mesh"};
                    mesh.vertices = new Vector3[4];
                    mesh.triangles = new[]{0,1,2,0,2,3};
                    AssetDatabase.CreateAsset(mesh,meshPath);
                }
                filter.sharedMesh = mesh;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sharedMaterial = materials[0];
                animator.partMeshes[i] = filter;
                animator.partRenderers[i] = renderer;
            }
            animator.ApplyFrame("Idle",0);
            foreach(var filter in animator.partMeshes) EditorUtility.SetDirty(filter.sharedMesh);

            var capsule = player.AddComponent<CharacterController>();
            capsule.height = 1.7f;
            capsule.radius = .24f;
            capsule.center = new Vector3(0,.88f,0);
            capsule.stepOffset = .15f;
            capsule.skinWidth = .025f;
            capsule.minMoveDistance = 0;
            var controller = player.AddComponent<PuppetPlayerController>();
            controller.visuals = visualObject.transform;
            controller.puppet = animator;
            controller.moveSpeed = 4.8f;
            ApplyBlockout(player);

            PrefabUtility.SaveAsPrefabAsset(player,PrefabPath);
            Object.DestroyImmediate(player);
            AssetDatabase.SaveAssets();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Selection.activeGameObject = instance;
            Debug.Log("Created Jenn with "+data.parts.Length+" editable original animation parts, "+
                      data.clips[0].frames.Length+" idle and "+data.clips[1].frames.Length+" walk frames.");
            return instance;
        }
    }
}
