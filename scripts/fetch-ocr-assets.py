#!/usr/bin/env python3
"""Fetch pinned OCR code/models at build time; verify input archives and every extracted asset."""
import hashlib, io, json, pathlib, tarfile, urllib.request
root = pathlib.Path(__file__).resolve().parents[1]
out = root / 'src/PdfSpace.App/Platforms/WebAssembly/OcrAssets'
inputs = json.loads((root / 'scripts/ocr-inputs.json').read_text())
manifest = json.loads((root / 'scripts/ocr-assets.json').read_text())
manifest.pop('inputs.json', None)
def verified(data, expected, name):
    if hashlib.sha256(data).hexdigest() != expected:
        raise RuntimeError(f'OCR asset hash mismatch: {name}; review upstream changes before updating pins.')
    return data
if not all((out / name).is_file() and hashlib.sha256((out / name).read_bytes()).hexdigest() == digest for name, digest in manifest.items()):
    out.mkdir(parents=True, exist_ok=True)
    for source in inputs:
        with urllib.request.urlopen(urllib.request.Request(source['url'], headers={'User-Agent': 'PdfSpace-build'}), timeout=60) as response:
            data = verified(response.read(64 * 1024 * 1024), source['sha256'], source['url'])
        if source['url'].endswith('.tgz'):
            folder = 'core' if 'tesseract.js-core' in source['url'] else 'runtime'
            with tarfile.open(fileobj=io.BytesIO(data), mode='r:gz') as archive:
                for item in archive.getmembers():
                    relative = folder + '/' + pathlib.PurePosixPath(item.name).name
                    if item.isfile() and relative in manifest:
                        destination = out / relative; destination.parent.mkdir(parents=True, exist_ok=True)
                        destination.write_bytes(verified(archive.extractfile(item).read(), manifest[relative], relative))
        else:
            relative = 'lang/' + source['url'].rsplit('/', 1)[1]
            destination = out / relative; destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(verified(data, manifest[relative], relative))
for name, digest in manifest.items():
    verified((out / name).read_bytes(), digest, name)
(out / 'asset-manifest.json').write_text(json.dumps(manifest, indent=2))
print(f'Verified {len(manifest)} self-hosted OCR assets: Tesseract.js 7.0.0, eng/pol/deu fast models 4.1.0.')
