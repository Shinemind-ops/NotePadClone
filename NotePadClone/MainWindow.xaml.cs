using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

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

            // Write the clipboard AFTER returning to the message loop. Clipboard.SetText
            // blocks the UI thread until clipboard-chain listeners (history/sync tools)
            // acknowledge; doing it inline stalls the paint and the deleted text visibly
            // lingers (~0.4s). Background priority = runs after rendering is done.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    Clipboard.SetText(selected);
                }
                catch
                {
                    // Clipboard can be transiently locked by another process; deletion already happened.
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
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
                current = VisualTreeHelper.GetParent(current);

            return current as T;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_viewModel is null) throw new InvalidOperationException("View model is null.");
            if (_windowService is null) throw new InvalidOperationException("Window service is null.");

            _windowService.SubscribeToWindowEvents(this);
            _viewModel.SwitchThemeAction = () => _windowService.SwitchWindowTheme(this);
            _viewModel.OpenFileSelectionDialogHandler = () => OpenFileDialog(new OpenFileDialog());
            _viewModel.OpenSaveFileDialogHandler = () => OpenFileDialog(new SaveFileDialog());
        }

        private static string? OpenFileDialog(FileDialog dialog)
        {
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}