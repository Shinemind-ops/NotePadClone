using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace NotePadClone.Controls;

/// <summary>
/// v1.3 mod addition: line-number gutter.
/// Placed left of the native TextBox inside the same DataTemplate (its own Grid column); OnRender
/// draws only the currently visible line numbers (virtualized — no lag scrolling files of >5000 lines).
///
/// Positioning strategy: don't guess "line N = N pixels" — use TextBox.GetRectFromCharacterIndex
/// to get each hard line's first character exact Y coordinate (accurate even with soft wrap; measured: that rect is itself a
/// "viewport coordinate" — it already shifts with scrolling, so no manual VerticalOffset subtraction needed). The first visible line is found by binary search (line numbers are strictly
/// monotonically increasing), then only lines inside the viewport are drawn. After content/wrap changes, LayoutUpdated triggers a re-render to pick up
/// the real post-layout coordinates.
///
/// Horizontal scroll is inherently unaffected: the gutter is a separate Grid column, not inside the TextBox's ScrollViewer;
/// the TextBox scrolls its own content horizontally while the gutter stays put.
/// </summary>
public sealed class LineNumberMargin : FrameworkElement
{
    /// <summary>The companion TextBox (bound in via ElementName inside the DataTemplate).</summary>
    public static readonly DependencyProperty EditorBoxProperty = DependencyProperty.Register(
        nameof(EditorBox),
        typeof(TextBox),
        typeof(LineNumberMargin),
        new PropertyMetadata(null, OnEditorBoxChanged));

    /// <summary>Line-number toggle (global setting, bound to the main VM).</summary>
    public static readonly DependencyProperty ShowLineNumbersProperty = DependencyProperty.Register(
        nameof(ShowLineNumbers),
        typeof(bool),
        typeof(LineNumberMargin),
        new PropertyMetadata(false, (d, _) => ((LineNumberMargin)d).InvalidateVisual()));

    private static readonly Brush NumberBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x9A, 0xA8));
    private static readonly Brush SeparatorBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00));

    private const double DigitsRightPadding = 8;
    private const double LeftPadding = 2;
    private const double SeparatorWidth = 1;

    private ScrollViewer? _scrollViewer;
    private int[] _lineStarts = { 0 };   // Character index of each hard line's first character
    private double _verticalOffset;      // Pixel scroll amount of the inner ScrollViewer
    private double _digitsWidth = 16;    // Width of the line-number text column (recomputed from the current max digit count)
    private double _lineHeight = 18;     // Estimated line height (safety bound for stopping the draw loop)

    public TextBox? EditorBox
    {
        get => (TextBox?)GetValue(EditorBoxProperty);
        set => SetValue(EditorBoxProperty, value);
    }

    public bool ShowLineNumbers
    {
        get => (bool)GetValue(ShowLineNumbersProperty);
        set => SetValue(ShowLineNumbersProperty, value);
    }

    public LineNumberMargin()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    private static void OnEditorBoxChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var margin = (LineNumberMargin)d;
        if (e.OldValue is TextBox oldBox)
            margin.Detach(oldBox);
        if (e.NewValue is TextBox newBox)
            margin.Attach(newBox);
    }

    private void Attach(TextBox box)
    {
        box.TextChanged += OnTextChanged;
        box.SizeChanged += OnTextBoxSizeChanged;
        box.IsVisibleChanged += OnTextBoxIsVisibleChanged;
        box.LayoutUpdated += OnTextBoxLayoutUpdated;
        Loaded += OnLoaded;
        RebuildLineStarts(box);
    }

    private void Detach(TextBox box)
    {
        box.TextChanged -= OnTextChanged;
        box.SizeChanged -= OnTextBoxSizeChanged;
        box.IsVisibleChanged -= OnTextBoxIsVisibleChanged;
        box.LayoutUpdated -= OnTextBoxLayoutUpdated;
        Loaded -= OnLoaded;
        if (_scrollViewer is not null)
            _scrollViewer.ScrollChanged -= OnScrollChanged;
        _scrollViewer = null;
    }

    /// <summary>
    /// After content changes / re-wrap, the TextBox's text layout only has new coordinates once layout completes;
    /// re-rendering synchronously in TextChanged only gets stale/unlaid-out (Infinity) coordinates. LayoutUpdated fires
    /// after every completed layout pass; re-render there so line numbers match the real post-wrap positions.
    /// The _needLayoutRender flag: re-render only once after content/size/visibility changes, not on every layout.
    /// </summary>
    private bool _needLayoutRender = true;

    private void OnTextBoxLayoutUpdated(object? sender, EventArgs e)
    {
        if (!_needLayoutRender || _lineStarts.Length == 0)
            return;
        _needLayoutRender = false;
        InvalidateVisual();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (EditorBox is not { } box)
            return;
        _scrollViewer = FindScrollViewer(box);
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer.ScrollChanged += OnScrollChanged;
            _verticalOffset = _scrollViewer.VerticalOffset;
        }
        // After a tab switch / mode change the TextBox re-enters the tree; force one re-render to align with the new viewport.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(InvalidateVisual));
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (EditorBox is not { } box)
            return;
        RebuildLineStarts(box);
        InvalidateMeasure(); // More digits → the line-number gutter must widen
        InvalidateVisual();
        _needLayoutRender = true; // Content changed → re-render after layout completes (to get the real wrapped coordinates)
    }

    private void OnTextBoxSizeChanged(object sender, SizeChangedEventArgs e)
    {
        InvalidateMeasure();
        InvalidateVisual();
        _needLayoutRender = true;
    }

    private void OnTextBoxIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        InvalidateVisual();
        _needLayoutRender = true;
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(_verticalOffset - e.VerticalOffset) < 0.25)
            return;
        _verticalOffset = e.VerticalOffset;
        InvalidateVisual();
    }

    private void RebuildLineStarts(TextBox box)
    {
        var text = box.Text ?? string.Empty;
        // O(N) single pass to collect each line's start; even N=50000 is microseconds — fine to run per keystroke.
        var starts = new System.Collections.Generic.List<int>(text.Length / 16 + 2) { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                starts.Add(i + 1);
        }
        _lineStarts = starts.ToArray();
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        // The TextBox's internal PART_ContentHost is the ScrollViewer.
        if (root is ScrollViewer viewer)
            return viewer;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
                return found;
        }
        return null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Re-measure the line-number text width on every measure: line count grows (more digits) → the gutter auto-widens.
        // Can't measure only in OnRender — measure happens before render; without re-measuring we'd always be one frame late widening.
        if (EditorBox is { } box)
            UpdateMetrics(box);
        // Width = left padding + line-number text width + right padding + divider; height is stretched by the Grid.
        var width = LeftPadding + _digitsWidth + DigitsRightPadding + SeparatorWidth;
        return new Size(width, availableSize.Height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        // Don't draw when text is empty (GetRectFromCharacterIndex throws out-of-range on empty text — just avoid it entirely)
        if (EditorBox is not { } box || !ShowLineNumbers || _lineStarts.Length == 0)
            return;
        if (box.Text.Length == 0)
            return;

        // Divider line (right edge)
        dc.DrawRectangle(
            SeparatorBrush,
            null,
            new Rect(ActualWidth - SeparatorWidth, 0, SeparatorWidth, ActualHeight));

        // LineTop() returns "viewport coordinates" (content coordinate − scroll offset), so the visible range must also use viewport coordinates.
        var visibleTop = 0d;
        var visibleBottom = Math.Max(0, _scrollViewer?.ViewportHeight ?? 0);

        // Binary search for the first line whose top is still at or above the viewport top edge (or just past it)
        var lo = 0;
        var hi = _lineStarts.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (LineTop(box, mid) < visibleTop - _lineHeight)
                lo = mid + 1;
            else
                hi = mid;
        }

        // Draw only lines inside the viewport (virtualized); line tops are monotonically increasing — stop once past the bottom edge.
        var maxIterations = Math.Min(_lineStarts.Length, (int)(ActualHeight / _lineHeight) + 8);
        for (var i = lo; i < _lineStarts.Length && (i - lo) <= maxIterations; i++)
        {
            var top = LineTop(box, i);
            if (top > visibleBottom)
                break;

            var text = new FormattedText(
                (i + 1).ToString(CultureInfo.InvariantCulture),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(box.FontFamily, box.FontStyle, box.FontWeight, box.FontStretch),
                box.FontSize,
                NumberBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(ActualWidth - SeparatorWidth - DigitsRightPadding - text.Width, top));
        }
    }

    /// <summary>
    /// Y of a line-start character in viewport coordinates. Measured: the rect returned by GetRectFromCharacterIndex is already
    /// "viewport-relative" (auto-shifts with scrolling: content coordinate − VerticalOffset), so do NOT subtract
    /// _verticalOffset again — otherwise the numbers go wrong while scrolling and deep scrolling draws everything above the viewport.
    /// Returns +infinity on failure (skip drawing that line).
    /// </summary>
    private double LineTop(TextBox box, int lineIndex)
    {
        var start = _lineStarts[lineIndex];
        if (start > box.Text.Length)
            return double.PositiveInfinity;
        try
        {
            var rect = box.GetRectFromCharacterIndex(start);
            return rect.Top;
        }
        catch (Exception)
        {
            // Extreme cases (e.g. caret boundary at end of text): if no coordinate can be obtained, skip that line — must never crash the UI.
            return double.PositiveInfinity;
        }
    }

    private void UpdateMetrics(TextBox box)
    {
        var digits = _lineStarts.Length.ToString(CultureInfo.InvariantCulture).Length;
        var probe = new FormattedText(
            new string('9', Math.Max(digits, 1)),
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(box.FontFamily, box.FontStyle, box.FontWeight, box.FontStretch),
            box.FontSize,
            NumberBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        _digitsWidth = probe.Width;
        _lineHeight = Math.Max(probe.Height, 10);
    }
}