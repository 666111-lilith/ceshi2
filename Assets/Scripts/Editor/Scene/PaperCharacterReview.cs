using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Palworks.Character;

namespace Palworks.Editor
{
    public static class PaperCharacterReview
    {
        public static void CaptureWalk()
        {
            if(!EditorApplication.isPlaying) throw new InvalidOperationException("Run this review in Play to use cloned meshes.");
            var animator=UnityEngine.Object.FindObjectOfType<PuppetAnimator>();
            var controller=animator.GetComponentInParent<PuppetPlayerController>();
            var scale=animator.transform.localScale;
            bool animationEnabled=animator.enabled, controllerEnabled=controller.enabled;
            int count=0;
            try
            {
                animator.enabled=false; controller.enabled=false;
                var walk=animator.Data.clips.First(c=>c.name=="Walk");
                int head=Array.FindIndex(animator.Data.parts,p=>p.name.Contains("HeadBack"));
                foreach(float direction in new[]{1f,-1f})
                {
                    animator.transform.localScale=new Vector3(Mathf.Abs(scale.x)*direction,scale.y,scale.z);
                    animator.SetMoving(true);
                    for(int frame=0;frame<walk.frames.Length;frame++)
                    {
                        animator.ApplyFrame("Walk",frame);
                        var renderer=animator.partRenderers[head];
                        if(!renderer.enabled || renderer.sharedMaterial.GetTexture("_BlockoutShadeMap")==null)
                            throw new Exception("Walk head regions not bound at frame "+frame);
                        count++;
                    }
                    animator.ApplyFrame("Walk",6);
                    PaperRoomBuilder.Capture();
                    File.Copy("output/scene-review/paper-room-play.png",
                        "output/scene-review/walk-"+(direction>0?"right":"left")+".png",true);
                }
                File.WriteAllText("output/scene-review/walk-runtime-check.txt","Result=PASS\nHeadAndMaterialBoundPoses="+count+"\n");
            }
            finally
            {
                animator.transform.localScale=scale;
                animator.SetMoving(false); animator.ApplyFrame("Idle",0);
                animator.enabled=animationEnabled; controller.enabled=controllerEnabled;
            }
        }
    }
}
