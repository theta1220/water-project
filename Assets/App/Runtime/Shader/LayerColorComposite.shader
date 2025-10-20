// Hidden/LayerColorComposite: captured layer alpha を利用して一律の塗りつぶし色を合成
Shader "Hidden/LayerColorComposite"
{
    Properties
    {
        _FillColor("Fill Color", Color) = (1,1,1,1)
        _YFlip("Force Y Flip (0|1)", Float) = 0
        _UseSrcAlpha("Multiply Source Alpha (0|1)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" }

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

            float4 _BlitScaleBias;
            float4 _FillColor;
            float  _YFlip;
            float  _UseSrcAlpha;

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

            float4 Frag(Varyings i) : SV_Target
            {
                float2 uv = ApplyFlip(i.uv);
                float4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv);
                float colorAlpha = saturate(_FillColor.a);
                float srcAlpha = saturate(src.a);
                float useSrcAlpha = saturate(_UseSrcAlpha);
                float finalAlpha = lerp(colorAlpha, srcAlpha * colorAlpha, useSrcAlpha);

                float3 finalColor = _FillColor.rgb;
                return float4(finalColor, finalAlpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
