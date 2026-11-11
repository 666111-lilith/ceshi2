using System;
using UnityEngine;

namespace Palworks.Character
{
    [Serializable] public class PuppetAnimationData
    {
        public int version;
        public float unitsPerPixel = 0.0075f;
        public float sourceGroundY;
        public float framesPerSecond = 24;
        public PuppetPart[] parts;
        public PuppetBitmap[] bitmaps;
        public PuppetClip[] clips;
    }
    [Serializable] public class PuppetPart { public string name; }
    [Serializable] public class PuppetBitmap { public string file, source; public int symbolId; }
    [Serializable] public class PuppetClip { public string name, source; public PuppetFrame[] frames; }
    [Serializable] public class PuppetFrame { public PuppetDraw[] parts; }
    [Serializable] public class PuppetDraw
    {
        public int part, bitmap;
        public float[] corners;
        public float alpha;
    }
}
