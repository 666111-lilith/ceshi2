using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Palworks.Character;

namespace Palworks.Performance
{
    // Only temporary material instances fade. The lamp's alpha mask ignores
    // TransitionOpacity, so the same shadows remain on the cloth throughout.
    public sealed class WorldTransitionFade
    {
        readonly Dictionary<Material,Material> copies=new Dictionary<Material,Material>();
        readonly Dictionary<Renderer,Material[]> originals=new Dictionary<Renderer,Material[]>();
        PuppetAnimator puppet;
        Material[] bitmapMaterials;
        public bool Active { get; private set; }

        public void Begin(Camera view,PerformanceStation station,PuppetPlayerController player)
        {
            Restore();
            foreach(var renderer in Object.FindObjectsOfType<Renderer>(true))
            {
                int layer=renderer.gameObject.layer;
                if(layer==9 || layer==10 || (view.cullingMask&(1<<layer))==0) continue;
                if(!renderer.gameObject.scene.IsValid()) continue;
                var sources=renderer.sharedMaterials;
                var materials=new Material[sources.Length];
                bool changed=false;
                for(int i=0;i<sources.Length;i++)
                {
                    materials[i]=Copy(sources[i]);
                    changed|=materials[i]!=sources[i];
                }
                if(!changed) continue;
                originals.Add(renderer,sources); renderer.sharedMaterials=materials;
            }
            puppet=player!=null?player.puppet:null;
            if(puppet!=null)
            {
                bitmapMaterials=puppet.bitmapMaterials;
                var materials=new Material[bitmapMaterials.Length];
                for(int i=0;i<materials.Length;i++) materials[i]=Copy(bitmapMaterials[i]);
                puppet.bitmapMaterials=materials;
            }
            Active=true; SetAmount(0);
        }

        Material Copy(Material source)
        {
            if(source==null || !source.HasProperty("_TransitionOpacity")) return source;
            if(copies.TryGetValue(source,out var copy)) return copy;
            copy=new Material(source){name=source.name,hideFlags=HideFlags.HideAndDontSave};
            copy.SetFloat("_TransitionOpacity",1);
            // Keep cutout depth writes: the large floor card must not repaint
            // over nearer props when their queue changes for the dissolve.
            copy.renderQueue=Mathf.Max((int)RenderQueue.Transparent,source.renderQueue);
            copies.Add(source,copy);
            return copy;
        }

        public void SetAmount(float amount)
        {
            float opacity=1-Mathf.Clamp01(amount);
            foreach(var material in copies.Values) material.SetFloat("_TransitionOpacity",opacity);
        }

        public void Restore()
        {
            foreach(var item in originals) if(item.Key!=null) item.Key.sharedMaterials=item.Value;
            originals.Clear();
            if(puppet!=null && bitmapMaterials!=null) puppet.bitmapMaterials=bitmapMaterials;
            puppet=null;bitmapMaterials=null;
            foreach(var material in copies.Values)
                if(Application.isPlaying) Object.Destroy(material); else Object.DestroyImmediate(material);
            copies.Clear();Active=false;
        }
    }
}
