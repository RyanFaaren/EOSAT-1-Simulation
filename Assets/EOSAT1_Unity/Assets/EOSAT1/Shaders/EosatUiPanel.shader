// EOSAT-1 VR - a flat rounded rectangle with a border, for the menu's screens, buttons,
// markers and the fade to black. Drawn on a unit quad; _Aspect is the quad's width / height.
// The draw order between overlapping panels is set with the renderer's sorting order.
Shader "EOSAT1/UiPanel"
{
    Properties
    {
        _Color ("Fill (top)", Color) = (0.05, 0.09, 0.16, 1)
        _Color2 ("Fill (bottom)", Color) = (0.02, 0.04, 0.08, 1)
        _BorderColor ("Border", Color) = (0.3, 0.85, 1, 1)
        _Border ("Border width (fraction of height)", Float) = 0.03
        _Radius ("Corner radius (fraction of height)", Float) = 0.1
        _Aspect ("Width / height", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float4 _Color2;
            float4 _BorderColor;
            float _Border;
            float _Radius;
            float _Aspect;

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
                // work in units of the panel's height, origin at its centre
                float2 extent = float2(max(_Aspect, 1e-3), 1.0) * 0.5;
                float2 p = (i.uv - 0.5) * float2(max(_Aspect, 1e-3), 1.0);
                float r = min(_Radius, min(extent.x, extent.y));
                float2 q = abs(p) - (extent - r);
                float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;   // < 0 inside
                float aa = max(fwidth(d), 1e-5);

                float inside = 1.0 - smoothstep(-aa, 0.0, d);
                float edge = _Border > 0.0 ? smoothstep(-_Border - aa, -_Border, d) * _BorderColor.a : 0.0;

                float4 fill = lerp(_Color2, _Color, i.uv.y);
                float4 c;
                c.rgb = lerp(fill.rgb, _BorderColor.rgb, edge);
                c.a = lerp(fill.a, 1.0, edge) * inside;
                return c;
            }
            ENDCG
        }
    }
    FallBack Off
}
