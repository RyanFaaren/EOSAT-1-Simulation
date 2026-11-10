// EOSAT-1 VR - the operator monitors in the mission control room (main menu). Draws a
// busy-looking display without any texture: a title bar, rows of flickering "text" and a
// small bar chart. _Seed makes every monitor different.
Shader "EOSAT1/ConsoleScreen"
{
    Properties
    {
        _Color ("Colour", Color) = (0.3, 0.8, 1, 1)
        _Seed ("Seed", Float) = 0
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

            float4 _Color;
            float _Seed;

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

            float hash21 (float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

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
                float2 uv = i.uv;
                float time = _Time.y;

                // title bar
                float title = step(0.87, uv.y) * step(uv.y, 0.95) * step(0.05, uv.x) * step(uv.x, 0.5);

                // rows of "text" on the left: each row has its own length and refresh beat
                float rowPos = uv.y * 11.0;
                float row = floor(rowPos);
                float inRow = step(0.28, frac(rowPos)) * step(frac(rowPos), 0.72);
                float cell = floor(uv.x * 40.0);
                float beat = floor(time * (0.4 + hash21(float2(row, _Seed)) * 1.2) + _Seed);
                float glyph = step(0.3, hash21(float2(cell + beat * 13.0, row + _Seed * 7.0)));
                float rowLength = 0.2 + 0.36 * hash21(float2(row + 3.0, _Seed));
                float text = glyph * inRow * step(0.05, uv.x) * step(uv.x, rowLength) * step(0.08, uv.y) * step(uv.y, 0.82);

                // bar chart on the right
                float bx = (uv.x - 0.62) / 0.33;
                float bar = floor(bx * 6.0);
                float phase = hash21(float2(bar, _Seed + 5.0)) * 6.2832;
                float height = 0.25 + 0.5 * (0.5 + 0.5 * sin(time * (0.3 + 0.5 * hash21(float2(bar, _Seed))) + phase));
                float chart = step(0.0, bx) * step(bx, 1.0) * step(0.18, frac(bx * 6.0)) *
                              step(0.1, uv.y) * step(uv.y, 0.1 + height * 0.7);

                // soft darkening towards the edges
                float2 e = uv * (1.0 - uv);
                float vignette = saturate(pow(e.x * e.y * 16.0, 0.35));

                float lit = title * 0.9 + text * 0.75 + chart * 0.55;
                float3 col = _Color.rgb * (0.05 + lit) * vignette;
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
