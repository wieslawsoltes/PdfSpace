#!/usr/bin/env python3
"""Fetch the exact QPDF WASM runtime and retain its upstream licenses and notices."""
import base64
import hashlib
import io
import json
import tarfile
from pathlib import Path
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / 'src/PdfSpace.App/Platforms/WebAssembly/PdfSecurity'
ARCHIVE = 'https://registry.npmjs.org/@neslinesli93/qpdf-wasm/-/qpdf-wasm-0.3.0.tgz'
INTEGRITY = 'ObyTmabopTMwN6AGwrEkmDbdmM5wnwlRdn9jnJYQ2KbBUPdPDcfNf1QkcWFOVD+qIgSqILz9nK51e22T+x+gkQ=='
ASSETS = {
    'qpdf.js': 'c0e8fe62e0c3385dd8cb5d6b613f74d87a4138a3a3343e2add45a067a14d0884',
    'qpdf.wasm': 'abd933f4ccace4f732999381b21aec8b7e3726f18a5b167fafd57f88dd440876',
}
QPDF = 'https://raw.githubusercontent.com/qpdf/qpdf/856d32c610334855d30e96d25eb5f9636fb62f08/'
JPEG = 'https://raw.githubusercontent.com/ImageMagick/jpeg-turbo/7aa2a898c564041a24b09d0a6e780aaa632d08d3/'
LICENSES = {
    'QPDF-LICENSE.txt': QPDF + 'LICENSE.txt',
    'QPDF-NOTICE.md': QPDF + 'NOTICE.md',
    'zlib-README.txt': 'https://raw.githubusercontent.com/madler/zlib/21767c654d31d2dccdde4330529775c6c5fd5389/README',
    'jpeg-turbo-LICENSE.md': JPEG + 'LICENSE.md',
    'jpeg-turbo-README.ijg': JPEG + 'README.ijg',
}


def download(url, limit=16 * 1024 * 1024):
    with urlopen(Request(url, headers={'User-Agent': 'PdfSpace-build'}), timeout=45) as response:
        data = response.read(limit + 1)
    if not data or len(data) > limit:
        raise RuntimeError('Invalid security asset download size: ' + url)
    return data


def fetch():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    if not all((OUTPUT / name).exists() for name in ASSETS):
        archive = download(ARCHIVE)
        if base64.b64encode(hashlib.sha512(archive).digest()).decode() != INTEGRITY:
            raise RuntimeError('The QPDF npm archive does not match its pinned SHA-512 integrity.')
        # Read only fixed members. Never extract package paths or symlinks into the source tree.
        with tarfile.open(fileobj=io.BytesIO(archive), mode='r:gz') as package:
            for name in ASSETS:
                member = package.getmember('package/dist/' + name)
                if not member.isfile() or member.size > 16 * 1024 * 1024:
                    raise RuntimeError('Invalid QPDF package member: ' + name)
                data = package.extractfile(member).read()
                if hashlib.sha256(data).hexdigest() != ASSETS[name]:
                    raise RuntimeError('QPDF runtime hash mismatch: ' + name)
                (OUTPUT / name).write_bytes(data)
            (OUTPUT / 'qpdf-wasm-package.json').write_bytes(package.extractfile('package/package.json').read())
    for name, expected in ASSETS.items():
        if hashlib.sha256((OUTPUT / name).read_bytes()).hexdigest() != expected:
            raise RuntimeError('The cached QPDF runtime was modified: ' + name)
    for name, url in LICENSES.items():
        (OUTPUT / name).write_bytes(download(url, 1024 * 1024))
    (OUTPUT / 'runtime-manifest.json').write_text(json.dumps({
        'package': '@neslinesli93/qpdf-wasm', 'version': '0.3.0', 'qpdf': '12.2.0',
        'archive': ARCHIVE, 'integrity': 'sha512-' + INTEGRITY, 'sha256': ASSETS,
        'licenses': LICENSES,
    }, indent=2) + '\n')
    print('Verified QPDF 12.2.0 WebAssembly runtime and retained dependency notices.')


if __name__ == '__main__':
    fetch()
