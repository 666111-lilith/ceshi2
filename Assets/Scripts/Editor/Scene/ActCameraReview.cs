using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Palworks.CameraSystem;

namespace Palworks.Editor
{
    /// <summary>Offline traversal of the authored camera; never moves or saves scene objects.</summary>
    public static class ActCameraReview
    {
        const float Left=-11.6f, Right=59.6f, Front=-5.8f, Back=2.9f, Speed=4.8f;

        [MenuItem("Palworks/Validate Act Camera")]
        public static void Validate()
        {
            var source=Camera.main;
            var settings=source!=null?source.GetComponent<StageCameraFollow>():null;
            if(settings==null || settings.target==null)
                throw new InvalidOperationException("An authored StageCameraFollow and target are required.");
            var originalCamera=source.transform.position;
            var originalTarget=settings.target.position;
            var report=new StringBuilder("Result=PASS\nMode=Offline (scene transforms unchanged)\n");
            var csv=new StringBuilder("fps,depth,time,playerX,playerZ,cameraX,cameraZ,viewportX,viewportY\n");
            float minimumX=1,maximumX=0,minimumY=1,maximumY=0,maxSpeed=0;
            int samples=0;
            foreach(int fps in new[]{30,60,120})
            foreach(float depth in new[]{Front,-1.5f,Back})
            {
                var holder=new GameObject("ActCameraValidation") {hideFlags=HideFlags.HideAndDontSave};
                var actor=new GameObject("ActCameraValidationTarget") {hideFlags=HideFlags.HideAndDontSave};
                try
                {
                    var camera=holder.AddComponent<Camera>();camera.CopyFrom(source);camera.enabled=false;camera.aspect=16f/9f;
                    holder.transform.SetPositionAndRotation(originalCamera,source.transform.rotation);
                    actor.transform.position=originalTarget;
                    var follow=holder.AddComponent<StageCameraFollow>();
                    EditorUtility.CopySerialized(settings,follow);follow.enabled=false;follow.target=actor.transform;
                    follow.ResetRuntimeState();
                    float dt=1f/fps,time=0;
                    Vector3 position=originalCamera,target=originalTarget;
                    Action<Vector3> step=next=>
                    {
                        var previous=position;target=next;position=follow.Evaluate(target,dt);
                        holder.transform.position=position;time+=dt;samples++;
                        maxSpeed=Mathf.Max(maxSpeed,Mathf.Abs(position.x-previous.x)/dt);
                        if(!Finite(position) || position.x<follow.minCameraX-.001f || position.x>follow.maxCameraX+.001f)
                            throw new Exception("Invalid camera state or camera exceeded authored bounds.");
                        if(Mathf.Abs(position.y-originalCamera.y)>.0001f)
                            throw new Exception("Follow changed the authored camera height.");
                        // Check feet and upper body, not only an abstract target at ground level.
                        foreach(float height in new[]{0f,.9f,1.8f})
                        foreach(float side in new[]{-.75f,.75f})
                        {
                            var point=camera.WorldToViewportPoint(target+Vector3.up*height+Vector3.right*side);
                            minimumX=Mathf.Min(minimumX,point.x);maximumX=Mathf.Max(maximumX,point.x);
                            minimumY=Mathf.Min(minimumY,point.y);maximumY=Mathf.Max(maximumY,point.y);
                            if(point.z<=0 || point.x<.04f || point.x>.96f || point.y<.02f || point.y>.98f)
                                throw new Exception(string.Format(CultureInfo.InvariantCulture,
                                    "Actor approaches/leaves viewport: fps={0}, target={1}, camera={2}, viewport={3}. Check camera limits, especially front corners.",fps,target,position,point));
                        }
                        if(fps==60)
                        {
                            var point=camera.WorldToViewportPoint(target);
                            csv.AppendLine(string.Format(CultureInfo.InvariantCulture,"{0},{1:F2},{2:F3},{3:F3},{4:F3},{5:F3},{6:F3},{7:F4},{8:F4}",fps,depth,time,target.x,target.z,position.x,position.z,point.x,point.y));
                        }
                    };
                    Action<Vector3> walk=destination=>
                    {
                        while(Vector3.Distance(target,destination)>.0001f)
                            step(Vector3.MoveTowards(target,destination,Speed*dt));
                    };
                    for(int i=0;i<fps;i++) step(target);
                    if(Vector3.Distance(position,originalCamera)>.0001f) throw new Exception("Camera drifts while idle.");
                    walk(new Vector3(target.x,target.y,depth));
                    walk(new Vector3(Right,target.y,depth));
                    for(int i=0;i<fps;i++) step(target);
                    walk(new Vector3(Left,target.y,depth));
                    for(int i=0;i<fps;i++) step(target);
                    walk(new Vector3(Right,target.y,depth));
                    // Both front/back transitions at both ends, where perspective has least margin.
                    walk(new Vector3(Right,target.y,Back));
                    walk(new Vector3(Right,target.y,Front));
                    walk(new Vector3(Left,target.y,Front));
                    walk(new Vector3(Left,target.y,Back));
                    for(int i=0;i<fps*3;i++) step(target);
                    var settled=position;
                    for(int i=0;i<fps*2;i++) step(target);
                    if(Vector3.Distance(settled,position)>.01f) throw new Exception("Camera does not settle after stopping.");

                    // Well inside the limits, equally spaced remote positions must produce
                    // equal camera travel; the previous 0.72 total-displacement rule fails this.
                    holder.transform.position=originalCamera;actor.transform.position=originalTarget;follow.ResetRuntimeState();
                    Vector3 a=originalTarget,b=originalTarget;a.x=24;b.x=40;
                    Vector3 ca=originalCamera,cb=originalCamera;
                    for(int i=0;i<fps*5;i++) ca=follow.Evaluate(a,dt);
                    for(int i=0;i<fps*5;i++) cb=follow.Evaluate(b,dt);
                    if(Mathf.Abs((cb.x-ca.x)-(b.x-a.x))>.025f)
                        throw new Exception("Long-distance framing does not follow one-to-one.");
                    // Reset and explicit Tick must match the public Evaluate API.
                    holder.transform.position=originalCamera;actor.transform.position=originalTarget;follow.ResetRuntimeState();
                    var sample=originalTarget+new Vector3(.1f,0,.02f);
                    var evaluated=follow.Evaluate(sample,dt);
                    holder.transform.position=originalCamera;actor.transform.position=originalTarget;follow.ResetRuntimeState();
                    actor.transform.position=sample;follow.Tick(dt);
                    if(Vector3.Distance(evaluated,holder.transform.position)>.0001f)
                        throw new Exception("Tick and Evaluate disagree.");
                }
                finally {UnityEngine.Object.DestroyImmediate(holder);UnityEngine.Object.DestroyImmediate(actor);}
            }
            if(source.transform.position!=originalCamera || settings.target.position!=originalTarget)
                throw new Exception("Offline review changed scene transforms.");
            if(maxSpeed>Speed*1.15f) throw new Exception("Camera makes an abrupt horizontal jump.");
            report.AppendLine("Samples="+samples);
            report.AppendLine("FrameRates=30,60,120\nTravel=full-length both directions, depth transitions at both ends");
            report.AppendLine("CameraBounds="+settings.minCameraX+".."+settings.maxCameraX);
            report.AppendLine("CompositionOffsetLimit="+settings.maxCompositionOffset);
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,"ViewportX={0:F4}..{1:F4}\nViewportY={2:F4}..{3:F4}\nMaxCameraSpeed={4:F4}",minimumX,maximumX,minimumY,maximumY,maxSpeed));
            Directory.CreateDirectory("output/scene-review");
            File.WriteAllText("output/scene-review/act-camera-check.txt",report.ToString());
            File.WriteAllText("output/scene-review/act-camera-motion.csv",csv.ToString());
            Debug.Log("PALWORKS: act camera offline validation passed. "+samples+" movement samples.");
        }

        static bool Finite(Vector3 v)=>!float.IsNaN(v.x)&&!float.IsInfinity(v.x)&&
            !float.IsNaN(v.y)&&!float.IsInfinity(v.y)&&!float.IsNaN(v.z)&&!float.IsInfinity(v.z);
    }
}
