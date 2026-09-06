using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NotePadClone.DocumentModel;

/// <summary>
/// Represents a document.
/// </summary>
public interface IDocument
{
    string Content { get; set; }
    DocumentMetadata Metadata { get; }

    /// <summary>True when the content has been modified since it was last saved.</summary>
    bool IsDirty { get; }

    /// <summary>Tab display title: file name plus a bullet marker when unsaved changes exist.</summary>
    string DisplayName { get; }

    /// <summary>Marks the current content as saved (clears the dirty flag).</summary>
    void MarkSaved();
}
