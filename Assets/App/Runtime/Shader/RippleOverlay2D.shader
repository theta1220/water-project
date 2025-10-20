// Hidden/RippleOverlay2D
// Fullscreen radial ripple overlay for LayerPostFeature Effect pass.
// Generates animated rings over the entire screen and blends in Composite.

Shader "Hidden/RippleOverlay2D"
{
    Properties
    {
        _Center("Ripple Center (UV)", Vector) = (0.5, 0.5, 0, 0)
        _Color("Color", Color) = (0.75, 0.9, 1, 0.35)
        _WavelengthPixels("Wavelength (px)", Range(8, 1024)) = 96
        _Speed("Speed (px/sec)", Range(0, 2048)) = 480
        _Attenuation("Attenuation (1/px)", Range(0, 0.05)) = 0.008
        _Sharpness("Ring Sharpness", Range(0.5, 8)) = 3
        _YFlip("Force Y Flip (0|1)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        ZWrite Off
        ZTest  Always
        Cull   Off

        // Pass 0: generate ring overlay into target
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            float4 _BlitScaleBias;
            float  _YFlip;

            float4 _Center;      // xy
            float4 _Color;       // rgba
            float  _WavelengthPixels;
            float  _Speed;
            float  _Attenuation;
            float  _Sharpness;

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

            float4 Frag(Varyings i) : SV_Target
            {
                float2 uv = ApplyFlip(i.uv);

                // radial distance in pixels
                float2 dUV = uv - _Center.xy;
                float2 dPX = dUV * _ScreenParams.xy;
                float r = length(dPX);

                float lambda = max(_WavelengthPixels, 1.0);
                float k = 6.28318530718 / lambda; // 2*pi
                float w = 6.28318530718 * _Speed / lambda;
                float phase = k * r - w * _Time.y;

                // ring pattern [0..1]
                float ring = 0.5 * (cos(phase) + 1.0);
                ring = pow(ring, _Sharpness);

                // distance attenuation
                float att = exp(-_Attenuation * r);

                float a = _Color.a * ring * att;
                return float4(_Color.rgb, a);
            }
            ENDHLSL
        }

        // Pass 1: copy (keeps compatibility with two-pass effect pipeline)
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCopy

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            float4 _BlitScaleBias;
            float  _YFlip;

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

            float4 FragCopy(Varyings i) : SV_Target
            {
                float2 uv = ApplyFlip(i.uv);
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

