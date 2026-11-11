using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Palworks.Character;

namespace Palworks.Editor
{
    public static class ActMovementReview
    {
        public static void Validate()
        {
            if(!EditorApplication.isPlaying) throw new InvalidOperationException("Validate movement in Play.");
            var obstacle=UnityEngine.Object.FindObjectsOfType<BoxCollider>().First(c=>
                c.transform.root.name=="Obstacles" && c.name=="Rock" && c.bounds.center.x<12);
            var probe=new GameObject("MovementValidation") {hideFlags=HideFlags.HideAndDontSave};
            var controller=probe.AddComponent<CharacterController>();
            controller.height=1.7f; controller.radius=.24f; controller.center=new Vector3(0,.88f,0);
            controller.stepOffset=.15f; controller.skinWidth=.025f;
            try
            {
                var bounds=obstacle.bounds;
                probe.transform.position=new Vector3(bounds.min.x-1,.03f,bounds.center.z);
                Physics.SyncTransforms();
                for(int i=0;i<35;i++) controller.Move(new Vector3(.1f,-.03f,0));
                float stopped=probe.transform.position.x;
                if(stopped>bounds.min.x-.15f || stopped<bounds.min.x-.5f)
                    throw new Exception("Controller does not stop at the rock face: "+stopped);
                // Walk around its front edge, then return to the original depth on the other side.
                float front=bounds.min.z-.7f;
                for(int i=0;i<30 && probe.transform.position.z>front;i++) controller.Move(new Vector3(0,-.03f,-.1f));
                float destination=bounds.max.x+.8f;
                for(int i=0;i<60 && probe.transform.position.x<destination;i++) controller.Move(new Vector3(.1f,-.03f,0));
                for(int i=0;i<30 && probe.transform.position.z<bounds.center.z;i++) controller.Move(new Vector3(0,-.03f,.1f));
                if(probe.transform.position.x<bounds.max.x+.5f || Mathf.Abs(probe.transform.position.z-bounds.center.z)>.15f)
                    throw new Exception("Controller cannot walk around the obstacle.");
                Directory.CreateDirectory("output/scene-review");
                File.WriteAllText("output/scene-review/movement-check.txt","Result=PASS\nDirectPath=BlockedByRock\nDetour=Reachable\n");
            }
            finally { UnityEngine.Object.DestroyImmediate(probe); }
        }
    }
}
