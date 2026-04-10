# ECMA-262 Reader Tool

Local helper to collect ECMA-262 multipage specs and convert section-level markdown using existing converters (`node`/`bun`/`python`/`pandoc`).

## Setup

Recommended local setup for section extraction:

```powershell
cd .\tools\OkojoEcma262
npm install
```

This installs `turndown` under `tools/OkojoEcma262/node_modules`. The script calls the checked-in JS converter `tools/OkojoEcma262/ConvertHtmlToMarkdown.mjs`, and that converter resolves the local dependency automatically.

## Commands

```powershell
.\tools\OkojoEcma262\Read-Ecma262Spec.ps1 -h
.\tools\OkojoEcma262\Read-Ecma262Spec.ps1 --help
```

```powershell
.\tools\OkojoEcma262\Read-Ecma262Spec.ps1
```

List all top-level ECMA-262 multipage pages (`*.html` pages from `multipage/`).

```powershell
.\tools\OkojoEcma262\Read-Ecma262Spec.ps1 numbers-and-dates
```

List available section IDs for one page (no `.html` extension needed).

```powershell
.\tools\OkojoEcma262\Read-Ecma262Spec.ps1 numbers-and-dates#sec-number-constructor
```

Download and cache the page (if needed), create `sections.md` for that page if missing, generate section markdown, and print the markdown path.

`-PreferredConverter Remote` is only valid for full-page requests. Section requests (`#section`) require a local converter because the remote endpoint returns full-page markdown and ignores extracted snippets.

Nested section ids are split into nested directories.  
Example: `sec-date.prototype.gettime` -> `artifacts/ecma262/markdown/numbers-and-dates/sec-date/sec-prototype/sec-gettime.md`.

```powershell
.\tools\OkojoEcma262\Read-Ecma262Spec.ps1 -Catalog
```

Pre-populate index cache and per-page section inventories (writes files under `artifacts/ecma262/index` and `artifacts/ecma262/markdown/<page>/sections.md`).

## Output Layout

- `artifacts/ecma262/cache/<page>/<page>.html`
  - raw page cache
- `artifacts/ecma262/markdown/<page>/sections.md`
  - list of section IDs like `sec-number-constructor`
- `artifacts/ecma262/markdown/<page>/<section>.md`
  - nested sections are expanded by dot segments, for example:
    - `.../<page>/sec-date/sec-prototype/sec-gettime.md`
  - parent sections include a short `Child Sections` list at the top when immediate nested sections exist
  - markdown for one section request
- `artifacts/ecma262/index/ecma262-page-urls.txt`
  - collected multipage `*.html` URLs
- `artifacts/ecma262/index/ecma262-multipage-index.html`
  - raw index snapshot

## Legacy mode

```powershell
.\tools\OkojoEcma262\Read-Ecma262Spec.ps1 -Section sec-number-constructor
.\tools\OkojoEcma262\Read-Ecma262Spec.ps1 -SectionUrl "https://tc39.es/ecma262/multipage/numbers-and-dates.html#sec-number-constructor"
```

`-Section` still resolves against the index and shares the same conversion output.

## Notes

- No custom HTML parser is implemented in this tool.
- If an existing section markdown file is actually a stale full-page remote conversion, the script treats it as invalid and regenerates it.
- If no local converter is found and a section slice is requested, the script exits with a clear `cd .\tools\OkojoEcma262 && npm install` setup message instead of silently returning a full-page markdown file.
- Full-page conversion may still use remote fallback when `-PreferredConverter Remote` is used.

## Why this layout

- `cache/` and `markdown/` are separate to keep raw and readable artifacts isolated.
- Files are per-page folders so review can jump directly to a section directory.
