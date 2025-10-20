// Hidden/DropShadow2D
// Works with URP Blitter + LayerPostFeature Effect pass
// Generates a blurred, offset shadow from captured layer content and keeps the original on top.
// Notes:
// - Uses TEXTURE2D_X(_BlitTexture) so it is robust across URP variants.
// - Uses _BlitScaleBias and optional _YFlip like DemoTint.

Shader "Hidden/DropShadow2D"
{
    Properties
    {
        _ShadowColor("Shadow Color", Color) = (0,0,0,0.5)
        _ShadowOffset("Shadow Offset (UV)", Vector) = (0.01,-0.01,0,0)
        _SigmaPixels("Gaussian Sigma (px)", Range(0,8)) = 2
        _KernelRadius("Kernel Radius (px)", Range(1,8)) = 3
        _YFlip("Force Y Flip (0|1)", Float) = 0
    }

    SubShader
    {
        Tags{ "RenderType" = "Opaque" }

        ZWrite Off
        ZTest Always
        Cull Off

        // Pass 0: Horizontal Gaussian blur of alpha into A channel.
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Source from URP Blitter
            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            float4 _BlitScaleBias;
            float  _YFlip;

            float4 _ShadowColor;
            float4 _ShadowOffset; // xy used
            float  _SigmaPixels;  // gaussian sigma in pixels
            float  _KernelRadius; // kernel radius in pixels (int)

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings  { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float2 uv = float2((input.vertexID << 1) & 2, input.vertexID & 2);
                o.positionCS = float4(uv * 2.0 - 1.0, 0.0, 1.0);
                o.uv = uv;
                return o;
            }

            float2 ApplyFlip(float2 uv)
            {
                uv = uv * _BlitScaleBias.xy + _BlitScaleBias.zw;
                if (_YFlip >= 0.5) uv.y = 1.0 - uv.y;
                return uv;
            }

            // Apply Gaussian horizontally to alpha
            float SampleH(float2 uv)
            {
                float2 texel = abs(float2(ddx(uv).x, ddy(uv).y));
                float sigma = max(_SigmaPixels, 1e-4);
                int R = (int)clamp(_KernelRadius, 1.0, 8.0);

                float a = 0.0;
                float wsum = 0.0;

                // center
                float w0 = 1.0;
                float a0 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv).a;
                a += w0 * a0;
                wsum += w0;

                // symmetric taps
                [unroll]
                for (int i = 1; i <= 8; i++)
                {
                    if (i > R) break;
                    float w = exp(- (i * i) / (2.0 * sigma * sigma));
                    float2 o = float2(texel.x * i, 0);
                    float a1 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv - o).a;
                    float a2 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv + o).a;
                    a += w * (a1 + a2);
                    wsum += 2.0 * w;
                }

                return a / max(wsum, 1e-5);
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float2 uv = ApplyFlip(i.uv);
                // Horizontal blur only, write to alpha channel, keep RGB clear
                float mH = SampleH(uv);
                return float4(0, 0, 0, mH);
            }
            ENDHLSL
        }

        // Pass 1: Vertical blur of A from previous pass, then compose colored shadow
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragV

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            float4 _BlitScaleBias;
            float  _YFlip;

            float4 _ShadowColor;
            float4 _ShadowOffset; // xy
            float  _SigmaPixels;
            float  _KernelRadius;

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings  { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float2 uv = float2((input.vertexID << 1) & 2, input.vertexID & 2);
                o.positionCS = float4(uv * 2.0 - 1.0, 0.0, 1.0);
                o.uv = uv;
                return o;
            }

            float2 ApplyFlip(float2 uv)
            {
                uv = uv * _BlitScaleBias.xy + _BlitScaleBias.zw;
                if (_YFlip >= 0.5) uv.y = 1.0 - uv.y;
                return uv;
            }

            float SampleV(float2 uv)
            {
                float2 texel = abs(float2(ddx(uv).x, ddy(uv).y));
                float sigma = max(_SigmaPixels, 1e-4);
                int R = (int)clamp(_KernelRadius, 1.0, 8.0);

                float a = 0.0;
                float wsum = 0.0;

                float w0 = 1.0;
                float a0 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv).a;
                a += w0 * a0;
                wsum += w0;

                [unroll]
                for (int i = 1; i <= 8; i++)
                {
                    if (i > R) break;
                    float w = exp(- (i * i) / (2.0 * sigma * sigma));
                    float2 o = float2(0, texel.y * i);
                    float a1 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv - o).a;
                    float a2 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv + o).a;
                    a += w * (a1 + a2);
                    wsum += 2.0 * w;
                }

                return a / max(wsum, 1e-5);
            }

            float4 FragV(Varyings i) : SV_Target
            {
                float2 uv = ApplyFlip(i.uv);
                // Sample vertical blur of the horizontally blurred alpha, then offset for shadow
                float2 suv = uv + _ShadowOffset.xy;
                float m = SampleV(suv);

                float a = saturate(m * _ShadowColor.a);
                return float4(_ShadowColor.rgb, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
