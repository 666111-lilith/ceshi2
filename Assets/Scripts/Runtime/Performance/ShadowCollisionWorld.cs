using System;
using System.Collections.Generic;
using UnityEngine;
using Palworks.Projection;

namespace Palworks.Performance
{
    /// <summary>Solid coverage projected from source sprites. Penumbrae never become walls.</summary>
    [DisallowMultipleComponent]
    public sealed class ShadowCollisionWorld : MonoBehaviour
    {
        public PaperProjector projector;
        [Range(32, 256)] public int silhouetteResolution = 256;
        [Range(.01f, .95f)] public float alphaThreshold = .5f;
        [Range(.8f, .99f)] public float solidCoverageThreshold = .9f;
        [Range(256, 2048)] public int solidMaskResolution = 1280;
        [Min(.0001f)] public float contactSkin = .001f;
        public bool drawCollisionOutlines = true;
        [SerializeField] Transform generatedBodies;
        [SerializeField] List<Body> bodies = new List<Body>();
        readonly List<Polygon> polygons = new List<Polygon>();
        readonly Dictionary<int, Pixels> texturePixels = new Dictionary<int, Pixels>();
        readonly List<Vector2> verticalIntervals = new List<Vector2>();
        public int PolygonCount => polygons.Count;
        public int BodyCount => bodies.Count;
        public int SolidCasterCount { get; private set; }
        public int SolidMaskWidth { get; private set; }
        public int SolidMaskHeight { get; private set; }
        public string CollisionSource { get; private set; } = "Unbuilt";

        [Serializable] sealed class Body
        {
            public SpriteRenderer source;
            public PolygonCollider2D collider;
            public bool integratedMask;
        }
        sealed class Polygon
        {
            public Vector2[] vertices;
            public Rect bounds;
        }
        sealed class Pixels
        {
            public int width, height;
            public Color32[] data;
        }
        struct Run
        {
            public int x, y, width, height;
        }

        void Awake() { RestorePolygons(); }

        [ContextMenu("Rebuild Shadow Collisions")]
        public void Rebuild() { Rebuild(projector); }

        public void Rebuild(PaperProjector source)
        {
            projector = source;
            if (projector == null || projector.screen == null || projector.lens == null) return;
            polygons.Clear(); bodies.Clear(); texturePixels.Clear();
            if (generatedBodies != null)
            {
                generatedBodies.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(generatedBodies.gameObject);
                else DestroyImmediate(generatedBodies.gameObject);
            }
            generatedBodies = new GameObject("ShadowBodies").transform;
            generatedBodies.SetParent(projector.screen, false);
            generatedBodies.gameObject.layer = 2;
            var paths=CaptureSolidCoverage();
            if(paths.Count>0)
            {
                var item = new GameObject("SolidShadows");
                item.layer = 2;
                item.transform.SetParent(generatedBodies, false);
                var collider = item.AddComponent<PolygonCollider2D>();
                // Diagnostic shapes only. Our continuous sweep below owns movement.
                collider.isTrigger = true;
                collider.pathCount = paths.Count;
                for (int path = 0; path < paths.Count; path++)
                {
                    collider.SetPath(path, paths[path]);
                    AddPolygon(paths[path]);
                }
                bodies.Add(new Body { collider = collider, integratedMask = true });
            }
            texturePixels.Clear();
        }

        void RestorePolygons()
        {
            polygons.Clear();
            foreach (var body in bodies)
                if (body.collider != null && (body.integratedMask || IsSolidCaster(body.source)))
                    for (int i = 0; i < body.collider.pathCount; i++) AddPolygon(body.collider.GetPath(i));
        }

        List<Vector2[]> CaptureSolidCoverage()
        {
            if(projector.shadowTexture==null || projector.projectionCamera==null)
            {
                CollisionSource="SourceGeometryFallback";SolidCasterCount=0;
                var fallback=new List<Vector2[]>();
                foreach(var renderer in FindObjectsOfType<SpriteRenderer>())
                    if(renderer.sprite!=null && (projector.casterLayers.value&(1<<renderer.gameObject.layer))!=0 && IsSolidCaster(renderer))
                    {fallback.AddRange(ProjectSilhouette(renderer));SolidCasterCount++;}
                return fallback;
            }
            CollisionSource="RenderedUmbra";
            var hidden=new List<Renderer>();
            Texture2D copy=null;
            var previous=RenderTexture.active;
            SolidCasterCount=0;
            try
            {
                // Use exactly the area-light integration used by the visible curtain.
                // Player meshes, pale scenery and non-solid props never enter this mask.
                foreach(var renderer in FindObjectsOfType<Renderer>())
                {
                    if(!renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                        (projector.casterLayers.value&(1<<renderer.gameObject.layer))==0)continue;
                    if(renderer is SpriteRenderer sprite && sprite.sprite!=null && IsSolidCaster(sprite))SolidCasterCount++;
                    else{hidden.Add(renderer);renderer.enabled=false;}
                }
                projector.RenderPreview();
                var target=projector.shadowTexture;
                RenderTexture.active=target;
                copy=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false,true);
                copy.ReadPixels(new Rect(0,0,target.width,target.height),0,0);copy.Apply(false,false);
                var pixels=copy.GetPixels32();
                SolidMaskWidth=Mathf.Min(target.width,solidMaskResolution);
                SolidMaskHeight=Mathf.Max(1,Mathf.RoundToInt(target.height*(float)SolidMaskWidth/target.width));
                int width=SolidMaskWidth,height=SolidMaskHeight,cutoff=Mathf.CeilToInt(solidCoverageThreshold*255);
                var runs=new List<Run>();var previousRow=new Dictionary<long,int>();
                for(int y=0;y<height;y++)
                {
                    var row=new Dictionary<long,int>();int x=0;
                    while(x<width)
                    {
                        if(!Solid(x,y)){x++;continue;}
                        int first=x++;while(x<width && Solid(x,y))x++;
                        int count=x-first;long key=((long)first<<32)|(uint)count;
                        if(previousRow.TryGetValue(key,out int index)){var run=runs[index];run.height++;runs[index]=run;}
                        else{index=runs.Count;runs.Add(new Run{x=first,y=y,width=count,height=1});}
                        row[key]=index;
                    }
                    previousRow=row;
                }
                var paths=new List<Vector2[]>(runs.Count);var size=projector.screenSize;
                foreach(var run in runs)
                {
                    float x0=(run.x/(float)width-.5f)*size.x,x1=((run.x+run.width)/(float)width-.5f)*size.x;
                    float y0=(run.y/(float)height-.5f)*size.y,y1=((run.y+run.height)/(float)height-.5f)*size.y;
                    paths.Add(new[]{new Vector2(x0,y0),new Vector2(x1,y0),new Vector2(x1,y1),new Vector2(x0,y1)});
                }
                return paths;

                bool Solid(int x,int y)
                {
                    int px=Mathf.Clamp(Mathf.FloorToInt((x+.5f)*target.width/width),0,target.width-1);
                    int py=Mathf.Clamp(Mathf.FloorToInt((y+.5f)*target.height/height),0,target.height-1);
                    return pixels[py*target.width+px].a>=cutoff;
                }
            }
            finally
            {
                RenderTexture.active=previous;
                foreach(var renderer in hidden)if(renderer!=null)renderer.enabled=true;
                if(copy!=null){if(Application.isPlaying)Destroy(copy);else DestroyImmediate(copy);}
                projector.RenderPreview();
            }
        }

        public static bool IsSolidCaster(SpriteRenderer source)
        {
            if (source == null || !source.enabled || !source.gameObject.activeInHierarchy) return false;
            var style = source.GetComponentInParent<ProjectionCasterStyle>();
            return style != null && style.solid && style.depthLayer == ProjectionCasterStyle.DepthLayer.Solid &&
                style.opacity >= .98f && source.color.a >= .98f;
        }

        void AddPolygon(Vector2[] points)
        {
            if (points == null || points.Length < 3) return;
            var min = points[0]; var max = min;
            for (int i = 1; i < points.Length; i++) { min = Vector2.Min(min, points[i]); max = Vector2.Max(max, points[i]); }
            polygons.Add(new Polygon { vertices = points, bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y) });
        }

        List<Vector2[]> ProjectSilhouette(SpriteRenderer renderer)
        {
            var result = new List<Vector2[]>();
            Sprite sprite = renderer.sprite;
            // Runtime atlas packing is not used by the source art. Refuse a rotated
            // atlas instead of silently producing collision in a transparent area.
            if (sprite.packed && sprite.packingRotation != SpritePackingRotation.None) return result;
            var pixels = ReadPixels(sprite.texture);
            Rect rect = sprite.textureRect;
            int width = Mathf.Clamp(Mathf.CeilToInt(rect.width), 1, silhouetteResolution);
            int height = Mathf.Clamp(Mathf.CeilToInt(rect.height), 1, silhouetteResolution);
            float cellX = rect.width / width, cellY = rect.height / height;
            var runs = new List<Run>();
            var previous = new Dictionary<long, int>();
            int cutoff = Mathf.RoundToInt(alphaThreshold * 255);
            for (int y = 0; y < height; y++)
            {
                var next = new Dictionary<long, int>();
                int x = 0;
                while (x < width)
                {
                    if (!Opaque(x, y)) { x++; continue; }
                    int first = x++;
                    while (x < width && Opaque(x, y)) x++;
                    int count = x - first;
                    long key = ((long)first << 32) | (uint)count;
                    int index;
                    if (previous.TryGetValue(key, out index))
                    {
                        var run = runs[index]; run.height++; runs[index] = run;
                    }
                    else
                    {
                        index = runs.Count;
                        runs.Add(new Run { x = first, y = y, width = count, height = 1 });
                    }
                    next[key] = index;
                }
                previous = next;
            }
            Vector2 offset = sprite.textureRectOffset - sprite.pivot;
            foreach (var run in runs)
            {
                float x0 = (offset.x + run.x * cellX) / sprite.pixelsPerUnit;
                float x1 = (offset.x + (run.x + run.width) * cellX) / sprite.pixelsPerUnit;
                float y0 = (offset.y + run.y * cellY) / sprite.pixelsPerUnit;
                float y1 = (offset.y + (run.y + run.height) * cellY) / sprite.pixelsPerUnit;
                var corners = new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) };
                bool valid = true;
                for (int i = 0; i < corners.Length; i++)
                {
                    Vector2 corner = corners[i];
                    if (renderer.flipX) corner.x = -corner.x;
                    if (renderer.flipY) corner.y = -corner.y;
                    if (!Project(renderer.transform.TransformPoint(corner), out corners[i])) { valid = false; break; }
                }
                if (valid)
                {
                    Vector2 min = corners[0], max = min;
                    for (int i = 1; i < corners.Length; i++) { min = Vector2.Min(min, corners[i]); max = Vector2.Max(max, corners[i]); }
                    Vector2 half = projector.screenSize * .5f;
                    if (max.x >= -half.x && min.x <= half.x && max.y >= -half.y && min.y <= half.y) result.Add(corners);
                }
            }
            return result;

            bool Opaque(int x, int y)
            {
                int px = Mathf.Clamp(Mathf.FloorToInt(rect.x + (x + .5f) * cellX), 0, pixels.width - 1);
                int py = Mathf.Clamp(Mathf.FloorToInt(rect.y + (y + .5f) * cellY), 0, pixels.height - 1);
                return pixels.data[py * pixels.width + px].a >= cutoff;
            }
        }

        Pixels ReadPixels(Texture2D texture)
        {
            int key = texture.GetInstanceID();
            if (texturePixels.TryGetValue(key, out var cached)) return cached;
            Color32[] pixels;
            if (texture.isReadable) pixels = texture.GetPixels32();
            else
            {
                var previous = RenderTexture.active;
                var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Texture2D copy = null;
                try
                {
                    Graphics.Blit(texture, target);
                    RenderTexture.active = target;
                    copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, true);
                    copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                    copy.Apply(false, false);
                    pixels = copy.GetPixels32();
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(target);
                    if (copy != null) { if (Application.isPlaying) Destroy(copy); else DestroyImmediate(copy); }
                }
            }
            cached = new Pixels { width = texture.width, height = texture.height, data = pixels };
            texturePixels[key] = cached;
            return cached;
        }

        bool Project(Vector3 world, out Vector2 projected)
        {
            Vector3 ray = world - projector.lens.position;
            float depth = Vector3.Dot(ray, projector.screen.forward);
            float screenDepth = Vector3.Dot(projector.screen.position - projector.lens.position, projector.screen.forward);
            projected = default;
            if (depth <= .025f || depth > screenDepth + .05f) return false;
            Vector3 point = projector.lens.position + ray * (screenDepth / depth);
            projected = projector.screen.InverseTransformPoint(point);
            return true;
        }

        /// <summary>Foot and dimensions are in the cloth's local XY coordinates.</summary>
        public Vector2 Move(Vector2 foot, Vector2 delta, Vector2 halfsize)
        {
            halfsize = new Vector2(Mathf.Max(.0005f, halfsize.x), Mathf.Max(.0005f, halfsize.y));
            Vector2 center = foot + new Vector2(0, halfsize.y);
            for (int iteration = 0; iteration < 5 && delta.sqrMagnitude > .00000001f; iteration++)
            {
                float first = 1; Vector2 normal = Vector2.zero;
                foreach (var polygon in polygons)
                    if (Sweep(center, delta, halfsize, polygon, out float time, out Vector2 hitNormal) && time < first)
                    { first = time; normal = hitNormal; }
                if (normal == Vector2.zero) { center += delta; break; }
                float length = delta.magnitude;
                center += delta * Mathf.Max(0, first - Mathf.Min(contactSkin,.0015f) / length);
                delta *= 1 - first;
                float into = Vector2.Dot(delta, normal);
                if (into < 0) delta -= normal * into;
            }
            return center - new Vector2(0, halfsize.y);
        }

        public bool Overlaps(Vector2 foot, Vector2 halfsize)
        {
            Vector2 center = foot + new Vector2(0, halfsize.y);
            foreach (var polygon in polygons)
                if (Overlap(center, halfsize, polygon)) return true;
            return false;
        }

        public float MoveVertical(Vector2 foot,float distance,Vector2 halfsize)
        {
            if(Mathf.Abs(distance)<.0000001f)return 0;
            halfsize=new Vector2(Mathf.Max(.0005f,halfsize.x),Mathf.Max(.0005f,halfsize.y));
            Vector2 center=foot+Vector2.up*halfsize.y;
            float first=1;
            foreach(var polygon in polygons)
                if(Sweep(center,Vector2.up*distance,halfsize,polygon,out float time,out _) && time<first)first=time;
            return distance*(first>=1?1:Mathf.Max(0,first-Mathf.Min(contactSkin,.0015f)/Mathf.Abs(distance)));
        }

        public bool TryGetSupportHeight(float x,float minimum,float maximum,out float height)
        {
            height=float.NegativeInfinity;verticalIntervals.Clear();
            foreach(var polygon in polygons)
            {
                if(x<polygon.bounds.xMin || x>polygon.bounds.xMax)continue;
                float low=float.PositiveInfinity,high=float.NegativeInfinity;
                for(int i=0;i<polygon.vertices.Length;i++)
                {
                    var a=polygon.vertices[i];var b=polygon.vertices[(i+1)%polygon.vertices.Length];
                    if(x<Mathf.Min(a.x,b.x) || x>Mathf.Max(a.x,b.x))continue;
                    if(Mathf.Abs(a.x-b.x)<.000001f){low=Mathf.Min(low,Mathf.Min(a.y,b.y));high=Mathf.Max(high,Mathf.Max(a.y,b.y));}
                    else{float y=Mathf.Lerp(a.y,b.y,(x-a.x)/(b.x-a.x));low=Mathf.Min(low,y);high=Mathf.Max(high,y);}
                }
                if(low<=high)verticalIntervals.Add(new Vector2(low,high));
            }
            // The alpha raster is stored as many rectangles. Merge their occupied
            // intervals before considering a top, so an internal row seam is not a floor.
            verticalIntervals.Sort((a,b)=>a.x.CompareTo(b.x));
            float top=float.NegativeInfinity;
            foreach(var interval in verticalIntervals)
            {
                if(interval.x>top+.0001f)
                {
                    if(top>=minimum && top<=maximum)height=Mathf.Max(height,top);
                    top=interval.y;
                }
                else top=Mathf.Max(top,interval.y);
            }
            if(top>=minimum && top<=maximum)height=Mathf.Max(height,top);
            return !float.IsNegativeInfinity(height);
        }

        public bool IsGrounded(Vector2 foot, Vector2 halfsize, float distance = .04f)
        {
            Vector2 result = Move(foot, Vector2.down * distance, halfsize);
            return result.y > foot.y - distance + contactSkin * .5f;
        }

        public Vector2 FindNearestFreeFoot(Vector2 foot, Vector2 halfsize, Rect limits)
        {
            foot = Clamp(foot);
            if (!Overlaps(foot, halfsize)) return foot;
            for (int ring = 1; ring <= 80; ring++)
            {
                float radius = ring * .05f;
                for (int sample = 0; sample < 32; sample++)
                {
                    float angle = sample * Mathf.PI * 2 / 32;
                    Vector2 candidate = Clamp(foot + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                    if (!Overlaps(candidate, halfsize)) return candidate;
                }
            }
            return foot;
            Vector2 Clamp(Vector2 point) => new Vector2(Mathf.Clamp(point.x, limits.xMin, limits.xMax), Mathf.Clamp(point.y, limits.yMin, limits.yMax));
        }

        bool Sweep(Vector2 center, Vector2 delta, Vector2 halfsize, Polygon polygon, out float time, out Vector2 normal)
        {
            time = 0; normal = Vector2.zero;
            Vector2 end = center + delta;
            if (Mathf.Max(center.x, end.x) + halfsize.x < polygon.bounds.xMin || Mathf.Min(center.x, end.x) - halfsize.x > polygon.bounds.xMax ||
                Mathf.Max(center.y, end.y) + halfsize.y < polygon.bounds.yMin || Mathf.Min(center.y, end.y) - halfsize.y > polygon.bounds.yMax) return false;
            float enter = float.NegativeInfinity, exit = float.PositiveInfinity;
            Vector2 entryNormal = Vector2.zero;
            for (int i = -2; i < polygon.vertices.Length; i++)
            {
                Vector2 axis = Axis(polygon, i);
                if (axis.sqrMagnitude < .5f) continue;
                Range(polygon, axis, halfsize, out float min, out float max);
                float start = Vector2.Dot(center, axis), movement = Vector2.Dot(delta, axis);
                if (Mathf.Abs(movement) < .0000001f) { if (start <= min || start >= max) return false; continue; }
                float a = (min - start) / movement, b = (max - start) / movement;
                Vector2 n = movement > 0 ? -axis : axis;
                if (a > b) { float swap = a; a = b; b = swap; }
                if (a > enter) { enter = a; entryNormal = n; }
                exit = Mathf.Min(exit, b);
                if (enter > exit) return false;
            }
            // Existing overlaps must be resolved at entry with FindNearestFreeFoot;
            // allowing an already-overlapping body to leave avoids trapping it.
            if (enter < -.00001f || enter > 1 || exit <= 0) return false;
            time = Mathf.Max(0, enter); normal = entryNormal; return true;
        }

        static bool Overlap(Vector2 center, Vector2 halfsize, Polygon polygon)
        {
            if (center.x + halfsize.x <= polygon.bounds.xMin || center.x - halfsize.x >= polygon.bounds.xMax ||
                center.y + halfsize.y <= polygon.bounds.yMin || center.y - halfsize.y >= polygon.bounds.yMax) return false;
            for (int i = -2; i < polygon.vertices.Length; i++)
            {
                Vector2 axis = Axis(polygon, i);
                if (axis.sqrMagnitude < .5f) continue;
                Range(polygon, axis, halfsize, out float min, out float max);
                float point = Vector2.Dot(center, axis);
                if (point <= min || point >= max) return false;
            }
            return true;
        }

        static Vector2 Axis(Polygon polygon, int index)
        {
            if (index == -2) return Vector2.right;
            if (index == -1) return Vector2.up;
            Vector2 edge = polygon.vertices[(index + 1) % polygon.vertices.Length] - polygon.vertices[index];
            return new Vector2(-edge.y, edge.x).normalized;
        }

        static void Range(Polygon polygon, Vector2 axis, Vector2 halfsize, out float min, out float max)
        {
            min = max = Vector2.Dot(polygon.vertices[0], axis);
            for (int i = 1; i < polygon.vertices.Length; i++)
            { float value = Vector2.Dot(polygon.vertices[i], axis); min = Mathf.Min(min, value); max = Mathf.Max(max, value); }
            float radius = Mathf.Abs(axis.x) * halfsize.x + Mathf.Abs(axis.y) * halfsize.y;
            min -= radius; max += radius;
        }

        void OnDrawGizmosSelected()
        {
            if (!drawCollisionOutlines || projector == null || projector.screen == null) return;
            Gizmos.color = new Color(.2f, .9f, 1f, .65f);
            Gizmos.matrix = projector.screen.localToWorldMatrix;
            foreach (var body in bodies)
                if (body.collider != null)
                    for (int path = 0; path < body.collider.pathCount; path++)
                    {
                        var points = body.collider.GetPath(path);
                        for (int i = 0; i < points.Length; i++) Gizmos.DrawLine(points[i], points[(i + 1) % points.Length]);
                    }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
