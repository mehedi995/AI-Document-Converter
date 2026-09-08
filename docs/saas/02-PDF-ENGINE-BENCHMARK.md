# PDF Engine Replacement — Table Fidelity Benchmark

**Date:** 2026-09-08
**Driver:** SaaS audit **F-01** — PyMuPDF 1.28.2 is dual-licensed AGPL-3.0 / Artifex commercial.
AGPL §13's network clause is triggered by serving PDF conversion over a network, and subprocess or
container separation does not discharge it.
**Decision taken by the owner:** replace with a permissively licensed library.
**Outcome:** replaced with **pdfplumber 0.11.10 (MIT)**. Measured table fidelity is **identical** to
PyMuPDF on the benchmark corpus; extraction is ~27% slower.

---

## 1. Candidate selection

The extractor's actual PyMuPDF surface is: `find_tables()` with per-table `bbox` and `extract()`;
`get_text("dict")` for text blocks with bbox and per-span font size, plus image blocks;
`needs_pass`; `FileDataError`; `metadata["author"]`; page count.

| Candidate | Licence | Table extraction | Verdict |
|---|---|---|---|
| **pdfplumber 0.11.10** | **MIT** | `find_tables()` / `extract_tables()`, bbox + cell text | **Selected** |
| pypdfium2 5.13.0 | BSD-3 / Apache-2.0 | **None** — verified: no table API of any kind | Rejected: would require writing table detection from scratch |
| pdfminer.six | MIT | Layout objects only, no table API | Rejected as a direct replacement (used *via* pdfplumber) |

### Licence chain actually verified

Read from installed package metadata, not from memory:

| Package | Version | Licence |
|---|---|---|
| pdfplumber | 0.11.10 | MIT |
| pdfminer.six | 20260107 | MIT |
| Pillow | 12.3.0 | MIT-CMU |
| pypdfium2 | 5.13.0 | BSD-3-Clause / Apache-2.0 |
| cryptography | 50.0.1 | Apache-2.0 OR BSD-3-Clause |

**No copyleft anywhere in the chain.** F-01 is resolved for the runtime.

---

## 2. Why a new corpus was needed

`samples/sample.pdf` contains exactly **one** table: 3 rows × 2 columns, fully ruled — the easiest
case any extractor handles. Certifying "table fidelity is preserved" from that would have been
meaningless.

`scripts/generate-table-benchmark.py` adds six fixtures covering the shapes that actually separate
PDF table extractors, each with ground truth declared in code so runs are scored automatically:

| Fixture | What it probes |
|---|---|
| `t1-ruled-grid` | Conventional fully-ruled grid (baseline) |
| `t2-borderless` | No ruling lines at all — column structure is text position only |
| `t3-header-ruled` | Rule under the header only (common report style) |
| `t4-wide-ruled` | 8 columns — column-splitting behaviour |
| `t5-bengali-ruled` | Mixed Bengali/English — directly relevant to this product |
| `t6-prose-and-table` | Table surrounded by prose — over/under-detection |

Fixtures are drawn with PyMuPDF. That does not bias the comparison: table detection reads ruling
lines and text positions out of the finished page and has no visibility into the producer.

### A fixture bug found and fixed during the run

The first `t5` build used PyMuPDF's built-in `china-s` font for Bengali. It has no Bengali coverage
and **silently substituted CJK glyphs** — the resulting PDF's text layer contained zero Bengali
codepoints (a stray `阉`, a Chinese character, gave it away). Both engines therefore appeared to
lose 3 Bengali cells each, when in truth the Bengali had never been written to the page.

The fixture now embeds a real Bengali font (`kalpurush.ttf` / `Nirmala.ttc`) and **refuses to
generate rather than silently produce a misleading fixture** if no such font is found. After the
fix, both engines recover Bengali at 9/9.

This is recorded because the first numbers were wrong, and a benchmark that can quietly measure its
own fixtures instead of the software under test is worth guarding against explicitly.

---

## 3. Results

### 3.1 Cell-exact fidelity, default (line-based) strategy

Scoring is cell-exact against declared ground truth. Whitespace is normalized; nothing else is
forgiven. Missing rows/columns count as misses, so a truncated table cannot score well.

| Fixture | PyMuPDF (AGPL) | pdfplumber (MIT) |
|---|---|---|
| t1-ruled-grid | 12/12 · 100% · shape OK | 12/12 · 100% · shape OK |
| t2-borderless | 0/9 · 0% · **not detected** | 0/9 · 0% · **not detected** |
| t3-header-ruled | 0/9 · 0% · **not detected** | 0/9 · 0% · **not detected** |
| t4-wide-ruled | 24/24 · 100% · shape OK | 24/24 · 100% · shape OK |
| t5-bengali-ruled | 9/9 · 100% · shape OK | 9/9 · 100% · shape OK |
| t6-prose-and-table | 9/9 · 100% · shape OK | 9/9 · 100% · shape OK |
| **TOTAL** | **54/72 · 75% · 4/6 shapes** | **54/72 · 75% · 4/6 shapes** |

**Identical, fixture by fixture.**

### 3.2 Text-based strategy

Both libraries expose a text-position strategy for unruled tables. Neither benefits:

| | PyMuPDF | pdfplumber |
|---|---|---|
| Total, text strategy | 14/72 · 19% | 14/72 · 19% |

It is substantially *worse* for both (19% vs 75%) because it fragments ruled tables it would
otherwise read perfectly. **The line-based default is correct and is what the extractor uses.**

### 3.3 Speed

60-page synthetic document (each page: heading, prose, one 4×3 ruled table), median of 3 runs,
full text + table extraction:

| Engine | Median | Per page | Rows extracted |
|---|---|---|---|
| PyMuPDF | 0.77 s | ~13 ms | 240 |
| pdfplumber | 0.98 s | ~16 ms | 240 |

**pdfplumber is ~27% slower**, extracting identical content. On this workload that is ~3 ms per
page. Judged an acceptable cost for removing an AGPL obligation, but it is a real cost and it
compounds on a metered service — it should be re-measured against realistic customer documents
before capacity planning or pricing is fixed.

### 3.4 Full API surface parity

| Capability | PyMuPDF | pdfplumber | Notes |
|---|---|---|---|
| Page count | `len(doc)` | `len(pdf.pages)` | parity |
| Tables + bbox | `find_tables()` | `find_tables()` | parity |
| Font size (heading heuristic) | span `size` | char/line `size` | parity — 20.0 pt heading detected by both |
| Image detection | block `type == 1` | `page.images` | parity — 1 image on `image-sample.pdf` in both |
| Author metadata | `metadata["author"]` | `metadata["Author"]` | key differs; handled |
| Corrupt file | `FileDataError` | `PdfminerException` | handled |
| **Password-protected** | **`needs_pass` flag** | **`PdfminerException` — same as corrupt** | **gap, see below** |

**The one genuine gap:** pdfplumber collapses "encrypted" and "corrupt" into a single exception type,
but BR-003 requires a password-protected file to get a clear "not supported" message rather than a
generic corruption error. Resolved by asking pdfminer directly *before* pdfplumber opens the file —
`PDFPasswordIncorrect` and a non-null `document.encryption` distinguish encryption, `PDFSyntaxError`
distinguishes corruption. Verified against `samples/password-protected-sample.pdf` (→
`unsupportedFile`) and a deliberately corrupted file (→ `corruptedDocument`).

---

## 4. Honest limitations

1. **The corpus is synthetic and simple.** The identical scores mean **this corpus does not
   discriminate between the two engines** — not that the engines are equivalent on real documents.
   Real-world PDFs (scanner output, multi-column journals, nested and spanning cells, rotated pages,
   CID-encoded fonts, generator quirks from Word/LaTeX/Crystal Reports) are where extractors
   genuinely diverge, and none of that is represented here.
2. **No real customer documents were used**, because none are available in this environment and
   synthetic non-confidential fixtures are what `CLAUDE.md` §34 requires.
3. **Neither engine handles unruled tables** (t2, t3) — 0/9 each. This is a pre-existing product
   limitation, not a regression introduced by the swap, but it is now measured rather than assumed
   and should be disclosed rather than advertised around.
4. **Speed was measured on one synthetic shape** on one machine. Not a capacity-planning number.
5. `t5` proves Bengali *text-layer* extraction from a born-digital PDF. It says nothing about
   scanned Bengali, which needs OCR and remains out of scope (BR-007).

## 5. Reproducing

```bash
python -m pip install -r src/AI.Document.Converter.Python/requirements-dev.txt
python scripts/generate-table-benchmark.py samples/table-benchmark
python scripts/benchmark-pdf-tables.py samples/table-benchmark
```

`requirements-dev.txt` keeps PyMuPDF available as a **development-only** comparison baseline and
fixture generator. It is excluded from the runtime and from the shipped bundle.

## 6. Verification that the shipped artifact is AGPL-free

The PyInstaller bundle was rebuilt with `--exclude-module pymupdf --exclude-module fitz`:

```
find . -iname "*mupdf*" -o -iname "*fitz*" -o -iname "*pymupdf*"   ->  0 matches
```

The rebuilt engine was then exercised directly: `sample.pdf` extracts correctly (3 pages, heading,
`Item`/`Value` table, `noExtractableText` warning on the blank page), the password-protected sample
returns `unsupportedFile`, and `health_check` succeeds.

**Full suite after the swap: 160 passed, 0 failed (116 unit + 44 integration) — with no test changes
required.**

## 7. Remaining work

- Add a `LICENSE` file and third-party notices (audit **F-03** — still open; the repository has none).
- Add a release check that fails if `pymupdf` is importable inside the shipped image.
- Re-measure against real customer documents before publishing any table-fidelity claim.
- `scripts/generate-samples.py` still imports pymupdf; it is dev-only and now covered by
  `requirements-dev.txt`, but it should eventually move to a permissive generator so the dev
  environment can drop the AGPL dependency entirely.
