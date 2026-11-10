// EOSAT-1 VR - the world map on the mission control wall (main menu). Redraws the Earth
// texture (longitude -180 at the left, north at the top) as a control-room display:
// dark oceans, glowing land, and a 30 degree latitude/longitude grid.
Shader "EOSAT1/MapScreen"
{
    Properties
    {
        _MainTex ("Earth map", 2D) = "black" {}
        _Ocean ("Ocean", Color) = (0.008, 0.03, 0.07, 1)
        _Land ("Land", Color) = (0.1, 0.5, 0.68, 1)
        _GridColor ("Grid", Color) = (0.2, 0.55, 0.75, 1)
        _Gain ("Land contrast", Float) = 12
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Cull Off
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Ocean;
            float4 _Land;
            float4 _GridColor;
            float _Gain;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 t = tex2D(_MainTex, i.uv).rgb;
                // oceans are blue-dominant; land and ice are not
                float land = saturate((max(t.r, t.g) - t.b * 0.75) * _Gain);
                float shade = 0.55 + 0.45 * saturate(dot(t, float3(0.3, 0.59, 0.11)) * 4.0);
                float3 col = lerp(_Ocean.rgb, _Land.rgb * shade, land);

                // graticule every 30 degrees; the equator a little brighter
                float2 g = i.uv * float2(12.0, 6.0);
                float2 toLine = 0.5 - abs(frac(g) - 0.5);
                float2 px = toLine / max(fwidth(g), 1e-5);
                float grid = 1.0 - saturate(min(px.x, px.y));
                float equator = 1.0 - saturate(abs(i.uv.y - 0.5) / max(fwidth(i.uv.y), 1e-5) / 1.5);
                col += _GridColor.rgb * (grid * 0.22 + equator * 0.25);
                return fixed4(col, 1.0);
            }
            ENDCG
        }

        Pass
        {
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target { return 0; }
            ENDCG
        }
    }
    FallBack Off
}
