Shader "ResortEmpire/UI/Island Base Cohesion"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Sharpen ("Sharpen", Range(0, 0.25)) = 0.08
        _Contrast ("Contrast", Range(0.9, 1.15)) = 1.03
        _Warmth ("Warmth", Range(0, 0.25)) = 0.08
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "CanUseSpriteAtlas"="True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            fixed _Sharpen;
            fixed _Contrast;
            fixed _Warmth;

            v2f vert(appdata_t input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 center = tex2D(_MainTex, input.texcoord);
                fixed3 neighbours =
                    tex2D(_MainTex, input.texcoord + float2(_MainTex_TexelSize.x, 0)).rgb +
                    tex2D(_MainTex, input.texcoord - float2(_MainTex_TexelSize.x, 0)).rgb +
                    tex2D(_MainTex, input.texcoord + float2(0, _MainTex_TexelSize.y)).rgb +
                    tex2D(_MainTex, input.texcoord - float2(0, _MainTex_TexelSize.y)).rgb;

                fixed3 localAverage = neighbours * 0.25;
                fixed3 rgb = center.rgb + (center.rgb - localAverage) * _Sharpen;
                rgb = (rgb - 0.5) * _Contrast + 0.5;

                fixed3 warmRgb = saturate(rgb * fixed3(1.02, 1.0, 0.96));
                rgb = lerp(rgb, warmRgb, _Warmth);

                fixed4 output = fixed4(saturate(rgb), center.a);
                output *= input.color;
                return output;
            }
            ENDCG
        }
    }
}
