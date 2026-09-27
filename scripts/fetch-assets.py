#!/usr/bin/env python3
"""Fetch and verify the exact OFL-licensed font assets used by the application."""
from pathlib import Path
from urllib.request import Request, urlopen
import hashlib
root = Path(__file__).resolve().parents[1]
output = root / 'src/PdfSpace.App/Assets/Fonts'
output.mkdir(parents=True, exist_ok=True)
base = 'https://raw.githubusercontent.com/google/fonts/main/ofl/notosans/'
assets = [
    ('NotoSans%5Bwdth,wght%5D.ttf', 'NotoSans.ttf', 'bfb7bb691513f12e734dc346c03a03f784912432d7e3fa8e56efcf906fe86b3d'),
    ('OFL.txt', 'OFL.txt', 'cee9892f9f0cc8fe882c9e9537ee6a89621d86ee7ceaf70b02e2b2b1c25c061a')
]
for remote, local, expected in assets:
    destination = output / local
    if destination.exists():
        data = destination.read_bytes()
    else:
        with urlopen(Request(base + remote, headers={'User-Agent': 'PdfSpace-build'}), timeout=45) as response:
            data = response.read()
    actual = hashlib.sha256(data).hexdigest()
    if actual != expected:
        raise RuntimeError(f'{local}: upstream asset changed; review and deliberately update its pinned hash. Expected {expected}, received {actual}.')
    destination.write_bytes(data)
    print(local, len(data), 'sha256=' + actual)
