using UnityEngine;
namespace Palworks.Projection
{
    [ExecuteAlways,DefaultExecutionOrder(1100)]
    public sealed class ClothKeyProjection : MonoBehaviour
    {
        public PaperProjector projector;
        public SpriteRenderer source;
        public Color pigment=new Color(1f,.66f,.11f,1);
        public float floorInset=.16f;
        MaterialPropertyBlock block;
        void LateUpdate(){Refresh();}
        void OnDisable(){Clear();}
        void Clear(){if(projector==null||projector.screenRenderer==null)return;if(block==null)block=new MaterialPropertyBlock();projector.screenRenderer.GetPropertyBlock(block);block.SetFloat("_KeyVisible",0);projector.screenRenderer.SetPropertyBlock(block);}
        public void Refresh()
        {
            if(projector==null || projector.screenRenderer==null || source==null || source.sprite==null){Clear();return;}
            source.enabled=false;
            if(block==null)block=new MaterialPropertyBlock();
            projector.screenRenderer.GetPropertyBlock(block);
            var screen=projector.screen;var bounds=source.sprite.bounds;
            Vector2 min=Vector2.one*1000,max=Vector2.one*-1000;
            foreach(float x in new[]{bounds.min.x,bounds.max.x}) foreach(float y in new[]{bounds.min.y,bounds.max.y})
            {
                Vector3 local=screen.InverseTransformPoint(source.transform.TransformPoint(new Vector3(x,y,0)));
                Vector2 uv=new Vector2(local.x/projector.screenSize.x+.5f,local.y/projector.screenSize.y+.5f);
                min=Vector2.Min(min,uv);max=Vector2.Max(max,uv);
            }
            var mapping=source.transform.worldToLocalMatrix*screen.localToWorldMatrix;
            var origin=mapping.MultiplyPoint3x4(new Vector3(-projector.screenSize.x*.5f,-projector.screenSize.y*.5f,0));
            var dx=mapping.MultiplyVector(Vector3.right*projector.screenSize.x);var dy=mapping.MultiplyVector(Vector3.up*projector.screenSize.y);
            var rowX=new Vector4(dx.x/bounds.size.x,dy.x/bounds.size.x,(origin.x-bounds.min.x)/bounds.size.x,0);
            var rowY=new Vector4(dx.y/bounds.size.y,dy.y/bounds.size.y,(origin.y-bounds.min.y)/bounds.size.y,0);
            if(source.flipX){rowX=-rowX;rowX.z+=1;}if(source.flipY){rowY=-rowY;rowY.z+=1;}
            block.SetVector("_KeyUVX",rowX);block.SetVector("_KeyUVY",rowY);
            block.SetTexture("_KeyShape",source.sprite.texture);
            block.SetVector("_KeyRect",new Vector4(min.x,min.y,max.x-min.x,max.y-min.y));
            block.SetColor("_KeyPigment",pigment);
            block.SetFloat("_KeyVisible",source.gameObject.activeInHierarchy?1:0);
            block.SetFloat("_GroundLine",floorInset/(projector.screenSize.y*Mathf.Abs(screen.lossyScale.y)));
            projector.screenRenderer.SetPropertyBlock(block);
        }
    }
}
