# Test262Runner

`tools/Test262Runner` runs focused or broad Test262 slices against Okojo, can emit progress reports, and now
isolates test execution in persistent child worker processes so per-test timeouts fail a case without killing the
whole run.

## Build

```powershell
dotnet build .\tools\Test262Runner\Test262Runner.csproj -c Release
```

## Common Runs

Full or broad run:

```powershell
dotnet run --project .\tools\Test262Runner\Test262Runner.csproj -c Release -- --category built-ins
```

Focused continuation:

```powershell
dotnet run --project .\tools\Test262Runner\Test262Runner.csproj -c Release -- --category built-ins --filter /Date/ --skip-passed
```

Timeout-focused sweep:

```powershell
dotnet run --project .\tools\Test262Runner\Test262Runner.csproj -c Release -- --filter test/staging --progress-doc --stop-on-long-test-seconds 8
```

Notes:

- each runner thread reuses one child worker process for multiple tests
- if a test exceeds the effective per-case timeout, only that worker is killed and restarted
- timeout failures are recorded as normal failed cases (`long-running test exceeded <n>s` or `Timeout after <ms> ms`)

## Progress Outputs

Enable progress outputs with:

```powershell
--progress-doc
--progress-json
```

Explicit paths are optional:

```powershell
--progress-doc my-report.md
--progress-json my-report.json
```

### Full-scope progress

A full-scope run writes:

- `TEST262_PROGRESS.md`
- `TEST262_PROGRESS.json`

Full scope means:

- root is `test262/test`
- no `--filter`
- no `--category`
- no `--feature`
- no `--max-tests`

These files are reserved for a real full-scope snapshot and are not overwritten by filtered runs.

### Partial progress history

Filtered or capped runs write timestamped current-scope snapshots under:

- `TEST262_PROGRESS_HISTORY/*.md`
- `TEST262_PROGRESS_HISTORY/*.json`

Examples:

- `--filter /Date/`
- `--category built-ins`
- `--feature Reflect`
- `--max-tests 50`

### Incremental full progress

Any progress-enabled run also updates:

- `TEST262_PROGRESS_INCREMENTAL.md`
- `TEST262_PROGRESS_INCREMENTAL.json`

This is the cumulative full-progress view. Partial runs merge only the tests they actually know about into the
incremental store. Unknown tests remain at their previous status.

The incremental markdown is intentionally compact:

- no filter/category/feature header block
- no selected-file run header
- grouped tables only

## Report Contents

The generated markdown report includes:

- test date
- scope kind
- scope label
- selected file count
- category summary
- folder summary
- feature summary
- skip reason summary
- latest update timestamp per category/folder/feature bucket

Status columns:

- `Passed`
- `Failed`
- `Skipped`
- `Not Yet`

## Notes

- `TEST262_PROGRESS.json` is for machine consumption.
- `TEST262_PROGRESS_INCREMENTAL.json` is the merge store for cumulative progress.
- Do not use the cache json files as human-readable workflow artifacts.

## Query Incremental Progress

Read failed tests, skipped tests, and grouped progress from the incremental store:

```powershell
dotnet run --project .\tools\Test262Runner\Test262Runner.csproj -c Release -- --query-incremental
```

Optional filters reuse the normal runner filters:

```powershell
dotnet run --project .\tools\Test262Runner\Test262Runner.csproj -c Release -- --query-incremental --category built-ins --filter /Date/
```

Status-only example:

```powershell
dotnet run --project .\tools\Test262Runner\Test262Runner.csproj -c Release -- --query-incremental --status failed --group-by folder
```

Reason/timestamp example:

```powershell
dotnet run --project .\tools\Test262Runner\Test262Runner.csproj -c Release -- --query-incremental --status skipped --reason "excluded feature" --updated-since 2026-03-11 --show-skipped
```

List/top example:

```powershell
dotnet run --project .\tools\Test262Runner\Test262Runner.csproj -c Release -- --query-incremental --group-by feature --top 10 --list skipped --show-skipped
```

Timeout failure example:

```powershell
dotnet run --project .\tools\Test262Runner\Test262Runner.csproj -c Release -- --query-incremental --status failed --reason "long-running test exceeded" --group-by none --list failed
```

Notes:

- default source is `TEST262_PROGRESS_INCREMENTAL.json`
- `--query-incremental <path>` can point to another incremental json file
- `--status` supports `failed`, `skipped`, `passed`, `not-yet`
- `--reason` filters failed and skipped entries by reason substring
- `--updated-since` filters entries by latest test update time
- `--group-by` supports `all`, `category`, `folder`, `feature`, `none`
- `--list` supports `failed`, `skipped`, `passed`, `not-yet`, `all`, `none`
- `--top` limits grouped table rows
- output includes:
    - `By Category`
    - `By Folder`
    - `By Feature`
    - status list selected by `--list`
- skip reason summary
- failure reason summary
- `--show-skipped` without `--list` keeps the old failed+skipped behavior
