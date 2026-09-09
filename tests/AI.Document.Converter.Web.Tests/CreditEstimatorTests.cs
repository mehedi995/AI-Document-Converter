using AI.Document.Converter.Persistence.Billing;

namespace AI.Document.Converter.Web.Tests;

// Against the real sample corpus, because the whole point of this estimator is
// that it reads real containers. A hand-built byte array would prove that the
// parsing code runs, not that it understands what Word and Excel actually write.
//
// The expected counts are ground truth measured independently with the Python
// libraries (pdfplumber, openpyxl, python-docx) rather than by running the code
// under test: a test that asserts the estimator agrees with itself is worthless.
public sealed class CreditEstimatorTests
{
    private static string SamplePath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "AI.Document.Converter.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var path = Path.Combine(directory.FullName, "samples", fileName);
        Assert.True(File.Exists(path), $"Sample not found at '{path}'. Run scripts/generate-samples.py.");
        return path;
    }

    private static CreditEstimate Estimate(string fileName)
    {
        using var stream = File.OpenRead(SamplePath(fileName));
        var seekable = new MemoryStream();
        stream.CopyTo(seekable);
        return CreditEstimator.ForUpload(fileName, seekable);
    }

    // Ground truth: sample.pptx has 2 slide parts, image-sample.pptx has 1.
    [Theory]
    [InlineData("sample.pptx", 2)]
    [InlineData("image-sample.pptx", 1)]
    public void SlidesAreCountedExactlyFromThePackage(string fileName, long expectedCredits)
    {
        var estimate = Estimate(fileName);

        Assert.Equal(expectedCredits, estimate.Credits);

        // A presentation is the one format where the container states the
        // billable unit directly, so the quote can be offered as exact.
        Assert.True(estimate.IsExact);
    }

    // Ground truth from pdfplumber: sample.pdf is 3 pages, image-sample.pdf is 1.
    [Theory]
    [InlineData("sample.pdf", 3)]
    [InlineData("image-sample.pdf", 1)]
    public void PdfPagesAreEstimatedFromTheDocumentStructure(string fileName, long expectedCredits)
    {
        var estimate = Estimate(fileName);

        Assert.Equal(expectedCredits, estimate.Credits);

        // Never advertised as exact: a PDF using cross-reference streams can
        // hide its page tree from this scan.
        Assert.False(estimate.IsExact);
    }

    // Ground truth: 230 scalar values, one credit under the 3,000 character
    // rule. Counted from the RAW bytes - reading the file in Python text mode
    // reports 224, because it silently collapses this file's six CRLF pairs.
    // The customer is billed for what is in the file, not for what a particular
    // reader's newline translation leaves behind.
    [Fact]
    public void PlainTextIsCountedExactly()
    {
        var estimate = Estimate("sample.txt");

        Assert.Equal(1, estimate.Credits);
        Assert.True(estimate.IsExact);
        Assert.Contains("230 characters", estimate.Basis);
    }

    // The used range spans empty cells, so the estimate is an upper bound on the
    // 515 non-empty cells openpyxl reports. It must still land on one credit
    // here, and must never claim to be exact.
    [Fact]
    public void SpreadsheetsAreEstimatedFromTheDeclaredUsedRange()
    {
        var estimate = Estimate("sample.xlsx");

        Assert.Equal(1, estimate.Credits);
        Assert.False(estimate.IsExact);
        Assert.Contains("used range", estimate.Basis);
    }

    // An over-estimate is refunded at settlement; an under-estimate lets a job
    // start that the allowance cannot cover. So the bound has to hold in the
    // safe direction, not merely be "close".
    [Fact]
    public void TheSpreadsheetEstimateIsNotBelowTheTrueCellCount()
    {
        var estimate = Estimate("sample.xlsx");

        // 515 non-empty cells, measured with openpyxl.
        Assert.True(
            estimate.Credits >= ConversionCredits.ForSpreadsheet(515),
            $"The hold must not be below the real cost; got {estimate.Credits}.");
    }

    [Fact]
    public void WordDocumentsAreEstimatedFromTheDocumentBody()
    {
        var estimate = Estimate("sample.docx");

        Assert.Equal(1, estimate.Credits);
        Assert.False(estimate.IsExact);
    }

    // A file the estimator cannot read must not be free. Quoting zero would make
    // an unreadable file the cheapest way to occupy the pipeline.
    [Fact]
    public void AnUnreadableFileIsQuotedAtTheMinimumRatherThanNothing()
    {
        using var notAZip = new MemoryStream("this is not a docx"u8.ToArray());

        var estimate = CreditEstimator.ForUpload("broken.docx", notAZip);

        Assert.Equal(ConversionCredits.MinimumCreditsPerFile, estimate.Credits);
        Assert.False(estimate.IsExact);
    }

    [Fact]
    public void AnUnpricedFileTypeIsQuotedAtTheMinimum()
    {
        using var content = new MemoryStream("data"u8.ToArray());

        var estimate = CreditEstimator.ForUpload("mystery.rtf", content);

        Assert.Equal(ConversionCredits.MinimumCreditsPerFile, estimate.Credits);
    }

    // Every estimate has to be explainable to the person being charged.
    [Fact]
    public void EveryEstimateSaysWhatItCounted()
    {
        foreach (var fileName in new[] { "sample.pdf", "sample.docx", "sample.xlsx", "sample.pptx", "sample.txt" })
        {
            var estimate = Estimate(fileName);

            Assert.False(string.IsNullOrWhiteSpace(estimate.Basis), $"{fileName} produced no basis");
            Assert.True(estimate.Credits >= 1, $"{fileName} was quoted at nothing");
        }
    }
}
