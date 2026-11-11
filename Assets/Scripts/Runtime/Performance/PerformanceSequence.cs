using UnityEngine;
using Palworks.CameraSystem;
using Palworks.Character;
using Palworks.Rendering;

namespace Palworks.Performance
{
    [DisallowMultipleComponent, DefaultExecutionOrder(1500)]
    public sealed class PerformanceSequence : MonoBehaviour
    {
        public enum Mode { Reality, Entering, Performing, Returning }
        public PuppetPlayerController worldPlayer;
        public Camera view;
        public PerformanceStation[] stations = new PerformanceStation[0];
        public LayerMask performanceLayers = 1 << 9;
        [Min(.05f)] public float transitionDuration = 1.6f;
        [Min(.1f)] public float interactionDistance = 1.7f;
        [Range(1,1.3f)] public float framingMargin = 1.055f;

        [System.NonSerialized] Mode mode;
        [System.NonSerialized] PerformanceStation activeStation;
        [System.NonSerialized] bool initialized;
        [System.NonSerialized] bool captured;
        CameraState returnCamera;
        Vector3 transitionPosition, goalPosition;
        Quaternion transitionRotation, goalRotation;
        float transitionFov, goalFov, elapsed;
        Vector3 returnPlayerPosition;
        Quaternion returnPlayerRotation;
        bool playerControllerEnabled, worldVisualsActive, puppetEnabled, followEnabled, focusEnabled;
        StageCameraFollow follow;
        StageDepthOfField focus;
        WorldTransitionFade transitionFade;
        Renderer[] sourceRenderers;
        int[] sourceLayers;
        int originalCasterLayers;
        bool casterCaptured;
        string lastInteractionMessage;
        bool sprintInput, dashInput, jumpInput;

        struct CameraState
        {
            public Vector3 position;
            public Quaternion rotation;
            public float fieldOfView, orthographicSize;
            public bool orthographic;
            public int cullingMask;
            public CameraClearFlags clearFlags;
            public Color backgroundColor;
        }

        public Mode CurrentMode => mode;
        public PerformanceStation ActiveStation => activeStation;
        public bool IsInPerformance => mode!=Mode.Reality;
        public string LastInteractionMessage => lastInteractionMessage;
        public int CurrentStationIndex
        {
            get
            {
                if(stations!=null) for(int i=0;i<stations.Length;i++)
                    if(stations[i]!=null && !stations[i].Completed) return i;
                return -1;
            }
        }

        void Start() { InitializeSession(); }

        public void InitializeSession()
        {
            if(initialized) return;
            if(worldPlayer==null || view==null) return;
            foreach(var station in stations) if(station!=null) station.ResetSession();
            mode=Mode.Reality; initialized=true;
            RefreshProgress();
        }

        void Update()
        {
            if(!initialized) InitializeSession();
            if(!initialized) return;
            if(mode==Mode.Reality && Input.GetKeyDown(KeyCode.E)) TryEnter(CurrentStationIndex);
            sprintInput=Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift);
            dashInput=Input.GetKeyDown(KeyCode.LeftControl)||Input.GetKeyDown(KeyCode.RightControl);
            jumpInput=Input.GetKeyDown(KeyCode.Space);
            Step(new Vector2(Input.GetAxisRaw("Horizontal"),Input.GetAxisRaw("Vertical")),Time.deltaTime);
        }

        public void Step(Vector2 input,float deltaTime)
        {
            if(!initialized || deltaTime<=0) return;
            if(mode==Mode.Reality) { RefreshProgress(); return; }
            if(mode==Mode.Entering || mode==Mode.Returning)
            {
                elapsed+=deltaTime;
                float t=Mathf.Clamp01(elapsed/Mathf.Max(.05f,transitionDuration));
                float smooth=t*t*(3-2*t);
                view.transform.SetPositionAndRotation(Vector3.Lerp(transitionPosition,goalPosition,smooth),
                    Quaternion.Slerp(transitionRotation,goalRotation,smooth));
                view.fieldOfView=Mathf.Lerp(transitionFov,goalFov,smooth);
                transitionFade?.SetAmount(mode==Mode.Entering?smooth:1-smooth);
                if(mode==Mode.Entering) activeStation.AdvanceEntry(smooth);
                if(t>=1) FinishTransition();
                return;
            }
            activeStation.MoveActor(input,deltaTime,sprintInput,dashInput,jumpInput);
            dashInput=false;jumpInput=false;
            if(activeStation.ActorTouchesKey()) CollectKey();
        }

        public bool CanInteractWithCurrent()
        {
            int index=CurrentStationIndex;
            if(mode!=Mode.Reality || index<0 || worldPlayer==null || stations[index].lever==null) return false;
            Vector3 offset=worldPlayer.transform.position-stations[index].lever.position;
            offset.y=0;
            return offset.sqrMagnitude<=interactionDistance*interactionDistance;
        }

        public bool TryEnter(int index,bool requireProximity=true)
        {
            if(!initialized) InitializeSession();
            if(!initialized || mode!=Mode.Reality || index<0 || index!=CurrentStationIndex || index>=stations.Length) return false;
            var station=stations[index];
            if(station==null || station.Completed || station.Screen==null || station.projector.lens==null ||
                station.stageActor==null || station.stagePuppet==null || station.key==null ||
                worldPlayer.visuals==null || worldPlayer.puppet==null)
            {
                lastInteractionMessage="Performance station references are incomplete.";
                Debug.LogWarning(lastInteractionMessage,this);
                return false;
            }
            if(requireProximity && !CanInteractWithCurrent()) return false;
            float screenDistance=Vector3.Dot(station.Screen.position-station.projector.lens.position,station.Screen.forward);
            float actorDistance=Vector3.Dot(worldPlayer.visuals.position-station.projector.lens.position,station.Screen.forward);
            if(actorDistance<=.08f || actorDistance>=screenDistance-.02f)
            {
                lastInteractionMessage="The player must stand between this projector and its cloth.";
                Debug.LogWarning(lastInteractionMessage,this);
                return false;
            }
            activeStation=station;
            CaptureReality();
            worldPlayer.enabled=false;
            if(worldPlayer.puppet!=null) worldPlayer.puppet.enabled=false;
            if(follow!=null) follow.enabled=false;
            if(focus!=null) focus.enabled=false;
            station.PrepareStage(worldPlayer);
            SubstituteProjectionCaster();
            transitionFade=new WorldTransitionFade();
            transitionFade.Begin(view,station,worldPlayer);
            transitionFade.SetAmount(0);
            view.orthographic=false;
            view.cullingMask=returnCamera.cullingMask & ~(1<<PerformanceStation.ActorLayer);
            var scale=station.Screen.lossyScale;
            float width=station.ScreenSize.x*Mathf.Abs(scale.x),height=station.ScreenSize.y*Mathf.Abs(scale.y);
            float fov=returnCamera.orthographic?30:returnCamera.fieldOfView;
            float halfAngle=Mathf.Tan(fov*Mathf.Deg2Rad*.5f);
            float distance=Mathf.Max(height*.5f/halfAngle,width*.5f/(halfAngle*Mathf.Max(.1f,view.aspect)))*framingMargin;
            BeginTransition(Mode.Entering,station.Screen.position-station.Screen.forward*distance,station.Screen.rotation,fov);
            lastInteractionMessage=null;
            return true;
        }

        public bool CollectKey()
        {
            if(mode!=Mode.Performing || activeStation==null) return false;
            activeStation.MarkComplete();
            RestoreProjectionCaster();
            RestorePlayer();
            view.cullingMask=returnCamera.cullingMask & ~(1<<PerformanceStation.ActorLayer);
            BeginTransition(Mode.Returning,returnCamera.position,returnCamera.rotation,returnCamera.fieldOfView);
            RefreshProgress();
            return true;
        }

        public void FinishTransition()
        {
            if(mode==Mode.Entering)
            {
                view.transform.SetPositionAndRotation(goalPosition,goalRotation); view.fieldOfView=goalFov;
                activeStation.AdvanceEntry(1);
                transitionFade?.SetAmount(1);
                if(worldPlayer.visuals!=null) worldPlayer.visuals.gameObject.SetActive(false);
                view.cullingMask=(int)performanceLayers & ~(1<<PerformanceStation.ActorLayer);
                mode=Mode.Performing;
                return;
            }
            if(mode!=Mode.Returning) return;
            transitionFade?.Restore(); transitionFade=null;
            RestoreCamera();
            if(worldPlayer.puppet!=null) worldPlayer.puppet.enabled=puppetEnabled;
            worldPlayer.enabled=playerControllerEnabled;
            if(follow!=null) { follow.ResetRuntimeState(); follow.enabled=followEnabled; }
            if(focus!=null) focus.enabled=focusEnabled;
            captured=false; activeStation=null; mode=Mode.Reality;
            RefreshProgress();
        }

        void CaptureReality()
        {
            returnCamera=new CameraState {position=view.transform.position,rotation=view.transform.rotation,
                fieldOfView=view.fieldOfView,orthographic=view.orthographic,orthographicSize=view.orthographicSize,cullingMask=view.cullingMask,
                clearFlags=view.clearFlags,backgroundColor=view.backgroundColor};
            returnPlayerPosition=worldPlayer.transform.position; returnPlayerRotation=worldPlayer.transform.rotation;
            playerControllerEnabled=worldPlayer.enabled;
            worldVisualsActive=worldPlayer.visuals!=null && worldPlayer.visuals.gameObject.activeSelf;
            puppetEnabled=worldPlayer.puppet!=null && worldPlayer.puppet.enabled;
            follow=view.GetComponent<StageCameraFollow>(); focus=view.GetComponent<StageDepthOfField>();
            followEnabled=follow!=null && follow.enabled; focusEnabled=focus!=null && focus.enabled;
            captured=true;
        }

        void RestorePlayer()
        {
            if(worldPlayer==null) return;
            var motor=worldPlayer.GetComponent<CharacterController>();
            bool motorEnabled=motor!=null && motor.enabled;
            if(motor!=null) motor.enabled=false;
            worldPlayer.transform.SetPositionAndRotation(returnPlayerPosition,returnPlayerRotation);
            if(motor!=null) motor.enabled=motorEnabled;
            if(worldPlayer.visuals!=null) worldPlayer.visuals.gameObject.SetActive(worldVisualsActive);
            // Its frozen mesh remains the same silhouette while the world fades back in.
        }

        void SubstituteProjectionCaster()
        {
            sourceRenderers=worldPlayer.visuals.GetComponentsInChildren<Renderer>(true);
            sourceLayers=new int[sourceRenderers.Length];
            for(int i=0;i<sourceRenderers.Length;i++)
            {
                sourceLayers[i]=sourceRenderers[i].gameObject.layer;
                sourceRenderers[i].gameObject.layer=0;
            }
            originalCasterLayers=activeStation.projector.casterLayers;
            activeStation.projector.casterLayers=originalCasterLayers | (1<<PerformanceStation.ActorLayer);
            activeStation.projector.RefreshProjection();
            casterCaptured=true;
        }

        void RestoreProjectionCaster()
        {
            if(!casterCaptured) return;
            if(sourceRenderers!=null) for(int i=0;i<sourceRenderers.Length;i++)
                if(sourceRenderers[i]!=null) sourceRenderers[i].gameObject.layer=sourceLayers[i];
            if(activeStation!=null && activeStation.projector!=null)
            {
                activeStation.projector.casterLayers=originalCasterLayers;
                activeStation.projector.RefreshProjection();
            }
            sourceRenderers=null; sourceLayers=null; casterCaptured=false;
        }

        void RestoreCamera()
        {
            if(view==null) return;
            view.transform.SetPositionAndRotation(returnCamera.position,returnCamera.rotation);
            view.fieldOfView=returnCamera.fieldOfView; view.orthographic=returnCamera.orthographic;
            view.orthographicSize=returnCamera.orthographicSize; view.cullingMask=returnCamera.cullingMask;
            view.clearFlags=returnCamera.clearFlags; view.backgroundColor=returnCamera.backgroundColor;
        }

        void BeginTransition(Mode next,Vector3 position,Quaternion rotation,float fieldOfView)
        {
            transitionPosition=view.transform.position; transitionRotation=view.transform.rotation;
            transitionFov=view.fieldOfView; goalPosition=position; goalRotation=rotation; goalFov=fieldOfView;
            elapsed=0; mode=next;
        }

        public void RefreshProgress()
        {
            int current=CurrentStationIndex;
            if(current>=0 && worldPlayer!=null) stations[current].ArmEntranceIfInside(worldPlayer.transform.position);
            if(stations!=null) for(int i=0;i<stations.Length;i++)
                if(stations[i]!=null) stations[i].RefreshGates(i==current);
        }

        void OnDisable()
        {
            if(!captured) return;
            if(activeStation!=null) activeStation.HideStage();
            RestoreProjectionCaster();
            transitionFade?.Restore(); transitionFade=null;
            RestorePlayer(); RestoreCamera();
            if(worldPlayer!=null) worldPlayer.enabled=playerControllerEnabled;
            if(worldPlayer!=null && worldPlayer.puppet!=null) worldPlayer.puppet.enabled=puppetEnabled;
            if(follow!=null) {follow.ResetRuntimeState();follow.enabled=followEnabled;}
            if(focus!=null) focus.enabled=focusEnabled;
            captured=false; activeStation=null; mode=Mode.Reality;
        }

    }
}
