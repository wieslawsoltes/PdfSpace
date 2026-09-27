# Keyboard and pointer guide

| Command | Shortcut |
|---|---|
| Open PDF or workspace | Ctrl/Cmd+O |
| New blank PDF | Ctrl/Cmd+N |
| Save editable workspace | Ctrl/Cmd+S |
| Export visual PDF | Ctrl/Cmd+Shift+S |
| Find | Ctrl/Cmd+F |
| Open printable PDF | Ctrl/Cmd+P |
| Undo / redo | Ctrl/Cmd+Z / Ctrl/Cmd+Shift+Z; Ctrl+Y |
| Copy selected text | Ctrl/Cmd+C |
| Fit page / 100% | Ctrl/Cmd+0 / Ctrl/Cmd+1 |
| Select / hand / text / ink tool | V / H / T / D |
| Previous / next page | Page Up / Page Down |
| Delete selected annotation | Delete or Backspace while the document viewport is focused |
| Nudge selected annotation | Arrow keys |
| Cancel gesture or inline text edit | Escape |

Drag in the page to create a shape or select text. Double-click a text annotation to edit it. Click outside the inline text field to apply it; Escape cancels. Drag selection handles to resize an annotation. Mouse-wheel scrolls; Ctrl+wheel zooms around the pointer; Shift+wheel pans horizontally. A middle-button drag pans the document.

In Organize pages, drag a thumbnail onto another position to reorder. Arrow keys move between thumbnails. Touch panning and pinch handling are implemented, but physical-device behavior is not part of the desktop Chromium acceptance suite.

Browser-reserved shortcuts can vary by browser and operating system. The visible command buttons provide an alternative for every workflow described above.


## Interactive forms (0.2)

While using Fill form, Tab / Shift+Tab move through supported editable widgets in page/widget-array order. Traversal commits the current text. Space selects the focused check/radio button; Up/Down change a focused choice; Enter starts/commits single-line field editing and Escape cancels the current text input. The Prepare form panel also exposes Next field / Previous field and a property inspector. This does not implement every PDF `/Tabs` structure ordering mode.
