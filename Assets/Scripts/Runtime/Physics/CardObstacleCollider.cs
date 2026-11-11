using UnityEngine;
namespace Palworks.PhysicsSystem
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class CardObstacleCollider : MonoBehaviour
    {
        public SpriteRenderer visual;
        public BoxCollider body;
        void LateUpdate(){Refresh();}
        public void Refresh()
        {
            if(body==null)return;
            if(visual==null){body.enabled=false;return;}
            body.enabled=visual.enabled && visual.gameObject.activeInHierarchy;
            if(!body.enabled || visual.sprite==null)return;
            var foot=visual.transform.TransformPoint(new Vector3(visual.sprite.bounds.center.x,visual.sprite.bounds.min.y,0));
            var center=body.transform.TransformPoint(body.center);
            center.x=foot.x;center.z=foot.z;
            body.center=body.transform.InverseTransformPoint(center);
        }
    }
}

