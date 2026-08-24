using System.Text;
using AI.Document.Converter.Domain.Entities;

namespace AI.Document.Converter.Application.Services;

// The "original" side of the FR-014/015 token comparison: a naive
// concatenation of the same extracted content with no Markdown formatting
// (no #, |, -, or [text](url) syntax) - approximating what a plain copy/paste
// of the document's text would look like, so the displayed reduction actually
// reflects the value of structuring the content, not an unrelated baseline.
public static class PlainTextRenderer
{
    public static string Render(DocumentModel document)
    {
        var builder = new StringBuilder();

        foreach (var section in document.Sections)
        {
            if (!string.IsNullOrWhiteSpace(section.Heading))
            {
                builder.AppendLine(section.Heading);
            }

            foreach (var block in section.Blocks)
            {
                AppendBlock(builder, block);
            }
        }

        return builder.ToString();
    }

    private static void AppendBlock(StringBuilder builder, ContentBlock block)
    {
        switch (block)
        {
            case ParagraphBlock paragraph:
                builder.AppendLine(paragraph.Text);
                break;

            case ListBlock list:
                foreach (var item in list.Items)
                {
                    builder.AppendLine(item);
                }

                break;

            case TableBlock table:
                builder.AppendLine(string.Join(' ', table.Headers));
                foreach (var row in table.Rows)
                {
                    builder.AppendLine(string.Join(' ', row));
                }

                break;

            case LinkBlock link:
                builder.AppendLine($"{link.Text} {link.Url}");
                break;

            case UnextractableTextBlock unextractable:
                builder.AppendLine(unextractable.Note);
                break;

            case ImagePlaceholderBlock:
                // No text content to contribute.
                break;
        }
    }
}
