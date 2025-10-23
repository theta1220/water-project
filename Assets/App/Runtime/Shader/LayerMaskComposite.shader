// Hidden/LayerMaskComposite: uses captured mask alpha to gate content color/alpha.
Shader "Hidden/LayerMaskComposite"
{
    Properties
    {
        _YFlip("Force Y Flip (0|1)", Float) = 0
        _MaskThreshold("Mask Threshold", Range(0, 1)) = 0
        _InvertMask("Invert Mask (0|1)", Float) = 0
        _ApplyToColor("Apply To Color (0|1)", Float) = 1
        _ApplyToAlpha("Apply To Alpha (0|1)", Float) = 1
        _ContentColor("Content Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Overlay" }

        Pass
        {
            ZWrite Off
            Cull Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            TEXTURE2D_X(_MaskTex);
            SAMPLER(sampler_MaskTex);

            float4 _BlitScaleBias;
            float  _YFlip;
            float  _MaskThreshold;
            float  _InvertMask;
            float  _ApplyToColor;
            float  _ApplyToAlpha;
            float4 _ContentColor;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

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
                if (_YFlip >= 0.5)
                    uv.y = 1.0 - uv.y;
                return uv;
            }

            float EvaluateMask(float maskAlpha)
            {
                float range = max(1e-5, 1.0 - _MaskThreshold);
                float normalized = saturate((maskAlpha - _MaskThreshold) / range);
                if (_InvertMask >= 0.5)
                    normalized = 1.0 - normalized;
                return normalized;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float2 uv = ApplyFlip(i.uv);
                float4 content = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv);
                float maskAlpha = SAMPLE_TEXTURE2D_X(_MaskTex, sampler_MaskTex, uv).a;
                float mask = EvaluateMask(maskAlpha);

                float3 color = content.rgb;
                // if (_ApplyToColor >= 0.5)
                //     color *= maskAlpha;

                float alpha = content.a;
                if (_ApplyToAlpha >= 0.5)
                    alpha *= maskAlpha;

                maskAlpha = ceil(maskAlpha);

                // color += 0.05;
                color += _ContentColor.rgb;

                // test
                // color.rgb = float3(1,0,0);
                return float4(color, maskAlpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
