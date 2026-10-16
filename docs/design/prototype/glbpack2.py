import base64, json, struct, os
KITS = {
  'factory-kit': ['arrow','box-small','box-large','box-wide','box-long','conveyor-junction-t','conveyor-stripe','floor','machine','machine-window','hopper-round','pipe-large-long','robot-arm-a','screen-hanging-wide','screen-panel-flat','scanner-low','structure-wall','structure-window-wide','structure-corner-outer','structure-doorway-wide','door-wide-open','structure-yellow-short','structure-short','warning-traffic','cone','indicator-special-area','catwalk-straight'],
  'car-kit': ['delivery','truck','van','truck-flat','cone-flat'],
  'mini-characters': ['character-male-a'],
  'city-kit-industrial': ['shipping-container-a','shipping-container-b','shipping-container-c','water-tower','chimney-medium','building-a','building-c','building-h','detail-tank'],
  'nature-kit': [],
}
def pad4(b, ch=b'\x00'): return b + ch * ((4 - len(b) % 4) % 4)
def repack(data, png):
    clen, = struct.unpack('<I', data[12:16]); js = json.loads(data[20:20+clen]); off = 20 + clen
    blen, = struct.unpack('<I', data[off:off+4]); binc = data[off+8:off+8+blen]
    imgs = js.get('images', [])
    if not imgs or 'uri' not in imgs[0]: return data
    binc = pad4(binc); start = len(binc); binc += png
    js.setdefault('bufferViews', []).append({'buffer': 0, 'byteOffset': start, 'byteLength': len(png)})
    for im in imgs: im.pop('uri', None); im['bufferView'] = len(js['bufferViews']) - 1; im['mimeType'] = 'image/png'
    binc = pad4(binc); js['buffers'][0]['byteLength'] = len(binc)
    jb = pad4(json.dumps(js, separators=(',', ':')).encode(), b' ')
    body = struct.pack('<II', len(jb), 0x4E4F534A) + jb + struct.pack('<II', len(binc), 0x004E4942) + binc
    return struct.pack('<III', 0x46546C67, 2, 12 + len(body)) + body
entries = []; total = 0
for kit, names in KITS.items():
    base = f'kenney-dl/{kit}/x/Models/GLB format'
    png_path = f'{base}/Textures/colormap.png'; png = open(png_path, 'rb').read() if os.path.exists(png_path) else None
    for n in names:
        p = f'{base}/{n}.glb'
        if not os.path.exists(p): print('MISSING', p); continue
        g = open(p, 'rb').read(); g = repack(g, png) if png else g
        entries.append(f'"{n}":"{base64.b64encode(g).decode()}"'); total += len(g); print(f'{n:26s} {len(g):8d}')
open('pf3d/models.js', 'w').write('window.PF_MODELS={' + ','.join(entries) + '};\n')
print('total bytes', total, 'models', len(entries))
