// my-rekkr dev5 — "Masterpiece" world effects on the software-rendered frame, driven by the G-buffer
// code the renderer stores in the frame's alpha (docs/DEV5.md §2). Works in FRAME space on the
// transposed column-major frame texture (u = row / H, v = column / W), before the upscale.
// Pass 0: world  (sky clouds + sun, water reflections, hot liquids, lights, AO, fog, lightning)
// Pass 1: sun rays (radial blur of the bright sky, half resolution)
// Pass 2: downsample (4 taps) for depth of field
// Pass 3: composite (world + rays + DoF), alpha = G-buffer code kept for later passes
// Pass 4: particles + dev7 3D weather (quads in frame space, depth-tested against the G-buffer)
// Pass 6: dev8 solid particles (smoke, chips, debris, blood): alpha-blended, G-buffer alpha kept
Shader "Rekkr/World"
{
    Properties
    {
        _MainTex ("Frame", 2D) = "black" {}
        _GTex ("GBuffer source", 2D) = "black" {}
        _RaysTex ("Rays", 2D) = "black" {}
        _BlurTex ("Blur", 2D) = "black" {}
    }
    CGINCLUDE
    #include "UnityCG.cginc"
    #pragma target 3.5
    // dev6 bug fix: the frame and its G-buffer are read with exact texel loads, never through the
    // texture's bilinear sampler — "centre" samples of the bilinear frame came back blended with the
    // neighbours on some GPUs, so weapon/HUD edges got in-between codes (252..254 = "far sky") and the
    // world pass painted fog/sun on them: the bright or red outline around the weapon and the HUD.
    Texture2D _MainTex; SamplerState sampler_MainTex; float4 _MainTex_TexelSize;
    Texture2D _GTex;
    sampler2D _RaysTex;
    sampler2D _BlurTex;
    float4 _Frame;      // W (columns), H (rows), 1/W, 1/H
    float4 _ViewP;      // centerX, centerY, projection, time (s)
    float4 _Win;        // window x, y, w, h (frame px)
    float4 _Cam;        // viewX, viewY, viewZ, angle (rad)
    float4 _Sun;        // world dir xyz (x east, y north, z up), strength
    float4 _SunCol;     // rgb, sun-rays strength
    float4 _SunScreen;  // frame px x, y, visible (0/1), rays radius
    float4 _FogCol;     // rgb, density (1/units)
    float4 _Cloud;      // rgb tint, coverage
    float4 _CloudP;     // wind x, wind y, speed, on (0/1)
    float4 _Weather;    // type (0 none, 1 rain, 2 snow, 3 embers, 4 dust), intensity*outdoor, lightning, rainOnWater
    float4 _FogP;       // dev7: x = outdoor (player under the sky, smoothed)
    float4 _Fx;         // water on, AO strength, lights on, hot on
    float4 _Fx2;        // fog on, DoF on, dof start (units), debug view
    float4 _LightPos[8];  // view space x (right), y (up), z (forward), radius
    float4 _LightCol[8];  // rgb, unused
    float _LightCount;

    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
    v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }

    // Frame pixel (x = column, y = row); the texture is transposed (texel x = row, texel y = column).
    int3 TexelOf(float x, float y) { return int3(clamp((int)floor(y), 0, (int)_Frame.y - 1), clamp((int)floor(x), 0, (int)_Frame.x - 1), 0); }
    float4 Fetch(float x, float y) { return _MainTex.Load(TexelOf(x, y)); }
    float4 FetchG(float x, float y) { return _GTex.Load(TexelOf(x, y)); }
    int CodeOf(float a) { return (int)(a * 255.0 + 0.5); }
    float ZOf(int c)
    {
        if (c < 200) return 8.0 * exp2(c / 19.9);
        if (c < 224) return 8.0 * exp2((c - 200) * (200.0 / 24.0) / 19.9);
        if (c < 236) return 8.0 * exp2((c - 224) * (200.0 / 12.0) / 19.9);
        if (c < 248) return 8.0 * exp2((c - 236) * (200.0 / 12.0) / 19.9);
        return 100000.0;
    }
    bool IsLiquid(int c) { return c >= 200 && c < 248; }
    bool InWindow(float x, float y) { return x >= _Win.x && x < _Win.x + _Win.z && y >= _Win.y && y < _Win.y + _Win.w; }

    // ---- noise
    float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
    float VNoise(float2 p)
    {
        float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
        return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
    }
    float Fbm(float2 p)
    {
        float s = 0, a = 0.5;
        for (int k = 0; k < 4; k++) { s += a * VNoise(p); p = p * 2.03 + 17.1; a *= 0.5; }
        return s;
    }

    // World direction of frame pixel (x, y): Doom angle a (0 = east, CCW), x right, y up.
    float3 WorldDir(float x, float y)
    {
        float tx = (x + 0.5 - _ViewP.x) / _ViewP.z;
        float ty = (_ViewP.y - y - 0.5) / _ViewP.z;
        float a = _Cam.w;
        float2 fwd = float2(cos(a), sin(a));
        float2 right = float2(sin(a), -cos(a));
        float2 h = fwd + right * tx;
        return normalize(float3(h.x, h.y, ty));
    }

    // Procedural cloud layer + sun glow over the painted sky colour.
    float3 SkyShade(float x, float y, float3 base)
    {
        float3 d = WorldDir(x, y);
        float3 c = base;
        if (_CloudP.w > 0)
        {
            float el = max(d.z, 0.035);
            float2 p = d.xy / el * 0.9;
            float t = _ViewP.w * _CloudP.z;
            float n = Fbm(p * 1.6 + float2(_CloudP.x, _CloudP.y) * t);
            float n2 = Fbm(p * 3.7 - float2(_CloudP.y, _CloudP.x) * t * 1.7 + 5.2);
            float cov = _Cloud.a;
            float m = smoothstep(1 - cov, 1 - cov + 0.35, n * 0.75 + n2 * 0.35);
            float horizon = smoothstep(0.02, 0.22, d.z);
            float shade = saturate(0.55 + (n2 - 0.5) * 1.2);
            float3 cc = _Cloud.rgb * lerp(0.62, 1.15, shade);
            // clouds lit by the sun
            float sd = saturate(dot(d, _Sun.xyz));
            cc += _SunCol.rgb * pow(sd, 6) * 0.45 * _Sun.w;
            c = lerp(c, cc, m * horizon * 0.85);
        }
        float s = saturate(dot(d, _Sun.xyz));
        c += _SunCol.rgb * (pow(s, 350) * 1.6 + pow(s, 24) * 0.35 + pow(s, 4) * 0.08) * _Sun.w;
        c += _Weather.z * float3(0.55, 0.6, 0.75);   // lightning flash
        return c;
    }

    // World position on the water plane for stable ripples (assumes eye height 41 above the liquid).
    float2 PlanePos(float x, float y)
    {
        float dy = max(y + 0.5 - _ViewP.y, 0.5);
        float v = _ViewP.z / dy;                     // forward distance / eye height
        float u = (x + 0.5 - _ViewP.x) / dy;         // lateral distance / eye height
        float a = _Cam.w;
        float2 fwd = float2(cos(a), sin(a)), right = float2(sin(a), -cos(a));
        return _Cam.xy + (fwd * v + right * u) * 41.0;
    }

    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass // 0 world
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float x = floor(i.uv.y * _Frame.x), y = floor(i.uv.x * _Frame.y);
                float4 src = Fetch(x, y);
                int code = CodeOf(src.a);
                float3 c = src.rgb;
                if (code >= 249) return src;   // 2D / weapon (and any non-world code) untouched
                if (_Fx2.w > 0.5) // debug: G-buffer false colour
                {
                    if (code < 200) return float4((1 - code / 200.0).xxx, src.a);
                    if (code < 224) return float4(0, 0.25, 1, src.a);
                    if (code < 236) return float4(0.5, 0.5, 0.15, src.a);
                    if (code < 248) return float4(1, 0.45, 0, src.a);
                    return float4(0, 0.85, 1, src.a);
                }
                float z = ZOf(code);
                if (code == 248)
                {
                    c = SkyShade(x, y, c);
                    return float4(c, src.a);
                }

                // ---- liquids
                if (code >= 200 && code < 236 && _Fx.x > 0)
                {
                    float murky = code >= 224 ? 1.0 : 0.0;
                    // find the shore row above (first non-liquid pixel)
                    float stp = max(1.0, _Frame.y / 300.0);
                    float shore = _Win.y;
                    float yy = y;
                    bool found = false;
                    [loop] for (int k = 0; k < 64; k++)
                    {
                        yy -= stp;
                        if (yy < _Win.y) break;
                        int cc = CodeOf(Fetch(x, yy).a);
                        if (cc == 249 || cc == 255) continue;          // weapon / HUD in front: look through
                        if (!(cc >= 200 && cc < 236)) { found = true; break; }
                    }
                    if (found)
                    {
                        float lo = yy, hi = yy + stp;
                        [unroll] for (int r = 0; r < 3; r++)
                        {
                            float mid = floor((lo + hi) * 0.5);
                            int cm = CodeOf(Fetch(x, mid).a);
                            if ((cm >= 200 && cm < 236) || cm == 249 || cm == 255) hi = mid; else lo = mid;
                        }
                        shore = hi;
                    }
                    float2 P = PlanePos(x, y);
                    float t = _ViewP.w;
                    float2 w1 = float2(VNoise(P * 0.045 + t * float2(0.35, 0.2)), VNoise(P * 0.045 + 7.3 - t * float2(0.2, 0.31)));
                    float2 w2 = float2(VNoise(P * 0.13 - t * 0.6), VNoise(P * 0.13 + 3.1 + t * 0.5));
                    float2 n = (w1 - 0.5) * 1.2 + (w2 - 0.5) * 0.6;
                    if (_Weather.w > 0) // rain rings
                    {
                        float2 cell = floor(P * 0.05); float2 f = frac(P * 0.05) - 0.5;
                        float ph = frac(t * 0.8 + Hash(cell));
                        float ring = sin((length(f) - ph * 0.5) * 60.0) * smoothstep(0.5 * ph + 0.06, 0.5 * ph, length(f)) * (1 - ph);
                        n += ring * 0.8 * _Weather.w;
                    }
                    float dist = max(y + 0.5 - _ViewP.y, 1.0);
                    float amp = clamp(dist / _Frame.y * 18.0, 0.6, 6.0) * _Frame.y / 400.0;
                    float yr = shore - (y - shore) - 1.0 + n.y * amp;
                    float xr = x + n.x * amp * 1.5;
                    float3 refl;
                    float reflOk = 1.0;
                    if (yr < _Win.y || !found)
                        refl = SkyShade(x, max(_Win.y, _ViewP.y - (y - _ViewP.y)), _FogCol.rgb * 0.9);
                    else
                    {
                        float4 rs = Fetch(clamp(xr, _Win.x, _Win.x + _Win.z - 1), yr);
                        int rc = CodeOf(rs.a);
                        refl = rc == 248 ? SkyShade(xr, yr, rs.rgb) : rs.rgb;
                        if (rc == 255 || rc == 249 || (rc >= 200 && rc < 236)) reflOk = 0.0;   // no valid mirror source
                    }
                    float grazing = saturate(1.0 - dist / (_Win.w * 0.55));
                    float F = lerp(0.28, 0.78, grazing * grazing) * lerp(1.0, 0.35, murky) * reflOk;
                    float3 tint = lerp(float3(1, 1, 1), c / max(max(c.r, max(c.g, c.b)), 0.05), 0.45);
                    // dev6 bug fix: the ripple-displaced body sample must be liquid too — near the weapon or the
                    // HUD it used to pick their pixels, drawing wavy weapon-coloured fringes ("ripples on the weapon").
                    float4 bs = Fetch(x + n.x * 0.8, y + n.y * 0.5);
                    int bc = CodeOf(bs.a);
                    float3 body = (bc >= 200 && bc < 236) ? bs.rgb : c;
                    c = lerp(body * 0.85, refl * tint, F);
                    // sparkles + sun specular
                    float sn = VNoise(P * 0.35 + t * 1.3) * VNoise(P * 0.31 - t * 1.1 + 4.0);
                    float sp = smoothstep(0.62, 0.9, sn) * (0.35 + 0.65 * grazing);
                    c += _SunCol.rgb * sp * 0.55 * (0.3 + _Sun.w) * (1 - murky * 0.7);
                    // shore foam
                    float fd = (y - shore) / max(1.0, _Frame.y / 200.0);
                    if (found) c += float3(0.9, 0.95, 1.0) * saturate(1.0 - fd / 2.5) * 0.28 * (0.6 + 0.4 * VNoise(P * 0.2 + t)) * (1 - murky * 0.5);
                }
                else if (code >= 236 && _Fx.w > 0)
                {
                    float2 P = PlanePos(x, y);
                    float t = _ViewP.w;
                    float2 o = float2(VNoise(P * 0.06 + t * 0.4), VNoise(P * 0.06 + 5.0 - t * 0.35)) - 0.5;
                    float4 w = Fetch(x + o.x * 3.0, y + o.y * 2.0);
                    if (CodeOf(w.a) >= 236 && CodeOf(w.a) < 248) c = w.rgb;
                    float pulse = 1.18 + 0.14 * sin(t * 1.7 + (P.x + P.y) * 0.02) + 0.1 * VNoise(P * 0.1 + t);
                    c *= pulse;
                }

                // ---- dynamic lights
                if (_Fx.z > 0 && _LightCount > 0)
                {
                    float3 vp = float3((x + 0.5 - _ViewP.x) / _ViewP.z * z, (_ViewP.y - y - 0.5) / _ViewP.z * z, z);
                    float3 add = 0;
                    [loop] for (int L = 0; L < 8; L++)
                    {
                        if (L >= (int)_LightCount) break;
                        float3 dv = vp - _LightPos[L].xyz;
                        float r = _LightPos[L].w;
                        float att = saturate(1.0 - dot(dv, dv) / (r * r));
                        add += _LightCol[L].rgb * att * att;
                    }
                    // dev7: soft-saturated and much weaker (dev6 capped at 0.5 x 1.8 = up to x1.9 brightness,
                    // which the owner called "horribly strong"); intensity per source comes from WorldFx
                    add = add / (1.0 + add);
                    c += c * add * 1.2 + add * 0.025;
                }

                // ---- ambient occlusion (solid pixels)
                if (_Fx.y > 0 && code < 200)
                {
                    float rad = max(1.5, _Frame.y / 180.0) * clamp(260.0 / z, 0.6, 3.0);
                    float occ = 0;
                    [unroll] for (int k = 0; k < 8; k++)
                    {
                        float ang = k * 0.785398 + 0.39;
                        float2 o = float2(cos(ang), sin(ang)) * rad * (1.0 + (k & 1));
                        int cs = CodeOf(Fetch(x + o.x, y + o.y).a);
                        if (cs >= 200) continue;
                        float zs = ZOf(cs);
                        float dz = (z - zs) / z;
                        occ += saturate(dz * 6.0) * step(dz, 0.35);
                    }
                    c *= 1.0 - saturate(occ / 8.0) * _Fx.y;
                }

                // ---- fog + sun scattering
                if (_Fx2.x > 0)
                {
                    // dev7: the fog is lit like the scene (owner: "the fog is bad" — dev5 painted one flat haze
                    // colour over everything, so dark rooms turned light blue/red). Indoors the fog colour follows
                    // the local brightness (dark stays dark) and is thinner; under the open sky it is the full
                    // atmospheric colour with the sun glow (distant hills fade into the air).
                    float f = 1.0 - exp(-z * _FogCol.a);
                    float3 d = WorldDir(x, y);
                    float3 fc = _FogCol.rgb + _SunCol.rgb * pow(saturate(dot(d, _Sun.xyz)), 8) * 0.18 * _Sun.w;
                    float lum = dot(c, float3(0.3, 0.55, 0.15));
                    float lit = lerp(saturate(lum * 2.0 + 0.03), 1.0, _FogP.x);
                    c = lerp(c, fc * lit, f * lerp(0.5, 0.8, _FogP.x));
                }
                // dev7: rain/snow/embers/dust are 3D particles now (WorldFx, pass 4), not a screen pattern
                c *= 1.0 + _Weather.z * 0.35;
                return float4(c, src.a);
            }
            ENDCG
        }
        Pass // 1 sun rays (target is half size; source = world)
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                if (_SunScreen.z < 0.5) return 0;
                float x = i.uv.y * _Frame.x, y = i.uv.x * _Frame.y;
                float2 p = float2(x, y);
                float2 dirv = (_SunScreen.xy - p) / 20.0;
                float3 acc = 0; float wgt = 1.0;
                [unroll] for (int k = 0; k < 20; k++)
                {
                    float4 s = Fetch(p.x, p.y);
                    if (CodeOf(s.a) == 248 && InWindow(p.x, p.y))
                    {
                        float l = dot(s.rgb, float3(0.3, 0.55, 0.15));
                        acc += s.rgb * smoothstep(0.45, 0.95, l) * wgt;
                    }
                    wgt *= 0.93;
                    p += dirv;
                }
                float fall = saturate(1.0 - length(float2(x, y) - _SunScreen.xy) / _SunScreen.w);
                return float4(acc / 20.0 * _SunCol.a * fall * _SunCol.rgb * 0.9, 1);
            }
            ENDCG
        }
        Pass // 2 downsample
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float2 d = _MainTex_TexelSize.xy;
                return (_MainTex.Sample(sampler_MainTex, i.uv + float2(-d.x, -d.y)) + _MainTex.Sample(sampler_MainTex, i.uv + float2(d.x, -d.y))
                      + _MainTex.Sample(sampler_MainTex, i.uv + float2(-d.x, d.y)) + _MainTex.Sample(sampler_MainTex, i.uv + float2(d.x, d.y))) * 0.25;
            }
            ENDCG
        }
        Pass // 3 composite
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float x = floor(i.uv.y * _Frame.x), y = floor(i.uv.x * _Frame.y);
                float4 w = Fetch(x, y);
                int code = CodeOf(FetchG(x, y).a);
                float3 c = w.rgb;
                if (code < 249)
                {
                    if (_Fx2.y > 0 && code <= 248)
                    {
                        float z = ZOf(code);
                        float k = saturate((z - _Fx2.z) / (_Fx2.z * 2.0));
                        // dev6 bug fix: the blur is premultiplied by a "world pixel" mask (pass 5), so the weapon
                        // and the HUD no longer bleed into the far background as a bright/red halo around them.
                        float4 b = tex2D(_BlurTex, i.uv);
                        if (b.a > 0.02) c = lerp(c, b.rgb / b.a, k * 0.85);
                    }
                    c += tex2D(_RaysTex, i.uv).rgb;
                }
                return float4(c, w.a);
            }
            ENDCG
        }
        Pass // 4 particles: vertex colour, uv.x = view depth (map units), uv.yz = quad corner
        {
            Blend One One
            CGPROGRAM
            #pragma vertex pvert
            #pragma fragment pfrag
            struct pin { float4 vertex : POSITION; float4 color : COLOR; float3 uv : TEXCOORD0; };
            struct pv2f { float4 pos : SV_POSITION; float4 col : COLOR; float3 uv : TEXCOORD0; float4 sp : TEXCOORD1; };
            pv2f pvert(pin v)
            {
                pv2f o; o.pos = UnityObjectToClipPos(v.vertex); o.col = v.color; o.uv = v.uv;
                o.sp = ComputeScreenPos(o.pos);
                return o;
            }
            float4 pfrag(pv2f i) : SV_Target
            {
                float2 suv = i.sp.xy / i.sp.w;
                int code = CodeOf(FetchG(suv.y * _Frame.x, suv.x * _Frame.y).a);
                if (code >= 249) discard;
                if (code < 248 && ZOf(code) < i.uv.x) discard;   // behind geometry
                float2 q = i.uv.yz * 2.0 - 1.0;
                float m = saturate(1.0 - dot(q, q));
                return float4(i.col.rgb * i.col.a * m, 0);
            }
            ENDCG
        }
        Pass // 5 masked downsample (DoF source): premultiplied by "is a world pixel" (not weapon/HUD)
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float2 d = _MainTex_TexelSize.xy * 0.5;
                float4 acc = 0;
                [unroll] for (int k = 0; k < 4; k++)
                {
                    float2 o = float2((k & 1) ? d.x : -d.x, (k & 2) ? d.y : -d.y);
                    float4 s = _MainTex.SampleLevel(sampler_MainTex, i.uv + o, 0);
                    int cc = CodeOf(s.a);
                    float w = (cc == 249 || cc >= 250) ? 0.0 : 1.0;
                    acc += float4(s.rgb * w, w);
                }
                return acc * 0.25;
            }
            ENDCG
        }
        Pass // 6 dev8 solid particles: like pass 4 but alpha-blended (dark smoke / chips / blood); dst alpha kept
        {
            Blend One OneMinusSrcAlpha, Zero One
            CGPROGRAM
            #pragma vertex pvert
            #pragma fragment pfrag
            struct pin { float4 vertex : POSITION; float4 color : COLOR; float3 uv : TEXCOORD0; };
            struct pv2f { float4 pos : SV_POSITION; float4 col : COLOR; float3 uv : TEXCOORD0; float4 sp : TEXCOORD1; };
            pv2f pvert(pin v)
            {
                pv2f o; o.pos = UnityObjectToClipPos(v.vertex); o.col = v.color; o.uv = v.uv;
                o.sp = ComputeScreenPos(o.pos);
                return o;
            }
            float4 pfrag(pv2f i) : SV_Target
            {
                float2 suv = i.sp.xy / i.sp.w;
                int code = CodeOf(FetchG(suv.y * _Frame.x, suv.x * _Frame.y).a);
                if (code >= 249) discard;
                if (code < 248 && ZOf(code) < i.uv.x) discard;   // behind geometry
                float2 q = i.uv.yz * 2.0 - 1.0;
                float m = saturate(1.0 - dot(q, q));
                m = m * m * (3.0 - 2.0 * m);                      // soft round edge
                float a = saturate(i.col.a * m);
                return float4(i.col.rgb * a, a);
            }
            ENDCG
        }
    }
}
