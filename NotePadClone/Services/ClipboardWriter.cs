using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;

namespace NotePadClone.Services;

/// <summary>
/// Modded addition: safe clipboard writing.
/// Local always-on clipboard-history/sync software makes Clipboard OpenClipboard block for a long
/// time while contending for the lock (measured ~0.4s in v1.1; v1.4.2 CC-107: the owner hit 「複製路徑即沒有回應 0.5s」 (copy path → 0.5s unresponsive)).
/// Dispatching back to the UI thread (old approach) only postpones the block, it does not remove
/// it — SetText itself still waits synchronously for the other side to confirm. Therefore the final design:
///  1. All writes are delegated to one resident STA worker thread; the UI thread never touches OpenClipboard;
///  2. The worker thread retries failures 3 times (50ms apart); all exceptions are swallowed;
///  3. Writes run in order (FIFO); a later write never clobbers an earlier one.
/// </summary>
public static class ClipboardWriter
{
    private const int MaxAttempts = 3;

    private static readonly BlockingCollection<Action> Queue = new(new ConcurrentQueue<Action>());
    private static readonly Thread Worker = EnsureWorker();

    private static Thread EnsureWorker()
    {
        var t = new Thread(WorkerLoop)
        {
            Name = "ClipboardWriter",
            IsBackground = true,
        };
        t.SetApartmentState(ApartmentState.STA); // Clipboard API requires STA
        t.Start();
        return t;
    }

    private static void WorkerLoop()
    {
        foreach (var work in Queue.GetConsumingEnumerable())
        {
            try { work(); }
            catch { /* never crashes through */ }
        }
    }

    /// <summary>
    /// Write text to the clipboard (UI thread returns immediately; the actual write is queued on the dedicated STA thread).
    /// Any failure is swallowed.
    /// </summary>
    public static void WriteText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        Queue.Add(() =>
        {
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                try
                {
                    SetTextCore(text);
                    return;
                }
                catch
                {
                    // Transient failures such as CLIPBRD_E_CANT_OPEN: retry after 50ms.
                    Thread.Sleep(50);
                }
            }
        });
    }

    /// <summary>
    /// Calls the Win32 clipboard APIs (OpenClipboard/EmptyClipboard/SetClipboardData) directly on the STA worker thread,
    /// bypassing the WPF Clipboard class — avoiding its implicit dependency on Application.Current.Dispatcher.
    /// </summary>
    private static void SetTextCore(string text)
    {
        if (!OpenClipboard(IntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!EmptyClipboard())
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            var bytes = Encoding.Unicode.GetByteCount(text + "\0");
            var hmem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
            if (hmem == IntPtr.Zero)
                throw new OutOfMemoryException("GlobalAlloc 失敗");
            var ptr = GlobalLock(hmem);
            try
            {
                System.Runtime.InteropServices.Marshal.Copy(Encoding.Unicode.GetBytes(text + "\0"), 0, ptr, bytes);
            }
            finally
            {
                GlobalUnlock(hmem);
            }
            if (SetClipboardData(CF_UNICODETEXT, hmem) == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            // After success, ownership of hmem belongs to the system; GlobalFree must not be called again.
            return;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);
}
