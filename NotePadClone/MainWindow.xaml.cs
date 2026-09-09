using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

using MaterialDesignThemes.Wpf;

using Microsoft.Win32;

using NotePadClone.DocumentModel;
using NotePadClone.Services;
using NotePadClone.ViewModels;

using WpfEssentials.Base;

namespace NotePadClone
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// inherits from WindowChromeCommands
    /// </summary>
    public partial class MainWindow : Window
    {
        readonly IWindowService? _windowService;
        readonly MainWindowVm? _viewModel;

        public MainWindow()
        {
            InitializeComponent();
        }

        public MainWindow(MainWindowVm viewModel, IWindowService windowService) : this()
        {
            _windowService = windowService;
            _viewModel = viewModel;
            DataContext = _viewModel;
        }

        /// <summary>
        /// mod: ask about unsaved changes before the window closes.
        /// </summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_viewModel is not null && !_viewModel.ConfirmCloseWindow())
            {
                e.Cancel = true;
                return;
            }

            base.OnClosing(e);
        }

        /// <summary>
        /// mod: reliable Cut (copy selection + delete it). We intercept Cut in the Window's
        /// PreviewExecuted stage — this runs BEFORE the TextBox class handler for Cut, so the
        /// built-in cut (whose delete step gets reverted by the editor's TwoWay Text binding)
        /// never runs. Serves both Ctrl+X and the context menu.
        /// </summary>
        private void OnPreviewExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            // CC-107: eliminate the remaining clipboard-blocking sources — WPF's built-in TextBox copy (Ctrl+C / right-click copy)
            // also calls OpenClipboard synchronously on the UI thread; any local clipboard-chain tool grabbing the lock freezes us ~0.5s.
            // Intercept in PreviewExecuted (pipeline stage, before the TextBox class handler), same as Cut, and
            // route the write through ClipboardWriter (a persistent STA worker thread) so the UI thread returns immediately.
            if (e.Command == ApplicationCommands.Copy)
            {
                if (e.OriginalSource is TextBox copyBox && copyBox.SelectionLength > 0)
                {
                    ClipboardWriter.WriteText(copyBox.SelectedText);
                    e.Handled = true;
                }
                return;
            }

            if (e.Command != ApplicationCommands.Cut)
                return;

            if (e.OriginalSource is not TextBox box || box.SelectionLength == 0)
            {
                // Nothing to cut; swallow the broken built-in anyway.
                e.Handled = true;
                return;
            }

            var selected = box.SelectedText;
            var caret = box.SelectionStart;

            // Delete first — this order cannot be undone by the binding.
            box.SelectedText = string.Empty;
            box.CaretIndex = caret;

            e.Handled = true;

            // Write the clipboard AFTER returning to the message loop, via the shared
            // ClipboardWriter (Dispatcher.Background + try/catch + up to 3 retries).
            // Clipboard.SetText blocks the UI thread until clipboard-chain listeners
            // (history/sync tools) acknowledge; doing it inline stalls the paint and the
            // deleted text visibly lingers (~0.4s). Background priority = runs after
            // rendering is done.
            ClipboardWriter.WriteText(selected);
        }

        // ===== mod: tab drag-reorder =====

        private const string TabDragFormat = "NotePadClone.TabItem";

        private Point _tabDragStart;
        private TabItem? _draggedTab;

        private void OnTabControlPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _draggedTab = FindVisualAncestor<TabItem>(e.OriginalSource as DependencyObject);
            _tabDragStart = e.GetPosition(null);
        }

        private void OnTabControlMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _draggedTab is null)
                return;

            var diff = _tabDragStart - e.GetPosition(null);
            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            DragDrop.DoDragDrop(_draggedTab, new DataObject(TabDragFormat, _draggedTab), DragDropEffects.Move);
            _draggedTab = null;
        }

        private void OnTabControlDrop(object sender, DragEventArgs e)
        {
            // v1.2.1: drag files from Explorer/Desktop into the window → open directly (path gets revealed in the tree).
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                DropFilesOntoWindow(e);
                return;
            }

            if (_draggedTab is null
                || sender is not TabControl tabs
                || tabs.DataContext is not MainWindowVm vm)
                return;

            var target = FindVisualAncestor<TabItem>(e.OriginalSource as DependencyObject);
            if (target is null || ReferenceEquals(target, _draggedTab))
                return;

            var from = vm.Documents.IndexOf((IDocument)_draggedTab.DataContext);
            var to = vm.Documents.IndexOf((IDocument)target.DataContext);

            if (from >= 0 && to >= 0)
                vm.Documents.Move(from, to);
        }

        private static T? FindVisualAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current is not null && current is not T)
                current = GetParentSafe(current);

            return current as T;
        }

        /// <summary>
        /// v1.2 crash fix: walking up to the parent node must be fault-tolerant.
        /// VisualTreeHelper.GetParent only accepts Visual/Visual3D; content elements in Markdown
        /// browse mode (FlowDocument / Run / Paragraph etc.) are not Visuals — passing them throws
        /// InvalidOperationException ("not a Visual or Visual3D") and kills the whole process.
        /// For non-visual nodes, walk up via LogicalTreeHelper instead; on any exception treat as no parent and return null safely.
        /// </summary>
        private static DependencyObject? GetParentSafe(DependencyObject node)
        {
            if (node is Visual || node is Visual3D)
            {
                try
                {
                    return VisualTreeHelper.GetParent(node);
                }
                catch (InvalidOperationException)
                {
                    // Rare case (node not yet connected to the tree, etc.): fall back to the logical tree; if that fails, treat as no parent.
                    return LogicalTreeHelper.GetParent(node);
                }
            }

            // Content elements (FlowDocument and its Run/Paragraph/Table...) can only find their parent via the logical tree.
            return LogicalTreeHelper.GetParent(node);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_viewModel is null) throw new InvalidOperationException("View model is null.");
            if (_windowService is null) throw new InvalidOperationException("Window service is null.");

            _windowService.SubscribeToWindowEvents(this);
            _viewModel.SwitchThemeAction = () => _windowService.SwitchWindowTheme(this);
            _viewModel.OpenFileSelectionDialogHandler = () => OpenFileDialog(new OpenFileDialog());
            _viewModel.OpenSaveFileDialogHandler = () => OpenFileDialog(new SaveFileDialog());

            // v1.2.1: "Open Folder..." uses the .NET 8 native folder picker.
            _viewModel.OpenFolderDialogHandler = () =>
            {
                var dialog = new OpenFolderDialog { Title = "選擇文件列表根資料夾" };
                return dialog.ShowDialog() == true && Directory.Exists(dialog.FolderName)
                    ? dialog.FolderName
                    : null;
            };

            // v1.2 mod: when toggling the sidebar, set the sidebar column width to 0 or restore it, fully reverting the editor to the v1.1 layout.
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplySidebarColumnWidth();
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowVm.ShowSidePanel))
                ApplySidebarColumnWidth();
        }

        private void ApplySidebarColumnWidth()
        {
            if (_viewModel is null)
                return;
            SidebarColumn.Width = new GridLength(_viewModel.ShowSidePanel ? Math.Max(40, _viewModel.SidebarWidth) : 0);
        }

        private void OnExplorerSplitterDragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (_viewModel is null)
                return;
            var width = SidebarColumn.ActualWidth;
            if (width >= 40)
                _viewModel.SidebarWidth = width;
        }

        private void OnExplorerTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is not FileExplorerItem item)
                return;

            // v1.2.1 StackOverflow fix: while a programmatic reveal is inside SelectItem (IsProgrammaticSelect=true),
            // return directly here, breaking the self-locking loop "Select→IsSelected binding sync→handleSelected→event→Select again"
            // (the dump shows the whole thread stacking TreeViewItem.Select/ChangeSelection frame over frame until the stack blows with
            // 0xC0000409). Only a real user click on the tree opens a file; programmatic selection must not re-enter.
            if (_viewModel is { IsProgrammaticSelect: true })
                return;
            if (item.IsTextFile)
                _viewModel?.OpenFileFromExplorer(item.FullPath);

            // v1.2.1 reveal (auto-locate): after external open/drop, when the tree selects the target, scroll it into view and focus it.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (e.NewValue is not FileExplorerItem sel)
                    return;
                var tvi = FindTreeViewItemContainer(ExplorerTree, sel);
                tvi?.BringIntoView();
                tvi?.Focus();
            }));
        }

        // ===== v1.2.1 file-list context menu =====

        private void OnExplorerRefreshClick(object sender, RoutedEventArgs e)
        {
            _viewModel?.RefreshExplorerNode(GetContextItem(sender));
        }

        private void OnExplorerOpenInExplorerClick(object sender, RoutedEventArgs e)
        {
            var item = GetContextItem(sender);
            if (item is null)
                return;
            try
            {
                // File node: open Explorer with the file selected; folder node: open the folder directly.
                var args = item.IsDirectory
                    ? $"\"{item.FullPath}\""
                    : $"/select,\"{item.FullPath}\"";
                System.Diagnostics.Process.Start("explorer.exe", args);
            }
            catch (Exception)
            {
                // Explorer launch failure: ignore (nothing in the app depends on it).
            }
        }

        private void OnExplorerSetRootClick(object sender, RoutedEventArgs e)
        {
            var item = GetContextItem(sender);
            if (item is not { IsDirectory: true })
                return;
            // Manual root lock: auto-follow is disabled immediately — control goes back to the user.
            _viewModel?.LockExplorerRoot(item.FullPath);
        }

        private void OnExplorerDeleteClick(object sender, RoutedEventArgs e)
        {
            var item = GetContextItem(sender);
            if (item is null || item.IsDirectory || !File.Exists(item.FullPath))
                return;

            var name = item.Name;
            var result = MessageBox.Show(
                $"確定要刪除「{name}」嗎？\n\n此檔案會移到資源回收筒。",
                "記事本",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                // Delete to Recycle Bin, not permanently. (UIOption lives in the Microsoft.VisualBasic.FileIO namespace.)
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    item.FullPath,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                _viewModel?.RefreshExplorerNode(item);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"刪除失敗：\n{ex.Message}", "記事本", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static FileExplorerItem? GetContextItem(object sender)
        {
            // The sender of MenuItem.Click is the MenuItem itself; its DataContext is inherited from the ContextMenu (= the tree node).
            return (sender as MenuItem)?.DataContext as FileExplorerItem;
        }

        // ===== v1.2.1 drag-and-drop open =====

        private void DropFilesOntoWindow(DragEventArgs e)
        {
            if (_viewModel is null)
                return;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
                return;
            foreach (var file in files)
            {
                if (File.Exists(file))
                    _viewModel.OpenFileFromExplorer(file);
            }
        }

        // ===== v1.2.1 tree container lookup (used for reveal scrolling) =====

        private static TreeViewItem? FindTreeViewItemContainer(ItemsControl parent, object item)
        {
            var container = parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
            if (container is not null)
                return container;
            foreach (var child in parent.Items)
            {
                var childContainer = parent.ItemContainerGenerator.ContainerFromItem(child) as TreeViewItem;
                if (childContainer is null)
                    continue;
                var found = FindTreeViewItemContainer(childContainer, item);
                if (found is not null)
                    return found;
            }
            return null;
        }

        private static string? OpenFileDialog(FileDialog dialog)
        {
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}