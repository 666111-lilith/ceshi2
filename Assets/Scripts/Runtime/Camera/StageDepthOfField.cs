using UnityEngine;

namespace Palworks.Rendering
{
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class StageDepthOfField : MonoBehaviour
    {
        [Tooltip("World-space point on the stage focus plane. The sharp band stays here when the camera moves.")]
        public Vector3 stageFocusPoint = Vector3.zero;
        [Min(0)] public float nearSharpRange = 4.7f;
        [Min(0)] public float farSharpRange = 4.2f;
        [Min(.01f)] public float nearTransition = 3.4f;
        [Min(.01f)] public float farTransition = 4.5f;
        [Range(0,8), Tooltip("Foreground blur radius at 1080p. Scales with the Game view resolution.")]
        public float foregroundBlur = 5f;
        [Range(0,8), Tooltip("Background blur radius at 1080p.")]
        public float backgroundBlur = 2.8f;

        public float FocusDistance(Camera camera)
        {
            return Mathf.Max(.1f, Vector3.Dot(stageFocusPoint-camera.transform.position, camera.transform.forward));
        }

        public float BlurRadiusAt(Camera camera, Vector3 worldPoint)
        {
            float depth = Vector3.Dot(worldPoint-stageFocusPoint,camera.transform.forward);
            float near = Mathf.Clamp01((-nearSharpRange-depth)/Mathf.Max(.01f,nearTransition));
            float far = Mathf.Clamp01((depth-farSharpRange)/Mathf.Max(.01f,farTransition));
            return Mathf.Max(near*near*(3-2*near)*foregroundBlur, far*far*(3-2*far)*backgroundBlur);
        }
    }
}
