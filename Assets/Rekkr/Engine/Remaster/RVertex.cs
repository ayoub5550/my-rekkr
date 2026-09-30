// my-rekkr dev6 "Remaster" — GPU 3D renderer, Unity-free part (also compiled by tools/HeadlessTest).
//
// Coordinate convention (used everywhere in dev6):
//   Unity world position = (doomX, doomZ, doomY) in map units. Doom is right-handed (x east, y north,
//   z up); swapping y/z gives Unity's left-handed y-up space, so north = +Unity z and the Doom angle θ
//   (0 = east, counter-clockwise) has forward = (cos θ, 0, sin θ). Heights are NOT scaled: the 1.2
//   pixel aspect is applied when the frame is shown, exactly like the software renderer.
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Runtime.InteropServices;

namespace ManagedDoom.Remaster
{
    /// <summary>One vertex of the level / thing meshes (48 bytes, blittable, same layout as the Unity mesh:
    /// Position float3, TexCoord0 float4, TexCoord1 float4).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RVertex
    {
        public float X, Y, Z;          // Unity world position (doom x, doom z, doom y)
        public float U, V;             // texel coordinates inside the texture (wrap/tile in the shader)
        public float Tex;              // texture slot (TextureAtlas.Slot*), -1 = none
        public float Sector;           // sector number (light lookup); -1 = fixed light (see Light)
        public float Nx, Nz;           // horizontal normal (Unity x/z); 0,0 for flats (normal = ±y by Kind)
        public float Kind;             // RKind
        public float Light;            // walls: fake-contrast delta (-1/0/+1 light levels); flats: GBuffer flat class;
                                       // things: 1 = full bright; >= 2: light level (fixed, e.g. weapon) + 2

        public const int Size = 48;
    }

    public static class RKind
    {
        public const float Wall = 0, Floor = 1, Ceiling = 2, Sky = 3, Thing = 4, Fuzz = 5;
    }
}
