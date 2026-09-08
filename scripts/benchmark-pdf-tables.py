"""Scores PDF table extraction fidelity: PyMuPDF (incumbent) vs pdfplumber
(proposed permissive replacement). SaaS audit F-01.

Run from the repo root, after generating the corpus:
    python scripts/generate-table-benchmark.py samples/table-benchmark
    python scripts/benchmark-pdf-tables.py samples/table-benchmark

Scoring is cell-exact against the ground truth declared in
generate-table-benchmark.py. Whitespace is normalized (a newline inside a
wrapped cell is not a fidelity difference); nothing else is forgiven, because a
wrong or missing cell value is exactly the failure this product must not ship.
"""

import os
import re
import sys

import pymupdf

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from importlib import import_module

benchmark = import_module("generate-table-benchmark".replace("-", "_")) if False else None

# The generator module name contains hyphens, so it cannot be imported normally.
import importlib.util

_spec = importlib.util.spec_from_file_location(
    "table_benchmark",
    os.path.join(os.path.dirname(os.path.abspath(__file__)), "generate-table-benchmark.py"),
)
table_benchmark = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(table_benchmark)


def normalize(cell):
    if cell is None:
        return ""
    return re.sub(r"\s+", " ", str(cell)).strip()


def normalize_grid(grid):
    return [[normalize(c) for c in row] for row in grid]


def extract_pymupdf(path):
    document = pymupdf.open(path)
    grids = []
    for page in document:
        for table in page.find_tables().tables:
            rows = table.extract()
            if rows:
                grids.append(normalize_grid(rows))
    document.close()
    return grids


def extract_pdfplumber(path):
    import pdfplumber

    grids = []
    with pdfplumber.open(path) as pdf:
        for page in pdf.pages:
            for rows in page.extract_tables():
                if rows:
                    grids.append(normalize_grid(rows))
    return grids


def score(expected, actual):
    """Cell-exact score for one table against ground truth.

    Returns (matched, total, shape_ok). Missing rows/columns count as misses
    rather than being skipped, so a truncated table cannot score well.
    """
    total = sum(len(row) for row in expected)
    if not actual:
        return 0, total, False

    matched = 0
    for r, exp_row in enumerate(expected):
        for c, exp_cell in enumerate(exp_row):
            if r < len(actual) and c < len(actual[r]) and actual[r][c] == exp_cell:
                matched += 1

    shape_ok = len(actual) == len(expected) and all(
        len(actual[i]) == len(expected[i]) for i in range(min(len(actual), len(expected)))
    )
    return matched, total, shape_ok


def run(corpus_dir):
    expected_grids = table_benchmark.expected_grids()

    engines = [("PyMuPDF (AGPL)", extract_pymupdf), ("pdfplumber (MIT)", extract_pdfplumber)]
    totals = {name: [0, 0, 0, 0] for name, _ in engines}  # matched, total, tables_found, shape_ok

    header = f"{'fixture':26} {'engine':20} {'tables':>7} {'cells':>12} {'shape':>7}"
    print(header)
    print("-" * len(header))

    for name in sorted(expected_grids):
        path = os.path.join(corpus_dir, name)
        if not os.path.exists(path):
            print(f"{name:26} MISSING - run generate-table-benchmark.py first")
            continue

        expected = normalize_grid(expected_grids[name])

        for engine_name, extractor in engines:
            try:
                grids = extractor(path)
            except Exception as ex:  # a crash is a real, reportable result
                print(f"{name:26} {engine_name:20} ERROR {type(ex).__name__}: {ex}")
                totals[engine_name][1] += sum(len(r) for r in expected)
                continue

            best = (0, sum(len(r) for r in expected), False)
            for grid in grids:
                candidate = score(expected, grid)
                if candidate[0] > best[0]:
                    best = candidate
            matched, total, shape_ok = best

            pct = (matched / total * 100) if total else 0
            print(
                f"{name:26} {engine_name:20} {len(grids):>7} "
                f"{matched:>4}/{total:<4} {pct:>3.0f}% {'OK' if shape_ok else 'DIFF':>7}"
            )

            totals[engine_name][0] += matched
            totals[engine_name][1] += total
            totals[engine_name][2] += len(grids)
            totals[engine_name][3] += 1 if shape_ok else 0

    print()
    print(f"{'TOTAL':26} {'engine':20} {'tables':>7} {'cells':>12} {'shapes ok':>10}")
    print("-" * len(header))
    for engine_name, _ in engines:
        matched, total, tables, shapes = totals[engine_name]
        pct = (matched / total * 100) if total else 0
        print(
            f"{'':26} {engine_name:20} {tables:>7} {matched:>4}/{total:<4} {pct:>3.0f}% "
            f"{shapes:>7}/{len(expected_grids)}"
        )


if __name__ == "__main__":
    corpus = sys.argv[1] if len(sys.argv) > 1 else os.path.join("samples", "table-benchmark")
    run(corpus)
