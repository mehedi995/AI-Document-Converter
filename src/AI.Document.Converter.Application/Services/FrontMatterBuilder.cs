using System.Text;
using AI.Document.Converter.Domain.Entities;

namespace AI.Document.Converter.Application.Services;

// FR-013. Extends CLAUDE.md's illustrative front-matter template (source,
// file_type, created_date, converted_date, pages, author) with slides/sheets,
// since the data model tracks page/slide/sheet counts as three distinct
// optional fields rather than one - forcing a DOCX file to report a
// meaningless "pages: null" would be worse than naming the field for what it
// actually counts.
public static class FrontMatterBuilder
{
    public static string Build(DocumentMetadata metadata)
    {
        var builder = new StringBuilder();
        builder.Append("---\n");

        builder.Append("source: ").Append(YamlString(Path.GetFileName(metadata.SourceFilePath))).Append('\n');
        builder.Append("file_type: ").Append(metadata.FileType.ToString().ToLowerInvariant()).Append('\n');
        builder.Append("created_date: ").Append(FormatDate(metadata.CreatedDate)).Append('\n');
        builder.Append("converted_date: ").Append(FormatDate(metadata.ConvertedDate)).Append('\n');

        if (metadata.PageCount is not null)
        {
            builder.Append("pages: ").Append(metadata.PageCount).Append('\n');
        }

        if (metadata.SlideCount is not null)
        {
            builder.Append("slides: ").Append(metadata.SlideCount).Append('\n');
        }

        if (metadata.SheetCount is not null)
        {
            builder.Append("sheets: ").Append(metadata.SheetCount).Append('\n');
        }

        // Omitted entirely when absent (not written as an empty string), so
        // downstream RAG parsing stays consistent across formats.
        if (!string.IsNullOrWhiteSpace(metadata.Author))
        {
            builder.Append("author: ").Append(YamlString(metadata.Author)).Append('\n');
        }

        builder.Append("---");
        return builder.ToString();
    }

    private static string FormatDate(DateTime value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

    private static string YamlString(string value) =>
        $"\"{value.Replace("\"", "\\\"")}\"";
}
