// my-rekkr dev6 "Remaster" — GPU world shader. Faithful Doom look: palette-index textures from one
// atlas, COLORMAP lighting by sector light + distance (vanilla scalelight/zlight formulas, continuous
// like the dev3 smooth lighting), fake contrast on axis-aligned walls, extra light (gun flash), fixed
// colormaps (invulnerability / light amp), current damage/bonus palette. Alpha = dev5 G-buffer code.
// Passes: 0 opaque walls+flats, 1 masked (mid textures, things; alpha test), 2 sky, 3 sun shadow caster,
// 4 spectre fuzz.
// Vertex layout: see Engine/Remaster/RVertex.cs.
Shader "Rekkr/Remaster/RemasterWorld"
{
    Properties
    {
        _Atlas ("Atlas", 2D) = "black" {}
        _Lookup ("Lookup", 2D) = "black" {}
        _Sectors ("Sectors", 2D) = "black" {}
        _LitPal ("LitPal", 2D) = "black" {}
        _MaskCull ("Mask cull", Float) = 2
    }
    CGINCLUDE
    #include "UnityCG.cginc"
    #pragma target 3.5
    sampler2D _Atlas; float4 _AtlasSize;
    sampler2D _Lookup; float4 _LookupSize;
    sampler2D _Sectors; float4 _SectorsSize;
    sampler2D _LitPal;
    float4 _View;     // doom x, y, z, angle
    float4 _Proj;     // centerX, centerY (window px, sheared), projection (px), window height
    float4 _RTSize;   // w, h, 1/w, 1/h
    float4 _Sky;      // sky texture slot, row step per pixel, drift (rad), stretched (0/1)
    float4 _Light;    // extra light, fixed colormap, smooth (0/1), time
    sampler2D _ShadowMap;
    float4x4 _ShadowVP;   // world -> shadow clip (xy in -1..1, z = light depth in map units in w-less ortho)
    float4 _SunDir;       // unity world dir towards the sun, on (0/1)
    float4 _SunTint;      // rgb, shadow darkness (0..1)
    float4 _ShadowParams; // texel size (uv), bias (units), unused, unused

    struct appdata { float4 vertex : POSITION; float4 uv0 : TEXCOORD0; float4 uv1 : TEXCOORD1; };
    struct v2f
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
        nointerpolation float4 rect : TEXCOORD1;   // atlas x, y, w, h
        float fz : TEXCOORD2;                      // forward (view-space) distance, map units
        nointerpolation float4 info : TEXCOORD3;   // kind, lightnum (with contrast + extra), class/flags, outdoor (0/1)
        float4 sp : TEXCOORD4;                     // screen pos
        float3 wpos : TEXCOORD5;                   // unity world position
        nointerpolation float3 n : TEXCOORD6;      // surface normal (unity world)
    };

    float4 Slot(float s)
    {
        float2 c = float2(fmod(s, _LookupSize.x), floor(s / _LookupSize.x));
        return tex2Dlod(_Lookup, float4((c + 0.5) * _LookupSize.zw, 0, 0));
    }
    float2 SectorInfo(float s)   // light level, outdoor (sky ceiling) 0/1
    {
        float2 c = float2(fmod(s, _SectorsSize.x), floor(s / _SectorsSize.x));
        float4 t = tex2Dlod(_Sectors, float4((c + 0.5) * _SectorsSize.zw, 0, 0));
        return float2(t.r * 255.0, t.g);
    }

    v2f vert(appdata v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.uv0.xy;
        float slot = v.uv0.z;
        o.rect = slot >= 0 ? Slot(slot) : float4(0, 0, 1, 1);
        float2 d = v.vertex.xz - _View.xy;
        o.fz = d.x * cos(_View.w) + d.y * sin(_View.w);
        float kind = v.uv1.z;
        float lightnum = 0; float outdoor = 0;
        if (v.uv0.w >= 0)
        {
            float2 si = SectorInfo(v.uv0.w);
            lightnum = floor(si.x / 16.0) + _Light.x;
            outdoor = si.y;
            if (kind < 0.5) lightnum += v.uv1.w;          // wall fake contrast
        }
        o.info = float4(kind, lightnum, v.uv1.w, outdoor);
        o.sp = ComputeScreenPos(o.pos);
        o.wpos = v.vertex.xyz;
        o.n = kind < 0.5 ? float3(v.uv1.x, 0, v.uv1.y) : kind < 1.5 ? float3(0, 1, 0) : kind < 2.5 ? float3(0, -1, 0) : float3(v.uv1.x, 0.3, v.uv1.y);
        return o;
    }

    // COLORMAP row (continuous) for a light number and distance; flats use zlight, the rest scalelight
    float LightRow(float lightnum, float z, bool flat, bool fullBright)
    {
        if (_Light.y > 0.5) return _Light.y;          // fixed colormap (invulnerability 32, light amp 1)
        if (fullBright) return 0;
        float startmap = (15.0 - clamp(lightnum, 0, 15)) * 4.0;
        float zz = max(z, 1.0);
        float sub = flat ? 1280.0 / (zz + 16.0) : min(2560.0 / zz, 47.0) * 0.5;
        return clamp(startmap - sub, 0, 31);
    }

    float2 AtlasTexel(float2 uv, float4 rect)
    {
        float2 l = uv - rect.zw * floor(uv / rect.zw);
        return tex2Dlod(_Atlas, float4((rect.xy + floor(l) + 0.5) * _AtlasSize.zw, 0, 0)).rg;
    }

    float3 Lit(float idx, float row)
    {
        float r0 = floor(row);
        float3 c0 = tex2Dlod(_LitPal, float4((idx + 0.5) / 256.0, (r0 + 0.5) / 34.0, 0, 0)).rgb;
        if (_Light.z < 0.5 || row - r0 < 0.004 || r0 >= 31) return c0;
        float3 c1 = tex2Dlod(_LitPal, float4((idx + 0.5) / 256.0, (r0 + 1.5) / 34.0, 0, 0)).rgb;
        return lerp(c0, c1, row - r0);
    }

    // dev5 G-buffer depth code
    float DepthCode(float z)
    {
        if (z <= 8.0) return 0;
        return min(floor(log2(z / 8.0) * 19.9), 199);
    }
    float Code(float z, float cls)
    {
        float d = DepthCode(z);
        if (cls > 0.5 && cls < 1.5) return 200 + floor(d * 24.0 / 200.0);
        if (cls > 1.5 && cls < 2.5) return 224 + floor(d * 12.0 / 200.0);
        if (cls > 2.5 && cls < 3.5) return 236 + floor(d * 12.0 / 200.0);
        return d;
    }

    // dev6 stage 5: sun + shadow map for outdoor sectors (sky ceiling). 1 = unchanged Doom light.
    float3 SunLight(float3 wpos, float3 n, float kind)
    {
        float4 sc = mul(_ShadowVP, float4(wpos, 1));
        float2 uv = sc.xy * 0.5 + 0.5;
        if (uv.x <= 0 || uv.y <= 0 || uv.x >= 1 || uv.y >= 1) return 1;
        float ndl = dot(normalize(n), _SunDir.xyz);
        float facing = kind > 3.5 ? 0.6 : saturate(ndl);
        float bias = _ShadowParams.y * (1.5 + 2.0 * (1.0 - saturate(abs(ndl))));
        float d = sc.z - bias;
        float lit = 0;
        float2 o = _ShadowParams.xx;
        lit += step(d, tex2Dlod(_ShadowMap, float4(uv + float2(-0.5, -0.5) * o, 0, 0)).r);
        lit += step(d, tex2Dlod(_ShadowMap, float4(uv + float2(0.5, -0.5) * o, 0, 0)).r);
        lit += step(d, tex2Dlod(_ShadowMap, float4(uv + float2(-0.5, 0.5) * o, 0, 0)).r);
        lit += step(d, tex2Dlod(_ShadowMap, float4(uv + float2(0.5, 0.5) * o, 0, 0)).r);
        lit *= 0.25;
        if (ndl <= 0 && kind < 3.5) lit = 0;          // facing away from the sun = self-shadowed
        float3 shadowed = (1.0 - _SunTint.w).xxx;
        float3 sunny = 1.0 + _SunTint.rgb * 0.35 * facing;
        return lerp(shadowed, sunny, lit);
    }

    float4 Shade(v2f i, bool masked)
    {
        float2 t = AtlasTexel(i.uv, i.rect);
        if (masked) clip(t.g - 0.5);
        float kind = i.info.x;
        bool flat = kind > 0.5 && kind < 2.5;
        bool thing = kind > 3.5;
        bool fb = thing && i.info.z > 0.5 && i.info.z < 1.5;
        float row = LightRow(i.info.y, i.fz, flat, fb);
        float3 c = Lit(floor(t.r * 255.0 + 0.5), row);
        float cls = flat ? fmod(i.info.z, 8.0) : 0;
        if (_SunDir.w > 0.5 && i.info.w > 0.5 && !fb) c *= SunLight(i.wpos, i.n, kind);
        return float4(c, Code(i.fz, cls) / 255.0);
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass // 0 opaque walls + flats
        {
            Cull Off ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target { return Shade(i, false); }
            ENDCG
        }
        Pass // 1 masked: two-sided mid textures and things (alpha test)
        {
            Cull [_MaskCull] ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target { return Shade(i, true); }
            ENDCG
        }
        Pass // 2 sky: vanilla sky mapping from the pixel position (column = view angle, rows fixed)
        {
            Cull Off ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float2 p = i.sp.xy / i.sp.w * _RTSize.xy;       // window pixels, y up
                float x = floor(p.x), y = _RTSize.y - p.y;       // y down (frame rows); column angle at the pixel's left edge like xtoviewangle
                float ang = _View.w + atan2(_Proj.x - x, _Proj.z) + _Sky.z;
                float col = floor(frac(ang / 6.2831853) * 1024.0);
                float4 rect = Slot(_Sky.x);
                float row = 100.0 + (y - _Proj.y) * _Sky.y;
                float maxRow = min(rect.w, 128.0) - 1.0;
                if (_Sky.w > 0.5) row = clamp(row, 0, maxRow);
                float2 t = AtlasTexel(float2(col, row), rect);
                float3 c = tex2Dlod(_LitPal, float4((floor(t.r * 255.0 + 0.5) + 0.5) / 256.0, 0.5 / 34.0, 0, 0)).rgb;
                return float4(c, 248.0 / 255.0);
            }
            ENDCG
        }
        Pass // 3 shadow caster (opaque + masked + things): light-space depth into an RFloat target
        {
            Cull Off ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma vertex vshadow
            #pragma fragment fshadow
            struct sv2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; nointerpolation float4 rect : TEXCOORD1; float depth : TEXCOORD2; nointerpolation float masked : TEXCOORD3; };
            float4 _SunCasterMask;   // x: 1 = alpha-test
            sv2f vshadow(appdata v)
            {
                sv2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float4 sc = mul(_ShadowVP, float4(v.vertex.xyz, 1));
                o.depth = sc.z;
                o.uv = v.uv0.xy;
                o.rect = v.uv0.z >= 0 ? Slot(v.uv0.z) : float4(0, 0, 1, 1);
                o.masked = _SunCasterMask.x;
                return o;
            }
            float4 fshadow(sv2f i) : SV_Target
            {
                if (i.masked > 0.5) { float2 t = AtlasTexel(i.uv, i.rect); clip(t.g - 0.5); }
                return float4(i.depth, 0, 0, 1);
            }
            ENDCG
        }
        Pass // 4 spectre fuzz: darken what is behind with a flickering column pattern (keeps alpha)
        {
            Cull Off ZWrite Off ZTest LEqual
            Blend DstColor Zero
            ColorMask RGB
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float2 t = AtlasTexel(i.uv, i.rect);
                clip(t.g - 0.5);
                float2 p = i.sp.xy / i.sp.w * _RTSize.xy;
                float n = frac(sin(dot(floor(p * float2(1.0, 0.5)) + floor(_Light.w * 35.0), float2(12.9898, 78.233))) * 43758.5453);
                return float4((0.62 + 0.18 * n).xxx, 1);
            }
            ENDCG
        }
    }
}
