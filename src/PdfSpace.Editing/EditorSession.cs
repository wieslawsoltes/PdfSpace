using PdfSpace.Core;
namespace PdfSpace.Editing;

public sealed record HistoryEntry(string Label, PdfWorkspace Before, PdfWorkspace After);
public sealed partial class EditorSession
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
    public void SetTool(PdfTool tool)
    {
        if (!Enum.IsDefined(tool)) throw new ArgumentOutOfRangeException(nameof(tool));
        Tool = tool; SelectedAnnotationId = null; SelectedFieldId = null; ViewChanged?.Invoke(this, EventArgs.Empty);
    }
    public void Navigate(int index) { SelectedFieldId = null; CurrentPage = Math.Clamp(index, 0, Document.Pages.Length - 1); SelectedAnnotationId = null; ViewChanged?.Invoke(this, EventArgs.Empty); }
    public void Select(Guid? id) { SelectedFieldId = null; SelectedAnnotationId = id; ViewChanged?.Invoke(this, EventArgs.Empty); }
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
    public void Undo() { if (!CanUndo) return; var entry = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); _redo.Add(entry); Document = entry.Before; Notify(); }
    public void Redo() { if (!CanRedo) return; var entry = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); _undo.Add(entry); Document = entry.After; Notify(); }
    public void AddAnnotation(Annotation annotation, int? pageIndex = null)
    {
        var index = pageIndex ?? CurrentPage;
        if (index < 0 || index >= Document.Pages.Length) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        var page = Document.Pages[index];
        Execute("Add " + annotation.Kind.ToString().ToLowerInvariant(), document => document.UpdatePage(page.Id, state => state with { Annotations = [..state.Annotations, annotation] }));
        if (index == CurrentPage) SelectedAnnotationId = annotation.Id;
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }
    public void UpdateAnnotation(Guid id, Func<Annotation, Annotation> update, string label = "Edit annotation")
    {
        var page = Page; var existing = page.Annotations.FirstOrDefault(annotation => annotation.Id == id);
        if (existing is null) return;
        var replacement = update(existing); if (replacement == existing) return;
        if (replacement.Id != id) throw new InvalidOperationException("An annotation update cannot change its identity.");
        Execute(label, document => document.UpdatePage(page.Id, state => state with { Annotations = state.Annotations.Select(annotation => annotation.Id == id ? replacement : annotation).ToArray() }));
    }
    public void DeleteSelection()
    {
        if (SelectedAnnotationId is not { } id || !Page.Annotations.Any(annotation => annotation.Id == id)) return;
        var page = Page;
        Execute("Delete annotation", document => document.UpdatePage(page.Id, state => state with { Annotations = state.Annotations.Where(annotation => annotation.Id != id).ToArray() }));
    }
    public void RotatePage(int degrees = 90)
    {
        if (degrees % 90 != 0) throw new ArgumentOutOfRangeException(nameof(degrees), "Rotation must be a multiple of 90 degrees.");
        var page = Page; var rotation = (int)(((long)page.Rotation + degrees) % 360 + 360) % 360;
        if (rotation == page.Rotation) return;
        Execute("Rotate page", document => document.UpdatePage(page.Id, state => state with { Rotation = rotation }));
    }
    public void CropPage(RectD? crop)
    { var page = Page; if (crop == page.Crop) return; Execute(crop is null ? "Reset crop" : "Crop page", document => document.UpdatePage(page.Id, state => state with { Crop = crop })); }
    public void BookmarkPage(string name)
    { var page = Page; if (name == page.Bookmark) return; Execute("Edit bookmark", document => document.UpdatePage(page.Id, state => state with { Bookmark = name })); }
    public void InsertBlank()
    {
        var index = CurrentPage + 1; Execute("Insert blank page", document => document with { Pages = [..document.Pages.Take(index), new PdfPageState(), ..document.Pages.Skip(index)] }); Navigate(index);
    }
    public void DuplicatePage()
    {
        var index = CurrentPage;
        if (Page.Fields.Length > 0) throw new InvalidOperationException("Duplicating interactive form pages is not supported; flatten a copy first.");
        var copy = Page with { Id = Guid.NewGuid(), Annotations = Page.Annotations.Select(annotation => annotation with { Id = Guid.NewGuid(), Replies = annotation.Replies.Select(reply => reply with { Id = Guid.NewGuid() }).ToArray() }).ToArray() };
        Execute("Duplicate page", document => document with { Pages = [..document.Pages.Take(index + 1), copy, ..document.Pages.Skip(index + 1)] }); Navigate(index + 1);
    }
    public void DeletePage()
    {
        if (Document.Pages.Length == 1) throw new InvalidOperationException("Keep at least one page in the document.");
        var id = Page.Id; Execute("Delete page", document => document with { Pages = document.Pages.Where(page => page.Id != id).ToArray() });
    }
    public void MovePage(int target)
    {
        target = Math.Clamp(target, 0, Document.Pages.Length - 1); var source = CurrentPage; if (source == target) return;
        Execute("Reorder pages", document => { var pages = document.Pages.ToList(); var page = pages[source]; pages.RemoveAt(source); pages.Insert(target, page); return document with { Pages = pages.ToArray() }; }); Navigate(target);
    }
    public void Combine(PdfWorkspace other) => Execute("Combine PDFs", document => WorkspaceComposition.Append(document, other));
    public void Reply(Guid id, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        UpdateAnnotation(id, annotation => annotation with { Replies = [..annotation.Replies, new CommentReply(Guid.NewGuid(), "You", text.Trim(), DateTimeOffset.UtcNow)] }, "Reply to comment");
    }
}
