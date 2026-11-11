using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Palworks.Rendering
{
    /// <summary>
    /// Depth-based focus anchored to the playable stage as the camera moves.
    /// Runs before transparent puppets so their texture edges remain sharp.
    /// </summary>
    public sealed class PaperDepthOfFieldFeature : ScriptableRendererFeature
    {
        [Serializable]
        public sealed class Settings
        {
            public bool enabled = false;
            [Tooltip("Assign PaperDepthOfField.shader so it is retained in player builds.")]
            public Shader shader;
            [Min(.01f), Tooltip("Reject blur samples separated by more than this view-space depth, reducing colour bleeding between paper layers.")]
            public float depthTolerance = 1.2f;
            [HideInInspector] public bool visualizeFocus;
        }

        public Settings settings = new Settings();
        Material material;
        PaperFocusPass pass;

        public override void Create()
        {
            pass?.Dispose();
            CoreUtils.Destroy(material);
            var selectedShader = settings.shader != null
                ? settings.shader : Shader.Find("Hidden/Palworks/PaperDepthOfField");
            material = selectedShader != null ? CoreUtils.CreateEngineMaterial(selectedShader) : null;
            pass = new PaperFocusPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!settings.enabled) return;
            var cameraData = renderingData.cameraData;
            if (cameraData.cameraType == CameraType.Preview || cameraData.cameraType == CameraType.Reflection) return;
            if (cameraData.isSceneViewCamera) return;
            if (cameraData.renderType == CameraRenderType.Overlay) return;
            var stageFocus = cameraData.camera.GetComponent<StageDepthOfField>();
            if (stageFocus == null || !stageFocus.isActiveAndEnabled) return;
            if (material == null || (settings.shader != null && material.shader != settings.shader)) Create();
            if (material == null) return;
            pass.Setup(material, settings, stageFocus);
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            pass?.Dispose();
            CoreUtils.Destroy(material);
        }

        sealed class PaperFocusPass : ScriptableRenderPass
        {
            static readonly int FocusParams = Shader.PropertyToID("_PaperFocusParams");
            static readonly int BlurParams = Shader.PropertyToID("_PaperBlurParams");
            static readonly int PixelParams = Shader.PropertyToID("_PaperPixelParams");
            readonly ProfilingSampler sampler = new ProfilingSampler("Palworks Paper Focus");
            RTHandle temporary;
            Material effect;
            Settings options;
            StageDepthOfField stage;

            public PaperFocusPass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
                // Color also requests an intermediate camera target in URP's
                // Auto mode, avoiding an attempt to sample the backbuffer.
                ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Color);
            }

            public void Setup(Material value, Settings values, StageDepthOfField focus)
            {
                effect = value;
                options = values;
                stage = focus;
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                var descriptor = renderingData.cameraData.cameraTargetDescriptor;
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                descriptor.bindMS = false;
                RenderingUtils.ReAllocateIfNeeded(ref temporary, descriptor, FilterMode.Bilinear,
                    TextureWrapMode.Clamp, name: "_PalworksPaperFocusColor");
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (effect == null || temporary == null) return;
                var descriptor = renderingData.cameraData.cameraTargetDescriptor;
                var camera = renderingData.cameraData.camera;
                effect.SetFloat("_PaperFocusDebug", options.visualizeFocus ? 1 : 0);
                float resolutionScale = descriptor.height / 1080f;
                effect.SetVector(FocusParams, new Vector4(stage.FocusDistance(camera),
                    Mathf.Max(0, stage.nearSharpRange), Mathf.Max(0, stage.farSharpRange),
                    Mathf.Max(.01f, options.depthTolerance)));
                effect.SetVector(BlurParams, new Vector4(Mathf.Max(.01f, stage.nearTransition),
                    Mathf.Max(.01f, stage.farTransition), Mathf.Clamp(stage.foregroundBlur, 0, 8)*resolutionScale,
                    Mathf.Clamp(stage.backgroundBlur, 0, 8)*resolutionScale));
                effect.SetVector(PixelParams, new Vector4(1f / Mathf.Max(1, descriptor.width),
                    1f / Mathf.Max(1, descriptor.height), descriptor.width, descriptor.height));
                var source = renderingData.cameraData.renderer.cameraColorTargetHandle;
                var cmd = CommandBufferPool.Get();
                try
                {
                    using (new ProfilingScope(cmd, sampler))
                    {
                        Blitter.BlitCameraTexture(cmd, source, temporary, effect, 0);
                        Blitter.BlitCameraTexture(cmd, temporary, source);
                        // Blitter binds a colour-only target. Restore the scene
                        // depth attachment before transparent puppets are drawn.
                        CoreUtils.SetRenderTarget(cmd, source,
                            renderingData.cameraData.renderer.cameraDepthTargetHandle);
                    }
                    context.ExecuteCommandBuffer(cmd);
                }
                finally
                {
                    CommandBufferPool.Release(cmd);
                }
            }

            public void Dispose()
            {
                temporary?.Release();
                temporary = null;
            }
        }
    }
}
