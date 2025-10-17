using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace App.Runtime.Shader
{
    public class AlphaDistortionFeature : ScriptableRendererFeature
    {
        [Serializable]
        public class Settings
        {
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            public Material material; // 上のシェーダーで作るマテリアル
            public Texture2D maskTexture; // アルファを見るマスク（Aのみ使用）
            [Range(0, 10)] public float intensity = 2f; // 歪みの強さ（px）
            [Range(0, 2)] public float rippleAmount = 0.25f;
            [Range(0, 40)] public float rippleFreq = 12f;
            [Range(0, 5)] public float rippleSpeed = 1f;
            public Vector2 maskTiling = Vector2.one;
            public Vector2 maskOffset = Vector2.zero;
            [Range(0, 3)] public float chromatic = 0.5f;
        }

        private class Pass : ScriptableRenderPass
        {
            private readonly string _profilerTag = "Alpha Distortion (From Mask A)";
            private Material _mat;
            private Settings _settings;

#if UNITY_2022_1_OR_NEWER
            private RTHandle _cameraSource;
            private RTHandle _tempColor;
#else
        RenderTargetIdentifier _cameraSource;
        int _tempColorID = Shader.PropertyToID("_AlphaDistort_Temp");
#endif

            private static readonly int _SourceTexID = UnityEngine.Shader.PropertyToID("_SourceTex");
            private static readonly int _MaskTexID = UnityEngine.Shader.PropertyToID("_MaskTex");
            private static readonly int _IntensityID = UnityEngine.Shader.PropertyToID("_Intensity");
            private static readonly int _RippleAmountID = UnityEngine.Shader.PropertyToID("_RippleAmount");
            private static readonly int _RippleFreqID = UnityEngine.Shader.PropertyToID("_RippleFreq");
            private static readonly int _RippleSpeedID = UnityEngine.Shader.PropertyToID("_RippleSpeed");
            private static readonly int _MaskTilingID = UnityEngine.Shader.PropertyToID("_MaskTiling");
            private static readonly int _MaskOffsetID = UnityEngine.Shader.PropertyToID("_MaskOffset");
            private static readonly int _ChromaticID = UnityEngine.Shader.PropertyToID("_Chromatic");

            public Pass(Settings settings)
            {
                _settings = settings;
                _mat = settings.material;
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
#if UNITY_2022_1_OR_NEWER
                _cameraSource = renderingData.cameraData.renderer.cameraColorTargetHandle;
                RenderingUtils.ReAllocateIfNeeded(ref _tempColor, renderingData.cameraData.cameraTargetDescriptor,
                    name: "_AlphaDistort_Temp");
#else
            _cameraSource = renderingData.cameraData.renderer.cameraColorTarget;
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            cmd.GetTemporaryRT(_tempColorID, desc);
#endif
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (_mat == null) return;

                var cmd = CommandBufferPool.Get(_profilerTag);

                // パラメータ反映
                _mat.SetTexture(_MaskTexID, _settings.maskTexture);
                _mat.SetFloat(_IntensityID, _settings.intensity);
                _mat.SetFloat(_RippleAmountID, _settings.rippleAmount);
                _mat.SetFloat(_RippleFreqID, _settings.rippleFreq);
                _mat.SetFloat(_RippleSpeedID, _settings.rippleSpeed);
                _mat.SetVector(_MaskTilingID, new Vector4(_settings.maskTiling.x, _settings.maskTiling.y, 0, 0));
                _mat.SetVector(_MaskOffsetID, new Vector4(_settings.maskOffset.x, _settings.maskOffset.y, 0, 0));
                _mat.SetFloat(_ChromaticID, _settings.chromatic);

#if UNITY_2022_1_OR_NEWER
                // Blit相当（Blitter推奨）
                Blitter.BlitCameraTexture(cmd, _cameraSource, _tempColor, _mat, 0);
                Blitter.BlitCameraTexture(cmd, _tempColor, _cameraSource);
#else
            cmd.SetGlobalTexture(_SourceTexID, _cameraSource);
            cmd.Blit(_cameraSource, _tempColor, _mat, 0);
            cmd.Blit(_tempColor, _cameraSource);
#endif
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public override void OnCameraCleanup(CommandBuffer cmd)
            {
#if UNITY_2022_1_OR_NEWER
                // RTHandleはReAllocateIfNeededしたのでここでは解放しない
#else
            if (_tempColorID != -1) cmd.ReleaseTemporaryRT(_tempColorID);
#endif
            }
        }

        public Settings settings = new Settings();
        private Pass _pass;

        public override void Create()
        {
            _pass = new Pass(settings) { renderPassEvent = settings.renderPassEvent };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (settings.material == null) return;
            renderer.EnqueuePass(_pass);
        }
    }
}