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
            json.WriteNumber("annotations", Session.Document.AnnotationCount); json.WriteNumber("documents", _documents.Count); json.WriteNumber("zoom", Viewport.Zoom); json.WriteNumber("rotation", Session.Page.Rotation);
            json.WriteBoolean("dirty", Session.IsDirty); json.WriteBoolean("canUndo", Session.CanUndo); json.WriteBoolean("canRedo", Session.CanRedo); json.WriteBoolean("editingText", Viewport.IsEditingText); json.WriteBoolean("dialog", _dialogs.IsOpen);
            json.WriteString("selection", Session.SelectedAnnotation?.Kind.ToString()); json.WriteString("selectedText", Session.SelectedAnnotation?.Text); json.WriteNumber("results", _searchResults.Length);
            var origin = Viewport.TransformToVisual(this).TransformPoint(new Point(0, 0)); var page = Viewport.PageScreenBounds(Session.CurrentPage);
            json.WriteStartObject("pageBounds"); json.WriteNumber("x", origin.X + page.X); json.WriteNumber("y", origin.Y + page.Y); json.WriteNumber("width", page.Width); json.WriteNumber("height", page.Height); json.WriteEndObject();
            json.WriteStartArray("controls");
            void Visit(DependencyObject node, bool visible)
            {
                if (node is FrameworkElement element)
                {
                    visible &= element.Visibility == Visibility.Visible;
                    var name = AutomationProperties.GetName(element);
                    if (visible && name.Length > 0 && element.ActualWidth > 0 && element.ActualHeight > 0 && element is Control)
                    {
                        var p = element.TransformToVisual(this).TransformPoint(new Point(0, 0));
                        json.WriteStartObject(); json.WriteString("name", name); json.WriteNumber("x", p.X); json.WriteNumber("y", p.Y); json.WriteNumber("width", element.ActualWidth); json.WriteNumber("height", element.ActualHeight); json.WriteBoolean("enabled", ((Control)element).IsEnabled); json.WriteEndObject();
                    }
                }
                var count = VisualTreeHelper.GetChildrenCount(node); for (var i = 0; i < count; i++) Visit(VisualTreeHelper.GetChild(node, i), visible);
            }
            Visit(this, true); json.WriteEndArray(); json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
