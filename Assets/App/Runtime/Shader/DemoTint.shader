// Hidden/DemoTint (URP安定版)
// ・Blitter が渡す _BlitTexture をサンプル
// ・_BlitScaleBias と _YFlip で上下反転に強い
// ・_TintStrength で効果量を調整

Shader "Hidden/DemoTint"
{
    Properties
    {
        _TintColor("Tint Color", Color) = (1,0.3,0.3,1)
        _TintStrength("Tint Strength", Range(0,1)) = 0.5
        _YFlip("Force Y Flip (0|1)", Float) = 0
    }

    SubShader

    {

        Tags
        {
            "RenderType"="Opaque"
        }

        ZWrite Off

        Cull Off

        ZTest Always



        Pass

        {

            HLSLPROGRAM
            #pragma vertex Vert

            #pragma fragment Frag


            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"


            TEXTURE2D_X(_BlitTexture);

            SAMPLER(sampler_BlitTexture);


            float4 _BlitScaleBias;

            float4 _TintColor;

            float _TintStrength;

            float _YFlip;


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

                if (_YFlip >= 0.5) uv.y = 1.0 - uv.y;

                return uv;
            }


            float4 Frag(Varyings i) : SV_Target

            {
                float2 uv = ApplyFlip(i.uv);

                float4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv);

                float s = saturate(_TintStrength);

                return lerp(src, float4(_TintColor.rgb, 1), s);
            }
            ENDHLSL

        }

    }

    Fallback Off

}