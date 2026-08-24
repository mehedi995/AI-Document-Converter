using System.Text;
using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.ValueObjects;

namespace AI.Document.Converter.Application.Services;

// FR-012: pure transformation from DocumentModel to Markdown text - no file
// I/O (docs/15-IMPLEMENTATION-PLAN.md Section 4).
public sealed class MarkdownGenerator : IMarkdownGenerator
{
    public string Generate(DocumentModel document)
    {
        var builder = new StringBuilder();

        builder.Append(FrontMatterBuilder.Build(document.Metadata)).Append("\n\n");

        foreach (var section in document.Sections)
        {
            AppendSection(builder, section);
        }

        return builder.ToString().TrimEnd() + "\n";
    }

    private static void AppendSection(StringBuilder builder, Section section)
    {
        if (!string.IsNullOrWhiteSpace(section.Heading))
        {
            var level = Math.Clamp(section.HeadingLevel ?? 1, 1, 6);
            builder.Append('#', level).Append(' ').Append(section.Heading).Append("\n\n");
        }

        // FR-012: a page/slide reference is always emitted when present, even
        // for a section with no heading (e.g., a PDF page after the first) -
        // sheet name is skipped here since the xlsx extractor already uses it
        // as the section heading itself.
        var reference = BuildReferenceLine(section.Location);
        if (reference is not null)
        {
            builder.Append(reference).Append("\n\n");
        }

        foreach (var block in section.Blocks)
        {
            AppendBlock(builder, block);
        }
    }

    private static string? BuildReferenceLine(SourceLocation? location)
    {
        if (location?.PageNumber is int pageNumber)
        {
            return $"*Page {pageNumber}*";
        }

        if (location?.SlideNumber is int slideNumber)
        {
            return $"*Slide {slideNumber}*";
        }

        return null;
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
