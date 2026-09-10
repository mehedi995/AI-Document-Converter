"""Investigates what drives PDF extraction memory, and whether it can be bounded.

Motivated by the cost benchmark (docs/saas/05-CREDIT-COST-BENCHMARK.md): a
100-page PDF peaked at 484 MB against 48-92 MB for every other format. The
question this answers is not "how much" but "what is it proportional to, and
what stops it".

Every measurement runs in a FRESH SUBPROCESS. The first version of this script
ran them all in one process and produced nonsense: after a large run the heap
had already grown, so a later measurement of "RSS growth above baseline" read
zero for work that plainly allocated. Peak memory is only meaningful against a
clean process.

Run from the repo root:
    python scripts/probe-pdf-memory.py
"""

import gc
import json
import os
import shutil
import subprocess
import sys
import time

import psutil
import pymupdf

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROBE_DIR = os.path.join(REPO_ROOT, "artifacts", "pdf-memory-probe")


def make_pdf(path, pages, characters_per_page):
    """A PDF with a controlled page count and a controlled amount of text."""
    document = pymupdf.open()
    body = ("The quarterly review covers operating performance. " * 200)[:characters_per_page]

    for page_number in range(pages):
        page = document.new_page()
        if characters_per_page > 0:
            page.insert_textbox(
                pymupdf.Rect(50, 50, 545, 780), f"{page_number + 1}\n{body}", fontsize=9
            )

    document.save(path)
    document.close()


# Runs inside the child process: extract one PDF and report peak RSS.
CHILD = r"""
import gc, json, os, sys, time
import pdfplumber, psutil

path, release = sys.argv[1], sys.argv[2] == "1"
process = psutil.Process()
gc.collect()
baseline = process.memory_info().rss
peak = baseline
started = time.perf_counter()
text_bytes = 0

with pdfplumber.open(path) as pdf:
    for page in pdf.pages:
        # The same work extractors/pdf_extractor.py does per page.
        page.find_tables()
        lines = page.extract_text_lines()
        _ = page.images
        text_bytes += sum(len(line.get("text") or "") for line in lines)

        if release:
            page.close()

        peak = max(peak, process.memory_info().rss)

print(json.dumps({
    "peakMb": (peak - baseline) / 1024 / 1024,
    "seconds": time.perf_counter() - started,
    "textMb": text_bytes / 1024 / 1024,
}))
"""


def measure(path, release_pages):
    child = os.path.join(PROBE_DIR, "_child.py")
    with open(child, "w", encoding="utf-8") as handle:
        handle.write(CHILD)

    result = subprocess.run(
        [sys.executable, child, path, "1" if release_pages else "0"],
        capture_output=True,
        text=True,
        timeout=900,
    )

    if result.returncode != 0:
        raise RuntimeError(result.stderr[-500:])

    return json.loads(result.stdout)


def experiment_what_drives_it():
    print("1. What is memory proportional to?\n")
    print(f"{'pages':>7} {'chars/page':>11} {'total chars':>12} {'file KB':>9} {'peak MB':>9}")
    print("-" * 54)

    rows = []

    for pages, characters in [
        (50, 0), (500, 0), (1000, 0),          # many pages, no text
        (50, 1800), (100, 1800), (200, 1800),  # same text per page, more pages
        (50, 400), (50, 900),                  # same pages, less text
    ]:
        path = os.path.join(PROBE_DIR, f"p{pages}-c{characters}.pdf")
        make_pdf(path, pages, characters)

        outcome = measure(path, release_pages=False)
        rows.append((pages, characters, outcome["peakMb"]))

        print(
            f"{pages:>7} {characters:>11} {pages * characters:>12} "
            f"{os.path.getsize(path) / 1024:>9.0f} {outcome['peakMb']:>9.1f}"
        )

    return rows


def experiment_release_pages():
    print("\n2. Does releasing each page after use bound it?\n")
    print(f"{'pages':>7} {'held MB':>9} {'released MB':>13} {'saved':>8} {'held s':>8} {'rel s':>8}")
    print("-" * 60)

    for pages in [50, 200, 500]:
        path = os.path.join(PROBE_DIR, f"rel-{pages}.pdf")
        make_pdf(path, pages, 1800)

        held = measure(path, release_pages=False)
        released = measure(path, release_pages=True)

        saved = 1 - (released["peakMb"] / held["peakMb"]) if held["peakMb"] > 0 else 0

        print(
            f"{pages:>7} {held['peakMb']:>9.1f} {released['peakMb']:>13.1f} "
            f"{saved * 100:>7.0f}% {held['seconds']:>8.2f} {released['seconds']:>8.2f}"
        )


def experiment_retained_versus_produced():
    print("\n3. How much is retained per byte of text actually extracted?\n")

    path = os.path.join(PROBE_DIR, "ratio-200.pdf")
    make_pdf(path, 200, 1800)

    outcome = measure(path, release_pages=False)

    print(f"   text extracted    : {outcome['textMb']:.2f} MB")
    print(f"   peak memory       : {outcome['peakMb']:.1f} MB")
    print(f"   retained per byte : {outcome['peakMb'] / max(outcome['textMb'], 0.001):.0f}x")


def main():
    if os.path.exists(PROBE_DIR):
        shutil.rmtree(PROBE_DIR)
    os.makedirs(PROBE_DIR)

    experiment_what_drives_it()
    experiment_release_pages()
    experiment_retained_versus_produced()


if __name__ == "__main__":
    main()
