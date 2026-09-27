#!/usr/bin/env python3
"""Fetch OFL-licensed Noto Sans from the upstream Google Fonts repository."""
from pathlib import Path
from urllib.request import Request, urlopen
import hashlib
root = Path(__file__).resolve().parents[1]
output = root / 'src/PdfSpace.App/Assets/Fonts'
output.mkdir(parents=True, exist_ok=True)
base = 'https://raw.githubusercontent.com/google/fonts/main/ofl/notosans/'
for remote, local in [('NotoSans%5Bwdth,wght%5D.ttf', 'NotoSans.ttf'), ('OFL.txt', 'OFL.txt')]:
    destination = output / local
    if destination.exists():
        continue
    with urlopen(Request(base + remote, headers={'User-Agent': 'PdfSpace-build'}), timeout=45) as response:
        data = response.read()
    if not data:
        raise RuntimeError('The upstream font response was empty.')
    destination.write_bytes(data)
    print(local, len(data), 'sha256=' + hashlib.sha256(data).hexdigest())
