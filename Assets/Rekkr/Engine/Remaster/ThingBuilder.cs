// my-rekkr dev6 — things (monsters, items, decorations, projectiles) as camera-plane billboards, built
// every frame from the world state (read-only): same frame / 8-rotation choice, flip and patch offsets
// as vanilla R_ProjectSprite, interpolated positions, sector light, full-bright frames, spectre fuzz.
// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;

namespace ManagedDoom.Remaster
{
    public sealed class ThingBuilder
    {
        private readonly TextureAtlas atlas;
        private readonly ISpriteLookup sprites;
        public RVertex[] Vertices = new RVertex[4096];
        public int[] Indices = new int[6144];
        public int VertexCount, IndexCount, Count, FuzzIndexStart;
        private readonly List<int> fuzzQuads = new List<int>();

        /// <summary>Largest distance (map units) a sprite is lifted so its feet do not sink into the floor
        /// (true 3D clips them; vanilla draws sprites over the floor).</summary>
        public static float MaxLift = 24F;

        /// <summary>Optional hook: return true to skip the billboard of a thing (dev6 stage 6 extruded
        /// meshes draw it instead).</summary>
        public Func<Mobj, Patch, bool, float, float, float, bool> Replace;

        public ThingBuilder(TextureAtlas atlas, ISpriteLookup sprites) { this.atlas = atlas; this.sprites = sprites; }

        public void Build(World world, Fixed frac, float viewX, float viewY, float viewAngle, Mobj skip)
        {
            VertexCount = 0; IndexCount = 0; Count = 0; fuzzQuads.Clear();
            float ca = MathF.Cos(viewAngle), sa = MathF.Sin(viewAngle);
            float rx = sa, ry = -ca;   // camera right in Doom xy
            var vx = Fixed.FromFloat(viewX); var vy = Fixed.FromFloat(viewY);
            foreach (var thinker in world.Thinkers)
            {
                if (!(thinker is Mobj mo) || mo == skip) continue;
                if ((mo.Flags & MobjFlags.NoSector) != 0) continue;   // invisible (e.g. teleport fog targets)
                if (mo.State == null) continue;
                var x = mo.GetInterpolatedX(frac).ToFloat(); var y = mo.GetInterpolatedY(frac).ToFloat();
                var dx = x - viewX; var dy = y - viewY;
                var tz = dx * ca + dy * sa;
                if (tz < 4F) continue;   // MINZ
                var def = sprites[mo.Sprite];
                var fi = mo.Frame & 0x7FFF;
                if (def == null || fi >= def.Frames.Length || def.Frames[fi] == null) continue;
                var frame = def.Frames[fi];
                Patch patch; bool flip;
                if (frame.Rotate)
                {
                    var ang = Geometry.PointToAngle(vx, vy, mo.GetInterpolatedX(frac), mo.GetInterpolatedY(frac));
                    var rot = (ang - mo.Angle + new Angle(Angle.Ang45.Data / 2 * 9)).Data >> 29;
                    patch = frame.Patches[rot]; flip = frame.Flip[rot];
                }
                else { patch = frame.Patches[0]; flip = frame.Flip[0]; }
                if (patch == null) continue;
                var slot = atlas.SpriteSlot(patch);
                if (slot < 0) continue;
                var z = mo.GetInterpolatedZ(frac).ToFloat();
                var top = z + patch.TopOffset;
                var bottom = top - patch.Height;
                var floor = mo.Subsector.Sector.GetInterpolatedFloorHeight(frac).ToFloat();
                if (bottom < floor && z <= floor + 0.5F && floor - bottom <= MaxLift) { top += floor - bottom; bottom = floor; }
                var fullBright = (mo.Frame & 0x8000) != 0;
                var fuzz = (mo.Flags & MobjFlags.Shadow) != 0;
                if (Replace != null && Replace(mo, patch, flip, x, y, bottom)) { Count++; continue; }
                var x0 = -patch.LeftOffset; var x1 = patch.Width - patch.LeftOffset;
                float u0 = flip ? patch.Width : 0, u1 = flip ? 0 : patch.Width;
                Ensure(4, 6);
                var v = VertexCount;
                var sector = mo.Subsector.Sector.Number;
                var light = fullBright ? 1F : 0F;
                var kind = fuzz ? RKind.Fuzz : RKind.Thing;
                Vertices[v + 0] = new RVertex { X = x + rx * x0, Y = bottom, Z = y + ry * x0, U = u0, V = patch.Height, Tex = slot, Sector = sector, Kind = kind, Light = light, Nx = -ca, Nz = -sa };
                Vertices[v + 1] = new RVertex { X = x + rx * x0, Y = top, Z = y + ry * x0, U = u0, V = 0, Tex = slot, Sector = sector, Kind = kind, Light = light, Nx = -ca, Nz = -sa };
                Vertices[v + 2] = new RVertex { X = x + rx * x1, Y = top, Z = y + ry * x1, U = u1, V = 0, Tex = slot, Sector = sector, Kind = kind, Light = light, Nx = -ca, Nz = -sa };
                Vertices[v + 3] = new RVertex { X = x + rx * x1, Y = bottom, Z = y + ry * x1, U = u1, V = patch.Height, Tex = slot, Sector = sector, Kind = kind, Light = light, Nx = -ca, Nz = -sa };
                VertexCount += 4;
                if (fuzz) { fuzzQuads.Add(v); }
                else AddQuad(v);
                Count++;
            }
            FuzzIndexStart = IndexCount;
            foreach (var q in fuzzQuads) AddQuad(q);
        }

        private void AddQuad(int v)
        {
            Ensure(0, 6);
            var i = IndexCount;
            Indices[i] = v; Indices[i + 1] = v + 1; Indices[i + 2] = v + 2;
            Indices[i + 3] = v; Indices[i + 4] = v + 2; Indices[i + 5] = v + 3;
            IndexCount += 6;
        }

        private void Ensure(int verts, int idx)
        {
            if (VertexCount + verts > Vertices.Length) Array.Resize(ref Vertices, Vertices.Length * 2);
            if (IndexCount + idx > Indices.Length) Array.Resize(ref Indices, Indices.Length * 2);
        }
    }
}
