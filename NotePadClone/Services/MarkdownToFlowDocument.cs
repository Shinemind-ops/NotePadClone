using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace NotePadClone.Services;

/// <summary>
/// Modded addition: minimal Markdown → FlowDocument renderer (v1.2 browsing mode).
/// Coverage per requirements: headings, bold/italic, inline code, fenced + indented code blocks, quotes, ordered/unordered lists,
/// links (shown as styled text + ToolTip, not clickable), GFM pipe tables, horizontal rules.
/// Pure managed code, no external runtime dependency (no Markdig/NuGet — avoids restore risk on mainland-China networks).
/// Not supported (acceptable): nested lists, nested quotes, setext headings, HTML tags, images.
/// Tables (since v1.2.1): built as a WPF Grid hosted in a BlockUIContainer — WPF FlowDocument.Table
/// triggers a native crash (FailFast) in the non-managed PtsHost (TableParaClient) during reflow/window resize,
/// bypassing every managed safety net; so tables use Controls only (Grid/Border/TextBlock), never any in-table FlowDocument element.
/// </summary>
public static class MarkdownToFlowDocument
{
    private static readonly FontFamily CodeFont = new("Consolas");

    // Neutral colors, readable in both dark and light themes.
    private static readonly Brush CodeBackground = new SolidColorBrush(Color.FromArgb(0x24, 0x00, 0x00, 0x00));
    private static readonly Brush SubtleBorder = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00));
    private static readonly Brush QuoteAccent = new SolidColorBrush(Color.FromArgb(0x60, 0x95, 0x75, 0xCD)); // Theme purple
    private static readonly Brush LinkColor = new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5));

    /// <summary>Render Markdown text into a new FlowDocument (a fresh instance on every call).</summary>
    public static FlowDocument Render(string markdown)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(20, 12, 20, 12),
            FontSize = 14,
        };

        var lines = SplitLines(markdown);
        var i = 0;
        while (i < lines.Count)
        {
            var raw = lines[i];
            var trimmed = raw.Trim();

            if (trimmed.Length == 0) { i++; continue; }

            // Fenced code blocks ```lang / ~~~lang
            if (TryMatchFence(trimmed, out var fenceChar))
            {
                var end = ConsumeFencedCode(lines, i, fenceChar, out var codeText);
                doc.Blocks.Add(CodeParagraph(codeText));
                i = end + 1;
                continue;
            }

            // Headings #..######
            if (TryMatchHeading(trimmed, out var level, out var headingText))
            {
                doc.Blocks.Add(HeadingParagraph(level, headingText));
                i++;
                continue;
            }

            // Horizontal rules
            if (IsHorizontalRule(trimmed))
            {
                doc.Blocks.Add(HorizontalRuleParagraph());
                i++;
                continue;
            }

            // GFM pipe table: current line contains '|' and the next line is the delimiter row
            if (i + 1 < lines.Count && IsTableHeaderSep(lines[i + 1]))
            {
                var end = ConsumeTable(lines, i, out var table);
                if (end >= 0)
                {
                    doc.Blocks.Add(table);
                    i = end + 1;
                    continue;
                }
            }

            // Block quotes
            if (trimmed.StartsWith(">", StringComparison.Ordinal))
            {
                var end = ConsumeBlockquote(lines, i, out var quoteText);
                doc.Blocks.Add(QuoteParagraph(quoteText));
                i = end + 1;
                continue;
            }

            // Unordered lists
            if (TryMatchBullet(trimmed))
            {
                var end = ConsumeList(lines, i, ordered: false, out var items);
                doc.Blocks.Add(ListParagraph(items, ordered: false));
                i = end + 1;
                continue;
            }

            // Ordered lists
            if (TryMatchNumbered(trimmed))
            {
                var end = ConsumeList(lines, i, ordered: true, out var items);
                doc.Blocks.Add(ListParagraph(items, ordered: true));
                i = end + 1;
                continue;
            }

            // Indented code blocks (4 spaces / tab)
            if (IsIndentedCode(raw))
            {
                var end = ConsumeIndentedCode(lines, i, out var codeText);
                doc.Blocks.Add(CodeParagraph(codeText));
                i = end + 1;
                continue;
            }

            // Plain paragraphs
            var pEnd = ConsumeParagraph(lines, i);
            doc.Blocks.Add(ParagraphFromLines(lines.GetRange(i, pEnd - i + 1)));
            i = pEnd + 1;
        }

        return doc;
    }

    // ===== Line helpers =====

    private static List<string> SplitLines(string markdown)
    {
        var normalized = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        var list = normalized.Split('\n').ToList();
        // Strip trailing blank lines at the very end of the document
        while (list.Count > 0 && list[^1].Trim().Length == 0)
            list.RemoveAt(list.Count - 1);
        return list;
    }

    private static bool TryMatchFence(string trimmed, out char fenceChar)
    {
        fenceChar = '\0';
        if (trimmed.Length < 3)
            return false;
        if (trimmed[0] != '`' && trimmed[0] != '~')
            return false;
        var ch = trimmed[0];
        var count = 0;
        while (count < trimmed.Length && trimmed[count] == ch)
            count++;
        if (count < 3)
            return false;
        fenceChar = ch;
        return true;
    }

    private static bool TryMatchHeading(string trimmed, out int level, out string text)
    {
        level = 0;
        text = string.Empty;
        var i = 0;
        while (i < trimmed.Length && trimmed[i] == '#')
            i++;
        if (i == 0 || i > 6)
            return false;
        level = i;
        if (i == trimmed.Length)
        {
            text = string.Empty;
            return true; // "#": empty heading
        }
        if (trimmed[i] != ' ')
            return false;
        text = trimmed[(i + 1)..].Trim();
        return true;
    }

    private static bool IsHorizontalRule(string trimmed)
    {
        var ch = trimmed[0];
        if (ch != '-' && ch != '*' && ch != '_')
            return false;
        foreach (var c in trimmed)
        {
            if (c != ch)
                return false;
        }
        return trimmed.Length >= 3;
    }

    private static bool IsTableHeaderSep(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.Contains('|') && !trimmed.StartsWith(new string('-', 3)))
            return false;

        var cells = SplitCells(trimmed);
        if (cells.Count == 0)
            return false;

        foreach (var cell in cells)
        {
            var s = cell.Trim();
            if (s.Length == 0)
                return false;
            foreach (var c in s)
            {
                if (c != '-' && c != ':')
                    return false;
            }
        }
        return true;
    }

    private static List<string> SplitCells(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|'))
            trimmed = trimmed[1..];
        if (trimmed.EndsWith('|'))
            trimmed = trimmed[..^1];
        return trimmed.Split('|').Select(c => c.Trim()).ToList();
    }

    private static bool TryMatchBullet(string trimmed)
    {
        if (trimmed.Length < 2)
            return false;
        var ch = trimmed[0];
        if (ch != '-' && ch != '*' && ch != '+')
            return false;
        return trimmed[1] == ' ';
    }

    private static bool TryMatchNumbered(string trimmed)
    {
        if (trimmed.Length < 2)
            return false;
        var i = 0;
        while (i < trimmed.Length && char.IsDigit(trimmed[i]))
            i++;
        if (i == 0)
            return false;
        if (i >= trimmed.Length)
            return false;
        if (trimmed[i] != '.' && trimmed[i] != ')')
            return false;
        return i + 1 < trimmed.Length && trimmed[i + 1] == ' ';
    }

    private static bool IsIndentedCode(string raw)
        => raw.Length > 0 && (raw[0] == '\t' || raw.StartsWith("    ", StringComparison.Ordinal));

    // ===== Block consumers (return the index of the block's last line, inclusive) =====

    private static int ConsumeFencedCode(List<string> lines, int start, char fenceChar, out string text)
    {
        var sb = new StringBuilder();
        var i = start + 1;
        for (; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.Length >= 3 && t.All(c => c == fenceChar))
                break;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(lines[i]);
        }
        text = sb.ToString();
        return Math.Min(i, lines.Count - 1);
    }

    private static int ConsumeIndentedCode(List<string> lines, int start, out string text)
    {
        var sb = new StringBuilder();
        var i = start;
        while (i < lines.Count && IsIndentedCode(lines[i]))
        {
            var line = lines[i];
            var content = line.StartsWith("\t", StringComparison.Ordinal)
                ? line[1..]
                : line.Length >= 4 ? line[4..] : line;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(content);
            i++;
        }
        text = sb.ToString();
        return i - 1;
    }

    private static int ConsumeTable(List<string> lines, int start, out Block table)
    {
        table = null!;
        var header = SplitCells(lines[start]);
        if (header.Count == 0)
            return -1;

        // GFM alignment marks: each delimiter cell's `:` position decides the column alignment (--- left, ---: right, :---: center)
        var sepCells = SplitCells(lines[start + 1]);
        var alignments = ParseAlignments(sepCells, header.Count);

        var body = new List<List<string>>();
        var i = start + 2; // Skip the delimiter row
        for (; i < lines.Count; i++)
        {
            if (lines[i].Trim().Length == 0 || !lines[i].Contains('|'))
                break;
            body.Add(SplitCells(lines[i]));
        }

        var columnCount = header.Count;
        foreach (var row in body)
            columnCount = Math.Max(columnCount, row.Count);

        table = BuildTableBlock(header, body, columnCount, alignments);
        return i - 1;
    }

    private static int ConsumeBlockquote(List<string> lines, int start, out string text)
    {
        var sb = new StringBuilder();
        var i = start;
        while (i < lines.Count)
        {
            var t = lines[i].Trim();
            if (!t.StartsWith(">", StringComparison.Ordinal))
                break;
            var content = t.Length > 1 ? t[1..].TrimStart() : string.Empty;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(content);
            i++;
        }
        text = sb.ToString();
        return i - 1;
    }

    private static int ConsumeList(List<string> lines, int start, bool ordered, out List<string> items)
    {
        items = new List<string>();
        var i = start;
        while (i < lines.Count)
        {
            var trimmed = lines[i].Trim();
            string? content = null;
            if (!ordered && TryMatchBullet(trimmed))
            {
                content = trimmed[2..].Trim();
            }
            else if (ordered && TryMatchNumbered(trimmed))
            {
                var digitEnd = 0;
                while (digitEnd < trimmed.Length && char.IsDigit(trimmed[digitEnd]))
                    digitEnd++;
                var markerLen = digitEnd + 2; // digit + '.'/')' + space
                content = trimmed[markerLen..].Trim();
            }
            else
            {
                break;
            }
            items.Add(content);
            i++;
        }
        return i - 1;
    }

    /// <summary>Collect a plain paragraph: until a blank line or a line that would start a new block.</summary>
    private static int ConsumeParagraph(List<string> lines, int start)
    {
        var i = start;
        while (i < lines.Count)
        {
            var raw = lines[i];
            var trimmed = raw.Trim();
            if (trimmed.Length == 0)
                break;
            if (i > start && StartsNewBlock(lines, i))
                break;
            i++;
        }
        return i - 1;
    }

    private static bool StartsNewBlock(List<string> lines, int index)
    {
        var raw = lines[index];
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return true;
        if (TryMatchFence(trimmed, out _)) return true;
        if (TryMatchHeading(trimmed, out _, out _)) return true;
        if (IsHorizontalRule(trimmed)) return true;
        if (trimmed.StartsWith(">", StringComparison.Ordinal)) return true;
        if (TryMatchBullet(trimmed)) return true;
        if (TryMatchNumbered(trimmed)) return true;
        if (IsTableHeaderSep(raw) && index > 0) return true; // Table delimiter row
        // Indented continuation lines stay in the paragraph (Markdown paragraph joining), not a new block
        return false;
    }

    // ===== Block builders =====

    private static Paragraph CodeParagraph(string text)
    {
        return new Paragraph(new Run(string.IsNullOrEmpty(text) ? " " : text))
        {
            FontFamily = CodeFont,
            FontSize = 13,
            Background = CodeBackground,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 4, 0, 4),
        };
    }

    private static Paragraph HeadingParagraph(int level, string text)
    {
        var fontSize = level switch { 1 => 24, 2 => 20, 3 => 17, 4 => 15, _ => 14 };
        var p = new Paragraph
        {
            FontSize = fontSize,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, level <= 2 ? 10 : 8, 0, level <= 2 ? 4 : 2),
        };

        if (level <= 2)
        {
            p.BorderBrush = SubtleBorder;
            p.BorderThickness = new Thickness(0, 0, 0, 1);
            p.Padding = new Thickness(0, 0, 0, 3);
        }

        AddInlines(p.Inlines, text);
        return p;
    }

    private static Paragraph HorizontalRuleParagraph() => new()
    {
        BorderBrush = SubtleBorder,
        BorderThickness = new Thickness(0, 0, 0, 1),
        Margin = new Thickness(0, 6, 0, 6),
        Padding = new Thickness(0),
    };

    private static Paragraph QuoteParagraph(string text)
    {
        var p = new Paragraph
        {
            Background = CodeBackground,
            BorderBrush = QuoteAccent,
            BorderThickness = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 4, 0, 4),
        };
        AddInlines(p.Inlines, text);
        return p;
    }

    private static Paragraph ListParagraph(List<string> items, bool ordered)
    {
        var p = new Paragraph
        {
            Margin = new Thickness(0, 2, 0, 2),
        };
        for (var n = 0; n < items.Count; n++)
        {
            if (n > 0)
                p.Inlines.Add(new LineBreak());
            var marker = ordered ? $"{n + 1}. " : "•  ";
            p.Inlines.Add(new Run(marker));
            p.Inlines.Add(new Run(" "));
            AddInlines(p.Inlines, items[n]);
        }
        return p;
    }

    private static Paragraph ParagraphFromLines(List<string> lines)
    {
        var p = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
        for (var n = 0; n < lines.Count; n++)
        {
            if (n > 0)
                p.Inlines.Add(new LineBreak());
            AddInlines(p.Inlines, lines[n]);
        }
        return p;
    }

    /// <summary>
    /// Parse a GFM delimiter row into per-column text alignment (GFM marks: - left, -: right, :-: center).
    /// </summary>
    private static List<TextAlignment> ParseAlignments(List<string> sepCells, int columnCount)
    {
        var result = new List<TextAlignment>(columnCount);
        for (var c = 0; c < columnCount; c++)
        {
            var s = c < sepCells.Count ? sepCells[c] : string.Empty;
            if (s.EndsWith(":", StringComparison.Ordinal))
                result.Add(s.StartsWith(":", StringComparison.Ordinal) ? TextAlignment.Center : TextAlignment.Right);
            else
                result.Add(TextAlignment.Left);
        }
        return result;
    }

    /// <summary>
    /// v1.2.1: build the visual table with a WPF Grid hosted in a BlockUIContainer (no more FlowDocument.Table —
    /// the latter triggers a non-managed PtsHost native crash (FailFast) during reflow/resize). Every element in the table is limited to
    /// Controls (Border/Grid/TextBlock); no FlowDocument element may appear inside the BlockUIContainer.
    /// Alignment (GFM `---:` right-aligns numeric columns), bold headers and grid lines are all done with Grid rows/columns + Borders.
    /// v1.4: per-table horizontal scrolling. Oversized tables showed only their left half, and a document-wide
    /// horizontal scrollbar was rejected by the owner — so each table wraps its own ScrollViewer (horizontal Auto, vertical Disabled):
    /// a bar appears only when the table is wider than the visible page; narrow tables get none. The key is that the ScrollViewer must have an explicit width —
    /// when FlowDocument measures a BlockUIContainer it hands it a near-infinite width, so "auto width" makes the ScrollViewer
    /// believe the content fits: the bar never appears and the right half stays clipped. So bind ScrollViewer.Width to
    /// the outermost FlowDocumentScrollViewer's ActualWidth; the bar appears only when the content Grid wants more width than the container.
    /// </summary>
    private static Block BuildTableBlock(List<string> header, List<List<string>> body, int columnCount, List<TextAlignment> alignments)
    {
        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        // Star column widths are no longer usable: BlockUIContainer measures children with an infinite-width constraint,
        // so Star columns resolve to infinite width and Wrap-mode TextBlocks spiral into an unbounded measure loop → StackOverflow (0xC0000409).
        // Auto column widths + NoWrap: width is purely content-driven, so no unbounded measuring exists.
        for (var c = 0; c < columnCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var r = 0; r < body.Count; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddTableRow(grid, 0, header, alignments, isHeader: true);
        for (var r = 0; r < body.Count; r++)
            AddTableRow(grid, r + 1, body[r], alignments, isHeader: false);

        // Each cell Border carries "right + bottom" lines; the outer Border adds "top + left", composing a complete grid (no double lines).
        var host = new Border
        {
            BorderBrush = SubtleBorder,
            BorderThickness = new Thickness(1, 1, 0, 0),
            Child = grid,
        };

        var scrollViewer = new ScrollViewer
        {
            Content = host,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        // Width constraint: bind to the host FlowDocumentScrollViewer's viewport width (see the method-body notes), so that
        // the ScrollViewer shows a horizontal bar only when "content Grid desired width > container width".
        // No approach other than a FindAncestor binding, because the document is not yet in the tree at Render() time; a binding only takes effect once attached to the visual tree.
        scrollViewer.SetBinding(FrameworkElement.WidthProperty,
            new Binding(nameof(FlowDocumentScrollViewer.ActualWidth))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(FlowDocumentScrollViewer), 1),
            });
        // Vertical scrolling stays with the outer document: the table's own ScrollViewer has vertical Disabled, but when wider than the
        // viewport the ScrollViewer swallows the mouse wheel for horizontal scrolling; intercept it in the Preview phase and drive
        // FlowDocumentScrollViewer to scroll vertically, so the wheel never "sticks" over the table.
        scrollViewer.PreviewMouseWheel += OnTableScrollViewerPreviewMouseWheel;

        return new BlockUIContainer(scrollViewer)
        {
            Margin = new Thickness(0, 6, 0, 6),
            Padding = new Thickness(0),
        };
    }

    /// <summary>
    /// Stop the table ScrollViewer from swallowing the mouse wheel: vertical wheeling returns to the outer document (vertical scroll).
    /// The table ScrollViewer has vertical Disabled, but when it is wider than the viewport its built-in OnMouseWheel
    /// still grabs the wheel for horizontal scrolling; intercept in the Preview phase and directly drive the
    /// inner ScrollViewer in the FlowDocumentScrollViewer template to scroll vertically (FDSV itself exposes no public scrolling method).
    /// </summary>
    private static void OnTableScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled)
            return;

        var viewer = FindAncestor<FlowDocumentScrollViewer>(sender as DependencyObject);
        if (viewer is null)
            return; // Document not in the tree (e.g. unit tests): do nothing.

        e.Handled = true;

        var host = FindFirstScrollViewer(viewer);
        if (host is null)
            return; // Theoretically impossible: the FDSV template always has a ScrollViewer content host; if not found, give up.

        var steps = Math.Max(1, Math.Abs(e.Delta) / 120) * 3; // 3 lines per wheel notch — close to the system default feel
        for (var n = 0; n < steps; n++)
        {
            if (e.Delta > 0)
                host.LineUp();
            else
                host.LineDown();
        }
    }

    /// <summary>
    /// Walk up the visual tree safely (content elements Run/Paragraph are not Visuals — VisualTreeHelper would throw
    /// InvalidOperationException; once at a content element, switch to LogicalTreeHelper — the same strategy as MainWindow
    /// GetParentSafe).
    /// </summary>
    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T target)
                return target;
            current = current is Visual or Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    /// <summary>
    /// Depth-first search for the first ScrollViewer in the visual tree — starting from FlowDocumentScrollViewer,
    /// the first ScrollViewer hit is guaranteed to be its template content host (the document's ScrollViewer),
    /// never the table's own (which is buried deep inside the document content).
    /// </summary>
    private static ScrollViewer? FindFirstScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv)
                return sv;
            var found = FindFirstScrollViewer(child);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static void AddTableRow(Grid grid, int rowIndex, List<string> cells, List<TextAlignment> alignments, bool isHeader)
    {
        for (var c = 0; c < grid.ColumnDefinitions.Count; c++)
        {
            var cellText = c < cells.Count ? cells[c] : string.Empty;
            var cell = new Border
            {
                BorderBrush = SubtleBorder,
                BorderThickness = new Thickness(0, 0, 1, 1),
                Background = isHeader ? CodeBackground : null,
                Padding = new Thickness(8, 4, 8, 4),
                Child = new TextBlock
                {
                    Text = cellText,
                    FontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal,
                    TextAlignment = alignments[c],
                    TextWrapping = TextWrapping.NoWrap,
                },
            };
            Grid.SetRow(cell, rowIndex);
            Grid.SetColumn(cell, c);
            grid.Children.Add(cell);
        }
    }

    // ===== Inline parsing =====

    /// <summary>Parse inline formatting (`` ` `` code, `**`, `__`, `*`, `_`, `[..](..)` links, `\` escapes) and append to the InlineCollection.</summary>
    private static void AddInlines(InlineCollection target, string s)
    {
        if (string.IsNullOrEmpty(s))
            return;

        var sb = new StringBuilder();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];

            if (c == '\\' && i + 1 < s.Length)
            {
                sb.Append(s[i + 1]);
                i += 2;
                continue;
            }

            if (c == '`')
            {
                var close = s.IndexOf('`', i + 1);
                if (close > i + 1)
                {
                    FlushText(target, sb);
                    target.Add(new Run(s.Substring(i + 1, close - i - 1))
                    {
                        FontFamily = CodeFont,
                        FontSize = 13,
                        Background = CodeBackground,
                    });
                    i = close + 1;
                    continue;
                }
                sb.Append(c);
                i++;
                continue;
            }

            if (c == '[')
            {
                if (TryParseLink(s, i, out var end, out var linkText, out var url))
                {
                    FlushText(target, sb);
                    target.Add(new Run(linkText)
                    {
                        TextDecorations = TextDecorations.Underline,
                        Foreground = LinkColor,
                        ToolTip = url,
                    });
                    i = end + 1;
                    continue;
                }
                sb.Append(c);
                i++;
                continue;
            }

            if (c == '*' || c == '_')
            {
                // Three consecutive characters: bold + italic
                if (i + 2 < s.Length && s[i + 1] == c && s[i + 2] == c)
                {
                    var close = FindClose(s, i + 3, c, 3);
                    if (close > i + 3)
                    {
                        FlushText(target, sb);
                        var span = new Span { FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic };
                        AddInlines(span.Inlines, s.Substring(i + 3, close - i - 3));
                        target.Add(span);
                        i = close + 3;
                        continue;
                    }
                    sb.Append(c);
                    i++;
                    continue;
                }

                // Two consecutive characters: bold
                if (i + 1 < s.Length && s[i + 1] == c)
                {
                    var close = FindClose(s, i + 2, c, 2);
                    if (close > i + 2)
                    {
                        FlushText(target, sb);
                        var span = new Span { FontWeight = FontWeights.Bold };
                        AddInlines(span.Inlines, s.Substring(i + 2, close - i - 2));
                        target.Add(span);
                        i = close + 2;
                        continue;
                    }
                    sb.Append(c);
                    i++;
                    continue;
                }

                // Single character: italic
                var closeSingle = FindClose(s, i + 1, c, 1);
                if (closeSingle > i + 1)
                {
                    FlushText(target, sb);
                    var span = new Span { FontStyle = FontStyles.Italic };
                    AddInlines(span.Inlines, s.Substring(i + 1, closeSingle - i - 1));
                    target.Add(span);
                    i = closeSingle + 1;
                    continue;
                }
                sb.Append(c);
                i++;
                continue;
            }

            sb.Append(c);
            i++;
        }

        FlushText(target, sb);
    }

    private static void FlushText(InlineCollection target, StringBuilder sb)
    {
        if (sb.Length == 0)
            return;
        target.Add(new Run(sb.ToString()));
        sb.Clear();
    }

    private static int FindClose(string s, int start, char marker, int markerCount)
    {
        var needle = new string(marker, markerCount);
        var idx = s.IndexOf(needle, start, StringComparison.Ordinal);
        return idx;
    }

    private static bool TryParseLink(string s, int at, out int end, out string text, out string url)
    {
        end = -1;
        text = string.Empty;
        url = string.Empty;

        var closeBracket = s.IndexOf(']', at + 1);
        if (closeBracket < 0 || closeBracket + 1 >= s.Length || s[closeBracket + 1] != '(')
            return false;
        var closeParen = s.IndexOf(')', closeBracket + 2);
        if (closeParen < 0)
            return false;

        var urlPart = s.Substring(closeBracket + 2, closeParen - closeBracket - 2).Trim();
        if (urlPart.Length == 0 || urlPart.Contains(' '))
            return false;

        text = s.Substring(at + 1, closeBracket - at - 1);
        url = urlPart;
        end = closeParen;
        return true;
    }
}