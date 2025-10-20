// Hidden/Ripple2D
// Radial ripple/refraction post effect for LayerPostFeature Effect pass.
// Pass 0 applies ripple distortion. Pass 1 is a no-op copy so it fits the
// two-pass execution used by the current EffectPass implementation.

Shader "Hidden/Ripple2D"
{
    Properties
    {
        _Center("Ripple Center (UV)", Vector) = (0.5, 0.5, 0, 0)
        _AmpPixels("Amplitude (px)", Range(0, 50)) = 5
        _WavelengthPixels("Wavelength (px)", Range(4, 512)) = 64
        _Speed("Speed (px/sec)", Range(0, 1024)) = 240
        _Attenuation("Attenuation (1/px)", Range(0, 0.05)) = 0.01
        _YFlip("Force Y Flip (0|1)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }

        ZWrite Off
        ZTest  Always
        Cull   Off

        // Pass 0: ripple distortion
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragRipple

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            float4 _BlitScaleBias;
            float  _YFlip;

            float4 _Center;          // xy used
            float  _AmpPixels;       // amplitude in pixels
            float  _WavelengthPixels;// wavelength in pixels
            float  _Speed;           // outward speed in px/sec
            float  _Attenuation;     // decay per pixel of radius

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

            float4 FragRipple(Varyings i) : SV_Target
            {
                float2 uv = ApplyFlip(i.uv);

                // delta in UV space
                float2 dUV = uv - _Center.xy;

                // convert to pixel space for radial wave math
                float2 pxScale = _ScreenParams.xy; // (width, height)
                float2 dPX = dUV * pxScale;
                float rPX = length(dPX);

                // avoid div by zero near center
                float2 dirUV = (rPX > 1e-5) ? (dUV / max(length(dUV), 1e-6)) : float2(0, 0);

                // wave phase (k r - w t) with k=2pi/lambda, w=2pi*v/lambda
                float lambda = max(_WavelengthPixels, 1e-4);
                float k = 6.28318530718 / lambda; // 2*pi
                float w = 6.28318530718 * _Speed / lambda;
                float phase = k * rPX - w * _Time.y;

                // Allow positive speeds to expand the ripple reach smoothly over time.
                float propagate = saturate(sign(_Speed));
                float front = max(_Speed, 0.0) * _Time.y;
                float distanceAhead = max(rPX - front, 0.0);
                float reachT = saturate(1.0 - distanceAhead / (lambda + 1e-4));
                float reach = lerp(1.0, reachT * reachT * (3.0 - 2.0 * reachT), propagate);

                float envelopeBase = exp(-_Attenuation * rPX);
                float envelopeFront = exp(-_Attenuation * distanceAhead);
                float envelope = lerp(envelopeBase, envelopeFront, propagate);
                float wave = sin(phase) * envelope * reach;

                // convert amplitude in px to UV units (use vertical texel for isotropy)
                float ampUV = _AmpPixels / max(_ScreenParams.y, 1.0);
                float2 offsetUV = dirUV * (wave * ampUV);

                float2 suv = uv + offsetUV;
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, suv);
            }
            ENDHLSL
        }

        // Pass 1: no-op copy (ensures compatibility with two-step blit).
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
