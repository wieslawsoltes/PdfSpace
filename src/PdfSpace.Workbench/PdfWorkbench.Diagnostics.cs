using System.Text;
using System.Text.Json;
namespace PdfSpace.Workbench;

public sealed partial class PdfWorkbench
{
    /// <summary>Read-only opt-in diagnostics for real pointer/keyboard acceptance tests. No mutation API.</summary>
    public string GetDiagnosticsJson()
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject(); json.WriteBoolean("ready", true); json.WriteString("title", Session.Document.Title); json.WriteString("tool", Session.Tool.ToString());
            json.WriteString("mode", _mode); json.WriteString("rightPanel", _right); json.WriteString("status", Status); json.WriteNumber("pages", Session.Document.Pages.Length); json.WriteNumber("page", Session.CurrentPage + 1);
            json.WriteNumber("fields", Session.Document.FieldCount); json.WriteBoolean("sensitive", Session.Document.IsSensitive); json.WriteString("selectedField", Session.SelectedField?.Name);
            json.WriteNumber("redactions", Session.Document.Pages.Sum(page => page.Annotations.Count(annotation => annotation.Kind == AnnotationKind.RedactionMark)));
            json.WriteStartArray("formValues"); foreach (var field in Session.Document.Pages.SelectMany(page => page.Fields)) { json.WriteStartObject(); json.WriteString("name", field.Name); json.WriteString("value", Session.Document.IsSensitive ? "[protected]" : field.Value); json.WriteEndObject(); } json.WriteEndArray();
            json.WriteNumber("annotations", Session.Document.AnnotationCount); json.WriteNumber("documents", _documents.Count); json.WriteNumber("zoom", Viewport.Zoom); json.WriteNumber("rotation", Session.Page.Rotation);
            json.WriteNumber("replies", Session.Document.Pages.Sum(page => page.Annotations.Sum(annotation => annotation.Replies.Length)));
            json.WriteNumber("resolved", Session.Document.Pages.Sum(page => page.Annotations.Count(annotation => annotation.Resolved)));
            json.WriteString("layout", Viewport.LayoutMode.ToString()); json.WriteBoolean("cropped", Session.Page.Crop is not null);
            json.WriteBoolean("dirty", Session.IsDirty); json.WriteBoolean("canUndo", Session.CanUndo); json.WriteBoolean("canRedo", Session.CanRedo); json.WriteBoolean("editingText", Viewport.IsEditingText); json.WriteBoolean("dialog", _dialogs.IsOpen);
            json.WriteString("selection", Session.SelectedAnnotation?.Kind.ToString()); json.WriteString("selectedText", Session.SelectedAnnotation?.Text); json.WriteNumber("results", _searchResults.Length);
            if (XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused) json.WriteString("focusedControl", AutomationProperties.GetName(focused));
            var origin = Viewport.TransformToVisual(this).TransformPoint(new Point(0, 0)); var pageBounds = Viewport.PageScreenBounds(Session.CurrentPage);
            json.WriteStartObject("pageBounds"); json.WriteNumber("x", origin.X + pageBounds.X); json.WriteNumber("y", origin.Y + pageBounds.Y); json.WriteNumber("width", pageBounds.Width); json.WriteNumber("height", pageBounds.Height); json.WriteEndObject();
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
                        json.WriteStartObject(); json.WriteString("name", name); json.WriteNumber("x", position.X); json.WriteNumber("y", position.Y); json.WriteNumber("width", element.ActualWidth); json.WriteNumber("height", element.ActualHeight); json.WriteBoolean("enabled", control.IsEnabled); json.WriteEndObject();
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
