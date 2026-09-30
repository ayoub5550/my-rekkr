// my-rekkr dev6 — puts the Remaster GPU view into the software frame. Works on the transposed frame
// texture (u = row / H, v = column / W). A frame pixel whose alpha code is 250 ("GPU here", written by the
// software renderer with WorldPassOff) inside the 3D window takes the GPU pixel (colour + G-buffer code);
// every other pixel (weapon 249, 2D 255, outside the window) stays the software pixel.
Shader "Rekkr/Remaster/RemasterComposite"
{
    Properties
    {
        _MainTex ("Frame", 2D) = "black" {}
        _GpuTex ("GPU view", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            sampler2D _GpuTex;
            float4 _Win;     // window x, y, w, h (frame px)
            float4 _Frame;   // W, H, 1/W, 1/H
            float4 frag(v2f_img i) : SV_Target
            {
                float x = floor(i.uv.y * _Frame.x), y = floor(i.uv.x * _Frame.y);
                float4 s = tex2Dlod(_MainTex, float4((y + 0.5) * _Frame.w, (x + 0.5) * _Frame.z, 0, 0));
                int code = (int)(s.a * 255.0 + 0.5);
                if (code != 250 && code != 251) return s;
                float wx = x - _Win.x, wy = y - _Win.y;
                if (wx < 0 || wy < 0 || wx >= _Win.z || wy >= _Win.w) return float4(s.rgb, 1);
                float4 g = tex2Dlod(_GpuTex, float4((wx + 0.5) / _Win.z, 1.0 - (wy + 0.5) / _Win.w, 0, 0));
                if (code == 251) return float4(g.rgb * 0.33, 1);   // under the HUD panel (COLORMAP 22 ≈ 1/3)
                return g;
            }
            ENDCG
        }
    }
}
