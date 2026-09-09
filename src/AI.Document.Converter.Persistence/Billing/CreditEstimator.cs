using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace AI.Document.Converter.Persistence.Billing;

// What a job is expected to cost, and how confident that number is. Exactness is
// carried explicitly because an estimate presented as a fact is how a customer
// ends up feeling misled by a bill that did not match the quote.
public sealed record CreditEstimate(long Credits, string Basis, bool IsExact);

// The pre-extraction half of SR-BIL-5: what to HOLD before any work is done.
//
// Deliberately NOT size-based. Measuring the sample corpus showed bytes-per-unit
// varying by more than a factor of six within a single format - sample.docx is
// 182 bytes per character, image-sample.docx is 1,196, because one of them
// embeds a picture. A compressed container's size says almost nothing about how
// much billable content is inside it, so a size heuristic would both refuse
// small-but-heavy files and wave through large-but-empty ones.
//
// Instead this reads the CONTAINER, which is cheap and mostly exact: a PPTX
// states its slides as separate parts, an XLSX sheet declares its used range,
// and a TXT file is just its own characters. No extraction runs, no Python
// subprocess starts, and no document is parsed for content - which is also what
// makes this safe to expose as a preflight quote. Letting a caller price a file
// for free must not let them charge us real processing work for free.
//
// Over-estimating is the safe direction: the hold is provisional, and
// MeteringService.CloseReservationAsync hands back whatever the actual
// settlement did not consume.
public static class CreditEstimator
{
    // A quote is worthless if producing it is itself expensive. These cap the
    // work regardless of what the container claims; the upload validator has
    // already rejected decompression bombs before anything reaches here.
    private const int MaxInspectedEntries = 2_000;
    private const int MaxInspectedTextBytes = 8 * 1024 * 1024;

    public static CreditEstimate ForUpload(string fileName, Stream content)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        try
        {
            return extension switch
            {
                ".txt" => ForPlainText(content),
                ".pptx" => ForPresentation(content),
                ".xlsx" => ForSpreadsheet(content),
                ".docx" => ForWordProcessing(content),
                ".pdf" => ForPdf(content),
                _ => Unknown("an unrecognised file type")
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
        {
            // A container we cannot read is not a billing failure. The upload
            // will be rejected or the extraction will fail, and either way the
            // hold is released - so quote the floor rather than blocking here.
            return Unknown("a file whose structure could not be inspected");
        }
    }

    private static CreditEstimate ForPlainText(Stream content)
    {
        content.Position = 0;

        // Bounded: a very large text file is priced from a sample scaled up,
        // rather than by pulling all of it into memory.
        var buffer = new byte[Math.Min(content.Length, MaxInspectedTextBytes)];
        var read = content.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        var scalarValues = (long)ConversionCredits.CountScalarValues(
            Encoding.UTF8.GetString(buffer, 0, read));

        var isExact = read >= content.Length;
        if (!isExact && read > 0)
        {
            scalarValues = (long)(scalarValues * ((double)content.Length / read));
        }

        return new CreditEstimate(
            CreditsForCharacters(scalarValues), Describe(scalarValues, "characters"), isExact);
    }

    // Exact. Each slide is its own part in the package, so counting parts counts
    // slides without opening any of them.
    private static CreditEstimate ForPresentation(Stream content)
    {
        content.Position = 0;
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);

        var slides = archive.Entries
            .Take(MaxInspectedEntries)
            .Count(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
                        && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

        return slides == 0
            ? Unknown("a presentation with no readable slides")
            : new CreditEstimate(
                ConversionCredits.ForPresentation(slides), Describe(slides, "slides"), IsExact: true);
    }

    // An UPPER BOUND, not a count. A sheet declares its used range, which spans
    // empty cells too, while billing counts only non-empty ones. Erring high is
    // correct for a hold: the unused portion comes back at settlement, whereas
    // erring low would let a job start that the allowance cannot cover.
    private static CreditEstimate ForSpreadsheet(Stream content)
    {
        content.Position = 0;
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);

        long cells = 0;
        var sawDimension = false;

        foreach (var entry in archive.Entries.Take(MaxInspectedEntries))
        {
            if (!entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                || !entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var dimension = ReadDimensionRef(entry);
            if (dimension is null)
            {
                continue;
            }

            sawDimension = true;
            cells += CellsInRange(dimension);
        }

        return sawDimension
            ? new CreditEstimate(
                ConversionCredits.ForSpreadsheet((int)Math.Min(cells, int.MaxValue)),
                Describe(cells, "cells in the used range"),
                IsExact: false)
            : Unknown("a workbook that does not declare its used range");
    }

    // The document body is one part, so its text length is readable without
    // interpreting the formatting around it. Tag-stripping approximates what the
    // extractor will produce rather than reproducing it, so this is not exact.
    private static CreditEstimate ForWordProcessing(Stream content)
    {
        content.Position = 0;
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);

        var body = archive.GetEntry("word/document.xml");
        if (body is null)
        {
            return Unknown("a document with no readable body");
        }

        using var stream = body.Open();
        var buffer = new byte[MaxInspectedTextBytes];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        var characters = CountTextOutsideTags(Encoding.UTF8.GetString(buffer, 0, read));

        return new CreditEstimate(
            CreditsForCharacters(characters), Describe(characters, "characters"), IsExact: false);
    }

    // Best effort, and honest about it. Counting pages properly means parsing the
    // page tree, and in a PDF that uses cross-reference streams the tree itself
    // can sit inside a compressed object stream where this scan cannot see it.
    // Both signals are therefore taken and the larger wins, because a hold that
    // is too small is worse than one that is too large.
    private static CreditEstimate ForPdf(Stream content)
    {
        content.Position = 0;

        using var reader = new StreamReader(content, Encoding.Latin1, false, 64 * 1024, leaveOpen: true);
        var raw = reader.ReadToEnd();

        // "/Type /Pages" is the page TREE, not a page, so it must not be counted
        // as one.
        var pageObjects = CountOccurrences(raw, "/Type /Page") + CountOccurrences(raw, "/Type/Page")
            - CountOccurrences(raw, "/Type /Pages") - CountOccurrences(raw, "/Type/Pages");

        var pages = Math.Max(pageObjects, LargestPageTreeCount(raw));

        return pages <= 0
            ? Unknown("a PDF whose page count could not be read")
            : new CreditEstimate(ConversionCredits.ForPdf((int)Math.Min(pages, int.MaxValue)),
                Describe(pages, "pages"), IsExact: false);
    }

    // The floor, used whenever the container will not say. Never zero: a file
    // that cannot be measured still occupies the pipeline, and quoting nothing
    // would make an unmeasurable file the cheapest way to use the service.
    private static CreditEstimate Unknown(string what) =>
        new(ConversionCredits.MinimumCreditsPerFile,
            $"the {ConversionCredits.MinimumCreditsPerFile}-credit minimum for {what}",
            IsExact: false);

    // Applies the text rule to a count that is already known, without
    // materialising a string of that length just to measure it again.
    private static long CreditsForCharacters(long characters) =>
        Math.Max(
            ConversionCredits.MinimumCreditsPerFile,
            characters <= 0
                ? 0
                : (characters + ConversionCredits.ScalarValuesPerCredit - 1)
                    / ConversionCredits.ScalarValuesPerCredit);

    private static string Describe(long units, string noun) =>
        string.Create(CultureInfo.InvariantCulture, $"{units:N0} {noun}");

    private static string? ReadDimensionRef(ZipArchiveEntry entry)
    {
        // The dimension element sits in the sheet header, so a short prefix is
        // enough - there is no need to decompress an entire worksheet.
        using var stream = entry.Open();
        var buffer = new byte[4096];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        var header = Encoding.UTF8.GetString(buffer, 0, read);

        const string marker = "<dimension ref=";
        var start = header.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length + 1;
        if (start >= header.Length)
        {
            return null;
        }

        var end = header.IndexOf('"', start);
        return end < 0 ? null : header[start..end];
    }

    // "A1:E103" -> 5 columns x 103 rows. A single-cell range has no colon.
    private static long CellsInRange(string reference)
    {
        var parts = reference.Split(':');
        if (parts.Length != 2)
        {
            return 1;
        }

        if (!TryParseCell(parts[0], out var firstColumn, out var firstRow)
            || !TryParseCell(parts[1], out var lastColumn, out var lastRow))
        {
            return 0;
        }

        var columns = Math.Max(lastColumn - firstColumn + 1, 0);
        var rows = Math.Max(lastRow - firstRow + 1, 0);
        return columns * rows;
    }

    private static bool TryParseCell(string cell, out long column, out long row)
    {
        column = 0;
        row = 0;

        var index = 0;
        while (index < cell.Length && char.IsAsciiLetter(cell[index]))
        {
            column = (column * 26) + (char.ToUpperInvariant(cell[index]) - 'A' + 1);
            index++;
        }

        return column > 0
            && index < cell.Length
            && long.TryParse(cell[index..], NumberStyles.None, CultureInfo.InvariantCulture, out row)
            && row > 0;
    }

    private static long CountTextOutsideTags(string xml)
    {
        long characters = 0;
        var insideTag = false;

        foreach (var rune in xml.EnumerateRunes())
        {
            if (rune.Value == '<')
            {
                insideTag = true;
            }
            else if (rune.Value == '>')
            {
                insideTag = false;
            }
            else if (!insideTag)
            {
                characters++;
            }
        }

        return characters;
    }

    private static long LargestPageTreeCount(string raw)
    {
        long largest = 0;
        var index = 0;

        while ((index = raw.IndexOf("/Count", index, StringComparison.Ordinal)) >= 0)
        {
            index += "/Count".Length;

            var digitsStart = index;
            while (digitsStart < raw.Length && raw[digitsStart] == ' ')
            {
                digitsStart++;
            }

            var digitsEnd = digitsStart;
            while (digitsEnd < raw.Length && char.IsAsciiDigit(raw[digitsEnd]))
            {
                digitsEnd++;
            }

            if (digitsEnd > digitsStart
                && long.TryParse(
                    raw[digitsStart..digitsEnd], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                largest = Math.Max(largest, value);
            }
        }

        return largest;
    }

    private static long CountOccurrences(string haystack, string needle)
    {
        long count = 0;
        var index = 0;

        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
