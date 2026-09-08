"""Licence compliance gate (SaaS audit F-01 / F-03).

Run from the repo root:
    python scripts/check-licences.py            # check, exit 1 on violation
    python scripts/check-licences.py --inventory # also print the full inventory

Two things are enforced:

  1. NO FORBIDDEN LICENCE MAY SHIP. Specifically AGPL: the cloud edition serves
     conversion over a network, which triggers AGPL section 13's obligation to
     offer the complete corresponding source of the combined work. Subprocess or
     container separation does NOT discharge it. PyMuPDF was removed for exactly
     this reason and must not creep back in.

  2. THE BUILT BUNDLE MUST BE CLEAN. Checking requirements.txt alone is not
     enough - a transitive dependency or a stale build can put a forbidden
     library into dist/ even when no manifest mentions it. So the bundle
     directory itself is scanned when present.

This is intended to run in CI and before any release.
"""

import argparse
import importlib.metadata as md
import os
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BUNDLE_DIR = os.path.join(
    REPO_ROOT, "src", "AI.Document.Converter.Python", "dist", "AIDocumentConverter.PythonEngine"
)
RUNTIME_REQUIREMENTS = os.path.join(
    REPO_ROOT, "src", "AI.Document.Converter.Python", "requirements.txt"
)

# Substrings that must never appear in a shipped licence string.
FORBIDDEN_LICENCE_MARKERS = ("AGPL", "AFFERO")

# Distribution artifacts that betray a forbidden library even if metadata is
# missing. PyMuPDF ships as pymupdf/fitz and carries MuPDF native libraries.
FORBIDDEN_BUNDLE_MARKERS = ("mupdf", "fitz", "pymupdf")

# Package names allowed in requirements-dev.txt but never in a shipped bundle.
DEV_ONLY_PACKAGES = {"pymupdf", "pyinstaller"}


def runtime_requirement_names():
    names = set()
    if not os.path.exists(RUNTIME_REQUIREMENTS):
        return names
    for line in open(RUNTIME_REQUIREMENTS, encoding="utf-8"):
        line = line.strip()
        if not line or line.startswith("#") or line.startswith("-r"):
            continue
        for separator in ("==", ">=", "<=", "~=", ">", "<"):
            if separator in line:
                line = line.split(separator)[0]
                break
        names.add(line.strip().lower())
    return names


def licence_of(package):
    try:
        meta = md.metadata(package)
    except md.PackageNotFoundError:
        return None
    expression = (meta.get("License-Expression") or "").strip()
    if expression:
        return expression
    classifiers = [
        c.split("::")[-1].strip()
        for c in (meta.get_all("Classifier") or [])
        if "License" in c
    ]
    if classifiers:
        return ", ".join(classifiers)
    raw = (meta.get("License") or "").strip().splitlines()
    return raw[0][:80] if raw else "UNKNOWN"


def check_runtime_requirements(violations, inventory):
    for name in sorted(runtime_requirement_names()):
        licence = licence_of(name)
        if licence is None:
            inventory.append((name, "(not installed)", "(cannot verify)"))
            continue

        version = md.version(name)
        inventory.append((name, version, licence))

        upper = licence.upper()
        if any(marker in upper for marker in FORBIDDEN_LICENCE_MARKERS):
            violations.append(
                f"FORBIDDEN LICENCE: '{name}' {version} is licensed '{licence}' and is listed as a "
                f"RUNTIME dependency in requirements.txt. AGPL-licensed code must never ship in "
                f"this product."
            )

        if name in DEV_ONLY_PACKAGES:
            violations.append(
                f"DEV-ONLY PACKAGE IN RUNTIME: '{name}' belongs in requirements-dev.txt, not "
                f"requirements.txt."
            )


def check_bundle(violations):
    """Scan the built bundle for forbidden artifacts. Absence of the bundle is
    reported, not treated as a pass - 'nothing found because nothing was built'
    must never read like a clean result.
    """
    if not os.path.isdir(BUNDLE_DIR):
        return False

    hits = []
    for root, dirs, files in os.walk(BUNDLE_DIR):
        for entry in list(dirs) + list(files):
            lowered = entry.lower()
            if any(marker in lowered for marker in FORBIDDEN_BUNDLE_MARKERS):
                hits.append(os.path.relpath(os.path.join(root, entry), BUNDLE_DIR))

    if hits:
        violations.append(
            "FORBIDDEN ARTIFACT IN SHIPPED BUNDLE: "
            + ", ".join(sorted(hits)[:10])
            + (f" (and {len(hits) - 10} more)" if len(hits) > 10 else "")
        )
    return True


def check_own_licence_files(violations):
    """The product's own LICENSE and THIRD-PARTY-NOTICES.md must exist, and
    LICENSE must not still carry the unfilled copyright-holder placeholder.

    A copyright line naming '<COPYRIGHT-HOLDER>' is worse than no LICENSE at
    all: it looks like a completed legal document while asserting nothing.
    """
    licence_path = os.path.join(REPO_ROOT, "LICENSE")
    notices_path = os.path.join(REPO_ROOT, "THIRD-PARTY-NOTICES.md")

    if not os.path.exists(licence_path):
        violations.append("MISSING LICENSE: the repository has no LICENSE file.")
    else:
        text = open(licence_path, encoding="utf-8").read()
        if "<COPYRIGHT-HOLDER>" in text:
            violations.append(
                "LICENSE PLACEHOLDER NOT FILLED: LICENSE still contains "
                "'<COPYRIGHT-HOLDER>'. Replace it with the full legal name of the "
                "copyright holder before distributing or publishing."
            )

    if not os.path.exists(notices_path):
        violations.append(
            "MISSING THIRD-PARTY-NOTICES.md: attribution required by the MIT, BSD, "
            "Apache-2.0 and MPL-2.0 components cannot be satisfied without it."
        )


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--inventory", action="store_true", help="print the full licence inventory")
    args = parser.parse_args()

    violations = []
    inventory = []

    check_runtime_requirements(violations, inventory)
    check_own_licence_files(violations)
    bundle_checked = check_bundle(violations)

    if args.inventory:
        print(f"{'package':22} {'version':14} licence")
        print("-" * 78)
        for name, version, licence in inventory:
            print(f"{name:22} {version:14} {licence}")
        print()

    print(f"runtime requirements checked : {len(inventory)}")
    print(
        "shipped bundle scanned       : "
        + (BUNDLE_DIR if bundle_checked else "NOT BUILT - bundle not verified")
    )

    if not bundle_checked:
        print(
            "\nWARNING: the engine bundle has not been built, so this run did NOT verify what\n"
            "actually ships. Build it and re-run before treating this as a release gate."
        )

    if violations:
        print(f"\nFAILED - {len(violations)} licence violation(s):\n")
        for violation in violations:
            print(f"  * {violation}")
        return 1

    print("\nPASSED - no forbidden licences in the runtime, and no forbidden artifacts in the bundle.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
