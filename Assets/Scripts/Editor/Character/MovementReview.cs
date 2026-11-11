using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using Palworks.Character;

namespace Palworks.Editor
{
    public static class MovementReview
    {
        public static void Validate()
        {
            if(!EditorApplication.isPlaying) throw new InvalidOperationException("Movement verification requires Play mode.");
            var source=UnityEngine.Object.FindObjectOfType<PuppetPlayerController>();
            if(source==null) throw new InvalidOperationException("Player missing.");
            var floor=new GameObject("MovementReviewGround");
            floor.transform.position=new Vector3(10000,-1,10000);
            floor.AddComponent<BoxCollider>().size=new Vector3(100,2,100);
            var copy=UnityEngine.Object.Instantiate(source.gameObject);
            var motor=copy.GetComponent<CharacterController>();
            var player=copy.GetComponent<PuppetPlayerController>();
            GameObject wall=null;
            try
            {
                player.enabled=false; player.puppet.enabled=false;
                int total=0;
                foreach(var clip in player.puppet.Data.clips)
                {
                    for(int frame=0;frame<clip.frames.Length;frame++)
                    {
                        player.puppet.ApplyFrame(clip.name,frame);
                        foreach(var draw in clip.frames[frame].parts)
                        {
                            if(draw.part>=player.puppet.partMeshes.Length || draw.bitmap>=player.puppet.bitmapMaterials.Length)
                                throw new InvalidOperationException("Unbound movement part in "+clip.name);
                            foreach(var vertex in player.puppet.partMeshes[draw.part].sharedMesh.vertices)
                                if(float.IsNaN(vertex.x) || float.IsNaN(vertex.y)) throw new InvalidOperationException("Invalid pose vertex.");
                        }
                        total++;
                    }
                }
                Reset(player,motor);
                float x=player.transform.position.x;
                for(int i=0;i<30;i++) player.StepMovement(Vector2.right,false,false,false,1f/120);
                float walk=player.transform.position.x-x;
                Reset(player,motor); x=player.transform.position.x;
                for(int i=0;i<30;i++) player.StepMovement(Vector2.right,true,false,false,1f/120);
                float run=player.transform.position.x-x;
                if(Mathf.Abs(run/walk-player.sprintMultiplier)>.03f) throw new InvalidOperationException("Sprint speed ratio mismatch.");
                Reset(player,motor);
                float origin=player.transform.position.y, peak=origin;
                bool airborne=false;
                for(int i=0;i<180;i++)
                {
                    player.StepMovement(Vector2.zero,false,false,i==0,1f/120);
                    peak=Mathf.Max(peak,player.transform.position.y);
                    airborne|=player.IsAirborne;
                }
                float height=peak-origin;
                if(!airborne || height<player.jumpHeight*.85f || height>player.jumpHeight*1.1f || player.IsAirborne)
                    throw new InvalidOperationException("Jump/landing failed: height="+height+" airborne="+player.IsAirborne);
                Reset(player,motor);
                wall=new GameObject("MovementReviewWall");
                wall.transform.position=new Vector3(10002,1.5f,10000);
                wall.AddComponent<BoxCollider>().size=new Vector3(.2f,3,8);
                Physics.SyncTransforms();
                for(int i=0;i<45;i++) player.StepMovement(Vector2.right,false,i==0,false,1f/120);
                float stopped=player.transform.position.x;
                if(stopped>10001.9f || stopped<10001) throw new InvalidOperationException("Dash collision failed: "+stopped);
                Directory.CreateDirectory("output/scene-review");
                File.WriteAllText("output/scene-review/movement-check.txt",
                    "PASS OriginalPoses="+total+" Clips="+player.puppet.Data.clips.Length+Environment.NewLine+
                    "WalkDistance="+walk+" SprintDistance="+run+" Ratio="+(run/walk)+Environment.NewLine+
                    "JumpHeight="+height+" Landed=True DashBlocked=True"+Environment.NewLine+
                    "PreservedSceneMoveSpeed="+source.moveSpeed);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(copy);
                UnityEngine.Object.DestroyImmediate(floor);
                if(wall!=null) UnityEngine.Object.DestroyImmediate(wall);
            }
        }

        static void Reset(PuppetPlayerController player,CharacterController motor)
        {
            // Toggle the controller to clear action timers without changing its authored parameters.
            player.enabled=true; player.enabled=false;
            player.puppet.CancelAction();
            motor.enabled=false;
            player.transform.position=new Vector3(10000,.03f,10000);
            motor.enabled=true;
            Physics.SyncTransforms();
            for(int i=0;i<12;i++) player.StepMovement(Vector2.zero,false,false,false,1f/120);
        }
    }
}
