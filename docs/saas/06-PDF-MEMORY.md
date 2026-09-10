# PDF extraction memory: investigation and fix

**Investigated and fixed 2026-09-10.** Reproduce with `scripts/probe-pdf-memory.py`.

The cost benchmark showed a 100-page PDF peaking at **484 MB** against 48-92 MB for every other
format. This is what was behind it.

**Outcome: a one-line fix cut peak memory by 89% and processing time by 48%.**

---

## 1. First, a correction

The cost benchmark originally stated that PDF memory "grows faster than linearly" and cited 264 MB
at 50 pages against 484 MB at 100. **That was wrong**, and it was wrong because I compared the raw
peaks without subtracting the ~36 MB process baseline.

Net of baseline, the growth is *exactly* linear:

| pages | peak MB | net of 36 MB baseline | MB per page |
|---|---|---|---|
| 1 | 49 | 13 | 13.0 |
| 5 | 67 | 31 | 6.2 |
| 20 | 132 | 96 | 4.8 |
| 50 | 264 | 228 | 4.56 |
| 100 | 484 | 448 | 4.48 |

Least-squares fit: **4.39 MB per page, r² = 1.0000**. Doubling 50 → 100 pages multiplies net memory
by 1.965, not by more than 2. The apparent super-linearity was an artefact of leaving a fixed
overhead in the numerator.

The conclusion that PDF was the memory problem survived; the reason given for it did not.

## 2. What memory is actually proportional to

Not page count, and not file size. **Text.**

| pages | chars/page | total chars | file KB | peak MB |
|---|---|---|---|---|
| 50 | 0 | 0 | 8 | 0.2 |
| 500 | 0 | 0 | 81 | 2.4 |
| 1,000 | 0 | 0 | 164 | 4.8 |
| 50 | 400 | 20,000 | 24 | 40.2 |
| 50 | 900 | 45,000 | 26 | 89.0 |
| 50 | 1,800 | 90,000 | 29 | 178.5 |
| 100 | 1,800 | 180,000 | 58 | 357.2 |
| 200 | 1,800 | 360,000 | 117 | 715.4 |

A thousand empty pages cost 4.8 MB. Fifty pages of ordinary text cost 178 MB. The relationship is
**≈ 2 MB of memory per 1,000 characters of PDF text**, consistent to within 2% across every row
above.

**Why that was dangerous.** The upload limit constrains *bytes*, and PDF text compresses well.
A 117 KB file consumed 715 MB - **2,098× the 0.34 MB of text it actually extracted**. The 100 MB
limit permits files carrying vastly more text than any worker has memory for, so the limit that
looked like a safety bound was not one. This is the failure mode where a worker is killed by a file
the validator happily accepted.

## 3. Cause

`pdfplumber` caches each page's parsed objects - characters, lines, rects, curves - on the `Page`
after first access, and the `PDF` object keeps every page alive for its lifetime. The extractor
looped over `pdf.pages` and never released them, so by the last page the process held the fully
parsed object graph of the entire document, to produce a few hundred KB of text.

## 4. Fix

One line in `extractors/pdf_extractor.py`, after `_extract_page` has finished with the page:

```python
page.close()
```

It must come after `_extract_page`, which reads tables off the page; closing earlier would discard
data still needed.

### Measured effect, isolated (`scripts/probe-pdf-memory.py`)

| pages | held (before) | released (after) | saved | before | after |
|---|---|---|---|---|---|
| 50 | 178.6 MB | 7.0 MB | 96% | 3.51 s | 3.40 s |
| 200 | 715.1 MB | 8.2 MB | 99% | 13.74 s | 12.88 s |
| 500 | 1,787.6 MB | 11.3 MB | 99% | 35.86 s | 31.48 s |

### Measured effect, through the real bundled engine

| fixture | peak before | peak after |
|---|---|---|
| pdf-001p | 49 MB | 48.6 MB |
| pdf-020p | 132 MB | 49.7 MB |
| pdf-050p | 264 MB | 50.7 MB |
| pdf-100p | **484 MB** | **51.8 MB** |

Per-page memory growth fell from **4.39 MB/page to 0.03 MB/page** - a 137x reduction in slope.
Memory is now effectively flat in document size.

Processing time fell too: PDF went from **140.6 to ~73 ms per credit** (two confirming runs: 72.72,
73.51), a 48% reduction. Holding a large object graph was costing allocator and cache pressure, not
just resident memory.

## 5. Verification that nothing broke

- `scripts/benchmark-pdf-tables.py`: **54/72 cells, 4/6 exact shapes** - identical to the documented
  baseline and to PyMuPDF. No table fidelity regression.
- Full suite: **372 passed, 0 failed**, including the 45 integration tests that drive the real
  bundled engine against the sample corpus.

## 6. Effect on the credit ratios

The cost benchmark's headline changes, because PDF got substantially cheaper:

| one credit of | before the fix | after the fix |
|---|---|---|
| DOCX | 1 | 1 |
| XLSX | ~16-18x | ~15-19x |
| PDF | **~37x** | **~28x** |

Still not cost-proportionate, and the conclusion of the cost benchmark is unchanged - but the gap
is now materially smaller, and it closed by making the product faster rather than by charging more.

## 7. Limitations

- **Synthetic PDFs**, generated text. Scanned pages, embedded fonts and images have different
  profiles; a scanned PDF extracts almost no text and may behave very differently.
- **`page.close()` is pdfplumber's documented API** (0.11.10), but it is a cache flush. If a future
  change reads from a page after `_extract_page` returns, it will re-parse - correct, but slow. The
  comment at the call site says so.
- **No bound is enforced.** Memory is now flat in practice, but nothing *guarantees* it: a single
  pathological page could still be large. A hard cap on worker memory (SR-SEC-5, blocked on a
  container runtime) is still the thing that would make this safe rather than merely well-behaved.
- Measured on one Windows machine. The ratios should hold; absolute figures need re-measuring on
  production hardware.
