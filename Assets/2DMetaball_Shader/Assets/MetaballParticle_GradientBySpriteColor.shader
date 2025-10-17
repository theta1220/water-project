Shader "Metaball/MetaballRenderer_Gradient"
{
    Properties
    {
        _MainTex ("MainTex", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Cutoff ("Cutoff", Range(0,1)) = 0.5
        _Stroke ("Stroke", Range(0,1)) = 0.1
        _StrokeColor ("StrokeColor", Color) = (1,1,1,1)
        _GradientPower ("Gradient Power", Range(0.1, 4)) = 1.5
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
        }

        Cull Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            half4 _Color;
            half4 _StrokeColor;
            fixed _Cutoff;
            fixed _Stroke;
            fixed _GradientPower;

            v2f vert (appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                return OUT;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.texcoord);

                // アルファで形状決定
                fixed alpha = tex.a;

                // カットオフ以下は破棄（透明部分）
                clip(alpha - _Cutoff);

                // グラデーション係数 (中心1→外周0)
                fixed t = pow(saturate((alpha - _Cutoff) / max(1e-5, (1.0 - _Cutoff))), _GradientPower);

                // ストローク境界（縁）に達したらストローク色に変化
                fixed strokeMask = smoothstep(_Stroke, _Stroke + 0.05, alpha);

                // 色補間
                fixed4 gradColor = lerp(_StrokeColor, i.color, strokeMask);
                fixed4 finalColor = lerp(_StrokeColor, gradColor, t);

                finalColor.a = t * i.color.a;

                return finalColor;
            }
            ENDCG
        }
    }
}
