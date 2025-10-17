Shader "Hidden/ScreenDistortFromAlpha"
{
    Properties
    {
        _MaskTex ("Mask (A used)", 2D) = "white" {}
        _Intensity ("Distort Intensity (px)", Range(0,10)) = 2.0
        _RippleAmount ("Extra Ripple Amount", Range(0,2)) = 0.25
        _RippleFreq ("Ripple Frequency", Range(0,40)) = 12
        _RippleSpeed ("Ripple Speed", Range(0,5)) = 1.0
        _MaskTiling ("Mask Tiling", Vector) = (1,1,0,0)
        _MaskOffset ("Mask Offset", Vector) = (0,0,0,0)
        _Chromatic ("Chromatic Aberration (px)", Range(0,3)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque" "Queue"="Overlay"
        }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_SourceTex);
            SAMPLER(sampler_SourceTex);
            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);

            float _Intensity;
            float _RippleAmount;
            float _RippleFreq;
            float _RippleSpeed;
            float4 _MaskTiling; // xy = tiling, zw = unused
            float4 _MaskOffset; // xy = offset
            float _Chromatic;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS);
                o.uv = v.uv;
                return o;
            }

            // 画面解像度に基づくUVステップ（ピクセルサイズ）
            float2 PixelStep()
            {
                float2 size = _ScreenParams.xy; // (width, height)
                return 1.0 / size;
            }

            // マスクのアルファ勾配から“法線っぽい”方向ベクトルを作る
            float2 AlphaGradient(float2 uv)
            {
                float2 suv = uv * _MaskTiling.xy + _MaskOffset.xy;
                float2 px = PixelStep();
                float aL = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, suv - float2(px.x, 0)).a;
                float aR = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, suv + float2(px.x, 0)).a;
                float aD = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, suv - float2(0, px.y)).a;
                float aU = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, suv + float2(0, px.y)).a;

                float2 grad = float2(aR - aL, aU - aD); // ∇alpha
                return grad;
            }

            float AlphaAt(float2 uv)
            {
                float2 suv = uv * _MaskTiling.xy + _MaskOffset.xy;
                return SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, suv).a;
            }

            float2 Ripple(float2 uv)
            {
                // 細かい水面さざ波（アルファに依存しない“足し”）
                float t = _Time.y * _RippleSpeed;
                float r = sin((uv.x + uv.y) * _RippleFreq + t)
                    + 0.5 * sin((uv.x * 1.7 - uv.y * 1.3) * (_RippleFreq * 0.7) - t * 1.3);
                float2 dir = normalize(float2(ddx(uv.y), ddy(uv.x)) + 1e-5);
                return dir * r * _RippleAmount * PixelStep();
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;

                float a = AlphaAt(uv); // マスクのアルファ（歪み強度の元）
                float2 grad = AlphaGradient(uv); // アルファの勾配（エッジで強い）
                float2 baseOffset = normalize(grad + 1e-5) * a * _Intensity * PixelStep();

                float2 uvOffset = baseOffset + Ripple(uv);

                // クロマ収差（RGBで微妙にサンプル位置をずらす）
                float3 col;
                col.r = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_SourceTex,
                           uv + uvOffset + float2(_Chromatic, 0) * PixelStep()).r;
                col.g = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_SourceTex, uv + uvOffset).g;
                col.b = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_SourceTex,
                                                      uv + uvOffset - float2(_Chromatic, 0) * PixelStep()).b;

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}