Shader "ResortEmpire/UI/Island Base Wave Overlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _WaveColor ("Wave Color", Color) = (0.82,0.97,1,1)
        _Intensity ("Intensity", Range(0,1)) = 0.38
        _Speed ("Speed", Range(0,2)) = 0.25
        _Distortion ("Distortion", Range(0,0.03)) = 0.006
        _Scale ("Motion Scale", Range(1,40)) = 18
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
            fixed4 _Color;
            fixed4 _WaveColor;
            fixed _Intensity;
            fixed _Speed;
            fixed _Distortion;
            fixed _Scale;
            float _OceanCyclePhase;

            v2f vert(appdata_t input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            fixed FoamMask(fixed4 sample)
            {
                fixed luminance = dot(sample.rgb, fixed3(0.299, 0.587, 0.114));
                fixed maximum = max(sample.r, max(sample.g, sample.b));
                fixed minimum = min(sample.r, min(sample.g, sample.b));
                fixed saturation = maximum - minimum;
                fixed bright = smoothstep(0.70, 0.94, luminance);
                fixed neutral = 1.0 - smoothstep(0.10, 0.30, saturation);
                return bright * neutral * sample.a;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float time = _OceanCyclePhase * _Speed;
                float2 uv = input.texcoord;
                float2 phase = uv * _Scale * 6.2831853;
                float2 movement = float2(
                    sin(phase.y + time * 6.2831853) + 0.5 * sin(phase.x * 0.63 - time * 4.1),
                    cos(phase.x - time * 5.1) + 0.5 * cos(phase.y * 0.71 + time * 3.7));
                float2 animatedUv = saturate(uv + movement * _Distortion);

                fixed foam = FoamMask(tex2D(_MainTex, animatedUv));
                fixed shimmer = 0.78 + 0.22 * sin((uv.x + uv.y) * _Scale * 3.1415926 + time * 6.2831853);
                fixed alpha = saturate(foam * _Intensity * shimmer * input.color.a);
                return fixed4(_WaveColor.rgb, _WaveColor.a * alpha);
            }
            ENDCG
        }
    }
}
