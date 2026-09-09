using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

using WpfEssentials.Base;

namespace NotePadClone.ViewModels;

/// <summary>
/// v1.2 modded addition: tree node in the left file tree (folder / file).
/// Children are lazy-loaded: subdirectories are scanned only on expand (IsExpanded = true), never a recursive full-disk scan at startup.
/// </summary>
public class FileExplorerItem : ObservableObject
{
    /// <summary>Whitelist defining "text files"; only these open in a new Tab on single-click in the sidebar, preventing binary files from being read as garbage.</summary>
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown", ".txt", ".log", ".ini", ".json", ".xml", ".yaml", ".yml",
        ".cs", ".csproj", ".sln", ".xaml", ".py", ".js", ".ts", ".html", ".css", ".c", ".h", ".cpp",
    };

    private readonly string _fullPath;
    private bool _isExpanded;
    private bool _isSelected;
    private bool _childrenLoaded;

    public FileExplorerItem(string fullPath)
    {
        _fullPath = fullPath;
        Name = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(Name))
            Name = fullPath; // Root directory
        IsDirectory = Directory.Exists(fullPath);
    }

    public string Name { get; }

    public string FullPath => _fullPath;

    public bool IsDirectory { get; }

    public bool IsTextFile => !IsDirectory && TextExtensions.Contains(Path.GetExtension(_fullPath));

    /// <summary>Parent node (null for the root); used when rebuilding the tree to locate "refresh a file node = refresh its parent folder".</summary>
    public FileExplorerItem? Parent { get; private set; }

    public ObservableCollection<FileExplorerItem> Children { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetField(ref _isExpanded, value))
                return;
            if (value && IsDirectory)
                EnsureChildrenLoaded();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    private void EnsureChildrenLoaded()
    {
        if (_childrenLoaded)
            return;
        // Heavy directories (node_modules, .git, etc.) still show as-is; they simply obey the same "scan only on expand" rule, so the cost stays naturally low.
        _childrenLoaded = true;

        try
        {
            var dirInfo = new DirectoryInfo(_fullPath);
            var dirs = dirInfo.EnumerateDirectories()
                              .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                              .ToList();
            var files = dirInfo.EnumerateFiles()
                               .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                               .ToList();

            Children.Clear();
            foreach (var d in dirs)
            {
                var item = new FileExplorerItem(d.FullName) { Parent = this };
                Children.Add(item);
            }
            foreach (var f in files)
            {
                var item = new FileExplorerItem(f.FullName) { Parent = this };
                Children.Add(item);
            }
        }
        catch (Exception)
        {
            // Unauthorized / IO errors: treated as an empty folder.
        }
    }

    /// <summary>Force-rescan children (refresh). Callers are responsible for preserving expanded state.</summary>
    public void ReloadChildren()
    {
        if (!IsDirectory)
            return;
        _childrenLoaded = false;
        EnsureChildrenLoaded();
    }

    /// <summary>v1.2.1 AX-name fix: the node name read by assistive technology now returns the file/folder display name, not the class name.</summary>
    public override string ToString() => Name;
}