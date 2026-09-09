using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("NotePadCloneTests")]

namespace NotePadClone.Services;

/// <summary>
/// v1.2.1 modded addition: minimal settings persistence (%LOCALAPPDATA%\NotePadClone\settings.json).
/// Currently: the file list's "last root folder" and the v1.3 "line-number toggle" — each flushed to disk immediately.
/// Writes flush to disk immediately (the settings payload is tiny; no debounce needed).
/// </summary>
public sealed class SettingsService
{
    private readonly string _settingsPath;
    private Dictionary<string, string?> _values;

    public SettingsService() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NotePadClone",
        "settings.json"))
    { }

    /// <summary>v1.3: explicit settings-file path (unit tests use a temp file; never pollutes real %LOCALAPPDATA% settings).</summary>
    internal SettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;
        _values = Load(settingsPath);
    }

    /// <summary>File-list root folder at last startup/shutdown; null if never set.</summary>
    public string? LastExplorerRoot
    {
        get => _values.TryGetValue("LastExplorerRoot", out var v) ? v : null;
        set
        {
            _values["LastExplorerRoot"] = value;
            Save();
        }
    }

    /// <summary>v1.3: line-number display toggle. Default (never set) = off.</summary>
    public bool ShowLineNumbers
    {
        get => _values.TryGetValue("ShowLineNumbers", out var v)
            && bool.TryParse(v, out var b)
            && b;
        set
        {
            _values["ShowLineNumbers"] = value ? "true" : "false";
            Save();
        }
    }

    private static Dictionary<string, string?> Load(string settingsPath)
    {
        try
        {
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                var data = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
                if (data is not null)
                    return data;
            }
        }
        catch (Exception)
        {
            // Corrupt settings file: treated as nonexistent; never blocks app startup.
        }
        return new Dictionary<string, string?>();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var json = JsonSerializer.Serialize(_values);
            var tmp = _settingsPath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(_settingsPath))
                File.Delete(_settingsPath);
            File.Move(tmp, _settingsPath);
        }
        catch (Exception)
        {
            // Unwritable settings (read-only directory etc.) do not affect core functionality.
        }
    }
}