using System;
using System.IO;

using NUnit.Framework;

using NotePadClone.Services;

namespace NotePadCloneTests;

/// <summary>
/// v1.3 line-number toggle persistence tests.
/// Uses the injectable-path constructor to write a temp settings.json; never pollutes the real %LOCALAPPDATA%\NotePadClone.
/// </summary>
[TestFixture]
public class SettingsServiceTests
{
    private string? _tempPath;

    [TearDown]
    public void TearDown()
    {
        if (_tempPath is not null && File.Exists(_tempPath))
        {
            try { File.Delete(_tempPath); } catch { /* Cleanup failure does not affect test results */ }
            try
            {
                var tmp = _tempPath + ".tmp";
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch { }
        }
    }

    [Test]
    public void ShowLineNumbers_Default_IsFalse()
    {
        var settings = CreateSettings();
        Assert.That(settings.ShowLineNumbers, Is.False, "默認要關閉");
    }

    [Test]
    public void ShowLineNumbers_SetTrue_SavesAndReloadsTrue()
    {
        var settings = CreateSettings();
        settings.ShowLineNumbers = true;
        Assert.That(settings.ShowLineNumbers, Is.True, "設定後立即讀返要係 true");

        // New instance on the same path = the app-restart scenario
        var reloaded = new SettingsService(_tempPath!);
        Assert.That(reloaded.ShowLineNumbers, Is.True, "重開後要讀返 true");
    }

    [Test]
    public void ShowLineNumbers_SetFalseThenReload_IsFalse()
    {
        var settings = CreateSettings();
        settings.ShowLineNumbers = true;
        settings.ShowLineNumbers = false;

        var reloaded = new SettingsService(_tempPath!);
        Assert.That(reloaded.ShowLineNumbers, Is.False, "存咗 false 之後重開要讀返 false");
    }

    [Test]
    public void ShowLineNumbers_Document_RemainsUntouched()
    {
        // Line-number persistence must not disturb other settings such as the remembered root folder
        var settings = CreateSettings();
        settings.LastExplorerRoot = "C:\\test\\root";
        settings.ShowLineNumbers = true;

        var reloaded = new SettingsService(_tempPath!);
        Assert.That(reloaded.LastExplorerRoot, Is.EqualTo("C:\\test\\root"), "其他設定要原樣保留");
        Assert.That(reloaded.ShowLineNumbers, Is.True);
    }

    private SettingsService CreateSettings()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"np_settings_test_{Guid.NewGuid():N}.json");
        return new SettingsService(_tempPath);
    }
}