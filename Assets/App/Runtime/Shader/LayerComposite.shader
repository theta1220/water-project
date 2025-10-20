// Hidden/LayerComposite (安定版・強制Y反転スイッチ付き)
// デフォルトはアルファ合成。グローなら Blend One One に。
Shader "Hidden/LayerComposite"
{
    Properties{
        _YFlip("Force Y Flip (0|1)", Float) = 0
        _UseSrcAlpha("Use Source Alpha (0|1)", Float) = 1
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

            // Blitter が供給するテクスチャ（URP版差に強い TEXTURE2D_X を使用）
            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            // URPが渡すスケール/バイアス（ある版では未設定だがあっても害はない）
            float4 _BlitScaleBias;
            float  _YFlip;
            float  _UseSrcAlpha;

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings  { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                // フルスクリーントライアングル（Blit.hlsl 非依存）
                float2 uv = float2((input.vertexID << 1) & 2, input.vertexID & 2);
                o.positionCS = float4(uv * 2.0 - 1.0, 0.0, 1.0);
                o.uv = uv;
                return o;
            }

            float2 ApplyFlip(float2 uv)
            {
                // 1) まず _BlitScaleBias を“無条件”に適用（ある版で必要、ない版では単位変換になり無害）
                uv = uv * _BlitScaleBias.xy + _BlitScaleBias.zw;

                // 2) それでもズレる環境向けの“強制Y反転スイッチ”
                //    _YFlip >= 0.5 のときだけ Y を反転
                if (_YFlip >= 0.5)
                    uv.y = 1.0 - uv.y;

                return uv;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float2 uv = ApplyFlip(i.uv);
                float4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv);
                float useAlpha = saturate(_UseSrcAlpha);
                col.a = lerp(1.0, col.a, useAlpha);
                return col;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
