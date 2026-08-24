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
