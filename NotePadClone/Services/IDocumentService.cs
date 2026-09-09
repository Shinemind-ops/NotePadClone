using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using NotePadClone.DocumentModel;

namespace NotePadClone.Services;

/// <summary>
/// Service for opening and saving documents.
/// </summary>
public interface IDocumentService
{
    /// <summary>
    /// Creates a new document.
    /// </summary>
    /// <returns>An empty document.</returns>
    IDocument CreateNewDocument();

    ///<summary>
    /// Opens a file and returns its content.
    /// </summary>
    /// <param name="fileSelectionHandler">Delegate to open the file selection dialog.</param>
    /// <returns>The content of the file.</returns>
    IDocument? OpenDocument(Func<string?>? fileSelectionHandler);

    /// <summary>
    /// Saves a document to a file.
    /// </summary>
    /// <param name="fileSelectionHandler">Delegate to open the save file dialog.</param>
    /// <param name="document">Document to save to the chosen path.</param>
    void SaveAs(Func<string?>? fileSelectionHandler, IDocument document);

    /// <summary>
    /// Saves a document to its filepath.
    /// </summary>
    /// <param name="document">Document to save.</param>
    void Save(IDocument document);    
}
