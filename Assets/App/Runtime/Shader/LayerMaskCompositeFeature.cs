// LayerMaskCompositeFeature: capture a fill layer as mask and composite another layer through it.
// Inspired by LayerFillFeature, but composites masked content instead of a flat color.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class LayerMaskCompositeFeature : ScriptableRendererFeature
{
    [Serializable]
    public class Settings
    {
        [Header("General")] public string featureName = "LayerMaskCompositeFeature";
        public LayerMask maskLayer = ~0;
        public LayerMask contentLayer = ~0;

        [Header("Events")] public RenderPassEvent maskCaptureEvent = RenderPassEvent.AfterRenderingSkybox;
        public RenderPassEvent contentCaptureEvent = RenderPassEvent.AfterRenderingSkybox;
        public RenderPassEvent compositeEvent = RenderPassEvent.AfterRenderingTransparents;

        [Header("Quality")] public bool hdr = true;
        [Range(0.25f, 1f)] public float downsample = 1f;

        [Header("Mask Composite")] public bool invertMask = false;
        [Range(0f, 1f)] public float maskThreshold = 0f;
        public bool applyToColor = true;
        public bool applyToAlpha = true;
        public Color contentColor;

        [Header("Debug")] public bool showInSceneView = false;

        [Header("Shaders")] public Shader compositeShader;
    }

    class CapturePass : ScriptableRenderPass
    {
        readonly string profilerTag;
        readonly bool renderInSceneView;
        readonly bool hdr;
        readonly float downsample;
        readonly LayerMask layerMask;

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

        public CapturePass(string tag, LayerMask mask, Settings settings)
        {
            profilerTag = tag;
            layerMask = mask;
            renderInSceneView = settings.showInSceneView;
            hdr = settings.hdr;
            downsample = settings.downsample;
            filtering = new FilteringSettings(RenderQueueRange.all, layerMask);
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
            desc.graphicsFormat = hdr
                ? SystemInfo.GetGraphicsFormat(DefaultFormat.HDR)
                : SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);

            desc.width = Mathf.Max(1, Mathf.RoundToInt(desc.width * downsample));
            desc.height = Mathf.Max(1, Mathf.RoundToInt(desc.height * downsample));

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
        System.Func<RTHandle> getMaskRT;
        System.Func<RTHandle> getContentRT;
        RTHandle maskRT;
        RTHandle contentRT;
        RTHandle cameraColor;

        static readonly int _MaskTex = Shader.PropertyToID("_MaskTex");
        static readonly int _YFlip = Shader.PropertyToID("_YFlip");
        static readonly int _MaskThreshold = Shader.PropertyToID("_MaskThreshold");
        static readonly int _InvertMask = Shader.PropertyToID("_InvertMask");
        static readonly int _ApplyColor = Shader.PropertyToID("_ApplyToColor");
        static readonly int _ApplyAlpha = Shader.PropertyToID("_ApplyToAlpha");
        static readonly int _ContentColor = Shader.PropertyToID("_ContentColor");

        public CompositePass(string tag, Shader shader, Settings settings)
        {
            profilerTag = tag;
            featureSettings = settings;
            renderInSceneView = settings.showInSceneView;

            if (shader == null) shader = Shader.Find("Hidden/LayerMaskComposite");
            if (shader != null) compositeMat = CoreUtils.CreateEngineMaterial(shader);
        }

        public void Setup(System.Func<RTHandle> maskProvider, System.Func<RTHandle> contentProvider)
        {
            getMaskRT = maskProvider;
            getContentRT = contentProvider;
        }

        [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (!renderInSceneView && renderingData.cameraData.isSceneViewCamera) return;
            cameraColor = renderingData.cameraData.renderer.cameraColorTargetHandle;
            maskRT = getMaskRT != null ? getMaskRT() : null;
            contentRT = getContentRT != null ? getContentRT() : null;
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!renderInSceneView && renderingData.cameraData.isSceneViewCamera) return;
            if (compositeMat == null || cameraColor == null || contentRT == null || maskRT == null) return;

            compositeMat.SetTexture(_MaskTex, maskRT);
            compositeMat.SetFloat(_YFlip, 1f);
            compositeMat.SetFloat(_MaskThreshold, featureSettings.maskThreshold);
            compositeMat.SetFloat(_InvertMask, featureSettings.invertMask ? 1f : 0f);
            compositeMat.SetFloat(_ApplyColor, featureSettings.applyToColor ? 1f : 0f);
            compositeMat.SetFloat(_ApplyAlpha, featureSettings.applyToAlpha ? 1f : 0f);
            compositeMat.SetColor(_ContentColor, featureSettings.contentColor);

            var cmd = CommandBufferPool.Get(profilerTag);
            using (new ProfilingScope(cmd, new ProfilingSampler(profilerTag)))
            {
                CoreUtils.SetRenderTarget(
                    cmd,
                    cameraColor,
                    RenderBufferLoadAction.Load,
                    RenderBufferStoreAction.Store
                );

                Blitter.BlitCameraTexture(cmd, contentRT, cameraColor, compositeMat, 0);
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

    CapturePass maskPass;
    CapturePass contentPass;
    CompositePass compositePass;

    public override void Create()
    {
        compositePass?.Cleanup();
        maskPass?.Cleanup();
        contentPass?.Cleanup();

        maskPass = new CapturePass(
            $"{settings.featureName}_MaskCapture",
            settings.maskLayer,
            settings
        );
        maskPass.renderPassEvent = settings.maskCaptureEvent;

        contentPass = new CapturePass(
            $"{settings.featureName}_ContentCapture",
            settings.contentLayer,
            settings
        );
        var contentEvent = (RenderPassEvent)Mathf.Max((int)settings.contentCaptureEvent, (int)maskPass.renderPassEvent + 1);
        contentPass.renderPassEvent = contentEvent;

        compositePass = new CompositePass(
            $"{settings.featureName}_Composite",
            settings.compositeShader,
            settings
        );
        var compositeEvent = (RenderPassEvent)Mathf.Max((int)settings.compositeEvent, (int)contentEvent + 1);
        compositePass.renderPassEvent = compositeEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!settings.showInSceneView && renderingData.cameraData.isSceneViewCamera)
            return;

        System.Func<RTHandle> maskProvider = () => maskPass.GetColorRT();
        System.Func<RTHandle> contentProvider = () => contentPass.GetColorRT();
        compositePass.Setup(maskProvider, contentProvider);

        renderer.EnqueuePass(maskPass);
        renderer.EnqueuePass(contentPass);
        renderer.EnqueuePass(compositePass);
    }

    protected override void Dispose(bool disposing)
    {
        compositePass?.Cleanup();
        maskPass?.Cleanup();
        contentPass?.Cleanup();
    }
}
