
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
