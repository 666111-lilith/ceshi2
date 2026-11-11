using UnityEngine;
using UnityEngine.Rendering;

namespace Palworks.Character
{
    [DisallowMultipleComponent]
    public sealed class PuppetAnimator : MonoBehaviour
    {
        public TextAsset animationJson;
        public MeshFilter[] partMeshes;
        public MeshRenderer[] partRenderers;
        public Material[] bitmapMaterials;
        [Tooltip("Playback rate is adjustable; the source runtime's FPS was not exported.")]
        public float framesPerSecond = 24;
        public int baseSortingOrder = 100;
        [System.NonSerialized] PuppetAnimationData data;
        string currentClip = "Idle";
        float time;
        string motionClip = "Idle";
        float motionRate = 1;
        float actionDuration, actionTime;
        bool sampledAction;
        int lastFrame = -1;
        Mesh[] ownedMeshes;
        readonly Vector3[] corners = new Vector3[4];
        static readonly Vector2[] UV = {new Vector2(0,1),new Vector2(1,1),new Vector2(1,0),new Vector2(0,0)};
        static readonly int[] Triangles = {0,1,2,0,2,3};
        MaterialPropertyBlock properties;
        static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public PuppetAnimationData Data
        {
            get
            {
                if (data == null && animationJson != null)
                    data = JsonUtility.FromJson<PuppetAnimationData>(animationJson.text);
                return data;
            }
        }

        void Awake()
        {
            // Clone the edit-time meshes: play mode never mutates prefab mesh assets.
            ownedMeshes = new Mesh[partMeshes.Length];
            for (int i=0;i<partMeshes.Length;i++)
            {
                var mesh = Instantiate(partMeshes[i].sharedMesh);
                mesh.name = partMeshes[i].name+"Mesh";
                mesh.MarkDynamic();
                partMeshes[i].sharedMesh = mesh;
                ownedMeshes[i] = mesh;
            }
            ApplyFrame("Idle", 0);
        }

        public void SetMoving(bool moving)
        {
            SetMotion(moving,false);
        }

        public string CurrentClip => currentClip;
        public bool HasClip(string clipName) => FindClip(clipName) != null;
        public void ReloadData() { data=null; }

        public void SetMotion(bool moving,bool sprinting)
        {
            motionClip = moving ? (sprinting && HasClip("Run") ? "Run" : "Walk") : "Idle";
            motionRate = moving && sprinting ? 1.25f : 1f;
            sampledAction = false;
            if(actionDuration>0 && actionTime<actionDuration) return;
            SwitchClip(motionClip);
        }

        void SwitchClip(string next)
        {
            if (next == currentClip) return;
            currentClip = next; time = 0; lastFrame = -1;
            ApplyFrame(currentClip,0);
        }

        public void PlayAction(string clipName,float duration)
        {
            if(!HasClip(clipName)) return;
            sampledAction=false;
            actionDuration=Mathf.Max(.01f,duration); actionTime=0;
            SwitchClip(clipName); time=0; lastFrame=-1;
            ApplyFrame(clipName,0);
        }

        public void SampleAction(string clipName,float normalized)
        {
            var clip=FindClip(clipName);
            if(clip==null || clip.frames.Length==0) return;
            if(currentClip!=clipName) lastFrame=-1;
            sampledAction=true; actionDuration=0;
            currentClip=clipName;
            int frame=Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(normalized)*(clip.frames.Length-1)),0,clip.frames.Length-1);
            if(frame!=lastFrame) { ApplyFrame(clipName,frame); lastFrame=frame; }
        }

        public void CancelAction()
        {
            sampledAction=false; actionDuration=0; actionTime=0;
            SwitchClip(motionClip);
        }

        void Update()
        {
            if(sampledAction) return;
            if(actionDuration>0)
            {
                actionTime+=Time.deltaTime;
                if(actionTime<actionDuration)
                {
                    var action=FindClip(currentClip);
                    if(action!=null && action.frames.Length>0)
                    {
                        int index=Mathf.Min(action.frames.Length-1,Mathf.FloorToInt(actionTime/actionDuration*action.frames.Length));
                        if(index!=lastFrame) { ApplyFrame(currentClip,index); lastFrame=index; }
                    }
                    return;
                }
                actionDuration=0;
                SwitchClip(motionClip);
            }
            time += Time.deltaTime;
            var clip = FindClip(currentClip);
            if (clip == null || clip.frames.Length == 0) return;
            int frame = (int)(time * framesPerSecond * motionRate) % clip.frames.Length;
            if (frame != lastFrame) { ApplyFrame(currentClip,frame); lastFrame = frame; }
        }

        PuppetClip FindClip(string clipName)
        {
            if (Data == null || Data.clips == null) return null;
            foreach (var clip in Data.clips) if (clip.name == clipName) return clip;
            return null;
        }

        public void ApplyFrame(string clipName, int frameIndex)
        {
            var clip = FindClip(clipName);
            if (clip == null || partMeshes == null) return;
            foreach (var renderer in partRenderers) if (renderer != null) renderer.enabled = false;
            var frame = clip.frames[Mathf.Clamp(frameIndex,0,clip.frames.Length-1)];
            if (properties == null) properties = new MaterialPropertyBlock();
            for (int drawIndex=0;drawIndex<frame.parts.Length;drawIndex++)
            {
                var draw = frame.parts[drawIndex];
                var mesh = partMeshes[draw.part].sharedMesh;
                float z = -drawIndex*0.0005f;
                for (int v=0;v<4;v++) corners[v] = new Vector3(
                    draw.corners[v*2]*Data.unitsPerPixel,
                    (Data.sourceGroundY-draw.corners[v*2+1])*Data.unitsPerPixel,z);
                mesh.vertices = corners;
                mesh.uv = UV; mesh.triangles = Triangles;
                mesh.RecalculateBounds();
                var renderer = partRenderers[draw.part];
                renderer.sharedMaterial = bitmapMaterials[draw.bitmap];
                renderer.sortingOrder = baseSortingOrder+drawIndex;
                properties.Clear();
                properties.SetFloat(OpacityId,draw.alpha);
                renderer.SetPropertyBlock(properties);
                renderer.enabled = true;
            }
        }

        public void ClearColorOverrides()
        {
            // Migrate the old animation color override without changing the current pose.
            var block = new MaterialPropertyBlock();
            foreach (var renderer in partRenderers)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block);
                float opacity = block.HasFloat(OpacityId) ? block.GetFloat(OpacityId)
                    : block.HasColor(BaseColorId) ? block.GetColor(BaseColorId).a : 1f;
                block.Clear();
                block.SetFloat(OpacityId,Mathf.Clamp01(opacity));
                renderer.SetPropertyBlock(block);
            }
            if (properties != null) properties.Clear();
        }

        void OnDestroy()
        {
            if (ownedMeshes == null) return;
            foreach(var mesh in ownedMeshes) if(mesh != null) Destroy(mesh);
        }
    }
}
