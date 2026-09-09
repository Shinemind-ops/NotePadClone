using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using WpfEssentials.Base;

namespace NotePadClone.DocumentModel;

/// <summary>
/// Represents the metadata of a document.
/// </summary>
public class DocumentMetadata : ObservableObject
{
    private const string TitleUntitled = "未命名";
    private const string visibleCharactersRegexStr = @"[^\p{Cc}^\p{Cn}^\p{Cs}]";

    public string? _filePath;
    private string _title = TitleUntitled;
    public int _numberOfCharacters;
    public int _numberOfLines;
    public int _fileSizeInBytes;

    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (!SetField(ref _filePath, value))
                return;
            
            Title = GetTitle();
        }
    }

    public string Title
    {
        get => _title;
        set
        {
            if (!SetField(ref _title, value))
                return;

            // Keep the owning document's tab label in sync when the title changes.
            TitleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised when the title changes so the document can refresh its display name.</summary>
    public event EventHandler? TitleChanged;

    public int NumberOfCharacters
    {
        get => _numberOfCharacters;
        set => SetField(ref _numberOfCharacters, value);
    }
    public int NumberOfLines
    {
        get => _numberOfLines;
        set => SetField(ref _numberOfLines, value);
    }
    public int FileSizeInBytes
    {
        get => _fileSizeInBytes;
        set => SetField(ref _fileSizeInBytes, value);
    }

    /// <summary>
    /// Updates the document metadata.
    /// </summary>
    /// <param name="documentContent">Content of the document.</param>
    public void Update(string documentContent)
    {
        NumberOfCharacters = GetNumberOfVisibleCharacters(documentContent);
        NumberOfLines = GetNumberOfLines(documentContent);
        FileSizeInBytes = GetFileSizeInBytes();
        Title = GetTitle();
    }

    private string GetTitle()
    {
        if (string.IsNullOrEmpty(FilePath))
        {
            return TitleUntitled;
        }

        return Path.GetFileName(FilePath);
    }

    private static int GetNumberOfVisibleCharacters(string documentContent)
    {
        if (string.IsNullOrEmpty(documentContent))
        {
            return 0;
        }
        
        return Regex.Matches(documentContent, visibleCharactersRegexStr).Count;
    }

    private static int GetNumberOfNewLines(string documentContent) => Regex.Matches(documentContent ?? string.Empty, Environment.NewLine).Count;

    private int GetNumberOfLines(string documentContent)
    {
        var numberOfNewLines = GetNumberOfNewLines(documentContent);

        if (string.IsNullOrEmpty(documentContent)
            || (numberOfNewLines == 0 && NumberOfCharacters == 0))
        {
            return 0;
        }

        return numberOfNewLines + 1;
    }

    private int GetFileSizeInBytes()
    {
        if (string.IsNullOrEmpty(FilePath))
        {
            return 0;
        }

        return (int)Convert.ToInt64(new FileInfo(FilePath).Length);
    }
}
