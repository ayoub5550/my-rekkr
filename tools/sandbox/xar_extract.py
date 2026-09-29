import struct, zlib, sys, os, re, gzip, io
import xml.etree.ElementTree as ET
pkg, out = sys.argv[1], sys.argv[2]
f = open(pkg,'rb')
magic, hsize, ver, toc_c, toc_u, ck = struct.unpack('>4sHHQQI', f.read(28))
assert magic == b'xar!'
f.seek(hsize); toc = zlib.decompress(f.read(toc_c)); heap = hsize + toc_c
root = ET.fromstring(toc)
def walk(node, path):
    for fe in node.findall('file'):
        name = fe.find('name').text; p = path + '/' + name
        d = fe.find('data')
        if d is not None:
            off = int(d.find('offset').text); ln = int(d.find('length').text)
            enc = d.find('encoding').get('style')
            print(p, ln, enc)
            if name == 'Payload':
                f.seek(heap+off); raw = f.read(ln)
                if 'gzip' in enc: raw = zlib.decompress(raw)
                yield p, raw
        yield from walk(fe, p)
for p, raw in walk(root.find('toc'), ''):
    # payload itself is gzip'd cpio (odc)
    if raw[:2] == b'\x1f\x8b': raw = gzip.decompress(raw)
    bio = io.BytesIO(raw); n=0
    while True:
        hdr = bio.read(76)
        if len(hdr) < 76 or hdr[:6] != b'070707': break
        mode = int(hdr[18:24], 8); namesize = int(hdr[59:65], 8); fsize = int(hdr[65:76], 8)
        name = bio.read(namesize)[:-1].decode()
        data = bio.read(fsize)
        if name == 'TRAILER!!!': break
        name = os.path.normpath(name).lstrip('./')
        if name.startswith('..') or name.startswith('/'): continue
        dst = os.path.join(out, name)
        t = mode & 0o170000
        if t == 0o040000: os.makedirs(dst, exist_ok=True)
        elif t == 0o120000:
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            if os.path.lexists(dst): os.remove(dst)
            os.symlink(data.decode(), dst)
        elif t == 0o100000:
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            open(dst,'wb').write(data); os.chmod(dst, mode & 0o777 | 0o600); n+=1
    print('extracted files', n)
