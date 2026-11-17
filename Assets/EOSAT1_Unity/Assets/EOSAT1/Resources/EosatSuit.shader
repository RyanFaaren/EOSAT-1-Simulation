// EOSAT-1 VR - the space-suit gloves (and other things the wearer carries). Lit by the Sun
// like the spacecraft, plus a soft light from the viewer (a helmet lamp), so the gloves can
// still be seen in the Earth's shadow. Lives in a Resources folder so that it is included
// in builds even though only scripts use it. Single-pass stereo included.
Shader "EOSAT1/Suit"
{
    Properties
    {
        _Color ("Colour", Color) = (0.92, 0.92, 0.9, 1)
        _Shine ("Shine", Range(0, 1)) = 0.15
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" }
        Pass
        {
            Cull Back
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Shine;
            float4 _EOSAT_SunDir;          // set by SpaceScene: direction to the Sun, world space
            float _EOSAT_SunVisibility;    // 1 in sunlight, 0 in the Earth's shadow

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
                float3 N = normalize(i.nWS);
                float3 V = normalize(_WorldSpaceCameraPos.xyz - i.posWS);
                float lamp = saturate(dot(N, V));
                float sunLen = length(_EOSAT_SunDir.xyz);
                float sun = 0.0;
                float spec = 0.0;
                if (sunLen > 0.001)
                {
                    float3 L = _EOSAT_SunDir.xyz / sunLen;
                    sun = saturate(dot(N, L)) * saturate(_EOSAT_SunVisibility);
                    float3 H = normalize(L + V);
                    spec = pow(saturate(dot(N, H)), 24.0) * _Shine * saturate(_EOSAT_SunVisibility);
                }
                float rim = pow(1.0 - lamp, 3.0) * 0.08;
                float3 c = _Color.rgb * (0.2 + 0.42 * lamp + 0.6 * sun) + spec + rim;
                return fixed4(c, 1.0);
            }
            ENDCG
        }

        Pass
        {
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Back
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
