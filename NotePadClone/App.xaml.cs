using System.CodeDom;
using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Threading;

using Microsoft.Extensions.DependencyInjection;
using NotePadClone.Services;
using NotePadClone.ViewModels;

namespace NotePadClone
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private IServiceProvider? _serviceProvider;

        /// <summary>Error log for unhandled exceptions (%LOCALAPPDATA%\NotePadClone\errors.log).</summary>
        private static readonly string ErrorLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NotePadClone",
            "errors.log");

        protected override void OnStartup(StartupEventArgs e)
        {
            // v1.2 crash fix: app-wide safety net. An unhandled exception killing the process means all
            // the user's unsaved changes are lost — the single most unacceptable failure mode for this app.
            // Any unhandled exception reaching the UI thread is logged + surfaced in a dialog, then e.Handled = true keeps the process alive.
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            _serviceProvider = serviceCollection.BuildServiceProvider();

            var mainWindow = _serviceProvider.GetService<MainWindow>() ?? throw new InvalidOperationException("Could not resolve MainWindow.");
            mainWindow.Show();

            // CC-106: supports "Open with > NotePadClone mod" from Explorer and files passed on the command line.
            // Explorer passes the full path of the clicked file in args; previously OnStartup ignored
            // e.Args and dropped everything, which is why it always opened "Untitled". Take the first arg that is an existing file path,
            // and after Loaded open it via the ViewModel's existing open-file flow (including already-open tab switching logic).
            string? startupFile = e.Args
                .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)
                    && !a.StartsWith('-')
                    && !a.StartsWith('/')
                    && File.Exists(a));
            if (startupFile is not null)
            {
                // Note: MainWindowVm is registered as Transient, so GetService would create a new instance with no window attached
                // (the file would open into the void). Must use the window's own DataContext — that is the
                // VM actually being displayed. ContentRendered fires once and unsubscribes itself.
                EventHandler handler = null!;
                handler = (_, _) =>
                {
                    mainWindow.ContentRendered -= handler;
                    try
                    {
                        if (mainWindow.DataContext is MainWindowVm vm)
                            vm.OpenFileFromPathOnStartup(startupFile);
                    }
                    catch
                    {
                        // If the file can't be opened, still don't crash the process (v1.2 safety-net spirit); the user can open it manually.
                    }
                };
                mainWindow.ContentRendered += handler;
            }
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IWindowService, WindowService>();
            services.AddSingleton<IDocumentService, DocumentService>();
            services.AddSingleton<SettingsService>(); // v1.2.1: settings persistence (remember the file-list root directory)
            services.AddTransient<MainWindowVm>();
            services.AddTransient<MainWindow>();
        }

        /// <summary>
        /// v1.2 crash fix: DispatcherUnhandledException global safety net.
        /// Log → dialog explaining what happened → e.Handled = true to keep running;
        /// never let an unhandled exception silently take down the whole process.
        /// </summary>
        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                AppendErrorLog(e.Exception);
            }
            catch
            {
                // If even the log can't be written, absolutely no re-throw.
            }

            try
            {
                MessageBox.Show(
                    $"發生未處理的錯誤，程式不會關閉：\n\n{e.Exception.Message}\n\n錯誤記錄已寫入：\n{ErrorLogPath}",
                    "記事本",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // If the dialog fails (extreme case), absolutely no re-throw.
            }

            // Swallow this exception; keep the process and the user's unsaved changes alive.
            e.Handled = true;
        }

        private static void AppendErrorLog(Exception exception)
        {
            var dir = Path.GetDirectoryName(ErrorLogPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}\r\n----\r\n";
            File.AppendAllText(ErrorLogPath, entry);
        }
    }
}
