// LayerFillFeature: captures a target layer and composites it as a solid color overlay.
// LayerPostFeature の枠組みを簡略化して、指定レイヤの形状を塗りつぶす用途に特化。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class LayerFillFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("General")] public string featureName = "LayerFillFeature";
        public LayerMask layerMask = ~0;
        public Color fillColor = new Color(1f, 1f, 1f, 1f);

        [Header("Events")] public RenderPassEvent captureEvent = RenderPassEvent.AfterRenderingSkybox;
        public RenderPassEvent compositeEvent = RenderPassEvent.AfterRenderingTransparents;

        [Header("Quality")] public bool hdr = true;
        [Range(0.25f, 1f)] public float downsample = 1f;

        [Header("Composite")] public bool useSourceAlpha = true;

        [Header("Debug")] public bool showInSceneView = false;

        [Header("Shaders")] public Shader compositeShader;
    }

    class CapturePass : ScriptableRenderPass
    {
        readonly string profilerTag;
        readonly Settings featureSettings;
        readonly bool renderInSceneView;

        FilteringSettings filtering;
        RenderStateBlock stateBlock = new(RenderStateMask.Nothing);

        readonly List<ShaderTagId> shaderTags = new()
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
            new ShaderTagId("Universal2D"),
            new ShaderTagId("SpriteLit"),
            new ShaderTagId("SpriteUnlit"),
        };

        RTHandle colorRT;

        public CapturePass(string tag, Settings settings)
        {
            profilerTag = tag;
            featureSettings = settings;
            renderInSceneView = settings.showInSceneView;
            filtering = new FilteringSettings(RenderQueueRange.all, settings.layerMask);
        }

        public RTHandle GetColorRT() => colorRT;

        [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (!renderInSceneView && renderingData.cameraData.isSceneViewCamera) return;

            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.msaaSamples = 1;
            desc.depthBufferBits = 0;
            desc.useMipMap = false;
            desc.bindMS = false;
            desc.enableRandomWrite = false;
            desc.graphicsFormat = featureSettings.hdr
                ? SystemInfo.GetGraphicsFormat(DefaultFormat.HDR)
                : SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);

            desc.width = Mathf.Max(1, Mathf.RoundToInt(desc.width * featureSettings.downsample));
            desc.height = Mathf.Max(1, Mathf.RoundToInt(desc.height * featureSettings.downsample));

            int camId = renderingData.cameraData.camera.GetInstanceID();
            string uniqueName = $"{profilerTag}_Color_c{camId}_{desc.width}x{desc.height}";

            RenderingUtils.ReAllocateIfNeeded(
                ref colorRT, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: uniqueName);

            ConfigureTarget(colorRT);
            ConfigureClear(ClearFlag.Color, new Color(0, 0, 0, 0));
        }

        [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!renderInSceneView && renderingData.cameraData.isSceneViewCamera) return;
            if (colorRT == null) return;

            var cmd = CommandBufferPool.Get(profilerTag);
            using (new ProfilingScope(cmd, new ProfilingSampler(profilerTag)))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                var sortFlags = SortingCriteria.SortingLayer | SortingCriteria.RenderQueue |
                                SortingCriteria.CommonOpaque;
                var sorting = new SortingSettings(renderingData.cameraData.camera) { criteria = sortFlags };

                var drawing = new DrawingSettings(shaderTags[0], sorting);
                for (int i = 1; i < shaderTags.Count; i++)
                    drawing.SetShaderPassName(i, shaderTags[i]);

                var filter = filtering;
                context.DrawRenderers(renderingData.cullResults, ref drawing, ref filter, ref stateBlock);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Cleanup()
        {
            /* colorRT の寿命は URP が管理 */
        }
    }

    class CompositePass : ScriptableRenderPass
    {
        readonly string profilerTag;
        readonly Settings featureSettings;
        readonly bool renderInSceneView;
        Material compositeMat;
        RTHandle srcRT;
        System.Func<RTHandle> getSrcRT;
        RTHandle cameraColor;

        static readonly int _YFlip = Shader.PropertyToID("_YFlip");
        static readonly int _UseSrcAlpha = Shader.PropertyToID("_UseSrcAlpha");
        static readonly int _FillColor = Shader.PropertyToID("_FillColor");

        public CompositePass(string tag, Shader shader, Settings settings)
        {
            profilerTag = tag;
            featureSettings = settings;
            renderInSceneView = settings.showInSceneView;

            if (shader == null) shader = Shader.Find("Hidden/LayerColorComposite");
            if (shader != null) compositeMat = CoreUtils.CreateEngineMaterial(shader);
        }

        public void Setup(System.Func<RTHandle> srcProvider)
        {
            getSrcRT = srcProvider;
        }

        [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (!renderInSceneView && renderingData.cameraData.isSceneViewCamera) return;
            cameraColor = renderingData.cameraData.renderer.cameraColorTargetHandle;
            srcRT = getSrcRT != null ? getSrcRT() : null;
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!renderInSceneView && renderingData.cameraData.isSceneViewCamera) return;
            if (compositeMat == null || cameraColor == null || srcRT == null) return;

            compositeMat.SetFloat(_UseSrcAlpha, featureSettings.useSourceAlpha ? 1f : 0f);
            compositeMat.SetColor(_FillColor, featureSettings.fillColor);
            compositeMat.SetFloat(_YFlip, 1f);

            var cmd = CommandBufferPool.Get(profilerTag);
            using (new ProfilingScope(cmd, new ProfilingSampler(profilerTag)))
            {
                CoreUtils.SetRenderTarget(
                    cmd,
                    cameraColor,
                    RenderBufferLoadAction.Load,
                    RenderBufferStoreAction.Store
                );

                Blitter.BlitCameraTexture(cmd, srcRT, cameraColor, compositeMat, 0);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Cleanup()
        {
            CoreUtils.Destroy(compositeMat);
            compositeMat = null;
        }
    }

    public Settings settings = new Settings();

    CapturePass capturePass;
    CompositePass compositePass;

    public override void Create()
    {
        compositePass?.Cleanup();
        capturePass?.Cleanup();

        capturePass = new CapturePass(
            $"{settings.featureName}_Capture",
            settings
        );
        capturePass.renderPassEvent = settings.captureEvent;

        compositePass = new CompositePass(
            $"{settings.featureName}_Composite",
            settings.compositeShader,
            settings
        );

        // Ensure compositeEvent is after LayerPostFeature's event
        var compositeEvent = settings.compositeEvent;
        if ((int)compositeEvent <= (int)RenderPassEvent.BeforeRenderingPostProcessing)
            compositeEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingPostProcessing + 1);
        compositePass.renderPassEvent = compositeEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!settings.showInSceneView && renderingData.cameraData.isSceneViewCamera)
            return;

        System.Func<RTHandle> provider = () => capturePass.GetColorRT();
        compositePass.Setup(provider);

        renderer.EnqueuePass(capturePass);
        renderer.EnqueuePass(compositePass);
    }

    protected override void Dispose(bool disposing)
    {
        compositePass?.Cleanup();
        capturePass?.Cleanup();
    }
}
