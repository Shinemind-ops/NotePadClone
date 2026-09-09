# Architecture

```
NotePadClone.sln
├── NotePadClone/                 # WPF app (net8.0-windows, MVVM)
│   ├── App.xaml(.cs)             # startup; CLI file-open args (e.Args) parsed here
│   ├── MainWindow.xaml(.cs)      # shell: tab host, key bindings, Copy-command takeover
│   ├── WpfEssentialsShim.cs      # in-repo replacement for upstream's external WpfEssentials DLL
│   ├── Controls/
│   │   ├── MenuBarControl.xaml           # menus (File/Edit/View...)
│   │   ├── StatusBarControl.xaml         # stats + 4 toggle groups (label 4px + divider)
│   │   └── LineNumberMargin.cs           # custom virtualized line-number gutter
│   ├── ViewModels/
│   │   ├── MainWindowVm.cs       # app state: docs, tabs, theme, sidebar, path bar, CopyPath
│   │   └── FileExplorerItem.cs   # sidebar tree nodes (FileSystemWatcher-backed)
│   ├── DocumentModel/
│   │   ├── Document.cs           # per-tab document: text, metadata, browse/edit state
│   │   └── DocumentMetadata.cs   # size / char / line counters
│   ├── Services/
│   │   ├── ClipboardWriter.cs    # STA background-thread clipboard with WM_CLIPBOARDUPDATE ack
│   │   ├── MarkdownToFlowDocument.cs     # md -> FlowDocument (headings/tables/code)
│   │   └── SettingsService.cs    # persisted toggles (theme, line numbers, root folder)
│   ├── Resources/                # window chrome, themes
│   └── ValueConverters/
└── NotePadCloneTests/            # xUnit, 12 tests (markdown render + settings)
```

## Key flows

- **Open file (Explorer right-click)**: `App.OnStartup` captures `e.Args` → after `ContentRendered`, resolves the *live* window's DataContext (NOT the transient startup VM — a known pitfall where the file opened into an unreferenced VM) → `MainWindowVm.OpenFile`.
- **Copy (any kind)**: UI thread → `ClipboardWriter.Write()` returns immediately → dedicated STA thread retries `Clipboard.SetText` until `WM_CLIPBOARDUPDATE` observed (max 8 backoff attempts; each attempt first probes whether the app's message pump is alive so a user-blocked UI is never starved).
- **Markdown browse mode**: `Document.IsMarkdown` → `MarkdownToFlowDocument` builds a FlowDocument → hosted in `FlowDocumentScrollViewer` with width bound to viewport (prevents infinite measure from per-table scrollbars).
- **Line numbers**: `LineNumberMargin` draws only lines whose rects intersect the viewport (`GetRectFromCharacterIndex`); hidden automatically in browse mode.

## Testing

`dotnet test NotePadClone.sln -c Release` — covers markdown block/table structure and settings persistence. UI regression is currently verified manually + via UI Automation probes (out of repo).
