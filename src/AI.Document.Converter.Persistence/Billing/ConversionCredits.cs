using System.Globalization;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Enums;

namespace AI.Document.Converter.Persistence.Billing;

// What a finished extraction actually cost, and what was counted to arrive at
// it. The basis travels with the number because a ledger entry that says only
// "7 credits" cannot answer a customer asking why.
public sealed record SettlementPrice(long Credits, string Basis);

// SR-BIL-4. The deterministic processing unit customers are charged in.
//
// DELIBERATELY SEPARATE FROM AI TOKEN ESTIMATES. Token counts are estimates that
// depend on a tokenizer we do not control and cannot reproduce exactly; a bill
// must be reproducible from the input alone. A credit is defined by what is in
// the document, not by what a model would make of it.
//
// The policy is VERSIONED. A ledger entry records the version that priced it, so
// changing the rules never silently rewrites what a customer was already
// charged (SR-BIL-6).
public static class ConversionCredits
{
    // Bump when any rule below changes. Never edit a rule without bumping.
    public const string PolicyVersion = "credits-v1";

    // The baselines named in SR-BIL-4. They are NOT validated commercial
    // pricing - they are a documented, deterministic unit.
    //
    // COST-TESTED 2026-09-10, and they are NOT cost-proportionate: measured
    // against the real engine, one PDF credit costs roughly 28x what one DOCX
    // credit costs, and one XLSX credit roughly 15x. See
    // docs/saas/05-CREDIT-COST-BENCHMARK.md for the method, the numbers and
    // what they do and do not settle.
    //
    // The PDF figure was 37x until a memory fix in the extractor cut PDF cost
    // by 48% (docs/saas/06-PDF-MEMORY.md). Worth knowing before adjusting any
    // ratio: part of this gap is an engineering problem, not a pricing one,
    // and that route is not exhausted.
    //
    // Left unchanged deliberately: whether price should track cost or customer
    // value is a commercial decision that has not been made, and quietly
    // rewriting the ratios here would make that decision by accident. Any
    // change must bump PolicyVersion, because ledger entries record the
    // version that priced them.
    public const int ScalarValuesPerCredit = 3_000;   // DOCX, TXT
    public const int SourceCellsPerCredit = 1_000;    // XLSX, CSV

    // "Rounded up per nonempty file": every file that contains anything costs
    // at least one credit, so a thousand one-line files cannot be processed for
    // nothing.
    //
    // Measurement supports this: every format shows a fixed cost of roughly one
    // second of a worker slot per file before any content is read, dominated by
    // engine startup. A file that produces almost nothing still costs about a
    // second.
    public const int MinimumCreditsPerFile = 1;

    // Charged per page and per slide respectively - one credit each.
    public static long ForPdf(int pageCount) => AtLeastMinimum(pageCount);

    public static long ForPresentation(int slideCount) => AtLeastMinimum(slideCount);

    public static long ForSpreadsheet(int billableSourceCells) =>
        AtLeastMinimum(DivideRoundingUp(billableSourceCells, SourceCellsPerCredit));

    // Unicode SCALAR VALUES, not UTF-16 code units and not UTF-8 bytes.
    //
    // SR-BIL-4 calls this out because the three differ and the difference is not
    // academic: "নমস্কার 👋" is 9 scalar values, 10 UTF-16 code units, and 26
    // UTF-8 bytes. Billing on bytes would charge a Bengali or emoji user roughly
    // three times what an ASCII user pays for the same amount of writing.
    //
    // C#'s string.Length is UTF-16 code units, so it is the wrong answer by
    // default - EnumerateRunes is what counts scalar values.
    public static long ForText(string extractedText)
    {
        var scalarValues = CountScalarValues(extractedText);
        return AtLeastMinimum(DivideRoundingUp(scalarValues, ScalarValuesPerCredit));
    }

    public static int CountScalarValues(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var count = 0;
        foreach (var _ in text.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    // Prices a completed extraction from what the engine actually reported, and
    // says what it counted so the ledger entry can explain the charge.
    // Settlement uses this; the pre-conversion estimate is deliberately a
    // different, more conservative calculation (see CreditEstimator).
    //
    // The plain text is derived HERE rather than accepted from the caller. The
    // obvious thing to hand in is the generated Markdown, and that would charge
    // the customer for heading hashes and table pipes the engine added - the
    // bill has to reflect their document, not our formatting of it.
    public static SettlementPrice ForExtractedDocument(DocumentModel document)
    {
        var metadata = document.Metadata;

        return metadata.FileType switch
        {
            SupportedFileType.Pdf => Priced(ForPdf(metadata.PageCount ?? 0), metadata.PageCount, "pages"),

            SupportedFileType.Pptx =>
                Priced(ForPresentation(metadata.SlideCount ?? 0), metadata.SlideCount, "slides"),

            SupportedFileType.Xlsx => Priced(
                ForSpreadsheet(metadata.BillableSourceCells ?? 0),
                metadata.BillableSourceCells,
                "non-empty cells"),

            SupportedFileType.Docx or SupportedFileType.Txt => PricedText(document),

            // A format with no rule must not be silently free, and must not be
            // guessed at either. Failing loudly is how an unpriced format gets
            // noticed before it ships rather than after.
            _ => throw new NotSupportedException(
                $"No credit rule is defined for {metadata.FileType}. Add one before "
                + "enabling this format for billing.")
        };
    }

    // The customer's own words, without the Markdown we wrapped around them.
    public static string PlainTextOf(DocumentModel document)
    {
        var text = new System.Text.StringBuilder();

        foreach (var section in document.Sections)
        {
            // A section with no heading contributes no characters. Appending an
            // empty line anyway would bill a newline per section that the
            // customer never wrote.
            if (!string.IsNullOrEmpty(section.Heading))
            {
                text.AppendLine(section.Heading);
            }

            foreach (var block in section.Blocks)
            {
                switch (block)
                {
                    case ParagraphBlock paragraph:
                        text.AppendLine(paragraph.Text);
                        break;

                    case ListBlock list:
                        foreach (var listItem in list.Items)
                        {
                            text.AppendLine(listItem);
                        }

                        break;

                    case TableBlock table:
                        text.AppendLine(string.Join(' ', table.Headers));
                        foreach (var row in table.Rows)
                        {
                            text.AppendLine(string.Join(' ', row));
                        }

                        break;

                    case LinkBlock link:
                        // The visible text, not the URL. A customer did not
                        // write the href and should not pay for its length.
                        text.AppendLine(link.Text);
                        break;

                    // An image placeholder and an unextractable-text marker are
                    // both things WE inserted to describe an absence. Charging
                    // for them would bill the customer for content that could
                    // not be recovered.
                }
            }
        }

        return text.ToString();
    }

    private static SettlementPrice PricedText(DocumentModel document)
    {
        var scalarValues = CountScalarValues(PlainTextOf(document));
        return Priced(
            AtLeastMinimum(DivideRoundingUp(scalarValues, ScalarValuesPerCredit)), scalarValues, "characters");
    }

    // A null count means the extractor did not report one - an older extraction,
    // or a format that does not carry it. Writing "0 pages" onto that charge
    // would state a fact we do not have, on the one record a customer disputing
    // a bill will be shown. The minimum still applies; only the explanation
    // changes.
    private static SettlementPrice Priced(long credits, long? units, string noun) =>
        new(credits, units is { } counted
            ? string.Create(CultureInfo.InvariantCulture, $"{counted:N0} {noun}")
            : $"minimum charge - {noun} not reported by the extractor");

    private static SettlementPrice Priced(long credits, long units, string noun) =>
        Priced(credits, (long?)units, noun);

    public static string Describe() =>
        string.Create(CultureInfo.InvariantCulture,
            $"1 credit per PDF page, per PPTX slide, per {ScalarValuesPerCredit:N0} characters of "
            + $"DOCX/TXT text, or per {SourceCellsPerCredit:N0} non-empty spreadsheet cells. "
            + $"Every non-empty file costs at least {MinimumCreditsPerFile} credit.");

    private static long AtLeastMinimum(long credits) => Math.Max(MinimumCreditsPerFile, credits);

    private static long DivideRoundingUp(long value, long divisor) =>
        value <= 0 ? 0 : (value + divisor - 1) / divisor;
}
