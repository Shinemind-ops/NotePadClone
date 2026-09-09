# NotePadClone — Traditional-Chinese Enhanced Fork

A fork of [thomaswening/NotePadClone](https://github.com/thomaswening/NotePadClone) — the modern Windows-11-style Notepad reimagining — taken further for daily professional use: rendered Markdown viewing, a file-explorer sidebar, line numbers, a fully localized Traditional-Chinese UI, and a set of real-world reliability fixes (most notably: **copying never freezes the UI**, even when third-party clipboard software stalls the Windows clipboard chain).

## Modifications in this fork

| Area | What changed | Why |
|---|---|---|
| Markdown viewing | `.md` files render as formatted documents (headings, tables, code) with a per-document Browse/Edit toggle; tables get their own horizontal scrollbars | Read rendered Markdown without leaving Notepad; wide tables no longer stretch the window |
| File explorer sidebar | Open-folder, right-click menu (set as root / refresh / reveal), `FileSystemWatcher` auto-refresh, remembers last root | Quick navigation between notes in a folder |
| Line numbers | Status-bar toggle; custom virtualized gutter (`GetRectFromCharacterIndex`); auto-widens with digit count; hidden in Markdown browse mode; persisted | Code editing; also fixes stock-gutter offset bugs on wrapped/long documents |
| Clipboard: non-blocking copy | All clipboard writes go through a dedicated STA worker thread with bounded retries and liveness probing; WPF's built-in `Copy` command is routed through it too | Windows requires releasing clipboard ownership before a new write; a third-party clipboard consumer (sync tools, IME cloud clipboard) holding the chain froze the UI ~0.5s per copy. Now the UI never blocks on copy |
| Cut latency | Clipboard write dispatched off the render path | 0.4s visual lag after Ctrl+X |
| Open-with / CLI | Startup reads `e.Args` and opens the file through the live window; a pristine untitled tab auto-closes | "Open with → this editor" from Explorer works |
| Unsaved-changes prompt | Dialog before closing a dirty document; untitled+empty windows close silently | No silent data loss, no nagging |
| Word wrap & path bar | Menu options; path bar with one-click copy of the full path | Daily QoL |
| Status-bar toggle groups | Four toggles (dark / sidebar / browse / line numbers), each label hugging its own switch (4px) with 1px theme-colored dividers between groups | Proximity ambiguity: it was unclear which label belonged to which switch |
| Traditional-Chinese UI | Menus, dialogs, tooltips fully localized | Primary users are TC-Chinese readers |
| Self-contained build | In-repo shim replaces upstream's external `WpfEssentials` DLL requirement | Upstream needs a companion repo built by hand before compiling; here `dotnet build` just works |

## Building

Prerequisites: .NET 8 SDK only.

```
dotnet build NotePadClone.sln -c Release
dotnet test NotePadClone.sln -c Release        # 12 unit tests
```

Run: `NotePadClone/bin/Release/net8.0-windows/NotePadClone.exe`

## Documentation

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — project layout and key flows
- [CONTRIBUTING.md](CONTRIBUTING.md) — setup, conventions, PR checklist

## License

GPL-3.0 (same as upstream — this fork stays under GPLv3). Original code: Thomas Wening. See [LICENSE.txt](LICENSE.txt).
