using System.Text;
using System.Text.Json;
namespace PdfSpace.Workbench;

public sealed partial class PdfWorkbench
{
    private readonly PdfObjectDiagnostics _objectDiagnostics = new();

    /// <summary>Read-only opt-in diagnostics for real pointer/keyboard acceptance tests. No mutation API.</summary>
    public string GetDiagnosticsJson()
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject(); json.WriteBoolean("ready", true); json.WriteString("title", Session.Document.Title); json.WriteString("tool", Session.Tool.ToString());
            json.WriteString("mode", _mode); json.WriteString("rightPanel", _right); json.WriteString("status", Status); json.WriteNumber("pages", Session.Document.Pages.Length); json.WriteNumber("page", Session.CurrentPage + 1);
            json.WriteNumber("pageLabelIndexBuilds", Viewport.PageLabelIndexBuilds);
            json.WriteString("pageLabel", Session.Document.IsSensitive ? "[protected]" : Viewport.PageLabels[Session.CurrentPage]);
            json.WriteNumber("fields", Session.Document.FieldCount); json.WriteBoolean("sensitive", Session.Document.IsSensitive); json.WriteString("selectedField", Session.SelectedField?.Name);
            json.WriteNumber("redactions", Session.Document.Pages.Sum(page => page.Annotations.Count(annotation => annotation.Kind == AnnotationKind.RedactionMark)));
            json.WriteStartArray("formValues"); foreach (var field in Session.Document.Pages.SelectMany(page => page.Fields)) { json.WriteStartObject(); json.WriteString("name", field.Name); json.WriteString("value", Session.Document.IsSensitive ? "[protected]" : field.Value);
                json.WriteString("defaultValue", Session.Document.IsSensitive ? "[protected]" : field.DefaultValue);
                json.WriteString("label", field.Label); json.WriteBoolean("readOnly", field.ReadOnly); json.WriteBoolean("required", field.Required); json.WriteBoolean("multiline", field.Multiline);
                json.WriteNumber("fontSize", field.FontSize); json.WriteNumber("maxLength", field.MaxLength); json.WriteEndObject(); } json.WriteEndArray();
            json.WriteNumber("annotations", Session.Document.AnnotationCount); json.WriteNumber("documents", _documents.Count); json.WriteNumber("zoom", Viewport.Zoom); json.WriteNumber("rotation", Session.Page.Rotation);
            json.WriteNumber("replies", Session.Document.Pages.Sum(page => page.Annotations.Sum(annotation => annotation.Replies.Length)));
            json.WriteNumber("resolved", Session.Document.Pages.Sum(page => page.Annotations.Count(annotation => annotation.Resolved)));
            json.WriteString("layout", Viewport.LayoutMode.ToString()); json.WriteBoolean("cropped", Session.Page.Crop is not null);
            json.WriteBoolean("dirty", Session.IsDirty); json.WriteBoolean("canUndo", Session.CanUndo); json.WriteBoolean("canRedo", Session.CanRedo); json.WriteBoolean("editingText", Viewport.IsEditingText); json.WriteBoolean("dialog", _dialogs.IsOpen);
            json.WriteString("selection", Session.SelectedAnnotation?.Kind.ToString()); json.WriteString("selectedText", Session.Document.IsSensitive ? "[protected]" : Session.SelectedAnnotation?.Text); json.WriteNumber("results", _searchResults.Length);
            if (XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused) json.WriteString("focusedControl", AutomationProperties.GetName(focused));
            var origin = Viewport.TransformToVisual(this).TransformPoint(new Point(0, 0)); var pageBounds = Viewport.PageScreenBounds(Session.CurrentPage);
            json.WriteStartObject("pageBounds"); json.WriteNumber("x", origin.X + pageBounds.X); json.WriteNumber("y", origin.Y + pageBounds.Y); json.WriteNumber("width", pageBounds.Width); json.WriteNumber("height", pageBounds.Height); json.WriteEndObject();
            json.WriteBoolean("ocrBusy", _ocrCancellation is not null);
            json.WriteNumber("ocrWords", Session.Document.Pages.Sum(page => page.Ocr?.Words.Length ?? 0));
            json.WriteNumber("ocrReviewed", Session.Document.Pages.Sum(page => page.Ocr?.Words.Count(word => word.Reviewed) ?? 0));
            json.WriteString("ocrLanguage", _ocrLanguage);
            json.WriteStartArray("ocrText");
            foreach (var text in Session.Document.Pages.SelectMany(page => page.Ocr?.Words ?? []).Take(500)) json.WriteStringValue(Session.Document.IsSensitive ? "[protected]" : text.Text);
            json.WriteEndArray();
            json.WriteNumber("nativeImages", _nativeImages.Length); json.WriteNumber("selectedImage", _imageSelection);
            if ((uint)_imageSelection < _nativeImages.Length) { var image = _nativeImages[_imageSelection]; json.WriteStartObject("imageBounds"); json.WriteNumber("x", image.Bounds.X); json.WriteNumber("y", image.Bounds.Y); json.WriteNumber("width", image.Bounds.Width); json.WriteNumber("height", image.Bounds.Height); json.WriteEndObject(); }
            json.WriteNumber("retainedSourceBytes", Session.RetainedSourceBytes);
            json.WriteNumber("undoCount", Session.UndoCount); json.WriteNumber("redoCount", Session.RedoCount);
            json.WriteNumber("prunedHistoryEntries", Session.PrunedHistoryEntries);
            json.WriteStartArray("imageSamples");
            foreach (var image in _nativeImages) { json.WriteStartObject(); json.WriteNumber("width", image.PixelWidth); json.WriteNumber("height", image.PixelHeight); json.WriteEndObject(); }
            json.WriteEndArray();
            json.WriteNumber("nativeObjects", _pageObjects.Length);
            json.WriteNumber("objectIndexBuilds", _objectIndexBuilds);
            json.WriteNumber("objectListBuilds", _objectListBuilds);
            json.WriteNumber("objectListRowsCreated", _objectListRowsCreated);
            json.WriteNumber("objectListStart", _objectListWindow.Start);
            json.WriteNumber("objectListVisible", _objectListWindow.VisibleCount);
            json.WriteBoolean("objectResizeSnapping", Viewport.SnapNativeObjectResize);
            json.WriteBoolean("objectPointSnapping", Viewport.SnapNativeObjectPoints);
            if (Viewport.NativeObjectPointPreview is { } np)
            { json.WriteStartObject("objectPointPreview"); json.WriteNumber("x", np.X); json.WriteNumber("y", np.Y); json.WriteEndObject(); }
            json.WriteBoolean("objectSnapping", Viewport.SnapNativeObjectMovement);
            json.WriteNumber("snapIndexBuilds", Viewport.ObjectSnapIndexBuilds);
            json.WriteNumber("rotationPreview", Viewport.ObjectRotationPreview);
            if (Viewport.NativeObjectRotationHandle is { } rh)
            { json.WriteStartObject("rotationHandle"); json.WriteNumber("x", rh.X); json.WriteNumber("y", rh.Y); json.WriteEndObject(); }
            if (Viewport.NativeObjectPreviewBounds is { } pb)
            { json.WriteStartObject("objectPreview"); json.WriteNumber("x", pb.X); json.WriteNumber("y", pb.Y); json.WriteNumber("width", pb.Width); json.WriteNumber("height", pb.Height); json.WriteEndObject(); }
            json.WriteBoolean("snapVertical", Viewport.ObjectVerticalGuide is not null);
            json.WriteBoolean("snapHorizontal", Viewport.ObjectHorizontalGuide is not null);
            json.WriteNumber("objectHitBoundsTested", Viewport.LastObjectHitTestCount);
            json.WriteNumber("visibleObjectOutlines", Viewport.VisibleObjectOutlineCount);
            json.WriteStartArray("selectedObjects");
            foreach (var i in _selectedObjects)
                json.WriteNumberValue(i);
            json.WriteEndArray();
            json.WritePropertyName("objects");
            _objectDiagnostics.Write(json, _pageObjects, Session.Document.IsSensitive);
            json.WriteNumber("objectDiagnosticBuilds", _objectDiagnostics.BuildCount);
            json.WriteStartArray("controls");
            void Visit(DependencyObject node, bool visible)
            {
                if (node is FrameworkElement element)
                {
                    visible &= element.Visibility == Visibility.Visible;
                    var name = AutomationProperties.GetName(element);
                    if (visible && !string.IsNullOrEmpty(name) && element.ActualWidth > 0 && element.ActualHeight > 0 && element is Control control && element.XamlRoot == XamlRoot)
                    {
                        var position = element.TransformToVisual(this).TransformPoint(new Point(0, 0));
                        json.WriteStartObject(); json.WriteString("name", name); json.WriteNumber("x", position.X); json.WriteNumber("y", position.Y); json.WriteNumber("width", element.ActualWidth); json.WriteNumber("height", element.ActualHeight); json.WriteBoolean("enabled", control.IsEnabled);
                        // Observe the actual routed pointer target, not just possibly-ahead scroll geometry.
                        // This getter is read only and only runs in opt-in diagnostic sessions.
                        if (control is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase button)
                            json.WriteBoolean("pointerOver", button.IsPointerOver);
                        json.WriteEndObject();
                    }
                }
                var count = VisualTreeHelper.GetChildrenCount(node); for (var i = 0; i < count; i++) Visit(VisualTreeHelper.GetChild(node, i), visible);
            }
            Visit(_dialogs.IsOpen ? _dialogs : this, true);
            if (!_dialogs.IsOpen && XamlRoot is not null)
                foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot))
                    if (popup.Child is { } child) Visit(child, true);
            json.WriteEndArray(); json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
