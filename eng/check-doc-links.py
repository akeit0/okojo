"""Check relative Markdown links in docs/ and root policy files.

Usage: python3 eng/check-doc-links.py (run from repo root).
Exit code 0 when all links resolve, 1 otherwise.
Skips external URLs and pure in-page #anchors.
"""

import os
import re
import sys

LINK = re.compile(r"\]\(([^)\s]+?)(#[^)]*)?\)")

ROOT_FILES = [
    "README.md",
    "AGENTS.md",
    "TODO.md",
    "OKOJO_BROWSER_COMPATIBILITY_PLAN.md",
    "OKOJO_ECMA262_COMPLIANCE_PLAYBOOK.md",
    os.path.join(
        ".agents", "skills", "okojo-engine-development", "SKILL.md"
    ),
]


def collect_files():
    found = []
    for dirpath, _, names in os.walk("docs"):
        for name in names:
            if name.endswith(".md"):
                found.append(os.path.join(dirpath, name))
    return found + ROOT_FILES


def main():
    bad = []
    count = 0
    for path in collect_files():
        base = os.path.dirname(path) or "."
        text = open(path, encoding="utf-8").read()
        for match in LINK.finditer(text):
            url = match.group(1)
            if not url or url.startswith("#"):
                continue
            if re.match(r"https?://|mailto:|[a-zA-Z][a-zA-Z0-9+.-]*:", url):
                continue
            if url.startswith("/") or url.startswith("artifacts/"):
                continue
            count += 1
            target = os.path.normpath(os.path.join(base, url))
            if not os.path.exists(target):
                bad.append(f"{path} -> {url}")
    print(f"checked {count} links")
    if bad:
        print("BROKEN:")
        for line in bad:
            print("  " + line)
        return 1
    print("ALL OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
