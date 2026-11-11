using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Palworks.Projection
{
    // A normal perspective camera at the lamp captures only silhouette casters.
    // Everything is authored in the scene; this component never builds objects.
    [ExecuteAlways, DefaultExecutionOrder(1000)]
    public sealed class PaperProjector : MonoBehaviour
    {
        public Transform lens;
        public Transform screen;
        public Renderer screenRenderer;
        public Camera projectionCamera;
        public RenderTexture shadowTexture;
        public Vector2 screenSize = new Vector2(12,5.6f);
        public LayerMask casterLayers = 1 << 8;
        [Range(0,1)] public float shadowOpacity = .84f;
        [Range(0,3)] public float edgeSoftness = .65f;
        [Header("Physical Penumbra")]
        [Tooltip("World-space radius of the emitting lamp surface. Zero is a sharp point source.")]
        [Range(0,.3f)] public float lightRadius = .065f;
        [Range(16,96)] public int lightSamples = 48;
        [Range(1,3)] public int silhouetteSupersampling = 2;
        [HideInInspector] public int maximumBlurPixels = 18;
        public bool physicalSoftness = true;
        public Light illuminationLight;
        [Min(0)] public float screenIlluminance = 75;
        public Color lampColor = new Color(1,.91f,.76f,1);
        [System.NonSerialized] public int renderRevision;
        MaterialPropertyBlock properties;
        RenderTexture captureTexture;

        void OnEnable() { renderRevision++; RefreshProjection(); }
        void LateUpdate() { RefreshProjection(); }
        void OnValidate() { if(isActiveAndEnabled) RefreshProjection(); }
        void OnDisable()
        {
            if(projectionCamera!=null) {projectionCamera.enabled=false;projectionCamera.targetTexture=null;}
            ReleaseCapture();
        }
        void OnDestroy() { ReleaseCapture(); }
        void ReleaseCapture()
        {
            if(captureTexture==null) return;
            captureTexture.Release();
            if(Application.isPlaying) Destroy(captureTexture); else DestroyImmediate(captureTexture);
            captureTexture=null;
        }
        RenderTexture CaptureTarget()
        {
            if(!physicalSoftness) return shadowTexture;
            if(captureTexture==null || captureTexture.width!=shadowTexture.width || captureTexture.height!=shadowTexture.height)
            {
                ReleaseCapture();
                captureTexture=new RenderTexture(shadowTexture.width,shadowTexture.height,24,RenderTextureFormat.ARGB32)
                {name="ProjectionCapture",hideFlags=HideFlags.HideAndDontSave,antiAliasing=1};
                captureTexture.Create();
            }
            return captureTexture;
        }

        public void RefreshProjection()
        {
            if(lens==null || screen==null || projectionCamera==null || shadowTexture==null) return;
            var view=projectionCamera;
            view.transform.SetPositionAndRotation(lens.position,screen.rotation);
            Vector3 center=view.transform.InverseTransformPoint(screen.position);
            float distance=center.z;
            if(distance<=.15f) { view.enabled=false; return; }
            float width=screenSize.x*Mathf.Abs(screen.lossyScale.x);
            float height=screenSize.y*Mathf.Abs(screen.lossyScale.y);
            const float near=.08f;
            view.orthographic=false;
            view.fieldOfView=2*Mathf.Atan(height*.5f/distance)*Mathf.Rad2Deg;
            view.nearClipPlane=near;
            view.farClipPlane=distance-.015f;
            view.aspect=width/height;
            view.projectionMatrix=Matrix4x4.Frustum(
                (center.x-width*.5f)*near/distance,(center.x+width*.5f)*near/distance,
                (center.y-height*.5f)*near/distance,(center.y+height*.5f)*near/distance,
                near,view.farClipPlane);
            view.cullingMask=physicalSoftness?0:casterLayers.value;
            view.clearFlags=CameraClearFlags.SolidColor;
            view.backgroundColor=Color.clear;
            view.allowHDR=false; view.allowMSAA=false;
            view.targetTexture=CaptureTarget();
            view.depth=-50;
            view.enabled=true;
            if(!shadowTexture.IsCreated()) shadowTexture.Create();
            if(screenRenderer!=null)
            {
                if(properties==null) properties=new MaterialPropertyBlock();
                screenRenderer.GetPropertyBlock(properties);
                properties.SetTexture("_ShadowMap",shadowTexture);
                properties.SetVector("_ClothSize",new Vector4(screenSize.x,screenSize.y,0,0));
                properties.SetFloat("_ShadowOpacity",shadowOpacity);
                properties.SetFloat("_EdgeSoftness",edgeSoftness);
                properties.SetFloat("_PhysicalSoftness",physicalSoftness?1:0);
                properties.SetVector("_ProjectorPosition",lens.position);
                properties.SetVector("_ProjectorDirection",(screen.position-lens.position).normalized);
                properties.SetColor("_ProjectorLightColor",illuminationLight!=null?illuminationLight.color:lampColor);
                properties.SetFloat("_ProjectorIlluminance",illuminationLight!=null?illuminationLight.intensity*15:screenIlluminance);
                screenRenderer.SetPropertyBlock(properties);
            }
        }

        // Call before an isolated MainCamera screenshot so all three feeds are current.
        public void RenderPreview()
        {
            renderRevision++;
            RefreshProjection();
            if(projectionCamera==null || !projectionCamera.enabled || shadowTexture==null) return;
            GetComponent<ClothKeyProjection>()?.Refresh();
            var request=new UniversalRenderPipeline.SingleCameraRequest { destination=projectionCamera.targetTexture };
            if(RenderPipeline.SupportsRenderRequest(projectionCamera,request))
                RenderPipeline.SubmitRenderRequest(projectionCamera,request);
        }

        public static void RenderAllPreviews()
        {
            foreach(var projector in FindObjectsOfType<PaperProjector>())
                if(projector.isActiveAndEnabled) projector.RenderPreview();
        }

        public float PenumbraPixels(float casterDistance)
        {
            float distance=Vector3.Dot(screen.position-lens.position,screen.forward);
            float pixels=shadowTexture.width/Mathf.Max(.1f,screenSize.x*Mathf.Abs(screen.lossyScale.x));
            return lightRadius*Mathf.Max(0,distance/Mathf.Max(.08f,casterDistance)-1)*pixels;
        }

        void OnDrawGizmosSelected()
        {
            if(lens==null || screen==null) return;
            Gizmos.color=new Color(.85f,.8f,.5f,.6f);
            foreach(float x in new[]{-.5f,.5f}) foreach(float y in new[]{-.5f,.5f})
                Gizmos.DrawLine(lens.position,screen.TransformPoint(new Vector3(x*screenSize.x,y*screenSize.y,0)));
        }
    }
}
