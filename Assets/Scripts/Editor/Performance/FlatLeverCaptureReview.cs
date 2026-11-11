using System;
using System.IO;
using System.Linq;
using System.Text;
using Palworks.Performance;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Palworks.Editor
{
    // Review-only captures in Unity's isolated preview scene. No authored camera,
    // transform, material, light, or active state is changed, and nothing is saved.
    public static class FlatLeverCaptureReview
    {
        const string Output = "output/scene-review/flat-lever";

        [MenuItem("Tools/Performance/Capture Flat Lever")]
        public static void Capture()
        {
            var sequence = UnityEngine.Object.FindObjectOfType<PerformanceSequence>();
            if (sequence == null || sequence.stations.Length == 0 || sequence.stations[0].lever == null)
                throw new InvalidOperationException("The existing performance lever is missing.");
            var source = sequence.stations[0].lever.Find("FlatLever");
            if (source == null) throw new InvalidOperationException("Apply the flat lever repair first.");
            var scene = SceneManager.GetActiveScene();
            bool dirtyBefore = scene.isDirty;
            string cameraBefore = CameraState(sequence.view);
            string sourceBefore = SourceState(source);
            var activeBefore = RenderTexture.active;
            var utility = new PreviewRenderUtility();
            GameObject copy = null;
            try
            {
                copy = UnityEngine.Object.Instantiate(source.gameObject);
                copy.name = "LeverPreview";
                copy.hideFlags = HideFlags.HideAndDontSave;
                copy.transform.SetPositionAndRotation(Vector3.zero, source.rotation);
                copy.transform.localScale = source.lossyScale;
                copy.SetActive(true);
                utility.AddSingleGO(copy);
                var renderers = copy.GetComponentsInChildren<SpriteRenderer>(true);
                if (renderers.Length != 3 || copy.GetComponentsInChildren<MeshRenderer>(true).Length != 0)
                    throw new InvalidOperationException("Expected exactly three source sprite cards and no model renderer.");
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);

                utility.ambientColor = new Color(.48f, .48f, .48f);
                utility.lights[0].color = Color.white;
                utility.lights[0].intensity = 1.0f;
                utility.lights[0].transform.rotation = Quaternion.LookRotation(source.forward, source.up);
                utility.lights[1].color = Color.white;
                utility.lights[1].intensity = .35f;
                utility.lights[1].transform.rotation = Quaternion.LookRotation(-source.forward, source.up);
                utility.camera.orthographic = true;
                utility.camera.orthographicSize = Mathf.Max(.65f, bounds.size.magnitude * .62f);
                utility.camera.nearClipPlane = .01f;
                utility.camera.farClipPlane = 30;
                utility.camera.clearFlags = CameraClearFlags.SolidColor;
                utility.camera.backgroundColor = new Color(.12f, .14f, .17f);
                Directory.CreateDirectory(Output);
                CaptureView(utility, source, bounds, 0, "front");
                CaptureView(utility, source, bounds, 75, "oblique");
                CaptureView(utility, source, bounds, 90, "side");
                var report = new StringBuilder();
                report.AppendLine("Render=Isolated Unity Preview Scene");
                report.AppendLine("Source=Station1/Lever/FlatLever");
                report.AppendLine("Lighting=Neutral review lights; original paper material and sprite colors");
                report.AppendLine("SpriteRenderers=" + renderers.Length);
                report.AppendLine("MeshRenderers=0");
                report.AppendLine("Views=front,oblique75,side90");
                foreach (var renderer in source.GetComponentsInChildren<SpriteRenderer>(true))
                    report.AppendLine(renderer.name + "=" + AssetDatabase.GetAssetPath(renderer.sprite));
                File.WriteAllText(Path.Combine(Output, "capture.txt"), report.ToString());
            }
            finally
            {
                RenderTexture.active = activeBefore;
                utility.Cleanup();
                if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            }
            if (cameraBefore != CameraState(sequence.view) || sourceBefore != SourceState(source) || scene.isDirty != dirtyBefore)
                throw new InvalidOperationException("Capture unexpectedly changed an authored camera, lever, or scene dirty state.");
            File.AppendAllText(Path.Combine(Output, "capture.txt"), "CameraUnchanged=True\nLeverUnchanged=True\nSceneDirtyUnchanged=True\n");
        }

        static void CaptureView(PreviewRenderUtility utility, Transform source, Bounds bounds, float angle, string name)
        {
            var direction = Quaternion.AngleAxis(angle, source.up) * source.forward;
            utility.camera.transform.SetPositionAndRotation(bounds.center - direction * 5, Quaternion.LookRotation(direction, source.up));
            utility.BeginStaticPreview(new Rect(0, 0, 960, 960));
            Texture2D image = null;
            try
            {
                utility.Render(true);
                image = utility.EndStaticPreview();
                if (image == null) throw new InvalidOperationException("Preview capture returned no image.");
                File.WriteAllBytes(Path.Combine(Output, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
            }
        }

        static string CameraState(Camera camera)
        {
            if (camera == null) return "None";
            return EditorJsonUtility.ToJson(camera) + "|" + camera.transform.position.ToString("R") + "|" + camera.transform.rotation.ToString("R") + "|" + camera.transform.localScale.ToString("R");
        }

        static string SourceState(Transform source)
        {
            return string.Join("\n", source.GetComponentsInChildren<Transform>(true).Select(t => t.name + "|" + t.localPosition.ToString("R") + "|" + t.localRotation.ToString("R") + "|" + t.localScale.ToString("R") + "|" + t.gameObject.activeSelf + "|" + string.Join(";", t.GetComponents<SpriteRenderer>().Select(r => EditorJsonUtility.ToJson(r)))));
        }
    }
}
