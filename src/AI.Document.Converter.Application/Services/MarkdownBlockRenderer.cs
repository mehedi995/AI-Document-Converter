using System.Text;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.ValueObjects;

namespace AI.Document.Converter.Application.Services;

// The low-level Markdown formatting rules (headings, lists, tables, links,
// placeholders, page/slide references) shared between MarkdownGenerator (the
// whole document) and ChunkGenerator (individual atomic units) - kept in one
// place so the two never drift apart on what a table or a heading looks like
// as Markdown.
public static class MarkdownBlockRenderer
{
    public static string RenderHeading(Section section)
    {
        if (string.IsNullOrWhiteSpace(section.Heading))
        {
            return string.Empty;
        }

        var level = Math.Clamp(section.HeadingLevel ?? 1, 1, 6);
        return new string('#', level) + " " + section.Heading + "\n\n";
    }

    // FR-012: a page/slide reference is always emitted when present, even for
    // a section with no heading (e.g., a PDF page after the first) - sheet
    // name is skipped since the xlsx extractor already uses it as the section
    // heading itself.
    public static string RenderReference(SourceLocation? location)
    {
        if (location?.PageNumber is int pageNumber)
        {
            return $"*Page {pageNumber}*\n\n";
        }

        if (location?.SlideNumber is int slideNumber)
        {
            return $"*Slide {slideNumber}*\n\n";
        }

        return string.Empty;
    }

    public static string RenderBlock(ContentBlock block)
    {
        var builder = new StringBuilder();
        AppendBlock(builder, block);
        return builder.ToString();
    }

    private static void AppendBlock(StringBuilder builder, ContentBlock block)
    {
        switch (block)
        {
            case ParagraphBlock paragraph:
                builder.Append(paragraph.Text).Append("\n\n");
                break;

            case ListBlock list:
                AppendList(builder, list);
                break;

            case TableBlock table:
                AppendTable(builder, table);
                break;

            case LinkBlock link:
                builder.Append('[').Append(link.Text).Append("](").Append(link.Url).Append(")\n\n");
                break;

            case ImagePlaceholderBlock image:
                builder.Append(image.AltText is null ? "*[Image omitted]*" : $"*[Image: {image.AltText}]*")
                    .Append("\n\n");
                break;

            case UnextractableTextBlock unextractable:
                builder.Append("> ").Append(unextractable.Note).Append("\n\n");
                break;
        }
    }

    private static void AppendList(StringBuilder builder, ListBlock list)
    {
        for (var i = 0; i < list.Items.Count; i++)
        {
            var marker = list.IsOrdered ? $"{i + 1}." : "-";
            builder.Append(marker).Append(' ').Append(list.Items[i]).Append('\n');
        }

        builder.Append('\n');
    }

    private static void AppendTable(StringBuilder builder, TableBlock table)
    {
        AppendTableRow(builder, table.Headers);

        builder.Append('|');
        foreach (var _ in table.Headers)
        {
            builder.Append(" --- |");
        }

        builder.Append('\n');

        foreach (var row in table.Rows)
        {
            AppendTableRow(builder, row);
        }

        builder.Append('\n');
    }

    private static void AppendTableRow(StringBuilder builder, IReadOnlyList<string> cells)
    {
        builder.Append('|');
        foreach (var cell in cells)
        {
            builder.Append(' ').Append(EscapeTableCell(cell)).Append(" |");
        }

        builder.Append('\n');
    }

    // Guards against a cell's own content breaking the table's row/column
    // structure (a literal "|" or embedded newline).
    private static string EscapeTableCell(string value) =>
        value.Replace("|", "\\|").Replace("\n", " ");
}
