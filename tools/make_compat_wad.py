#!/usr/bin/env python3
"""Build rekkr-compat.wad: the only two Doom-IWAD wall patches that REKKR.wad textures
actually used by its maps reference but REKKR.wad does not ship (WALL54_1 under SW1CMT,
W65B_1 under FIREBLU1/KS_W10 on E1M7). Taken from Freedoom Phase 1 (BSD-3-Clause),
the free stand-in for doom.wad that REKKR's author recommends.
Usage: make_compat_wad.py <freedoom1.wad> <out.wad>"""
import struct, sys
NEEDED = ["WALL54_1", "W65B_1"]
src, out = sys.argv[1], sys.argv[2]
d = open(src, "rb").read()
_, n, o = struct.unpack("<4sii", d[:12])
lumps = {}
for i in range(n):
    fp, sz = struct.unpack("<ii", d[o+16*i:o+16*i+8])
    nm = d[o+16*i+8:o+16*i+16].split(b"\0")[0].decode("latin1").upper()
    lumps.setdefault(nm, d[fp:fp+sz])
entries = [("P_START", b"")] + [(x, lumps[x]) for x in NEEDED] + [("P_END", b"")]
body = b""; dirs = b""; off = 12
for nm, data in entries:
    dirs += struct.pack("<ii8s", off if data else 0, len(data), nm.encode())
    body += data; off += len(data)
open(out, "wb").write(b"PWAD" + struct.pack("<ii", len(entries), 12 + len(body)) + body + dirs)
print("wrote", out, [x for x, _ in entries])
