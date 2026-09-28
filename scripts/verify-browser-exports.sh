#!/usr/bin/env bash
# Reopen real browser downloads with the independent native backend.
set -euo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
DIRECTORY="${1:-artifacts/browser-exports}"
test -d "$DIRECTORY"
dotnet build tests/PdfSpace.Tests -c Release
for mode in security ocr objects photos mixed paint; do
  dotnet run --project tests/PdfSpace.Tests -c Release --no-build -- "--verify-browser-$mode" "$DIRECTORY"
done
