// PdfSpace adaptation: isolated namespace. Original copyright and license retained.
// Copyright 2024 BobLd
//
// Licensed under the Apache License, Version 2.0 (the "License").
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using SkiaSharp;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Graphics.Core;

namespace PdfSpace.Rendering.Skia.Helpers
{
    internal sealed class SKPaintCache : IDisposable
    {
        private readonly bool _isAntialias;

        // PdfSpace fix: retain full keys; hash collisions must never alias paint state.
        private readonly Dictionary<PaintKey, SKPaint> _cache = new();
        private readonly Dictionary<(bool, BlendMode, byte), SKPaint> _imagePaintCache = new();

#if DEBUG
        private readonly SKPaint _imageDebugPaint;
#endif

        public SKPaintCache(bool isAntialias, float minimumLineWidth)
        {
            _isAntialias = isAntialias;
            // minimumLineWidth not in use

#if DEBUG
            _imageDebugPaint = new SKPaint()
            {
                Style = SKPaintStyle.StrokeAndFill,
                Color = new SKColor(SKColors.IndianRed.Red, SKColors.IndianRed.Green, SKColors.IndianRed.Blue, 150),
                IsAntialias = _isAntialias,
                StrokeWidth = 2
            };
#endif
        }

        private readonly record struct PaintKey(SKColor Color, bool Stroke, float? Width,
            LineJoinStyle? Join, LineCapStyle? Cap, DashKey Dash, BlendMode Blend,
            SKBlendMode? Override, double Miter);

        // Avoid allocating for cache hits. Copy dash intervals only when storing a new key.
        private readonly struct DashKey : IEquatable<DashKey>
        {
            private readonly IReadOnlyList<double>? _lengths;
            private readonly int _phase;
            public DashKey(LineDashPattern? pattern)
            { _lengths = pattern?.Array; _phase = pattern?.Phase ?? 0; }
            private DashKey(IReadOnlyList<double>? lengths, int phase)
            { _lengths = lengths; _phase = phase; }
            public DashKey Snapshot()
            {
                if (_lengths is null || _lengths.Count == 0) return this;
                var copy = new double[_lengths.Count];
                for (var i = 0; i < copy.Length; i++) copy[i] = _lengths[i];
                return new(copy, _phase);
            }
            public bool Equals(DashKey other)
            {
                var count = _lengths?.Count ?? 0;
                if (_phase != other._phase || count != (other._lengths?.Count ?? 0)) return false;
                for (var i = 0; i < count; i++) if (_lengths![i] != other._lengths![i]) return false;
                return true;
            }
            public override bool Equals(object? other) => other is DashKey key && Equals(key);
            public override int GetHashCode()
            {
                var hash = new HashCode(); hash.Add(_phase);
                if (_lengths is not null) for (var i = 0; i < _lengths.Count; i++) hash.Add(_lengths[i]);
                return hash.ToHashCode();
            }
        }

        public SKPaint GetPaint(IColor? color, double alpha, bool stroke, float? strokeWidth, LineJoinStyle? joinStyle,
            LineCapStyle? capStyle, LineDashPattern? dashPattern, BlendMode blendMode, SKBlendMode? skBlendModeOverride = null, double miterLimit = 10)
        {
            color ??= RGBColor.Black;
            var key = new PaintKey(color.ToSKColor(alpha), stroke, strokeWidth, joinStyle,
                capStyle, new DashKey(dashPattern), blendMode, skBlendModeOverride, miterLimit);

            if (_cache.TryGetValue(key, out var paint))
            {
                return paint;
            }

            paint = new SKPaint()
            {
                IsAntialias = _isAntialias,
                Color = key.Color,
                Style = stroke ? SKPaintStyle.Stroke : SKPaintStyle.Fill,
                BlendMode = skBlendModeOverride ?? blendMode.ToSKBlendMode()
            };
            
            if (stroke)
            {
                // Careful - we assume they all have values if stroke!
                paint.StrokeWidth = (strokeWidth ?? throw new ArgumentException("Missing stroke width."));
                paint.StrokeJoin = (joinStyle ?? throw new ArgumentException("Missing line join.")).ToSKStrokeJoin();
                paint.StrokeCap = (capStyle ?? throw new ArgumentException("Missing line cap.")).ToSKStrokeCap();
                paint.StrokeMiter = (float)miterLimit;
                paint.PathEffect = (dashPattern ?? throw new ArgumentException("Missing dash pattern.")).ToSKPathEffect();
            }

            _cache[key with { Dash = key.Dash.Snapshot() }] = paint;

            return paint;
        }

        public SKPaint GetPaint(IPdfImage pdfImage, BlendMode blendMode, double alpha)
        {
            // PDF nonstroking alpha also applies to non-stencil images, in addition
            // to any per-image or graphics-state soft mask. Key by the actual byte
            // opacity used by Skia so cache hits cannot reuse another image's alpha.
            var opacity = (byte)Math.Round(Math.Clamp(double.IsFinite(alpha) ? alpha : 1, 0, 1) * 255);
            var key = (pdfImage.Interpolate, blendMode, opacity);

            if (_imagePaintCache.TryGetValue(key, out var paint))
            {
                return paint;
            }

            paint = new SKPaint
            {
                IsAntialias = pdfImage.Interpolate,
                Color = SKColors.White.WithAlpha(opacity),
                BlendMode = blendMode.ToSKBlendMode()
            };
            
            _imagePaintCache[key] = paint;

            return paint;
        }

#if DEBUG
        public SKPaint GetImageDebug()
        {
            return _imageDebugPaint;
        }
#endif

        public void Dispose()
        {
#if DEBUG
            _imageDebugPaint.Dispose();
#endif
            foreach (var pair in _cache)
            {
                pair.Value.PathEffect?.Dispose();
                pair.Value.Dispose();
            }
            _cache.Clear();

            foreach (var pair in _imagePaintCache)
            {
                pair.Value.Dispose();
            }
            _imagePaintCache.Clear();
        }
    }
}
