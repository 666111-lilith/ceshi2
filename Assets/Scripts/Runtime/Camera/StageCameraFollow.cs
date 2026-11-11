using UnityEngine;

namespace Palworks.CameraSystem
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class StageCameraFollow : MonoBehaviour
    {
        public Transform target;

        [Header("Horizontal Framing")]
        [Tooltip("Initial follow fraction. Once the composition offset reaches its limit, long travel follows one-to-one.")]
        [Range(0,1)] public float horizontalInfluence = 0.72f;
        [Tooltip("Maximum horizontal displacement retained in the composition. Prevents partial follow from losing the actor on long stages.")]
        [Min(0)] public float maxCompositionOffset = 2.75f;
        [Tooltip("Full width of the horizontal buffer as a fraction of the visible view width.")]
        [Range(0.01f, 0.5f)] public float deadZoneWidth = 0.04f;
        [Min(0)] public float lookAheadDistance = 0f;
        [Min(0.01f)] public float lookAheadSmoothTime = 0.25f;
        [Min(0.01f)] public float smoothTime = 0.42f;
        [Tooltip("World-space camera-center bounds, independent of player movement limits.")]
        public float minCameraX = -5.5f;
        public float maxCameraX = 53.5f;

        [Header("Shallow Depth")]
        [Range(0,1)] public float depthInfluence = 0.25f;
        [Min(0.01f)] public float depthSmoothTime = 0.35f;

        [Tooltip("Ignore horizontal motion slower than this many world units per second when choosing look-ahead direction.")]
        [Min(0)] public float directionSpeedThreshold = 0.05f;

        [System.NonSerialized] bool initialized;
        Camera viewCamera;
        Transform initializedTarget;
        Vector3 anchorCameraPosition;
        Vector3 anchorTargetPosition;
        Vector3 previousTargetPosition;
        Vector3 evaluatedPosition;
        float desiredCenterX;
        float velocityX;
        float velocityZ;
        float desiredLookAhead;
        float currentLookAhead;
        float lookAheadVelocity;

        public float RuntimeLookAhead => currentLookAhead;
        public float RuntimeDesiredCenterX => desiredCenterX;
        public bool HasRuntimeState => initialized;

        void OnEnable()
        {
            // No ExecuteAlways: authoring the room never moves the editor camera.
            if (Application.isPlaying) ResetRuntimeState();
        }

        void LateUpdate()
        {
            if (Application.isPlaying) Tick(Time.deltaTime);
        }

        /// <summary>Capture the existing composition without changing the transform.</summary>
        public void ResetRuntimeState()
        {
            initialized = false;
            if (target != null) Initialize(target.position);
        }

        void Initialize(Vector3 targetPosition)
        {
            viewCamera = GetComponent<Camera>();
            initializedTarget = target;
            anchorCameraPosition = transform.position;
            anchorTargetPosition = targetPosition;
            previousTargetPosition = targetPosition;
            evaluatedPosition = anchorCameraPosition;
            desiredCenterX = anchorCameraPosition.x;
            velocityX = velocityZ = 0;
            desiredLookAhead = currentLookAhead = lookAheadVelocity = 0;
            initialized = true;
        }

        /// <summary>Advance and apply one follow step. Can be called explicitly by editor validation.</summary>
        public void Tick(float deltaTime)
        {
            if (target == null) return;
            if (!initialized || initializedTarget != target) Initialize(target.position);
            transform.position = Evaluate(target.position,deltaTime);
        }

        /// <summary>
        /// Stateful evaluation without changing any Transform. Tests can supply target samples
        /// directly; production uses Tick. Height and rotation remain authored in the scene.
        /// </summary>
        public Vector3 Evaluate(Vector3 targetPosition,float deltaTime)
        {
            if (!initialized) Initialize(targetPosition);
            if (deltaTime <= 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                return evaluatedPosition;

            float targetSpeedX = (targetPosition.x-previousTargetPosition.x)/deltaTime;
            previousTargetPosition = targetPosition;
            if (Mathf.Abs(targetSpeedX) > directionSpeedThreshold)
                desiredLookAhead = Mathf.Sign(targetSpeedX)*lookAheadDistance;
            // On stopping, keep the last look-ahead and buffer placement. There is no
            // automatic return to the actor's center; only the remaining damping settles.
            currentLookAhead = Mathf.SmoothDamp(currentLookAhead,desiredLookAhead,
                ref lookAheadVelocity,Mathf.Max(.01f,lookAheadSmoothTime),Mathf.Infinity,deltaTime);

            float projectionDepth = Mathf.Max(.01f,Vector3.Dot(
                targetPosition-evaluatedPosition,viewCamera.transform.forward));
            float halfViewWidth = viewCamera.orthographic
                ? viewCamera.orthographicSize*viewCamera.aspect
                : projectionDepth*
                  Mathf.Tan(viewCamera.fieldOfView*Mathf.Deg2Rad*.5f)*viewCamera.aspect;
            float halfBuffer = Mathf.Max(.01f,halfViewWidth*Mathf.Clamp(deadZoneWidth,.01f,.5f));
            float displacementX = targetPosition.x-anchorTargetPosition.x;
            // Retain the authored room composition over short moves, but bound the part
            // of travel that the camera does not follow. Beyond this limit each metre of
            // actor travel advances the framing by one metre, in either direction.
            float initialOffset = anchorCameraPosition.x-anchorTargetPosition.x;
            float offsetLimit = Mathf.Max(Mathf.Abs(initialOffset),Mathf.Max(0,maxCompositionOffset));
            float compositionOffset = Mathf.Clamp(initialOffset-displacementX*(1-Mathf.Clamp01(horizontalInfluence)),
                -offsetLimit,offsetLimit);
            float framedTargetX = targetPosition.x+compositionOffset+currentLookAhead;
            float error = framedTargetX-evaluatedPosition.x;
            if (error > halfBuffer) desiredCenterX = framedTargetX-halfBuffer;
            else if (error < -halfBuffer) desiredCenterX = framedTargetX+halfBuffer;

            float lower = Mathf.Min(minCameraX,maxCameraX);
            float upper = Mathf.Max(minCameraX,maxCameraX);
            desiredCenterX = Mathf.Clamp(desiredCenterX,lower,upper);
            float nextX = Mathf.SmoothDamp(evaluatedPosition.x,desiredCenterX,ref velocityX,
                Mathf.Max(.01f,smoothTime),Mathf.Infinity,deltaTime);
            evaluatedPosition.x = Mathf.Clamp(nextX,lower,upper);
            if ((evaluatedPosition.x <= lower && velocityX < 0) ||
                (evaluatedPosition.x >= upper && velocityX > 0)) velocityX = 0;

            float desiredZ = anchorCameraPosition.z+
                (targetPosition.z-anchorTargetPosition.z)*Mathf.Clamp01(depthInfluence);
            evaluatedPosition.z = Mathf.SmoothDamp(evaluatedPosition.z,desiredZ,ref velocityZ,
                Mathf.Max(.01f,depthSmoothTime),Mathf.Infinity,deltaTime);
            evaluatedPosition.y = anchorCameraPosition.y;
            return evaluatedPosition;
        }
    }
}
