// Displays Managed Doom's column-major framebuffer (x * height + y) uploaded as a
// texture of width = screen height, height = screen width, i.e. transposed.
Shader "Rekkr/Screen"
{
    Properties
    {
        _MainTex ("Frame", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                // Screen (sx right, sy up) -> framebuffer row = sx, column = 1 - sy.
                float2 t = float2(1.0 - i.uv.y, i.uv.x);
                // Sharp pixel scaling: nearest texel centre with a 1-texel wide smooth edge,
                // so the 4:3 stretch stays crisp without shimmering on phones.
                float2 texSize = _MainTex_TexelSize.zw;
                float2 p = t * texSize;
                float2 fw = max(fwidth(p), 1e-4);
                float2 c = floor(p - 0.5) + 0.5;
                float2 f = clamp((p - c - 0.5) / fw + 0.5, 0.0, 1.0);
                float2 uv = (c + f) / texSize;
                fixed4 col = tex2D(_MainTex, uv);
                col.a = 1;
                return col;
            }
            ENDCG
        }
    }
}
