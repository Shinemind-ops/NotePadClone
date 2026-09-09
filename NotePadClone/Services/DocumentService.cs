using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using NotePadClone.DocumentModel;

namespace NotePadClone.Services;
public class DocumentService : IDocumentService
{
    public IDocument? OpenDocument(Func<string?>? fileSelectionHandler)
    {
        var filePath = fileSelectionHandler?.Invoke();
        if (string.IsNullOrEmpty(filePath))
            return null;

        var content = File.ReadAllText(filePath);
        if (content is null)
            return null;

        return new Document(filePath, content);
    }

    public void SaveAs(Func<string?>? fileSelectionHandler, IDocument document)
    {
        var filePath = fileSelectionHandler?.Invoke();
        if (string.IsNullOrEmpty(filePath))
            return;

        document.Metadata.FilePath = filePath;
        File.WriteAllText(filePath, document.Content);
        document.MarkSaved();
    }

    public void Save(IDocument document)
    {
        if (string.IsNullOrEmpty(document.Metadata.FilePath))
            return;

        File.WriteAllText(document.Metadata.FilePath, document.Content);
        document.MarkSaved();
    }

    public IDocument CreateNewDocument() => new Document();
}
