using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Documents;

using NotePadClone.Services;

using WpfEssentials.Base;

namespace NotePadClone.DocumentModel;
public class Document : ObservableObject, IDocument
{
    private string _content = string.Empty;
    private string _savedContent = string.Empty;

    public Document()
    {
        Metadata.TitleChanged += OnMetadataTitleChanged;
    }
    public Document(string? filePath, string content)
    {
        Metadata.TitleChanged += OnMetadataTitleChanged;
        Content = content;
        Metadata.FilePath = filePath;
        Metadata.Update(Content);
        _savedContent = content;
    }

    /// <summary>v1.2 mod addition: sync the Markdown flag when the document path or title changes.</summary>
    private void OnMetadataTitleChanged(object? sender, EventArgs e)
    {
        UpdateMarkdownState();
        OnPropertyChanged(nameof(DisplayName));
    }

    // ===== v1.2 mod addition: Markdown browse-mode state (per-document, not lost when switching tabs) =====

    private bool _isMarkdown;
    /// <summary>Whether this document is .md/.markdown (decides "Browse" button availability).</summary>
    public bool IsMarkdown
    {
        get => _isMarkdown;
        private set
        {
            if (!SetField(ref _isMarkdown, value))
                return;
            OnPropertyChanged(nameof(IsMarkdown));
        }
    }

    private bool _isBrowseMode;
    /// <summary>Whether this document is currently in browse mode (editor area shows the Markdown preview). Switching tabs does not affect each document's own mode.</summary>
    public bool IsBrowseMode
    {
        get => _isBrowseMode;
        set
        {
            if (!SetField(ref _isBrowseMode, value))
                return;
            if (value)
                RefreshPreview();
        }
    }

    private FlowDocument? _previewDocument;
    /// <summary>Rendered Markdown result for browse mode (non-null only for .md documents).</summary>
    public FlowDocument? PreviewDocument
    {
        get => _previewDocument;
        private set => SetField(ref _previewDocument, value);
    }

    public void RefreshPreview()
    {
        if (!IsMarkdown)
        {
            PreviewDocument = null;
            return;
        }
        PreviewDocument = MarkdownToFlowDocument.Render(Content ?? string.Empty);
    }

    private void UpdateMarkdownState()
    {
        var isMd = IsMarkdownPath(Metadata.FilePath);
        if (IsMarkdown == isMd)
            return;
        IsMarkdown = isMd;
        if (isMd)
            RefreshPreview();
        else
            PreviewDocument = null;
    }

    private static bool IsMarkdownPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        var ext = Path.GetExtension(path);
        return string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".markdown", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool IsDirty => Content != _savedContent;

    /// <inheritdoc />
    public string DisplayName => Metadata.Title + (IsDirty ? " •" : string.Empty);

    /// <inheritdoc />
    public void MarkSaved()
    {
        _savedContent = Content;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(DisplayName));
    }
    public DocumentMetadata Metadata { get; } = new();

    public string Content
    {
        get => _content;
        set
        {
            if (!SetField(ref _content, value))
                return;

            Metadata.Update(Content);
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(DisplayName));

            // In browse mode (or once a preview has ever been generated), keep the preview live-updating; never touch Content/dirty flags.
            if (IsMarkdown && _previewDocument is not null)
                RefreshPreview();
        }
    }
}
