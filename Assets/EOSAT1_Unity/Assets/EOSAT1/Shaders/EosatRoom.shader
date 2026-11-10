// EOSAT-1 VR - walls, floor and furniture of the mission control room (main menu).
// Lit by up to four soft "screen glow" lights that MissionControlRoom sets as shader
// globals, plus a sky/ground ambient. It does not depend on the render pipeline's
// lights, so the room looks the same on every pipeline and in both eyes.
Shader "EOSAT1/Room"
{
    Properties
    {
        _Color ("Colour", Color) = (0.3, 0.33, 0.4, 1)
        _Emission ("Emission", Color) = (0, 0, 0, 1)
        _Gloss ("Gloss", Range(0, 1)) = 0.1
        _Grid ("Seam lines per metre (0 = none)", Float) = 0
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
            float4 _Emission;
            float _Gloss;
            float _Grid;
            float4 _EosatRoomLightPos[4];   // xyz = position, w = falloff per square metre
            float4 _EosatRoomLightCol[4];
            float4 _EosatRoomAmbientSky;
            float4 _EosatRoomAmbientGround;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 nWS : TEXCOORD0;
                float3 posWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.nWS = UnityObjectToWorldNormal(v.normal);
                o.posWS = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.nWS);
                float3 toEye = normalize(_WorldSpaceCameraPos - i.posWS);

                float3 diffuse = lerp(_EosatRoomAmbientGround.rgb, _EosatRoomAmbientSky.rgb, n.y * 0.5 + 0.5);
                float3 specular = float3(0.0, 0.0, 0.0);
                for (int k = 0; k < 4; k++)
                {
                    float3 toLight = _EosatRoomLightPos[k].xyz - i.posWS;
                    float d2 = dot(toLight, toLight);
                    toLight *= rsqrt(max(d2, 1e-4));
                    float atten = 1.0 / (1.0 + d2 * _EosatRoomLightPos[k].w);
                    // wrapped lighting: the lights stand in for large glowing screens
                    float nl = saturate(dot(n, toLight) * 0.8 + 0.2);
                    diffuse += _EosatRoomLightCol[k].rgb * (nl * atten);
                    float3 halfDir = normalize(toLight + toEye);
                    specular += _EosatRoomLightCol[k].rgb * (pow(saturate(dot(n, halfDir)), 40.0) * atten);
                }

                float3 col = _Color.rgb * diffuse + specular * _Gloss + _Emission.rgb;

                // panel / tile seams, laid out in world space on the two axes the surface spans
                // (no branch: with _Grid = 0 the seam term is simply zero)
                float3 an = abs(n);
                float2 w = an.y > 0.6 ? i.posWS.xz : (an.x > 0.6 ? i.posWS.zy : i.posWS.xy);
                w *= _Grid;
                float2 toSeam = 0.5 - abs(frac(w) - 0.5);
                float2 px = toSeam / max(fwidth(w), 1e-5);
                float seam = (1.0 - saturate(min(px.x, px.y) / 1.5)) * step(1e-4, _Grid);
                col *= 1.0 - 0.5 * seam;
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
