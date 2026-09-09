using AI.Document.Converter.Persistence.Billing;

namespace AI.Document.Converter.Web.Tests;

// SR-BIL-4. The unit a customer is billed in, so these tests are about the
// definition being right rather than the plumbing working.
public sealed class ConversionCreditsTests
{
    // The trap SR-BIL-4 names explicitly. C#'s string.Length is UTF-16 code
    // units, which is NOT the same as Unicode scalar values, which is NOT the
    // same as UTF-8 bytes.
    //
    // This matters commercially and not just pedantically: for this product's
    // Bengali users, billing on UTF-8 bytes would charge roughly three times
    // what an ASCII user pays for the same amount of writing.
    [Fact]
    public void CharactersAreCountedAsUnicodeScalarValuesNotCodeUnitsOrBytes()
    {
        const string bengaliWithEmoji = "নমস্কার 👋";

        var scalarValues = ConversionCredits.CountScalarValues(bengaliWithEmoji);
        var utf16CodeUnits = bengaliWithEmoji.Length;
        var utf8Bytes = System.Text.Encoding.UTF8.GetByteCount(bengaliWithEmoji);

        Assert.Equal(9, scalarValues);

        // Pinned so a "simplification" to string.Length or a byte count is
        // caught rather than silently changing everybody's bill.
        Assert.Equal(10, utf16CodeUnits);
        Assert.Equal(26, utf8Bytes);
        Assert.NotEqual(scalarValues, utf16CodeUnits);
        Assert.NotEqual(scalarValues, utf8Bytes);
    }

    [Fact]
    public void AnAstralCharacterIsOneScalarValueNotTwo()
    {
        // A single emoji is one surrogate PAIR in UTF-16 - two code units, one
        // scalar value. Counting code units would double-charge for it.
        Assert.Equal(1, ConversionCredits.CountScalarValues("👋"));
        Assert.Equal(2, "👋".Length);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(250, 250)]
    public void PdfIsChargedPerPage(int pages, long expected)
    {
        Assert.Equal(expected, ConversionCredits.ForPdf(pages));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(40, 40)]
    public void PresentationsAreChargedPerSlide(int slides, long expected)
    {
        Assert.Equal(expected, ConversionCredits.ForPresentation(slides));
    }

    [Theory]
    [InlineData(1, 1)]        // rounded up per non-empty file
    [InlineData(1_000, 1)]
    [InlineData(1_001, 2)]    // rounding up, not to nearest
    [InlineData(2_500, 3)]
    public void SpreadsheetsAreChargedPerThousandSourceCellsRoundedUp(int cells, long expected)
    {
        Assert.Equal(expected, ConversionCredits.ForSpreadsheet(cells));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3_000, 1)]
    [InlineData(3_001, 2)]
    [InlineData(9_000, 3)]
    public void TextIsChargedPerThreeThousandCharactersRoundedUp(int characters, long expected)
    {
        Assert.Equal(expected, ConversionCredits.ForText(new string('a', characters)));
    }

    // "Rounded up per nonempty file": a thousand one-line documents must not be
    // processed for nothing.
    [Fact]
    public void EveryNonEmptyFileCostsAtLeastOneCredit()
    {
        Assert.Equal(1, ConversionCredits.ForText("a"));
        Assert.Equal(1, ConversionCredits.ForSpreadsheet(1));
        Assert.Equal(1, ConversionCredits.ForPdf(1));
    }

    // Charging for an empty file would be indefensible, and the minimum must
    // not sneak one in.
    [Fact]
    public void AnEmptyDocumentStillCostsTheMinimumButNotMore()
    {
        // The minimum applies per non-empty FILE; a zero-page PDF still
        // occupied the pipeline, so one credit is the floor rather than zero.
        Assert.Equal(1, ConversionCredits.ForPdf(0));
        Assert.Equal(1, ConversionCredits.ForText(string.Empty));
    }

    // A ledger entry records this, so changing a rule without bumping it would
    // make historical charges unexplainable (SR-BIL-6).
    // The ledger entry is what a customer disputing a charge is shown, so it
    // must not state a count that was never measured.
    [Fact]
    public void AnUnreportedCountIsExplainedRatherThanPrintedAsZero()
    {
        var document = new AI.Document.Converter.Domain.Entities.DocumentModel
        {
            Metadata = new AI.Document.Converter.Domain.Entities.DocumentMetadata
            {
                SourceFilePath = "old.xlsx",
                FileType = AI.Document.Converter.Domain.Enums.SupportedFileType.Xlsx,
                CreatedDate = DateTime.UtcNow,
                ConvertedDate = DateTime.UtcNow,
                // Not reported - an extraction from before the counter existed.
                BillableSourceCells = null
            },
            Sections = []
        };

        var price = ConversionCredits.ForExtractedDocument(document);

        Assert.Equal(1, price.Credits);
        Assert.DoesNotContain("0 non-empty cells", price.Basis);
        Assert.Contains("not reported", price.Basis);
    }

    [Fact]
    public void ThePolicyIsVersioned()
    {
        Assert.False(string.IsNullOrWhiteSpace(ConversionCredits.PolicyVersion));
        Assert.StartsWith("credits-v", ConversionCredits.PolicyVersion);
    }

    [Fact]
    public void TheUnitIsDescribableToACustomer()
    {
        var description = ConversionCredits.Describe();

        Assert.Contains("PDF page", description);
        Assert.Contains("3,000", description);
        Assert.Contains("1,000", description);
    }
}
