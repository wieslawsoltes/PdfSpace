#!/usr/bin/env python3
"""Locate and collect the actual Uno WebAssembly publish root; never substitute a fallback app."""
import argparse, json, os, shutil
from pathlib import Path
parser = argparse.ArgumentParser(); parser.add_argument('publish', type=Path); parser.add_argument('output', type=Path); args = parser.parse_args()
candidates = sorted(args.publish.rglob('index.html'), key=lambda p: len(p.parts))
if not candidates: raise SystemExit('The Uno publish output does not contain index.html.')
source = candidates[0].parent
args.output.mkdir(parents=True, exist_ok=True); shutil.copytree(source, args.output, dirs_exist_ok=True)
(args.output / '.nojekyll').touch()
(args.output / 'build-info.json').write_text(json.dumps({'application': 'PdfSpace', 'host': 'Uno WebAssembly', 'version': os.environ.get('VERSION', '0.1.0-alpha.1'), 'commit': os.environ.get('GITHUB_SHA', 'local')}))
print('Collected', source, 'into', args.output)
