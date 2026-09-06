using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using WpfEssentials.Base;

namespace NotePadClone.DocumentModel;
public class Document : ObservableObject, IDocument
{
    private string _content = string.Empty;
    private string _savedContent = string.Empty;

    public Document()
    {
        Metadata.TitleChanged += (_, _) => OnPropertyChanged(nameof(DisplayName));
    }
    public Document(string? filePath, string content)
    {
        Content = content;
        Metadata.FilePath = filePath;
        Metadata.Update(Content);
        _savedContent = content;
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
        }
    }
}
