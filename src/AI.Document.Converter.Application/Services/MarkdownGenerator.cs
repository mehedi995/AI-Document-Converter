using System.Text;
using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Domain.Entities;

namespace AI.Document.Converter.Application.Services;

// FR-012: pure transformation from DocumentModel to Markdown text - no file
// I/O (docs/15-IMPLEMENTATION-PLAN.md Section 4). Per-block formatting rules
// live in MarkdownBlockRenderer, shared with ChunkGenerator (Phase 7).
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
        builder.Append(MarkdownBlockRenderer.RenderHeading(section));
        builder.Append(MarkdownBlockRenderer.RenderReference(section.Location));

        foreach (var block in section.Blocks)
        {
            builder.Append(MarkdownBlockRenderer.RenderBlock(block));
        }
    }
}
