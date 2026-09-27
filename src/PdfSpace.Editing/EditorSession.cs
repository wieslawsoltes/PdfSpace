using PdfSpace.Core;
namespace PdfSpace.Editing;

public sealed record HistoryEntry(string Label, PdfWorkspace Before, PdfWorkspace After);
public sealed class EditorSession
{
    private readonly List<HistoryEntry> _undo = [], _redo = [];
    private PdfWorkspace _saved;
    public PdfWorkspace Document { get; private set; }
    public event EventHandler? Changed;
    public event EventHandler? ViewChanged;
    public int CurrentPage { get; private set; }
    public Guid? SelectedAnnotationId { get; private set; }
    public PdfTool Tool { get; private set; }
    public uint Color { get; set; } = 0xFF1473E6;
    public double StrokeWidth { get; set; } = 2;
    public double FontSize { get; set; } = 14;
    public string PendingText { get; set; } = "";
    public long Revision { get; private set; }
    public bool IsDirty => !ReferenceEquals(_saved, Document);
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string UndoLabel => CanUndo ? _undo[^1].Label : "";
    public PdfPageState Page => Document.Pages[CurrentPage];
    public Annotation? SelectedAnnotation => Page.Annotations.FirstOrDefault(a => a.Id == SelectedAnnotationId);
    public EditorSession(PdfWorkspace document) { WorkspaceJson.Validate(document); Document = _saved = document; }
    public void MarkSaved() { _saved = Document; ViewChanged?.Invoke(this, EventArgs.Empty); }
    public void SetTool(PdfTool tool) { Tool = tool; SelectedAnnotationId = null; ViewChanged?.Invoke(this, EventArgs.Empty); }
    public void Navigate(int index) { CurrentPage = Math.Clamp(index, 0, Document.Pages.Length - 1); SelectedAnnotationId = null; ViewChanged?.Invoke(this, EventArgs.Empty); }
    public void Select(Guid? id) { SelectedAnnotationId = id; ViewChanged?.Invoke(this, EventArgs.Empty); }
    public void Execute(string label, Func<PdfWorkspace, PdfWorkspace> command)
    {
        var before = Document; var after = command(before); if (ReferenceEquals(before, after)) return;
        WorkspaceJson.Validate(after); _undo.Add(new(label, before, after));
        if (_undo.Count > 100) _undo.RemoveAt(0);
        _redo.Clear(); Document = after; Notify();
    }
    private void Notify()
    {
        CurrentPage = Math.Clamp(CurrentPage, 0, Document.Pages.Length - 1);
        if (!Page.Annotations.Any(a => a.Id == SelectedAnnotationId)) SelectedAnnotationId = null;
        Revision++; Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Undo() { if (!CanUndo) return; var e = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); _redo.Add(e); Document = e.Before; Notify(); }
    public void Redo() { if (!CanRedo) return; var e = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); _undo.Add(e); Document = e.After; Notify(); }
    public void AddAnnotation(Annotation annotation, int? pageIndex = null)
    {
        var page = Document.Pages[pageIndex ?? CurrentPage];
        Execute("Add " + annotation.Kind.ToString().ToLowerInvariant(), d => d.UpdatePage(page.Id, p => p with { Annotations = [..p.Annotations, annotation] }));
        SelectedAnnotationId = annotation.Id; ViewChanged?.Invoke(this, EventArgs.Empty);
    }
    public void UpdateAnnotation(Guid id, Func<Annotation, Annotation> update, string label = "Edit annotation")
    { var page = Page; Execute(label, d => d.UpdatePage(page.Id, p => p with { Annotations = p.Annotations.Select(a => a.Id == id ? update(a) : a).ToArray() })); }
    public void DeleteSelection()
    {
        if (SelectedAnnotationId is not { } id) return; var page = Page;
        Execute("Delete annotation", d => d.UpdatePage(page.Id, p => p with { Annotations = p.Annotations.Where(a => a.Id != id).ToArray() }));
    }
    public void RotatePage(int degrees = 90)
    { var page = Page; Execute("Rotate page", d => d.UpdatePage(page.Id, p => p with { Rotation = ((p.Rotation + degrees) % 360 + 360) % 360 })); }
    public void CropPage(RectD? crop)
    { var page = Page; Execute(crop is null ? "Reset crop" : "Crop page", d => d.UpdatePage(page.Id, p => p with { Crop = crop })); }
    public void BookmarkPage(string name)
    { var page = Page; Execute("Edit bookmark", d => d.UpdatePage(page.Id, p => p with { Bookmark = name })); }
    public void InsertBlank()
    {
        var i = CurrentPage + 1; Execute("Insert blank page", d => d with { Pages = [..d.Pages.Take(i), new PdfPageState(), ..d.Pages.Skip(i)] }); Navigate(i);
    }
    public void DuplicatePage()
    {
        var i = CurrentPage; var copy = Page with { Id = Guid.NewGuid(), Annotations = Page.Annotations.Select(a => a with { Id = Guid.NewGuid() }).ToArray() };
        Execute("Duplicate page", d => d with { Pages = [..d.Pages.Take(i + 1), copy, ..d.Pages.Skip(i + 1)] }); Navigate(i + 1);
    }
    public void DeletePage()
    {
        if (Document.Pages.Length == 1) throw new InvalidOperationException("Keep at least one page in the document.");
        var id = Page.Id; Execute("Delete page", d => d with { Pages = d.Pages.Where(p => p.Id != id).ToArray() });
    }
    public void MovePage(int target)
    {
        target = Math.Clamp(target, 0, Document.Pages.Length - 1); var source = CurrentPage; if (source == target) return;
        Execute("Reorder pages", d => { var pages = d.Pages.ToList(); var page = pages[source]; pages.RemoveAt(source); pages.Insert(target, page); return d with { Pages = pages.ToArray() }; }); Navigate(target);
    }
    public void Combine(PdfWorkspace other)
    {
        WorkspaceJson.Validate(other);
        Execute("Combine PDFs", d => d with { Sources = [..d.Sources, ..other.Sources], Pages = [..d.Pages, ..other.Pages] });
    }
    public void Reply(Guid id, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        UpdateAnnotation(id, a => a with { Replies = [..a.Replies, new CommentReply(Guid.NewGuid(), "You", text.Trim(), DateTimeOffset.UtcNow)] }, "Reply to comment");
    }
}
