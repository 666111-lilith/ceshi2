using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Palworks.Performance;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Palworks.Editor
{
    /// <summary>Replaces only the existing lever artwork with original flat source sprites.</summary>
    public static class FlatLeverRepair
    {
        const string Art = "Assets/Art/Stage/Textures/Lever/";

        [MenuItem("Tools/Performance/Repair Flat Levers")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Exit Play before editing the scene.");
            var scene = SceneManager.GetActiveScene();
            var sequence = UnityEngine.Object.FindObjectOfType<PerformanceSequence>();
            if (scene.path != PaperRoomBuilder.ScenePath || sequence == null)
                throw new InvalidOperationException("Open the existing performance scene.");
            if (sequence.stations.Any(s => s == null || s.lever == null))
                throw new InvalidOperationException("A station is missing its existing lever.");

            var pending = sequence.stations.Where(s => s.lever.Find("FlatLever") == null).ToArray();
            if (pending.Length == 0)
            {
                Audit(sequence, null, 0);
                return;
            }

            var shaft = Import("SignPost", new Vector2(.38f, 0));
            var grip = Import("DST_Alchemy_Ball1", new Vector2(.5f, .5f));
            var pedestal = Import("Toolshed_wood_1", new Vector2(.5f, 0));
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Environment/Materials/PaperBlockout.mat");
            if (material == null || material.shader.name != "Palworks/PaperBlockout")
                throw new InvalidOperationException("The existing lit paper material is missing.");

            string backup = "Assets/Scenes/Backups/BeforeFlatLevers" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".unity";
            if (!EditorSceneManager.SaveScene(scene, backup, true))
                throw new InvalidOperationException("Scene backup failed.");
            var before = Snapshot(scene, sequence);

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Replace lever artwork with source cards");
            try
            {
                foreach (var station in pending)
                {
                    Undo.RecordObject(station, "Preserve lever interaction");
                    // These are the previous generated artwork roots. The authored Lever
                    // transform, its collider, and any other user children stay untouched.
                    foreach (var child in station.lever.Cast<Transform>().ToArray())
                        if (child.name == "SourceLever" || child.name == "Visual")
                            Undo.DestroyObjectImmediate(child.gameObject);

                    var cards = Child("FlatLever", station.lever);
                    cards.localRotation = Quaternion.Euler(16, 0, 0);
                    var handle = Child("Handle", cards);
                    handle.localPosition = new Vector3(0, .16f, 0);
                    handle.localRotation = Quaternion.Euler(0, 0, -25);
                    Card("Shaft", handle, shaft, material, .72f, new Vector3(0, 0, 0), .77f);
                    Card("Grip", handle, grip, material, .19f, new Vector3(0, .70f, -.004f), .91f);
                    // The small original log fragment reads as the fixed foot. It covers
                    // the pivot; the handle rotates within the same card plane.
                    Card("Base", cards, pedestal, material, .27f, new Vector3(0, 0, -.008f), .53f);
                    station.leverHandle = handle;
                    station.leverPullAxis = Vector3.forward;
                    station.leverPullAngle = 50;
                    EditorUtility.SetDirty(station);
                }

                var after = Snapshot(scene, sequence);
                var changed = before.Where(pair => !after.TryGetValue(pair.Key, out string value) || value != pair.Value).ToArray();
                if (changed.Length != 0)
                    throw new InvalidOperationException("An authored transform, collider, or renderer changed outside the lever artwork.");
                Audit(sequence, backup, before.Count);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException("Could not save the repaired scene.");
                AssetDatabase.SaveAssets();
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
        }

        static Sprite Import(string name, Vector2 pivot)
        {
            string path = Art + name + ".png";
            if (!File.Exists(path)) throw new FileNotFoundException("Original source sprite missing.", path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Could not import " + path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("Source sprite import failed: " + path);
            return sprite;
        }

        static Transform Child(string name, Transform parent)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create source card");
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static void Card(string name, Transform parent, Sprite sprite, Material material, float height, Vector3 position, float gray)
        {
            var card = Child(name, parent);
            card.localPosition = position;
            card.localScale = Vector3.one * (height / sprite.bounds.size.y);
            var renderer = Undo.AddComponent<SpriteRenderer>(card.gameObject);
            renderer.sprite = sprite;
            renderer.sharedMaterial = material;
            renderer.color = new Color(gray, gray, gray, 1);
            renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
            renderer.receiveShadows = true;
        }

        static Dictionary<int, string> Snapshot(Scene scene, PerformanceSequence sequence)
        {
            var values = new Dictionary<int, string>();
            foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (sequence.stations.Any(s => t != s.lever && t.IsChildOf(s.lever))) continue;
                string value = t.name + "|" + t.localPosition.ToString("R") + "|" + t.localRotation.ToString("R") + "|" + t.localScale.ToString("R") + "|" + t.gameObject.activeSelf;
                foreach (var collider in t.GetComponents<Collider>())
                    value += "|" + EditorJsonUtility.ToJson(collider);
                foreach (var renderer in t.GetComponents<Renderer>())
                    value += "|" + EditorJsonUtility.ToJson(renderer);
                values.Add(t.GetInstanceID(), value);
            }
            return values;
        }

        static void Audit(PerformanceSequence sequence, string backup, int preserved)
        {
            var report = new StringBuilder();
            report.AppendLine("Backup=" + backup);
            report.AppendLine("PreservedRecords=" + preserved);
            report.AppendLine("UnexpectedChanges=0");
            foreach (var station in sequence.stations)
            {
                var cards = station.lever.Find("FlatLever");
                var sprites = cards != null ? cards.GetComponentsInChildren<SpriteRenderer>(true) : new SpriteRenderer[0];
                int visibleMeshes = station.lever.GetComponentsInChildren<MeshRenderer>(true).Count(r => r.enabled && r.gameObject.activeInHierarchy);
                if (cards == null || sprites.Length != 3 || visibleMeshes != 0 || station.leverHandle == null || station.leverHandle.parent != cards)
                    throw new InvalidOperationException("Flat lever verification failed for " + station.name);
                report.AppendLine(station.name + ": Sprites=" + sprites.Length + ", VisibleMeshes=" + visibleMeshes + ", HandleAxis=" + station.leverPullAxis);
                foreach (var sprite in sprites)
                    report.AppendLine("  " + sprite.name + "=" + AssetDatabase.GetAssetPath(sprite.sprite));
            }
            Directory.CreateDirectory("output/scene-review");
            File.WriteAllText("output/scene-review/flat-lever-repair.txt", report.ToString());
        }
    }
}
