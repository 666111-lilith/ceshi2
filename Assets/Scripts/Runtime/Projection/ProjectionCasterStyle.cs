using UnityEngine;

namespace Palworks.Projection
{
    // Explicit authoring separates translucent scenery from collidable silhouettes.
    [DisallowMultipleComponent]
    public sealed class ProjectionCasterStyle : MonoBehaviour
    {
        public enum DepthLayer { Background, Middle, Solid }
        public DepthLayer depthLayer = DepthLayer.Solid;
        public bool solid = true;
        [Range(0,1)] public float opacity = 1;
        [Range(.25f,6)] public float softnessScale = 1;
    }
}
