"""Measures what a conversion credit actually costs to serve.

SR-BIL-4 fixed the credit as 1 PDF page = 1 PPTX slide = 3,000 DOCX/TXT
characters = 1,000 non-empty XLSX cells. Those baselines were chosen to be
deterministic and explainable, NOT because anybody had measured them. This
script measures them, so the ratios can be defended or corrected with numbers
rather than adjusted on a hunch.

What it measures, per file, against the REAL bundled engine:

  wall time      the thing a customer waits for and the thing that occupies a
                 worker slot - the dominant cost in a conversion service
  peak memory    what decides how many workers fit on a machine
  output bytes   the extracted model, a proxy for what gets stored and chunked

Each file is run several times and the MEDIAN is taken, because a single timing
on a developer machine measures whatever else the machine was doing.

Fixed and marginal cost are separated by LINEAR REGRESSION across sizes within
each format, not by subtracting a separately measured startup time. Subtraction
was the first approach and it failed: engine startup is ~0.7-1.0 s and drifts
between runs, so subtracting it from a cheap format's similar-magnitude total
produced zero and negative "net" times - noise presented as data. Fitting
total_ms against credits recovers both numbers from the shape of the data
instead: the slope is what one more credit costs, the intercept is what
invoking the engine costs at all.

Both matter, and they answer different questions: the intercept justifies the
one-credit-per-file minimum, the slope justifies the ratios between formats.

Run from the repo root, after scripts/generate-cost-corpus.py:
    python scripts/measure-conversion-cost.py

Requires psutil (development only - see requirements-dev.txt) and the built
engine in src/AI.Document.Converter.Python/dist/.
"""

import json
import os
import statistics
import subprocess
import sys
import threading
import time

import psutil

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CORPUS_DIR = os.path.join(REPO_ROOT, "artifacts", "cost-corpus")
RESULTS_PATH = os.path.join(REPO_ROOT, "artifacts", "cost-measurements.json")

ENGINE = os.path.join(
    REPO_ROOT, "src", "AI.Document.Converter.Python", "dist",
    "AIDocumentConverter.PythonEngine", "AIDocumentConverter.PythonEngine.exe",
)

REPEATS = 5

# Credit rules from SR-BIL-4, restated here rather than imported, so the
# measurement does not depend on the code it is measuring. If these drift apart
# that is itself worth knowing.
UNITS_PER_CREDIT = {"pdf": 1, "pptx": 1, "docx": 3_000, "xlsx": 1_000}

# TXT is deliberately absent. It has no Python extractor - TextDocumentProcessor
# reads the file in .NET, in process, so it never pays the engine's startup cost
# and cannot be measured on this path. Measuring it here produced a number, and
# the number was the cost of an error.
ENGINE_FORMATS = set(UNITS_PER_CREDIT)


def run_engine(request):
    """Runs one engine request, returning (seconds, peak_rss_bytes, response)."""
    payload = json.dumps(request) + "\n"

    started = time.perf_counter()
    process = subprocess.Popen(
        [ENGINE],
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
    )

    peak = 0
    stop = threading.Event()

    def sample_memory():
        nonlocal peak
        try:
            handle = psutil.Process(process.pid)
            while not stop.is_set():
                try:
                    peak = max(peak, handle.memory_info().rss)
                except (psutil.NoSuchProcess, psutil.AccessDenied):
                    return
                # Fine-grained enough to catch a short-lived peak without the
                # poller itself becoming a measurable load.
                time.sleep(0.005)
        except psutil.Error:
            return

    poller = threading.Thread(target=sample_memory, daemon=True)
    poller.start()

    stdout, stderr = process.communicate(payload, timeout=600)
    stop.set()
    poller.join(timeout=1)

    elapsed = time.perf_counter() - started

    if process.returncode != 0:
        raise RuntimeError(f"engine exited {process.returncode}: {stderr[:400]}")

    # The engine reports failures in its RESPONSE, with exit code 0. Without
    # this check the harness happily timed error responses and reported them as
    # extraction cost - which is how the first run of this script concluded
    # that plain text was almost free to process, when in fact TXT has no
    # Python extractor at all and every measurement was the cost of producing
    # "unsupportedFile". A benchmark that times failures is worse than no
    # benchmark, because it looks like data.
    try:
        parsed = json.loads(stdout)
    except json.JSONDecodeError as ex:
        raise RuntimeError(f"engine returned unparseable output: {ex}") from ex

    if not parsed.get("success"):
        raise RuntimeError(
            f"engine refused the request: {parsed.get('errorCategory')} "
            f"- {parsed.get('errorMessage')}"
        )

    return elapsed, peak, stdout


def least_squares(xs, ys):
    """Returns (slope, intercept, r_squared) for a simple linear fit."""
    n = len(xs)
    mean_x = sum(xs) / n
    mean_y = sum(ys) / n

    variance = sum((x - mean_x) ** 2 for x in xs)
    if variance == 0:
        return 0.0, mean_y, 0.0

    slope = sum((x - mean_x) * (y - mean_y) for x, y in zip(xs, ys)) / variance
    intercept = mean_y - slope * mean_x

    total = sum((y - mean_y) ** 2 for y in ys)
    residual = sum((y - (slope * x + intercept)) ** 2 for x, y in zip(xs, ys))
    r_squared = 1 - residual / total if total > 0 else 0.0

    return slope, intercept, r_squared


def report_fits(results):
    """Marginal cost per credit and fixed cost per file, per format."""
    print()
    header = f"{'format':8} {'points':>7} {'ms/credit':>11} {'fixed ms':>10} {'r2':>7} {'KB/credit':>11}"
    print(header)
    print("-" * len(header))

    fits = {}

    for file_format in sorted({r["format"] for r in results}):
        rows = [r for r in results if r["format"] == file_format]
        credits = [r["credits"] for r in rows]

        slope, intercept, r_squared = least_squares(credits, [r["totalMs"] for r in rows])
        storage_slope, _, _ = least_squares(credits, [r["outputBytes"] / 1024 for r in rows])

        fits[file_format] = {
            "points": len(rows),
            "msPerCredit": round(slope, 3),
            "fixedMs": round(intercept, 1),
            "rSquared": round(r_squared, 4),
            "kbPerCredit": round(storage_slope, 3),
            "peakMemoryMbMax": max(r["peakMemoryMb"] for r in rows),
        }

        print(
            f"{file_format:8} {len(rows):>7} {slope:>11.2f} {intercept:>10.0f} "
            f"{r_squared:>7.3f} {storage_slope:>11.2f}"
        )

    # The comparison the whole exercise exists to make. A credit is meant to
    # mean the same amount of work whatever the format; this says whether it
    # does.
    cheapest = min(f["msPerCredit"] for f in fits.values())

    if cheapest > 0:
        print()
        print("relative cost of one credit (cheapest format = 1.0):")
        for file_format, fit in sorted(fits.items(), key=lambda kv: kv[1]["msPerCredit"]):
            print(f"  {file_format:8} {fit['msPerCredit'] / cheapest:>8.1f}x")

    return fits


def median_of(runs):
    return statistics.median(runs)


def measure_startup():
    """The fixed cost of one engine invocation, with no document involved."""
    times, peaks = [], []

    for _ in range(REPEATS):
        elapsed, peak, _ = run_engine({"operation": "health_check", "payload": {}})
        times.append(elapsed)
        peaks.append(peak)

    return median_of(times), median_of(peaks)


def measure_file(file_name, file_format):
    path = os.path.join(CORPUS_DIR, file_name)
    request = {"operation": "extract", "payload": {"format": file_format, "filePath": path}}

    # Discarded: the first touch of a file pays for the OS page cache and any
    # one-off import the engine does, which is not what a steady-state server
    # experiences.
    run_engine(request)

    times, peaks, output = [], [], 0

    for _ in range(REPEATS):
        elapsed, peak, response = run_engine(request)
        times.append(elapsed)
        peaks.append(peak)
        output = len(response)

    return median_of(times), median_of(peaks), output


def main():
    if not os.path.exists(ENGINE):
        sys.exit(f"Engine not built at {ENGINE}. Run scripts/build-python-engine.ps1 first.")

    if not os.path.isdir(CORPUS_DIR):
        sys.exit(f"No corpus at {CORPUS_DIR}. Run scripts/generate-cost-corpus.py first.")

    print(f"engine   : {ENGINE}")
    print(f"repeats  : {REPEATS} (median reported)\n")

    startup_seconds, startup_peak = measure_startup()
    print(f"engine startup: {startup_seconds * 1000:.0f} ms, {startup_peak / 1024 / 1024:.0f} MB peak")
    print("(context only - the per-format fit below does not subtract it)\n")

    files = sorted(
        f for f in os.listdir(CORPUS_DIR)
        if not f.startswith(".") and f.split("-")[0] in ENGINE_FORMATS)
    results = []

    header = (
        f"{'file':20} {'units':>8} {'credits':>8} {'total ms':>9} "
        f"{'peak MB':>8} {'out KB':>8}"
    )
    print(header)
    print("-" * len(header))

    for file_name in files:
        file_format = file_name.split("-")[0]
        units = int("".join(c for c in file_name.split("-")[1] if c.isdigit()))

        if file_name.split("-")[1].endswith("k.docx") or file_name.split("-")[1].endswith("k.txt") \
                or file_name.split("-")[1].endswith("k.xlsx"):
            units *= 1_000

        seconds, peak, output_bytes = measure_file(file_name, file_format)
        credits = max(1, -(-units // UNITS_PER_CREDIT[file_format]))

        results.append({
            "file": file_name,
            "format": file_format,
            "units": units,
            "credits": credits,
            "totalMs": round(seconds * 1000, 1),
            "peakMemoryMb": round(peak / 1024 / 1024, 1),
            "outputBytes": output_bytes,
        })

        print(
            f"{file_name:20} {units:>8} {credits:>8} {seconds * 1000:>9.0f} "
            f"{peak / 1024 / 1024:>8.0f} {output_bytes / 1024:>8.0f}"
        )

    fits = report_fits(results)

    with open(RESULTS_PATH, "w", encoding="utf-8") as handle:
        json.dump(
            {
                "startupMs": round(startup_seconds * 1000, 1),
                "startupPeakMemoryMb": round(startup_peak / 1024 / 1024, 1),
                "repeats": REPEATS,
                "fits": fits,
                "results": results,
            },
            handle,
            indent=2,
        )

    print(f"\nWritten to {RESULTS_PATH}")


if __name__ == "__main__":
    main()
