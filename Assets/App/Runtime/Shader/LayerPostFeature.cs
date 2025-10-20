// URP12〜URP17対応: レイヤ別キャプチャ → 任意ポスト → カメラカラーへブレンド合成（Quad不要）
// ポイント：
// - cameraColorTargetHandle は ScriptableRenderPass（OnCameraSetup/Execute）内のみ参照
// - RTHandle名はカメラID＋解像度でユニーク化（RTプール衝突回避）
// - 合成は Material 経由の BlitCameraTexture（Blend が効く）＋ LoadAction=Load で上積み

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class LayerPostFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        [Header("General")] public string featureName = "LayerPostFeature";
        public LayerMask layerMask = ~0;

        [Header("Events")] public RenderPassEvent captureEvent = RenderPassEvent.AfterRenderingSkybox;
        public RenderPassEvent effectEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        public RenderPassEvent compositeEvent = RenderPassEvent.AfterRenderingTransparents;

        [Header("Quality")] public bool hdr = true;
        [Range(0.25f, 1f)] public float downsample = 1f;

        [Header("Capture")] public bool includeCameraColor = false;

        [Header("Shaders")] public Material postMaterial; // 例: DemoTint.mat
        public Shader compositeShader; // 空なら Hidden/LayerComposite を自動使用

        [Header("Composite")]
        public bool useSourceAlpha = true;

        [Header("Debug")] public bool showInSceneView = false;
    }

    // ───────────────── Capture（対象レイヤだけRTへ描画） ─────────────────
    class CapturePass : ScriptableRenderPass
    {
        readonly string profilerTag;
        readonly bool hdr;
        readonly float downsample;
        readonly bool includeCameraColor;
        readonly bool renderInSceneView;

        FilteringSettings filtering;
        RenderStateBlock stateBlock = new RenderStateBlock(RenderStateMask.Nothing);

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

        public CapturePass(string tag, LayerMask mask, bool hdr, float downsample, bool includeCameraColor, bool renderInSceneView)
        {
            profilerTag = tag;
            this.hdr = hdr;
            this.downsample = downsample;
            this.includeCameraColor = includeCameraColor;
            this.renderInSceneView = renderInSceneView;
            filtering = new FilteringSettings(RenderQueueRange.all, mask);
        }

        public RTHandle GetColorRT() => colorRT;

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

            // RTHandle版の ConfigureTarget（非Obsolete）
            ConfigureTarget(colorRT);
            ConfigureClear(ClearFlag.Color, new Color(0, 0, 0, 0));
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!renderInSceneView && renderingData.cameraData.isSceneViewCamera) return;
            if (colorRT == null) return;

            var cmd = CommandBufferPool.Get(profilerTag);
            using (new ProfilingScope(cmd, new ProfilingSampler(profilerTag)))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                if (includeCameraColor)
                {
                    var cameraTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
                    if (cameraTarget != null)
                    {
                        Blitter.BlitCameraTexture(cmd, cameraTarget, colorRT);
                        context.ExecuteCommandBuffer(cmd);
                        cmd.Clear();
                    }
                }

                var sortFlags = SortingCriteria.CommonTransparent;
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
            /* colorRTはURP側で管理（手動解放不要）*/
        }
    }

    // ───────────────── Effect（キャプチャRTに任意ポスト） ─────────────────
    class EffectPass : ScriptableRenderPass
    {
        readonly string profilerTag;
        readonly bool renderInSceneView;
        readonly float downsample;
        Material effectMat;
        RTHandle srcRT;
        System.Func<RTHandle> getSrcRT;
        RTHandle tmpRT;

        public EffectPass(string tag, Material material, float downsample, bool renderInSceneView)
        {
            profilerTag = tag;
            this.downsample = downsample;
            this.renderInSceneView = renderInSceneView;
            if (material != null) effectMat = material;
        }

        public void Setup(System.Func<RTHandle> srcProvider)
        {
            getSrcRT = srcProvider;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (!renderInSceneView && renderingData.cameraData.isSceneViewCamera) return;
            // Fetch captured RT after CapturePass allocates it
            srcRT = getSrcRT != null ? getSrcRT() : null;

            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.msaaSamples = 1;
            desc.depthBufferBits = 0;
            desc.useMipMap = false;
            desc.bindMS = false;
            desc.enableRandomWrite = false;
            desc.graphicsFormat = SystemInfo.GetGraphicsFormat(DefaultFormat.HDR);
            desc.width = Mathf.Max(1, Mathf.RoundToInt(desc.width * downsample));
            desc.height = Mathf.Max(1, Mathf.RoundToInt(desc.height * downsample));

            int camId = renderingData.cameraData.camera.GetInstanceID();
            string uniqueName = $"{profilerTag}_Tmp_c{camId}_{desc.width}x{desc.height}";

            RenderingUtils.ReAllocateIfNeeded(
                ref tmpRT, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: uniqueName);

            ConfigureTarget(tmpRT);
            ConfigureClear(ClearFlag.None, Color.clear);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!renderInSceneView && renderingData.cameraData.isSceneViewCamera) return;
            if (effectMat == null || srcRT == null || tmpRT == null) return;

            var cmd = CommandBufferPool.Get(profilerTag);
            using (new ProfilingScope(cmd, new ProfilingSampler(profilerTag)))
            {
                // Use BlitCameraTexture so URP supplies correct _BlitScaleBias/Y orientation
                // Pass 0: horizontal blur into tmpRT
                Blitter.BlitCameraTexture(cmd, srcRT, tmpRT, effectMat, 0);
                // Pass 1: vertical blur + colorize into srcRT
                Blitter.BlitCameraTexture(cmd, tmpRT, srcRT, effectMat, 1);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Cleanup()
        {
        }
    }

    // ───────────────── Composite（カメラカラーへ“ブレンド”合成） ─────────────────
    class CompositePass : ScriptableRenderPass
    {
        readonly string profilerTag;
        readonly bool renderInSceneView;
        readonly Settings featureSettings;
        Material compositeMat;
        RTHandle srcRT;
        System.Func<RTHandle> getSrcRT;
        RTHandle cameraColor;
        RTHandle tmpCamera;
        bool useFullscreenRipple;

        static readonly int _MainTex = Shader.PropertyToID("_MainTex");
        static readonly int _UseSrcAlpha = Shader.PropertyToID("_UseSrcAlpha");

        public CompositePass(string tag, Shader shader, bool renderInSceneView, Settings featureSettings)
        {
            profilerTag = tag;
            this.renderInSceneView = renderInSceneView;
            this.featureSettings = featureSettings;

            if (shader == null) shader = Shader.Find("Hidden/LayerComposite");
            if (shader != null) compositeMat = CoreUtils.CreateEngineMaterial(shader);
            if (compositeMat != null && compositeMat.shader != null)
                useFullscreenRipple = compositeMat.shader.name.Contains("Ripple2D");
        }

        public void Setup(System.Func<RTHandle> srcProvider)
        {
            getSrcRT = srcProvider;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (!renderInSceneView && renderingData.cameraData.isSceneViewCamera) return;
            cameraColor = renderingData.cameraData.renderer.cameraColorTargetHandle; // パス内で取得
            // Query latest source from capture (allocated in its OnCameraSetup)
            srcRT = getSrcRT != null ? getSrcRT() : null;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!renderInSceneView && renderingData.cameraData.isSceneViewCamera) return;
            if (compositeMat == null || cameraColor == null) return;

            if (compositeMat.HasProperty(_UseSrcAlpha))
            {
                float useAlpha = (featureSettings != null && featureSettings.useSourceAlpha) ? 1f : 0f;
                compositeMat.SetFloat(_UseSrcAlpha, useAlpha);
            }

            var cmd = CommandBufferPool.Get(profilerTag);
            bool needCameraCopy = useFullscreenRipple || srcRT == null;
            if (needCameraCopy)
            {
                var desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.msaaSamples = 1;
                desc.depthBufferBits = 0;
                desc.useMipMap = false;
                desc.bindMS = false;
                desc.enableRandomWrite = false;
                int camId = renderingData.cameraData.camera.GetInstanceID();
                string uniqueName = $"{profilerTag}_TmpCam_c{camId}_{desc.width}x{desc.height}";
                RenderingUtils.ReAllocateIfNeeded(
                    ref tmpCamera, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: uniqueName);
            }
            using (new ProfilingScope(cmd, new ProfilingSampler(profilerTag)))
            {
                if (useFullscreenRipple)
                {
                    Blitter.BlitCameraTexture(cmd, cameraColor, tmpCamera, compositeMat, 0);
                    Blitter.BlitCameraTexture(cmd, tmpCamera, cameraColor, compositeMat, 1);
                    context.ExecuteCommandBuffer(cmd);
                    CommandBufferPool.Release(cmd);
                    return;
                }
                var sourceTex = srcRT;
                if (sourceTex == null)
                {
                    if (tmpCamera != null)
                    {
                        Blitter.BlitCameraTexture(cmd, cameraColor, tmpCamera);
                        sourceTex = tmpCamera;
                    }
                    else
                    {
                        sourceTex = cameraColor;
                    }
                }
                compositeMat.SetTexture(_MainTex, sourceTex);
                // Rely on URP-provided _BlitScaleBias for proper orientation.
                // Avoid extra manual flip to prevent upside-down output.
                compositeMat.SetFloat("_YFlip", 1f);

                // 既存カメラカラーを保持（Load）してその上にブレンド
                CoreUtils.SetRenderTarget(
                    cmd,
                    cameraColor,
                    RenderBufferLoadAction.Load, // ← 既存色を読み込む
                    RenderBufferStoreAction.Store // ← 書き戻す
                );

                Blitter.BlitCameraTexture(cmd, sourceTex, cameraColor, compositeMat, 0);
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

    // ───────────────── Feature本体 ─────────────────
    public Settings settings = new Settings();

    CapturePass capturePass;
    EffectPass effectPass;
    CompositePass compositePass;

    public override void Create()
    {
        capturePass = new CapturePass(
            $"{settings.featureName}_Capture",
            settings.layerMask,
            settings.hdr,
            settings.downsample,
            settings.includeCameraColor,
            settings.showInSceneView
        );
        capturePass.renderPassEvent = settings.captureEvent;

        effectPass = new EffectPass(
            $"{settings.featureName}_Effect",
            settings.postMaterial,
            settings.downsample,
            settings.showInSceneView
        );
        // Ensure ordering: Capture -> Effect -> Composite
        var effectEvent = (RenderPassEvent)Mathf.Max((int)settings.effectEvent, (int)capturePass.renderPassEvent + 1);
        effectPass.renderPassEvent = effectEvent;

        compositePass = new CompositePass(
            $"{settings.featureName}_Composite",
            settings.compositeShader,
            settings.showInSceneView,
            settings
        );
        var compositeEvent = (RenderPassEvent)Mathf.Max((int)settings.compositeEvent, (int)effectEvent + 1);
        compositePass.renderPassEvent = compositeEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // Capture が管理する RT を参照で後続に渡す（ここで cameraColor は触らない）
        System.Func<RTHandle> provider = () => capturePass.GetColorRT();
        effectPass.Setup(provider);
        compositePass.Setup(provider);

        renderer.EnqueuePass(capturePass);
        renderer.EnqueuePass(effectPass);
        renderer.EnqueuePass(compositePass);
    }

    protected override void Dispose(bool disposing)
    {
        effectPass?.Cleanup();
        compositePass?.Cleanup();
        capturePass?.Cleanup();
    }
}
