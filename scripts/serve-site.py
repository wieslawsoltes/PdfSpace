#!/usr/bin/env python3
"""Serve the publish root at the same /PdfSpace/ base path used by GitHub Pages."""
from http.server import ThreadingHTTPServer, SimpleHTTPRequestHandler
from functools import partial
import argparse
parser = argparse.ArgumentParser(); parser.add_argument('--directory', default='artifacts/site'); parser.add_argument('--port', type=int, default=4173); args = parser.parse_args()
class Handler(SimpleHTTPRequestHandler):
    extensions_map = {**SimpleHTTPRequestHandler.extensions_map, '.wasm': 'application/wasm', '.js': 'application/javascript', '.json': 'application/json', '.dat': 'application/octet-stream'}
    def do_GET(self):
        if self.path.startswith('/PdfSpace/'):
            self.path = '/' + self.path[len('/PdfSpace/'):]
        super().do_GET()
ThreadingHTTPServer(('127.0.0.1', args.port), partial(Handler, directory=args.directory)).serve_forever()
