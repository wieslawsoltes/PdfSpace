using PdfSpace.Core;
using PdfSpace.Documents;
using SkiaSharp;
namespace PdfSpace.Skia;

public static class SampleDocument
{
    public static PdfWorkspace Create(SKTypeface? typeface = null)
    {
        typeface ??= SKTypeface.Default; using var stream = new MemoryStream();
        using (var pdf = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata { Title = "Circular futures — Fieldwork report", Author = "Fieldwork Studio", Creator = "PdfSpace" }))
        {
            for (var page = 0; page < 6; page++)
            {
                var c = pdf.BeginPage(595, 842); c.Clear(SKColors.White);
                void Text(string value, double x, double y, double size = 12, string color = "#28332F", double width = 490) => AnnotationPainter.DrawText(c, value, x, y, size, typeface, SKColor.Parse(color), width);
                void Box(float x, float y, float w, float h, string color) { using var paint = new SKPaint { Color = SKColor.Parse(color), IsAntialias = true }; c.DrawRect(x, y, w, h, paint); }
                void Line(float y) => Box(48, y, 499, 1, "#DCE2DD");
                Text("FIELDWORK", 48, 34, 12); Text("RESEARCH & IDEAS / 2026", 367, 36, 8, "#64706A"); Line(62);
                if (page == 0)
                {
                    Text("THE CIRCULAR FUTURES REPORT", 48, 103, 10, "#537466");
                    Text("Good ideas.\nLasting impact.", 45, 137, 49, "#243B31");
                    Text("A practical guide to designing a better tomorrow.", 49, 279, 13, "#5B6860");
                    Box(48, 331, 499, 332, "#E8EEE5");
                    using var arc = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 72, Color = SKColor.Parse("#466B52") };
                    c.Save(); c.ClipRect(new(48, 331, 547, 663)); c.DrawCircle(302, 500, 160, arc); arc.Color = SKColor.Parse("#C5D7B8"); arc.StrokeWidth = 58; c.DrawCircle(302, 500, 88, arc);
                    using var accent = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#EE7957") }; c.DrawCircle(439, 405, 54, accent); c.Restore();
                    Text("01 — RETHINK WHAT COMES NEXT", 48, 703, 9, "#63766A");
                    Text("Insights, principles and practical steps for a more\nthoughtful use of our shared resources.", 48, 728, 12);
                }
                else if (page == 1)
                {
                    Text("01 / THE BIG PICTURE", 48, 98, 10, "#537466"); Text("Less waste.\nMore possibility.", 48, 133, 38);
                    Text("A circular approach starts with a simple question: what could we keep in use for longer? This report turns that question into a shared plan for action.", 48, 251, 13, width: 470);
                    var metrics = new[] { ("32%", "LESS MATERIAL"), ("2.4×", "LONGER LIFE"), ("86%", "RECOVERABLE") };
                    for (var i = 0; i < 3; i++) { Box(48 + i * 170, 350, 158, 112, "#EEF2EB"); Text(metrics[i].Item1, 61 + i * 170, 369, 32, "#365E46"); Text(metrics[i].Item2, 61 + i * 170, 427, 8, "#64706A"); }
                    Text("Three principles to put into practice", 48, 510, 21);
                    var rows = new[] { ("Design for longevity", "Make repair and maintenance part of the original idea."), ("Keep materials moving", "Create pathways for reuse before adding new resources."), ("Measure what matters", "Track useful outcomes, not just the volume produced.") };
                    for (var i = 0; i < rows.Length; i++) { Line(563 + i * 63); Text($"0{i + 1}", 48, 579 + i * 63, 12, "#537466"); Text(rows[i].Item1, 86, 574 + i * 63, 13); Text(rows[i].Item2, 86, 598 + i * 63, 10, "#64706A"); }
                }
                else if (page == 2)
                {
                    Text("02 / FROM INTENT TO ACTION", 48, 98, 10, "#537466"); Text("A roadmap for\nmeaningful change.", 48, 136, 36);
                    var stages = new[] { ("Discover", "Map material flows and listen to the people closest to the work."), ("Design", "Prototype one clear improvement. Test it in a real setting."), ("Deliver", "Create a repeatable system with clear ownership and measures."), ("Learn", "Share results, capture feedback and make the next iteration better.") };
                    for (var i = 0; i < 4; i++) { Box(48, 287 + i * 105, 42, 42, "#E8EEE5"); Text($"0{i + 1}", 57, 296 + i * 105, 18, "#466B52"); Text(stages[i].Item1, 111, 284 + i * 105, 20); Text(stages[i].Item2, 111, 320 + i * 105, 12, "#64706A", 411); }
                }
                else if (page == 3)
                {
                    Text("03 / MATERIAL INTELLIGENCE", 48, 98, 10, "#537466"); Text("Choose with care.", 48, 141, 36);
                    Text("Every material has a story", 48, 226, 20);
                    Text("Understand where it comes from, how it performs and what happens at the end of its first life. A good specification balances immediate needs with long-term responsibility.", 48, 273, 12, "#64706A", 223);
                    Text("Build a useful checklist", 320, 226, 20);
                    Text("Prioritize durable materials.\n\nChoose reversible connections.\n\nDocument what is inside.\n\nCreate a repair pathway.\n\nPlan for a second life.", 320, 273, 12, "#64706A", 218);
                    Box(48, 514, 499, 191, "#E8EEE5"); Text("“The most valuable resource\nis the one we keep in use.”", 72, 550, 26, "#466B52", 450); Text("FIELDWORK DESIGN PRINCIPLE / 04", 72, 659, 9, "#64706A");
                }
                else if (page == 4)
                {
                    Text("04 / LEARNING THROUGH EVIDENCE", 48, 98, 10, "#537466"); Text("Progress, made visible.", 48, 142, 32);
                    Text("Illustrative recovery rates across four material streams.", 48, 217, 12, "#64706A");
                    var names = new[] { "Paper", "Metals", "Textiles", "Polymers" }; var values = new[] { 86, 78, 64, 53 };
                    for (var i = 0; i < 4; i++) { Text(names[i], 48, 300 + i * 81, 12); Box(140, 298 + i * 81, 346, 30, "#EDF1E9"); Box(140, 298 + i * 81, values[i] * 3.46f, 30, i == 0 ? "#466B52" : "#A7BF99"); Text(values[i] + "%", 506, 303 + i * 81, 12); }
                    Text("About this example", 48, 679, 15); Text("These values are fictional demonstration data, not a research claim. Use the annotation tools to review, highlight and comment on this document.", 48, 713, 11, "#64706A");
                }
                else
                {
                    Text("05 / TURN INSIGHT INTO COMMITMENT", 48, 98, 10, "#537466"); Text("Make it a shared effort.", 48, 142, 32);
                    Text("Use Fill & Sign to add text, check marks and a drawn signature. Marks in this sample are visual annotations, not certificate-based digital signatures.", 48, 220, 13, "#64706A");
                    Text("YOUR NAME", 48, 329, 9); Line(391); Text("TEAM / ORGANIZATION", 48, 432, 9); Line(494); Text("SIGNATURE", 48, 536, 9); Line(632); Text("DATE", 370, 536, 9); Box(367, 630, 180, 1, "#DCE2DD");
                    Text("Start small. Learn together. Keep going.", 48, 716, 18, "#466B52");
                }
                Line(792); Text("CIRCULAR FUTURES / FIELDWORK STUDIO", 48, 810, 8, "#64706A"); Text((page + 1).ToString("00"), 528, 807, 10); pdf.EndPage();
            }
            pdf.Close();
        }
        var document = PdfReader.Open(stream.ToArray(), "Circular futures.pdf");
        var titles = new[] { "Cover", "The big picture", "Roadmap", "Materials", "Evidence", "Commitment" };
        return document with { Pages = document.Pages.Select((p, i) => p with { Bookmark = titles[i] }).ToArray() };
    }
}
