using System;
using System.Collections.Generic;

namespace Md2OneNote.Core
{
    /// <summary>A heading, levels 1 through 6, mapped to the matching OneNote heading style.</summary>
    public sealed class HeadingBlock : IBlock
    {
        public HeadingBlock(int level, IReadOnlyList<IInline> content)
        {
            if (level < 1 || level > 6) throw new ArgumentOutOfRangeException(nameof(level));
            if (content == null) throw new ArgumentNullException(nameof(content));

            Level = level;
            Content = content;
        }

        public int Level { get; }

        public IReadOnlyList<IInline> Content { get; }
    }

    public sealed class ParagraphBlock : IBlock
    {
        public ParagraphBlock(IReadOnlyList<IInline> content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));

            Content = content;
        }

        public IReadOnlyList<IInline> Content { get; }
    }

    /// <summary>A block quote. Its children are ordinary blocks, so quotes nest.</summary>
    public sealed class QuoteBlock : IBlock
    {
        public QuoteBlock(IReadOnlyList<IBlock> children)
        {
            if (children == null) throw new ArgumentNullException(nameof(children));

            Children = children;
        }

        public IReadOnlyList<IBlock> Children { get; }
    }

    public enum ListKind
    {
        Bulleted,
        Ordered
    }

    /// <summary>Whether a list item carries a GFM task checkbox, and if so its state.</summary>
    public enum TaskState
    {
        None,
        Unchecked,
        Checked
    }

    public sealed class ListItem
    {
        public ListItem(TaskState task, IReadOnlyList<IBlock> content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));

            Task = task;
            Content = content;
        }

        public TaskState Task { get; }

        /// <summary>
        /// The item's own blocks. The first is normally a paragraph, which the renderer folds onto
        /// the bulleted <c>one:OE</c> itself; anything after it becomes a nested child.
        /// </summary>
        public IReadOnlyList<IBlock> Content { get; }
    }

    public sealed class ListBlock : IBlock
    {
        public ListBlock(ListKind kind, IReadOnlyList<ListItem> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            Kind = kind;
            Items = items;
        }

        public ListKind Kind { get; }

        public IReadOnlyList<ListItem> Items { get; }
    }

    /// <summary>
    /// A code block, fenced or indented. <see cref="Language"/> is the fence info string, empty
    /// when there is none; an unrecognized language is rendered without highlighting rather than
    /// failing (FR-13).
    /// </summary>
    public sealed class CodeBlock : IBlock
    {
        public CodeBlock(string language, string code)
        {
            if (code == null) throw new ArgumentNullException(nameof(code));

            Language = language ?? string.Empty;
            Code = code;
        }

        public string Language { get; }

        public string Code { get; }
    }

    /// <summary>
    /// A fence whose language is registered as renderable. The block keeps the original source, so
    /// that a failed render can still emit it as a code block (FR-14).
    /// </summary>
    public sealed class DiagramBlock : IBlock
    {
        public DiagramBlock(string key, string language, string source)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            if (string.IsNullOrEmpty(language)) throw new ArgumentNullException(nameof(language));
            if (source == null) throw new ArgumentNullException(nameof(source));

            Key = key;
            Language = language;
            Source = source;
        }

        /// <summary>Matches the <see cref="DiagramRequest.Key"/> pass 2 resolved.</summary>
        public string Key { get; }

        public string Language { get; }

        public string Source { get; }
    }

    /// <summary>
    /// A referenced image. Images are always blocks: OneNote has no way to place a
    /// <c>one:Image</c> inside a <c>one:T</c>, so an image written inline is lifted out of its
    /// paragraph by the parser.
    /// </summary>
    public sealed class ImageBlock : IBlock
    {
        public ImageBlock(string key, string rawPath, string altText)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            if (rawPath == null) throw new ArgumentNullException(nameof(rawPath));

            Key = key;
            RawPath = rawPath;
            AltText = altText ?? string.Empty;
        }

        /// <summary>Matches the <see cref="AssetRequest.Key"/> pass 2 resolved.</summary>
        public string Key { get; }

        public string RawPath { get; }

        public string AltText { get; }
    }

    public sealed class TableCell
    {
        public TableCell(IReadOnlyList<IBlock> content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));

            Content = content;
        }

        public IReadOnlyList<IBlock> Content { get; }
    }

    public sealed class TableRow
    {
        public TableRow(bool isHeader, IReadOnlyList<TableCell> cells)
        {
            if (cells == null) throw new ArgumentNullException(nameof(cells));

            IsHeader = isHeader;
            Cells = cells;
        }

        public bool IsHeader { get; }

        public IReadOnlyList<TableCell> Cells { get; }
    }

    public sealed class TableBlock : IBlock
    {
        public TableBlock(int columnCount, IReadOnlyList<TableRow> rows)
        {
            if (columnCount <= 0) throw new ArgumentOutOfRangeException(nameof(columnCount));
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            ColumnCount = columnCount;
            Rows = rows;
        }

        /// <summary>
        /// The widest row's cell count. Short rows are padded at render time so that every OneNote
        /// row has the same number of cells, which the schema requires.
        /// </summary>
        public int ColumnCount { get; }

        public IReadOnlyList<TableRow> Rows { get; }

        public bool HasHeaderRow
        {
            get { return Rows.Count > 0 && Rows[0].IsHeader; }
        }
    }

    public sealed class ThematicBreakBlock : IBlock
    {
        private ThematicBreakBlock()
        {
        }

        public static ThematicBreakBlock Instance { get; } = new ThematicBreakBlock();
    }

    public sealed class FootnoteDefinition
    {
        public FootnoteDefinition(int number, IReadOnlyList<IBlock> content)
        {
            if (number <= 0) throw new ArgumentOutOfRangeException(nameof(number));
            if (content == null) throw new ArgumentNullException(nameof(content));

            Number = number;
            Content = content;
        }

        public int Number { get; }

        public IReadOnlyList<IBlock> Content { get; }
    }

    /// <summary>
    /// The footnote definitions, always last in the document. The heading above them is resolved
    /// through <see cref="IStringCatalog"/> at render time rather than being fixed here, because a
    /// hardcoded "Notes" is exactly the English dependency NFR-11 forbids.
    /// </summary>
    public sealed class FootnotesBlock : IBlock
    {
        public FootnotesBlock(IReadOnlyList<FootnoteDefinition> notes)
        {
            if (notes == null) throw new ArgumentNullException(nameof(notes));

            Notes = notes;
        }

        public IReadOnlyList<FootnoteDefinition> Notes { get; }
    }
}
