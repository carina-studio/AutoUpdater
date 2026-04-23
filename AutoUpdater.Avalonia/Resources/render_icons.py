#!/usr/bin/env python3
"""
render_icons.py - generate AutoUpdater icon outputs.

Pipeline per target size:
  1. cairosvg renders the source SVG directly at the target pixel size -
     no 1024-px intermediate, so any internal stroke widths and curves
     scale proportionally and small sizes stay sharp.
  2. macOS uses AutoUpdater.Avalonia.svg (gradient background, square PNG;
     the OS applies the squircle mask at runtime).
  3. Windows + Linux share the same .ico built from AutoUpdater.Avalonia.NoBg.svg
     (transparent background).
  4. .icns built via the icnsutil package; .ico packed manually via struct
     packing (Pillow's native ICO save is unreliable for multi-size files).
"""

import io, os, struct, shutil
import cairosvg
import icnsutil
from PIL import Image

_HERE   = os.path.dirname(os.path.abspath(__file__))
RES_DIR = _HERE
APP_DIR = os.path.dirname(_HERE)

SVG_MACOS = os.path.join(RES_DIR, 'AutoUpdater.Avalonia.svg')
SVG_TRANS = os.path.join(RES_DIR, 'AutoUpdater.Avalonia.NoBg.svg')

WIN_SIZES = [16, 32, 48, 64, 128, 256]

ICONSET_FILES = {
    'icon_16x16.png':      16,
    'icon_16x16@2x.png':   32,
    'icon_32x32.png':      32,
    'icon_32x32@2x.png':   64,
    'icon_128x128.png':    128,
    'icon_128x128@2x.png': 256,
    'icon_256x256.png':    256,
    'icon_256x256@2x.png': 512,
    'icon_512x512.png':    512,
    'icon_512x512@2x.png': 1024,
}


def render_svg(svg_path, size):
    with open(svg_path) as f:
        svg = f.read()
    png = cairosvg.svg2png(
        bytestring=svg.encode(),
        output_width=size,
        output_height=size,
    )
    return Image.open(io.BytesIO(png)).convert('RGBA')


def write_icns(iconset_dir, icns_path):
    icns = icnsutil.IcnsFile()
    for fname in ICONSET_FILES:
        icns.add_media(file=os.path.join(iconset_dir, fname))
    icns.write(icns_path)


def save_ico(imgs_by_size, path):
    sizes = sorted(imgs_by_size)
    blobs = []
    for s in sizes:
        buf = io.BytesIO()
        imgs_by_size[s].save(buf, format='PNG')
        blobs.append(buf.getvalue())

    n      = len(sizes)
    header = struct.pack('<HHH', 0, 1, n)
    offset = 6 + n * 16
    dirs   = b''
    for s, blob in zip(sizes, blobs):
        w = s if s < 256 else 0   # 0 encodes 256 in the ICO spec
        dirs += struct.pack('<BBBBHHII', w, w, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)

    with open(path, 'wb') as f:
        f.write(header + dirs + b''.join(blobs))
    print(f'  wrote {path}  ({len(sizes)} sizes)')


def main():
    iconset_dir = os.path.join(RES_DIR, 'AutoUpdater.Avalonia.iconset')
    os.makedirs(iconset_dir, exist_ok=True)
    for stale in os.listdir(iconset_dir):
        if stale.endswith('.png'):
            os.remove(os.path.join(iconset_dir, stale))

    print('=== macOS iconset ===')
    for fname, px in ICONSET_FILES.items():
        render_svg(SVG_MACOS, px).save(os.path.join(iconset_dir, fname))
    print(f'  wrote {iconset_dir}/  ({len(ICONSET_FILES)} files)')

    print('=== macOS .icns ===')
    icns_path = os.path.join(RES_DIR, 'AutoUpdater.Avalonia.icns')
    write_icns(iconset_dir, icns_path)
    print(f'  wrote {icns_path}')

    print('=== Windows + Linux .ico ===')
    ico_imgs = {s: render_svg(SVG_TRANS, s) for s in WIN_SIZES}
    ico_path = os.path.join(RES_DIR, 'AutoUpdater.Avalonia.ico')
    save_ico(ico_imgs, ico_path)

    print('=== Copy to project root ===')
    shutil.copy(icns_path, os.path.join(APP_DIR, 'AutoUpdater.Avalonia.icns'))
    shutil.copy(ico_path,  os.path.join(APP_DIR, 'AutoUpdater.Avalonia.ico'))
    print(f'  copied .icns + .ico -> {APP_DIR}/')

    print('\nDone.')


if __name__ == '__main__':
    main()
