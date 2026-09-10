# What a conversion credit costs to serve

**Measured 2026-09-10.** Reproduce with `scripts/generate-cost-corpus.py` then
`scripts/measure-conversion-cost.py`.

> **This document does not set prices, and nothing in it is approved commercial terms.**
> It measures what the credit ratios in SR-BIL-4 actually cost us to serve, so that pricing can
> later be decided from evidence. The conclusion is that the current ratios are **not**
> cost-proportionate, by a wide margin.

---

## 1. What was being tested

SR-BIL-4 defines a conversion credit as:

| | one credit |
|---|---|
| PDF | 1 page |
| PPTX | 1 slide |
| DOCX / TXT | 3,000 characters |
| XLSX | 1,000 non-empty cells |

Those baselines were chosen to be **deterministic and explainable**, not because anyone had
measured them. `ConversionCredits` says so in its own comment: "must be cost-tested before anything
is sold." This is that test.

The question is narrow: **does one credit mean roughly the same amount of work regardless of
format?** If it does not, then some customers subsidise others, and the mismatch surfaces the
moment somebody with a PDF-heavy workload compares their bill to somebody with a text-heavy one.

## 2. Method

A synthetic corpus spanning a range of sizes per format (1 to 100 PDF pages, 3k to 1.5M DOCX
characters, 1k to 100k XLSX cells, 1 to 50 PPTX slides), run against the **real bundled engine** -
the same PyInstaller executable the worker invokes.

Per file: median of 5 runs after a discarded warm-up, measuring wall time, peak resident memory of
the engine process, and the size of the extracted model returned.

**Fixed and marginal cost are separated by linear regression, not by subtraction.** That matters,
and it was the second attempt:

> The first version measured engine startup separately with a health check and subtracted it.
> Startup is ~0.7-1.0 s and drifts between runs, so subtracting it from a cheap format's
> similar-magnitude total produced zero and negative "net" times. Fitting total time against
> credits recovers both numbers from the shape of the data instead - the slope is what one more
> credit costs, the intercept is what invoking the engine costs at all.

## 3. Results

Two independent runs, same machine (Windows 11, the development laptop):

| format | ms per credit (run 1) | ms per credit (run 2) | r² | fixed ms per file | KB per credit | peak MB |
|---|---|---|---|---|---|---|
| PPTX | -1.93 | 2.22 | 0.04 / 0.93 | ~1,200-1,500 | 0.78 | 50 |
| DOCX | **3.68** | **3.73** | 0.996 | ~1,050-1,180 | 3.04 | 55 |
| XLSX | **66.92** | **58.23** | 0.98-0.998 | ~1,140-1,430 | 10.97 | 92 |
| PDF | **140.56** | **134.07** | 1.000 | ~920-1,030 | 2.35 | **484** |

DOCX, XLSX and PDF fit cleanly and reproduce across runs. **PPTX does not fit at all** (r² 0.04 in
one run, and a negative slope): 50 slides cost no more than 1 slide within measurement error, so
its marginal cost is *below the noise floor* rather than known. Treat it as "≲ DOCX", not as a
number.

### The comparison this exists to make

Anchored on DOCX, the stable cheapest measurable format:

| one credit of | costs, relative to a DOCX credit |
|---|---|
| PPTX | ≲ 1 (below measurement floor) |
| DOCX | 1 |
| XLSX | **≈ 16-18x** |
| PDF | **≈ 36-38x** |

## 4. Findings

**1. The ratios are not cost-proportionate.** A PDF credit costs us roughly 37 times what a DOCX
credit costs. A customer converting PDFs is paying the same per credit as one converting Word
documents, for nearly forty times the work.

**2. The direction is consistent: text formats are overpriced, PDF and XLSX underpriced.** If the
intent is cost-proportional pricing, PDF pages and spreadsheet cells are the ones out of line.

**3. The one-credit-per-file minimum is justified by measurement.** Every format shows a fixed cost
of roughly **1 second of a worker slot per file**, before any content is read - dominated by engine
startup. A file that produces almost nothing still costs about a second. The minimum was a
judgement call when it was written; it now has a number behind it.

**4. Memory, not time, is likely the binding constraint for PDF.** A 100-page PDF peaks at **484
MB**, against 48-92 MB for everything else. Worker density - how many conversions fit on a machine -
will be set by PDF, and the 100 MB upload limit permits PDFs far larger than the 100-page fixture
measured here.

**5. PDF cost grows faster than linearly in memory.** 50 pages peaked at 264 MB, 100 pages at 484
MB. The per-page credit therefore *undercharges* large PDFs relative to small ones, on top of
undercharging PDFs generally.

**6. Storage tells a different story from time.** XLSX produces **10.97 KB of extracted model per
credit**, against 2.35 for PDF - roughly 4.7x. Whichever cost eventually dominates the unit
economics, the two axes disagree about which format is expensive, so a single ratio cannot be
correct for both.

## 5. What this does not settle

Cost-proportionality is one input to pricing, not the decision. Three reasons not to simply
rewrite the ratios to match these numbers:

- **Explainability was a deliberate goal.** SR-BIL-4 chose units a customer can verify against
  their own document. Equalising cost exactly would mean something like "1 credit per 120,000
  characters", which is defensible arithmetic and a worse thing to put on an invoice.
- **Value differs from cost.** A converted PDF page is plausibly worth more to a customer than
  3,000 characters of plain text. Whether price should track cost or value is a commercial
  decision, and not one to make inside a benchmark document.
- **These are relative, not absolute, numbers.** See below.

**A proposal, not a decision:** if cost-proportionality is chosen as the goal, the smallest change
that closes most of the gap is to reduce what a PDF page and a spreadsheet cell buy - not to
inflate the text allowances. Any such change **must** bump `ConversionCredits.PolicyVersion`, since
ledger entries record the version that priced them.

## 6. Limitations, stated plainly

- **Synthetic corpus.** Generated documents with repeated prose. Real documents - scanned PDFs,
  spreadsheets with formulas and merged cells, presentations with images - would cost more, and
  quite possibly move the ratios. The direction and rough magnitude are the trustworthy part; the
  exact multipliers are not.
- **One machine, Windows, developer laptop.** Production is intended to be Linux containers. These
  numbers set *relative* expectations between formats; absolute costs need re-measuring on
  production-like hardware before any price is derived from them.
- **Wall time is a proxy for cost.** It is the right proxy for a worker-slot-bound service, but it
  is not CPU time, and it does not price storage, egress, or the database.
- **PPTX marginal cost is unmeasured**, not measured-as-zero. It needs a corpus with far more
  slides, or slides carrying real content, to rise above the fixed cost.
- **TXT is absent entirely.** It has no Python extractor - `TextDocumentProcessor` reads the file
  in .NET, in process - so it never pays the engine's ~1 s startup and cannot be compared on this
  path. An earlier version of the harness *did* report TXT numbers, and they were the cost of
  producing an `unsupportedFile` error, because the harness accepted a `success: false` response as
  a measurement. That bug is fixed (the harness now raises on any unsuccessful response), and the
  numbers it produced are discarded. TXT needs a separate .NET-side measurement.
- **No concurrency.** Every measurement is a single engine process on an otherwise idle machine.
  Real worker contention for CPU, disk and memory is not modelled.
