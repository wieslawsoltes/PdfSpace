#!/usr/bin/env python3
"""Independent native attachment fixture/download checks. Requires pypdf, never executes payloads."""
import argparse
from pathlib import Path
from pypdf import PdfReader

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('fixtures', type=Path)
parser.add_argument('--downloads', type=Path)
args = parser.parse_args()
reader = PdfReader(args.fixtures / 'attachments-sample.pdf')
pairs = reader.trailer['/Root']['/Names']['/EmbeddedFiles']['/Names']
expected = [b'PdfSpace attachment\nNo external requests.\n',
            'Zażółć gęślą jaźń — embedded Unicode.\n'.encode(),
            b'<!doctype html><title>Not executed</title>']
assert len(pairs) == 6
for i, data in enumerate(expected):
    spec = pairs[2 * i + 1].get_object()
    stream = spec['/EF']['/UF']
    assert stream.get_data() == data
    assert stream['/Params']['/Size'] == len(data)
assert pairs[3].get_object()['/UF'] == 'raport-Żółć.txt'
assert pairs[5].get_object()['/UF'] == '../../preview.html'
many = PdfReader(args.fixtures / 'attachments-many.pdf')
entries = many.trailer['/Root']['/Names']['/EmbeddedFiles']['/Names']
assert len(entries) == 130
assert entries[-1].get_object()['/EF']['/UF'].get_data() == expected[0]
print('PASS 11 independent pypdf fixture assertions')
if args.downloads:
    for name, data in zip(['attachment-browser-plain.txt', 'attachment-browser-unicode.txt',
                           'attachment-browser-active.download', 'attachment-browser-last.txt'],
                          [*expected, expected[0]]):
        assert (args.downloads / name).read_bytes() == data
    print('PASS 4 independently matched browser download payloads')
