using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Palworks.Projection;

namespace Palworks.Editor
{
    public static class ProjectionStationFactory
    {
        const string Root="Assets/Art/Stage/Projection";
        const float Width=12, Height=5.6f, Bottom=.05f, ScreenZ=3.8f, ProjectorZ=-6.3f;

        public static GameObject Create(Transform parent,int index,float x)
        {
            Directory.CreateDirectory("Assets/Art/Stage/Projection/Materials");
            Directory.CreateDirectory("Assets/Art/Stage/Projection/Meshes");
            Directory.CreateDirectory("Assets/Settings/Rendering/RenderTextures");
            AssetDatabase.Refresh();
            var station=new GameObject("Projection"+index);
            station.transform.SetParent(parent,false);
            station.transform.position=new Vector3(x,0,0);
            var frameMaterial=Solid("Frame",.34f);
            var bodyMaterial=Solid("ProjectorBody",.72f);
            var trimMaterial=Solid("ProjectorTrim",.42f);
            var lensMaterial=Solid("ProjectorLens",.95f);
            var curtainGroup=Group("Curtain",station.transform,new Vector3(0,0,ScreenZ));
            var screen=Card("Cloth",curtainGroup,ClothMesh(),ClothMaterial(index),
                new Vector3(0,Bottom+Height*.5f,0),Quaternion.identity);
            Card("TopRail",curtainGroup,Quad("TopRail",Width+.42f,.12f),frameMaterial,
                new Vector3(0,Bottom+Height+.07f,-.035f),Quaternion.identity);
            Card("BottomRail",curtainGroup,Quad("BottomRail",Width+.16f,.09f),frameMaterial,
                new Vector3(0,Bottom-.005f,-.04f),Quaternion.identity);
            foreach(int direction in new[]{-1,1})
            {
                string suffix=direction<0?"Left":"Right";
                var post=Card(suffix+"Post",curtainGroup,Quad("Post",.14f,Height+.25f),frameMaterial,
                    new Vector3(direction*(Width*.5f+.12f),(Height+.25f)*.5f,0),Quaternion.identity);
                var postCollider=post.AddComponent<BoxCollider>();
                postCollider.size=new Vector3(.3f,Height+.25f,.3f);
                Card(suffix+"Foot",curtainGroup,Quad("FrameFoot",.6f,.10f),frameMaterial,
                    new Vector3(direction*(Width*.5f+.12f),.05f,-.07f),Quaternion.identity);
            }
            var screenCollider=screen.AddComponent<BoxCollider>();
            screenCollider.size=new Vector3(Width,Height,.16f);

            var projectorGroup=Group("Projector",station.transform,new Vector3(0,0,ProjectorZ));
            var paperRotation=Quaternion.Euler(16,0,0);
            Card("Body",projectorGroup,Quad("Body",.86f,.62f),bodyMaterial,new Vector3(0,.53f,0),paperRotation);
            Card("Top",projectorGroup,Quad("Top",.66f,.10f),trimMaterial,new Vector3(0,.88f,.03f),paperRotation);
            Card("LensRing",projectorGroup,Disc("LensRing",.21f),trimMaterial,new Vector3(0,.35f,-.08f),paperRotation);
            Card("LensGlass",projectorGroup,Disc("LensGlass",.145f),lensMaterial,new Vector3(0,.35f,-.10f),paperRotation);
            Card("Vent",projectorGroup,Quad("Vent",.18f,.06f),trimMaterial,new Vector3(.26f,.65f,-.075f),paperRotation);
            foreach(int direction in new[]{-1,1})
                Card(direction<0?"LeftLeg":"RightLeg",projectorGroup,Quad("Leg",.10f,.32f),trimMaterial,
                    new Vector3(direction*.30f,.17f,-.01f),Quaternion.Euler(16,0,direction*-12));
            var bodyCollider=projectorGroup.gameObject.AddComponent<BoxCollider>();
            bodyCollider.center=new Vector3(0,.4f,0);
            bodyCollider.size=new Vector3(1.05f,.8f,.65f);
            var lens=Group("Lens",projectorGroup,new Vector3(0,.35f,.05f));
            var cameraObject=new GameObject("ProjectionCamera");
            cameraObject.transform.SetParent(projectorGroup,false);
            var camera=cameraObject.AddComponent<Camera>();
            var cameraData=camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing=false;
            cameraData.renderShadows=false;
            cameraData.antialiasing=AntialiasingMode.None;
            cameraData.requiresColorTexture=false;
            cameraData.requiresDepthTexture=false;
            var projection=projectorGroup.gameObject.AddComponent<PaperProjector>();
            projection.lens=lens;
            projection.screen=screen.transform;
            projection.screenRenderer=screen.GetComponent<Renderer>();
            projection.projectionCamera=camera;
            projection.shadowTexture=Target(index);
            projection.screenSize=new Vector2(Width,Height);
            projection.casterLayers=1<<8;
            projection.RefreshProjection();
            projection.screenRenderer.sharedMaterial.SetTexture("_ShadowMap",projection.shadowTexture);
            EditorUtility.SetDirty(projection.screenRenderer.sharedMaterial);
            return station;
        }

        static Transform Group(string name,Transform parent,Vector3 position)
        {
            var group=new GameObject(name).transform;
            group.SetParent(parent,false); group.localPosition=position;
            return group;
        }

        static GameObject Card(string name,Transform parent,Mesh mesh,Material material,Vector3 position,Quaternion rotation)
        {
            var go=new GameObject(name);
            go.transform.SetParent(parent,false);
            go.transform.localPosition=position; go.transform.localRotation=rotation;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>(); renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;
            return go;
        }

        static Material Solid(string name,float gray)
        {
            string path="Assets/Art/Stage/Projection/Materials/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material!=null) return material;
            var shader=Shader.Find("Palworks/PaperBlockout");
            if(shader==null) throw new InvalidOperationException("PaperBlockout shader is required.");
            material=new Material(shader) {name=name};
            material.SetTexture("_MainTex",Texture2D.whiteTexture);
            material.SetColor("_BaseColor",new Color(gray,gray,gray,1));
            material.SetColor("_Color",Color.white); material.SetColor("_RendererColor",Color.white);
            AssetDatabase.CreateAsset(material,path);
            return material;
        }

        static Material ClothMaterial(int index)
        {
            string path="Assets/Art/Stage/Projection/Materials/Cloth"+index+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material!=null) return material;
            var shader=Shader.Find("Palworks/ProjectionCloth");
            if(shader==null) throw new InvalidOperationException("ProjectionCloth shader must finish importing.");
            material=new Material(shader) {name="Cloth"+index};
            AssetDatabase.CreateAsset(material,path); return material;
        }

        static RenderTexture Target(int index)
        {
            string path="Assets/Settings/Rendering/RenderTextures/Projection"+index+".renderTexture";
            var target=AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
            if(target!=null) return target;
            target=new RenderTexture(1280,600,24,RenderTextureFormat.ARGB32)
            {
                name="Projection"+index, antiAliasing=1,
                filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,
                useMipMap=false, autoGenerateMips=false
            };
            AssetDatabase.CreateAsset(target,path); return target;
        }

        static Mesh Quad(string name,float width,float height)
        {
            string path="Assets/Art/Stage/Projection/Meshes/"+name+".asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(mesh!=null) return mesh;
            mesh=new Mesh {name=name};
            mesh.vertices=new[]{new Vector3(-width*.5f,-height*.5f,0),new Vector3(width*.5f,-height*.5f,0),
                new Vector3(width*.5f,height*.5f,0),new Vector3(-width*.5f,height*.5f,0)};
            mesh.uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)};
            mesh.triangles=new[]{0,2,1,0,3,2}; WhiteVertices(mesh); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,path); return mesh;
        }

        static Mesh Disc(string name,float radius)
        {
            string path="Assets/Art/Stage/Projection/Meshes/"+name+".asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(mesh!=null) return mesh;
            const int count=32;
            var vertices=new Vector3[count+1]; var uv=new Vector2[count+1]; var triangles=new int[count*3];
            uv[0]=new Vector2(.5f,.5f);
            for(int i=0;i<count;i++)
            {
                float angle=i*Mathf.PI*2/count;
                vertices[i+1]=new Vector3(Mathf.Cos(angle)*radius,Mathf.Sin(angle)*radius,0);
                uv[i+1]=new Vector2(.5f+Mathf.Cos(angle)*.5f,.5f+Mathf.Sin(angle)*.5f);
                triangles[i*3]=0; triangles[i*3+1]=(i+1)%count+1; triangles[i*3+2]=i+1;
            }
            mesh=new Mesh {name=name,vertices=vertices,uv=uv,triangles=triangles};
            WhiteVertices(mesh); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,path); return mesh;
        }

        static void WhiteVertices(Mesh mesh)
        {
            var colors=new Color[mesh.vertexCount];
            for(int i=0;i<colors.Length;i++) colors[i]=Color.white;
            mesh.colors=colors;
        }

        static Mesh ClothMesh()
        {
            string path="Assets/Art/Stage/Projection/Meshes/Cloth.asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(mesh!=null) return mesh;
            const int columns=32,rows=12;
            var vertices=new Vector3[(columns+1)*(rows+1)];
            var uv=new Vector2[vertices.Length]; var triangles=new int[columns*rows*6];
            for(int y=0;y<=rows;y++) for(int x=0;x<=columns;x++)
            {
                float u=(float)x/columns,v=(float)y/rows;
                float edgePull=.055f*Mathf.Sin(v*Mathf.PI)*Mathf.Sin(v*14);
                float sag=-.065f*Mathf.Sin(u*Mathf.PI)*Mathf.Cos(v*Mathf.PI);
                vertices[y*(columns+1)+x]=new Vector3((u-.5f)*Width+edgePull,(v-.5f)*Height+sag,0);
                uv[y*(columns+1)+x]=new Vector2(u,v);
                if(x==columns || y==rows) continue;
                int i=y*(columns+1)+x,t=(y*columns+x)*6;
                triangles[t]=i; triangles[t+1]=i+columns+2; triangles[t+2]=i+1;
                triangles[t+3]=i; triangles[t+4]=i+columns+1; triangles[t+5]=i+columns+2;
            }
            mesh=new Mesh {name="Cloth",vertices=vertices,uv=uv,triangles=triangles};
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,path); return mesh;
        }
    }
}
