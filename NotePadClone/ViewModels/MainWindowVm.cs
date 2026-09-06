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

    // ===== mod additions =====

    private TextWrapping _wordWrapMode = TextWrapping.Wrap;
    /// <summary>Word-wrap toggle (default on, matching Win11 Notepad). Bound to the editor TextBox.TextWrapping.</summary>
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
    /// <summary>Visibility of the file path bar at the top.</summary>
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

    /// <summary>mod: copy the full path of the current tab's file to the clipboard.</summary>
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

        switch (Documents.Count)
        {
            case 1:
                CloseWindowCommand.Execute(null);
                break;

            default:
                var index = Documents.IndexOf(doc);
                Documents.Remove(doc);
                SelectedDocument = Documents[Math.Min(index, Documents.Count - 1)];
                break;
        }
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
