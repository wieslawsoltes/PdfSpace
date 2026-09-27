using PdfSpace.Core;
using PdfSpace.Documents;
using PdfSpace.Editing;
using PdfSpace.Layout;
using PdfSpace.Skia;
using SkiaSharp;

var passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); Console.WriteLine("PASS " + name); passed++; }
void Reject(Action action, string name) { try { action(); } catch { Check(true, name); return; } throw new Exception("FAILED: " + name); }
var session = new EditorSession(new PdfWorkspace());
Check(!session.IsDirty, "new session is clean");
var note = new Annotation { Kind = AnnotationKind.Note, Text = "Review the material choice", Bounds = new(100, 100, 24, 24) };
session.AddAnnotation(note); Check(session.Document.AnnotationCount == 1 && session.IsDirty, "add annotation transaction");
session.MarkSaved(); session.UpdateAnnotation(note.Id, a => a with { Text = "Updated" });
Check(session.IsDirty, "edit after save is dirty"); session.Undo(); Check(!session.IsDirty && session.SelectedAnnotation?.Text == note.Text, "undo to saved identity");
session.Redo(); Check(session.SelectedAnnotation?.Text == "Updated", "redo mutation");
session.Reply(note.Id, "Agreed"); Check(session.SelectedAnnotation?.Replies.Length == 1, "thread reply");
session.RotatePage(); Check(session.Page.Rotation == 90, "clockwise rotation"); session.RotatePage(-90); Check(session.Page.Rotation == 0, "counterclockwise rotation");
session.DuplicatePage(); Check(session.Document.Pages.Length == 2 && session.Page.Annotations[0].Id != note.Id, "duplicate with new identifiers");
session.MovePage(0); Check(session.CurrentPage == 0, "page reordering"); session.DeletePage(); Check(session.Document.Pages.Length == 1, "delete page"); Reject(session.DeletePage, "cannot delete final page");
session.InsertBlank(); Check(session.Page.SourceId is null && session.Document.Pages.Length == 2, "blank insertion");
var serialized = WorkspaceJson.Save(session.Document); var restored = WorkspaceJson.Load(serialized); Check(restored.Pages.Length == 2, "source-generated workspace roundtrip");
Reject(() => WorkspaceJson.Validate(restored with { FormatVersion = 9 }), "future workspace rejection");
Reject(() => WorkspaceJson.Validate(new PdfWorkspace { Pages = [new PdfPageState { Width = double.NaN }] }), "nonfinite dimensions rejected");
Reject(() => WorkspaceJson.Validate(new PdfWorkspace { Pages = [new PdfPageState { Rotation = 45 }] }), "non-quarter rotation rejected");
foreach (var rotation in new[] { 0, 90, 180, 270 })
{
    var page = new PdfPageState { Width = 600, Height = 800, Rotation = rotation, Crop = new(40, 80, 500, 620) };
    foreach (var point in new[] { new PointD(40, 80), new PointD(120, 280), new PointD(540, 700) })
        Check(PageGeometry.ToPage(page, PageGeometry.ToDisplay(page, point)).Distance(point) < .00001, $"crop rotation {rotation} roundtrip {point}");
    var placement = PageLayout.Arrange([page], 1200, 1.2, 70, -45)[0]; var original = new PointD(201, 351);
    Check(placement.ToPage(page, placement.ToScreen(page, original, 1.2), 1.2).Distance(original) < .00001, $"viewport transform {rotation}");
}
var pairs = PageLayout.Arrange([new(), new(), new()], 1400, 1, 0, 0, PageLayoutMode.TwoPage);
Check(pairs.Length == 3 && pairs[0].Bounds.Y == pairs[1].Bounds.Y && pairs[2].Bounds.Y > pairs[0].Bounds.Y, "two-page spread layout");
var demo = SampleDocument.Create(); Check(demo.Pages.Length == 6, "real sample PDF parses six pages");
Check(PdfReader.ExtractText(demo).Contains("Good ideas", StringComparison.Ordinal), "sample contains real searchable text");
Check(PdfReader.Find(demo, "materials").Any(), "cross-page full text search");
using var renderer = new PdfRenderer { CacheCapacity = 2 };
var png = renderer.ExportPng(demo, 0, .5); Check(png.Length > 1000, "Skia renders imported PDF to PNG");
using (var bitmap = SKBitmap.Decode(png)) Check(bitmap.Width == 298 && bitmap.Height == 421, "PNG pixel dimensions");
var edit = new EditorSession(demo); edit.AddAnnotation(new Annotation { Kind = AnnotationKind.Text, Text = "PDFSPACE TEST", Bounds = new(48, 310, 220, 24), Color = 0xFFDD3300 });
var output = renderer.ExportPdf(edit.Document); var imported = PdfReader.Open(output, "roundtrip.pdf");
Check(imported.Pages.Length == 6, "vector export PDF reopens"); Check(PdfReader.ExtractText(imported).Contains("PDFSPACE TEST"), "added text is in exported PDF content");
var extract = renderer.ExportPdf(demo, [2, 0]); Check(PdfReader.Open(extract, "extract.pdf").Pages.Length == 2, "page extraction exports selected pages");
edit.RotatePage(); edit.CropPage(new(20, 30, 500, 700));
var rotated = PdfReader.Open(renderer.ExportPdf(edit.Document, [0]), "rotated.pdf"); Check(Math.Abs(rotated.Pages[0].Width - 700) < 1 && Math.Abs(rotated.Pages[0].Height - 500) < 1, "crop and rotation exported dimensions");
Check(renderer.CachedPictureCount <= 2, "bounded picture cache");
Directory.CreateDirectory("artifacts/engine"); File.WriteAllBytes("artifacts/engine/sample.pdf", demo.Sources[0].Bytes); File.WriteAllBytes("artifacts/engine/sample.png", png); File.WriteAllBytes("artifacts/engine/edited.pdf", output);
Console.WriteLine($"\n{passed} engine checks passed.");
