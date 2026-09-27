using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSpace.Core;
using PdfSpace.Skia;
using SkiaSharp;
using NativeReader = PdfSharp.Pdf.IO.PdfReader;
using WorkspaceReader = PdfSpace.Documents.PdfReader;
namespace PdfSpace.Pdf;

/// <summary>Structured PDF adapter. Native content streams and catalogs are not converted into rendered page pictures.</summary>
public static class PdfDocumentEngine
{
    internal static PdfDocument OpenNative(byte[] bytes, string? password = null, PdfDocumentOpenMode mode = PdfDocumentOpenMode.Modify)
    {
        if (bytes.Length > WorkspaceJson.MaximumSourceBytes) throw new InvalidDataException("PDF source exceeds 64 MB.");
        try
        {
            return NativeReader.Open(new MemoryStream(bytes, writable: false), password ?? "", mode);
        }
        catch (PdfReaderException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            throw new PdfPasswordRequiredException("This PDF requires an owner/editing password. The password stays in memory and is never stored.", ex);
        }
    }
    internal static byte[] Bytes(PdfDocument document)
    {
        using var stream = new MemoryStream(); document.Save(stream, closeStream: false); return stream.ToArray();
    }
    public static PdfWorkspace Open(byte[] bytes, string name, string? password = null)
    {
        using var native = OpenNative(bytes, password);
        var sensitive = native.SecurityHandler.Elements.GetName("/Filter") == "/Standard";
        if (sensitive)
        {
            // Only Modify-mode owner access is accepted. Do not sidestep permission restrictions through Import mode.
            native.SecurityHandler.SetEncryptionToNoneAndResetPasswords(); bytes = Bytes(native);
        }
        var workspace = WorkspaceReader.Open(bytes, name);
        return Enrich(workspace, importObjects: true, sensitive);
    }
    public static PdfWorkspace PrepareWorkspace(PdfWorkspace workspace) => Enrich(workspace, importObjects: false);
    private static PdfWorkspace Enrich(PdfWorkspace workspace, bool importObjects, bool sensitive = false)
    {
        WorkspaceJson.Validate(workspace);
        var sources = new List<PdfSource>(); var pages = workspace.Pages.ToArray();
        foreach (var source in workspace.Sources)
        {
            using var native = OpenNative(source.Bytes);
            var inspection = Inspect(native);
            var fieldsByPage = new Dictionary<int, PdfFormFieldState[]>(); var annotationsByPage = new Dictionary<int, Annotation[]>();
            var managedByPage = new Dictionary<int, HashSet<int>>();
            for (var number = 0; number < native.PageCount; number++)
            {
                var original = native.Pages[number]; var geometry = new SourceGeometry(original);
                var fields = new List<PdfFormFieldState>(); var annotations = new List<Annotation>(); var roots = new Dictionary<PdfDictionary, Annotation>(ReferenceEqualityComparer.Instance);
                var managed = new HashSet<int>();
                if (PdfObjects.Array(original.Elements["/Annots"]) is { } items)
                {
                    for (var index = 0; index < items.Elements.Count; index++)
                    {
                        var item = PdfObjects.Dictionary(items.Elements[index]); if (item is null) continue;
                        var key = $"{number + 1}:{index}";
                        if (PdfObjects.Text(item.Elements["/Subtype"]) == "Widget")
                        {
                            var field = NativeForms.Read(item, geometry, key);
                            if (field is not null)
                            {
                                if (inspection.HasXfa || inspection.SignatureFields > 0) field = field with { ReadOnly = true };
                                fields.Add(field);
                                if (field.Kind is not (PdfFieldKind.Signature or PdfFieldKind.Unsupported)) managed.Add(index);
                            }
                        }
                        else
                        {
                            var annotation = NativeAnnotations.Read(native, item, geometry, key);
                            if (annotation is not null) { annotations.Add(annotation); roots.Add(item, annotation); managed.Add(index); }
                        }
                    }
                    for (var index = 0; index < items.Elements.Count; index++)
                    {
                        var item = PdfObjects.Dictionary(items.Elements[index]); var parent = PdfObjects.Dictionary(item?.Elements["/IRT"]);
                        if (item is null || parent is null || !roots.TryGetValue(parent, out var root)) continue;
                        var reply = new CommentReply(Guid.TryParse(PdfObjects.Text(item.Elements["/NM"]), out var parsed) ? parsed : Guid.NewGuid(), PdfObjects.Text(item.Elements["/T"]), PdfObjects.Text(item.Elements["/Contents"]), new DateTimeOffset(DateTime.SpecifyKind(item.Elements.GetDateTime("/CreationDate", DateTime.UtcNow), DateTimeKind.Utc)));
                        var position = annotations.FindIndex(annotation => annotation.Id == root.Id);
                        annotations[position] = annotations[position] with { Replies = [..annotations[position].Replies, reply], Resolved = annotations[position].Resolved || PdfObjects.Text(item.Elements["/State"]) == "Completed" };
                        managed.Add(index);
                    }
                }
                fieldsByPage[number + 1] = fields.ToArray(); annotationsByPage[number + 1] = annotations.ToArray(); managedByPage[number + 1] = managed;
            }
            if (importObjects)
            {
                for (var i = 0; i < pages.Length; i++)
                    if (pages[i].SourceId == source.Id)
                        pages[i] = pages[i] with { Annotations = annotationsByPage[pages[i].SourcePage], Fields = fieldsByPage[pages[i].SourcePage] };
            }
            foreach (var page in pages.Where(page => page.SourceId == source.Id))
                if (page.SourcePage > native.PageCount) throw new InvalidDataException("A workspace refers to a nonexistent source page.");
            var stripped = false;
            foreach (var (number, managed) in managedByPage)
            {
                var list = PdfObjects.Array(native.Pages[number - 1].Elements["/Annots"]); if (list is null) continue;
                foreach (var index in managed.OrderDescending()) { list.Elements.RemoveAt(index); stripped = true; }
            }
            // Preview-only copy excludes objects represented by editable workspace overlays.
            sources.Add(source with { PreviewBytes = stripped ? Bytes(native) : null, Sensitive = sensitive || source.Sensitive });
        }
        var result = workspace with { Sources = sources.ToArray(), Pages = pages };
        WorkspaceJson.Validate(result); return result;
    }
    public static PdfInspection Inspect(byte[] bytes) { using var document = OpenNative(bytes); return Inspect(document); }
    private static PdfInspection Inspect(PdfDocument document)
    {
        var form = PdfObjects.Dictionary(document.Internals.Catalog.Elements["/AcroForm"]); var widgets = 0; var signatures = 0; var annotations = 0;
        foreach (var page in document.Pages)
            if (PdfObjects.Array(page.Elements["/Annots"]) is { } list)
                foreach (var entry in list.Elements)
                {
                    var item = PdfObjects.Dictionary(entry); if (item is null) continue;
                    annotations++;
                    if (PdfObjects.Text(item.Elements["/Subtype"]) == "Widget") widgets++;
                    if (PdfObjects.Text(PdfObjects.Inherited(item, "/FT")) == "Sig") signatures++;
                }
        if (document.Internals.Catalog.Elements.ContainsKey("/Perms")) signatures = Math.Max(signatures, 1);
        var names = PdfObjects.Dictionary(document.Internals.Catalog.Elements["/Names"]);
        return new(document.PageCount, widgets, signatures, form?.Elements.ContainsKey("/XFA") == true, names?.Elements.ContainsKey("/JavaScript") == true || document.Internals.Catalog.Elements.ContainsKey("/OpenAction"), document.SecurityHandler.Elements.GetName("/Filter") == "/Standard", annotations, document.Outlines.Count);
    }
    public static bool CanPreserveCatalog(PdfWorkspace workspace)
    {
        if (workspace.Sources.Length != 1 || workspace.Pages.Any(page => page.SourceId != workspace.Sources[0].Id)) return false;
        using var native = OpenNative(workspace.Sources[0].Bytes);
        return native.PageCount == workspace.Pages.Length && workspace.Pages.Select((page, i) => page.SourcePage == i + 1).All(value => value);
    }
    public static PdfWriteResult Save(PdfWorkspace workspace, SKTypeface typeface, PdfProtectionOptions? protection = null)
    {
        WorkspaceJson.Validate(workspace);
        if (workspace.Pages.Any(page => page.Annotations.Any(annotation => annotation.Kind == AnnotationKind.RedactionMark)))
            throw new InvalidOperationException("Apply pending redactions through Redact a PDF before saving a normal PDF. A workspace retains unredacted source data.");
        var nativeSources = new Dictionary<Guid, PdfDocument>();
        PdfDocument? output = null;
        try
        {
            foreach (var source in workspace.Sources)
            {
                var native = OpenNative(source.Bytes); nativeSources.Add(source.Id, native);
                var inspection = Inspect(native);
                if (inspection.SignatureFields > 0) throw new NotSupportedException("This PDF contains signature fields or certification. Structured changes are blocked to avoid silently invalidating signatures. A separately labeled flattened copy remains available.");
                if (inspection.HasXfa) throw new NotSupportedException("XFA documents are not supported by structured saving. No PDF has been written.");
            }
            var preserve = workspace.Sources.Length == 1 && workspace.Pages.Length == nativeSources[workspace.Sources[0].Id].PageCount && workspace.Pages.Select((page, i) => page.SourceId == workspace.Sources[0].Id && page.SourcePage == i + 1).All(match => match);
            if (preserve) output = nativeSources[workspace.Sources[0].Id];
            else
            {
                if (workspace.Pages.Any(page => page.Fields.Any(field => field.SourceKey is not null)) || nativeSources.Values.Any(native => Inspect(native).FormWidgets > 0)) throw new NotSupportedException("Structured saving of reorganized or combined form documents is not supported. Save the original page order or use a flattened copy.");
                output = new PdfDocument();
                for (var i = 0; i < workspace.Pages.Length; i++)
                {
                    var page = workspace.Pages[i];
                    if (page.SourceId is { } id)
                    {
                        // Page import requires a separate import-mode document; never use this path to bypass passwords.
                        using var import = OpenNative(workspace.Sources.First(source => source.Id == id).Bytes, mode: PdfDocumentOpenMode.Import);
                        output.AddPage(import.Pages[page.SourcePage - 1]);
                    }
                    else { var blank = output.AddPage(); blank.Width = PdfSharp.Drawing.XUnit.FromPoint(page.Width); blank.Height = PdfSharp.Drawing.XUnit.FromPoint(page.Height); }
                }
            }
            output.Info.Title = workspace.Title; output.Info.Author = workspace.Author;
            for (var i = 0; i < workspace.Pages.Length; i++)
            {
                var state = workspace.Pages[i]; var page = output.Pages[i]; var geometry = new SourceGeometry(page);
                var fields = new Dictionary<string, PdfDictionary>(); var removals = new HashSet<int>();
                if (PdfObjects.Array(page.Elements["/Annots"]) is { } original)
                {
                    var roots = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
                    for (var index = 0; index < original.Elements.Count; index++)
                    {
                        var item = PdfObjects.Dictionary(original.Elements[index]); if (item is null) continue;
                        var key = $"{state.SourcePage}:{index}";
                        if (PdfObjects.Text(item.Elements["/Subtype"]) == "Widget")
                        {
                            fields[key] = item;
                            var importedField = NativeForms.Read(item, geometry, key);
                            if (importedField is { Kind: not (PdfFieldKind.Signature or PdfFieldKind.Unsupported) } && !state.Fields.Any(field => field.SourceKey == key))
                            {
                                removals.Add(index);
                                NativeForms.RemoveFromTree(output, item);
                            }
                        }
                        else if (NativeAnnotations.Read(output, item, geometry, key) is not null) { removals.Add(index); roots.Add(item); }
                    }
                    for (var index = 0; index < original.Elements.Count; index++)
                        if (PdfObjects.Dictionary(PdfObjects.Dictionary(original.Elements[index])?.Elements["/IRT"]) is { } parent && roots.Contains(parent)) removals.Add(index);
                    foreach (var index in removals.OrderDescending()) original.Elements.RemoveAt(index);
                }
                foreach (var annotation in state.Annotations) NativeAnnotations.Write(output, page, annotation, geometry, typeface);
                foreach (var field in state.Fields) NativeForms.Write(output, page, field, geometry, typeface, field.SourceKey is { } key && fields.TryGetValue(key, out var originalField) ? originalField : null);
                if (state.Crop is { } crop) page.CropBox = geometry.ToPdf(crop);
                page.Rotate = (geometry.Rotation + state.Rotation) % 360;
            }
            var bookmarks = workspace.Pages.Select((page, i) => (page, i)).Where(item => item.page.Bookmark.Length > 0).ToArray();
            if (bookmarks.Length > 0)
            {
                for (var i = output.Outlines.Count - 1; i >= 0; i--) if (output.Outlines[i].Title == "PdfSpace bookmarks") output.Outlines.RemoveAt(i);
                var root = output.Outlines.Add("PdfSpace bookmarks", output.Pages[bookmarks[0].i], true);
                foreach (var item in bookmarks) root.Outlines.Add(item.page.Bookmark, output.Pages[item.i]);
            }
            if (protection is not null)
            {
                protection.Validate();
                output.SecuritySettings.UserPassword = protection.UserPassword; output.SecuritySettings.OwnerPassword = protection.OwnerPassword;
                output.SecurityHandler.SetEncryptionToV5();
                output.SecuritySettings.PermitPrint = protection.AllowPrint; output.SecuritySettings.PermitFullQualityPrint = protection.AllowPrint;
                output.SecuritySettings.PermitExtractContent = protection.AllowCopy; output.SecuritySettings.PermitModifyDocument = protection.AllowEdit;
                output.SecuritySettings.PermitAnnotations = protection.AllowEdit; output.SecuritySettings.PermitFormsFill = protection.AllowEdit; output.SecuritySettings.PermitAssembleDocument = protection.AllowEdit;
            }
            var bytes = Bytes(output);
            return new(bytes, preserve, preserve ? ["PDF objects are rewritten, not incrementally appended. Digital signatures and XFA changes are blocked."] : ["Pages and their content are imported into a new PDF catalog. Original document-level metadata, outlines, named destinations, tags and attachments are not guaranteed to survive page assembly."]);
        }
        finally
        {
            if (output is not null && !nativeSources.Values.Contains(output)) output.Dispose();
            foreach (var native in nativeSources.Values) native.Dispose();
        }
    }
    public static PdfWorkspace CreateFormSample(SKTypeface typeface)
    {
        var document = SampleDocument.Create(typeface);
        document = document.UpdatePage(document.Pages[5].Id, page => page with { Fields =
        [
            new() { Name = "FullName", GroupName = "FullName", Label = "Full name", Kind = PdfFieldKind.Text, Bounds = new(48, 355, 490, 31), Required = true, MaxLength = 80, Modified = true },
            new() { Name = "Organization", GroupName = "Organization", Kind = PdfFieldKind.Text, Bounds = new(48, 456, 490, 31), Modified = true },
            new() { Name = "Approved", GroupName = "Approved", Label = "Approved", Kind = PdfFieldKind.CheckBox, Bounds = new(50, 679, 19, 19), Value = "Off", DefaultValue = "Off", Modified = true },
            new() { Name = "Role", GroupName = "Role", Kind = PdfFieldKind.ComboBox, Bounds = new(319, 669, 219, 29), Options = [new("Design", "Design"), new("Engineering", "Engineering"), new("Review", "Review")], Value = "Design", DefaultValue = "Design", Modified = true }
        ] });
        return Open(Save(document, typeface).Bytes, "Interactive review form.pdf");
    }
}
