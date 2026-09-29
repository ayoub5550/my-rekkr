// my-rekkr dev3 stages 7+8 — GPU post-processing for the game frame (UI buttons are drawn after,
// untouched). Pass 0: bright-pass + 2x downsample. Pass 1: 4-tap downsample (dual-Kawase style).
// Pass 2: tent upsample, additive. Pass 3: plain 4-tap downsample (side-fill blur source).
// Pass 4: composite = scene + bloom, colour grade, sharpen, vignette, CRT, blurred side-fill.
// Mobile: half precision, no MRT; the chain works on 1/2..1/32 size targets.
Shader "Rekkr/Post"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _BloomTex ("Bloom", 2D) = "black" {}
        _BlurTex ("SideBlur", 2D) = "black" {}
    }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex; float4 _MainTex_TexelSize;
    sampler2D _BloomTex;
    sampler2D _BlurTex;
    half _Threshold, _Knee, _Bloom, _Vignette, _Contrast, _Saturation, _Warmth, _Sharpen, _Crt, _Scan;
    float4 _Content;   // x0, width (uv) of the centred 4:3 image; width 0 = no side-fill
    float4 _ScreenPx;  // game rect size in physical pixels (xy)
    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
    v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
    half3 Down4(float2 uv)
    {
        float2 d = _MainTex_TexelSize.xy;
        return (tex2D(_MainTex, uv + float2(-d.x, -d.y)).rgb + tex2D(_MainTex, uv + float2(d.x, -d.y)).rgb
              + tex2D(_MainTex, uv + float2(-d.x, d.y)).rgb + tex2D(_MainTex, uv + float2(d.x, d.y)).rgb) * 0.25h;
    }
    ENDCG
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass // 0 prefilter
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(v2f i) : SV_Target
            {
                half3 c = Down4(i.uv);
                half br = max(c.r, max(c.g, c.b));
                half soft = clamp(br - _Threshold + _Knee, 0, 2 * _Knee);
                soft = soft * soft / (4 * _Knee + 1e-4h);
                half w = max(soft, br - _Threshold) / max(br, 1e-4h);
                return half4(c * w, 1);
            }
            ENDCG
        }
        Pass // 1 downsample
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(v2f i) : SV_Target { return half4(Down4(i.uv), 1); }
            ENDCG
        }
        Pass // 2 upsample (additive onto the bigger level)
        {
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(v2f i) : SV_Target
            {
                float2 d = _MainTex_TexelSize.xy * 0.5;
                half3 c = (tex2D(_MainTex, i.uv + float2(-d.x, 0)).rgb + tex2D(_MainTex, i.uv + float2(d.x, 0)).rgb
                         + tex2D(_MainTex, i.uv + float2(0, -d.y)).rgb + tex2D(_MainTex, i.uv + float2(0, d.y)).rgb) * 0.25h;
                return half4(c, 1);
            }
            ENDCG
        }
        Pass // 3 plain downsample (same as 1; kept separate for clarity)
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(v2f i) : SV_Target { return half4(Down4(i.uv), 1); }
            ENDCG
        }
        Pass // 4 composite
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                half3 c = tex2D(_MainTex, uv).rgb;
                // Blurred side-fill: outside the centred 4:3 image show a dark, blurred, stretched copy.
                if (_Content.y > 0 && (uv.x < _Content.x || uv.x > _Content.x + _Content.y))
                {
                    float2 buv = float2(_Content.x + uv.x * _Content.y, uv.y);
                    half3 b = tex2D(_BlurTex, buv).rgb * 0.42h;
                    half edge = saturate(min(abs(uv.x - _Content.x), abs(uv.x - _Content.x - _Content.y)) * 60);
                    return half4(lerp(c, b, edge), 1);
                }
                // Sharpen (contrast-adaptive, 4 neighbours in screen pixels).
                if (_Sharpen > 0)
                {
                    float2 d = 1.0 / _ScreenPx.xy;
                    half3 n = tex2D(_MainTex, uv + float2(0, d.y)).rgb, s = tex2D(_MainTex, uv - float2(0, d.y)).rgb;
                    half3 e = tex2D(_MainTex, uv + float2(d.x, 0)).rgb, w = tex2D(_MainTex, uv - float2(d.x, 0)).rgb;
                    half3 mn = min(c, min(min(n, s), min(e, w))), mx = max(c, max(max(n, s), max(e, w)));
                    half3 amp = saturate(min(mn, 1 - mx) / max(mx, 1e-3h));
                    half3 k = -sqrt(amp) * _Sharpen * 0.2h;
                    c = saturate((c + (n + s + e + w) * k) / (1 + 4 * k));
                }
                c += tex2D(_BloomTex, uv).rgb * _Bloom;
                // Colour grade: contrast around mid grey, saturation, warm/cool balance.
                c = (c - 0.5h) * _Contrast + 0.5h;
                half l = dot(c, half3(0.299h, 0.587h, 0.114h));
                c = lerp(half3(l, l, l), c, _Saturation);
                c *= half3(1 + _Warmth, 1, 1 - _Warmth);
                // CRT: scanlines at physical pixel pitch + soft aperture mask.
                if (_Crt > 0)
                {
                    float y = uv.y * _ScreenPx.y;
                    half scan = 0.75h + 0.25h * cos(y * 3.14159 * 2 / max(_Scan, 2));
                    float x = uv.x * _ScreenPx.x;
                    half3 mask = half3(1, 1, 1) * 0.85h;
                    int m = (int)fmod(x, 3);
                    if (m == 0) mask.r = 1.1h; else if (m == 1) mask.g = 1.1h; else mask.b = 1.1h;
                    c *= lerp(half3(1, 1, 1), mask * scan, _Crt);
                }
                // Vignette.
                float2 q = uv - 0.5;
                c *= 1 - _Vignette * saturate(dot(q, q) * 2.2);
                return half4(saturate(c), 1);
            }
            ENDCG
        }
    }
}
