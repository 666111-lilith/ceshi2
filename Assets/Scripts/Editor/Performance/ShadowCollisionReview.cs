using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Palworks.Performance.Editor
{
    public static class ShadowCollisionReview
    {
        public static void Validate()
        {
            var report = new StringBuilder();
            var test = new GameObject("ShadowCollisionReview");
            try
            {
                var world = test.AddComponent<ShadowCollisionWorld>();
                var add = typeof(ShadowCollisionWorld).GetMethod("AddPolygon", BindingFlags.Instance | BindingFlags.NonPublic);
                if (add == null) throw new InvalidOperationException("Shadow collision geometry entry is missing.");
                void Box(float x0, float y0, float x1, float y1) => add.Invoke(world, new object[] {
                    new[] { new Vector2(x0,y0), new Vector2(x1,y0), new Vector2(x1,y1), new Vector2(x0,y1) }
                });
                void Check(bool value, string label, Vector2 result)
                {
                    report.AppendLine(label + "=" + value + " Result=" + result.ToString("F4"));
                    if (!value) throw new InvalidOperationException("Shadow collision check failed: " + label);
                }
                Vector2 half = new Vector2(.25f,.5f);
                Box(0,-1,1,2);
                var walk = world.Move(new Vector2(-2,0),new Vector2(4,0),half);
                Check(walk.x < -.25f && walk.x > -.27f, "SolidSideStops", walk);
                var dash = world.Move(new Vector2(-20,0),new Vector2(200,0),half);
                Check(dash.x < -.25f && dash.x > -.27f, "FastDashDoesNotTunnel", dash);
                var land = world.Move(new Vector2(.5f,4),new Vector2(0,-12),half);
                Check(land.y > 2 && land.y < 2.02f, "SolidTopSupportsLanding", land);
                Check(world.IsGrounded(land,half), "GroundedAfterLanding", land);
                var slide = world.Move(new Vector2(-2,0),new Vector2(4,1),half);
                Check(slide.x < -.25f && slide.x > -.27f && slide.y > .98f, "DiagonalMotionSlides", slide);
                var free = world.Move(new Vector2(-2,4),new Vector2(4,0),half);
                Check(Vector2.Distance(free,new Vector2(2,4)) < .0001f, "ClearSpaceRemainsOpen", free);
                var escape = world.FindNearestFreeFoot(new Vector2(.5f,0),half,new Rect(-3,-3,6,6));
                Check(!world.Overlaps(escape,half), "InitialOverlapResolved", escape);
                // Two separated islands must not fill their shared bounding rectangle.
                Box(4,-1,5,2); Box(7,-1,8,2);
                var opening = world.Move(new Vector2(6,4),new Vector2(0,-7),half);
                Check(Mathf.Abs(opening.y+3) < .0001f, "TransparentOpeningRemainsOpen", opening);
                report.AppendLine("AllShadowCollisionChecksPassed=True");
            }
            finally
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(test);
                else UnityEngine.Object.DestroyImmediate(test);
                Directory.CreateDirectory("output/scene-review");
                File.WriteAllText("output/scene-review/shadow-collision-check.txt", report.ToString());
            }
        }
    }
}
