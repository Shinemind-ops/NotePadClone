# Contributing

Thanks for interest! This fork is maintained by an AI agent on behalf of a Traditional-Chinese user, which means:

- Issues and PRs are read daily, but responses may batch up.
- Changes must keep `dotnet build -c Release` at 0 warnings/errors and all tests green.

## Development setup

1. Install .NET 8 SDK.
2. `git clone` this repo — it is self-contained, no companion repos to build.
3. `dotnet build NotePadClone.sln -c Release`
4. `dotnet test NotePadClone.sln -c Release`

## Conventions

- **Architecture**: MVVM. Views in `Controls/` + `MainWindow.xaml`, state in `ViewModels/`, logic in `Services/` and `DocumentModel/`.
- **UI text**: Traditional Chinese (繁體中文). Keep new user-facing strings in TC Chinese.
- **Code comments**: English. (They were Chinese during private development; translated for open source.)
- **Clipboard code**: all clipboard writes must go through `Services/ClipboardWriter.cs` (non-blocking STA thread). Do not write `Clipboard.SetText` on the UI thread — it reintroduces a UI freeze of up to seconds when third-party clipboard holders stall the ownership release.
- **Markdown rendering**: `Services/MarkdownToFlowDocument.cs`. Beware: FlowDocument + `BlockUIContainer` sizing interacts badly with viewport-sized hosts (`FlowDocumentScrollViewer` needs explicit width binding to avoid infinite-measure). Test table rendering after changes.

## PR checklist

- [ ] `dotnet build` — 0 errors, 0 new warnings
- [ ] `dotnet test` — all pass; add a test for new logic where practical
- [ ] Manual smoke: open a `.md` file, toggle dark mode, copy a path (UI must not stutter)
- [ ] No local absolute paths (`C:\Users\...`) in code or config
