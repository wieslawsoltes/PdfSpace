#!/usr/bin/env python3
"""Independent catalog-authoring fixture checks. Requires pypdf and PyMuPDF.

Uses actual native PDFs, not PdfSpace diagnostic state. Never executes payloads.
"""
from __future__ import annotations

import argparse
import hashlib
import io
from pathlib import Path

import fitz
from pypdf import PdfReader


class Verification:
    def __init__(self) -> None:
        self.checks = 0

    def check(self, condition: bool, message: str) -> None:
        if not condition:
            raise AssertionError(message)
        self.checks += 1
        print("PASS", message)

    def entries(self, document: PdfReader) -> list[tuple[str, dict]]:
        names = document.trailer["/Root"].get("/Names")
        if names is None:
            return []
        tree = names.get_object().get("/EmbeddedFiles")
        result: list[tuple[str, dict]] = []
        seen: set[int] = set()

        def visit(value: object, depth: int = 0) -> None:
            node = value.get_object()
            if depth > 16 or id(node) in seen:
                raise AssertionError("invalid fixture name tree")
            seen.add(id(node))
            if "/Names" in node:
                pairs = node["/Names"]
                if len(pairs) % 2:
                    raise AssertionError("odd fixture name pairs")
                keys = [bytes(key.original_bytes) for key in pairs[::2]]
                self.check(keys == sorted(keys), "native attachment name keys use byte ordering")
                result.extend((str(pairs[i]), pairs[i + 1].get_object()) for i in range(0, len(pairs), 2))
            for child in node.get("/Kids", []):
                visit(child, depth + 1)
        if tree is not None:
            visit(tree)
        return result

    def payload(self, entry: dict) -> bytes:
        stream = entry["/EF"]["/UF"]
        self.check(entry["/EF"].raw_get("/UF") == entry["/EF"].raw_get("/F"), "Unicode and fallback filenames share the same stream")
        data = stream.get_data()
        self.check(int(stream["/Params"]["/Size"]) == len(data), "native decoded Size is exact")
        return data

    def pages(self, original: bytes, changed: bytes, name: str) -> None:
        before, after = PdfReader(io.BytesIO(original)), PdfReader(io.BytesIO(changed))
        self.check(len(before.pages) == len(after.pages), name + " page count preserved")
        with fitz.open(stream=original, filetype="pdf") as source, fitz.open(stream=changed, filetype="pdf") as edited:
            for index, (old, new) in enumerate(zip(before.pages, after.pages)):
                self.check(old.get_contents().get_data() == new.get_contents().get_data(), f"{name} decoded source operators identical, page {index}")
                a, b = source[index].get_pixmap(alpha=False), edited[index].get_pixmap(alpha=False)
                self.check((a.width, a.height, a.samples) == (b.width, b.height, b.samples), f"{name} independently rendered pixels identical, page {index}")
                self.check(source[index].get_text() == edited[index].get_text(), f"{name} searchable native text unchanged, page {index}")

    def run(self, directory: Path, browser: Path | None) -> None:
        original = (directory / "attachment-edit-original.pdf").read_bytes()
        expected = "Native attachment — original.\n".encode()
        hashes = {file: hashlib.sha256(file.read_bytes()).digest() for file in directory.glob("attachment-edit-*.pdf")}
        for name in ("added", "replaced", "removed", "batch"):
            data = (directory / f"attachment-edit-{name}.pdf").read_bytes()
            entries = self.entries(PdfReader(io.BytesIO(data)))
            self.check(len(entries) == (0 if name == "removed" else 2 if name == "batch" else 1), name + " exact catalog count")
            if entries:
                entry = next(item for _, item in entries if str(item["/UF"]) == "raport-Żółć.txt")
                self.check(str(entry["/Desc"]) == ("Updated — 日本語" if name == "replaced" else "First description"), name + " Unicode description")
                self.check(str(entry["/EF"]["/UF"]["/Subtype"]) == "/text/plain", name + " native MIME name")
                self.check(self.payload(entry) == (b"Replacement payload\n" if name == "replaced" else expected), name + " exact payload bytes")
                if name == "batch":
                    compressed = next(item for _, item in entries if str(item["/UF"]) == "compressible.txt")
                    self.check(str(compressed["/EF"]["/UF"]["/Filter"]) == "/FlateDecode", "batch keeps profitable compression")
                    self.check(self.payload(compressed) == b"a" * 100000, "batch compressed payload lossless")
            self.pages(original, data, name)
        if browser:
            for name in ("added", "described", "replaced", "removed", "blank"):
                data = (browser / f"attachment-edit-browser-{name}.pdf").read_bytes()
                entries = self.entries(PdfReader(io.BytesIO(data)))
                self.check(len(entries) == (0 if name == "removed" else 1), "browser " + name + " catalog count")
                if entries:
                    entry = entries[0][1]
                    self.check(str(entry["/UF"]) == "attachment-edit-input.txt", "browser filename preserved: " + name)
                    self.check(str(entry["/Desc"]) == ("Initial — Żółć" if name in ("added", "blank") else "Reviewed — 日本語"), "browser Unicode description: " + name)
                    self.check(self.payload(entry) == (b"Replacement payload\n" if name == "replaced" else expected), "browser exact payload: " + name)
                if name != "blank":
                    self.pages(original, data, "browser " + name)
        self.check(all(hashlib.sha256(file.read_bytes()).digest() == digest for file, digest in hashes.items()), "verification left input files unchanged")
        print(f"{self.checks} independent attachment authoring assertions passed.")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--browser", type=Path)
    args = parser.parse_args()
    Verification().run(args.directory, args.browser)


if __name__ == "__main__":
    main()
