"""Token estimation (FR-014-017) via tiktoken, per the strategy in
docs/adr/ADR-003-token-estimation-strategy.md:

- GPT-4o-style estimate: tiktoken's o200k_base encoding (exact for GPT-4o).
- Claude-style estimate: tiktoken's cl100k_base encoding, used as an
  approximation - Anthropic does not publish an offline tokenizer, and any
  exact Claude count would require a network call (disallowed, NFR-001/002).
  The .NET side is responsible for labeling this estimate as approximated.

IMPORTANT (offline requirement, NFR-001/002): by default, tiktoken does NOT
ship its vocabulary files in the pip package - it downloads them over HTTPS on
first use and caches them under %TEMP%/data-gym-cache. On a genuinely offline
target machine with no pre-existing cache, that first call would fail (or
hang) - a silent violation of the offline guarantee that would only surface
once a user actually tried the token-estimation feature.

The fix: the two vocabulary files this app needs are checked into
tiktoken_cache/ (fetched once, on a dev machine, via
`tiktoken.get_encoding(...)`) and bundled by PyInstaller alongside the
executable. TIKTOKEN_CACHE_DIR is pointed at that bundled folder before
tiktoken is ever asked to load an encoding, so it always reads the local file
and never attempts a network call, whether running as a frozen exe or as
interpreted source during development.
"""

import hashlib
import os
import sys


def _bundled_cache_dir():
    # sys._MEIPASS is set by PyInstaller (onedir or onefile) to the directory
    # containing bundled data files; falling back to this file's own
    # directory covers running `python dispatch.py` directly, unfrozen.
    base_dir = getattr(sys, "_MEIPASS", os.path.dirname(os.path.abspath(__file__)))
    return os.path.join(base_dir, "tiktoken_cache")


os.environ.setdefault("TIKTOKEN_CACHE_DIR", _bundled_cache_dir())

import tiktoken  # noqa: E402 - must follow the TIKTOKEN_CACHE_DIR setup above
import tiktoken.load  # noqa: E402 - same as above


def _read_bundled_cache_file(blobpath, expected_hash=None):
    """Replaces tiktoken's own read_file_cached (Phase 8 finding).

    ADR-001 launches many of these processes concurrently for a batch
    (NFR-011). tiktoken's default read_file_cached, on a hash mismatch,
    deletes the cache file and rewrites it (read.py: os.remove + tmp-file +
    os.rename) - under real concurrent launches this created a window where
    one process's rewrite raced another's read of the same bundled cache
    file, corrupting that read ("Error parsing line ..." from
    load_tiktoken_bpe). Since every vocabulary file this app needs is
    bundled and checked into tiktoken_cache/ ahead of time and never
    downloaded at runtime (NFR-001/002), there is no legitimate reason to
    ever verify/rewrite it here - a plain, read-only, single-process load
    removes the only path that could race.
    """
    cache_dir = os.environ["TIKTOKEN_CACHE_DIR"]
    cache_key = hashlib.sha1(blobpath.encode()).hexdigest()
    cache_path = os.path.join(cache_dir, cache_key)

    with open(cache_path, "rb") as f:
        return f.read()


tiktoken.load.read_file_cached = _read_bundled_cache_file

_gpt4o_encoding = None
_claude_proxy_encoding = None


def _gpt4o():
    global _gpt4o_encoding
    if _gpt4o_encoding is None:
        _gpt4o_encoding = tiktoken.get_encoding("o200k_base")
    return _gpt4o_encoding


def _claude_proxy():
    global _claude_proxy_encoding
    if _claude_proxy_encoding is None:
        _claude_proxy_encoding = tiktoken.get_encoding("cl100k_base")
    return _claude_proxy_encoding


def estimate(payload):
    original_text = payload.get("originalText") or ""
    converted_text = payload.get("convertedText") or ""

    gpt4o = _gpt4o()
    claude_proxy = _claude_proxy()

    return {
        "originalGpt4oStyle": len(gpt4o.encode(original_text)),
        "convertedGpt4oStyle": len(gpt4o.encode(converted_text)),
        "originalClaudeStyle": len(claude_proxy.encode(original_text)),
        "convertedClaudeStyle": len(claude_proxy.encode(converted_text)),
    }


def count_batch(payload):
    """FR-018/ADR-003: chunk sizing uses the exact GPT-4o-style (o200k_base)
    count, not an approximation - one batched call per document (encode_batch
    is tiktoken's own multi-threaded batch API) rather than one subprocess
    invocation per chunk-boundary decision, since each subprocess call costs
    real wall-clock time (ADR-001).
    """
    texts = payload.get("texts") or []
    gpt4o = _gpt4o()
    return {"counts": [len(tokens) for tokens in gpt4o.encode_batch(texts)]}
