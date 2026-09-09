using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;

using NUnit.Framework;

using NotePadClone.Services;

namespace NotePadCloneTests;

/// <summary>
/// v1.2.1 table-crash fix tests: Markdown tables must be built as a WPF Grid (Control) hosted in a BlockUIContainer,
/// and must never produce a FlowDocument.Table again — the latter triggers a non-managed PtsHost native crash during reflow/resize.
/// </summary>
[TestFixture]
public class MarkdownToFlowDocumentTests
{
    private const string TableMd = @"
| 名稱 | 數量 | 價格 | 備註 |
|:-----|-----:|-----:|:-----|
| 蘋果 | 3 | 12.5 | 靚貨 |
| 香蕉 | 10 | 4.0 | |
| 西瓜 | 1 | 25.9 | 大果 |
";

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Render_Table_ProducesBlockUIContainerGrid_NotTable()
    {
        var doc = MarkdownToFlowDocument.Render(TableMd);

        var block = doc.Blocks.FirstBlock;
        Assert.That(block, Is.InstanceOf<BlockUIContainer>(), "表格必須包喺 BlockUIContainer 度,唔可以用 FlowDocument.Table");

        var layout = GetTableGrid(doc);
        Assert.That(layout, Is.Not.Null, "BlockUIContainer 內必須放 WPF Grid");
        Assert.That(layout.RowDefinitions.Count, Is.EqualTo(4), "表頭 1 行 + 主體 3 行");
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Render_Table_WrapsInPerTableHorizontalScrollViewer()
    {
        var doc = MarkdownToFlowDocument.Render(TableMd);

        var block = doc.Blocks.OfType<BlockUIContainer>().FirstOrDefault();
        Assert.That(block, Is.Not.Null);

        // v1.4: outer ScrollViewer enables horizontal only (Auto); vertical Disabled (table height still flows with the document)
        Assert.That(block!.Child, Is.InstanceOf<ScrollViewer>(), "BlockUIContainer 內必須包住 per-table ScrollViewer");
        var scroll = (ScrollViewer)block.Child;
        Assert.That(scroll.HorizontalScrollBarVisibility, Is.EqualTo(ScrollBarVisibility.Auto), "橫向條 Auto：窄表格唔出條,超規格先出");
        Assert.That(scroll.VerticalScrollBarVisibility, Is.EqualTo(ScrollBarVisibility.Disabled), "縱向唔滾：滾動交返畀外層文檔");

        // Width constraint: ScrollViewer.Width must bind to the host FlowDocumentScrollViewer's viewport width (Auto would stretch it open)
        var binding = BindingOperations.GetBinding(scroll, FrameworkElement.WidthProperty);
        Assert.That(binding, Is.Not.Null, "ScrollViewer 必須有寬度綁定（否則無限寬度量度→橫條永不出現）");
        var rr = binding!.RelativeSource as RelativeSource;
        Assert.That(rr, Is.Not.Null);
        Assert.That(rr!.Mode, Is.EqualTo(RelativeSourceMode.FindAncestor));
        Assert.That(rr.AncestorType, Is.EqualTo(typeof(FlowDocumentScrollViewer)));

        // Content structure is still a host Border wrapping the Grid (matches the two-level structure in existing tests)
        Assert.That(scroll.Content, Is.InstanceOf<Border>());
        Assert.That(((Border)scroll.Content).Child, Is.InstanceOf<Grid>());
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Render_Table_HeaderRow_IsBold()
    {
        var doc = MarkdownToFlowDocument.Render(TableMd);
        var grid = GetTableGrid(doc);

        foreach (var cell in GetRowCells(grid, 0))
        {
            var text = GetCellTextBlock(cell);
            Assert.That(text.FontWeight, Is.EqualTo(FontWeights.Bold), $"表頭文字「{text.Text}」要粗體");
        }

        // Body cells must not be bold
        var bodyText = GetCellTextBlock(GetRowCells(grid, 1)[0]);
        Assert.That(bodyText.FontWeight, Is.Not.EqualTo(FontWeights.Bold));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Render_Table_NumericColumn_RightAligned()
    {
        var doc = MarkdownToFlowDocument.Render(TableMd);
        var grid = GetTableGrid(doc);

        // Columns 2 / 3 are `-----:` → right-aligned; columns 1 / 4 are left/right aligned
        var bodyRow = GetRowCells(grid, 1);
        Assert.That(GetCellTextBlock(bodyRow[1]).TextAlignment, Is.EqualTo(TextAlignment.Right), "數值欄 欄2 要右對齊");
        Assert.That(GetCellTextBlock(bodyRow[2]).TextAlignment, Is.EqualTo(TextAlignment.Right), "數值欄 欄3 要右對齊");
        Assert.That(GetCellTextBlock(bodyRow[0]).TextAlignment, Is.EqualTo(TextAlignment.Left), "欄1 左對齊");
        Assert.That(GetCellTextBlock(bodyRow[3]).TextAlignment, Is.EqualTo(TextAlignment.Left), "欄4 左對齊");
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Render_Table_RaggedRows_PaddedToColumnCount()
    {
        const string md = @"
| 頭1 | 頭2 | 頭3 |
|-----|-----|-----|
| a   | b   |
| c   |
";
        var doc = MarkdownToFlowDocument.Render(md);
        var grid = GetTableGrid(doc);

        Assert.That(grid.ColumnDefinitions.Count, Is.EqualTo(3));
        // Two body rows; the second row fills only one cell, the remaining cells are still padded empty
        var row2 = GetRowCells(grid, 2);
        Assert.That(row2.Count, Is.EqualTo(3));
        Assert.That(GetCellTextBlock(row2[1]).Text, Is.Empty);
        Assert.That(GetCellTextBlock(row2[2]).Text, Is.Empty);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Render_MixedDocument_ContainsNoTableElement()
    {
        const string md = @"
# 標題

段落文字 **粗體**。

- 項目一
- 項目二

| 欄1 | 欄2 |
|-----|-----|
| a | 1 |

```csharp
int x = 1;
```

> 引用

---

尾段[連結](https://example.com)。
";
        var doc = MarkdownToFlowDocument.Render(md);

        // Scan the entire Block tree: a FlowDocument.Table must not appear at any level
        AssertIsTableFree(doc);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Render_Table_BlockUIContainer_HoldsOnlyControls()
    {
        var doc = MarkdownToFlowDocument.Render(TableMd);
        var layout = GetTableGrid(doc);

        // No FlowDocument element (Block/Inline content element) may appear inside a BlockUIContainer
        var types = new List<string>();
        foreach (var child in layout.Children)
        {
            Assert.That(child, Is.InstanceOf<FrameworkElement>(), "表內孩子必須係 Control 類別");
            if (child is Border border)
            {
                Assert.That(border.Child, Is.InstanceOf<TextBlock>(), "儲存格 Border 內必須係 TextBlock,唔可以係 RichTextBox/FlowDocument 等");
                Assert.That(border.Child, Is.Not.InstanceOf<Block>(), "唔准喺表內放內容元素");
                Assert.That(border.Child, Is.Not.InstanceOf<Inline>());
            }
            else
            {
                types.Add(child.GetType().Name);
            }
        }
        Assert.That(types, Is.Empty, $"發現非 Border 孩子:{string.Join(",", types)}");
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Render_NonTableMarks_StayAsParagraphElements()
    {
        const string md = @"
# 標題

**粗體**、*斜體*、`行內碼`

- 第一項
- 第二項

> 引用文字

```text
代碼
```

---

普通段落。
";
        var doc = MarkdownToFlowDocument.Render(md);
        Assert.That(doc.Blocks.Count, Is.GreaterThan(0));
        foreach (Block block in doc.Blocks)
        {
            // Documents without tables: every block is Paragraph + BlockUIContainer (horizontal rules use Paragraph); not a single Table allowed
            Assert.That(block, Is.Not.TypeOf<Table>(), "唔准再生産 FlowDocument.Table");
        }
    }

    // ===== Helpers =====

    private static Grid GetTableGrid(FlowDocument doc)
    {
        var block = doc.Blocks.OfType<BlockUIContainer>().FirstOrDefault();
        Assert.That(block, Is.Not.Null, "文檔必須包含 BlockUIContainer");
        var current = block!.Child;
        // v1.4: per-table horizontal ScrollViewer (a control class, not a FlowDocument element) wraps the outer border
        // host Border (control class) in turn wraps the actual Grid
        if (current is ScrollViewer scroll)
            current = scroll.Content as FrameworkElement;
        if (current is Border host)
            current = host.Child;
        var grid = current as Grid;
        Assert.That(grid, Is.Not.Null, "BlockUIContainer 內必須放 WPF Grid");
        return grid!;
    }

    private static List<Border> GetRowCells(Grid grid, int row)
    {
        var list = new List<Border>();
        foreach (var child in grid.Children)
        {
            if (child is Border border && Grid.GetRow(border) == row)
                list.Add(border);
        }
        Assert.That(list.Count, Is.EqualTo(grid.ColumnDefinitions.Count),
            $"第 {row} 行儲存格數量要等於欄數 {grid.ColumnDefinitions.Count}");
        list.Sort((a, b) => Grid.GetColumn(a).CompareTo(Grid.GetColumn(b)));
        return list;
    }

    private static TextBlock GetCellTextBlock(Border cell)
    {
        Assert.That(cell.Child, Is.InstanceOf<TextBlock>());
        return (TextBlock)cell.Child;
    }

    private static void AssertIsTableFree(FlowDocument doc)
    {
        foreach (Block block in doc.Blocks)
        {
            Assert.That(block, Is.Not.TypeOf<Table>(), "Block 樹內唔准出現 FlowDocument.Table");
            if (block is BlockUIContainer container)
            {
                Assert.That(container.Child, Is.Not.InstanceOf<Table>());
                Assert.That(container.Child, Is.Not.InstanceOf<FlowDocument>());
            }
        }
    }
}