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
    private IDocument _selectedDocument;

    public IDocument SelectedDocument
    {
        get => _selectedDocument;
        set => SetField(ref _selectedDocument, value);
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

    // ===== 魔改新增 =====

    private TextWrapping _wordWrapMode = TextWrapping.Wrap;
    /// <summary>自動換行開關（默認開，與 Win11 記事本一致）。綁定到編輯区 TextBox.TextWrapping。</summary>
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
    /// <summary>頂部文件路徑欄可見與否。</summary>
    public bool ShowPathBar
    {
        get => _showPathBar;
        set => SetField(ref _showPathBar, value);
    }

    public DelegateCommand CopyPathCommand { get; }

    public MainWindowVm(IWindowService windowService, IDocumentService documentService) : base(windowService)
    {
        _documentService = documentService;
        Documents.Add(_documentService.CreateNewDocument());
        _selectedDocument = Documents.First();

        OpenNewTabCommand = new DelegateCommand(_ => OpenNewTab());
        CloseTabCommand = new DelegateCommand(doc => CloseDocument(doc));
        OpenDocumentCommand = new DelegateCommand(_ => OpenDocument());
        SaveDocumentCommand = new DelegateCommand(_ => SaveDocument(SelectedDocument));
        SaveDocumentAsCommand = new DelegateCommand(_ => SaveDocumentAs(SelectedDocument));
        SaveOpenDocumentsCommand = new DelegateCommand(_ => SaveOpenDocuments());
        CopyPathCommand = new DelegateCommand(_ => CopyPathToClipboard());
    }

    /// <summary>魔改新增：把當前分頁文件嘅完整路徑複製到剪貼簿。</summary>
    private void CopyPathToClipboard()
    {
        var path = SelectedDocument?.Metadata.FilePath;
        if (!string.IsNullOrEmpty(path))
        {
            Clipboard.SetText(path);
        }
    }
       

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
            $"\"{name}\" has unsaved changes.\n\nDo you want to save before closing?",
            "NotePad Clone",
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
