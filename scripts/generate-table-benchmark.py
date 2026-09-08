"""Generates the PDF table-fidelity benchmark corpus (SaaS audit F-01 follow-up).

Run from the repo root:
    python scripts/generate-table-benchmark.py [output_dir]

Why this exists
---------------
samples/sample.pdf contains exactly one table: 3 rows x 2 columns, fully ruled.
That is the easiest case any extractor handles, so it cannot tell us whether a
PyMuPDF replacement preserves table fidelity. This corpus adds the shapes that
actually separate PDF table extractors from one another, each with ground truth
declared in code so a run can be scored automatically rather than eyeballed.

The fixtures are drawn with PyMuPDF, but that does not bias the comparison:
table detection reads ruling lines and text positions out of the finished page,
and has no visibility into which tool produced it.

Deliberately synthetic and non-confidential (CLAUDE.md Section 34).
"""

import os
import sys

import pymupdf

FONT = "helv"
FONT_SIZE = 10


def _draw_text(page, x, y, text, size=FONT_SIZE, fontname=FONT):
    page.insert_text((x, y), text, fontname=fontname, fontsize=size)


def _draw_ruled_table(page, x0, y0, rows, col_widths, row_height=20):
    """A conventional fully-ruled grid - every cell boundary has a visible line."""
    y = y0
    for row in rows:
        x = x0
        for value, width in zip(row, col_widths):
            page.draw_rect(pymupdf.Rect(x, y, x + width, y + row_height))
            _draw_text(page, x + 4, y + 14, str(value))
            x += width
        y += row_height


def _draw_borderless_table(page, x0, y0, rows, col_offsets, row_height=18):
    """No ruling lines at all - column structure exists only as consistent
    horizontal text positions. This is the single most common real-world table
    style that naive extractors miss entirely.
    """
    y = y0
    for row in rows:
        for value, dx in zip(row, col_offsets):
            _draw_text(page, x0 + dx, y, str(value))
        y += row_height


def _draw_header_ruled_table(page, x0, y0, rows, col_offsets, width, row_height=18):
    """Ruled under the header only - the common 'report' style. Enough of a hint
    for a line-based detector to find the top, but not the cell grid.
    """
    y = y0
    for index, row in enumerate(rows):
        for value, dx in zip(row, col_offsets):
            _draw_text(page, x0 + dx, y, str(value))
        if index == 0:
            page.draw_line(pymupdf.Point(x0, y + 4), pymupdf.Point(x0 + width, y + 4))
        y += row_height


# ---------------------------------------------------------------------------
# Fixtures. Each entry: (filename, builder, expected_tables, expected_grid)
# expected_grid is the exact cell content a perfect extractor should return.
# ---------------------------------------------------------------------------

RULED_GRID = [
    ["Branch", "Loans", "Members"],
    ["Dhaka", "1200", "340"],
    ["Chattogram", "980", "275"],
    ["Khulna", "640", "190"],
]

BORDERLESS_GRID = [
    ["Product", "Q1", "Q2"],
    ["Savings", "410", "455"],
    ["Credit", "220", "268"],
]

HEADER_RULED_GRID = [
    ["Code", "Description", "Amount"],
    ["A-100", "Opening balance", "5000"],
    ["A-200", "Disbursement", "1750"],
]

WIDE_GRID = [
    ["ID", "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Total"],
    ["R1", "10", "20", "30", "40", "50", "60", "210"],
    ["R2", "11", "21", "31", "41", "51", "61", "216"],
]

# Mixed Bengali/English matters for this product specifically (BR-007 declares
# English-only as a documented limitation, so we need to know how a replacement
# behaves rather than assume).
BENGALI_GRID = [
    ["Branch", "শাখা", "Total"],
    ["Dhaka", "ঢাকা", "1200"],
    ["Sylhet", "সিলেট", "870"],
]


def build_ruled(page):
    _draw_text(page, 72, 80, "Ruled Grid Table", size=14)
    _draw_ruled_table(page, 72, 100, RULED_GRID, [90, 70, 70])


def build_borderless(page):
    _draw_text(page, 72, 80, "Borderless Table", size=14)
    _draw_borderless_table(page, 72, 110, BORDERLESS_GRID, [0, 120, 200])


def build_header_ruled(page):
    _draw_text(page, 72, 80, "Header-Ruled Table", size=14)
    _draw_header_ruled_table(page, 72, 110, HEADER_RULED_GRID, [0, 70, 220], width=290)


def build_wide(page):
    _draw_text(page, 60, 80, "Wide Ruled Table", size=14)
    _draw_ruled_table(page, 60, 100, WIDE_GRID, [40, 40, 40, 40, 40, 40, 40, 50])


# Bengali needs a font that actually has the glyphs. PyMuPDF's built-in
# "china-s" does NOT - it silently substitutes CJK, producing a PDF whose text
# layer contains no Bengali codepoints at all. An earlier version of this
# fixture did exactly that, and made BOTH extractors appear to lose Bengali
# cells when in truth the Bengali had never been written to the page.
# Embedding a real Bengali-capable font is what makes this fixture measure
# extraction rather than font fallback.
BENGALI_FONT_CANDIDATES = [
    os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts", "kalpurush.ttf"),
    os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts", "Nirmala.ttc"),
    "/usr/share/fonts/truetype/noto/NotoSansBengali-Regular.ttf",
]


def _bengali_font_path():
    for candidate in BENGALI_FONT_CANDIDATES:
        if os.path.exists(candidate):
            return candidate
    return None


def build_bengali(page):
    _draw_text(page, 72, 80, "Mixed Script Table", size=14)

    font_path = _bengali_font_path()
    if font_path is None:
        raise RuntimeError(
            "No Bengali-capable font found. Refusing to generate t5, because a "
            "font-substituted fixture yields a misleading benchmark result rather "
            "than an obviously broken one. Install a Bengali font or extend "
            "BENGALI_FONT_CANDIDATES."
        )

    page.insert_font(fontname="bengali", fontfile=font_path)

    y = 100
    for row in BENGALI_GRID:
        x = 72
        for value in row:
            page.draw_rect(pymupdf.Rect(x, y, x + 100, y + 20))
            page.insert_text((x + 4, y + 14), value, fontname="bengali", fontsize=10)
            x += 100
        y += 20


def build_prose_and_table(page):
    """A table surrounded by prose - checks that body text is not swallowed into
    the table, and that the table is not missed among the paragraphs.
    """
    _draw_text(page, 72, 80, "Quarterly Summary", size=14)
    _draw_text(page, 72, 105, "The following table lists branch performance for the quarter.")
    _draw_ruled_table(page, 72, 125, RULED_GRID[:3], [90, 70, 70])
    _draw_text(page, 72, 210, "Figures are provisional and subject to audit review.")


FIXTURES = [
    ("t1-ruled-grid.pdf", build_ruled, 1, RULED_GRID),
    ("t2-borderless.pdf", build_borderless, 1, BORDERLESS_GRID),
    ("t3-header-ruled.pdf", build_header_ruled, 1, HEADER_RULED_GRID),
    ("t4-wide-ruled.pdf", build_wide, 1, WIDE_GRID),
    ("t5-bengali-ruled.pdf", build_bengali, 1, BENGALI_GRID),
    ("t6-prose-and-table.pdf", build_prose_and_table, 1, RULED_GRID[:3]),
]


def expected_grids():
    """Ground truth keyed by filename, for the scoring harness."""
    return {name: grid for name, _, _, grid in FIXTURES}


def expected_table_counts():
    return {name: count for name, _, count, _ in FIXTURES}


def generate(output_dir):
    os.makedirs(output_dir, exist_ok=True)
    for name, builder, _, _ in FIXTURES:
        document = pymupdf.open()
        page = document.new_page()
        builder(page)
        path = os.path.join(output_dir, name)
        document.save(path)
        document.close()
        print(f"wrote {path}")


if __name__ == "__main__":
    target = sys.argv[1] if len(sys.argv) > 1 else os.path.join("samples", "table-benchmark")
    generate(target)
