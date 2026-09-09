using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;

using NotePadClone.DocumentModel;
using NotePadClone.Services;

using WpfEssentials.Base;

namespace NotePadClone.ViewModels;

/// <summary>
/// View model for the main window, representing the text editor.
/// </summary>
public class MainWindowVm : WindowVm
{
    
    private readonly IDocumentService _documentService;
    private readonly SettingsService _settingsService = null!; // DI assigns in the constructor; WPF dual-compile falsely reports CS8618, so an initializer avoids the landmine
    private IDocument _selectedDocument;

    public IDocument SelectedDocument
    {
        get => _selectedDocument;
        set
        {
            if (!SetField(ref _selectedDocument, value))
                return;
            FollowSelectedDocumentDirectory();
        }
    }

    /// <summary>
    /// v1.2: when switching to a tab with a real path, the sidebar root automatically follows that file's directory.
    /// v1.2.1 change: once the user locks the root via 「開啟資料夾…」 (Open Folder…) / 「設為根目錄」 (Set as Root), it never auto-moves again
    /// — the app must not snatch away a folder the user picked by hand (vs. the complaint that the choice "is completely out of the user's hands").
    /// Auto-follow is kept only while unlocked (default 「我的文件夾」 (My Documents)), so it pays off from the first file opened.
    /// </summary>
    private void FollowSelectedDocumentDirectory()
    {
        if (_explorerRootIsDefault == false)
            return;
        var path = _selectedDocument?.Metadata.FilePath;
        if (string.IsNullOrEmpty(path))
            return;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            SetExplorerRoot(dir);
    }

    public ObservableCollection<IDocument> Documents { get; } = [ ];

    /// <summary>
    /// Delegate to open the file selection dialog in the view that uses this view model and return the selected path.
    /// </summary>
    public Func<string?>? OpenFileSelectionDialogHandler { get; set; }

    /// <summary>
    /// Delegate to open the save file dialog in the view that uses this view model and return the file's selected path.
    /// </summary>
    public Func<string?>? OpenSaveFileDialogHandler { get; set; }

    public DelegateCommand OpenNewTabCommand { get; }
    public DelegateCommand CloseTabCommand { get; }
    public DelegateCommand OpenDocumentCommand { get; }
    public DelegateCommand SaveDocumentCommand { get; }
    public DelegateCommand SaveDocumentAsCommand { get; }
    public DelegateCommand SaveOpenDocumentsCommand { get; }

    // ===== v1.3 modded addition: line-number display toggle (global setting, same across all tabs) =====

    private bool _showLineNumbers;
    /// <summary>Line-number visibility (global). Binds the status bar 「行號」 (Line Numbers) ToggleButton; persisted immediately.</summary>
    public bool ShowLineNumbers
    {
        get => _showLineNumbers;
        set
        {
            if (!SetField(ref _showLineNumbers, value))
                return;
            if (_settingsService is not null)
                _settingsService.ShowLineNumbers = value;
        }
    }

    // ===== Modded additions =====

    private TextWrapping _wordWrapMode = TextWrapping.Wrap;
    /// <summary>Auto-wrap toggle (default on, matching Win11 Notepad). Bound to the editing area TextBox.TextWrapping.</summary>
    public TextWrapping WordWrapMode
    {
        get => _wordWrapMode;
        set => SetField(ref _wordWrapMode, value);
    }

    public bool WordWrapEnabled
    {
        get => _wordWrapMode == TextWrapping.Wrap;
        set
        {
            WordWrapMode = value ? TextWrapping.Wrap : TextWrapping.NoWrap;
            OnPropertyChanged(nameof(WordWrapEnabled));
        }
    }

    private bool _showPathBar;
    /// <summary>Whether the top file-path bar is visible.</summary>
    public bool ShowPathBar
    {
        get => _showPathBar;
        set => SetField(ref _showPathBar, value);
    }

    public DelegateCommand CopyPathCommand { get; }

    // ===== v1.2 modded addition: left file-tree sidebar =====

    private double _sidebarWidth = 220;
    private bool _showSidePanel = true;
    private string? _explorerRootPath;
    private bool _explorerRootIsDefault = true;
    private FileSystemWatcher? _explorerWatcher;
    private DispatcherTimer? _explorerRefreshDebounce;

    /// <summary>Sidebar visibility (two-way synced with the bottom 「側欄」 (Sidebar) toggle button).</summary>
    public bool ShowSidePanel
    {
        get => _showSidePanel;
        set => SetField(ref _showSidePanel, value);
    }

    /// <summary>
    /// v1.2.1 StackOverflow fix — reentrancy guard: true while a programmatic reveal (SelectItem) is in progress.
    /// The TreeView's synchronous IsSelected binding easily re-fires SelectedItemChanged for a programmatic selection,
    /// so the code-behind handler skips opening files when it sees true, breaking the self-locking loop.
    /// </summary>
    public bool IsProgrammaticSelect
    {
        get => _isProgrammaticSelect;
        private set => SetField(ref _isProgrammaticSelect, value);
    }
    private bool _isProgrammaticSelect;

    /// <summary>Sidebar expanded width (px); saved while hidden, restored when shown. Updated live while dragging the splitter.</summary>
    public double SidebarWidth
    {
        get => _sidebarWidth;
        set => SetField(ref _sidebarWidth, value);
    }

    /// <summary>Sidebar root folder (follows the current file's directory; 「我的文件夾」 (My Documents) when no file is open).</summary>
    public string? ExplorerRootPath
    {
        get => _explorerRootPath;
        private set => SetField(ref _explorerRootPath, value);
    }

    /// <summary>File-tree root node (single root folder, first level auto-expanded).</summary>
    public ObservableCollection<FileExplorerItem> ExplorerRootItems { get; } = new();

    /// <summary>Picker for the file list 「開啟資料夾…」 (Open Folder…) button (supplies a real window; injected by MainWindow).</summary>
    public Func<string?>? OpenFolderDialogHandler { get; set; }

    public DelegateCommand OpenFolderCommand { get; }

    public MainWindowVm(IWindowService windowService, IDocumentService documentService, SettingsService settingsService) : base(windowService)
    {
        _documentService = documentService;
        _settingsService = settingsService;
        Documents.Add(_documentService.CreateNewDocument());
        _selectedDocument = Documents.First();
        _showLineNumbers = _settingsService?.ShowLineNumbers ?? false; // v1.3: restore the line-number toggle at startup (default off)

        OpenNewTabCommand = new DelegateCommand(_ => OpenNewTab());
        CloseTabCommand = new DelegateCommand(doc => CloseDocument(doc));
        OpenDocumentCommand = new DelegateCommand(_ => OpenDocument());
        SaveDocumentCommand = new DelegateCommand(_ => SaveDocument(SelectedDocument));
        SaveDocumentAsCommand = new DelegateCommand(_ => SaveDocumentAs(SelectedDocument));
        SaveOpenDocumentsCommand = new DelegateCommand(_ => SaveOpenDocuments());
        CopyPathCommand = new DelegateCommand(_ => CopyPathToClipboard());
        OpenFolderCommand = new DelegateCommand(_ => OpenFolder());

        // Sidebar initial root: restore last session's root if it still exists, otherwise 「我的文件夾」 (My Documents).
        var savedRoot = _settingsService?.LastExplorerRoot;
        var defaultDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _explorerRootIsDefault = string.IsNullOrEmpty(savedRoot) || !Directory.Exists(savedRoot);
        SetExplorerRoot(!_explorerRootIsDefault ? savedRoot! : defaultDir);
    }

    /// <summary>
    /// v1.2: set the sidebar root and rebuild the tree (root node auto-expanded, first level scanned only).
    /// v1.2.1 addition: the same root refreshes in place (keeping expanded subfolders); only a root change rebuilds;
    /// every set persists immediately (remembered root) and re-arms the FileSystemWatcher.
    /// </summary>
    public void SetExplorerRoot(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return;

        var sameRoot = string.Equals(ExplorerRootPath, directory, StringComparison.OrdinalIgnoreCase);
        ExplorerRootPath = directory;

        if (sameRoot && ExplorerRootItems.FirstOrDefault() is { IsDirectory: true } existingRoot)
        {
            RefreshExplorerTree();
            return;
        }

        ExplorerRootItems.Clear();
        var root = new FileExplorerItem(directory);
        ExplorerRootItems.Add(root);
        root.IsExpanded = true;

        if (_settingsService is not null)
            _settingsService.LastExplorerRoot = directory;
        RestartExplorerWatcher(directory);
    }

    /// <summary>
    /// v1.2.1: the 「開啟資料夾…」 (Open Folder…) button — opens a folder picker to lock the root manually.
    /// Once locked, auto-follow (FollowSelectedDocumentDirectory) stops — control returns to the user.
    /// </summary>
    private void OpenFolder()
    {
        if (OpenFolderDialogHandler is null)
            return;
        var directory = OpenFolderDialogHandler();
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return;
        LockExplorerRoot(directory);
    }

    /// <summary>v1.2.1: right-click 「設為根目錄」 (Set as Root) — locks the user-picked root folder.</summary>
    public void LockExplorerRoot(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return;
        _explorerRootIsDefault = false;
        SetExplorerRoot(directory);
    }

    /// <summary>v1.2.1: auto-refresh the tree when files/folders under the root are added/deleted/renamed externally. Watches only one level of subdirectories.</summary>
    public void RefreshExplorerTree()
    {
        var root = ExplorerRootItems.FirstOrDefault();
        if (root is null || string.IsNullOrEmpty(ExplorerRootPath))
            return;

        // Remember which direct subfolders were expanded; re-expand them after the rescan.
        var expandedPaths = root.Children
            .Where(c => c.IsDirectory && c.IsExpanded)
            .Select(c => c.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        root.ReloadChildren();

        foreach (var sub in root.Children)
        {
            if (sub.IsDirectory && expandedPaths.Contains(sub.FullPath))
                sub.IsExpanded = true; // setter triggers lazy loading
        }
    }

    /// <summary>v1.2.1: right-click 「重新整理」 (Refresh) — folder nodes rescan themselves; file nodes refresh their parent folder.</summary>
    public void RefreshExplorerNode(FileExplorerItem? item)
    {
        if (item is null)
            return;
        if (item.IsDirectory)
        {
            item.ReloadChildren();
            return;
        }
        var parent = item.Parent ?? ExplorerRootItems.FirstOrDefault();
        if (parent is { IsDirectory: true })
            parent.ReloadChildren();
    }

    // ===== v1.2.1 FileSystemWatcher auto-refresh =====

    private void RestartExplorerWatcher(string rootPath)
    {
        if (_explorerWatcher is not null)
        {
            _explorerWatcher.EnableRaisingEvents = false;
            _explorerWatcher.Dispose();
            _explorerWatcher = null;
        }

        try
        {
            var watcher = new FileSystemWatcher(rootPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            };
            watcher.Created += OnExplorerFsChanged;
            watcher.Deleted += OnExplorerFsChanged;
            watcher.Changed += OnExplorerFsChanged;
            watcher.Renamed += OnExplorerFsRenamed;
            watcher.EnableRaisingEvents = true;
            _explorerWatcher = watcher;
        }
        catch (Exception)
        {
            // If watching fails (permissions/network drive etc.), manual sidebar refresh still works.
        }
    }

    private void OnExplorerFsChanged(object sender, FileSystemEventArgs e) => ScheduleExplorerRefresh(e.FullPath);

    private void OnExplorerFsRenamed(object sender, RenamedEventArgs e) => ScheduleExplorerRefresh(e.FullPath);

    /// <summary>Event-storm protection: all changes coalesce into one debounce(300ms) timer; the tree refreshes once things go quiet.</summary>
    private void ScheduleExplorerRefresh(string fullPath)
    {
        if (IsBeyondWatchedDepth(fullPath))
            return;

        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (_explorerRefreshDebounce is null)
            {
                _explorerRefreshDebounce = new DispatcherTimer(
                    TimeSpan.FromMilliseconds(300),
                    DispatcherPriority.Background,
                    (_, _) => StopExplorerDebounceAndRefresh(),
                    dispatcher);
            }
            _explorerRefreshDebounce.Stop();
            _explorerRefreshDebounce.Start();
        }));
    }

    private void StopExplorerDebounceAndRefresh()
    {
        _explorerRefreshDebounce?.Stop();
        _explorerRefreshDebounce = null;
        RefreshExplorerTree();
    }

    /// <summary>Only watch changes under the root + one level of subdirectories (depth ≤ 2); ignore deeper ones to save resources.</summary>
    private bool IsBeyondWatchedDepth(string fullPath)
    {
        if (string.IsNullOrEmpty(ExplorerRootPath) || string.IsNullOrEmpty(fullPath))
            return true;
        var rootWithSep = ExplorerRootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            return true;
        var remainder = fullPath.Substring(rootWithSep.Length);
        return remainder.Split(Path.DirectorySeparatorChar).Length > 2;
    }

    // ===== v1.2.1 auto-reveal on file open =====

    /// <summary>If the file is in the tree, expand parent folders level by level and select it (VS Code reveal behavior).</summary>
    public void RevealInExplorer(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(ExplorerRootPath) || !IsInsideRoot(fullPath))
            return;

        var current = ExplorerRootItems.FirstOrDefault();
        if (current is null)
            return;
        current.IsExpanded = true;

        var rel = Path.GetRelativePath(ExplorerRootPath, fullPath);
        var parts = rel.Split(Path.DirectorySeparatorChar);

        var curPath = ExplorerRootPath;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            curPath = Path.Combine(curPath, parts[i]);
            var child = current.Children.FirstOrDefault(c => c.IsDirectory && string.Equals(c.FullPath, curPath, StringComparison.OrdinalIgnoreCase));
            if (child is null)
            {
                current.ReloadChildren(); // Folder just appeared — rescan once
                child = current.Children.FirstOrDefault(c => c.IsDirectory && string.Equals(c.FullPath, curPath, StringComparison.OrdinalIgnoreCase));
            }
            if (child is null)
                return;
            child.IsExpanded = true;
            current = child;
        }

        _isProgrammaticSelect = true;
        try
        {
            SelectItem(current.Children.FirstOrDefault(c => string.Equals(c.FullPath, fullPath, StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            _isProgrammaticSelect = false;
        }
    }

    private static void SelectItem(FileExplorerItem? target)
    {
        if (target is null)
            return;
        if (target.Parent is not null)
        {
            foreach (var sibling in target.Parent.Children)
                sibling.IsSelected = false;
        }
        target.IsSelected = true;
    }

    private bool IsInsideRoot(string fullPath)
    {
        if (string.IsNullOrEmpty(ExplorerRootPath))
            return false;
        var rootWithSep = ExplorerRootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fullPath, ExplorerRootPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>v1.2: single-click a text file in the sidebar → switch to it if already open, else open a new Tab via the existing open flow.</summary>
    public void OpenFileFromExplorer(string fullPath)
        => OpenFileFromPathCore(fullPath, reveal: true);

    /// <summary>CC-106: a file passed via command line / right-click "Open with" is opened directly at startup, with no Explorer reveal.</summary>
    public void OpenFileFromPathOnStartup(string fullPath)
    {
        OpenFileFromPathCore(fullPath, reveal: false);
        // The constructor pre-opens a blank 「未命名」 (Untitled) tab; after a command-line file opens successfully, if that blank
        // tab is still pristine (no path, no edits), close it — it must not crowd out the user's file.
        var blank = Documents.FirstOrDefault(d =>
            d.Metadata.FilePath is null && !d.IsDirty);
        if (blank is not null && Documents.Count > 1 && !ReferenceEquals(blank, SelectedDocument))
            Documents.Remove(blank);
    }

    private void OpenFileFromPathCore(string fullPath, bool reveal)
    {
        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            return;

        var existing = Documents.FirstOrDefault(d =>
            string.Equals(d.Metadata.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedDocument = existing;
            if (reveal) RevealInExplorer(fullPath);
            return;
        }

        // Reuse the existing IDocumentService.OpenDocument: pass a selector that returns the fixed path.
        var document = _documentService.OpenDocument(() => fullPath);
        if (document is null)
            return;

        Documents.Add(document);
        SelectedDocument = document;
        if (reveal) RevealInExplorer(fullPath);
    }

    /// <summary>Modded addition: copy the current tab file's full path to the clipboard.</summary>
    /// <remarks>
    /// v1.2 crash fix: the old synchronous Clipboard.SetText blocked the UI thread indefinitely under local clipboard-history/sync software
    /// (click → 「沒有回應」 (Not Responding) → window vanished). Now it queues an async write via ClipboardWriter at
    /// Dispatcher.Background priority, with try/catch + up to 3 retries, never blocking the UI.
    /// </remarks>
    private void CopyPathToClipboard() => ClipboardWriter.WriteText(SelectedDocument?.Metadata.FilePath);
       

    private void CloseDocument(object? document)
    {
        document ??= SelectedDocument;

        if (document is not IDocument doc) throw new ArgumentException("The document must be of type IDocument.", nameof(document));

        // mod: closing the last tab closes the window; the window's Closing handler
        // already asks about unsaved changes, so don't prompt twice here.
        if (Documents.Count <= 1)
        {
            CloseWindowCommand.Execute(null);
            return;
        }

        // mod: ask before discarding unsaved changes (returns false = user cancelled).
        if (!ConfirmDiscardDocument(doc))
            return;

        var index = Documents.IndexOf(doc);
        Documents.Remove(doc);
        SelectedDocument = Documents[Math.Min(index, Documents.Count - 1)];
    }

    /// <summary>
    /// mod: asks the user to save/discard a document that has unsaved changes.
    /// </summary>
    /// <returns>True if the document may be closed, false if the user cancelled.</returns>
    public bool ConfirmDiscardDocument(IDocument document)
    {
        if (!document.IsDirty)
            return true;

        var name = string.IsNullOrEmpty(document.Metadata.FilePath) ? document.Metadata.Title : Path.GetFileName(document.Metadata.FilePath);
        var result = MessageBox.Show(
            $"「{name}」有未儲存的變更。\n\n關閉前要先儲存嗎？",
            "記事本",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Yes);

        switch (result)
        {
            case MessageBoxResult.Yes:
                SaveDocument(document);
                // If the Save-As dialog was cancelled the document is still dirty -> treat as cancel.
                return !document.IsDirty;

            case MessageBoxResult.No:
                return true;

            default: // Cancel or dialog closed
                return false;
        }
    }

    /// <summary>
    /// mod: asks about every dirty document before the window closes.
    /// </summary>
    /// <returns>True if the window may close, false if the user cancelled.</returns>
    public bool ConfirmCloseWindow()
    {
        // Snapshot: the list may change while dialogs are open.
        foreach (var document in Documents.ToList())
        {
            if (!ConfirmDiscardDocument(document))
                return false;
        }

        return true;
    }

    private void OpenNewTab()
    {
        Documents.Add(_documentService.CreateNewDocument());
        SelectedDocument = Documents.Last();
    }   

    private void OpenDocument()
    {
        var document = _documentService.OpenDocument(OpenFileSelectionDialogHandler);
        if (document is not null)
        {
            Documents.Add(document);
            SelectedDocument = Documents.Last();
            RevealInExplorer(document.Metadata.FilePath ?? string.Empty);
        }
    }

    private void SaveDocument(IDocument document)
    {
        if (!string.IsNullOrEmpty(document.Metadata.FilePath))
        {
            _documentService.Save(document);
        }
        else
        {
            SaveDocumentAs(document);
        }
    }

    private void SaveDocumentAs(IDocument document)
    {
        _documentService.SaveAs(OpenSaveFileDialogHandler, document);
    }

    private void SaveOpenDocuments()
    {
        foreach (var document in Documents)
        {
            SaveDocument(document);
        }
    }
}
